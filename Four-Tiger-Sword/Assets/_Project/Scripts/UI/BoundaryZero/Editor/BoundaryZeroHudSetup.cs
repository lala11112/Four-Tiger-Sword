using System;
using ArpgHud;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Applies BoundaryZERO presentation to the existing, bound HUD and map prefabs.</summary>
public static class BoundaryZeroHudSetup
{
    private const string Art = "Assets/_Project/Sprites/UI/BoundaryZERO/";
    private const string HudPath = "Assets/_Project/Prefabs/UI/PlayerHUD.prefab";
    private const string MiniPath = "Assets/_Project/Prefabs/UI/DungeonMinimap.prefab";
    private const string MapPath = "Assets/_Project/Prefabs/UI/DungeonFullMap.prefab";
    private const string WheelPath = "Assets/_Project/Scripts/UI/Finisher/Prefabs/FinisherWheel.prefab";
    private const string GoldPath = "Assets/_Game/Shop/UI/GoldHUD.prefab";
    private static readonly Color TextColor = new Color(.88f, .92f, .96f, 1f);

    public static string Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Apply BoundaryZERO in Edit Mode.");
        Edit(HudPath, StyleHud);
        Edit(MiniPath, StyleMini);
        Edit(MapPath, StyleMap);
        Edit(WheelPath, StyleWheel);
        Edit(GoldPath, StyleGold);
        return "BoundaryZERO HUD, E/R skills, gold, minimap, full map and finisher wheel applied to existing prefabs.";
    }

    /// <summary>Adds the conditional form skill display without restyling unrelated HUD elements.</summary>
    public static string ApplyChainSkills()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Apply chain skill HUD in Edit Mode.");
        Edit(HudPath, StyleChainSkills);
        return "Fire/water chain readiness and F target are bound on PlayerHUD.";
    }

    /// <summary>Updates only the existing HUD and wheel's form-lock bindings.</summary>
    public static string ApplyFormLocks()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Apply form locks in Edit Mode.");
        Edit(HudPath, StyleFormLocks);
        Edit(WheelPath, BindWheelArtwork);
        return "Form lock labels and finisher artwork are bound on the existing prefabs.";
    }

    /// <summary>Places and styles the minimap stage label without restyling the other HUD prefabs.</summary>
    public static string ApplyMinimapStageLabel()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Apply the minimap stage label in Edit Mode.");
        Edit(MiniPath, StyleMiniStageLabel);
        return "BoundaryZERO chapter/stage label placed inside the minimap's lower-left corner.";
    }

    private static void Edit(string path, Action<GameObject> style)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            style(root);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            if (saved == null)
                throw new InvalidOperationException("Could not save " + path);
            if (path == MapPath && !saved.GetComponent<BoundaryZeroBackdrop>().HasBindings)
                throw new InvalidOperationException("Full-map backdrop bindings were not saved.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void StyleHud(GameObject root)
    {
        Canvas(root);
        var view = root.GetComponent<HudView>();
        if (view == null) throw new InvalidOperationException("The live PlayerHUD has no HudView.");
        var theme = Reference<HudElementTheme>(view, "elementTheme");
        if (theme == null) throw new InvalidOperationException("Preserve the existing E/R element theme.");
        Sprite ringSprite = view.SkillE != null ? Reference<UnityEngine.UI.Image>(view.SkillE, "ringFill")?.sprite : null;
        var legacy = root.transform.Find("HudRoot");
        if (legacy != null) legacy.gameObject.SetActive(false);
        var design = Rect("BoundaryZeroHUD", root.transform, 0, 0, 0, 0);
        Stretch(design);

        var vitals = Rect("Vitals", design, 52, 46, 625, 238);
        Image("Housing", vitals, 0, 0, 625, 202, "HUD/health_housing.png");
        var hp = Image("HealthFill", vitals, 118, 47, 458, 18, "HUD/health_fill.png");
        HorizontalFill(hp);
        Image("RedSeal", vitals, 46, 94, 28, 58, "Icons/red_seal.png");
        var hpLabel = Text("HealthValue", vitals, 126, 110, 320, 30, "", 25);
        // The housing's lower silver rail occupies the sample label baseline. Keep live values clear of it.
        var spTrack = PlainImage("SpTrack", vitals, 118, 187, 458, 6, null);
        spTrack.color = new Color(.12f, .19f, .24f, .8f);
        var spFill = PlainImage("SpFill", vitals, 118, 187, 458, 6, null);
        spFill.color = new Color(.35f, .72f, .8f, 1f);
        HorizontalFill(spFill);
        var spLabel = Text("SpValue", vitals, 126, 203, 380, 26, "", 18);
        var crosshair = Image("Crosshair", design, 0, 0, 20, 20, "HUD/crosshair.png");
        Center(crosshair.rectTransform, Vector2.zero, new Vector2(20, 20));

        var forms = Rect("Forms", design, 0, 0, 630, 174);
        forms.anchorMin = forms.anchorMax = forms.pivot = Vector2.zero;
        forms.anchoredPosition = new Vector2(55, 46);
        var overlays = new GameObject[5];
        var artwork = new UnityEngine.UI.Image[5];
        string[] elements = { "fire", "water", "wood", "metal", "earth" };
        for (int i = 0; i < 5; i++)
        {
            float x = 9 + i * 126f;
            Image("Mount" + i, forms, x, 12, 108, 114, "HUD/element_mount_" + elements[i] + ".png");
            artwork[i] = Image("Element" + i, forms, x + 21, 33, 66, 66, "Icons/element_" + elements[i] + ".png");
            overlays[i] = Image("Selected" + i, forms, x, 4.5f, 108, 123, "HUD/element_selection.png").gameObject;
            var key = Text("Key" + i, forms, x + 36, 129, 36, 34.5f, (i + 1).ToString(), 22.5f);
            key.alignment = TextAlignmentOptions.Center;
        }

        var skills = Rect("Skills", design, 0, 0, 220, 153);
        skills.anchorMin = skills.anchorMax = skills.pivot = new Vector2(1, 0);
        skills.anchoredPosition = new Vector2(-63.3f, 35.45f);
        skills.localScale = Vector3.one * 1.35f;
        var e = Skill(skills, "SkillE", 0, "E", ringSprite, false);
        var r = Skill(skills, "SkillR", 122, "R", ringSprite, true);
        var charge = Text("ChargeValue", (RectTransform)r.transform, -12, -33, 122, 28, "", 19);
        charge.alignment = TextAlignmentOptions.Center;
        view.Configure(hp, spFill, overlays, e, r);
        view.ConfigureFormArtwork(artwork);
        view.ConfigureElementTheme(theme);
        view.ConfigureUltimateEnergy(null);
        view.ConfigureUltimateCharge(null, charge);
        view.ConfigureVitalsText(hpLabel, spLabel);
        StyleChainSkills(root);
        StyleFormLocks(root);
        root.GetComponent<HudController>()?.Configure(view);
        foreach (var graphic in design.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
    }

    private static void StyleFormLocks(GameObject root)
    {
        var view = root.GetComponent<HudView>();
        if (view == null) throw new InvalidOperationException("The live PlayerHUD has no HudView.");
        var forms = RequiredRect(root.transform, "BoundaryZeroHUD/Forms");
        var locks = new GameObject[5];
        var keys = new GameObject[5];
        for (int i = 0; i < 5; i++)
        {
            keys[i] = RequiredRect(forms, "Key" + i).gameObject;
            var label = Text("Locked" + i, forms, 24 + i * 126f, 129, 78, 34.5f, "잠금", 20);
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(.72f, .76f, .8f, 1f);
            label.raycastTarget = false;
            locks[i] = label.gameObject;
            locks[i].SetActive(false);
        }
        view.ConfigureFormLocks(locks, keys);
    }

    private static void BindWheelArtwork(GameObject root)
    {
        var view = root.GetComponent<FinisherWheelView>();
        if (view == null) throw new InvalidOperationException("Finisher wheel view is missing.");
        var serialized = new SerializedObject(view);
        var sectors = serialized.FindProperty("sectors");
        for (int i = 0; i < sectors.arraySize; i++)
        {
            var icon = RequiredRect(root.transform, "BoundaryZeroContent/BoundaryZeroWheel/Sector" + i + "/Icon")
                .GetComponent<UnityEngine.UI.Image>();
            if (icon == null) throw new InvalidOperationException("Finisher sector artwork is missing.");
            sectors.GetArrayElementAtIndex(i).FindPropertyRelative("Artwork").objectReferenceValue = icon;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void StyleChainSkills(GameObject root)
    {
        var view = root.GetComponent<HudView>();
        if (view == null) throw new InvalidOperationException("The live PlayerHUD has no HudView.");
        var forms = RequiredRect(root.transform, "BoundaryZeroHUD/Forms");
        var group = Rect("ChainSkills", forms, 9, -100, 408, 90);
        var theme = Reference<HudElementTheme>(view, "elementTheme");
        Color fire = theme != null && theme.TryGet(1, out var fireTheme)
            ? fireTheme.Color : new Color(1f, .4f, .25f);
        Color water = theme != null && theme.TryGet(2, out var waterTheme)
            ? waterTheme.Color : new Color(.3f, .75f, 1f);
        var fireText = ChainSlot(group, "Fire", 0, fire, out var fireSelected);
        var waterText = ChainSlot(group, "Water", 164, water, out var waterSelected);
        var hint = Text("ActionHint", group, 0, 50, 320, 32, "", 23);
        hint.gameObject.SetActive(false);
        view.ConfigureChainSkills(group.gameObject, fireText, waterText, fireSelected, waterSelected, hint);
        group.gameObject.SetActive(false);
        EditorUtility.SetDirty(view);
    }

    private static TMP_Text ChainSlot(Transform parent, string name, float x, Color color,
        out GameObject selected)
    {
        var slot = Rect(name, parent, x, 0, 150, 42);
        slot.gameObject.SetActive(true);
        var background = PlainImage("Background", slot, 0, 0, 150, 42, null);
        background.color = new Color(.025f, .045f, .07f, .9f);
        var highlight = PlainImage("Selected", slot, 0, 0, 150, 42, null);
        highlight.color = new Color(color.r, color.g, color.b, .25f);
        selected = highlight.gameObject;
        selected.SetActive(false);
        var rail = PlainImage("ElementRail", slot, 0, 0, 150, 3, null);
        rail.color = color;
        var label = Text("Remaining", slot, 13, 8, 128, 27, "", 21);
        label.color = color;
        return label;
    }

    private static HudSkillView Skill(Transform parent, string name, float x, string key, Sprite ringSprite, bool charge)
    {
        var mount = Rect(name, parent, x, 0, 98, 120);
        Image("Mount", mount, 0, 0, 98, 120, "HUD/skill_mount.png");
        var track = PlainImage("RingTrack", mount, 5, 5, 88, 88, ringSprite);
        var gray = PlainImage("IconGray", mount, 14, 14, 70, 70, null);
        var reveal = PlainImage("IconColorReveal", mount, 14, 14, 70, 70, null);
        var ring = PlainImage("RingFill", mount, 5, 5, 88, 88, ringSprite);
        ring.type = UnityEngine.UI.Image.Type.Filled;
        ring.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
        ring.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
        var number = Text("Cooldown", mount, 5, 28, 88, 40, "", 27);
        number.alignment = TextAlignmentOptions.Center;
        Text("Key", mount, 25, 125, 48, 28, key, 18).alignment = TextAlignmentOptions.Center;
        var skill = mount.GetComponent<HudSkillView>() ?? mount.gameObject.AddComponent<HudSkillView>();
        skill.Configure(gray, reveal, ring, number);
        skill.ConfigureRingTrack(track);
        skill.ConfigureChargeReveal(charge);
        return skill;
    }

    private static void StyleMini(GameObject root)
    {
        const float minimapScale = 1.65f;
        Canvas(root);
        var panel = RequiredRect(root.transform, "Panel");
        Position(panel, 0, 0, 245, 255);
        panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one;
        panel.anchoredPosition = new Vector2(-60.75f, -54f);
        panel.localScale = new Vector3(minimapScale, minimapScale, 1f);
        ApplyImage(panel.GetComponent<UnityEngine.UI.Image>(), "HUD/minimap_frame.png");
        var oldBackground = panel.Find("Background");
        if (oldBackground != null) oldBackground.gameObject.SetActive(false);
        var viewport = RequiredRect(panel, "Viewport");
        Position(viewport, 4, 19, 222, 222);
        var arrow = viewport.GetComponentInChildren<DungeonMinimapIconGraphic>(true);
        if (arrow != null) arrow.raycastTarget = false;
        var hint = Text("MapHint", panel, 0, 255 + 8 / minimapScale, 245, 26 / minimapScale,
            "TAB  지도", 18 / minimapScale);
        hint.alignment = TextAlignmentOptions.MidlineRight;
        StyleMiniStageLabel(root);
    }

    private static void StyleMiniStageLabel(GameObject root)
    {
        DungeonMinimapSetup.UpgradeStageLabelRoot(root);
        var label = root.GetComponent<DungeonMinimapView>().StageLabel;
        StyleText(label, 20);
        label.alignment = TextAlignmentOptions.BottomLeft;
        label.overflowMode = TextOverflowModes.Ellipsis;
        EditorUtility.SetDirty(label);
    }

    private static void StyleMap(GameObject root)
    {
        Canvas(root);
        var view = root.GetComponent<DungeonMinimapView>();
        if (view == null) throw new InvalidOperationException("Full map view is missing.");
        var sharp = Rect("BoundaryZeroSharp", root.transform, 0, 0, 0, 0);
        Stretch(sharp);
        var sharpGroup = sharp.GetComponent<CanvasGroup>();
        if (sharpGroup == null) sharpGroup = sharp.gameObject.AddComponent<CanvasGroup>();
        sharpGroup.alpha = 1f;
        sharpGroup.interactable = true;
        sharpGroup.blocksRaycasts = true;
        EditorUtility.SetDirty(sharpGroup);
        var shade = root.transform.Find("Backdrop") as RectTransform ?? sharp.Find("Backdrop") as RectTransform;
        if (shade == null) throw new InvalidOperationException("Full-map backdrop is missing.");
        shade.SetParent(sharp, false);
        Stretch(shade);
        var shadeImage = shade.GetComponent<UnityEngine.UI.Image>();
        shadeImage.color = new Color(.005f, .012f, .022f, .56f);
        var panel = root.transform.Find("Panel") as RectTransform ?? sharp.Find("Panel") as RectTransform;
        if (panel == null) throw new InvalidOperationException("Full map panel is missing.");
        panel.SetParent(sharp, false);
        Center(panel, Vector2.zero, new Vector2(1920, 1120));
        var fitter = panel.GetComponent<BoundaryZeroWindowFit>() ?? panel.gameObject.AddComponent<BoundaryZeroWindowFit>();
        fitter.Configure(new Vector2(1920, 1120), .88f);
        ApplyImage(panel.GetComponent<UnityEngine.UI.Image>(), "Frames/window_wide_glass.png");
        var oldBackground = panel.Find("Background");
        if (oldBackground != null) oldBackground.gameObject.SetActive(false);
        Image("BoundaryZeroBorder", panel, 0, 0, 1920, 1120, "Frames/window_wide_border.png").transform.SetAsFirstSibling();
        var title = panel.Find("Title")?.GetComponent<TMP_Text>();
        if (title != null) { Position(title.rectTransform, 132, 68, 1300, 60); StyleText(title, 39); title.text = "탐사 지도"; }
        var divider = panel.Find("HeaderDivider");
        if (divider != null) divider.gameObject.SetActive(false);
        var viewport = RequiredRect(panel, "Viewport");
        Position(viewport, 73, 171, 1774, 790);
        var hint = panel.Find("CloseHint")?.GetComponent<TMP_Text>();
        if (hint != null) { Position(hint.rectTransform, 1335, 1012, 510, 38); StyleText(hint, 22); hint.text = "TAB / ESC  닫기"; }
        var travel = Reference<TMP_Text>(view, "travelStatus");
        if (travel != null) { Position(travel.rectTransform, 73, 1012, 1200, 38); StyleText(travel, 22); }
        var close = Image("BoundaryZeroClose", panel, 1806, 67, 44, 43, "Controls/close_button.png");
        close.raycastTarget = true;
        var button = close.GetComponent<UnityEngine.UI.Button>() ?? close.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = close;
        button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        UnityEventTools.AddPersistentListener(button.onClick, view.RequestClose);
        Image("Cross", close.transform, 10, 9.5f, 24, 24, "Controls/close_cross.png");
        var rawRect = Rect("BoundaryZeroBlur", root.transform, 0, 0, 0, 0);
        Stretch(rawRect);
        rawRect.SetAsFirstSibling();
        var raw = rawRect.GetComponent<UnityEngine.UI.RawImage>() ?? rawRect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        raw.raycastTarget = false;
        var blur = root.GetComponent<BoundaryZeroBackdrop>() ?? root.AddComponent<BoundaryZeroBackdrop>();
        blur.Configure(sharpGroup, raw);
        Field(view, "modalBackdrop", blur);
    }

    private static void StyleWheel(GameObject root)
    {
        Canvas(root);
        var view = root.GetComponent<FinisherWheelView>();
        if (view == null) throw new InvalidOperationException("Finisher wheel view is missing.");
        var oldContent = Reference<RectTransform>(view, "content");
        if (oldContent != null && oldContent.name != "BoundaryZeroContent") oldContent.gameObject.SetActive(false);
        var content = Rect("BoundaryZeroContent", root.transform, 0, 0, 0, 0);
        Stretch(content);
        content.gameObject.SetActive(true);
        var shade = PlainImage("Shade", content, 0, 0, 0, 0, null);
        Stretch(shade.rectTransform);
        shade.color = new Color(.008f, .016f, .028f, .55f);
        var ring = Rect("BoundaryZeroWheel", content, 0, 0, 800, 800);
        Center(ring, Vector2.zero, new Vector2(800, 800));
        Image("Halo", ring, 0, 0, 800, 800, "Wheel/halo.png");
        Image("Rings", ring, 0, 0, 800, 800, "Wheel/rings.png");
        var title = Text("Title", content, 0, 0, 800, 60, "변형 3타", 38);
        Center(title.rectTransform, new Vector2(0, 505), new Vector2(800, 60));
        title.alignment = TextAlignmentOptions.Center;
        var instruction = Text("Instruction", content, 0, 0, 800, 42, "우클릭을 놓아 선택", 24);
        Center(instruction.rectTransform, new Vector2(0, 452), new Vector2(800, 42));
        instruction.alignment = TextAlignmentOptions.Center;
        var center = Text("CurrentElement", ring, 295, 330, 210, 70, "화", 46);
        center.alignment = TextAlignmentOptions.Center;
        Text("CenterRole", ring, 270, 415, 260, 38, "현재 폼 · 3타", 22).alignment = TextAlignmentOptions.Center;
        var selection = Text("Selection", content, 0, 0, 800, 40, "", 28);
        Center(selection.rectTransform, new Vector2(0, -452), new Vector2(800, 40));
        selection.alignment = TextAlignmentOptions.Center;
        var help = Text("Help", content, 0, 0, 1000, 38, "중앙: 현재 폼 3타    ·    ESC: 취소", 23);
        Center(help.rectTransform, new Vector2(0, -505), new Vector2(1000, 38));
        help.alignment = TextAlignmentOptions.Center;
        var serialized = new SerializedObject(view);
        serialized.FindProperty("content").objectReferenceValue = content;
        serialized.FindProperty("wheel").objectReferenceValue = null;
        serialized.FindProperty("centerName").objectReferenceValue = center;
        serialized.FindProperty("selectionLabel").objectReferenceValue = selection;
        serialized.FindProperty("inputScaleMultiplier").floatValue = 4f / 3f;
        serialized.FindProperty("cursorPresentationScale").floatValue = 1.6f;
        var sectors = serialized.FindProperty("sectors");
        sectors.arraySize = 5;
        for (int i = 0; i < 5; i++)
        {
            var element = FinisherWheelSelection.ElementAt(i);
            float radians = i * 72f * Mathf.Deg2Rad;
            Vector2 position = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * 245f;
            var sector = Rect("Sector" + i, ring, 0, 0, 170, 170);
            Center(sector, position, new Vector2(170, 170));
            var hover = Image("Selected", sector, 0, 0, 170, 170, "Wheel/hover.png");
            var artwork = Image("Icon", sector, 43, 25, 84, 84, "Icons/element_" + ElementFile(element) + ".png");
            var elementLabel = Text("Element", sector, 0, 109, 170, 30, ElementName(element), 23);
            elementLabel.alignment = TextAlignmentOptions.Center;
            elementLabel.color = FinisherWheelGraphic.ElementColor(i);
            var role = Text("Role", sector, 0, 140, 170, 27, "", 17);
            role.alignment = TextAlignmentOptions.Center;
            var binding = sectors.GetArrayElementAtIndex(i);
            binding.FindPropertyRelative("Element").intValue = (int)element;
            binding.FindPropertyRelative("DisplayName").stringValue = ElementName(element);
            binding.FindPropertyRelative("ElementLabel").objectReferenceValue = elementLabel;
            binding.FindPropertyRelative("RoleLabel").objectReferenceValue = role;
            binding.FindPropertyRelative("SelectedVisual").objectReferenceValue = hover.gameObject;
            binding.FindPropertyRelative("Artwork").objectReferenceValue = artwork;
            hover.gameObject.SetActive(false);
        }
        var cursor = Image("VirtualCursor", content, 0, 0, 14, 14, "HUD/crosshair.png");
        Center(cursor.rectTransform, Vector2.zero, new Vector2(14, 14));
        cursor.transform.SetAsLastSibling();
        serialized.FindProperty("virtualCursor").objectReferenceValue = cursor.rectTransform;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (!view.ValidateBindings(out string error)) throw new InvalidOperationException(error);
    }

    private static void StyleGold(GameObject root)
    {
        Canvas(root);
        var panel = RequiredRect(root.transform, "Gold");
        Position(panel, 248, 196, 300, 30);
        var image = panel.GetComponent<UnityEngine.UI.Image>();
        if (image != null) image.enabled = false;
        var label = panel.GetComponentInChildren<TMP_Text>(true);
        if (label != null) { Stretch(label.rectTransform); StyleText(label, 20); label.alignment = TextAlignmentOptions.MidlineLeft; }
    }

    private static void Canvas(GameObject root)
    {
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        if (scaler == null) throw new InvalidOperationException(root.name + " has no CanvasScaler.");
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2560, 1440);
        scaler.matchWidthOrHeight = .5f;
    }

    private static string ElementFile(ElementType element)
    {
        switch (element)
        {
            case ElementType.ELEMENT_FIRE: return "fire";
            case ElementType.ELEMENT_WATER: return "water";
            case ElementType.ELEMENT_WOOD: return "wood";
            case ElementType.ELEMENT_GOLD: return "metal";
            case ElementType.ELEMENT_EARTH: return "earth";
            default: throw new ArgumentOutOfRangeException(nameof(element));
        }
    }

    private static string ElementName(ElementType element)
    {
        switch (element)
        {
            case ElementType.ELEMENT_FIRE: return "화";
            case ElementType.ELEMENT_WATER: return "수";
            case ElementType.ELEMENT_WOOD: return "목";
            case ElementType.ELEMENT_GOLD: return "금";
            case ElementType.ELEMENT_EARTH: return "토";
            default: return "";
        }
    }

    private static void HorizontalFill(UnityEngine.UI.Image image)
    {
        image.type = UnityEngine.UI.Image.Type.Filled;
        image.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        image.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Left;
        image.fillAmount = 1;
    }

    private static Sprite Sprite(string relative)
        => AssetDatabase.LoadAssetAtPath<Sprite>(Art + relative)
            ?? throw new InvalidOperationException("BoundaryZERO sprite is missing: " + relative);

    private static UnityEngine.UI.Image Image(string name, Transform parent, float x, float y, float w, float h, string sprite)
        => PlainImage(name, parent, x, y, w, h, Sprite(sprite));

    private static UnityEngine.UI.Image PlainImage(string name, Transform parent, float x, float y, float w, float h, Sprite sprite)
    {
        var rect = Rect(name, parent, x, y, w, h);
        var image = rect.GetComponent<UnityEngine.UI.Image>() ?? rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.raycastTarget = false;
        image.preserveAspect = sprite != null && sprite.border == Vector4.zero;
        image.type = sprite != null && sprite.border != Vector4.zero ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
        return image;
    }

    private static void ApplyImage(UnityEngine.UI.Image image, string path)
    {
        if (image == null) throw new InvalidOperationException("Existing image is missing.");
        image.sprite = Sprite(path);
        image.color = Color.white;
        image.type = image.sprite.border != Vector4.zero ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
        image.preserveAspect = image.type == UnityEngine.UI.Image.Type.Simple;
        image.raycastTarget = false;
    }

    private static TMP_Text Text(string name, Transform parent, float x, float y, float w, float h, string value, float size)
    {
        var rect = Rect(name, parent, x, y, w, h);
        var text = rect.GetComponent<TextMeshProUGUI>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>();
        StyleText(text, size);
        text.text = value;
        return text;
    }

    private static void StyleText(TMP_Text text, float size)
    {
        text.font = BoundaryZeroTypography.GetFont();
        text.fontSharedMaterial = BoundaryZeroTypography.GetMaterial();
        text.fontSize = size;
        text.color = TextColor;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
    }

    private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var rect = parent.Find(name) as RectTransform;
        if (rect == null) rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        if (rect.parent != parent) rect.SetParent(parent, false);
        Position(rect, x, y, w, h);
        return rect;
    }

    private static RectTransform RequiredRect(Transform parent, string name)
        => parent.Find(name) as RectTransform ?? throw new InvalidOperationException(parent.name + "/" + name + " is missing.");

    private static void Position(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void Center(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static T Reference<T>(Object target, string field) where T : Object
        => new SerializedObject(target).FindProperty(field)?.objectReferenceValue as T;

    private static void Field(Object target, string field, Object value)
    {
        var data = new SerializedObject(target);
        var property = data.FindProperty(field) ?? throw new InvalidOperationException(field + " binding is missing.");
        property.objectReferenceValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }
}
