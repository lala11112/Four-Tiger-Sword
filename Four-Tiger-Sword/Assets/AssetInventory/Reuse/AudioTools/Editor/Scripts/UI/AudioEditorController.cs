using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;

namespace AudioTool
{
    internal sealed class AudioEditorController : IDisposable
    {
        private readonly AudioEditSession _session;
        private readonly SemaphoreSlim _processingGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _loadCancellation;
        private int _processVersion;
        private IAudioSource _source;
        private AudioClip _sourceClip;
        private AudioClip _processedClip;
        private float[] _originalSamples;
        private float[] _displaySamples;
        private string _previewPath;
        private bool _disposed;

        internal event Action Changed;

        internal AudioEditorController(AudioEditSession session)
        {
            _session = session;
            PeakCache = new AudioWaveformPeakCache();
            _previewPath = Path.Combine(Application.temporaryCachePath, "AudioToolPreview-" + session.GetStableId() + ".wav");
        }

        internal AudioWaveformPeakCache PeakCache { get; }
        internal AudioClip SourceClip => _sourceClip;
        internal bool HasAudio => _sourceClip != null && _originalSamples != null;
        internal bool IsLoading { get; private set; }
        internal bool IsPlaying { get; private set; }
        internal string Status { get; private set; }
        internal string Error { get; private set; }
        internal float SourcePeak { get; private set; }
        internal float OutputPeak { get; private set; }
        internal int ClippedSampleCount { get; private set; }

        internal async Task InitializeAsync(IAudioSource source, string exportFolder, bool playWhenOpened)
        {
            if (source == null)
            {
                ClearSource();
                return;
            }

            CancelPendingWork();
            Stop();
            DestroyClip(_processedClip);
            _processedClip = null;
            _source = source;
            _session.ResetEdits();
            _session.FileName = source.FileName;
            _session.IsInProject = source.IsInProject;
            _session.ProjectPath = source.ProjectPath;
            _session.ExportFolder = ResolveExportFolder(source, exportFolder);
            IsLoading = true;
            Error = null;
            Status = "Loading audio file...";
            RaiseChanged();

            _loadCancellation = new CancellationTokenSource();
            CancellationToken token = _loadCancellation.Token;
            try
            {
                string path = await source.GetMaterializedPathAsync(token);
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    SetError("The audio source is no longer available. Choose the file again or retry the source operation.");
                    return;
                }

                _session.MaterializedPath = path;
                Status = "Decoding audio...";
                RaiseChanged();
                AudioClip clip = await AudioManager.LoadAudioFromFile(path, false);
                token.ThrowIfCancellationRequested();
                if (clip == null || !clip.LoadAudioData())
                {
                    SetError("Unity could not decode this audio file. Try WAV, MP3, OGG, AIFF, AIF, or a Unity-supported FLAC file.");
                    return;
                }

                float[] samples = new float[clip.samples * clip.channels];
                if (!clip.GetData(samples, 0))
                {
                    DestroyClip(clip);
                    SetError("Unity decoded the file but could not read its audio samples.");
                    return;
                }

                DestroyClip(_sourceClip);
                _sourceClip = clip;
                _originalSamples = samples;
                _displaySamples = new float[samples.Length];
                Array.Copy(samples, _displaySamples, samples.Length);
                int channels = clip.channels;
                SourcePeak = GetPeak(samples, out _);
                OutputPeak = SourcePeak;
                ClippedSampleCount = CountClipped(samples);
                Status = "Building waveform...";
                RaiseChanged();
                await Task.Run(() => PeakCache.Build(samples, channels), token);
                token.ThrowIfCancellationRequested();
                IsLoading = false;
                Status = "Ready";
                RaiseChanged();

                if (playWhenOpened) Play();
            }
            catch (OperationCanceledException)
            {
                if (!_disposed)
                {
                    IsLoading = false;
                    Status = HasAudio ? "Ready" : null;
                    RaiseChanged();
                }
            }
            catch (Exception exception)
            {
                SetError("Audio loading failed: " + exception.Message);
                Debug.LogError("Audio Tools load failed: " + exception);
            }
        }

