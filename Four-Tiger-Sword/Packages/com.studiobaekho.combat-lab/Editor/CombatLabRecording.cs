using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.Collections;
using UnityEditor.Media;
using UnityEngine;

namespace StudioBaekho.CombatLab
{
    /// <summary>Editor capture with explicit wall-clock timestamps, preserving global hitstop.</summary>
    public sealed class CombatLabRecording : IDisposable
    {
        public const int Width = 1280, Height = 720, FramesPerSecond = 60;
        readonly List<string> warnings = new List<string>();
        readonly Stopwatch clock = new Stopwatch();
        MediaEncoder encoder;
        CombatLabCaptureDriver driver;
        GameObject captureObject;
        CombatLabAudioTap audio;
        RenderTexture source, target;
        Texture2D pixels;
        bool savedBackground, stopping;
        double nextCaptureAt;
        long lastTimestamp = -1, audioFramesWritten;
        int audioChannels, sampleRate, receivedAudioBlocks;
        const int PrecisionCapacity = 48;
        const double PrecisionBeforeUs = 150000d, PrecisionAfterUs = 600000d;
        sealed class RawCapture
        {
            public byte[] bytes;
            public CaptureFrameMetadata frame;
        }
        readonly List<CaptureFrameMetadata> allFrames = new List<CaptureFrameMetadata>();
        readonly List<RawCapture> rawFrames = new List<RawCapture>(PrecisionCapacity);
        readonly Queue<RawCapture> freeFrames = new Queue<RawCapture>(PrecisionCapacity);
        bool precisionEnabled, precisionOverflow, impactMarked, captureFinalized;

        public string OutputPath { get; private set; }
        public string Error { get; private set; }
        public IReadOnlyList<string> Warnings => warnings;
        public bool IsRecording => encoder != null && !stopping;
        public bool IsReady => IsRecording;
        public int CapturedFrameCount { get; private set; }
        public float TimelineSeconds => (float)clock.Elapsed.TotalSeconds;
        public CaptureMetadata Capture { get; private set; } = new CaptureMetadata();

