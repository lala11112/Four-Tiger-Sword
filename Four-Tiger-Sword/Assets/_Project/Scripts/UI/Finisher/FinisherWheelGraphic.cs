using UnityEngine;

/// <summary>Texture-free, non-interactive annular wheel geometry.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class FinisherWheelGraphic : UnityEngine.UI.MaskableGraphic
{
    [SerializeField] private Color _fireColor = new Color(1f, 0.43f, 0.28f);
    [SerializeField] private Color _earthColor = new Color(0.79f, 0.61f, 0.40f);
    [SerializeField] private Color _goldColor = new Color(1f, 0.83f, 0.42f);
    [SerializeField] private Color _waterColor = new Color(0.35f, 0.72f, 1f);
    [SerializeField] private Color _woodColor = new Color(0.47f, 0.88f, 0.66f);
    [SerializeField] private Color _fallbackColor = new Color(0.88f, 0.91f, 0.95f);

    [SerializeField] private Color _backgroundColor = new Color(0.025f, 0.035f, 0.05f, 0.97f);
    [SerializeField] private Color _sectorBaseColor = new Color(0.065f, 0.08f, 0.105f, 1f);
    [SerializeField] private Color _centerColor = new Color(0.045f, 0.06f, 0.08f, 1f);
    [SerializeField] private Color _inactiveCenterRimColor = new Color(0.35f, 0.40f, 0.47f, 0.7f);
    [SerializeField, Range(0f, 1f)] private float _selectedFillBlend = 0.26f;
    [SerializeField, Range(0f, 1f)] private float _unselectedFillBlend = 0.07f;

    [SerializeField, Range(0f, 1f)] private float _selectedRimAlpha = 1f;
    [SerializeField, Range(0f, 1f)] private float _currentFormRimAlpha = 0.55f;
    [SerializeField, Range(0f, 1f)] private float _unselectedRimAlpha = 0.19f;
    [SerializeField, Range(0f, 60f)] private float _selectedRimWidth = 7f;
    [SerializeField, Range(0f, 60f)] private float _unselectedRimWidth = 3f;
    [SerializeField, Range(0f, 30f)] private float _centerRimWidth = 2f;
    [SerializeField, Range(0f, 30f)] private float _innerHighlightWidth = 2f;

    [Tooltip("Visual gap between adjacent sectors. The five selection sectors keep their fixed angles.")]
    [SerializeField, Range(0f, 30f)] private float _sectorGapDegrees = 1.4f;
    [SerializeField, Range(0f, 40f)] private float _backgroundPadding = 5f;
    [SerializeField, Range(0f, 60f)] private float _sectorInnerInset = 3f;
    [SerializeField, Range(0f, 60f)] private float _sectorOuterInset = 4f;
    [SerializeField, Range(0f, 30f)] private float _innerHighlightInset = 1f;
    [SerializeField, Range(0f, 30f)] private float _centerInset = 3f;

    private int _selection = -1;
    private int _current = -1;
    private int _lockedSectorMask;

    public void SetSelection(int selection, int current, int lockedSectorMask = 0)
    {
        if (_selection == selection && _current == current && _lockedSectorMask == lockedSectorMask)
            return;
        _selection = selection;
        _current = current;
        _lockedSectorMask = lockedSectorMask;
        SetVerticesDirty();
    }

    public static Color ElementColor(int index)
    {
        switch (index)
        {
            case 0: return new Color(1f, 0.43f, 0.28f);
            case 1: return new Color(0.79f, 0.61f, 0.40f);
            case 2: return new Color(1f, 0.83f, 0.42f);
            case 3: return new Color(0.35f, 0.72f, 1f);
            case 4: return new Color(0.47f, 0.88f, 0.66f);
            default: return new Color(0.88f, 0.91f, 0.95f);
        }
    }

    /// <summary>The authored palette used by this instance; static ElementColor supplies creation defaults only.</summary>
    public Color GetElementColor(int index)
    {
        switch (index)
        {
            case 0: return _fireColor;
            case 1: return _earthColor;
            case 2: return _goldColor;
            case 3: return _waterColor;
            case 4: return _woodColor;
            default: return _fallbackColor;
        }
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        const float inner = FinisherWheelSelection.InnerRadius;
        const float outer = FinisherWheelSelection.OuterRadius;
        Arc(mesh, 0f, outer + _backgroundPadding, 0f, 360f, _backgroundColor, 100);

        for (int i = 0; i < 5; i++)
        {
            bool locked = (_lockedSectorMask & (1 << i)) != 0;
            Color accent = locked ? _inactiveCenterRimColor : GetElementColor(i);
            bool selected = _selection == i;
            float halfGap = _sectorGapDegrees * 0.5f;
            float start = i * FinisherWheelSelection.SectorAngle - FinisherWheelSelection.SectorAngle * 0.5f + halfGap;
            float end = i * FinisherWheelSelection.SectorAngle + FinisherWheelSelection.SectorAngle * 0.5f - halfGap;
            Color fill = Color.Lerp(_sectorBaseColor, accent, selected ? _selectedFillBlend : _unselectedFillBlend);
            Arc(mesh, inner + _sectorInnerInset, outer - _sectorOuterInset, start, end, fill, 24);
            Color rim = accent;
            rim.a *= selected ? _selectedRimAlpha : i == _current ? _currentFormRimAlpha : _unselectedRimAlpha;
            Arc(mesh, outer - (selected ? _selectedRimWidth : _unselectedRimWidth), outer, start, end, rim, 24);
            if (selected)
                Arc(mesh, inner + _innerHighlightInset, inner + _innerHighlightInset + _innerHighlightWidth, start, end, accent, 24);
        }

        Arc(mesh, inner - _centerRimWidth, inner, 0f, 360f,
            _selection < 0 && _current >= 0 ? GetElementColor(_current) : _inactiveCenterRimColor, 64);
        Arc(mesh, 0f, inner - _centerInset, 0f, 360f, _centerColor, 64);
    }

    private void Arc(UnityEngine.UI.VertexHelper mesh, float inner, float outer,
        float start, float end, Color paintColor, int segments)
    {
        if (outer <= inner || end <= start)
            return;
        Color tint = paintColor * color;
        for (int i = 0; i < segments; i++)
        {
            float a = Mathf.Lerp(start, end, i / (float)segments) * Mathf.Deg2Rad;
            float b = Mathf.Lerp(start, end, (i + 1f) / segments) * Mathf.Deg2Rad;
            Vector2 first = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            Vector2 second = new Vector2(Mathf.Sin(b), Mathf.Cos(b));
            int vertex = mesh.currentVertCount;
            mesh.AddVert(first * inner, tint, Vector2.zero);
            mesh.AddVert(first * outer, tint, Vector2.zero);
            mesh.AddVert(second * outer, tint, Vector2.zero);
            mesh.AddVert(second * inner, tint, Vector2.zero);
            mesh.AddTriangle(vertex, vertex + 1, vertex + 2);
            mesh.AddTriangle(vertex, vertex + 2, vertex + 3);
        }
    }
}