        internal async Task RestoreAsync(bool playWhenOpened = false)
        {
            if (string.IsNullOrEmpty(_session.MaterializedPath) || !File.Exists(_session.MaterializedPath))
            {
                SetError("The previous source could not be restored. Choose the audio file again.");
                return;
            }

            AudioEditSession.EditSnapshot editSnapshot = _session.CaptureEdits();
            string materializedPath = _session.MaterializedPath;
            string exportFolder = _session.ExportFolder;
            await InitializeAsync(new FileAudioSource(materializedPath), exportFolder, playWhenOpened);
            if (HasAudio)
            {
                _session.RestoreEdits(editSnapshot);
                await ApplyEffectsNowAsync();
            }
        }

        internal void ClearSource()
        {
            CancelPendingWork();
            Stop();
            DestroyClip(_sourceClip);
            DestroyClip(_processedClip);
            _sourceClip = null;
            _processedClip = null;
            _originalSamples = null;
            _displaySamples = null;
            IsLoading = false;
            Error = null;
            Status = null;
            SourcePeak = 0f;
            OutputPeak = 0f;
            ClippedSampleCount = 0;
            RaiseChanged();
        }

        internal async void ScheduleProcessing()
        {
            int version = ++_processVersion;
            try
            {
                await Task.Delay(150);
                if (_disposed || version != _processVersion) return;
                await ApplyEffectsAsync(version);
            }
            catch (Exception exception)
            {
                Error = "Preview processing failed: " + exception.Message;
                Debug.LogError("Audio Tools preview processing failed: " + exception);
                RaiseChanged();
            }
        }

        internal async Task ApplyEffectsNowAsync()
        {
            int version = ++_processVersion;
            await ApplyEffectsAsync(version);
        }

        private async Task ApplyEffectsAsync(int version)
        {
            if (!HasAudio || version != _processVersion) return;
            await _processingGate.WaitAsync();
            try
            {
                if (_disposed || !HasAudio || version != _processVersion) return;
                AudioProcessingSnapshot snapshot = CreateSnapshot();
                int channels = _sourceClip.channels;
                int frequency = _sourceClip.frequency;
                Status = "Updating preview...";
                RaiseChanged();
                AudioProcessingResult result = await Task.Run(() => Process(_originalSamples, channels, snapshot));
                if (_disposed || version != _processVersion) return;

                _displaySamples = result.Samples;
                OutputPeak = result.Peak;
                ClippedSampleCount = result.ClippedSampleCount;
                await Task.Run(() => PeakCache.Build(result.Samples, channels));
                if (_disposed || version != _processVersion) return;

                DestroyClip(_processedClip);
                _processedClip = null;
                if (snapshot.HasEffects)
                {
                    _processedClip = await AudioProcessor.CreateClipFromSamples(result.Samples, channels, frequency, "AudioToolsPreview", _previewPath);
                    if (_disposed || version != _processVersion)
                    {
                        DestroyClip(_processedClip);
                        _processedClip = null;
                        return;
                    }
                }

                Status = "Ready";
                Error = null;
                if (IsPlaying)
                {
                    Stop();
                    Play();
                }
                RaiseChanged();
            }
            finally
            {
                _processingGate.Release();
            }
        }

        internal void TogglePlayback()
        {
            if (IsPlaying) Stop();
            else Play();
        }

        internal void Play()
        {
            AudioClip clip = _processedClip != null ? _processedClip : _sourceClip;
            if (clip == null) return;

            IsPlaying = true;
            if (_session.PlaySelection && _session.HasSelection)
            {
                int start = Mathf.RoundToInt(_session.SelectionStart * clip.samples);
                int end = Mathf.RoundToInt(_session.SelectionEnd * clip.samples);
                AudioManager.PlayClipRange(clip, start, end, _session.Loop);
            }
            else
            {
                int start = Mathf.RoundToInt(_session.Playhead * clip.samples);
                AudioManager.PlayClip(clip, start, _session.Loop);
            }
            RaiseChanged();
        }

