using System;
using Penta.BossUI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates the authored run overlay once, then binds it to the existing run controller.</summary>
public static class BoundaryZeroResultSetup
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/BoundaryZeroRunOverlay.prefab";
    private const string BossPrefabPath = "Assets/UI/PentaBossGroggy/Prefabs/BossHPWithGroggy.prefab";
    private const string SpriteRoot = "Assets/_Project/Sprites/UI/BoundaryZERO/";

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Apply the run overlay in Edit Mode.");
        var scene = SceneManager.GetActiveScene();
        StageResultUI result = null;
        foreach (var candidate in UnityEngine.Object.FindObjectsByType<StageResultUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (candidate.gameObject.scene == scene) { result = candidate; break; }
        if (result == null) throw new InvalidOperationException("The active scene has no StageResultUI.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) prefab = CreatePrefab();
        else
        {
            RefreshLoadingPreset();
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }
        var overlay = FindOverlay(scene);
        if (overlay == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            overlay = instance.GetComponent<BoundaryZeroRunOverlay>();
        }
        Validate(overlay);
        ConfigureTypography(overlay);
        PrefabUtility.RecordPrefabInstancePropertyModifications(overlay);
        var bossHud = EnsureBossHealthHud(overlay.gameObject, result.GetComponent<RunManager>());
        PrefabUtility.RecordPrefabInstancePropertyModifications(bossHud);
        overlay.ShowLoading();
        overlay.Hide();
        var serialized = new SerializedObject(result);
        serialized.FindProperty("boundaryOverlay").objectReferenceValue = overlay;
        serialized.FindProperty("resultPanel").objectReferenceValue = overlay.ModalRoot;
        serialized.FindProperty("resultLabel").objectReferenceValue = overlay.Title;
        serialized.FindProperty("detailText").objectReferenceValue = overlay.Details;
        serialized.FindProperty("retryButton").objectReferenceValue = overlay.ActionButton;
        serialized.FindProperty("progressText").objectReferenceValue = overlay.StageLabel;
        serialized.FindProperty("resultText").objectReferenceValue = null;
        serialized.FindProperty("menuButton").objectReferenceValue = null;
        serialized.FindProperty("fontAsset").objectReferenceValue = Font();
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(result);
        EditorSceneManager.MarkSceneDirty(scene);
        return "BoundaryZERO result/loading bound: " + PrefabPath;
    }

    private static BoundaryZeroRunOverlay FindOverlay(Scene scene)
    {
        foreach (var overlay in UnityEngine.Object.FindObjectsByType<BoundaryZeroRunOverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (overlay.gameObject.scene == scene) return overlay;
        return null;
    }

    private static void RefreshLoadingPreset()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var overlay = root.GetComponent<BoundaryZeroRunOverlay>();
            Validate(overlay);
            ConfigureTypography(overlay);
            EnsureBossHealthHud(root);
            // Title is shared by loading and result modes. Update its authored loading preset
            // in place; ShowResult restores the larger result layout when the run completes.
            overlay.ShowLoading();
            overlay.Hide();
            EditorUtility.SetDirty(overlay.Title);
            EditorUtility.SetDirty(overlay.Title.rectTransform);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void Validate(BoundaryZeroRunOverlay overlay)
    {
        if (overlay == null || overlay.ModalRoot == null || overlay.ActionButton == null
            || overlay.Title == null || overlay.Details == null || overlay.StageLabel == null)
            throw new InvalidOperationException("The authored run overlay is missing required bindings.");
        if (overlay.GetComponent<Canvas>() == null || overlay.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            throw new InvalidOperationException("The authored run overlay requires a Canvas and GraphicRaycaster.");
    }

    private static GameObject CreatePrefab()
    {
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = new GameObject("BoundaryZeroRunOverlay", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster), typeof(BoundaryZeroRunOverlay));
            SceneManager.MoveGameObjectToScene(root, preview);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1440);
            scaler.matchWidthOrHeight = .5f;

            var stage = Label("StageProgress", root.transform, 0, 0, 620, 42, 23, string.Empty);
            stage.rectTransform.anchorMin = stage.rectTransform.anchorMax = stage.rectTransform.pivot = new Vector2(.5f, 1);
            stage.rectTransform.anchoredPosition = new Vector2(0, -20);
            stage.alignment = TextAlignmentOptions.Center;

            var modal = Rect("Modal", root.transform, 0, 0, 2560, 1440);
            Stretch(modal);
            var shade = modal.gameObject.AddComponent<UnityEngine.UI.Image>();
            shade.color = new Color(.025f, .04f, .065f, .98f);
            shade.raycastTarget = true;
            var frame = Rect("ReferenceFrame", modal, 0, 0, 2560, 1440);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, .5f);
            frame.anchoredPosition = Vector2.zero;
            frame.gameObject.AddComponent<BoundaryZeroWindowFit>().Configure(new Vector2(2560, 1440), 1, 0);

            var artwork = Rect("ResultArtwork", frame, 120, 270, 1120, 806);
            var clear = Picture("ClearGate", artwork, "Results/clear_gate.png", 95, 166, 839, 573);
            var death = Picture("DeathTalisman", artwork, "Results/death_talisman.png", 344, 78, 419, 682);
            var rule = Picture("TitleRule", frame, "Results/title_rule.png", 1380, 521, 950, 6);

            var loading = Rect("Loading", frame, 0, 0, 2560, 1440);
            var seal = Rect("Seal", loading, 1280, 620, 1200, 1200);
            seal.pivot = new Vector2(.5f, .5f);
            seal.localScale = Vector3.one * .875f;
            Picture("Housing", seal, "Loading/housing.png", 0, 0, 1200, 1200);
            for (int i = 1; i <= 5; i++)
            {
                var shutter = Picture("Shutter" + i, seal, "Loading/shutter_" + i + ".png", 0, 0, 1200, 1200);
                var channel = Picture("Channel" + i, shutter.rectTransform, "Loading/channel_" + i + ".png", 0, 0, 1200, 1200);
                channel.type = UnityEngine.UI.Image.Type.Filled;
                channel.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
                channel.fillAmount = 0;
            }
            var rotor = Picture("Rotor", seal, "Loading/rotor.png", 0, 0, 1200, 1200);
            CenterLayer(rotor.rectTransform);
            Picture("Ticks", seal, "Loading/ticks.png", 0, 0, 1200, 1200);
            Picture("Core", seal, "Loading/core.png", 0, 0, 1200, 1200);
            var pulse = Picture("Pulse", seal, "Loading/pulse.png", 0, 0, 1200, 1200);
            var track = Picture("ProgressTrack", loading, "Loading/progress_track.png", 1860, 1271, 520, 4);
            var activity = Picture("Activity", track.rectTransform, "Loading/activity_signal.png", 0, 0, 64, 4);

            var title = Label("Title", frame, 1372, 365, 1000, 125, 78, string.Empty);
            title.fontStyle = FontStyles.Bold;
            var details = Label("Details", frame, 1380, 620, 950, 265, 33, string.Empty);
            details.color = new Color(.68f, .75f, .81f, 1);
            var actionImage = Picture("PrimaryAction", frame, "Controls/button_primary_950x94.png", 1380, 1030, 950, 94);
            actionImage.type = UnityEngine.UI.Image.Type.Sliced;
            actionImage.raycastTarget = true;
            var action = actionImage.gameObject.AddComponent<UnityEngine.UI.Button>();
            action.targetGraphic = actionImage;
            action.transition = UnityEngine.UI.Selectable.Transition.SpriteSwap;
            action.spriteState = new UnityEngine.UI.SpriteState
            {
                highlightedSprite = Sprite("Controls/button_primary_hover.png"),
                pressedSprite = Sprite("Controls/button_primary_pressed.png"),
                disabledSprite = Sprite("Controls/button_primary_disabled.png"),
                selectedSprite = Sprite("Controls/button_primary_hover.png")
            };
            var actionText = Label("Label", actionImage.transform, 0, 0, 950, 94, 32, "새 탐사 시작");
            actionText.alignment = TextAlignmentOptions.Center;

            root.GetComponent<BoundaryZeroRunOverlay>().Configure(modal.gameObject, loading.gameObject,
                clear.gameObject, death.gameObject, rule.gameObject, title, details, stage, action,
                rotor.rectTransform, pulse, activity.rectTransform);
            ConfigureTypography(root.GetComponent<BoundaryZeroRunOverlay>());
            root.GetComponent<BoundaryZeroRunOverlay>().ShowLoading();
            root.GetComponent<BoundaryZeroRunOverlay>().Hide();
            clear.gameObject.SetActive(false);
            death.gameObject.SetActive(false);
            modal.gameObject.SetActive(false);
            stage.gameObject.SetActive(false);
            EnsureBossHealthHud(root);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Validate(saved.GetComponent<BoundaryZeroRunOverlay>());
            return saved;
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    /// <summary>Applies only the boss artwork and its scene binding, preserving loading/results.</summary>
    public static string ApplyBossHealthHud()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Apply the boss HUD in Edit Mode.");
        var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            EnsureBossHealthHud(contents);
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var run in root.GetComponentsInChildren<RunManager>(true))
            {
                // RunManager resolves this component in Awake; authoring also runs before Play Mode.
                var resultUI = run.resultUI != null ? run.resultUI : run.GetComponent<StageResultUI>();
                if (resultUI == null) continue;
                var result = new SerializedObject(resultUI);
                var overlay = result.FindProperty("boundaryOverlay").objectReferenceValue as BoundaryZeroRunOverlay;
                if (overlay == null) continue;
                var hud = EnsureBossHealthHud(overlay.gameObject, run);
                PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
                EditorSceneManager.MarkSceneDirty(scene);
            }
        return "BoundaryZERO boss health artwork and active-scene bindings applied.";
    }

    /// <summary>Upgrade only the gameplay boss bar; preserve all existing modal artwork and bindings.</summary>
    public static BossHealthHud EnsureBossHealthHud(GameObject root, RunManager run = null)
    {
        var overlay = root.GetComponent<BoundaryZeroRunOverlay>();
        if (overlay == null) throw new InvalidOperationException("A boss HUD requires the authored run overlay.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
        var source = prefab != null ? prefab.GetComponent<BossHealthGroggyUI>() : null;
        if (source == null || source.HealthFill == null || source.Groggy == null || source.BossName == null)
            throw new InvalidOperationException("The integrated boss HP/groggy prefab is missing its bindings.");
        if (overlay.StageLabel != null) overlay.StageLabel.gameObject.SetActive(false);

        var bar = root.transform.Find("BossHealthBar") as RectTransform
            ?? Rect("BossHealthBar", root.transform, 0, 0, 720, 72);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(.5f, 1);
        bar.anchoredPosition = new Vector2(0, -48);
        bar.sizeDelta = new Vector2(720, 72);
        bar.gameObject.SetActive(true);
        // Keep the modal above gameplay UI even while its Canvas switches sorting order.
        if (overlay.ModalRoot != null && bar.GetSiblingIndex() > overlay.ModalRoot.transform.GetSiblingIndex())
            bar.SetSiblingIndex(overlay.ModalRoot.transform.GetSiblingIndex());
        var visibility = bar.GetComponent<CanvasGroup>();
        if (visibility == null) visibility = bar.gameObject.AddComponent<CanvasGroup>();

        var view = bar.Find("BossHPWithGroggy")?.GetComponent<BossHealthGroggyUI>();
        if (view == null)
            view = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, bar)).GetComponent<BossHealthGroggyUI>();
        var viewRect = (RectTransform)view.transform;
        viewRect.anchorMin = viewRect.anchorMax = viewRect.pivot = new Vector2(.5f, 1f);
        viewRect.anchoredPosition = Vector2.zero;
        viewRect.sizeDelta = new Vector2(640f, 54f);
        viewRect.localScale = Vector3.one;
        viewRect.localRotation = Quaternion.identity;

        var fill = view.HealthFill;
        var trail = EnsureBossTrail(fill);
        var name = view.BossName;
        // General overlay typography also visits nested labels; retain this prefab's style.
        name.font = source.BossName.font;
        name.fontSharedMaterial = source.BossName.fontSharedMaterial;
        name.fontStyle = source.BossName.fontStyle;
        name.color = source.BossName.color;
        name.fontSize = 20f;
        name.enableAutoSizing = false;
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.richText = false;
        foreach (var graphic in view.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            graphic.raycastTarget = false;

        var hud = root.GetComponent<BossHealthHud>() ?? root.AddComponent<BossHealthHud>();
        var boundRun = run != null ? run : new SerializedObject(hud).FindProperty("run").objectReferenceValue as RunManager;
        hud.Configure(boundRun, visibility, fill);
        hud.ConfigureVisuals(trail, 8f, .35f, .6f);
        hud.ConfigureName(name);
        hud.ConfigureGroggy(view.Groggy);

        // Rebind first, then remove only the obsolete visual layers, keeping the visibility root.
        foreach (string legacyName in new[] { "Track", "Fill", "Trail", "Frame", "BossName" })
        {
            var legacy = bar.Find(legacyName);
            if (legacy != null) UnityEngine.Object.DestroyImmediate(legacy.gameObject);
        }
        var oldBackground = bar.GetComponent<UnityEngine.UI.Image>();
        if (oldBackground != null) UnityEngine.Object.DestroyImmediate(oldBackground);

        foreach (UnityEngine.Object changed in new UnityEngine.Object[]
            { viewRect, name, fill, view.Groggy, view.Groggy.gameObject, view.Groggy.Fill })
            PrefabUtility.RecordPrefabInstancePropertyModifications(changed);
        EditorUtility.SetDirty(hud);
        return hud;
    }

    private static UnityEngine.UI.Image EnsureBossTrail(UnityEngine.UI.Image fill)
    {
        var sourceRect = fill.rectTransform;
        var rect = sourceRect.parent.Find("HP Trail") as RectTransform
            ?? Rect("HP Trail", sourceRect.parent, 0, 0, 1, 1);
        rect.anchorMin = sourceRect.anchorMin;
        rect.anchorMax = sourceRect.anchorMax;
        rect.pivot = sourceRect.pivot;
        rect.anchoredPosition = sourceRect.anchoredPosition;
        rect.sizeDelta = sourceRect.sizeDelta;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        // Move behind HP after first moving to the end, so repeated setup preserves order.
        rect.SetAsLastSibling();
        rect.SetSiblingIndex(sourceRect.GetSiblingIndex());
        var image = rect.GetComponent<UnityEngine.UI.Image>();
        if (image == null) image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = fill.sprite;
        image.color = new Color32(255, 145, 150, 255);
        image.material = fill.material;
        image.type = fill.type;
        image.fillMethod = fill.fillMethod;
        image.fillOrigin = fill.fillOrigin;
        image.fillClockwise = fill.fillClockwise;
        image.preserveAspect = fill.preserveAspect;
        image.maskable = fill.maskable;
        image.raycastTarget = false;
        return image;
    }

    private static void CenterLayer(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
    }

    private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        var result = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        result.SetParent(parent, false);
        result.anchorMin = result.anchorMax = result.pivot = new Vector2(0, 1);
        result.anchoredPosition = new Vector2(x, -y);
        result.sizeDelta = new Vector2(width, height);
        return result;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static UnityEngine.UI.Image Picture(string name, Transform parent, string path,
        float x, float y, float width, float height)
    {
        var image = Rect(name, parent, x, y, width, height).gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = Sprite(path);
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text Label(string name, Transform parent, float x, float y, float width,
        float height, float size, string value)
    {
        var label = Rect(name, parent, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = Font(); label.fontSharedMaterial = BoundaryZeroTypography.GetMaterial();
        label.fontSize = size; label.text = value;
        label.color = new Color(.9f, .93f, .96f, 1);
        label.raycastTarget = false; label.richText = false;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    private static Sprite Sprite(string path)
        => AssetDatabase.LoadAssetAtPath<Sprite>(SpriteRoot + path)
            ?? throw new InvalidOperationException("Missing BoundaryZERO sprite: " + path);
    private static TMP_FontAsset Font() => BoundaryZeroTypography.GetFont();

    private static void ConfigureTypography(BoundaryZeroRunOverlay overlay)
        => overlay.ConfigureTypography(Font(), BoundaryZeroTypography.GetFont(BoundaryZeroTypography.Role.KoreanTitle),
            BoundaryZeroTypography.GetFont(BoundaryZeroTypography.Role.Numeric));
}
