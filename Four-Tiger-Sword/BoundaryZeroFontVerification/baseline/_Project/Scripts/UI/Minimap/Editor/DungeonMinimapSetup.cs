using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Installs explicitly bound minimap/full-map views without rebuilding existing UI prefabs.</summary>
public static class DungeonMinimapSetup
{
    public const string PrefabPath = "Assets/_Project/Prefabs/UI/DungeonMinimap.prefab";
    public const string FullMapPrefabPath = "Assets/_Project/Prefabs/UI/DungeonFullMap.prefab";
    private const string GoldPath = "Assets/_Game/Shop/UI/GoldHUD.prefab";

    [MenuItem("Tools/Studio Baekho/Dungeon/Install Minimap")]
    private static void InstallMenu() => Debug.Log(Install());

    public static string Install()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Install the minimap in Edit Mode.");
        Scene scene = SceneManager.GetActiveScene();
        RunManager run = null;
        DungeonMinimapPresenter presenter = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (var candidate in root.GetComponentsInChildren<RunManager>(true))
            {
                if (run != null) throw new System.InvalidOperationException("The active scene must have exactly one RunManager.");
                run = candidate;
            }
            foreach (var candidate in root.GetComponentsInChildren<DungeonMinimapPresenter>(true))
            {
                if (presenter != null) throw new System.InvalidOperationException("The active scene already has multiple minimaps.");
                presenter = candidate;
            }
        }
        if (run == null) throw new System.InvalidOperationException("The active scene has no RunManager.");
        GameObject prefab = EnsurePrefab();
        // Upgrade the source even when this scene is already bound to an existing full-map instance.
        GameObject fullMapPrefab = EnsureFullMapPrefab();
        if (presenter == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Install dungeon minimap");
            presenter = instance.GetComponent<DungeonMinimapPresenter>();
        }
        var miniView = presenter.View != null ? presenter.View : presenter.GetComponent<DungeonMinimapView>();
        if (miniView == null) throw new System.InvalidOperationException("The minimap presenter has no minimap view.");
        UpgradeStageLabelRoot(miniView.gameObject, true);
        DungeonMinimapView fullMapView = presenter.FullMapView;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (var candidate in root.GetComponentsInChildren<DungeonMinimapView>(true))
            {
                if (candidate == miniView || candidate.Mode != DungeonMinimapView.MapMode.EntireDungeon) continue;
                if (fullMapView != null && fullMapView != candidate)
                    throw new System.InvalidOperationException("The active scene already has multiple full-map views.");
                fullMapView = candidate;
            }
        if (fullMapView == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(fullMapPrefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Install dungeon full map");
            fullMapView = instance.GetComponent<DungeonMinimapView>();
        }
        if (fullMapView == miniView || fullMapView.gameObject.scene != scene)
            throw new System.InvalidOperationException("The full map must reference a separate view in the active scene.");
        UpgradeFullMapRoot(fullMapView.gameObject, true);
        Undo.RecordObject(fullMapView, "Configure dungeon full map");
        fullMapView.Mode = DungeonMinimapView.MapMode.EntireDungeon;
        PrefabUtility.RecordPrefabInstancePropertyModifications(fullMapView);
        EditorUtility.SetDirty(fullMapView);
        Undo.RecordObject(presenter, "Bind dungeon minimap");
        presenter.Configure(run, miniView, fullMapView);
        PrefabUtility.RecordPrefabInstancePropertyModifications(presenter);
        EditorUtility.SetDirty(presenter);
        MoveGold();
        EditorSceneManager.MarkSceneDirty(scene);
        return "Installed one minimap and one independent Tab full map with explicit view bindings; existing UI prefabs retained. Scene remains open.";
    }

    private static GameObject EnsurePrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                if (UpgradeStageLabelRoot(contents))
                    PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }
        EnsureFolder("Assets/_Project/Prefabs/UI");
        var root = new GameObject("DungeonMinimap", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(CanvasGroup));
        try
        {
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            var group = root.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = group.interactable = false;

            RectTransform panel = Rect("Panel", root.transform);
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one;
            panel.anchoredPosition = new Vector2(-48f, -198f);
            panel.sizeDelta = new Vector2(300f, 300f);
            Image(panel, new Color(.06f, .17f, .20f, .92f));
            RectTransform background = Rect("Background", panel);
            Stretch(background, 2f);
            Image(background, new Color(.025f, .04f, .06f, .94f));
            RectTransform viewport = Rect("Viewport", panel);
            Stretch(viewport, 12f);
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            RectTransform content = Rect("MapContent", viewport);
            content.sizeDelta = Vector2.zero;
            RectTransform markers = Rect("MarkerLayer", viewport);
            Stretch(markers, 0f);
            RectTransform arrowRect = Rect("PlayerArrow", viewport);
            arrowRect.sizeDelta = new Vector2(18f, 22f);
            arrowRect.gameObject.AddComponent<CanvasRenderer>();
            var arrow = arrowRect.gameObject.AddComponent<DungeonMinimapIconGraphic>();
            arrow.SetSymbol(DungeonMinimapIconGraphic.Symbol.Player);
            arrow.color = Color.white;
            arrow.raycastTarget = false;
            var view = root.AddComponent<DungeonMinimapView>();
            view.Configure(group, viewport, content, markers, arrow);
            UpgradeStageLabelRoot(root);
            root.AddComponent<DungeonMinimapPresenter>().Configure(null, view, null);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }

    /// <summary>Upgrades only the minimap stage label, preserving the existing HUD and full-map art.</summary>
    public static string ApplyStageLabel()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Apply the minimap stage label in Edit Mode.");
        EnsurePrefab();
        return "Chapter/stage label bound inside the minimap's lower-left corner.";
    }

    public static bool UpgradeStageLabelRoot(GameObject root, bool undo = false)
    {
        var view = root.GetComponent<DungeonMinimapView>();
        if (view == null || view.Mode != DungeonMinimapView.MapMode.FollowPlayer)
            throw new System.InvalidOperationException("The stage label requires a minimap view.");
        var panel = root.transform.Find("Panel") as RectTransform;
        if (panel == null) throw new System.InvalidOperationException("The minimap panel is missing.");
        var label = view.StageLabel ?? panel.Find("StageLabel")?.GetComponent<TMP_Text>();
        bool created = label == null;
        if (created)
        {
            label = Text("StageLabel", panel, string.Empty, 20f, new Color(.88f, .92f, .96f, 1f));
            if (undo) Undo.RegisterCreatedObjectUndo(label.gameObject, "Add minimap stage label");
        }
        string previousText = EditorJsonUtility.ToJson(label);
        string previousRect = EditorJsonUtility.ToJson(label.rectTransform);
        bool wasActive = label.gameObject.activeSelf;
        if (undo)
        {
            Undo.RecordObject(label, "Style minimap stage label");
            Undo.RecordObject(label.rectTransform, "Place minimap stage label");
        }
        // A sibling of Viewport follows neither the scrolling map nor its clipping mask.
        if (label.transform.parent != panel)
        {
            if (undo) Undo.SetTransformParent(label.transform, panel, "Parent minimap stage label");
            else label.transform.SetParent(panel, false);
        }
        EnsureRenderer(label.gameObject, undo);
        var rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(12f, 20f);
        rect.sizeDelta = new Vector2(206f, 28f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.SetAsLastSibling();
        label.fontSize = 20f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 16f;
        label.fontSizeMax = 20f;
        label.color = new Color(.88f, .92f, .96f, 1f);
        label.alignment = TextAlignmentOptions.BottomLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.richText = false;
        label.raycastTarget = false;
        bool rebound = view.StageLabel != label;
        if (undo) Undo.RecordObject(view, "Bind minimap stage label");
        view.ConfigureStageLabel(label);
        bool changed = created || rebound || wasActive != label.gameObject.activeSelf
            || previousText != EditorJsonUtility.ToJson(label) || previousRect != EditorJsonUtility.ToJson(rect);
        if (changed)
        {
            Dirty(label);
            Dirty(rect);
            Dirty(label.gameObject);
            Dirty(view);
        }
        return changed;
    }

    private static GameObject EnsureFullMapPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(FullMapPrefabPath);
        if (existing != null)
        {
            var contents = PrefabUtility.LoadPrefabContents(FullMapPrefabPath);
            try
            {
                if (UpgradeFullMapRoot(contents, false))
                    PrefabUtility.SaveAsPrefabAsset(contents, FullMapPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(FullMapPrefabPath);
        }
        EnsureFolder("Assets/_Project/Prefabs/UI");
        var root = new GameObject("DungeonFullMap", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(CanvasGroup));
        try
        {
            // This root is independent of the minimap CanvasGroup, so hiding the minimap cannot hide it.
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 160; // Travel input covers shop hints; run results remain above it at 200.
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            var group = root.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = group.interactable = false;

            RectTransform backdrop = Rect("Backdrop", root.transform);
            Stretch(backdrop, 0f);
            Image(backdrop, new Color(.005f, .012f, .02f, .72f));
            RectTransform panel = Rect("Panel", root.transform);
            panel.anchorMin = new Vector2(.08f, .1f);
            panel.anchorMax = new Vector2(.92f, .9f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            Image(panel, new Color(.10f, .26f, .30f, .96f));
            RectTransform background = Rect("Background", panel);
            Stretch(background, 2f);
            Image(background, new Color(.018f, .033f, .047f, .98f));

            var title = Text("Title", panel, "DUNGEON MAP", 28f, new Color(.84f, .96f, .96f, 1f));
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = Vector2.one;
            titleRect.pivot = new Vector2(.5f, 1f);
            titleRect.offsetMin = new Vector2(32f, -72f);
            titleRect.offsetMax = new Vector2(-32f, -16f);
            title.alignment = TextAlignmentOptions.MidlineLeft;

            RectTransform divider = Rect("HeaderDivider", panel);
            divider.anchorMin = new Vector2(0f, 1f);
            divider.anchorMax = Vector2.one;
            divider.pivot = new Vector2(.5f, 1f);
            divider.offsetMin = new Vector2(32f, -77f);
            divider.offsetMax = new Vector2(-32f, -76f);
            Image(divider, new Color(.16f, .34f, .38f, .9f));

            RectTransform viewport = Rect("Viewport", panel);
            Stretch(viewport, 32f);
            viewport.offsetMin = new Vector2(32f, 72f);
            viewport.offsetMax = new Vector2(-32f, -96f);
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            RectTransform content = Rect("MapContent", viewport);
            content.sizeDelta = Vector2.zero;
            RectTransform markers = Rect("MarkerLayer", viewport);
            Stretch(markers, 0f);
            RectTransform arrowRect = Rect("PlayerArrow", viewport);
            arrowRect.sizeDelta = new Vector2(18f, 22f);
            arrowRect.gameObject.AddComponent<CanvasRenderer>();
            var arrow = arrowRect.gameObject.AddComponent<DungeonMinimapIconGraphic>();
            arrow.SetSymbol(DungeonMinimapIconGraphic.Symbol.Player);
            arrow.color = Color.white;
            arrow.raycastTarget = false;

            var hint = Text("CloseHint", panel, "TAB / ESC  CLOSE", 17f, new Color(.57f, .73f, .76f, 1f));
            RectTransform hintRect = hint.rectTransform;
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(.5f, 0f);
            hintRect.offsetMin = new Vector2(32f, 16f);
            hintRect.offsetMax = new Vector2(-32f, 56f);
            hint.alignment = TextAlignmentOptions.MidlineRight;

            var view = root.AddComponent<DungeonMinimapView>();
            view.Configure(group, viewport, content, markers, arrow);
            view.Mode = DungeonMinimapView.MapMode.EntireDungeon;
            UpgradeFullMapRoot(root, false);
            if (PrefabUtility.SaveAsPrefabAsset(root, FullMapPrefabPath) == null)
                throw new System.InvalidOperationException("Could not save the dungeon full-map prefab.");
        }
        finally { Object.DestroyImmediate(root); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(FullMapPrefabPath);
    }

    /// <summary>Adds travel input/status bindings while retaining the authored hierarchy, geometry and icon art.</summary>
    private static bool UpgradeFullMapRoot(GameObject root, bool undo)
    {
        bool changed = false;
        var view = root.GetComponent<DungeonMinimapView>();
        if (view == null) throw new System.InvalidOperationException("Full-map view is missing.");
        var viewport = Reference<RectTransform>(view, "viewport");
        if (viewport == null) throw new System.InvalidOperationException("Full-map viewport binding is missing.");
        var canvas = root.GetComponent<Canvas>();
        if (canvas == null) throw new System.InvalidOperationException("Full-map canvas is missing.");
        if (canvas.sortingOrder != 160)
        {
            if (undo) Undo.RecordObject(canvas, "Order map travel overlay");
            canvas.sortingOrder = 160;
            Dirty(canvas);
            changed = true;
        }
        var raycaster = root.GetComponent<UnityEngine.UI.GraphicRaycaster>();
        if (raycaster == null)
        { raycaster = Add<UnityEngine.UI.GraphicRaycaster>(root, undo); changed = true; }
        if (!raycaster.enabled)
        {
            if (undo) Undo.RecordObject(raycaster, "Enable map travel pointer");
            raycaster.enabled = true;
            Dirty(raycaster);
            changed = true;
        }

        var surface = viewport.GetComponent<UnityEngine.UI.Graphic>();
        if (surface == null)
        {
            EnsureRenderer(viewport.gameObject, undo);
            var image = Add<UnityEngine.UI.Image>(viewport.gameObject, undo);
            image.color = Color.clear;
            image.raycastTarget = false;
            surface = image;
            changed = true;
        }
        var handler = viewport.GetComponent<DungeonMapPointerHandler>();
        if (handler == null) { handler = Add<DungeonMapPointerHandler>(viewport.gameObject, undo); changed = true; }
        if (Reference<DungeonMinimapView>(handler, "view") != view
            || Reference<UnityEngine.UI.Graphic>(handler, "surface") != surface)
        {
            if (undo) Undo.RecordObject(handler, "Bind map pointer");
            handler.Configure(view, surface);
            Dirty(handler);
            changed = true;
        }

        var blocker = Reference<UnityEngine.UI.Graphic>(view, "inputBlocker");
        if (blocker == null)
        {
            Transform backdrop = root.transform.Find("Backdrop");
            blocker = backdrop != null ? backdrop.GetComponent<UnityEngine.UI.Graphic>() : null;
            if (blocker == null)
            {
                RectTransform rect = Rect("TravelInputBlocker", root.transform);
                Stretch(rect, 0f);
                rect.SetAsFirstSibling();
                EnsureRenderer(rect.gameObject, undo);
                var image = Add<UnityEngine.UI.Image>(rect.gameObject, undo);
                image.color = Color.clear;
                image.raycastTarget = false;
                blocker = image;
                if (undo) Undo.RegisterCreatedObjectUndo(rect.gameObject, "Add map input blocker");
            }
            changed = true;
        }

        var status = Reference<TMP_Text>(view, "travelStatus");
        if (status == null)
        {
            Transform parent = viewport.parent;
            Transform found = parent.Find("TravelStatus");
            status = found != null ? found.GetComponent<TMP_Text>() : null;
            if (status == null)
            {
                status = Text("TravelStatus", parent, "방문한 방을 클릭하면 이동합니다.", 19f,
                    new Color(.73f, .88f, .87f, 1f));
                RectTransform rect = status.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = new Vector2(.74f, 0f);
                rect.pivot = new Vector2(.5f, 0f);
                rect.offsetMin = new Vector2(32f, 16f);
                rect.offsetMax = new Vector2(-16f, 56f);
                status.alignment = TextAlignmentOptions.MidlineLeft;
                if (undo) Undo.RegisterCreatedObjectUndo(status.gameObject, "Add map travel status");
            }
            changed = true;
        }
        if (Reference<DungeonMapPointerHandler>(view, "pointerHandler") != handler
            || Reference<TMP_Text>(view, "travelStatus") != status
            || Reference<UnityEngine.UI.Graphic>(view, "inputBlocker") != blocker)
        {
            if (undo) Undo.RecordObject(view, "Bind map travel UI");
            view.ConfigureTravelUI(handler, status, blocker);
            Dirty(view);
            changed = true;
        }
        return changed;
    }

    private static T Reference<T>(Object target, string property) where T : Object
        => new SerializedObject(target).FindProperty(property)?.objectReferenceValue as T;

    private static T Add<T>(GameObject target, bool undo) where T : Component
        => undo ? Undo.AddComponent<T>(target) : target.AddComponent<T>();

    private static void EnsureRenderer(GameObject target, bool undo)
    { if (target.GetComponent<CanvasRenderer>() == null) Add<CanvasRenderer>(target, undo); }

    private static void Dirty(Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    private static void MoveGold()
    {
        var root = PrefabUtility.LoadPrefabContents(GoldPath);
        try
        {
            var panel = root.transform.Find("Gold") as RectTransform;
            if (panel == null) throw new System.InvalidOperationException("Gold HUD panel is missing.");
            // Gold's 1280x720 reference is 2/3 of the minimap's 1920x1080 reference.
            // (198 + 300 + 24) * 2/3 = 348; right 48 * 2/3 = 32.
            var position = new Vector2(-32f, -348f);
            if (panel.anchoredPosition != position)
            {
                panel.anchoredPosition = position;
                PrefabUtility.SaveAsPrefabAsset(root, GoldPath);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        return rect;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void Image(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private static TextMeshProUGUI Text(string name, Transform parent, string value, float size, Color color)
    {
        var rect = Rect(name, parent);
        rect.gameObject.AddComponent<CanvasRenderer>();
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Font/NotoSansKR SDF.asset")
            ?? TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }
}