        internal void Stop()
        {
            if (IsPlaying)
            {
                float position = GetNormalizedPlaybackPosition();
                if (position >= 0f) _session.Playhead = position;
            }
            IsPlaying = false;
            AudioManager.StopAudio();
            RaiseChanged();
        }

        internal void Seek(float normalized)
        {
            _session.Playhead = normalized;
            if (IsPlaying)
            {
                Stop();
                Play();
            }
            RaiseChanged();
        }

        internal void TickPlayback()
        {
            if (!IsPlaying) return;

            float position = GetNormalizedPlaybackPosition();
            if (position >= 0f) _session.Playhead = position;
#if !AUDIO_TOOL_NOAUDIO
            if (_session.PlaySelection && _session.HasSelection && AudioManager.IsRangePlaying && AudioManager.HasReachedRangeEnd())
            {
                if (_session.Loop) Play();
                else Stop();
            }
            else if (!AudioManager.IsPlaying())
            {
                if (_session.Loop) Play();
                else IsPlaying = false;
            }
#endif
            RaiseChanged();
        }

        internal bool SelectAudibleContent(out string message)
        {
            message = null;
            if (!HasAudio) return false;

            int rangeStart = _session.HasSelection ? Mathf.RoundToInt(_session.SelectionStart * _sourceClip.samples) : 0;
            int rangeEnd = _session.HasSelection ? Mathf.RoundToInt(_session.SelectionEnd * _sourceClip.samples) : _sourceClip.samples;
            float[] range = AudioProcessor.TrimAudio(_originalSamples, _sourceClip.channels, rangeStart, rangeEnd);
            float peak = GetPeak(range, out _);
            if (peak <= _session.SilenceThreshold)
            {
                message = "No audible samples were found at this threshold. Lower the silence threshold and try again.";
                return false;
            }

            (int localStart, int localEnd) = AudioProcessor.DetectSilence(range, _sourceClip.channels, _session.SilenceThreshold);
            _session.HasSelection = true;
            _session.SelectionStart = (float)(rangeStart + localStart) / _sourceClip.samples;
            _session.SelectionEnd = (float)(rangeStart + localEnd) / _sourceClip.samples;
            _session.Playhead = _session.SelectionStart;
            _session.NormalizeSelection(1f / _sourceClip.samples);
            RaiseChanged();
            return true;
        }

        internal string GetDefaultOutputPath(bool replaceOriginal)
        {
            if (replaceOriginal && _session.IsInProject && !string.IsNullOrEmpty(_session.ProjectPath))
            {
                return Path.Combine(Path.GetDirectoryName(Application.dataPath), _session.ProjectPath);
            }

            if (string.IsNullOrEmpty(_session.ExportFolder)) return null;
            return AudioProcessor.GenerateUniqueFilename(_session.ExportFolder, _session.FileName ?? "audio.wav");
        }

        internal string Export(string outputPath)
        {
            if (!HasAudio || _displaySamples == null) throw new InvalidOperationException("No audio is loaded.");
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("Choose an output path first.", nameof(outputPath));

            float startNormalized = _session.HasSelection ? _session.SelectionStart : 0f;
            float endNormalized = _session.HasSelection ? _session.SelectionEnd : 1f;
            int start = Mathf.RoundToInt(startNormalized * _sourceClip.samples);
            int end = Mathf.RoundToInt(endNormalized * _sourceClip.samples);
            float[] output = AudioProcessor.TrimAudio(_displaySamples, _sourceClip.channels, start, end);
            if (output == null || output.Length == 0) throw new InvalidOperationException("The selected region contains no audio samples.");

            if (!outputPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) outputPath = Path.ChangeExtension(outputPath, ".wav");
            AudioProcessor.ExportToWav(outputPath, output, _sourceClip.channels, _sourceClip.frequency);
            AssetDatabase.Refresh();
            PingOutput(outputPath);
            Status = "Saved " + Path.GetFileName(outputPath);
            RaiseChanged();
            return outputPath;
        }

