using System;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>Upgrades the authored shop prefab in place without touching the open scene.</summary>
public static class ShopTabsSetup
{
    public const string PrefabPath = ShopSetup.Folder + "/UI/TemporaryShop.prefab";
    private static readonly Color Card = new(.10f, .13f, .17f, 1f);
    private static readonly Color Accent = new(.76f, .58f, .29f, 1f);
    private static TMP_FontAsset Font => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Font/NotoSansKR SDF.asset");

    [MenuItem("Tools/Studio Baekho/Shop/Upgrade Purchase Sale Fusion Tabs")]
    private static void UpgradeFromMenu() => Debug.Log(Upgrade());

    public static string Upgrade()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before upgrading the shop prefab.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            throw new InvalidOperationException("Install the shop prefab before upgrading its tabs.");
        if (AssetDatabase.LoadAssetAtPath<ShopView>(PrefabPath)?.HasBoundaryLayout == true)
            return "BoundaryZERO shop already includes purchase, sale and fusion tabs.";
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ConfigureHierarchy(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return "Shop prefab upgraded with purchase, sale and fusion tabs; open scene preserved.";
    }

    public static void ConfigureHierarchy(GameObject root)
    {
        var view = root.GetComponent<ShopView>();
        var window = root.transform.Find("ShopPanel/Window") as RectTransform;
        if (view == null || window == null || Font == null)
            throw new InvalidOperationException("The shop view, Window or Korean font is missing.");
        var scroll = window.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
        if (scroll == null || scroll.viewport == null || scroll.content == null)
            throw new InvalidOperationException("The shop scroll viewport and content must already exist.");

        window.sizeDelta = new Vector2(1040, 660);
        var windowLayout = window.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(32, 32, 22, 22);
        windowLayout.spacing = 10;
        Size(window.Find("Title") as RectTransform, 40);
        Size(window.Find("Summary") as RectTransform, 30);
        Size(window.Find("Message") as RectTransform, 28);
        Size(window.Find("Footer") as RectTransform, 42);

        var bar = Rect("TabsBar", window);
        bar.SetSiblingIndex(2);
        Size(bar, 40);
        var barLayout = Horizontal(bar, 10);
        barLayout.childForceExpandWidth = true;
        var buyTab = Button("BuyTab", bar, "구매", out _);
        var sellTab = Button("SellTab", bar, "판매", out _);
        var fuseTab = Button("FuseTab", bar, "합성", out _);
        foreach (var tab in new[] { buyTab, sellTab, fuseTab })
        {
            var tabSize = Size(tab.transform as RectTransform, 40);
            tabSize.minWidth = tabSize.preferredWidth = 0; tabSize.flexibleWidth = 1;
        }

        var body = Rect("Body", window);
        body.SetSiblingIndex(3);
        var bodySize = Size(body, 0); bodySize.flexibleHeight = 1;
        var bodyLayout = Horizontal(body, 16);
        bodyLayout.childForceExpandWidth = false;
        bodyLayout.childForceExpandHeight = true;
        scroll.transform.SetParent(body, false);
        scroll.transform.SetAsFirstSibling();
        var scrollSize = Size(scroll.transform as RectTransform, 0);
        scrollSize.minWidth = scrollSize.preferredWidth = 0;
        scrollSize.flexibleWidth = scrollSize.flexibleHeight = 1;
        var empty = Text("EmptyMessage", scroll.transform, 20, new Color(.72f, .78f, .83f));
        Stretch(empty.rectTransform, 16);
        empty.alignment = TextAlignmentOptions.Center;
        empty.gameObject.SetActive(false);

        var details = Rect("TradeDetails", body);
        var detailSize = Size(details, 0, 304); detailSize.flexibleHeight = 1;
        Background(details, Card);

        var sale = Rect("SalePanel", details); Stretch(sale, 14); Column(sale, 8);
        var saleTitle = Text("SelectedTitle", sale, 21, Color.white); Size(saleTitle.rectTransform, 48);
        var saleIconRect = Rect("SelectedIcon", sale); Size(saleIconRect, 60);
        var saleIcon = Background(saleIconRect, Color.white); saleIcon.preserveAspect = true;
        var saleDescription = Text("SelectedDescription", sale, 18, new Color(.72f, .78f, .83f));
        var descriptionSize = Size(saleDescription.rectTransform, 0); descriptionSize.flexibleHeight = 1;
        saleDescription.alignment = TextAlignmentOptions.TopLeft;
        var salePrice = Text("SalePrice", sale, 22, Accent); Size(salePrice.rectTransform, 44);
        // Korean glyph metrics need more than the old 30px Latin-price row. Do not let
        // TMP's ellipsis mode discard the complete confirmation line at this size.
        salePrice.textWrappingMode = TextWrappingModes.NoWrap;
        salePrice.overflowMode = TextOverflowModes.Overflow;
        var sell = Button("SellButton", sale, "판매", out _); Size(sell.transform as RectTransform, 44);

        var fusion = Rect("FusionPanel", details); Stretch(fusion, 14); Column(fusion, 8);
        var first = Slot("FirstSlot", fusion, out var firstLabel, out var firstIcon);
        var second = Slot("SecondSlot", fusion, out var secondLabel, out var secondIcon);
        var odds = Text("FusionOdds", fusion, 17, new Color(.72f, .78f, .83f)); Size(odds.rectTransform, 62);
        var fuse = Button("FuseButton", fusion, "합성  ·  골드 소모 없음", out _); Size(fuse.transform as RectTransform, 44);
        var result = Rect("FusionResult", fusion); Size(result, 76);
        var resultLayout = Horizontal(result, 10); resultLayout.childAlignment = TextAnchor.MiddleLeft;
        var resultIconRect = Rect("Icon", result); Size(resultIconRect, 54, 54);
        var resultIcon = Background(resultIconRect, Color.white); resultIcon.preserveAspect = true;
        var resultLabel = Text("Label", result, 19, Accent);
        var resultLabelSize = Size(resultLabel.rectTransform, 72);
        resultLabelSize.minWidth = resultLabelSize.preferredWidth = 0; resultLabelSize.flexibleWidth = 1;

        view.ConfigureTabs(new ShopView.TabControls
        {
            BuyTab = buyTab, SellTab = sellTab, FuseTab = fuseTab, SellButton = sell, FuseButton = fuse,
            FirstSlot = first, SecondSlot = second, DetailsPanel = details.gameObject,
            SalePanel = sale.gameObject, FusionPanel = fusion.gameObject, ResultPanel = result.gameObject,
            EmptyMessage = empty, SaleTitle = saleTitle, SaleDescription = saleDescription, SalePrice = salePrice,
            SaleIcon = saleIcon, FirstLabel = firstLabel, SecondLabel = secondLabel, FirstIcon = firstIcon,
            SecondIcon = secondIcon, Odds = odds, ResultLabel = resultLabel, ResultIcon = resultIcon, Scroll = scroll
        });
        details.gameObject.SetActive(false);
        sale.gameObject.SetActive(false);
        fusion.gameObject.SetActive(false);
        result.gameObject.SetActive(false);
        EditorUtility.SetDirty(view);
    }

