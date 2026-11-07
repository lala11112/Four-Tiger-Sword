using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Authors the world Hub workshop from the existing BoundaryZERO UI assets.</summary>
public static class HubWorkshopSetup
{
    public const string PrefabPath = "Assets/_Project/Prefabs/UI/HubWorkshop.prefab";
    private const string HubPath = "Assets/_Project/Scenes/Hub Scene.unity";
    private const string Art = "Assets/_Project/Sprites/UI/BoundaryZERO/";
    private static readonly Color Ink = new Color32(231, 237, 242, 255);
    private static readonly Color Muted = new Color32(162, 175, 188, 255);
    private static readonly Color Pink = new Color32(225, 177, 192, 255);
    private static TMP_FontAsset _font;
    private static Material _fontMaterial;

    [MenuItem("Tools/Studio Baekho/Progression/Set Up Hub Workshop")]
    private static void SetupMenu() => Debug.Log(Setup());

    public static string Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Set up the Hub workshop in Edit Mode.");
        _font = BoundaryZeroTypography.GetFont();
        _fontMaterial = BoundaryZeroTypography.GetMaterial();
        BuildPrefab();
        var hub = SceneManager.GetSceneByPath(HubPath);
        if (!hub.isLoaded) hub = EditorSceneManager.OpenScene(HubPath, OpenSceneMode.Additive);
        var view = Components<HubWorkshopView>(hub).SingleOrDefault();
        if (view == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            view = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, hub)).GetComponent<HubWorkshopView>();
            Undo.RegisterCreatedObjectUndo(view.gameObject, "Add Hub Workshop");
        }
        var player = Components<PlayerController>(hub).Single();
        var world = Components<HubWorldController>(hub).Single();
        var station = hub.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Single(value => value.name == "04_Upgrade");
        var renderers = station.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException("The upgrade station has no renderer bounds.");
        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        Vector3 front = new Vector3(bounds.center.x, station.position.y, bounds.min.z - .55f);
        var interaction = world.GetComponent<HubWorkshopInteraction>();
        if (interaction == null) interaction = Undo.AddComponent<HubWorkshopInteraction>(world.gameObject);
        Undo.RecordObject(interaction, "Connect Hub Workshop");
        var prompt = view.transform.Find("InteractionPrompt").gameObject;
        interaction.Configure(player, station, view, prompt, world, station.InverseTransformPoint(front), 2f);
        EditorUtility.SetDirty(interaction);
        if (PrefabUtility.IsPartOfPrefabInstance(interaction))
            PrefabUtility.RecordPrefabInstancePropertyModifications(interaction);
        view.UI.panel.SetActive(false);
        prompt.SetActive(false);
        string result = Validate();
        EditorSceneManager.MarkSceneDirty(hub);
        if (!EditorSceneManager.SaveScene(hub)) throw new InvalidOperationException("Could not save the Hub workshop scene.");
        return "Hub workshop prefab and station interaction connected. " + result;
    }

    public static string Validate()
    {
        var hub = SceneManager.GetSceneByPath(HubPath);
        if (!hub.isLoaded) throw new InvalidOperationException("Load the Hub Scene before validating its workshop.");
        var view = Components<HubWorkshopView>(hub).Single();
        if (!view.gameObject.activeInHierarchy || view.UI == null || view.UI.panel == null)
            throw new InvalidOperationException("The workshop requires an active root and a configured modal panel.");
        foreach (var field in typeof(HubWorkshopView.References).GetFields())
            if (field.GetValue(view.UI) == null)
                throw new InvalidOperationException("The workshop is missing reference " + field.Name + ".");
        if (view.Entries == null || view.Entries.Length != 3
            || !new[] { StatType.ST_HP, StatType.ST_ATK, StatType.ST_DEF }.All(stat => view.Entries.Count(entry => entry.stat == stat) == 1)
            || view.Entries.Any(entry => entry.selectButton == null || entry.title == null || entry.level == null
                || entry.bonus == null || entry.selectedIndicator == null || entry.icon == null))
            throw new InvalidOperationException("The workshop must have one configured HP, ATK and DEF entry.");
        var canvas = view.GetComponent<Canvas>();
        if (canvas == null || canvas.sortingOrder != 80 || view.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            throw new InvalidOperationException("The workshop Canvas or pointer raycaster is not configured.");
        if (Components<HubWorkshopInteraction>(hub).Count() != 1)
            throw new InvalidOperationException("The Hub must have one workshop interaction controller.");
        if (!Application.isPlaying && (view.IsOpen || view.transform.Find("InteractionPrompt").gameObject.activeSelf))
            throw new InvalidOperationException("The workshop and interaction prompt must start hidden.");
        var fit = view.UI.panel.GetComponentInChildren<BoundaryZeroWindowFit>(true);
        if (fit == null) throw new InvalidOperationException("The workshop is missing its responsive window fit.");
        if (view.GetComponentsInChildren<UnityEngine.UI.Image>(true).Any(image => image.sprite == null
            && image.name != "Modal" && image.name != "Viewport"))
            throw new InvalidOperationException("The workshop contains an image without an authored BoundaryZERO sprite.");
        if (view.UI.statusScroll == null || view.UI.statusScroll.viewport == null || view.UI.statusScroll.content == null
            || view.UI.statusScroll.viewport.GetComponent<UnityEngine.UI.Mask>() == null
            || view.UI.statusScroll.content.GetComponent<UnityEngine.UI.ContentSizeFitter>() == null
            || !view.UI.status.transform.IsChildOf(view.UI.statusScroll.content))
            throw new InvalidOperationException("The workshop status requires a masked, vertically fitted scroll area.");
        return "PASS: three upgrade entries, complete detail bindings, responsive modal, and one station controller.";
    }

    private static void BuildPrefab()
    {
        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        Scene preview = default;
        GameObject root;
        if (existing) root = PrefabUtility.LoadPrefabContents(PrefabPath);
        else
        {
            preview = EditorSceneManager.NewPreviewScene();
            root = new GameObject("HubWorkshop", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(root, preview);
        }
        try
        {
            root.name = "HubWorkshop";
            root.layer = LayerMask.NameToLayer("UI");
            root.SetActive(true);
            var canvas = Ensure<Canvas>(root);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            var scaler = Ensure<UnityEngine.UI.CanvasScaler>(root);
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            Ensure<UnityEngine.UI.GraphicRaycaster>(root);
            var view = Ensure<HubWorkshopView>(root);
            var modal = Child("Modal", root.transform);
            Stretch(modal);
            var dimmer = Ensure<UnityEngine.UI.Image>(modal.gameObject);
            dimmer.sprite = null;
            dimmer.color = new Color(0f, 0f, 0f, .28f);
            dimmer.raycastTarget = true;
            var window = Child("Window", modal);
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f, .5f);
            window.anchoredPosition = Vector2.zero;
            Ensure<BoundaryZeroWindowFit>(window.gameObject).Configure(new Vector2(1536, 900), 1f, 24f);
            Picture("Glass", window, "Frames/window_wide_glass.png", 0, 0, 1536, 900, .90f);
            Hide(window, "Border", "PersistenceHint");
            var ui = new HubWorkshopView.References { panel = modal.gameObject };
            BuildHeader(window, ui);
            var entries = BuildBody(window, ui);
            BuildFooter(window, ui);

            var prompt = Place(Child("InteractionPrompt", root.transform), 0, 0, 330, 60);
            prompt.anchorMin = prompt.anchorMax = prompt.pivot = new Vector2(.5f, 0f);
            prompt.anchoredPosition = new Vector2(0, 70);
            Picture("Plate", prompt, "HUD/interaction_plate.png", 0, 0, 330, 60, .92f);
            Picture("Keycap", prompt, "Controls/keycap_34.png", 14, 12, 36, 36, 1f);
            Text("Key", prompt, "F", 14, 12, 36, 36, 20, Ink, FontStyles.Bold).alignment = TextAlignmentOptions.Center;
            Text("Label", prompt, "조율 공방 · 강화하기", 63, 8, 253, 44, 18, Ink);

            view.Configure(ui, entries);
            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(window);
            modal.gameObject.SetActive(false);
            prompt.gameObject.SetActive(false);
            if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                throw new InvalidOperationException("Could not save the Hub workshop prefab.");
        }
        finally
        {
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void BuildHeader(RectTransform window, HubWorkshopView.References ui)
    {
        var header = Place(Child("Header", window), 64, 64, 1408, 104);
        var row = Place(Child("Row", header), 0, 0, 1408, 80);
        Horizontal(row, 24, TextAnchor.MiddleLeft);
        var heading = Place(Child("Heading", row), 0, 0, 868, 80);
        Fixed(heading, 868, 80);
        Horizontal(heading, 24, TextAnchor.MiddleLeft);
        Move(window, heading, "HeaderIcon");
        var icon = Picture("HeaderIcon", heading, "Icons/red_seal.png", 0, 0, 64, 64, 1f, true);
        Fixed(icon.rectTransform, 64, 64);
        var titles = Place(Child("Titles", heading), 0, 0, 780, 80);
        Fixed(titles, 780, 80);
        Vertical(titles, 2);
        Move(window, titles, "Title", "Subtitle");
        Fixed(Text("Title", titles, "영구 강화", 0, 0, 780, 48, 32, Ink, FontStyles.Bold).rectTransform, 780, 48);
        Fixed(Text("Subtitle", titles, "허브 단말 · 다음 도전을 위한 준비", 0, 0, 780, 30, 18, Muted).rectTransform, 780, 30);

        var currency = Place(Child("Currency", row), 0, 0, 440, 80);
        Fixed(currency, 440, 80);
        Horizontal(currency, 12, TextAnchor.MiddleRight);
        Move(window, currency, "CurrencyIcon", "Balance");
        Fixed(Picture("CurrencyIcon", currency, "Icons/stat_legacycurrency.png", 0, 0, 36, 36, .9f, true).rectTransform, 36, 36);
        ui.balance = Text("Balance", currency, "강화석   —", 0, 0, 392, 48, 24, Ink, FontStyles.Bold);
        Fixed(ui.balance.rectTransform, 392, 48);
        var balanceLayout = ui.balance.GetComponent<UnityEngine.UI.LayoutElement>();
        balanceLayout.minWidth = 0f;
        balanceLayout.preferredWidth = -1f;
        balanceLayout.flexibleWidth = 0f;
        ui.balance.alignment = TextAlignmentOptions.MidlineRight;
        Move(window, row, "Close");
        ui.closeButton = MakeButton("Close", row, "Controls/close_button.png", 0, 0, 52, 52);
        Fixed(ui.closeButton.GetComponent<RectTransform>(), 52, 52);
        Picture("Cross", ui.closeButton.transform, "Controls/close_cross.png", 12, 12, 28, 28, .9f, true);
        Order(row, "Heading", "Currency", "Close");
        Order(heading, "HeaderIcon", "Titles");
        Order(titles, "Title", "Subtitle");
        Order(currency, "CurrencyIcon", "Balance");
        Move(window, header, "HeaderDivider", "HeaderAccent");
        Picture("HeaderDivider", header, "Frames/header_divider.png", 0, 104, 1408, 2, .65f);
        Picture("HeaderAccent", header, "Frames/header_accent.png", 0, 103, 94, 4, .7f);
    }

    private static HubWorkshopView.Entry[] BuildBody(RectTransform window, HubWorkshopView.References ui)
    {
        var body = Place(Child("Body", window), 64, 192, 1408, 460);
        Horizontal(body, 32);
        var left = Place(Child("Entries", body), 0, 0, 536, 460);
        Fixed(left, 536, 460);
        Vertical(left, 0);
        Move(window, left, "EntriesHeading");
        Fixed(Text("EntriesHeading", left, "강화 항목", 0, 0, 536, 28, 18, Muted).rectTransform, 536, 28);
        Spacer(left, "HeadingGap", 16);
        var list = Place(Child("List", left), 0, 0, 536, 404);
        Fixed(list, 536, 404);
        Vertical(list, 16);
        Move(window, list, "Health", "Attack", "Defense");
        var entries = new[]
        {
            BuildEntry(list, "Health", "최대 체력", "Icons/stat_health.png", StatType.ST_HP, true),
            BuildEntry(list, "Attack", "공격력", "Icons/stat_attack.png", StatType.ST_ATK, false),
            BuildEntry(list, "Defense", "방어력", "Icons/element_metal.png", StatType.ST_DEF, false)
        };
        Order(left, "EntriesHeading", "HeadingGap", "List");
        Order(list, "Health", "Attack", "Defense");

        Move(window, body, "Detail");
        var detail = Place(Child("Detail", body), 0, 0, 840, 460);
        Fixed(detail, 840, 460);
        Picture("Background", detail, "Frames/detail_panel.png", 0, 0, 840, 460, .44f);
        var content = Place(Child("Content", detail), 32, 32, 776, 396);
        Vertical(content, 0);
        var titleRow = Place(Child("Heading", content), 0, 0, 776, 96);
        Fixed(titleRow, 776, 96);
        Horizontal(titleRow, 28);
        Move(detail, titleRow, "Icon");
        ui.selectedIcon = Picture("Icon", titleRow, "Icons/stat_health.png", 0, 0, 96, 96, 1f, true);
        Fixed(ui.selectedIcon.rectTransform, 96, 96);
        var titles = Place(Child("Titles", titleRow), 0, 0, 652, 96);
        Fixed(titles, 652, 96);
        Vertical(titles, 8);
        Move(detail, titles, "SelectedTitle", "StatBasis");
        ui.selectedTitle = Text("SelectedTitle", titles, "최대 체력", 0, 0, 652, 48, 32, Ink, FontStyles.Bold);
        Fixed(ui.selectedTitle.rectTransform, 652, 48);
        Fixed(Text("StatBasis", titles, "기본 능력치 대비", 0, 0, 652, 30, 18, Muted).rectTransform, 652, 30);
        Spacer(content, "ComparisonGap", 32);
        Move(detail, content, "Comparison");
        var comparison = Place(Child("Comparison", content), 0, 0, 776, 152);
        Fixed(comparison, 776, 152);
        Picture("Background", comparison, "Frames/synthesis_panel.png", 0, 0, 776, 152, .55f);
        var columns = Place(Child("Columns", comparison), 24, 12, 728, 128);
        Horizontal(columns, 24);
        var current = Place(Child("Current", columns), 0, 0, 304, 128);
        Fixed(current, 304, 128);
        Vertical(current, 4);
        var next = Place(Child("Next", columns), 0, 0, 304, 128);
        Fixed(next, 304, 128);
        Vertical(next, 4);
        Move(comparison, current, "CurrentCaption", "CurrentLevel", "CurrentBonus");
        Move(comparison, next, "NextCaption", "NextLevel", "NextBonus");
        Fixed(Text("CurrentCaption", current, "현재 강화 효과", 0, 0, 304, 30, 18, Muted).rectTransform, 304, 30);
        ui.currentLevel = Text("CurrentLevel", current, "Lv. 00", 0, 0, 304, 30, 18, Muted);
        Fixed(ui.currentLevel.rectTransform, 304, 30);
        ui.currentBonus = Text("CurrentBonus", current, "+0%", 0, 0, 304, 60, 36, Ink, FontStyles.Bold);
        Fixed(ui.currentBonus.rectTransform, 304, 60);
        Fixed(Text("NextCaption", next, "다음 강화 효과", 0, 0, 304, 30, 18, Muted).rectTransform, 304, 30);
        ui.nextLevel = Text("NextLevel", next, "Lv. 01", 0, 0, 304, 30, 18, Muted);
        Fixed(ui.nextLevel.rectTransform, 304, 30);
        ui.nextBonus = Text("NextBonus", next, "+5%", 0, 0, 304, 60, 36, Pink, FontStyles.Bold);
        Fixed(ui.nextBonus.rectTransform, 304, 60);
        Move(comparison, columns, "Arrow");
        var arrow = Text("Arrow", columns, "→", 0, 0, 72, 128, 32, Muted);
        Fixed(arrow.rectTransform, 72, 128);
        arrow.alignment = TextAlignmentOptions.Center;
        Order(columns, "Current", "Arrow", "Next");
        Order(current, "CurrentCaption", "CurrentLevel", "CurrentBonus");
        Order(next, "NextCaption", "NextLevel", "NextBonus");
        Spacer(content, "GainGap", 26);
        var gainRow = Place(Child("GainRow", content), 0, 0, 776, 32);
        Fixed(gainRow, 776, 32);
        Horizontal(gainRow, 20);
        Move(detail, gainRow, "GainHeading", "Gain");
        Fixed(Text("GainHeading", gainRow, "증가량", 0, 0, 156, 32, 20, Muted).rectTransform, 156, 32);
        ui.gain = Text("Gain", gainRow, "단계마다 기본 능력치 +5%", 0, 0, 600, 32, 20, Ink);
        Fixed(ui.gain.rectTransform, 600, 32);
        ui.gain.alignment = TextAlignmentOptions.MidlineRight;
        Spacer(content, "BalanceGap", 18);
        var balanceRow = Place(Child("BalanceRow", content), 0, 0, 776, 32);
        Fixed(balanceRow, 776, 32);
        Horizontal(balanceRow, 20);
        Move(detail, balanceRow, "BalanceAfterHeading", "BalanceAfter");
        Fixed(Text("BalanceAfterHeading", balanceRow, "구매 후 잔액", 0, 0, 156, 32, 20, Muted).rectTransform, 156, 32);
        ui.balanceAfter = Text("BalanceAfter", balanceRow, "강화석   —", 0, 0, 600, 32, 20, Ink);
        Fixed(ui.balanceAfter.rectTransform, 600, 32);
        ui.balanceAfter.alignment = TextAlignmentOptions.MidlineRight;
        Order(titleRow, "Icon", "Titles");
        Order(titles, "SelectedTitle", "StatBasis");
        Order(gainRow, "GainHeading", "Gain");
        Order(balanceRow, "BalanceAfterHeading", "BalanceAfter");
        Order(content, "Heading", "ComparisonGap", "Comparison", "GainGap", "GainRow", "BalanceGap", "BalanceRow");
        Order(body, "Entries", "Detail");
        return entries;
    }

    private static void BuildFooter(RectTransform window, HubWorkshopView.References ui)
    {
        var footer = Place(Child("Footer", window), 64, 676, 1408, 160);
        Move(window, footer, "FooterDivider");
        Picture("FooterDivider", footer, "Frames/header_divider.png", 0, 0, 1408, 2, .65f);
        var scrollRoot = Place(Child("StatusScroll", footer), 0, 20, 1408, 60);
        var viewport = Child("Viewport", scrollRoot);
        Stretch(viewport);
        var maskImage = Ensure<UnityEngine.UI.Image>(viewport.gameObject);
        maskImage.sprite = null;
        maskImage.color = Color.white;
        maskImage.raycastTarget = true;
        var mask = Ensure<UnityEngine.UI.Mask>(viewport.gameObject);
        mask.showMaskGraphic = false;
        var content = Child("Content", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        content.localScale = Vector3.one;
        Vertical(content, 0);
        var fitter = Ensure<UnityEngine.UI.ContentSizeFitter>(content.gameObject);
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        Move(window, content, "Status");
        ui.status = Text("Status", content, "강화 항목을 선택해 내용을 확인하세요.", 0, 0, 1408, 60, 20, Muted);
        var statusLayout = Ensure<UnityEngine.UI.LayoutElement>(ui.status.gameObject);
        statusLayout.ignoreLayout = false;
        statusLayout.minWidth = statusLayout.preferredWidth = statusLayout.flexibleWidth = -1f;
        statusLayout.minHeight = 30f;
        statusLayout.preferredHeight = -1f;
        statusLayout.flexibleHeight = 0f;
        ui.status.alignment = TextAlignmentOptions.TopLeft;
        ui.status.textWrappingMode = TextWrappingModes.Normal;
        ui.status.overflowMode = TextOverflowModes.Overflow;
        ui.status.raycastTarget = true;
        ui.statusScroll = Ensure<UnityEngine.UI.ScrollRect>(scrollRoot.gameObject);
        ui.statusScroll.viewport = viewport;
        ui.statusScroll.content = content;
        ui.statusScroll.horizontal = false;
        ui.statusScroll.vertical = true;
        ui.statusScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        ui.statusScroll.inertia = false;
        ui.statusScroll.scrollSensitivity = 28f;
        ui.statusScroll.horizontalScrollbar = null;
        ui.statusScroll.verticalScrollbar = null;
        ui.statusScroll.verticalNormalizedPosition = 1f;
        var actions = Place(Child("Actions", footer), 0, 96, 1408, 64);
        Horizontal(actions, 24, TextAnchor.MiddleLeft);
        Move(window, actions, "DismissHint");
        var help = Text("DismissHint", actions, "Esc 닫기 · 항목을 선택해 강화 내용을 확인하세요.",
            0, 0, 700, 64, 18, Muted);
        Fixed(help.rectTransform, 700, 64);
        help.textWrappingMode = TextWrappingModes.Normal;
        var costRow = Place(Child("CostRow", actions), 0, 0, 420, 64);
        Fixed(costRow, 420, 64);
        Horizontal(costRow, 20, TextAnchor.MiddleRight);
        Move(window, costRow, "CostHeading", "Cost");
        Fixed(Text("CostHeading", costRow, "비용", 0, 0, 52, 44, 20, Muted).rectTransform, 52, 44);
        ui.cost = Text("Cost", costRow, "강화석   —", 0, 0, 348, 44, 24, Ink, FontStyles.Bold);
        Fixed(ui.cost.rectTransform, 348, 44);
        ui.cost.alignment = TextAlignmentOptions.MidlineRight;
        Move(window, actions, "Purchase");
        ui.purchaseButton = MakeButton("Purchase", actions, "Controls/button_primary_normal.png", 0, 0, 240, 64);
        Fixed(ui.purchaseButton.GetComponent<RectTransform>(), 240, 64);
        ui.purchaseButton.transition = UnityEngine.UI.Selectable.Transition.SpriteSwap;
        ui.purchaseButton.spriteState = new UnityEngine.UI.SpriteState
        {
            highlightedSprite = LoadSprite("Controls/button_primary_hover.png"),
            selectedSprite = LoadSprite("Controls/button_primary_hover.png"),
            pressedSprite = LoadSprite("Controls/button_primary_pressed.png"),
            disabledSprite = LoadSprite("Controls/button_primary_disabled.png")
        };
        ui.purchaseLabel = Text("Label", ui.purchaseButton.transform, "강화하기", 16, 8, 208, 48, 24, Ink, FontStyles.Bold);
        ui.purchaseLabel.alignment = TextAlignmentOptions.Center;
        Order(actions, "DismissHint", "CostRow", "Purchase");
        Order(costRow, "CostHeading", "Cost");
    }

    private static HubWorkshopView.Entry BuildEntry(Transform parent, string name, string title, string icon,
        StatType stat, bool selected)
    {
        var button = MakeButton(name, parent, "Cards/card_296x80_normal.png", 0, 0, 536, 124);
        Fixed(button.GetComponent<RectTransform>(), 536, 124);
        button.targetGraphic.color = new Color(1f, 1f, 1f, .48f);
        var selectedImage = Picture("Selected", button.transform, "Cards/card_296x80_selected.png", 0, 0, 536, 124, .68f);
        selectedImage.gameObject.SetActive(selected);
        var row = Place(Child("Content", button.transform), 24, 20, 488, 84);
        Horizontal(row, 22, TextAnchor.MiddleLeft);
        Move(button.transform, row, "IconFrame", "Bonus");
        var frame = Picture("IconFrame", row, "Controls/keycap_50.png", 0, 0, 64, 64, .72f);
        Fixed(frame.rectTransform, 64, 64);
        Move(button.transform, frame.transform, "Icon");
        Picture("Icon", frame.transform, icon, 8, 8, 48, 48, 1f, true);
        var names = Place(Child("Names", row), 0, 0, 220, 84);
        Fixed(names, 220, 84);
        Vertical(names, 8);
        Move(button.transform, names, "Title", "Level");
        var label = Text("Title", names, title, 0, 0, 220, 40, 24, Ink, FontStyles.Bold);
        Fixed(label.rectTransform, 220, 40);
        var level = Text("Level", names, "Lv. 00 / 20", 0, 0, 220, 32, 18, Muted);
        Fixed(level.rectTransform, 220, 32);
        var bonus = Text("Bonus", row, "+0%", 0, 0, 160, 60, 32, Ink, FontStyles.Bold);
        Fixed(bonus.rectTransform, 160, 60);
        bonus.alignment = TextAlignmentOptions.MidlineRight;
        Order(row, "IconFrame", "Names", "Bonus");
        Order(names, "Title", "Level");
        selectedImage.transform.SetAsFirstSibling();
        row.SetAsLastSibling();
        return new HubWorkshopView.Entry
        {
            stat = stat, selectButton = button, title = label, level = level, bonus = bonus,
            selectedIndicator = selectedImage.gameObject, icon = LoadSprite(icon)
        };
    }

    private static void Horizontal(RectTransform target, float spacing, TextAnchor alignment = TextAnchor.UpperLeft)
    {
        var layout = Ensure<UnityEngine.UI.HorizontalLayoutGroup>(target.gameObject);
        layout.padding = new RectOffset();
        layout.spacing = spacing;
        layout.childAlignment = alignment;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        layout.childScaleWidth = layout.childScaleHeight = false;
    }

    private static void Vertical(RectTransform target, float spacing)
    {
        var layout = Ensure<UnityEngine.UI.VerticalLayoutGroup>(target.gameObject);
        layout.padding = new RectOffset();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childScaleWidth = layout.childScaleHeight = false;
    }

    private static void Fixed(RectTransform rect, float width, float height)
    {
        var size = Ensure<UnityEngine.UI.LayoutElement>(rect.gameObject);
        size.ignoreLayout = false;
        size.minWidth = size.preferredWidth = width;
        size.minHeight = size.preferredHeight = height;
        size.flexibleWidth = size.flexibleHeight = 0f;
    }

    private static void Spacer(Transform parent, string name, float height)
    {
        var rect = Place(Child(name, parent), 0, 0, 0, height);
        Fixed(rect, 0, height);
    }

    private static void Move(Transform source, Transform destination, params string[] names)
    {
        if (source == destination) return;
        foreach (string name in names)
        {
            var existing = source.Find(name);
            if (existing == null) continue;
            var alreadyMoved = destination.Find(name);
            if (alreadyMoved == null) existing.SetParent(destination, false);
            else if (alreadyMoved != existing) existing.gameObject.SetActive(false);
        }
    }

    private static void Hide(Transform parent, params string[] names)
    {
        foreach (string name in names)
        {
            var child = parent.Find(name);
            if (child != null) child.gameObject.SetActive(false);
        }
    }

    private static void Order(Transform parent, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            var child = parent.Find(names[i]);
            if (child != null) child.SetSiblingIndex(i);
        }
    }

    private static System.Collections.Generic.IEnumerable<T> Components<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

    private static T Ensure<T>(GameObject target) where T : Component
    {
        var component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static RectTransform Child(string name, Transform parent)
    {
        var child = parent.Find(name) as RectTransform;
        if (child != null)
        {
            child.gameObject.SetActive(true);
            return child;
        }
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private static RectTransform Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static Sprite LoadSprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + path)
        ?? throw new InvalidOperationException("Missing BoundaryZERO sprite: " + path);

    private static UnityEngine.UI.Image Picture(string name, Transform parent, string path, float x, float y,
        float width, float height, float alpha = 1f, bool preserveAspect = false)
    {
        var image = Ensure<UnityEngine.UI.Image>(Place(Child(name, parent), x, y, width, height).gameObject);
        image.sprite = LoadSprite(path);
        image.type = preserveAspect ? UnityEngine.UI.Image.Type.Simple : UnityEngine.UI.Image.Type.Sliced;
        image.preserveAspect = preserveAspect;
        image.color = new Color(1f, 1f, 1f, alpha);
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text Text(string name, Transform parent, string value, float x, float y,
        float width, float height, float size, Color tint, FontStyles style = FontStyles.Normal)
    {
        var text = Ensure<TextMeshProUGUI>(Place(Child(name, parent), x, y, width, height).gameObject);
        text.font = _font;
        text.fontSharedMaterial = _fontMaterial;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = tint;
        text.margin = Vector4.zero;
        text.enableAutoSizing = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static UnityEngine.UI.Button MakeButton(string name, Transform parent, string art,
        float x, float y, float width, float height)
    {
        var image = Picture(name, parent, art, x, y, width, height);
        image.raycastTarget = true;
        var button = Ensure<UnityEngine.UI.Button>(image.gameObject);
        button.targetGraphic = image;
        button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
        colors.selectedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
        colors.pressedColor = new Color(.75f, .75f, .75f, 1f);
        colors.disabledColor = new Color(.55f, .58f, .63f, .8f);
        button.colors = colors;
        return button;
    }
}
