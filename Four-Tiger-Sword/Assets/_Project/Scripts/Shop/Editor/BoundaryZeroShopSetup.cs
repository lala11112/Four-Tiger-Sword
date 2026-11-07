using System;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>Rebinds the existing shop prefab to the BoundaryZERO authored composition.</summary>
public static class BoundaryZeroShopSetup
{
    public const string PrefabPath = "Assets/_Game/Shop/UI/TemporaryShop.prefab";
    private const string OfferPath = "Assets/_Game/Shop/UI/ShopOffer.prefab";
    private const string ArtRoot = "Assets/_Project/Sprites/UI/BoundaryZERO/";
    private static TMP_FontAsset Font => BoundaryZeroTypography.GetFont();
    private static readonly Color TextColor = new(.91f, .94f, .96f, 1);
    private static readonly Color Muted = new(.57f, .65f, .71f, 1);

    [MenuItem("Tools/Studio Baekho/BoundaryZERO/Apply Shop")]
    public static void Menu() => Debug.Log(Apply());

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Apply shop UI in Edit Mode.");
        if (Font == null) throw new InvalidOperationException("Existing Korean TMP font is missing.");
        var offer = BuildOffer();
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var view = root.GetComponent<ShopView>();
            if (view == null) throw new InvalidOperationException("The existing shop view is missing.");
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 150;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1440); scaler.matchWidthOrHeight = .5f;
            var panel = root.transform.Find("ShopPanel") as RectTransform;
            if (panel == null) throw new InvalidOperationException("The existing ShopPanel is missing.");
            Stretch(panel); var oldShade = panel.GetComponent<UnityEngine.UI.Image>();
            if (oldShade != null) { oldShade.color = Color.clear; oldShade.raycastTarget = false; }
            var modal = Rect("BoundaryModalContent", panel); Stretch(modal);
            // Native missing components can be Unity fake-null values; ?? does not call Unity's null operator.
            var group = modal.GetComponent<CanvasGroup>();
            if (group == null) group = modal.gameObject.AddComponent<CanvasGroup>();
            group.hideFlags = HideFlags.None; group.alpha = 1f; group.interactable = group.blocksRaycasts = true;
            EditorUtility.SetDirty(group); EditorUtility.SetDirty(modal.gameObject);
            var captureRect = Rect("BoundaryBlurBackground", panel); Stretch(captureRect); captureRect.SetAsFirstSibling();
            var capture = captureRect.GetComponent<UnityEngine.UI.RawImage>() ?? captureRect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            capture.raycastTarget = false;
            var shade = Rect("Shade", modal); Stretch(shade);
            Solid(shade, new Color(.015f, .025f, .04f, .42f), true);
            var window = panel.Find("Window") as RectTransform ?? modal.Find("Window") as RectTransform;
            if (window == null) throw new InvalidOperationException("The existing Window is missing.");
            window.SetParent(modal, false); window.SetAsLastSibling();
            var oldLayout = window.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (oldLayout != null) UnityEngine.Object.DestroyImmediate(oldLayout);
            var oldImage = window.GetComponent<UnityEngine.UI.Image>();
            if (oldImage != null) { oldImage.color = Color.clear; oldImage.raycastTarget = false; }
            foreach (Transform child in window) child.gameObject.SetActive(false);
            var oldGenerated = window.Find("BoundaryZERO");
            if (oldGenerated != null) UnityEngine.Object.DestroyImmediate(oldGenerated.gameObject);
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f, .5f);
            window.anchoredPosition = Vector2.zero; window.sizeDelta = new Vector2(1920, 1120);
            var fit = window.GetComponent<BoundaryZeroWindowFit>() ?? window.gameObject.AddComponent<BoundaryZeroWindowFit>();
            fit.Configure(new Vector2(1920, 1120), 1.08f, 24f);
            var generated = Rect("BoundaryZERO", window); Stretch(generated);
            Image("Glass", generated, "Frames/window_wide_glass.png", 0, 0, 1920, 1120);
            Image("Border", generated, "Frames/window_wide_border.png", 0, 0, 1920, 1120);
            Label("Title", generated, "경계 상점", 34, 132, 68, 950, 52);
            var balance = Label("Gold", generated, string.Empty, 24, 1180, 78, 290, 36);
            var health = Label("Health", generated, string.Empty, 22, 1470, 78, 300, 36);
            var close = Button("CloseButton", generated, string.Empty, 1806, 67, 44, 43, out _, false);
            (close.targetGraphic as UnityEngine.UI.Image).sprite = Art("Controls/close_button.png");
            close.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
            Image("Cross", close.transform, "Controls/close_cross.png", 10, 9, 24, 24);
            Label("Key", close.transform, "ESC", 13, -2, 48, 48, 22).alignment = TextAlignmentOptions.Center;
            var content = Rect("Content", generated); Place(content, 73, 171, 1774, 860);
            var buyTab = Tab("BuyTab", content, "구매", 0, 0, 135, 57);
            var sellTab = Tab("SellTab", content, "판매", 160, 0, 135, 57);
            var fuseTab = Tab("FuseTab", content, "합성", 320, 0, 135, 57);
            var toggle = Button("InventoryToggle", content, "소지품 접기 · Tab", 1514, 0, 260, 57, out var toggleLabel);

            var stock = Rect("ShopStockPanel", content); Place(stock, 0, 95, 550, 540);
            Label("Title", stock, "상인의 물품", 25, 0, 0, 540, 40);
            var stockFilters = Filters("Filters", stock, 60, 550);
            var stockScroll = Scroll("ShopStockScroll", stock, 0, 115, 550, 425, out var stockGrid);
            var stockEmpty = Label("Empty", stockScroll.viewport, string.Empty, 22, 12, 100, 520, 100);
            stockEmpty.alignment = TextAlignmentOptions.Center;
            stockEmpty.rectTransform.anchorMax = new Vector2(1, 1); stockEmpty.rectTransform.sizeDelta = new Vector2(-24, 100);
            var owned = Rect("ShopInventoryPanel", content); Place(owned, 1184, 95, 590, 714);
            var ownedTitle = Label("Title", owned, "소지품", 25, 0, 0, 580, 40);
            var ownedFilters = Filters("Filters", owned, 58, 590);
            var ownedScroll = Scroll("ShopOwnedScroll", owned, 0, 118, 590, 596, out var ownedGrid);
            var ownedEmpty = Label("Empty", ownedScroll.viewport, string.Empty, 22, 12, 100, 560, 100);
            ownedEmpty.alignment = TextAlignmentOptions.Center;
            ownedEmpty.rectTransform.anchorMax = new Vector2(1, 1); ownedEmpty.rectTransform.sizeDelta = new Vector2(-24, 100);

            var detail = Rect("ShopTradeDetail", content); Place(detail, 590, 95, 550, 714);
            var detailBackground = Image("Background", detail, "Frames/detail_panel.png", 0, 0, 550, 714); Stretch(detailBackground.rectTransform);
            var detailIcon = Image("OriginalItemIcon", detail, null, 125, 10, 300, 300); detailIcon.preserveAspect = true;
            var detailTitle = Label("ItemName", detail, string.Empty, 28, 28, 376, 494, 44); detailTitle.alignment = TextAlignmentOptions.Center;
            var detailMeta = Label("ItemGrade", detail, string.Empty, 20, 28, 420, 494, 32); detailMeta.color = Muted; detailMeta.alignment = TextAlignmentOptions.Center;
            Label("EffectTitle", detail, "효과", 20, 28, 452, 350, 32).color = Muted;
            var detailBody = BodyScroll(detail);
            var detailPrice = Label("Price", detail, string.Empty, 23, 28, 596, 494, 36);
            var detailAction = Button("DetailAction", detail, string.Empty, 145, 650, 260, 56, out var detailActionLabel);

            var services = Rect("Services", content); Place(services, 0, 658, 550, 148);
            var healingCost = Label("HealingCost", services, string.Empty, 21, 0, 0, 550, 42); healingCost.color = Muted;
            var heal = Button("HealingButton", services, "체력 회복", 0, 90, 550, 56, out var healingLabel, false);
            var fusion = Rect("SynthesisResult", content); Place(fusion, 1230, 95, 544, 714);
            var fusionBg = Image("Background", fusion, "Frames/synthesis_panel.png", 0, 0, 544, 714); Stretch(fusionBg.rectTransform);
            Label("Title", fusion, "합성 재료", 26, 24, 10, 496, 40);
            var first = Slot("FirstSlot", fusion, 40, out var firstLabel, out var firstIcon);
            var second = Slot("SecondSlot", fusion, 296, out var secondLabel, out var secondIcon);
            var odds = Label("FusionOdds", fusion, string.Empty, 21, 28, 300, 488, 84); odds.alignment = TextAlignmentOptions.Center;
            var result = Rect("FusionResult", fusion); Place(result, 28, 400, 488, 210);
            var resultIcon = Image("OriginalResultIcon", result, null, 174, 0, 140, 140); resultIcon.preserveAspect = true;
            var resultLabel = Label("ResultLabel", result, string.Empty, 23, 0, 146, 488, 72); resultLabel.alignment = TextAlignmentOptions.Center;
            var fuse = Button("FuseButton", fusion, "합성하기", 122, 650, 300, 56, out _);
            var feedback = Label("Feedback", content, string.Empty, 21, 0, 816, 1774, 40); feedback.color = Muted;
            var prompt = root.transform.Find("InteractionPrompt") as RectTransform;
            if (prompt == null) prompt = Rect("InteractionPrompt", root.transform);
            foreach (Transform child in prompt) child.gameObject.SetActive(false);
            prompt.anchorMin = prompt.anchorMax = prompt.pivot = new Vector2(.5f, .5f);
            prompt.anchoredPosition = new Vector2(0, -337); prompt.sizeDelta = new Vector2(192, 54);
            var promptImage = Solid(prompt, Color.white, false); promptImage.sprite = Art("HUD/interaction_plate.png");
            var promptLabel = Label("BoundaryLabel", prompt, "[F]  상점", 22, 0, 0, 192, 54); promptLabel.gameObject.SetActive(true);
            promptLabel.alignment = TextAlignmentOptions.Center;
            var backdrop = root.GetComponent<BoundaryZeroBackdrop>() ?? root.AddComponent<BoundaryZeroBackdrop>();
            backdrop.Configure(group, capture);
            view.Configure(panel.gameObject, prompt.gameObject, balance, health, feedback, stockScroll.content, offer, close);
            view.ConfigureTabs(new ShopView.TabControls
            {
                BuyTab = buyTab, SellTab = sellTab, FuseTab = fuseTab, FuseButton = fuse,
                FirstSlot = first, SecondSlot = second, DetailsPanel = detail.gameObject,
                SalePanel = detail.gameObject, FusionPanel = fusion.gameObject, ResultPanel = result.gameObject,
                EmptyMessage = ownedEmpty, SaleTitle = detailTitle, SaleDescription = detailBody, SalePrice = detailPrice,
                SaleIcon = detailIcon, FirstLabel = firstLabel, SecondLabel = secondLabel,
                FirstIcon = firstIcon, SecondIcon = secondIcon, Odds = odds, ResultLabel = resultLabel, ResultIcon = resultIcon, Scroll = ownedScroll
            });
            view.ConfigureBoundary(new ShopView.BoundaryControls
            {
                Content = content, StockPanel = stock, OwnedPanel = owned, DetailPanel = detail, FusionPanel = fusion,
                ServicePanel = services, StockScroll = stockScroll, OwnedScroll = ownedScroll, StockGrid = stockGrid, OwnedGrid = ownedGrid,
                InventoryToggle = toggle, InventoryToggleLabel = toggleLabel, DetailAction = detailAction, HealingButton = heal,
                StockFilters = stockFilters, OwnedFilters = ownedFilters, DetailTitle = detailTitle, DetailMeta = detailMeta,
                DetailBody = detailBody, DetailPrice = detailPrice, DetailIcon = detailIcon, DetailActionLabel = detailActionLabel,
                HealingCost = healingCost, HealingLabel = healingLabel, StockEmpty = stockEmpty, OwnedEmpty = ownedEmpty, OwnedTitle = ownedTitle,
                TabNormal = Art("Controls/tab_normal.png"), TabSelected = Art("Controls/tab_selected.png"), Backdrop = backdrop
            });
            result.gameObject.SetActive(false); panel.gameObject.SetActive(false); prompt.gameObject.SetActive(false);
            EditorUtility.SetDirty(backdrop);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            var savedBackdrop = saved.GetComponent<BoundaryZeroBackdrop>();
            var savedBinding = new SerializedObject(savedBackdrop).FindProperty("modalContent");
            if (savedBinding.objectReferenceValue == null)
                throw new InvalidOperationException("The saved shop backdrop is missing its sharp-content CanvasGroup.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return "BoundaryZERO shop applied: separate stock/owned grids, shared details, explicit trades and two-material fusion; original icons retained.";
    }

    private static ShopOfferView BuildOffer()
    {
        var root = PrefabUtility.LoadPrefabContents(OfferPath);
        try
        {
            foreach (Transform child in root.transform) child.gameObject.SetActive(false);
            foreach (var layout in root.GetComponents<UnityEngine.UI.LayoutGroup>()) UnityEngine.Object.DestroyImmediate(layout);
            var old = root.transform.Find("BoundaryZERO"); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var host = Rect("BoundaryZERO", root.transform); Stretch(host);
            var background = Solid(root.transform as RectTransform, Color.white, true);
            background.sprite = Art("Cards/card_172x142_normal.png"); background.type = UnityEngine.UI.Image.Type.Sliced;
            var button = root.GetComponent<UnityEngine.UI.Button>() ?? root.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = background; button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
            var colors = button.colors; colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f); colors.pressedColor = new Color(.8f, .8f, .8f);
            colors.disabledColor = new Color(.7f, .7f, .7f); button.colors = colors;
            var icon = Image("OriginalItemIcon", host, null, 0, 8, 166, 166); icon.preserveAspect = true;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(.5f, 1); icon.rectTransform.pivot = new Vector2(.5f, 1);
            icon.rectTransform.anchoredPosition = new Vector2(0, -8);
            var name = Label("Name", host, string.Empty, 24, 8, 174, 253, 36); name.alignment = TextAlignmentOptions.Center;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.rectTransform.anchorMax = new Vector2(1, 1); name.rectTransform.sizeDelta = new Vector2(-16, 36);
            var grade = Label("Grade", host, string.Empty, 18, 8, 210, 253, 28); grade.alignment = TextAlignmentOptions.Center; grade.color = Muted;
            grade.textWrappingMode = TextWrappingModes.NoWrap;
            grade.rectTransform.anchorMax = new Vector2(1, 1); grade.rectTransform.sizeDelta = new Vector2(-16, 28);
            var badge = Image("Element", host, "Icons/element_fire.png", 0, 10, 20, 20);
            badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = badge.rectTransform.pivot = new Vector2(1, 1);
            badge.rectTransform.anchoredPosition = new Vector2(-12, -12);
            var empty = Label("LegacyEmpty", host, string.Empty, 1, 0, 0, 1, 1); empty.gameObject.SetActive(false);
            var view = root.GetComponent<ShopOfferView>();
            view.Configure(icon, name, empty, empty, empty, button);
            var elements = new[] { Art("Icons/element_fire.png"), Art("Icons/element_water.png"), Art("Icons/element_wood.png"), Art("Icons/element_metal.png"), Art("Icons/element_earth.png") };
            view.ConfigureCardVisuals(background, Art("Cards/card_172x142_normal.png"), Art("Cards/card_172x142_selected.png"), grade, badge, elements);
            PrefabUtility.SaveAsPrefabAsset(root, OfferPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return AssetDatabase.LoadAssetAtPath<ShopOfferView>(OfferPath);
    }

    private static UnityEngine.UI.Button Slot(string name, Transform parent, float x, out TMP_Text label, out UnityEngine.UI.Image icon)
    {
        var button = Button(name, parent, string.Empty, x, 65, 208, 218, out label, false);
        (button.targetGraphic as UnityEngine.UI.Image).sprite = Art("Cards/card_172x142_normal.png");
        icon = Image("OriginalItemIcon", button.transform, null, 34, 10, 140, 140); icon.preserveAspect = true;
        Place(label.rectTransform, 8, 153, 192, 58); label.fontSize = 18;
        return button;
    }

    private static TMP_Text BodyScroll(Transform parent)
    {
        var root = Rect("EffectScroll", parent); Place(root, 28, 486, 494, 98);
        var scroll = root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>(); scroll.horizontal = false;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
        var viewport = Rect("Viewport", root); Stretch(viewport); Solid(viewport, Color.white, true);
        viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
        var text = Label("EffectText", viewport, string.Empty, 21, 0, 0, 494, 112);
        text.rectTransform.anchorMax = new Vector2(1, 1); text.rectTransform.sizeDelta = new Vector2(0, 112);
        text.alignment = TextAlignmentOptions.TopLeft; text.overflowMode = TextOverflowModes.Overflow;
        text.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport; scroll.content = text.rectTransform;
        return text;
    }

    private static UnityEngine.UI.ScrollRect Scroll(string name, Transform parent, float x, float y, float w, float h, out UnityEngine.UI.GridLayoutGroup grid)
    {
        var root = Rect(name, parent); Place(root, x, y, w, h);
        var scroll = root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        scroll.horizontal = false; scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 48;
        var viewport = Rect("Viewport", root); Stretch(viewport); Solid(viewport, Color.white, true);
        viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
        var content = Rect("Content", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
        grid = content.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 2;
        grid.cellSize = new Vector2((w - 12) * .5f, 238); grid.spacing = new Vector2(12, 12);
        content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport; scroll.content = content; return scroll;
    }

    private static UnityEngine.UI.Button[] Filters(string name, Transform parent, float y, float width)
    {
        var row = Rect(name, parent); Place(row, 0, y, width, 42); row.anchorMax = new Vector2(1, 1); row.sizeDelta = new Vector2(0, 42);
        var layout = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 8; layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = true;
        string[] names = { "전체", "화", "토", "금", "수", "목" }; var result = new UnityEngine.UI.Button[6];
        for (int i = 0; i < result.Length; i++) result[i] = Tab("Filter" + i, row, names[i], 0, 0, 80, 42);
        return result;
    }

    private static Sprite Art(string relative)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtRoot + relative);
        if (sprite == null) throw new InvalidOperationException("Missing BoundaryZERO sprite: " + relative);
        return sprite;
    }
    private static RectTransform Rect(string name, Transform parent)
    {
        var existing = parent != null ? parent.Find(name) as RectTransform : null;
        if (existing != null) { existing.gameObject.SetActive(true); return existing; }
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); return rect;
    }
    private static void Place(RectTransform rect, float x, float y, float width, float height)
    { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static UnityEngine.UI.Image Solid(RectTransform rect, Color color, bool raycast)
    { var image = rect.GetComponent<UnityEngine.UI.Image>() ?? rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = raycast; return image; }
    private static UnityEngine.UI.Image Image(string name, Transform parent, string sprite, float x, float y, float w, float h)
    { var rect = Rect(name, parent); Place(rect, x, y, w, h); var image = Solid(rect, sprite != null ? Color.white : Color.clear, false);
        image.sprite = sprite != null ? Art(sprite) : null; image.type = image.sprite != null && image.sprite.border != Vector4.zero ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple; return image; }
    private static TMP_Text Label(string name, Transform parent, string text, float size, float x, float y, float w, float h)
    { var rect = Rect(name, parent); Place(rect, x, y, w, h); var label = rect.GetComponent<TMP_Text>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = Font; label.fontSharedMaterial = BoundaryZeroTypography.GetMaterial();
        label.fontSize = size; label.color = TextColor; label.raycastTarget = false; label.richText = false;
        label.text = text; label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Ellipsis; label.alignment = TextAlignmentOptions.MidlineLeft; return label; }
    private static UnityEngine.UI.Button Button(string name, Transform parent, string text, float x, float y, float w, float h, out TMP_Text label, bool primary = true)
    { var rect = Rect(name, parent); Place(rect, x, y, w, h); var image = Solid(rect, Color.white, true);
        string prefix = primary ? "Controls/button_primary_" : "Controls/button_secondary_";
        image.sprite = Art(prefix + "normal.png"); image.type = UnityEngine.UI.Image.Type.Sliced;
        var button = rect.GetComponent<UnityEngine.UI.Button>() ?? rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
        button.transition = UnityEngine.UI.Selectable.Transition.SpriteSwap; button.spriteState = new UnityEngine.UI.SpriteState
        { highlightedSprite = Art(prefix + "hover.png"), pressedSprite = Art(prefix + "pressed.png"), disabledSprite = Art(prefix + "disabled.png") };
        label = Label("Label", rect, text, 22, 4, 4, w - 8, h - 8); label.alignment = TextAlignmentOptions.Center;
        Stretch(label.rectTransform); label.rectTransform.offsetMin = new Vector2(8, 4); label.rectTransform.offsetMax = new Vector2(-8, -4);
        return button; }
    private static UnityEngine.UI.Button Tab(string name, Transform parent, string text, float x, float y, float w, float h)
    { var button = Button(name, parent, text, x, y, w, h, out _, false);
        (button.targetGraphic as UnityEngine.UI.Image).sprite = Art("Controls/tab_normal.png"); button.transition = UnityEngine.UI.Selectable.Transition.None; return button; }
}