        internal float GetSelectionDuration()
        {
            if (_sourceClip == null) return 0f;
            return (_session.HasSelection ? _session.SelectionEnd - _session.SelectionStart : 1f) * _sourceClip.length;
        }

        internal float GetNormalizedPlaybackPosition()
        {
            AudioClip clip = _processedClip != null ? _processedClip : _sourceClip;
            if (clip == null || !AudioManager.IsPlaying()) return -1f;
            return Mathf.Clamp01(AudioManager.GetCurrentPosition() / Mathf.Max(0.0001f, clip.length));
        }

        private AudioProcessingSnapshot CreateSnapshot()
        {
            int curveSamples = 512;
            float[] fadeIn = new float[curveSamples];
            float[] fadeOut = new float[curveSamples];
            for (int i = 0; i < curveSamples; i++)
            {
                float t = (float)i / (curveSamples - 1);
                fadeIn[i] = _session.FadeInCurve.Evaluate(t);
                fadeOut[i] = _session.FadeOutCurve.Evaluate(t);
            }

            return new AudioProcessingSnapshot
            {
                HasSelection = _session.HasSelection,
                SelectionStart = _session.SelectionStart,
                SelectionEnd = _session.SelectionEnd,
                Normalize = _session.Normalize,
                NormalizeTarget = _session.NormalizeTarget,
                FadeIn = _session.FadeIn,
                FadeInSamples = Mathf.RoundToInt(_session.FadeInDuration * _sourceClip.frequency),
                FadeInCurve = fadeIn,
                FadeOut = _session.FadeOut,
                FadeOutSamples = Mathf.RoundToInt(_session.FadeOutDuration * _sourceClip.frequency),
                FadeOutCurve = fadeOut,
                AdjustVolume = _session.AdjustVolume,
                Volume = _session.Volume
            };
        }

        private static AudioProcessingResult Process(float[] original, int channels, AudioProcessingSnapshot settings)
        {
            float[] result = new float[original.Length];
            Array.Copy(original, result, original.Length);
            int totalFrames = original.Length / channels;
            int startFrame = settings.HasSelection ? ClampToInt((int)Math.Round(settings.SelectionStart * totalFrames), 0, totalFrames) : 0;
            int endFrame = settings.HasSelection ? ClampToInt((int)Math.Round(settings.SelectionEnd * totalFrames), startFrame, totalFrames) : totalFrames;
            int frameCount = Math.Max(0, endFrame - startFrame);
            float[] working = new float[frameCount * channels];
            Array.Copy(original, startFrame * channels, working, 0, working.Length);

            if (settings.Normalize && working.Length > 0)
            {
                float peak = 0f;
                for (int i = 0; i < working.Length; i++) peak = Math.Max(peak, Math.Abs(working[i]));
                if (peak > 0f)
                {
                    float factor = settings.NormalizeTarget / peak;
                    for (int i = 0; i < working.Length; i++) working[i] *= factor;
                }
            }

            if (settings.FadeIn) ApplyFade(working, channels, settings.FadeInSamples, true, settings.FadeInCurve);
            if (settings.FadeOut) ApplyFade(working, channels, settings.FadeOutSamples, false, settings.FadeOutCurve);
            if (settings.AdjustVolume)
            {
                for (int i = 0; i < working.Length; i++) working[i] = Clamp(working[i] * settings.Volume, -1f, 1f);
            }

            Array.Copy(working, 0, result, startFrame * channels, working.Length);
            float outputPeak = GetPeak(result, out int clipped);
            return new AudioProcessingResult(result, outputPeak, clipped);
        }

