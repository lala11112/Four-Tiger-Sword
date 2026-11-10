using BoundaryZero.Inventory;
using TMPro;
using UnityEngine;

/// <summary>Builds the permanent balance and hosts the catalog overlay.</summary>
internal sealed class HubFieldHud
{
    private static readonly Color Steel = new Color32(232, 238, 245, 255);
    private static readonly Color Muted = new Color32(174, 190, 205, 255);
    private static readonly Color Navy = new Color32(13, 26, 41, 245);
    private static readonly Color Line = new Color32(123, 145, 166, 140);
    private static readonly Color Red = new Color32(231, 43, 70, 255);
    private static readonly Vector2 TopLeft = new Vector2(0, 1);
    private static readonly Vector2 TopRight = Vector2.one;

    private readonly TMP_FontAsset _font;
    private readonly TMP_Text _currencyName;
    private readonly TMP_Text _balance;

    public RectTransform Root { get; }

    public HubFieldHud(Transform parent, InventoryCatalog artwork)
    {
        _font = Resources.Load<TMP_FontAsset>("BoundaryUI/Fonts/BoundaryFont")
            ?? (artwork != null ? artwork.font : null);
        Root = Child(parent, "HubFieldHud");
        var canvas = Root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 79;
        var scaler = Root.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        Root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        var currency = Child(Root, "PermanentCurrency", -36, -32, 208, 63, TopRight);
        Plate(currency, Line);
        Plate(Child(currency, "Fill", 1, -1, 206, 61), Navy);
        Plate(Child(currency, "Accent", 1, -1, 3, 61), Red);
        if (artwork != null && artwork.seal != null)
        {
            var seal = Plate(Child(currency, "Seal", 13, -16, 30, 30), Steel);
            seal.sprite = artwork.seal;
            seal.preserveAspect = true;
        }
        _currencyName = Label(currency, "CurrencyName", "", 51, -8, 143, 23, 16, Muted);
        _balance = Label(currency, "Balance", "", 51, -28, 143, 33, 26, Steel);
        _balance.fontStyle = FontStyles.Bold;
    }

    public void SetBalance(string currencyName, int? balance)
    {
        _currencyName.text = string.IsNullOrWhiteSpace(currencyName) ? "강화석" : currencyName;
        _balance.text = balance?.ToString("N0") ?? "—";
    }

    public InventoryView BuildCodex(InventoryCatalog artwork, InventoryData entries)
    {
        var rect = Child(Root, "HubItemCodex");
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var view = rect.gameObject.AddComponent<InventoryView>();
        view.BuildCodex(artwork, entries);
        return view;
    }

    private static RectTransform Child(Transform parent, string name, float x = 0, float y = 0,
        float width = 0, float height = 0, Vector2? anchor = null)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor ?? TopLeft;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static UnityEngine.UI.Image Plate(RectTransform parent, Color color)
    {
        var image = parent.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private TMP_Text Label(RectTransform parent, string name, string value, float x, float y,
        float width, float height, float size, Color color)
    {
        var label = Child(parent, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) label.font = _font;
        label.text = value;
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }
}