        public void Start(string absoluteVideoPathWithoutExtension, bool precision = false)
        {
            if (encoder != null) throw new InvalidOperationException("This recorder already has an active session.");
            if (!Application.isPlaying) throw new InvalidOperationException("Video recording requires Play Mode.");
            if (string.IsNullOrWhiteSpace(absoluteVideoPathWithoutExtension) || !Path.IsPathRooted(absoluteVideoPathWithoutExtension))
                throw new ArgumentException("Use an absolute video path without an extension.");
            var extension = Application.platform == RuntimePlatform.LinuxEditor ? ".webm" : ".mp4";
            OutputPath = Path.GetFullPath(absoluteVideoPathWithoutExtension) + extension;
            if (File.Exists(OutputPath)) throw new IOException("The recording already exists: " + OutputPath);
            string captureBase = Path.Combine(Path.GetDirectoryName(OutputPath), Path.GetFileNameWithoutExtension(OutputPath));
            if (File.Exists(captureBase + ".frames.json") || Directory.Exists(captureBase + ".frames"))
                throw new IOException("Capture metadata or images already exist: " + captureBase);
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            warnings.Clear();
            Error = null;
            CapturedFrameCount = receivedAudioBlocks = 0;
            audioFramesWritten = 0;
            lastTimestamp = -1;
            nextCaptureAt = 0;
            stopping = false;
            precisionEnabled = precision;
            precisionOverflow = impactMarked = captureFinalized = false;
            allFrames.Clear(); rawFrames.Clear(); freeFrames.Clear();
            Capture = new CaptureMetadata { precisionStatus = precision ? "missing-hit" : "disabled" };
            clock.Reset();
            savedBackground = Application.runInBackground;
            AddWarning("Video uses wall-clock timestamps, including global hitstop. Capture targets up to 60 fps at 1280x720; capture overhead can reduce rendered frame rate. This is not a performance benchmark.");
            AddWarning("Keep Game View visible during capture. Frame timestamps describe actual captured render samples; video seeking does not guarantee an exact encoded frame.");
            try
            {
                var attributes = new VideoTrackAttributes
                {
                    frameRate = new MediaRational(FramesPerSecond), width = Width, height = Height,
                    includeAlpha = false, bitRateMode = UnityEditor.VideoBitrateMode.High
                };
                AudioListener listener = null;
                foreach (var item in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                    if (item.isActiveAndEnabled) { listener = item; break; }
                audioChannels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 :
                    AudioSettings.speakerMode == AudioSpeakerMode.Stereo ? 2 : 0;
                sampleRate = AudioSettings.outputSampleRate;
                if (listener != null && audioChannels > 0 && sampleRate > 0)
                {
                    encoder = new MediaEncoder(OutputPath, attributes, new AudioTrackAttributes
                    { channelCount = (ushort)audioChannels, sampleRate = new MediaRational(sampleRate), language = "" });
                    audio = listener.gameObject.AddComponent<CombatLabAudioTap>();
                    audio.hideFlags = HideFlags.HideAndDontSave;
                    AddWarning("Audio is captured from the active AudioListener. Buffer latency makes audio/video synchronization approximate; check impact sound alignment before judging.");
                }
                else
                {
                    audioChannels = 0;
                    encoder = new MediaEncoder(OutputPath, attributes);
                    AddWarning("Silent video: an active AudioListener and mono/stereo speaker mode are required for audio capture.");
                }
                target = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32)
                { name = "Combat Lab Encoded Frame", hideFlags = HideFlags.HideAndDontSave };
                target.Create();
                pixels = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
                { name = "Combat Lab Readback", hideFlags = HideFlags.HideAndDontSave };
                if (precisionEnabled)
                {
                    // Allocate once before the first attack, not in the hit/capture loop.
                    for (int i = 0; i < PrecisionCapacity; i++)
                        freeFrames.Enqueue(new RawCapture { bytes = new byte[Width * Height * 4] });
                    AddWarning("Precision images retain up to 48 actual captures around the first confirmed hit (-150ms/+600ms). Missing render samples are not interpolated. PNG encoding happens after recording stops.");
                }
                Application.runInBackground = true;
                clock.Restart();
                if (audio != null) audio.Begin(clock, sampleRate, audioChannels);
                captureObject = new GameObject("Combat Lab Capture") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(captureObject);
                driver = captureObject.AddComponent<CombatLabCaptureDriver>();
                if (driver == null) throw new InvalidOperationException("Capture driver could not be attached. Check the Capture runtime assembly.");
                driver.FrameReady = CaptureFrame;
            }
            catch (Exception ex) { Error = ex.Message; Dispose(); throw; }
        }