        private static void ApplyFade(float[] samples, int channels, int requestedFrames, bool fadeIn, float[] curve)
        {
            int totalFrames = samples.Length / channels;
            int frames = Math.Min(Math.Max(0, requestedFrames), totalFrames);
            for (int i = 0; i < frames; i++)
            {
                float t = frames <= 1 ? 1f : (float)i / (frames - 1);
                float curveValue = SampleCurve(curve, t);
                int frame = fadeIn ? i : totalFrames - 1 - i;
                float multiplier = fadeIn ? curveValue : SampleCurve(curve, 1f - t);
                for (int channel = 0; channel < channels; channel++) samples[frame * channels + channel] *= multiplier;
            }
        }

        private static float SampleCurve(float[] values, float t)
        {
            if (values == null || values.Length == 0) return t;
            float position = Clamp(t, 0f, 1f) * (values.Length - 1);
            int first = ClampToInt((int)Math.Floor(position), 0, values.Length - 1);
            int second = Math.Min(first + 1, values.Length - 1);
            float amount = position - first;
            return values[first] + (values[second] - values[first]) * amount;
        }

        private static float GetPeak(float[] samples, out int clipped)
        {
            float peak = 0f;
            clipped = 0;
            if (samples == null) return 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float value = Math.Abs(samples[i]);
                if (value > peak) peak = value;
                if (value >= 0.999f) clipped++;
            }
            return peak;
        }

        private static int CountClipped(float[] samples)
        {
            GetPeak(samples, out int clipped);
            return clipped;
        }

        private static int ClampToInt(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static string ResolveExportFolder(IAudioSource source, string exportFolder)
        {
            if (!string.IsNullOrEmpty(exportFolder)) return exportFolder;
            if (source.IsInProject && !string.IsNullOrEmpty(source.ProjectPath))
            {
                string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), source.ProjectPath);
                return Path.GetDirectoryName(fullPath);
            }
            if (source is FileAudioSource fileSource) return Path.GetDirectoryName(fileSource.FullPath);
            return null;
        }

        private static void PingOutput(string outputPath)
        {
            string normalizedOutput = outputPath.Replace('\\', '/');
            string normalizedDataPath = Application.dataPath.Replace('\\', '/');
            if (!normalizedOutput.StartsWith(normalizedDataPath, StringComparison.OrdinalIgnoreCase)) return;

            string projectPath = "Assets" + normalizedOutput.Substring(normalizedDataPath.Length);
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(projectPath);
            if (asset == null) return;
            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }

        private void CancelPendingWork()
        {
            _processVersion++;
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;
        }

        private void SetError(string message)
        {
            IsLoading = false;
            Error = message;
            Status = null;
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            Changed?.Invoke();
        }

        private static void DestroyClip(AudioClip clip)
        {
            if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CancelPendingWork();
            Stop();
            DestroyClip(_processedClip);
            DestroyClip(_sourceClip);
            _processedClip = null;
            _sourceClip = null;
            if (!string.IsNullOrEmpty(_previewPath) && File.Exists(_previewPath))
            {
                try { File.Delete(_previewPath); }
                catch (IOException) { }
            }
        }

        private sealed class AudioProcessingSnapshot
        {
            internal bool HasSelection;
            internal float SelectionStart;
            internal float SelectionEnd;
            internal bool Normalize;
            internal float NormalizeTarget;
            internal bool FadeIn;
            internal int FadeInSamples;
            internal float[] FadeInCurve;
            internal bool FadeOut;
            internal int FadeOutSamples;
            internal float[] FadeOutCurve;
            internal bool AdjustVolume;
            internal float Volume;
            internal bool HasEffects => Normalize || FadeIn || FadeOut || AdjustVolume;
        }

        private readonly struct AudioProcessingResult
        {
            internal AudioProcessingResult(float[] samples, float peak, int clippedSampleCount)
            {
                Samples = samples;
                Peak = peak;
                ClippedSampleCount = clippedSampleCount;
            }

            internal float[] Samples { get; }
            internal float Peak { get; }
            internal int ClippedSampleCount { get; }
        }
    }
}