    private static UnityEngine.UI.Button Slot(string name, Transform parent, out TMP_Text label, out UnityEngine.UI.Image icon)
    {
        var button = Button(name, parent, string.Empty, out label);
        Size(button.transform as RectTransform, 64);
        label.fontSize = 17; label.alignment = TextAlignmentOptions.MidlineLeft;
        label.rectTransform.offsetMin = new Vector2(62, 5); label.rectTransform.offsetMax = new Vector2(-8, -5);
        var iconRect = Rect("Icon", button.transform);
        iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0, .5f);
        iconRect.anchoredPosition = new Vector2(8, 0); iconRect.sizeDelta = new Vector2(44, 44);
        icon = Background(iconRect, Color.white); icon.preserveAspect = true;
        return button;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var existing = parent.Find(name) as RectTransform;
        if (existing != null) return existing;
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); return rect;
    }
    private static void Stretch(RectTransform rect, float padding = 0)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * padding; rect.offsetMax = Vector2.one * -padding;
    }
    private static UnityEngine.UI.LayoutElement Size(RectTransform rect, float height, float width = -1)
    {
        if (rect == null) throw new InvalidOperationException("An expected shop RectTransform is missing.");
        var size = rect.GetComponent<UnityEngine.UI.LayoutElement>() ?? rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        size.minHeight = size.preferredHeight = height; size.flexibleHeight = 0;
        if (width >= 0) { size.minWidth = size.preferredWidth = width; size.flexibleWidth = 0; }
        return size;
    }
    private static UnityEngine.UI.Image Background(RectTransform rect, Color color, bool raycast = false)
    {
        var image = rect.GetComponent<UnityEngine.UI.Image>() ?? rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color; image.raycastTarget = raycast; return image;
    }
    private static TMP_Text Text(string name, Transform parent, float fontSize, Color color)
    {
        var rect = Rect(name, parent);
        var label = rect.GetComponent<TMP_Text>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = Font; label.fontSize = fontSize; label.color = color; label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Ellipsis;
        label.alignment = TextAlignmentOptions.MidlineLeft; return label;
    }
    private static UnityEngine.UI.Button Button(string name, Transform parent, string text, out TMP_Text label)
    {
        var rect = Rect(name, parent); var image = Background(rect, new Color(.26f, .36f, .39f), true);
        var button = rect.GetComponent<UnityEngine.UI.Button>() ?? rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        var colors = button.colors; colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.disabledColor = new Color(.5f, .5f, .5f, .55f); button.colors = colors;
        label = Text("Label", rect, 20, Color.white); Stretch(label.rectTransform, 4);
        label.text = text; label.alignment = TextAlignmentOptions.Center; return button;
    }
    private static UnityEngine.UI.HorizontalLayoutGroup Horizontal(RectTransform rect, float spacing)
    {
        var layout = rect.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>() ?? rect.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = spacing; layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false; return layout;
    }
    private static void Column(RectTransform rect, float spacing)
    {
        var layout = rect.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() ?? rect.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.spacing = spacing; layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
    }
}
