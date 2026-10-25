using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Installs one explicitly bound minimap without rebuilding the existing HUD.</summary>
public static class DungeonMinimapSetup
{
    public const string PrefabPath = "Assets/_Project/Prefabs/UI/DungeonMinimap.prefab";
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
        if (presenter == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Install dungeon minimap");
            presenter = instance.GetComponent<DungeonMinimapPresenter>();
        }
        Undo.RecordObject(presenter, "Bind dungeon minimap");
        presenter.Configure(run, presenter.GetComponent<DungeonMinimapView>());
        PrefabUtility.RecordPrefabInstancePropertyModifications(presenter);
        EditorUtility.SetDirty(presenter);
        MoveGold();
        EditorSceneManager.MarkSceneDirty(scene);
        return "Installed one dungeon minimap; form HUD retained, gold placed below map. Scene remains open.";
    }

    private static GameObject EnsurePrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null) return existing;
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
            root.AddComponent<DungeonMinimapPresenter>().Configure(null, view);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
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

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }
}
