using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Idempotent installer. Authored prefabs/settings are retained on subsequent runs.</summary>
public static class ShopSetup
{
    public const string Folder = "Assets/_Game/Shop";
    private static TMP_FontAsset Font => BoundaryZeroTypography.GetFont();
    private static readonly Color Ink = new(.055f, .075f, .10f, 1f);
    private static readonly Color Card = new(.10f, .13f, .17f, 1f);
    private static readonly Color Accent = new(.76f, .58f, .29f, 1f);

    [MenuItem("Tools/Studio Baekho/Shop/Install In Game Scene")]
    public static void InstallMenu() => Debug.Log(Install());

    public static string Install()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before installing the shop.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Project/Scenes/Game Scene.unity") throw new InvalidOperationException("Open Game Scene first.");
        var run = UnityEngine.Object.FindFirstObjectByType<RunManager>();
        if (run == null || run.player == null || run.definition == null || Font == null)
            throw new InvalidOperationException("RunManager, player, run definition or Korean font is missing.");
        // Preserve even unsaved authoring changes before adding the shop.
        string backupFolder = System.IO.Path.GetFullPath("ShopIntegration/Backups");
        System.IO.Directory.CreateDirectory(backupFolder);
        EditorSceneManager.SaveScene(scene, System.IO.Path.Combine(backupFolder,
            "GameScene_beforeShop_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity"), true);
        EnsureFolder(Folder); EnsureFolder(Folder + "/UI"); EnsureFolder(Folder + "/Materials");
        var economy = AssetDatabase.LoadAssetAtPath<EconomySettingsSO>(Folder + "/EconomySettings.asset");
        if (economy == null)
        {
            economy = ScriptableObject.CreateInstance<EconomySettingsSO>();
            AssetDatabase.CreateAsset(economy, Folder + "/EconomySettings.asset");
        }
        string rooms = ShopRoomSetup.Install(run.definition);
        var shopView = GetShopView();
        var goldView = GetGoldView();
        var counter = GetCounter();
        var service = run.GetComponent<StageShopService>() ?? Undo.AddComponent<StageShopService>(run.gameObject);
        Undo.RecordObject(service, "Configure stage shops");
        if (service.settings == null) service.settings = economy;
        if (service.shopViewPrefab == null) service.shopViewPrefab = shopView;
        if (service.goldViewPrefab == null) service.goldViewPrefab = goldView;
        if (service.counterPrefab == null) service.counterPrefab = counter;
        if (run.player.GetComponent<PlayerWallet>() == null) Undo.AddComponent<PlayerWallet>(run.player.gameObject);
        var eventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (eventSystems.Length == 0)
        {
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(events, "Add shop UI event system");
        }
        else if (eventSystems.Length > 1) throw new InvalidOperationException("Multiple EventSystems already exist; resolve them before saving shop setup.");
        PrefabUtility.RecordPrefabInstancePropertyModifications(service);
        EditorUtility.SetDirty(service);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return rooms + " Shop UI, wallet and reward service connected; existing run order preserved.";
    }

    private static ShopOfferView GetOfferView()
    {
        string path = Folder + "/UI/ShopOffer.prefab";
        var saved = AssetDatabase.LoadAssetAtPath<ShopOfferView>(path);
        if (saved != null) return saved;
        var root = Rect("ShopOffer", null);
        try
        {
            root.sizeDelta = new Vector2(808, 126);
            Background(root, Card);
            Element(root, 126);
            var layout = root.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 14); layout.spacing = 18;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            var iconRoot = Rect("Icon", root); Element(iconRoot, 72, 72);
            var icon = Background(iconRoot, Color.white); icon.preserveAspect = true;
            var body = Rect("Description", root);
            var bodySize = Element(body, 96); bodySize.minWidth = bodySize.preferredWidth = 0; bodySize.flexibleWidth = 1;
            var column = Column(body, 3); column.childForceExpandHeight = false;
            var title = Text("Name", body, 23, Color.white); Element(title.rectTransform, 30);
            var description = Text("DescriptionText", body, 17, new Color(.72f, .78f, .83f));
            description.textWrappingMode = TextWrappingModes.Normal;
            description.overflowMode = TextOverflowModes.Ellipsis;
            Element(description.rectTransform, 58);
            var actions = Rect("Purchase", root); Element(actions, 96, 136);
            Column(actions, 9);
            var price = Text("Price", actions, 22, Accent); price.alignment = TextAlignmentOptions.Center;
            Element(price.rectTransform, 31);
            var button = Button("PurchaseButton", actions, "구매", out var actionText); Element(button.transform as RectTransform, 48);
            var view = root.gameObject.AddComponent<ShopOfferView>();
            view.Configure(icon, title, description, price, actionText, button);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
        return AssetDatabase.LoadAssetAtPath<ShopOfferView>(path);
    }

    private static ShopView GetShopView()
    {
        string path = Folder + "/UI/TemporaryShop.prefab";
        var saved = AssetDatabase.LoadAssetAtPath<ShopView>(path);
        if (saved != null)
        {
            if (!saved.HasCurrentTabsLayout) ShopTabsSetup.Upgrade();
            return AssetDatabase.LoadAssetAtPath<ShopView>(path);
        }
        var row = GetOfferView();
        var root = CanvasRoot("TemporaryShop", 150);
        try
        {
            var overlay = Rect("ShopPanel", root.transform); Stretch(overlay);
            Background(overlay, new Color(.012f, .018f, .026f, .86f), true);
            var card = Rect("Window", overlay); card.sizeDelta = new Vector2(900, 620); Background(card, Ink);
            var layout = Column(card, 12); layout.padding = new RectOffset(32, 32, 25, 25);
            var header = Text("Title", card, 34, Accent); header.text = "상점"; Element(header.rectTransform, 48);
            var summary = Rect("Summary", card); Element(summary, 33);
            var summaryLayout = summary.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            summaryLayout.childControlWidth = summaryLayout.childControlHeight = true;
            summaryLayout.childForceExpandWidth = true;
            var balance = Text("Gold", summary, 23, Color.white);
            var health = Text("Health", summary, 20, new Color(.61f, .78f, .72f)); health.alignment = TextAlignmentOptions.MidlineRight;
            var scrollRoot = Rect("OffersScroll", card); var scrollSize = Element(scrollRoot, 360); scrollSize.flexibleHeight = 1;
            var scroll = scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.horizontal = false; scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28;
            var viewport = Rect("Viewport", scrollRoot); Stretch(viewport); Background(viewport, Color.white, true);
            var mask = viewport.gameObject.AddComponent<UnityEngine.UI.Mask>(); mask.showMaskGraphic = false;
            var content = Rect("Content", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
            Column(content, 10);
            var fitter = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            AddScrollbar(scroll, viewport);
            var feedback = Text("Message", card, 17, new Color(.72f, .78f, .83f)); Element(feedback.rectTransform, 28);
            var footer = Rect("Footer", card); Element(footer, 46);
            var footerLayout = footer.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            footerLayout.childControlHeight = footerLayout.childControlWidth = true;
            footerLayout.childForceExpandWidth = false; footerLayout.spacing = 24;
            var paused = Text("PauseHint", footer, 17, new Color(.57f, .65f, .71f)); paused.text = "게임 일시정지 중";
            Element(paused.rectTransform, 46).flexibleWidth = 1;
            var close = Button("CloseButton", footer, "닫기  [Esc]", out _); Element(close.transform as RectTransform, 46, 190);
            var prompt = Rect("InteractionPrompt", root.transform);
            prompt.anchorMin = prompt.anchorMax = prompt.pivot = new Vector2(.5f, 0);
            prompt.anchoredPosition = new Vector2(0, 120); prompt.sizeDelta = new Vector2(300, 52);
            Background(prompt, new Color(.03f, .05f, .07f, .92f));
            var promptText = Text("Label", prompt, 22, Color.white); Stretch(promptText.rectTransform);
            promptText.text = "[F]  상점 이용"; promptText.alignment = TextAlignmentOptions.Center;
            var view = root.AddComponent<ShopView>();
            view.Configure(overlay.gameObject, prompt.gameObject, balance, health, feedback, content, row, close);
            overlay.gameObject.SetActive(false); prompt.gameObject.SetActive(false);
            ShopTabsSetup.ConfigureHierarchy(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        return AssetDatabase.LoadAssetAtPath<ShopView>(path);
    }

    private static GoldHudView GetGoldView()
    {
        string path = Folder + "/UI/GoldHUD.prefab";
        var saved = AssetDatabase.LoadAssetAtPath<GoldHudView>(path);
        if (saved != null) return saved;
        var root = CanvasRoot("GoldHUD", 110);
        try
        {
            var group = root.AddComponent<CanvasGroup>(); group.alpha = 0; group.blocksRaycasts = group.interactable = false;
            var panel = Rect("Gold", root.transform);
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one;
            panel.anchoredPosition = new Vector2(-32, -348); panel.sizeDelta = new Vector2(210, 52);
            Background(panel, new Color(.025f, .04f, .06f, .90f));
            var label = Text("Amount", panel, 26, Accent); Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(16, 0); label.rectTransform.offsetMax = new Vector2(-16, 0);
            label.alignment = TextAlignmentOptions.MidlineRight; label.text = "0 G";
            var view = root.AddComponent<GoldHudView>(); view.Configure(group, label);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        return AssetDatabase.LoadAssetAtPath<GoldHudView>(path);
    }

    private static GameObject GetCounter()
    {
        string path = Folder + "/ShopCounter.prefab";
        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (saved != null) return saved;
        var wood = Material("Counter", new Color(.16f, .22f, .25f));
        var trim = Material("Trim", new Color(.72f, .50f, .18f));
        var root = new GameObject("ShopCounter");
        try
        {
            Cube("Counter", root.transform, new Vector3(0, .5f, 0), new Vector3(1.8f, 1, .9f), wood);
            Cube("Top", root.transform, new Vector3(0, 1.05f, 0), new Vector3(2, .12f, 1.05f), trim);
            Cube("SignPost", root.transform, new Vector3(0, 1.55f, -.25f), new Vector3(.12f, 1.2f, .12f), trim);
            Cube("Sign", root.transform, new Vector3(0, 2.15f, -.25f), new Vector3(1.9f, .70f, .12f), wood);
            var sign = new GameObject("ShopSign", typeof(RectTransform), typeof(TextMeshPro)).GetComponent<TextMeshPro>();
            sign.transform.SetParent(root.transform, false); sign.transform.localPosition = new Vector3(0, 2.15f, -.17f);
            sign.transform.localRotation = Quaternion.Euler(0, 180, 0);
            sign.rectTransform.sizeDelta = new Vector2(1.8f, .65f); sign.font = Font;
            sign.fontSize = 4; sign.text = "상점"; sign.color = new Color(1, .82f, .43f); sign.alignment = TextAlignmentOptions.Center;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static Material Material(string name, Color color)
    {
        string path = Folder + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = color;
        AssetDatabase.CreateAsset(material, path); return material;
    }
    private static void Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = name; cube.transform.SetParent(parent, false);
        cube.transform.localPosition = position; cube.transform.localScale = scale; cube.GetComponent<Renderer>().sharedMaterial = material;
    }
    private static GameObject CanvasRoot(string name, int order)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = order;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f; return root;
    }
    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); return rect;
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static UnityEngine.UI.Image Background(RectTransform rect, Color color, bool raycast = false)
    { var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = raycast; return image; }
    private static TMP_Text Text(string name, Transform parent, float size, Color color)
    {
        var label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>(); label.font = Font; label.fontSize = size;
        label.color = color; label.raycastTarget = false; label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap; return label;
    }
    private static UnityEngine.UI.LayoutElement Element(RectTransform rect, float height, float width = -1)
    {
        var element = rect.gameObject.GetComponent<UnityEngine.UI.LayoutElement>() ?? rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        element.minHeight = element.preferredHeight = height;
        if (width >= 0) { element.minWidth = element.preferredWidth = width; element.flexibleWidth = 0; }
        return element;
    }
    private static void AddScrollbar(UnityEngine.UI.ScrollRect scroll, RectTransform viewport)
    {
        viewport.offsetMax = new Vector2(-18, 0);
        var track = Rect("Scrollbar", scroll.transform);
        track.anchorMin = new Vector2(1, 0); track.anchorMax = Vector2.one;
        track.pivot = Vector2.one; track.sizeDelta = new Vector2(10, 0);
        Background(track, new Color(.13f, .18f, .22f), true);
        var handle = Rect("Handle", track); Stretch(handle);
        var handleImage = Background(handle, new Color(.42f, .52f, .55f), true);
        var bar = track.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
        bar.handleRect = handle; bar.targetGraphic = handleImage;
        bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
    }

    // Targeted correction for the first temporary prefab revision; never regenerates authored UI.
    public static string RepairTemporaryLayout()
    {
        string rowPath = Folder + "/UI/ShopOffer.prefab";
        var row = PrefabUtility.LoadPrefabContents(rowPath);
        try
        {
            var body = row.transform.Find("Description").GetComponent<UnityEngine.UI.LayoutElement>();
            body.minWidth = body.preferredWidth = 0; body.flexibleWidth = 1;
            row.transform.Find("Purchase").GetComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 0;
            row.transform.Find("Icon").GetComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 0;
            PrefabUtility.SaveAsPrefabAsset(row, rowPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(row); }
        string panelPath = Folder + "/UI/TemporaryShop.prefab";
        var panel = PrefabUtility.LoadPrefabContents(panelPath);
        try
        {
            var scroll = panel.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
            scroll.viewport.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            if (scroll.verticalScrollbar == null) AddScrollbar(scroll, scroll.viewport);
            PrefabUtility.SaveAsPrefabAsset(panel, panelPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(panel); }
        string goldPath = Folder + "/UI/GoldHUD.prefab";
        var gold = PrefabUtility.LoadPrefabContents(goldPath);
        try
        {
            ((RectTransform)gold.transform.Find("Gold")).anchoredPosition = new Vector2(-32, -348);
            PrefabUtility.SaveAsPrefabAsset(gold, goldPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(gold); }
        return "Aligned item columns, added a scrollbar and moved gold below the minimap.";
    }
    private static UnityEngine.UI.VerticalLayoutGroup Column(RectTransform rect, float spacing)
    {
        var group = rect.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>(); group.spacing = spacing;
        group.childControlWidth = group.childControlHeight = true; group.childForceExpandWidth = true; group.childForceExpandHeight = false;
        return group;
    }
    private static UnityEngine.UI.Button Button(string name, Transform parent, string text, out TMP_Text label)
    {
        var rect = Rect(name, parent); var image = Background(rect, new Color(.26f, .36f, .39f), true);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
        var colors = button.colors; colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f); colors.disabledColor = new Color(.5f, .5f, .5f, .55f); button.colors = colors;
        label = Text("Label", rect, 20, Color.white); Stretch(label.rectTransform); label.text = text; label.alignment = TextAlignmentOptions.Center;
        return button;
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, split));
        AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
}
