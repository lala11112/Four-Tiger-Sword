using System;
using TMPro;
using UnityEngine;

/// <summary>Prefab presentation only. The player still owns input, selection lifetime and world time.</summary>
[DisallowMultipleComponent]
public sealed class FinisherWheelView : MonoBehaviour
{
    [Serializable]
    public sealed class SectorBinding
    {
        public ElementType Element;
        [Tooltip("Name used when this sector has no text label (for an icon-only design).")]
        public string DisplayName;
        public TMP_Text ElementLabel;
        public TMP_Text RoleLabel;
        [Tooltip("Optional custom highlight. Shown only while this sector is selected.")]
        public GameObject SelectedVisual;

        [NonSerialized] internal FontStyles NormalFontStyle;
        [NonSerialized] internal Color NormalRoleColor;
    }

    [Header("Prefab bindings")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private RectTransform content;
    [SerializeField] private RectTransform virtualCursor;
    [Tooltip("Optional procedural ring. Replace it with authored images and sector highlights if desired.")]
    [SerializeField] private FinisherWheelGraphic wheel;
    [SerializeField] private TMP_Text centerName;
    [SerializeField] private TMP_Text selectionLabel;
    [SerializeField] private SectorBinding[] sectors = new SectorBinding[5];

    [Header("Selection presentation")]
    [SerializeField] private bool boldSelectedElement = true;
    [SerializeField] private bool tintCenterByElement = true;
    [SerializeField] private bool tintSelectionByElement = true;
    [SerializeField] private Color selectedRoleColor = new Color(0.97f, 0.98f, 1f);
    [SerializeField] private string currentRoleText = "현재 폼 · 3타";
    [SerializeField] private string otherRoleText = "기본 3타";
    [SerializeField] private string selectionSuffix = "  ·  기본 3타";

    private bool _initialized;
    private Color _normalCenterColor;
    private ElementType _current;

    public ElementType SelectedElement { get; private set; }
    public bool IsVisible { get; private set; }
    public float ScaleFactor => canvas != null ? Mathf.Max(0.0001f, canvas.scaleFactor) : 1f;

    public static FinisherWheelView Create(Transform owner = null)
    {
        var resources = Resources.Load<FinisherWheelResources>(FinisherWheelResources.ResourceName);
        var prefab = resources != null ? resources.Prefab : null;
        if (prefab == null)
            throw new InvalidOperationException("Assign a FinisherWheel prefab in FinisherWheelResources. "
                + "Use Tools/Studio Baekho/Finisher/Ensure UI Resources to create the default.");
        if (!prefab.ValidateBindings(out string error))
            throw new InvalidOperationException("Invalid FinisherWheel prefab: " + error);

        var view = Instantiate(prefab, owner, false);
        view.name = prefab.name;
        // Inactive custom prefabs must run Awake before Show, never after it.
        view.gameObject.SetActive(true);
        view.EnsureInitialized();
        view.Hide();
        return view;
    }

    private void Awake()
    {
        EnsureInitialized();
        Hide();
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        if (!ValidateBindings(out string error))
            throw new InvalidOperationException("FinisherWheel bindings: " + error);
        foreach (var sector in sectors)
        {
            if (sector.ElementLabel != null) sector.NormalFontStyle = sector.ElementLabel.fontStyle;
            if (sector.RoleLabel != null) sector.NormalRoleColor = sector.RoleLabel.color;
        }
        if (centerName != null) _normalCenterColor = centerName.color;
        _initialized = true;
    }

    /// <summary>Checks references without constructing or changing any UI objects.</summary>
    public bool ValidateBindings(out string error)
    {
        error = null;
        if (canvas == null || content == null || virtualCursor == null)
            error = "Canvas, Content and Virtual Cursor are required.";
        else if (!canvas.transform.IsChildOf(transform) || !content.IsChildOf(transform)
            || !virtualCursor.IsChildOf(content))
            error = "Canvas and Content must belong to this prefab; Virtual Cursor must be inside Content.";
        else if (sectors == null || sectors.Length != 5)
            error = "Exactly five sector bindings are required.";
        else
        {
            int found = 0;
            foreach (var sector in sectors)
            {
                int index = sector != null ? FinisherWheelSelection.IndexOf(sector.Element) : -1;
                if (index < 0 || (found & (1 << index)) != 0)
                {
                    error = "Each of Fire, Earth, Gold, Water and Wood must appear once.";
                    break;
                }
                if (!BelongsToContent(sector.ElementLabel) || !BelongsToContent(sector.RoleLabel)
                    || (sector.SelectedVisual != null && !sector.SelectedVisual.transform.IsChildOf(content)))
                {
                    error = "Sector labels and highlights must be inside Content.";
                    break;
                }
                found |= 1 << index;
            }
            if (error == null && (!BelongsToContent(wheel) || !BelongsToContent(centerName)
                || !BelongsToContent(selectionLabel)))
                error = "Wheel, center and selection labels must be inside Content.";
        }
        return error == null;
    }

    private bool BelongsToContent(Component component)
        => component == null || component.transform.IsChildOf(content);

    public void Show(ElementType current)
    {
        EnsureInitialized();
        _current = current;
        IsVisible = true;
        canvas.enabled = true;
        content.gameObject.SetActive(true);
        if (centerName != null) centerName.text = ElementName(current);
        UpdateSelection(Vector2.zero);
        Canvas.ForceUpdateCanvases();
    }

    public void UpdateSelection(Vector2 logicalCursor)
    {
        if (!IsVisible) return;
        ElementType resolved = FinisherWheelSelection.Resolve(logicalCursor);
        SelectedElement = resolved == ElementType.ELEMENT_NONE ? _current : resolved;
        int selected = FinisherWheelSelection.IndexOf(resolved);
        int current = FinisherWheelSelection.IndexOf(_current);
        if (wheel != null) wheel.SetSelection(selected, current);
        virtualCursor.anchoredPosition = Vector2.ClampMagnitude(logicalCursor, FinisherWheelSelection.OuterRadius);
        if (centerName != null && tintCenterByElement)
            centerName.color = selected < 0 ? ElementColor(_current) : _normalCenterColor;
        if (selectionLabel != null)
        {
            selectionLabel.text = ElementName(SelectedElement) + selectionSuffix;
            if (tintSelectionByElement) selectionLabel.color = ElementColor(SelectedElement);
        }
        foreach (var sector in sectors)
        {
            bool isSelected = sector.Element == resolved;
            if (sector.ElementLabel != null)
                sector.ElementLabel.fontStyle = sector.NormalFontStyle
                    | (isSelected && boldSelectedElement ? FontStyles.Bold : FontStyles.Normal);
            if (sector.RoleLabel != null)
            {
                sector.RoleLabel.text = sector.Element == _current ? currentRoleText : otherRoleText;
                sector.RoleLabel.color = isSelected ? selectedRoleColor : sector.NormalRoleColor;
            }
            if (sector.SelectedVisual != null) sector.SelectedVisual.SetActive(isSelected);
        }
    }

    public void Hide()
    {
        IsVisible = false;
        if (canvas != null) canvas.enabled = false;
        if (content != null) content.gameObject.SetActive(false);
    }

    private void OnDisable() => Hide();

    private SectorBinding Sector(ElementType element)
    {
        foreach (var sector in sectors) if (sector.Element == element) return sector;
        return null;
    }

    private string ElementName(ElementType element)
    {
        var sector = Sector(element);
        return sector?.ElementLabel != null ? sector.ElementLabel.text : sector?.DisplayName ?? "현재";
    }

    private Color ElementColor(ElementType element)
    {
        var sector = Sector(element);
        if (sector?.ElementLabel != null) return sector.ElementLabel.color;
        int index = FinisherWheelSelection.IndexOf(element);
        return wheel != null ? wheel.GetElementColor(index) : FinisherWheelGraphic.ElementColor(index);
    }
}
