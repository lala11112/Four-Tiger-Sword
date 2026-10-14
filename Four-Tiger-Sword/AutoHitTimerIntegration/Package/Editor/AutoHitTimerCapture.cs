using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace StudioBaekho.AutoHitTimer
{
    internal sealed class AutoHitTimerCapture : IDisposable
    {
        const int ViewSize = 512;
        readonly PreviewRenderUtility preview;
        readonly AnimationClip clip;
        readonly GameObject root;
        PlayableGraph graph;
        AnimationClipPlayable playable;
        readonly List<double> times;
        readonly int fps;
        readonly string sessionId;
        readonly string directory;
        readonly string batchId;
        readonly JArray frames = new JArray();
        readonly Vector3 originalPosition;
        readonly Quaternion originalRotation;
        Bounds bounds;
        int index;
        bool disposed;
        public string SessionId => sessionId;

        public AutoHitTimerCapture(JObject request)
        {
            fps = (int?)request["fps"] ?? 24;
            if (fps != 24 && fps != 60) throw new AutoHitTimerException("INVALID_FPS", "촬영 주기는 24 또는 60 fps여야 합니다.");
            clip = AutoHitTimerAssets.ResolveClip(request["clip"] as JObject);
            var actor = AutoHitTimerScene.ResolveAnimator((string)request["actorId"]);
            var reason = AutoHitTimerAssets.RigReason(clip, actor);
            if (reason != null) throw new AutoHitTimerException("RIG_MISMATCH", reason);
            times = BuildTimes(clip.length, fps, request["times"] as JArray);
            var suppliedId = (string)request["sessionId"];
            if (!string.IsNullOrEmpty(suppliedId) && !Guid.TryParseExact(suppliedId, "D", out _)) throw new AutoHitTimerException("INVALID_SESSION", "세션 ID는 UUID 형식이어야 합니다.");
            sessionId = suppliedId ?? Guid.NewGuid().ToString("D");
            // Unique batch prevents refinement captures from overwriting review frames.
            batchId = Guid.NewGuid().ToString("N");
            directory = Path.Combine(AutoHitTimerBridge.RootPath, "media", sessionId, batchId);
            if (times.Count * (System.Text.Encoding.UTF8.GetByteCount(directory) * 2 + 150L) > AutoHitTimerBridge.MaximumMessageBytes - 4096)
                throw new AutoHitTimerException("CAPTURE_TOO_LARGE", "촬영 메타데이터가 1 MiB를 초과합니다. 구간을 나눠 촬영해 주세요.");
            Directory.CreateDirectory(directory);
            try
            {
                root = AutoHitTimerScene.CloneRenderHierarchy(actor, (string)request["weaponId"], (string)request["weaponBone"]);
                originalPosition = root.transform.position;
                originalRotation = root.transform.rotation;
                preview = new PreviewRenderUtility();
                preview.AddSingleGO(root);
                preview.camera.clearFlags = CameraClearFlags.Color;
                preview.camera.backgroundColor = new Color(0.11f, 0.13f, 0.17f, 1f);
                preview.camera.cameraType = CameraType.Preview;
                preview.camera.orthographic = true;
                preview.camera.nearClipPlane = 0.01f;
                preview.camera.farClipPlane = 1000f;
                preview.ambientColor = new Color(0.6f, 0.6f, 0.6f, 1f);
                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
                preview.lights[1].intensity = 0.8f;
                preview.lights[1].transform.rotation = Quaternion.Euler(315f, 215f, 0f);
                graph = PlayableGraph.Create("AutoHitTimer preview");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                var output = AnimationPlayableOutput.Create(graph, "Preview", root.GetComponent<Animator>());
                output.SetSourcePlayable(playable);
                graph.Play();
                var initialized = false;
                for (var i = 0; i <= 4; i++)
                {
                    Sample(clip.length * i / 4d);
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                    {
                        if (!renderer.enabled) continue;
                        if (!initialized) { bounds = renderer.bounds; initialized = true; }
                        else bounds.Encapsulate(renderer.bounds);
                    }
                }
                if (!initialized || bounds.size.sqrMagnitude < 0.0001f) throw new AutoHitTimerException("NO_RENDERERS", "미리보기에 사용할 활성 메시 렌더러가 없습니다.");
                bounds.Expand(Mathf.Max(0.2f, bounds.size.magnitude * 0.18f));
            }
            catch { Dispose(); throw; }
        }

        public static List<double> BuildTimes(double duration, int fps, JArray requested)
        {
            if (fps != 24 && fps != 60) throw new AutoHitTimerException("INVALID_FPS", "촬영 주기는 24 또는 60 fps여야 합니다.");
            if (duration <= 0 || double.IsNaN(duration) || double.IsInfinity(duration)) throw new AutoHitTimerException("INVALID_DURATION", "클립 길이가 유효하지 않습니다.");
            var result = new List<double>();
            if (requested != null)
            {
                if (requested.Count == 0) throw new AutoHitTimerException("INVALID_TIMES", "하나 이상의 촬영 시간이 필요합니다.");
                foreach (var value in requested)
                {
                    var time = (double)value;
                    if (double.IsNaN(time) || double.IsInfinity(time) || time < 0 || time > duration + 0.00001) throw new AutoHitTimerException("INVALID_TIMES", "촬영 시간은 클립 범위 내여야 합니다.");
                    result.Add(Math.Min(duration, time));
                }
                result = result.Distinct().OrderBy(x => x).ToList();
            }
            else
            {
                if (Math.Ceiling(duration * fps) > 7199) throw new AutoHitTimerException("CAPTURE_TOO_LARGE", "촬영당 최대 7200프레임입니다. 클립을 구간으로 나눠 주세요.");
                for (var i = 0; i < Math.Ceiling(duration * fps); i++) result.Add(i / (double)fps);
                result.Add(duration);
            }
            if (result.Count > 7200) throw new AutoHitTimerException("CAPTURE_TOO_LARGE", "촬영당 최대 7200프레임입니다.");
            return result;
        }

        public bool Step(out JObject result)
        {
            if (disposed) throw new ObjectDisposedException(nameof(AutoHitTimerCapture));
            result = null;
            var time = times[index];
            Sample(time);
            var id = batchId + "_f" + index.ToString("D6", CultureInfo.InvariantCulture);
            var file = Path.Combine(directory, id + "_" + time.ToString("F6", CultureInfo.InvariantCulture) + "s.png");
            Texture2D combined = null;
            try
            {
                combined = new Texture2D(ViewSize * 2, ViewSize, TextureFormat.RGB24, false);
                RenderView(combined, 0, new Vector3(1f, 0.35f, 1f));
                RenderView(combined, ViewSize, new Vector3(1f, 0.12f, 0f));
                combined.Apply(false);
                File.WriteAllBytes(file, combined.EncodeToPNG());
            }
            finally { if (combined) UnityEngine.Object.DestroyImmediate(combined); }
            frames.Add(new JObject { ["id"] = id, ["time"] = time, ["path"] = file.Replace('\\', '/') });
            index++;
            if (index < times.Count) return false;
            result = new JObject { ["sessionId"] = sessionId, ["clip"] = AutoHitTimerAssets.ClipRef(clip), ["duration"] = clip.length, ["fps"] = fps, ["frames"] = frames, ["mediaRoot"] = directory.Replace('\\', '/'), ["views"] = new JArray("front-oblique", "side"), ["timeBasis"] = "unscaled-clip-seconds" };
            File.WriteAllText(Path.Combine(directory, "frames.json"), result.ToString(Formatting.Indented));
            return true;
        }

        void Sample(double time)
        {
            root.transform.SetPositionAndRotation(originalPosition, originalRotation);
            playable.SetTime(time);
            graph.Evaluate(0f);
        }

        void RenderView(Texture2D combined, int offset, Vector3 direction)
        {
            var camera = preview.camera;
            var radius = Mathf.Max(0.5f, bounds.extents.magnitude);
            camera.transform.position = bounds.center + direction.normalized * (radius * 4f);
            camera.transform.LookAt(bounds.center);
            camera.orthographicSize = radius;
            preview.BeginPreview(new Rect(0, 0, ViewSize, ViewSize), GUIStyle.none);
            Texture rendered;
            try { preview.Render(true); }
            finally { rendered = preview.EndPreview(); }
            var previous = RenderTexture.active;
            RenderTexture temp = null;
            try
            {
                temp = RenderTexture.GetTemporary(ViewSize, ViewSize, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(rendered, temp);
                RenderTexture.active = temp;
                combined.ReadPixels(new Rect(0, 0, ViewSize, ViewSize), offset, 0, false);
            }
            finally
            {
                RenderTexture.active = previous;
                if (temp) RenderTexture.ReleaseTemporary(temp);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (graph.IsValid()) graph.Destroy();
            if (preview != null) preview.Cleanup();
            else if (root) UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
