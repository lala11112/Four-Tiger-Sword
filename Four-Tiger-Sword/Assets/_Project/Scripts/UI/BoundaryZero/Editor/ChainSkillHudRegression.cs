using System;
using System.IO;
using ArpgHud;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Checks the saved chain display against authoritative gameplay snapshots.</summary>
public static class ChainSkillHudRegression
{
    private static int _checks;

    /// <summary>Renders the saved HUD in an isolated preview scene without changing open scenes.</summary>
    public static string CapturePreview(string outputPath)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Capture chain HUD in Edit Mode.");
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("A PNG output path is required.", nameof(outputPath));
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/PlayerHUD.prefab");
        if (prefab == null) throw new InvalidOperationException("PlayerHUD prefab is missing.");
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        GameObject cameraRoot = null;
        RenderTexture target = null;
        Texture2D pixels = null;
        var previousActive = RenderTexture.active;
        try
        {
            root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            root.hideFlags = HideFlags.HideAndDontSave;
            cameraRoot = EditorUtility.CreateGameObjectWithHideFlags("Chain HUD preview camera", HideFlags.HideAndDontSave, typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            var camera = cameraRoot.GetComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f, .04f, .06f, 1f);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 100f;
            camera.useOcclusionCulling = false;
            camera.transform.SetPositionAndRotation(new Vector3(0, 0, -10), Quaternion.identity);
            target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32)
            { hideFlags = HideFlags.HideAndDontSave };
            target.Create();
            camera.targetTexture = target;
            camera.aspect = 1920f / 1080f;

            var canvas = root.GetComponent<Canvas>();
            if (canvas == null) throw new InvalidOperationException("PlayerHUD Canvas is missing.");
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            foreach (var visibility in root.GetComponentsInChildren<CanvasGroup>(true)) visibility.alpha = 1f;
            var view = root.GetComponent<HudView>();
            if (view == null) throw new InvalidOperationException("PlayerHUD view is missing.");
            view.Render(new HudState
            {
                Hp = new MeterState(4200, 5000),
                Auxiliary = new MeterState(75, 100),
                UltimateCharge = new MeterState(65, 100),
                ActiveFormIndex = 3,
                FireChain = new ChainSkillState(4.2f, false),
                WaterChain = new ChainSkillState(2.6f, true)
            });
            Canvas.ForceUpdateCanvases();
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true, true);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false)
            { hideFlags = HideFlags.HideAndDontSave };
            pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            pixels.Apply();
            string absolute = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllBytes(absolute, pixels.EncodeToPNG());
            return absolute;
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (cameraRoot != null) cameraRoot.GetComponent<Camera>().targetTexture = null;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (cameraRoot != null) UnityEngine.Object.DestroyImmediate(cameraRoot);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run chain HUD regression in Edit Mode.");
        _checks = 0;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/PlayerHUD.prefab");
        Check(prefab != null, "PlayerHUD prefab is missing.");
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
            var view = root.GetComponent<HudView>();
            Check(view != null, "PlayerHUD view is missing.");
            var group = root.transform.Find("BoundaryZeroHUD/Forms/ChainSkills");
            Check(group != null, "Chain skill HUD was not saved on PlayerHUD.");
            var fire = group.Find("Fire");
            var water = group.Find("Water");
            var fireText = fire.Find("Remaining").GetComponent<TMP_Text>();
            var waterText = water.Find("Remaining").GetComponent<TMP_Text>();
            var hint = group.Find("ActionHint").GetComponent<TMP_Text>();
            Check(fireText != null && waterText != null && hint != null, "Chain labels are not bound.");

            var state = new HudState
            {
                FireChain = new ChainSkillState(4.2f, false),
                WaterChain = new ChainSkillState(2.6f, true)
            };
            view.Render(state);
            Check(group.gameObject.activeSelf && fire.gameObject.activeSelf && water.gameObject.activeSelf,
                "Both prepared forms must be visible.");
            Check(!fire.Find("Selected").gameObject.activeSelf && water.Find("Selected").gameObject.activeSelf,
                "Only the F target should be emphasized.");
            Check(hint.gameObject.activeSelf && hint.text == "F  수 교체", "F must identify the selected water form.");
            Check(fireText.text.Contains("4.2") && waterText.text.Contains("2.6"), "Readiness times differ from the snapshot.");
            string originalText = fireText.text;
            view.Render(state);
            Check(fireText.text == originalText, "Repeated rendering must not advance readiness timers.");

            state.FireChain.Selected = true;
            state.WaterChain.Selected = false;
            view.Render(state);
            Check(fire.Find("Selected").gameObject.activeSelf && !water.Find("Selected").gameObject.activeSelf,
                "Changing the F target must move the emphasis.");
            Check(hint.text == "F  화 교체", "F must identify the selected fire form.");

            state.FireChain = new ChainSkillState(1.2f, false);
            state.WaterChain = default;
            view.Render(state);
            Check(fire.gameObject.activeSelf && !water.gameObject.activeSelf, "Expired water readiness must disappear.");
            Check(!hint.gameObject.activeSelf && !fire.Find("Selected").gameObject.activeSelf,
                "A ready but unusable form must not advertise an F action.");

            view.Render(default);
            Check(!group.gameObject.activeSelf, "The readiness group must hide when neither form is ready.");
            Check(HudMath.ChainRemaining(new ChainSkillState(float.NaN, true)) == 0f
                && HudMath.ChainRemaining(new ChainSkillState(float.PositiveInfinity, true)) == 0f
                && HudMath.ChainRemaining(new ChainSkillState(-1f, true)) == 0f,
                "Invalid readiness values must not produce a ready display.");

            view.Render(state);
            Canvas.ForceUpdateCanvases();
            foreach (var label in group.GetComponentsInChildren<TMP_Text>(true))
            {
                Check(label.font != null && !label.raycastTarget, "Chain labels require the existing font and nonblocking graphics.");
                Check(label.rectTransform.rect.width > 0 && label.rectTransform.rect.height > 0,
                    "A chain label has no visible dimensions.");
            }
            foreach (var graphic in group.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                Check(!graphic.raycastTarget, "The chain display must not capture gameplay clicks.");
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
        }
        return "Chain skill HUD: " + _checks + " checks passed (saved bindings, both timers, F selection, expiration, passive rendering).";
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
