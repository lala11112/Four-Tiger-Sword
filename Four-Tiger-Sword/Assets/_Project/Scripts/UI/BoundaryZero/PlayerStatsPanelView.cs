using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Displays live player stats and expands one stat's sources inline.</summary>
[DisallowMultipleComponent]
public sealed class PlayerStatsPanelView : MonoBehaviour
{
    private const float ContentWidth = 420;
    private const float RowHeight = 67;
    private const float SourceHeight = 38;
    private const float PanelWidth = 490;
    private const float OpenDuration = .24f;
    private const float CloseDuration = .18f;
    private static readonly Vector2 TopLeft = new Vector2(0, 1);
    private static readonly Vector2 OpenPosition = new Vector2(-55, -115);
    private static readonly Vector2 ClosedPosition = new Vector2(PanelWidth + 8, OpenPosition.y);
    private static readonly Color Steel = new Color32(232, 238, 245, 255);
    private static readonly Color Muted = new Color32(200, 213, 225, 255);
    private static readonly Color Red = new Color32(237, 45, 77, 255);
    private static readonly Color Mint = new Color32(145, 217, 190, 255);
    private static readonly Color Neutral = new Color32(99, 123, 144, 255);
    private static readonly Color Rule = new Color32(169, 189, 213, 30);

    private sealed class StatRow
    {
        public RectTransform Root;
        public TMP_Text Label;
        public TMP_Text Value;
        public TMP_Text Delta;
        public GameObject Selected;
        public PlayerStatsReadModel.Entry Data;
    }

    private sealed class SourceRow
    {
        public RectTransform Root;
        public TMP_Text Label;
        public TMP_Text Value;
    }

    private readonly Dictionary<StatType, StatRow> _rows = new();
    private readonly List<SourceRow> _sources = new();
    private TMP_FontAsset _font;
    private Sprite _seal;
    private RectTransform _opener;
    private RectTransform _list;
    private RectTransform _accordion;
    private RectTransform _sourceArea;
    private UnityEngine.UI.ScrollRect _scroll;
    private CanvasGroup _panelGroup;
    private StatType? _selected;
    private bool _hasData;
    private bool _available = true;
    private float _transitionStart;
    private float _transitionElapsed;
    private float _transitionDuration;
    private Vector2 _openerOffset = new Vector2(-55, -55);

    public event Action OpenRequested;
    public event Action CloseRequested;
    public RectTransform Root { get; private set; }
    public RectTransform PanelRoot { get; private set; }
    public bool IsOpen { get; private set; }

    public void Build(bool showOpener = true)
    {
        if (Root != null) return;

        _font = Resources.Load<TMP_FontAsset>("BoundaryUI/Fonts/BoundaryFont");
        _seal = Resources.Load<Sprite>("BoundaryUI/Art/Seal");
        Root = Child(transform, "PlayerStatsCanvas", 0, 0, 0, 0);
        var canvas = Root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;
        var scaler = Root.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        Root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        if (showOpener)
        {
            _opener = Child(Root, "OpenStats", _openerOffset.x, _openerOffset.y, 126, 44, Vector2.one);
            Button(_opener, new Color32(11, 24, 40, 237), new Color32(32, 51, 72, 255))
                .onClick.AddListener(() => OpenRequested?.Invoke());
            Border(_opener, 126, 44, new Color32(191, 208, 220, 117));
            Seal(_opener, 13, -10, 20, 24);
            Label(_opener, "Label", "능력치", 41, 0, 77, 44, 19, Steel, bold: true);
        }

        PanelRoot = Child(Root, "StatsPanel", ClosedPosition.x, ClosedPosition.y, PanelWidth, 810, Vector2.one);
        _panelGroup = PanelRoot.gameObject.AddComponent<CanvasGroup>();
        PanelRoot.gameObject.AddComponent<PlayerStatsPanelChrome>().raycastTarget = true;
        BuildHeader();
        BuildList();
        SnapClosed();
    }

