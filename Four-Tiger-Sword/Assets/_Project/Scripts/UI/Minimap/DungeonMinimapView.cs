using System.Collections.Generic;
using UnityEngine;

/// <summary>Draws a generated dungeon with a fixed player and scrolling, clipped map geometry.</summary>
[DisallowMultipleComponent]
public sealed class DungeonMinimapView : MonoBehaviour
{
    [SerializeField] private CanvasGroup visibility;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RectTransform mapContent;
    [SerializeField] private RectTransform markerLayer;
    [SerializeField] private DungeonMinimapIconGraphic playerArrow;
    [SerializeField, Min(1f)] private float worldSpan = 100f;
    [SerializeField] private Color visitedColor = new Color(.17f, .31f, .35f, .95f);
    [SerializeField] private Color currentColor = new Color(.32f, .72f, .73f, 1f);
    [SerializeField] private Color entranceColor = new Color(.27f, .44f, .48f, .9f);

    private sealed class AreaVisual
    {
        public RectTransform Rect;
        public UnityEngine.UI.Image Image;
    }

    private sealed class MarkerVisual
    {
        public int OwnerAreaIndex;
        public Vector2 LocalPosition;
        public RectTransform Rect;
    }

    private readonly List<AreaVisual> _areas = new List<AreaVisual>();
    private readonly List<AreaVisual> _stubs = new List<AreaVisual>();
    private readonly List<MarkerVisual> _markers = new List<MarkerVisual>();
    private DungeonMinimapModel _model;
    private Vector2 _playerPosition;
    private Vector2 _lastViewportSize;
    private float _lastSpan, _pixelsPerMetre;
    private int _renderedRevision = -1;
    private bool _hasLayout;

    public float WorldSpan
    {
        get => Mathf.Max(1f, worldSpan);
        set { worldSpan = Mathf.Max(1f, value); _hasLayout = false; }
    }
    public bool IsVisible => isActiveAndEnabled && visibility != null && visibility.alpha > 0f;
    public int BuiltAreaCount => _areas.Count;

    public void Configure(CanvasGroup targetVisibility, RectTransform targetViewport, RectTransform targetMapContent,
        RectTransform targetMarkerLayer, DungeonMinimapIconGraphic targetPlayerArrow)
    {
        SetVisible(false);
        visibility = targetVisibility;
        viewport = targetViewport;
        mapContent = targetMapContent;
        markerLayer = targetMarkerLayer;
        playerArrow = targetPlayerArrow;
        _hasLayout = false;
        if (mapContent != null) Center(mapContent);
        if (playerArrow != null)
        {
            Center(playerArrow.rectTransform);
            playerArrow.rectTransform.sizeDelta = new Vector2(18f, 22f);
            playerArrow.SetSymbol(DungeonMinimapIconGraphic.Symbol.Player);
            playerArrow.color = Color.white;
            playerArrow.raycastTarget = false;
            playerArrow.gameObject.SetActive(false);
        }
        SetVisible(false);
    }

    public void Build(DungeonMinimapModel model)
    {
        Clear();
        if (model == null || viewport == null || mapContent == null || markerLayer == null || playerArrow == null) return;
        _model = model;
        for (int i = 0; i < model.Areas.Count; i++) _areas.Add(CreateArea("Area_" + i, mapContent));
        for (int i = 0; i < model.Stubs.Count; i++) _stubs.Add(CreateArea("Entrance_" + i, mapContent));
        playerArrow.gameObject.SetActive(true);
        playerArrow.transform.SetAsLastSibling();
        EnsureLayout();
        RefreshExploration(model);
    }

    public void AddMarker(int ownerAreaIndex, Vector2 localXZ, DungeonMinimapIconGraphic.Symbol symbol)
    {
        if (_model == null || markerLayer == null || ownerAreaIndex < 0 || ownerAreaIndex >= _model.Areas.Count) return;
        var instance = new GameObject(symbol + "Marker", typeof(RectTransform), typeof(CanvasRenderer), typeof(DungeonMinimapIconGraphic));
        var rect = (RectTransform)instance.transform;
        rect.SetParent(markerLayer, false);
        Center(rect);
        rect.sizeDelta = new Vector2(22f, 22f);
        var graphic = instance.GetComponent<DungeonMinimapIconGraphic>();
        graphic.SetSymbol(symbol);
        graphic.color = symbol == DungeonMinimapIconGraphic.Symbol.Shop
            ? new Color(1f, .77f, .28f, 1f) : new Color(.45f, .94f, .62f, 1f);
        var marker = new MarkerVisual { OwnerAreaIndex = ownerAreaIndex, LocalPosition = localXZ, Rect = rect };
        _markers.Add(marker);
        UpdateMarker(marker);
        if (playerArrow != null) playerArrow.transform.SetAsLastSibling();
    }

