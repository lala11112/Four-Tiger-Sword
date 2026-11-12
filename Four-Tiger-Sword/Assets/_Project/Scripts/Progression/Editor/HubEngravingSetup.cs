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
    private const string Controls = "Assets/_Project/Sprites/UI/BoundaryZERO/Controls/";

    [MenuItem("Tools/Studio Baekho/Progression/Set Up Engraving Workshop Prefab")]
    private static void Menu() => Debug.Log(SetupPrefab());

    public static string SetupPrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Configure engravings in Edit Mode.");
        var settings = EnsureSettings();
        EnsureTreeGlyphs(settings);
        HubWorkshopSetup.ApplyReadability();
        AssetDatabase.SaveAssetIfDirty(settings);
        return "PASS: workshop prefab has Basic/Engraving tabs, 38 nodes and 37 connections; scenes were not saved.";
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
        string content = "기본 강화 각인 트리 효과 해금 가능 완료 선행 필요 부족 무료 시작점으로 비용 설정을 불러올 수 없습니다 저장하지 못했습니다 이용 불가 0123456789+−-%×·,.°()p—"
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
        var modal = view.UI.panel.transform;
        var window = modal.Find("Window");
        var basic = window?.Find("BasicPage");
        if (basic == null) throw new InvalidOperationException("Build the shared Workshop window before configuring engravings.");
        var existing = window.Find("EngravingTree") ?? modal.Find("EngravingTree");
        var tree = existing != null ? existing.GetComponent<PentaUpgradeView>() : null;
        if (tree == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
            if (prefab == null) throw new InvalidOperationException("Delivered engraving panel is missing.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, window);
            instance.name = "EngravingTree";
            tree = instance.GetComponent<PentaUpgradeView>();
        }
        var treeRect = (RectTransform)tree.transform;
        if (treeRect.parent != window) treeRect.SetParent(window, false);
        PentaUpgradeView.Fill(treeRect);
        treeRect.localScale = Vector3.one;
        tree.ConfigureGameplayUI(new Vector2(HubWorkshopSetup.FrameWidth, HubWorkshopSetup.FrameHeight));
        var normal = AssetDatabase.LoadAssetAtPath<Sprite>(Controls + "tab_normal.png");
        var selected = AssetDatabase.LoadAssetAtPath<Sprite>(Controls + "tab_selected.png");
        if (normal == null || selected == null) throw new InvalidOperationException("Workshop tab sprites are missing.");
        var basicTab = Tab(window, "BasicTab", "기본 강화", 64, normal);
        var treeTab = Tab(window, "EngravingTab", "각인 트리", 276, normal);
        basicTab.transform.SetAsLastSibling();
        treeTab.transform.SetAsLastSibling();
        view.ConfigureEngravings(tree, basic.gameObject,
            new[] { basicTab }, new[] { treeTab }, normal, selected);
        EditorUtility.SetDirty(view);
        EditorUtility.SetDirty(tree);
        if (PrefabUtility.IsPartOfPrefabInstance(tree)) PrefabUtility.RecordPrefabInstancePropertyModifications(tree);
        if (PrefabUtility.IsPartOfPrefabInstance(view)) PrefabUtility.RecordPrefabInstancePropertyModifications(view);
    }

    private static UnityEngine.UI.Button Tab(Transform parent, string name, string value, float x, Sprite sprite)
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
        found.anchoredPosition = new Vector2(x, -126); found.sizeDelta = new Vector2(200, 48);
        var image = found.GetComponent<UnityEngine.UI.Image>();
        image.sprite = sprite;
        image.type = UnityEngine.UI.Image.Type.Sliced;
        image.color = Color.white;
        image.raycastTarget = true;
        var button = found.GetComponent<UnityEngine.UI.Button>();
        PentaUpgradeView.ButtonFeedback(button, image);
        var label = found.Find("Label")?.GetComponent<TMP_Text>();
        if (label == null)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer; go.transform.SetParent(found, false); label = go.GetComponent<TMP_Text>();
        }
        PentaUpgradeView.Fill(label.rectTransform);
        label.font = BoundaryZeroTypography.GetFont();
        label.fontSharedMaterial = BoundaryZeroTypography.GetMaterial();
        label.fontSize = 24;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap; label.text = value;
        return button;
    }
}