    public void Render(PlayerStatManager manager)
    {
        Build();
        _hasData = manager != null;
        foreach (StatType stat in PlayerStatsReadModel.Stats)
        {
            StatRow row = _rows[stat];
            if (!_hasData)
            {
                row.Data = default;
                row.Value.text = "—";
                row.Delta.text = string.Empty;
                continue;
            }

            row.Data = PlayerStatsReadModel.Read(manager, stat);
            row.Label.text = row.Data.Label;
            row.Value.text = row.Data.ValueText;
            bool positive = row.Data.DeltaText.StartsWith("+", StringComparison.Ordinal);
            bool negative = row.Data.DeltaText.StartsWith("-", StringComparison.Ordinal);
            row.Delta.text = positive || negative ? row.Data.DeltaText : "—";
            row.Delta.color = positive ? Mint : negative ? Red : Neutral;
        }
        UpdateAccordion();
    }

    public void Show()
    {
        Build();
        if (!_available || !isActiveAndEnabled || IsOpen) return;
        IsOpen = true;
        PanelRoot.gameObject.SetActive(true);
        _panelGroup.interactable = true;
        _panelGroup.blocksRaycasts = true;
        Select(StatType.ST_CRT);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_list);
        _scroll.StopMovement();
        _scroll.verticalNormalizedPosition = 1;
        UpdateOpener();
        StartTransition();
    }

    public void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _panelGroup.interactable = false;
        _panelGroup.blocksRaycasts = false;
        _scroll.StopMovement();
        StartTransition();
    }

    public void SetAvailable(bool available)
    {
        _available = available;
        if (available) UpdateOpener();
        else SnapClosed();
    }

    private void OnEnable() => UpdateOpener();

    private void OnDisable() => SnapClosed();

    private void StartTransition()
    {
        _transitionStart = _panelGroup.alpha;
        _transitionElapsed = 0;
        _transitionDuration = IsOpen
            ? OpenDuration * (1 - _transitionStart)
            : CloseDuration * _transitionStart;
        if (!IsOpen && _transitionDuration == 0) SnapClosed();
    }

    private void Update()
    {
        if (_transitionElapsed >= _transitionDuration) return;

        _transitionElapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(_transitionElapsed / _transitionDuration);
        if (!IsOpen && progress >= 1)
        {
            SnapClosed();
            return;
        }

        float easing = IsOpen ? 1 - Cube(1 - progress) : Cube(progress);
        ApplyVisibility(Mathf.Lerp(_transitionStart, IsOpen ? 1 : 0, easing));
    }

    private static float Cube(float value) => value * value * value;

    private void ApplyVisibility(float visibility)
    {
        _panelGroup.alpha = visibility;
        PanelRoot.anchoredPosition = Vector2.Lerp(ClosedPosition, OpenPosition, visibility);
    }

    private void SnapClosed()
    {
        IsOpen = false;
        _transitionDuration = 0;
        if (PanelRoot != null)
        {
            _panelGroup.interactable = false;
            _panelGroup.blocksRaycasts = false;
            ApplyVisibility(0);
            PanelRoot.gameObject.SetActive(false);
        }
        UpdateOpener();
    }

    public void SetOpenerOffset(Vector2 topRightOffset)
    {
        _openerOffset = topRightOffset;
        if (_opener != null) _opener.anchoredPosition = topRightOffset;
    }

    public string GetValueText(StatType stat) => _rows.TryGetValue(stat, out var row) ? row.Value.text : null;

    private void BuildHeader()
    {
        var header = Child(PanelRoot, "Header", 35, -35, ContentWidth, 59);
        Seal(header, 0, -10, 25, 29);
        Label(header, "Title", "능력치", 38, -1, 280, 43, 31, Steel, bold: true);
        Plate(Child(header, "Divider", 0, -58, ContentWidth, 1), new Color32(157, 179, 199, 81));
        Plate(Child(header, "SilverAccent", 0, -57, 71, 2), Steel);
        Plate(Child(header, "RedAccent", 78, -57, 32, 2), Red);

        var close = Child(header, "Close", 388, -8, 32, 32);
        Button(close, Color.clear, new Color32(142, 172, 207, 23))
            .onClick.AddListener(() => CloseRequested?.Invoke());
        foreach (float angle in new[] { -45f, 45f })
        {
            var stroke = Child(close, "CloseStroke", 0, 0, 18, 1.5f, new Vector2(.5f, .5f));
            stroke.localRotation = Quaternion.Euler(0, 0, angle);
            Plate(stroke, Muted);
        }
    }

    private void BuildList()
    {
        var scrollRoot = Child(PanelRoot, "StatsScroll", 35, -119, ContentWidth, 656);
        _scroll = scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        var viewport = Child(scrollRoot, "Viewport", 0, 0, ContentWidth, 656);
        Plate(viewport, Color.white, true);
        var mask = viewport.gameObject.AddComponent<UnityEngine.UI.Mask>();
        mask.showMaskGraphic = false;

        _list = Child(viewport, "Stats", 0, 0, ContentWidth, 0);
        var layout = _list.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        var fitter = _list.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        _scroll.viewport = viewport;
        _scroll.content = _list;
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 32;

        foreach (StatType stat in PlayerStatsReadModel.Stats)
            _rows.Add(stat, BuildStatRow(stat));

        _accordion = Child(_list, "Sources", 0, 0, ContentWidth, 0);
        _sourceArea = Child(_accordion, "SourceArea", 0, 0, ContentWidth, 0);
        Plate(_sourceArea, new Color32(10, 21, 37, 191));
        var accent = Child(_sourceArea, "RedAccent", 0, 0, 2, 0);
        accent.anchorMin = Vector2.zero;
        accent.offsetMin = Vector2.zero;
        accent.offsetMax = new Vector2(2, 0);
        Plate(accent, Red);
        var rule = Child(_sourceArea, "Divider", 0, 0, ContentWidth, 1, Vector2.zero);
        Plate(rule, new Color32(117, 152, 181, 57));
        _accordion.gameObject.SetActive(false);
    }

    private StatRow BuildStatRow(StatType stat)
    {
        var root = Child(_list, stat.ToString(), 0, 0, ContentWidth, RowHeight);
        var button = Button(root, Color.clear, new Color32(161, 189, 208, 11));
        button.onClick.AddListener(() => Select(_selected == stat ? (StatType?)null : stat));
        Plate(Child(root, "Divider", 0, -(RowHeight - 1), ContentWidth, 1), Rule);

        var selected = Child(root, "Selected", 0, 0, ContentWidth, RowHeight);
        selected.gameObject.AddComponent<PlayerStatsPanelChrome>().UseSelectionFill();
        Border(selected, ContentWidth, RowHeight, new Color32(201, 217, 229, 79));
        Plate(Child(selected, "RedAccent", 0, 0, 3, RowHeight), Red);
        selected.gameObject.SetActive(false);

        return new StatRow
        {
            Root = root,
            Selected = selected.gameObject,
            Label = Label(root, "Label", string.Empty, 11, 0, 218, RowHeight, 20, Muted),
            Value = Label(root, "Value", "—", 228, 0, 103, RowHeight, 26, Steel, TextAlignmentOptions.MidlineRight, true),
            Delta = Label(root, "Delta", string.Empty, 334, 0, 75, RowHeight, 14, Mint, TextAlignmentOptions.MidlineRight)
        };
    }

    private void Select(StatType? stat)
    {
        if (_selected.HasValue)
        {
            var previous = _rows[_selected.Value];
            previous.Selected.SetActive(false);
            previous.Label.color = Muted;
        }
        _selected = stat;
        if (_selected.HasValue)
        {
            var current = _rows[_selected.Value];
            current.Selected.SetActive(true);
            current.Label.color = Steel;
        }
        UpdateAccordion();
    }

    private void UpdateAccordion()
    {
        bool expanded = _selected.HasValue && _hasData;
        _accordion.gameObject.SetActive(expanded);
        if (!expanded) return;

        StatRow selected = _rows[_selected.Value];
        var data = selected.Data;
        var contributions = data.Contributions;
        int count = 1 + contributions.Count;
        while (_sources.Count < count)
            _sources.Add(BuildSourceRow(_sources.Count));

        SetSource(_sources[0], "기본", data.BaseText, Muted);
        for (int i = 0; i < contributions.Count; i++)
        {
            var contribution = contributions[i];
            bool loss = contribution.ValueText.StartsWith("-", StringComparison.Ordinal);
            SetSource(_sources[i + 1], contribution.Label, contribution.ValueText, loss ? Red : Mint);
        }
        for (int i = count; i < _sources.Count; i++)
            _sources[i].Root.gameObject.SetActive(false);

        float height = 12 + count * SourceHeight;
        _sourceArea.sizeDelta = new Vector2(ContentWidth, height);
        _accordion.sizeDelta = new Vector2(ContentWidth, height + 7);
        // Remove the accordion's existing index from the target calculation when it is above the selected row.
        int rowIndex = selected.Root.GetSiblingIndex();
        int accordionIndex = _accordion.GetSiblingIndex();
        _accordion.SetSiblingIndex(accordionIndex > rowIndex ? rowIndex + 1 : rowIndex);
    }

    private SourceRow BuildSourceRow(int index)
    {
        var root = Child(_sourceArea, index == 0 ? "Base" : "Contribution", 0, -5 - index * SourceHeight,
            ContentWidth, SourceHeight);
        var mark = Child(root, "Mark", 22, -14, 11, 11);
        mark.pivot = new Vector2(.5f, .5f);
        mark.anchoredPosition = new Vector2(28, -SourceHeight * .5f);
        mark.localRotation = Quaternion.Euler(0, 0, 45);
        Border(mark, 11, 11, new Color32(150, 174, 194, 255));

        return new SourceRow
        {
            Root = root,
            Label = Label(root, "Label", string.Empty, 48, 0, 238, SourceHeight, 16, Steel),
            Value = Label(root, "Value", string.Empty, 294, 0, 112, SourceHeight, 16, Muted,
                TextAlignmentOptions.MidlineRight, true)
        };
    }

    private static void SetSource(SourceRow row, string label, string value, Color tint)
    {
        row.Root.gameObject.SetActive(true);
        row.Label.text = label;
        row.Value.text = value;
        row.Value.color = tint;
    }

    private void UpdateOpener()
    {
        if (_opener != null)
            _opener.gameObject.SetActive(_available && isActiveAndEnabled
                && (PanelRoot == null || !PanelRoot.gameObject.activeSelf));
    }

    private void Seal(RectTransform parent, float x, float y, float width, float height)
    {
        if (_seal == null) return;
        var image = Plate(Child(parent, "Seal", x, y, width, height), Color.white);
        image.sprite = _seal;
        image.preserveAspect = true;
    }

    private TMP_Text Label(RectTransform parent, string name, string text, float x, float y, float width, float height,
        float fontSize, Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool bold = false)
    {
        var label = Child(parent, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) label.font = _font;
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.color = color;
        label.alignment = align;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        label.richText = false;
        return label;
    }

    private static UnityEngine.UI.Button Button(RectTransform rect, Color fill, Color hover)
    {
        var plate = Plate(rect, Color.white, true);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = plate;
        var colors = button.colors;
        colors.normalColor = fill;
        colors.highlightedColor = hover;
        colors.pressedColor = hover;
        colors.selectedColor = hover;
        colors.fadeDuration = .1f;
        button.colors = colors;
        return button;
    }

    private static void Border(RectTransform rect, float width, float height, Color tint)
    {
        Plate(Child(rect, "Top", 0, 0, width, 1), tint);
        Plate(Child(rect, "Bottom", 0, -(height - 1), width, 1), tint);
        Plate(Child(rect, "Left", 0, 0, 1, height), tint);
        Plate(Child(rect, "Right", width - 1, 0, 1, height), tint);
    }

    private static UnityEngine.UI.Image Plate(RectTransform rect, Color color, bool raycast = false)
    {
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    private static RectTransform Child(Transform parent, string name, float x, float y,
        float width, float height, Vector2? anchor = null)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor ?? TopLeft;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }
}