    public void RefreshExploration(DungeonMinimapModel model)
    {
        if (_model == null || model != _model || _areas.Count != model.Areas.Count || _stubs.Count != model.Stubs.Count) return;
        EnsureLayout();
        if (_renderedRevision == model.Revision) return;
        for (int i = 0; i < _areas.Count; i++)
        {
            var visual = _areas[i];
            visual.Rect.gameObject.SetActive(model.Areas[i].Visited);
            visual.Image.color = i == model.CurrentAreaIndex ? currentColor : visitedColor;
        }
        for (int i = 0; i < _stubs.Count; i++)
        {
            int owner = model.Stubs[i].OwnerAreaIndex;
            _stubs[i].Rect.gameObject.SetActive(owner >= 0 && owner < model.Areas.Count && model.Areas[owner].Visited);
            _stubs[i].Image.color = entranceColor;
        }
        _renderedRevision = model.Revision;
        UpdateMarkers();
    }

    public void SetPose(Vector2 playerLocalXZ, Vector2 forwardLocalXZ)
    {
        _playerPosition = playerLocalXZ;
        if (_model == null) return;
        EnsureLayout();
        if (mapContent != null) mapContent.anchoredPosition = -_playerPosition * _pixelsPerMetre;
        if (playerArrow != null && forwardLocalXZ.sqrMagnitude > .0001f)
            playerArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(forwardLocalXZ.x, forwardLocalXZ.y) * Mathf.Rad2Deg);
        UpdateMarkers();
    }

    public void SetVisible(bool value)
    {
        if (visibility == null) return;
        visibility.alpha = value && isActiveAndEnabled && _model != null ? 1f : 0f;
        visibility.interactable = false;
        visibility.blocksRaycasts = false;
    }

    public void Clear()
    {
        SetVisible(false);
        for (int i = 0; i < _areas.Count; i++) Remove(_areas[i].Rect);
        for (int i = 0; i < _stubs.Count; i++) Remove(_stubs[i].Rect);
        for (int i = 0; i < _markers.Count; i++) Remove(_markers[i].Rect);
        _areas.Clear();
        _stubs.Clear();
        _markers.Clear();
        _model = null;
        _renderedRevision = -1;
        _hasLayout = false;
        _playerPosition = Vector2.zero;
        if (mapContent != null) mapContent.anchoredPosition = Vector2.zero;
        if (playerArrow != null) playerArrow.gameObject.SetActive(false);
    }

    private void OnDisable() => SetVisible(false);

    private void EnsureLayout()
    {
        if (_model == null || viewport == null || mapContent == null) return;
        Vector2 size = viewport.rect.size;
        float span = WorldSpan;
        if (_hasLayout && size == _lastViewportSize && Mathf.Approximately(span, _lastSpan)) return;
        _lastViewportSize = size;
        _lastSpan = span;
        _hasLayout = true;
        _pixelsPerMetre = Mathf.Max(0f, Mathf.Min(size.x, size.y)) / span;
        mapContent.sizeDelta = size;
        for (int i = 0; i < _areas.Count; i++)
        {
            var area = _model.Areas[i];
            Layout(_areas[i].Rect, area.Center, area.Size, area.RotationDegrees);
        }
        for (int i = 0; i < _stubs.Count; i++)
        {
            var stub = _model.Stubs[i];
            Layout(_stubs[i].Rect, stub.Center, stub.Size, stub.RotationDegrees);
        }
        mapContent.anchoredPosition = -_playerPosition * _pixelsPerMetre;
    }

    private void Layout(RectTransform rect, Vector2 center, Vector2 size, float rotation)
    {
        rect.anchoredPosition = center * _pixelsPerMetre;
        rect.sizeDelta = size * _pixelsPerMetre;
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
    }

    private void UpdateMarkers()
    {
        for (int i = 0; i < _markers.Count; i++) UpdateMarker(_markers[i]);
    }

    private void UpdateMarker(MarkerVisual marker)
    {
        Vector2 position = (marker.LocalPosition - _playerPosition) * _pixelsPerMetre;
        marker.Rect.anchoredPosition = position;
        Vector2 available = viewport.rect.size * .5f - marker.Rect.sizeDelta * .5f;
        bool discovered = _model != null && marker.OwnerAreaIndex >= 0 && marker.OwnerAreaIndex < _model.Areas.Count
            && _model.Areas[marker.OwnerAreaIndex].Visited;
        bool inside = Mathf.Abs(position.x) <= available.x && Mathf.Abs(position.y) <= available.y;
        marker.Rect.gameObject.SetActive(discovered && inside);
    }

    private static AreaVisual CreateArea(string name, RectTransform parent)
    {
        var instance = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        var rect = (RectTransform)instance.transform;
        rect.SetParent(parent, false);
        Center(rect);
        var image = instance.GetComponent<UnityEngine.UI.Image>();
        image.raycastTarget = false;
        instance.SetActive(false);
        return new AreaVisual { Rect = rect, Image = image };
    }

    private static void Center(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }

    private static void Remove(RectTransform rect)
    {
        if (rect == null) return;
        rect.gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(rect.gameObject);
        else DestroyImmediate(rect.gameObject);
    }
}