        void CaptureFrame()
        {
            if (!IsRecording) return;
            double now = clock.Elapsed.TotalSeconds;
            if (now + .0001d < nextCaptureAt) return;
            nextCaptureAt = Math.Max(nextCaptureAt + 1d / FramesPerSecond, now);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var previous = RenderTexture.active;
            try
            {
                FlushAudio((long)(now * sampleRate));
                if (source == null || source.width != Screen.width || source.height != Screen.height)
                {
                    ReleaseTexture(ref source);
                    source = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32)
                    { name = "Combat Lab Game View", hideFlags = HideFlags.HideAndDontSave };
                    source.Create();
                }
                double capturedAt = clock.Elapsed.TotalSeconds;
                double unityRealtimeUs = Time.realtimeSinceStartupAsDouble * 1000000d;
                int unityFrame = Time.frameCount;
                ScreenCapture.CaptureScreenshotIntoRenderTexture(source);
                // Preserve aspect ratio without changing the user's Game View resolution.
                RenderTexture.active = target;
                GL.Clear(true, true, Color.black);
                float scale = Mathf.Min((float)Width / source.width, (float)Height / source.height);
                float w = source.width * scale, h = source.height * scale;
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0, Width, Height, 0);
                    var sourceRect = SystemInfo.graphicsUVStartsAtTop ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
                    Graphics.DrawTexture(new Rect((Width - w) * .5f, (Height - h) * .5f, w, h),
                        source, sourceRect, 0, 0, 0, 0);
                }
                finally { GL.PopMatrix(); }
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                pixels.Apply(false, false);
                long timestamp = CapturedFrameCount == 0 ? 0 : Math.Max(lastTimestamp + 1, ToTimestamp(capturedAt));
                if (!encoder.AddFrame(pixels, new MediaTime(timestamp, 1000000)))
                    throw new InvalidOperationException("Unity Media Encoder rejected a video frame.");
                lastTimestamp = timestamp;
                var frame = new CaptureFrameMetadata { captureIndex = CapturedFrameCount,
                    unityFrameCount = unityFrame, unityRealtimeUs = unityRealtimeUs,
                    captureElapsedUs = ToTimestamp(capturedAt), mediaPtsUs = timestamp, kind = "captured" };
                allFrames.Add(frame);
                RetainPrecisionFrame(frame);
                CapturedFrameCount++;
            }
            catch (Exception ex) { Error = ex.Message; Dispose(); }
            finally { RenderTexture.active = previous; }
        }

        static long ToTimestamp(double seconds) => (long)Math.Round(Math.Max(0d, seconds) * 1000000d);

        public void MarkImpact(CombatEvent impact)
        {
            if (impact == null || impact.kind != "confirmed-impact" || impactMarked || captureFinalized) return;
            if (double.IsNaN(impact.unityRealtimeUs) || double.IsInfinity(impact.unityRealtimeUs) || impact.unityRealtimeUs <= 0)
            {
                AddWarning("Confirmed hit has no valid Unity realtime timestamp; precise alignment is unavailable.");
                return;
            }
            impactMarked = true;
            Capture.hitEventId = string.IsNullOrWhiteSpace(impact.eventId)
                ? "impact-frame-" + impact.unityFrameCount : impact.eventId;
            Capture.hitRealtimeUs = impact.unityRealtimeUs;
            if (!precisionEnabled) return;
            // Retaining the full bounded rolling buffer until notification tolerates editor dispatch latency.
            // Only the actual requested window is persisted.
            for (int i = rawFrames.Count - 1; i >= 0; i--)
            {
                var item = rawFrames[i];
                if (InPrecisionWindow(item.frame.unityRealtimeUs)) continue;
                rawFrames.RemoveAt(i);
                item.frame = null;
                freeFrames.Enqueue(item);
            }
        }

        bool InPrecisionWindow(double realtimeUs)
            => realtimeUs >= Capture.hitRealtimeUs - PrecisionBeforeUs
                && realtimeUs <= Capture.hitRealtimeUs + PrecisionAfterUs;

        void RetainPrecisionFrame(CaptureFrameMetadata frame)
        {
            if (!precisionEnabled || (impactMarked && !InPrecisionWindow(frame.unityRealtimeUs))) return;
            RawCapture item;
            if (freeFrames.Count > 0) item = freeFrames.Dequeue();
            else if (!impactMarked)
            {
                item = rawFrames[0];
                rawFrames.RemoveAt(0);
            }
            else { precisionOverflow = true; return; }
            item.frame = frame;
            pixels.GetRawTextureData<byte>().CopyTo(item.bytes);
            rawFrames.Add(item);
        }

        void FinalizeCaptureMetadata()
        {
            if (captureFinalized || string.IsNullOrEmpty(OutputPath)) return;
            captureFinalized = true;
            Capture.capturedFrameCount = CapturedFrameCount;
            var intervals = new List<double>();
            Capture.maxIntervalUs = 0; Capture.gapsOver33333Us = 0;
            CaptureFrameMetadata previous = null;
            foreach (var frame in allFrames)
            {
                if (impactMarked) frame.relativeTimeSeconds = (float)((frame.unityRealtimeUs - Capture.hitRealtimeUs) / 1000000d);
                if (frame.kind != "captured") continue;
                if (previous != null)
                {
                    double interval = frame.unityRealtimeUs - previous.unityRealtimeUs;
                    intervals.Add(interval);
                    Capture.maxIntervalUs = Math.Max(Capture.maxIntervalUs, interval);
                    if (interval > 33333d) Capture.gapsOver33333Us++;
                }
                previous = frame;
            }
            intervals.Sort();
            Capture.medianIntervalUs = intervals.Count == 0 ? 0 : intervals.Count % 2 == 0
                ? (intervals[intervals.Count / 2 - 1] + intervals[intervals.Count / 2]) / 2d : intervals[intervals.Count / 2];
            Capture.frames.Clear();
            if (precisionEnabled && impactMarked)
            {
                bool startCovered = allFrames.Exists(frame => frame.kind == "captured"
                    && frame.unityRealtimeUs <= Capture.hitRealtimeUs - PrecisionBeforeUs);
                bool endCovered = allFrames.Exists(frame => frame.kind == "captured"
                    && frame.unityRealtimeUs >= Capture.hitRealtimeUs + PrecisionAfterUs);
                Capture.precisionStatus = !precisionOverflow && startCovered && endCovered && Error == null ? "complete" : "incomplete";
                string imageDirectory = Path.GetFileNameWithoutExtension(OutputPath) + ".frames";
                string absoluteImageDirectory = Path.Combine(Path.GetDirectoryName(OutputPath), imageDirectory);
                try
                {
                    Directory.CreateDirectory(absoluteImageDirectory);
                    rawFrames.Sort((a, b) => a.frame.captureIndex.CompareTo(b.frame.captureIndex));
                    foreach (var item in rawFrames)
                    {
                        if (!InPrecisionWindow(item.frame.unityRealtimeUs)) continue;
                        string imageName = item.frame.captureIndex.ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + ".png";
                        pixels.LoadRawTextureData(item.bytes);
                        pixels.Apply(false, false);
                        File.WriteAllBytes(Path.Combine(absoluteImageDirectory, imageName), pixels.EncodeToPNG());
                        item.frame.imagePath = imageDirectory + "/" + imageName;
                        Capture.frames.Add(item.frame);
                    }
                    int expectedImages = 0;
                    foreach (var frame in allFrames)
                        if (frame.kind == "captured" && InPrecisionWindow(frame.unityRealtimeUs)) expectedImages++;
                    if (Capture.frames.Count == 0 || Capture.frames.Count != expectedImages)
                    {
                        Capture.precisionStatus = "incomplete";
                        AddWarning("Some actual render samples in the hit window were not retained as precision images.");
                    }
                }
                catch (Exception exception)
                {
                    Capture.precisionStatus = "incomplete";
                    AddWarning("Precision image export incomplete: " + exception.Message);
                }
                if (precisionOverflow) AddWarning("Precision image buffer exceeded 48 samples. The saved window is incomplete.");
                if (!startCovered || !endCovered) AddWarning("Precision recording did not cover the complete -150ms/+600ms hit window.");
            }
            Capture.framesPath = Path.GetFileNameWithoutExtension(OutputPath) + ".frames.json";
            var sidecar = new CaptureMetadata { framesPath = Capture.framesPath, precisionStatus = Capture.precisionStatus,
                hitEventId = Capture.hitEventId, hitRealtimeUs = Capture.hitRealtimeUs, capturedFrameCount = Capture.capturedFrameCount,
                medianIntervalUs = Capture.medianIntervalUs, maxIntervalUs = Capture.maxIntervalUs,
                gapsOver33333Us = Capture.gapsOver33333Us, frames = allFrames };
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(OutputPath), Capture.framesPath), JsonUtility.ToJson(sidecar, true), new System.Text.UTF8Encoding(false)); }
            catch (Exception exception)
            {
                AddWarning("Capture frame metadata could not be saved: " + exception.Message);
                Capture.framesPath = null;
                if (precisionEnabled) Capture.precisionStatus = "incomplete";
            }
            rawFrames.Clear(); freeFrames.Clear();
        }

        void FlushAudio(long maximumFrame, bool final = false)
        {
            if (audio == null || encoder == null) return;
            while (audio.Blocks.TryPeek(out var pending))
            {
                if (!final && pending.firstFrame + pending.samples.Length / audioChannels > maximumFrame) break;
                if (!audio.Blocks.TryDequeue(out var block)) break;
                receivedAudioBlocks++;
                long start = Math.Max(0, block.firstFrame);
                WriteSilence(Math.Min(start, maximumFrame));
                int offsetFrames = (int)Math.Max(0L, audioFramesWritten - block.firstFrame);
                int frames = Math.Min(block.samples.Length / audioChannels - offsetFrames,
                    (int)Math.Max(0L, maximumFrame - audioFramesWritten));
                if (frames <= 0) continue;
                using (var buffer = new NativeArray<float>(frames * audioChannels, Allocator.Temp))
                {
                    NativeArray<float>.Copy(block.samples, offsetFrames * audioChannels, buffer, 0, buffer.Length);
                    if (!encoder.AddSamples(buffer)) throw new InvalidOperationException("Unity Media Encoder rejected audio samples.");
                }
                audioFramesWritten += frames;
            }
        }

        void WriteSilence(long targetFrame)
        {
            while (audioFramesWritten < targetFrame)
            {
                int frames = (int)Math.Min(4096L, targetFrame - audioFramesWritten);
                using (var silence = new NativeArray<float>(frames * audioChannels, Allocator.Temp))
                    if (!encoder.AddSamples(silence)) throw new InvalidOperationException("Unity Media Encoder rejected audio padding.");
                audioFramesWritten += frames;
            }
        }

        public void Stop()
        {
            if (encoder == null && captureObject == null && target == null && pixels == null && audio == null) return;
            stopping = true;
            double elapsed = clock.Elapsed.TotalSeconds;
            try
            {
                if (driver != null) driver.FrameReady = null;
                if (audio != null) audio.End();
                if (encoder != null)
                {
                    if (Error == null)
                    {
                        FlushAudio((long)(elapsed * sampleRate), true);
                        if (audioChannels > 0) WriteSilence((long)(elapsed * sampleRate));
                        long finalTimestamp = ToTimestamp(elapsed);
                        if (CapturedFrameCount > 0 && finalTimestamp > lastTimestamp)
                        {
                            if (!encoder.AddFrame(pixels, new MediaTime(finalTimestamp, 1000000)))
                                Error = "Unity Media Encoder rejected the final frame.";
                            else allFrames.Add(new CaptureFrameMetadata { captureIndex = CapturedFrameCount,
                                unityFrameCount = Time.frameCount, unityRealtimeUs = Time.realtimeSinceStartupAsDouble * 1000000d,
                                captureElapsedUs = ToTimestamp(elapsed), mediaPtsUs = finalTimestamp, kind = "terminalHold" });
                        }
                    }
                    encoder.Dispose();
                    encoder = null;
                }
                if (CapturedFrameCount == 0)
                    Error = Error ?? "No frames were captured. Keep Game View visible and use the interactive Editor.";
                else if (!File.Exists(OutputPath) || new FileInfo(OutputPath).Length == 0)
                    Error = Error ?? "Encoder stopped without a non-empty video file.";
                AddWarning("Captured " + CapturedFrameCount + " frames over " + elapsed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " real seconds (variable frame rate).");
                if (audioChannels > 0 && receivedAudioBlocks == 0)
                    AddWarning("No audio callback was received: the encoded audio track is silent. Check the active AudioListener and Editor audio settings.");
                else if (audio != null)
                {
                    float peak = audio.PeakAmplitude;
                    string peakDb = peak > 0f ? (20d * Math.Log10(peak)).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "-infinity";
                    AddWarning("Captured audio peak: " + peakDb + " dBFS from " + receivedAudioBlocks + " blocks.");
                    if (peak < .0001f)
                        AddWarning("Captured audio is silent or below -80 dBFS. Check assigned SFX, AudioListener and Editor mute state; this video cannot support sound comparison.");
                }
                if (audio != null && audio.DroppedBlocks > 0)
                    AddWarning("Audio capture dropped " + audio.DroppedBlocks + " blocks (queue overflow or channel changes); review audio quality.");
                FinalizeCaptureMetadata();
            }
            catch (Exception ex) { Error = ex.Message; throw; }
            finally
            {
                if (encoder != null)
                {
                    try { encoder.Dispose(); } catch (Exception ex) { Error = Error ?? ex.Message; }
                    encoder = null;
                }
                if (!captureFinalized)
                    try { FinalizeCaptureMetadata(); }
                    catch (Exception exception) { AddWarning("Capture metadata finalization failed: " + exception.Message); }
                if (captureObject != null) UnityEngine.Object.DestroyImmediate(captureObject);
                captureObject = null;
                driver = null;
                if (audio != null) { audio.End(); UnityEngine.Object.DestroyImmediate(audio); }
                audio = null;
                ReleaseTexture(ref source);
                ReleaseTexture(ref target);
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                pixels = null;
                rawFrames.Clear(); freeFrames.Clear();
                Application.runInBackground = savedBackground;
                clock.Stop();
                stopping = false;
            }
        }

        static void ReleaseTexture(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
        }

        void AddWarning(string warning) { if (!warnings.Contains(warning)) warnings.Add(warning); }
        public void Dispose() { try { Stop(); } catch (Exception ex) { Error = ex.Message; } }
    }
}
