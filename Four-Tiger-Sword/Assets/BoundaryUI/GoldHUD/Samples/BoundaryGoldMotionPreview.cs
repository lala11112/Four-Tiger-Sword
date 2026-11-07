using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BoundaryZeroUI
{
    /// <summary>Opt-in capture of the real reward animation for the delivery preview.</summary>
    public sealed class BoundaryGoldMotionPreview : MonoBehaviour
    {
        [Serializable] sealed class Frames { public float[] seconds; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--boundary-gold-motion-preview") >= 0)
                new GameObject("Gold Motion Preview").AddComponent<BoundaryGoldMotionPreview>();
        }

        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(.3f);
            string[] args = Environment.GetCommandLineArgs();
            int outputIndex = Array.IndexOf(args, "--boundary-gold-output");
            string output = outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1] : Path.Combine(Application.persistentDataPath, "GoldMotionPreview");
            Directory.CreateDirectory(output);
            var controller = FindFirstObjectByType<BoundaryUIController>();
            if (!controller) { Debug.LogError("Motion preview controller missing."); Application.Quit(2); yield break; }
            var demo = controller.GetComponent<BoundaryGoldHudDemo>();
            if (demo) demo.enabled = false;
            controller.ShowScreen("hud");
            controller.SetHudData("", "", .76f, "구역 탐색", new[] { "E", "R" });
            controller.SetGold(1250);
            var feed = controller.GoldHud.Feed;
            feed.Clear();
            var canvas = feed.GetComponentInParent<Canvas>();
            var camera = canvas && canvas.worldCamera ? canvas.worldCamera : FindFirstObjectByType<Camera>();
            if (!canvas || !camera) { Debug.LogError("Motion preview camera missing."); Application.Quit(2); yield break; }
            var target = new RenderTexture(2560, 1440, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(520, 300, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var previousMode = canvas.renderMode;
            var previousCamera = canvas.worldCamera;
            float previousDistance = canvas.planeDistance;
            var times = new List<float>();
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); yield return null;
                double start = Time.unscaledTimeAsDouble;
                float[] at = { .2f, .35f, .9f, 1.55f, 2.2f, 3.05f, 3.2f };
                int[] amounts = { 20, 10, 50, 100, 15, 25, 10 };
                int next = 0;
                while (Time.unscaledTimeAsDouble - start < 6.2)
                {
                    float elapsed = (float)(Time.unscaledTimeAsDouble - start);
                    while (next < at.Length && elapsed >= at[next]) feed.ShowGoldGain(amounts[next++]);
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    RenderTexture.active = target;
                    // Actual right-middle HUD pixels: no replacement artwork or simulated motion.
                    texture.ReadPixels(new Rect(2000, 570, 520, 300), 0, 0);
                    texture.Apply();
                    File.WriteAllBytes(Path.Combine(output, "frame-" + times.Count.ToString("D4") + ".png"), texture.EncodeToPNG());
                    times.Add(elapsed);
                    yield return new WaitForSecondsRealtime(1f / 30f);
                }
                File.WriteAllText(Path.Combine(output, "frames.json"), JsonUtility.ToJson(new Frames { seconds = times.ToArray() }, true));
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                canvas.renderMode = previousMode;
                canvas.worldCamera = previousCamera;
                canvas.planeDistance = previousDistance;
                Destroy(texture); target.Release(); Destroy(target);
            }
            Application.Quit(0);
        }
    }
}
