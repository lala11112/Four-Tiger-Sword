using System;
using System.IO;
using System.Linq;
using Penta.Upgrade;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>Adds the delivered tree to the existing workshop prefab without saving scenes.</summary>
public static class HubEngravingSetup
{
    public const string SettingsPath = "Assets/_Project/Resources/EngravingSettings.asset";
    public const string PanelPath = "Assets/UI/PentaUpgrade/Prefabs/BoundaryZERO_UpgradePanel.prefab";
    private const string FontPath = "Assets/UI/PentaUpgrade/Art/BoundaryUpgradeKorean.asset";

    [MenuItem("Tools/Studio Baekho/Progression/Set Up Engraving Workshop Prefab")]
    private static void Menu() => Debug.Log(SetupPrefab());

    public static string SetupPrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Configure engravings in Edit Mode.");
        var settings = EnsureSettings();
        EnsureTreeGlyphs(settings);
        var root = PrefabUtility.LoadPrefabContents(HubWorkshopSetup.PrefabPath);
        try
        {
            var view = root.GetComponent<HubWorkshopView>();
            ConfigureView(view);
            view.Close();
            if (PrefabUtility.SaveAsPrefabAsset(root, HubWorkshopSetup.PrefabPath) == null)
                throw new InvalidOperationException("Could not save engraving workshop prefab.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssetIfDirty(settings);
        return "PASS: workshop prefab has Basic/Engraving tabs, 40 nodes and 39 preserved connections; scenes were not saved.";
    }

    public static EngravingSettingsSO EnsureSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<EngravingSettingsSO>(SettingsPath);
        if (settings == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            settings = ScriptableObject.CreateInstance<EngravingSettingsSO>();
            settings.RefreshDescriptions();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }
        if (!settings.Validate(out string error)) throw new InvalidOperationException(error);
        return settings;
    }

    private static void EnsureTreeGlyphs(EngravingSettingsSO settings)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) throw new InvalidOperationException("The delivered Korean font is missing.");
        string content = "기본 강화 각인 트리 영구 강화석을 사용해 성장 가지를 해금하세요 가능 완료 선행 필요 부족 무료 시작점입니다 적용됩니다 불러올 수 없습니다 자동으로 저장됩니다 정보를 구매 후 잔액 0123456789+-%×·,.°()p"
            + string.Join(" ", settings.nodes.Select(node => node.displayName + " " + settings.Describe(node.id)));
        content = new string(content.Where(character => !char.IsControl(character)).Distinct().ToArray());
        font.TryAddCharacters(content);
        if (!font.HasCharacters(content, out uint[] missing, true, true))
            throw new InvalidOperationException("The engraving font is missing required Korean glyphs: " + string.Join(",", missing));
        foreach (var atlas in font.atlasTextures)
            if (atlas != null && !AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssetIfDirty(font);
    }

    /// <summary>Used by scene installers on a deliberately chosen workshop instance.</summary>
    public static void ConfigureView(HubWorkshopView view)
    {
        if (view == null || view.UI?.panel == null) throw new InvalidOperationException("Configure the basic workshop first.");
        var basic = view.UI.panel.transform.Find("Window");
        if (basic == null) throw new InvalidOperationException("Workshop basic Window was not found.");
        var existing = view.UI.panel.transform.Find("EngravingTree");
        var tree = existing != null ? existing.GetComponent<PentaUpgradeView>() : null;
        if (tree == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
            if (prefab == null) throw new InvalidOperationException("Delivered engraving panel is missing.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, view.UI.panel.transform);
            instance.name = "EngravingTree";
            tree = instance.GetComponent<PentaUpgradeView>();
        }
        tree.ConfigureGameplayUI();
        var treeWindow = tree.transform.Find("UpgradeVisual/UpgradePanel");
        var subtitle = basic.Find("Header/Row/Heading/Titles/Subtitle");
        if (subtitle != null) subtitle.gameObject.SetActive(false);
        var basicTab = Tab(basic, "BasicTab", "기본 강화", 152, 116, 170, 32);
        var basicTreeTab = Tab(basic, "EngravingTab", "각인 트리", 330, 116, 170, 32);
        var treeBasicTab = Tab(treeWindow, "BasicTab", "기본 강화", 870, 58, 170, 36);
        var treeTab = Tab(treeWindow, "EngravingTab", "각인 트리", 1048, 58, 170, 36);
        view.ConfigureEngravings(tree, basic.gameObject,
            new[] { basicTab, treeBasicTab }, new[] { basicTreeTab, treeTab });
        EditorUtility.SetDirty(view);
        EditorUtility.SetDirty(tree);
        if (PrefabUtility.IsPartOfPrefabInstance(tree)) PrefabUtility.RecordPrefabInstancePropertyModifications(tree);
        if (PrefabUtility.IsPartOfPrefabInstance(view)) PrefabUtility.RecordPrefabInstancePropertyModifications(view);
    }

    private static UnityEngine.UI.Button Tab(Transform parent, string name, string value,
        float x, float y, float width, float height)
    {
        var found = parent.Find(name) as RectTransform;
        if (found == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            found = (RectTransform)go.transform;
        }
        found.anchorMin = found.anchorMax = found.pivot = new Vector2(0, 1);
        found.anchoredPosition = new Vector2(x, -y); found.sizeDelta = new Vector2(width, height);
        var image = found.GetComponent<UnityEngine.UI.Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Sprites/UI/BoundaryZERO/Controls/button_primary_normal.png");
        image.type = UnityEngine.UI.Image.Type.Sliced;
        image.color = new Color(.65f, .73f, .83f, .85f); image.raycastTarget = true;
        var button = found.GetComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        var colors = button.colors; colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f); colors.selectedColor = Color.white;
        button.colors = colors;
        var label = found.Find("Label")?.GetComponent<TMP_Text>();
        if (label == null)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer; go.transform.SetParent(found, false); label = go.GetComponent<TMP_Text>();
        }
        PentaUpgradeView.Fill(label.rectTransform);
        label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        label.fontSize = 19; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap; label.text = value;
        return button;
    }
}
