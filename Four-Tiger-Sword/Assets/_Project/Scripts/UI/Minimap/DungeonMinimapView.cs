using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Draws the same explored dungeon as a following minimap or a fixed overview.</summary>
[DisallowMultipleComponent]
public sealed class DungeonMinimapView : MonoBehaviour
{
    public enum MapMode { FollowPlayer, EntireDungeon }

    [SerializeField] private CanvasGroup visibility;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RectTransform mapContent;
    [SerializeField] private RectTransform markerLayer;
    [SerializeField] private DungeonMinimapIconGraphic playerArrow;
    [SerializeField] private TMP_Text stageLabel;
    [SerializeField] private MapMode mode;
    [SerializeField, Min(1f)] private float worldSpan = 100f;
    [SerializeField, Min(0f)] private float entireDungeonPadding = 24f;
    [SerializeField] private Color visitedColor = new Color(.17f, .31f, .35f, .95f);
    [SerializeField] private Color currentColor = new Color(.32f, .72f, .73f, 1f);
    [SerializeField] private Color entranceColor = new Color(.27f, .44f, .48f, .9f);
    [Header("Replaceable icons")]
    [SerializeField] private Sprite playerSprite;
    [SerializeField] private Sprite shopSprite;
    [SerializeField] private Sprite exitSprite;
    [SerializeField] private Vector2 playerIconSize = new Vector2(18f, 22f);
    [SerializeField] private Vector2 markerIconSize = new Vector2(22f, 22f);
    [SerializeField] private Color playerColor = Color.white;
    [SerializeField] private Color shopColor = new Color(1f, .77f, .28f, 1f);
    [SerializeField] private Color exitColor = new Color(.45f, .94f, .62f, 1f);
    [Header("Enemy markers")]
    [SerializeField] private Sprite enemySprite;
    [SerializeField] private Color enemyColor = new Color(1f, .24f, .20f, 1f);
    [SerializeField] private Vector2 enemyIconSize = new Vector2(8f, 8f);
    [Header("Full-map travel")]
    [SerializeField] private DungeonMapPointerHandler pointerHandler;
    [SerializeField] private UnityEngine.UI.Graphic inputBlocker;
    [SerializeField] private TMP_Text travelStatus;
    [SerializeField] private BoundaryZeroBackdrop modalBackdrop;
    [SerializeField] private Color availableTravelColor = new Color(.55f, .95f, .72f, 1f);
    [SerializeField] private Color blockedTravelColor = new Color(1f, .48f, .39f, 1f);

    public event Action<int> RoomClicked;
    public event Action Disabled;
    public event Action CloseRequested;
    public void RequestClose() => CloseRequested?.Invoke();

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
        public DungeonMinimapIconGraphic Graphic;
    }

    private readonly List<AreaVisual> _areas = new List<AreaVisual>();
    private readonly List<AreaVisual> _stubs = new List<AreaVisual>();
    private readonly List<MarkerVisual> _markers = new List<MarkerVisual>();
    private readonly List<DungeonMinimapIconGraphic> _enemyPool = new List<DungeonMinimapIconGraphic>();
    private IReadOnlyList<Vector2> _enemyPositions;
    private int _enemyCount;
    private DungeonMinimapModel _model;
    private Vector2 _playerPosition;
    private Vector2 _dungeonCenter, _dungeonSize;
    private Vector2 _lastViewportSize;
    private float _lastSpan, _lastPadding, _pixelsPerMetre;
    private MapMode _lastMode;
    private int _renderedRevision = -1;
    private bool _hasLayout, _stylesDirty = true;
    private Func<int, string> _getTravelBlockReason;
    private int _hoveredAreaIndex = -1;
    private RectTransform _hoverOutline;
    private readonly List<UnityEngine.UI.Image> _hoverEdges = new List<UnityEngine.UI.Image>();
    private string _travelMessage;
    private Vector2 _pointerPosition;
    private Camera _pointerCamera;
    private bool _hasPointer;
    private bool _requestedVisible;
    private UIModalMotion _motion;

    public MapMode Mode
    {
        get => mode;
        set
        {
            if (mode == value) return;
            mode = value;
            _hasLayout = false;
            if (mode != MapMode.FollowPlayer) HideEnemyDots();
        }
    }

    public float WorldSpan
    {
        get => Mathf.Max(1f, worldSpan);
        set { worldSpan = Mathf.Max(1f, value); _hasLayout = false; }
    }
    public bool IsVisible => isActiveAndEnabled && visibility != null && visibility.alpha > 0f;
    public bool IsAnimating => _motion != null && _motion.IsAnimating;
    public bool IsClosing => _motion != null && _motion.IsClosing;
    public int BuiltAreaCount => _areas.Count;
    public int EnemyMarkerPoolCount => _enemyPool.Count;
    public int VisibleEnemyMarkerCount { get; private set; }
    public TMP_Text StageLabel => stageLabel;

    public void ConfigureStageLabel(TMP_Text label)
    {
        stageLabel = label;
        SetStageLabel(string.Empty);
    }

    public void SetStageLabel(string label)
    {
        if (stageLabel == null) return;
        label ??= string.Empty;
        if (stageLabel.text != label) stageLabel.text = label;
        bool show = mode == MapMode.FollowPlayer && !string.IsNullOrEmpty(label);
        if (stageLabel.gameObject.activeSelf != show) stageLabel.gameObject.SetActive(show);
    }

    /// <summary>The presenter supplies only living enemies from the current room, in generated-root XZ.</summary>
    public void SetEnemyPositions(IReadOnlyList<Vector2> localXZ)
    {
        if (mode != MapMode.FollowPlayer || _model == null || markerLayer == null || viewport == null
            || localXZ == null || localXZ.Count == 0)
        {
            HideEnemyDots();
            return;
        }
        _enemyPositions = localXZ;
        _enemyCount = localXZ.Count;
        EnsureLayout();
        while (_enemyPool.Count < _enemyCount)
        {
            var instance = new GameObject("EnemyMarker_" + _enemyPool.Count, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(DungeonMinimapIconGraphic));
            var rect = (RectTransform)instance.transform;
            rect.SetParent(markerLayer, false);
            Center(rect);
            // Destination icons stay above enemy dots, and the player remains above the marker layer.
            rect.SetAsFirstSibling();
            var graphic = instance.GetComponent<DungeonMinimapIconGraphic>();
            graphic.SetSymbol(DungeonMinimapIconGraphic.Symbol.Enemy);
            StyleEnemy(graphic);
            _enemyPool.Add(graphic);
        }
        if (playerArrow != null) playerArrow.transform.SetAsLastSibling();
        UpdateEnemyDots();
    }

    public void ConfigureTravelUI(DungeonMapPointerHandler targetPointer, TMP_Text targetStatus,
        UnityEngine.UI.Graphic targetInputBlocker)
    {
        pointerHandler = targetPointer;
        travelStatus = targetStatus;
        inputBlocker = targetInputBlocker;
        if (pointerHandler != null) pointerHandler.Configure(this, pointerHandler.GetComponent<UnityEngine.UI.Graphic>());
        RefreshTravelStatus();
        SetInteraction(IsVisible && mode == MapMode.EntireDungeon);
    }

    /// <summary>A null provider or an empty result allows travel; the caller remains authoritative at click time.</summary>
    public void SetTravelAvailability(Func<int, string> getBlockReason)
    {
        _getTravelBlockReason = getBlockReason;
        RefreshHover();
    }

    public void ShowTravelMessage(string message)
    {
        _travelMessage = message;
        RefreshTravelStatus();
    }

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
        _stylesDirty = true;
        if (mapContent != null) Center(mapContent);
        if (playerArrow != null)
        {
            Center(playerArrow.rectTransform);
            playerArrow.SetSymbol(DungeonMinimapIconGraphic.Symbol.Player);
            playerArrow.raycastTarget = false;
            playerArrow.gameObject.SetActive(false);
        }
        ApplyIconStyles();
        SetVisible(false);
    }

    public void Build(DungeonMinimapModel model)
    {
        Clear();
        if (model == null || viewport == null || mapContent == null || markerLayer == null || playerArrow == null) return;
        _model = model;
        MeasureDungeon();
        for (int i = 0; i < model.Areas.Count; i++) _areas.Add(CreateArea("Area_" + i, mapContent));
        for (int i = 0; i < model.Stubs.Count; i++) _stubs.Add(CreateArea("Entrance_" + i, mapContent));
        playerArrow.gameObject.SetActive(true);
        playerArrow.transform.SetAsLastSibling();
        EnsureLayout();
        RefreshExploration(model);
    }

    public void AddMarker(int ownerAreaIndex, Vector2 localXZ, DungeonMinimapIconGraphic.Symbol symbol)
        => AddTrackedMarker(ownerAreaIndex, localXZ, symbol);

    internal DungeonMinimapIconGraphic AddTrackedMarker(int ownerAreaIndex, Vector2 localXZ, DungeonMinimapIconGraphic.Symbol symbol)
    {
        if (_model == null || markerLayer == null || ownerAreaIndex < 0 || ownerAreaIndex >= _model.Areas.Count) return null;
        var instance = new GameObject(symbol + "Marker", typeof(RectTransform), typeof(CanvasRenderer), typeof(DungeonMinimapIconGraphic));
        var rect = (RectTransform)instance.transform;
        rect.SetParent(markerLayer, false);
        Center(rect);
        var graphic = instance.GetComponent<DungeonMinimapIconGraphic>();
        graphic.SetSymbol(symbol);
        StyleMarker(graphic);
        if (symbol == DungeonMinimapIconGraphic.Symbol.Boss) AddBossLabel(rect, graphic.color);
        var marker = new MarkerVisual { OwnerAreaIndex = ownerAreaIndex, LocalPosition = localXZ, Rect = rect, Graphic = graphic };
        _markers.Add(marker);
        UpdateMarker(marker);
        if (playerArrow != null) playerArrow.transform.SetAsLastSibling();
        return graphic;
    }

    private static void AddBossLabel(RectTransform icon, Color color)
    {
        var label = new GameObject("BossLabel", typeof(RectTransform), typeof(TextMeshProUGUI))
            .GetComponent<TextMeshProUGUI>();
        var rect = label.rectTransform;
        rect.SetParent(icon, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -2f);
        rect.sizeDelta = new Vector2(68f, 28f);
        label.font = Resources.Load<TMP_FontAsset>("BoundaryUI/Fonts/BoundaryFont") ?? TMP_Settings.defaultFontAsset;
        label.text = "보스";
        label.fontSize = 22f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color;
        label.raycastTarget = false;
    }

    internal void RemoveMarker(DungeonMinimapIconGraphic graphic)
    {
        if (graphic == null) return;
        int index = _markers.FindIndex(marker => marker.Graphic == graphic);
        if (index < 0) return;
        _markers.RemoveAt(index);
        graphic.gameObject.SetActive(false);
        Destroy(graphic.gameObject);
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
        if (mapContent != null) mapContent.anchoredPosition = -MapOrigin * _pixelsPerMetre;
        if (playerArrow != null) playerArrow.rectTransform.anchoredPosition = (_playerPosition - MapOrigin) * _pixelsPerMetre;
        if (playerArrow != null && forwardLocalXZ.sqrMagnitude > .0001f)
            playerArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(forwardLocalXZ.x, forwardLocalXZ.y) * Mathf.Rad2Deg);
        UpdateMarkers();
        UpdateEnemyDots();
        RefreshHover();
    }

    public void SetVisible(bool value)
    {
        if (visibility == null) return;
        bool show = value && isActiveAndEnabled && _model != null;
        if (_requestedVisible == show && show)
        {
            UpdateEnemyDots();
            return;
        }
        _requestedVisible = show;
        visibility.alpha = show ? 1f : 0f;
        if (!show)
        {
            _motion?.HideImmediate();
            modalBackdrop?.Hide();
            SetInteraction(false);
            ClearPointer();
            HideEnemyDots();
            return;
        }
        if (mode == MapMode.EntireDungeon)
        {
            SetInteraction(false);
            modalBackdrop?.Show();
            EnsureMotion();
            if (_motion == null) SetInteraction(true);
            else _motion.PlayShow(() =>
            {
                if (_requestedVisible && isActiveAndEnabled) SetInteraction(true);
            });
        }
        else SetInteraction(false);
        UpdateEnemyDots();
    }

    /// <summary>The presenter retains the map session until this visual exit completes.</summary>
    public void PlayClose(Action completed)
    {
        if (!_requestedVisible || mode != MapMode.EntireDungeon)
        { completed?.Invoke(); return; }
        SetInteraction(false);
        ClearPointer();
        EnsureMotion();
        if (_motion != null) _motion.PlayClose(completed);
        else completed?.Invoke();
    }

    private void EnsureMotion()
    {
        if (_motion != null) return;
        // The backdrop captures by hiding BoundaryZeroSharp. Animate the panel's own
        // CanvasGroup so capture restoration cannot overwrite the presentation alpha.
        var panel = viewport != null ? viewport.parent as RectTransform : transform as RectTransform;
        _motion = UIModalMotion.Ensure(gameObject, panel, .18f, .14f, 0f);
        UIButtonMotion.Install(gameObject);
    }

    public void Clear()
    {
        SetVisible(false);
        SetStageLabel(string.Empty);
        for (int i = 0; i < _areas.Count; i++) Remove(_areas[i].Rect);
        for (int i = 0; i < _stubs.Count; i++) Remove(_stubs[i].Rect);
        for (int i = 0; i < _markers.Count; i++) Remove(_markers[i].Rect);
        for (int i = 0; i < _enemyPool.Count; i++) Remove(_enemyPool[i].rectTransform);
        _enemyPool.Clear();
        Remove(_hoverOutline);
        _hoverOutline = null;
        _hoverEdges.Clear();
        _hoveredAreaIndex = -1;
        _hasPointer = false;
        _travelMessage = null;
        _areas.Clear();
        _stubs.Clear();
        _markers.Clear();
        _model = null;
        _renderedRevision = -1;
        _hasLayout = false;
        _stylesDirty = true;
        _playerPosition = Vector2.zero;
        _dungeonCenter = _dungeonSize = Vector2.zero;
        if (mapContent != null) mapContent.anchoredPosition = Vector2.zero;
        if (playerArrow != null) playerArrow.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        SetVisible(false);
        Disabled?.Invoke();
    }

    private void SetInteraction(bool interactive)
    {
        if (visibility != null)
        {
            visibility.interactable = interactive;
            visibility.blocksRaycasts = _requestedVisible && mode == MapMode.EntireDungeon;
        }
        if (pointerHandler != null) pointerHandler.SetInputEnabled(interactive);
        if (inputBlocker != null) inputBlocker.raycastTarget = _requestedVisible && mode == MapMode.EntireDungeon;
    }

    public void UpdatePointer(Vector2 screenPosition, Camera eventCamera)
    {
        if (IsAnimating) return;
        _pointerPosition = screenPosition;
        _pointerCamera = eventCamera;
        _hasPointer = true;
        RefreshHover();
    }

    public void ClearPointer()
    {
        _hasPointer = false;
        _hoveredAreaIndex = -1;
        if (_hoverOutline != null) _hoverOutline.gameObject.SetActive(false);
        RefreshTravelStatus();
    }

    public void ClickPointer(Vector2 screenPosition, Camera eventCamera)
    {
        if (IsAnimating) return;
        UpdatePointer(screenPosition, eventCamera);
        if (_hoveredAreaIndex < 0 || _model == null) return;
        int roomId = _model.Areas[_hoveredAreaIndex].RoomId;
        string reason = _getTravelBlockReason?.Invoke(roomId);
        if (!string.IsNullOrEmpty(reason)) { ShowTravelMessage(reason); return; }
        RoomClicked?.Invoke(roomId);
    }

    /// <summary>Uses rendered rectangles, so Canvas scaling and clipped geometry share the same hit test.</summary>
    public bool TryGetRoomAtScreenPoint(Vector2 screenPosition, Camera eventCamera, out int roomId)
    {
        int index = PickRoom(screenPosition, eventCamera);
        roomId = index >= 0 ? _model.Areas[index].RoomId : -1;
        return index >= 0;
    }

    private int PickRoom(Vector2 screenPosition, Camera eventCamera)
    {
        if (!IsVisible || IsAnimating || mode != MapMode.EntireDungeon || _model == null || viewport == null
            || !RectTransformUtility.RectangleContainsScreenPoint(viewport, screenPosition, eventCamera)) return -1;
        EnsureLayout();
        for (int i = _markers.Count - 1; i >= 0; --i)
        {
            var marker = _markers[i];
            if (marker.Rect.gameObject.activeInHierarchy && IsVisitedRoom(marker.OwnerAreaIndex)
                && RectTransformUtility.RectangleContainsScreenPoint(marker.Rect, screenPosition, eventCamera))
                return marker.OwnerAreaIndex;
        }
        for (int i = _areas.Count - 1; i >= 0; --i)
            if (IsVisitedRoom(i) && _areas[i].Rect.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(_areas[i].Rect, screenPosition, eventCamera)) return i;
        return -1;
    }

    private bool IsVisitedRoom(int index)
        => _model != null && index >= 0 && index < _model.Areas.Count
            && _model.Areas[index].Kind == DungeonMinimapAreaKind.Room && _model.Areas[index].Visited;

    private void RefreshHover()
    {
        int previous = _hoveredAreaIndex;
        _hoveredAreaIndex = _hasPointer ? PickRoom(_pointerPosition, _pointerCamera) : -1;
        if (_hoveredAreaIndex != previous) _travelMessage = null;
        if (_hoveredAreaIndex < 0)
        {
            if (_hoverOutline != null) _hoverOutline.gameObject.SetActive(false);
            RefreshTravelStatus();
            return;
        }
        EnsureHoverOutline();
        var area = _model.Areas[_hoveredAreaIndex];
        _hoverOutline.anchoredPosition = (area.Center - MapOrigin) * _pixelsPerMetre;
        _hoverOutline.sizeDelta = area.Size * _pixelsPerMetre;
        _hoverOutline.localRotation = Quaternion.Euler(0f, 0f, area.RotationDegrees);
        string reason = _getTravelBlockReason?.Invoke(area.RoomId);
        Color tint = string.IsNullOrEmpty(reason) ? availableTravelColor : blockedTravelColor;
        for (int i = 0; i < _hoverEdges.Count; i++) _hoverEdges[i].color = tint;
        _hoverOutline.gameObject.SetActive(true);
        RefreshTravelStatus(reason);
    }

    private void RefreshTravelStatus(string hoverBlockReason = null)
    {
        if (travelStatus == null) return;
        if (_hoveredAreaIndex >= 0 && _model != null && hoverBlockReason == null)
            hoverBlockReason = _getTravelBlockReason?.Invoke(_model.Areas[_hoveredAreaIndex].RoomId);
        string text = !string.IsNullOrEmpty(_travelMessage) ? _travelMessage : _hoveredAreaIndex >= 0
            ? string.IsNullOrEmpty(hoverBlockReason) ? "클릭하여 이 방으로 이동" : hoverBlockReason
            : "방문한 방을 클릭하면 이동합니다.";
        if (travelStatus.text != text) travelStatus.text = text;
    }

    private void EnsureHoverOutline()
    {
        if (_hoverOutline != null) return;
        var root = new GameObject("TravelHighlight", typeof(RectTransform));
        _hoverOutline = (RectTransform)root.transform;
        _hoverOutline.SetParent(viewport, false);
        Center(_hoverOutline);
        _hoverOutline.SetSiblingIndex(mapContent.GetSiblingIndex() + 1);
        CreateHoverEdge("Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, .5f), new Vector2(2f, 0f));
        CreateHoverEdge("Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, .5f), new Vector2(2f, 0f));
        CreateHoverEdge("Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(.5f, 1f), new Vector2(0f, 2f));
        CreateHoverEdge("Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 2f));
    }

    private void CreateHoverEdge(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
    {
        var edge = CreateArea(name, _hoverOutline);
        edge.Rect.anchorMin = anchorMin;
        edge.Rect.anchorMax = anchorMax;
        edge.Rect.pivot = pivot;
        edge.Rect.sizeDelta = size;
        edge.Rect.gameObject.SetActive(true);
        _hoverEdges.Add(edge.Image);
    }

    private void OnValidate()
    {
        _hasLayout = false;
        _stylesDirty = true;
        _renderedRevision = -1;
    }

    private Vector2 MapOrigin => mode == MapMode.EntireDungeon ? _dungeonCenter : _playerPosition;

    private void EnsureLayout()
    {
        if (_model == null || viewport == null || mapContent == null) return;
        Vector2 size = viewport.rect.size;
        float span = WorldSpan;
        ApplyIconStyles();
        float padding = Mathf.Max(0f, entireDungeonPadding);
        if (_hasLayout && size == _lastViewportSize && Mathf.Approximately(span, _lastSpan)
            && mode == _lastMode && Mathf.Approximately(padding, _lastPadding)) return;
        _lastViewportSize = size;
        _lastSpan = span;
        _lastMode = mode;
        _lastPadding = padding;
        _hasLayout = true;
        _pixelsPerMetre = mode == MapMode.EntireDungeon
            ? Mathf.Min(Mathf.Max(0f, size.x - padding * 2f) / Mathf.Max(1f, _dungeonSize.x),
                Mathf.Max(0f, size.y - padding * 2f) / Mathf.Max(1f, _dungeonSize.y))
            : Mathf.Max(0f, Mathf.Min(size.x, size.y)) / span;
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
        mapContent.anchoredPosition = -MapOrigin * _pixelsPerMetre;
        if (playerArrow != null) playerArrow.rectTransform.anchoredPosition = (_playerPosition - MapOrigin) * _pixelsPerMetre;
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

    private void UpdateEnemyDots()
    {
        VisibleEnemyMarkerCount = 0;
        int count = _enemyPositions != null ? Mathf.Min(_enemyCount, _enemyPositions.Count) : 0;
        bool visible = mode == MapMode.FollowPlayer && IsVisible && viewport != null;
        for (int i = 0; i < _enemyPool.Count; i++)
        {
            var graphic = _enemyPool[i];
            bool inside = false;
            if (visible && i < count)
            {
                Vector2 position = (_enemyPositions[i] - MapOrigin) * _pixelsPerMetre;
                Vector2 available = viewport.rect.size * .5f - graphic.rectTransform.sizeDelta * .5f;
                if (!float.IsNaN(position.x) && !float.IsInfinity(position.x)
                    && !float.IsNaN(position.y) && !float.IsInfinity(position.y))
                {
                    graphic.rectTransform.anchoredPosition = position;
                    inside = Mathf.Abs(position.x) <= available.x && Mathf.Abs(position.y) <= available.y;
                }
            }
            graphic.gameObject.SetActive(inside);
            if (inside) VisibleEnemyMarkerCount++;
        }
    }

    private void HideEnemyDots()
    {
        _enemyPositions = null;
        _enemyCount = 0;
        VisibleEnemyMarkerCount = 0;
        for (int i = 0; i < _enemyPool.Count; i++) _enemyPool[i].gameObject.SetActive(false);
    }

    private void StyleEnemy(DungeonMinimapIconGraphic graphic)
    {
        graphic.IconSprite = enemySprite;
        graphic.color = enemyColor;
        graphic.raycastTarget = false;
        graphic.rectTransform.sizeDelta = PositiveSize(enemyIconSize);
    }

    private void UpdateMarker(MarkerVisual marker)
    {
        Vector2 position = (marker.LocalPosition - MapOrigin) * _pixelsPerMetre;
        marker.Rect.anchoredPosition = position;
        Vector2 available = viewport.rect.size * .5f - marker.Rect.sizeDelta * .5f;
        bool discovered = _model != null && marker.OwnerAreaIndex >= 0 && marker.OwnerAreaIndex < _model.Areas.Count
            && _model.Areas[marker.OwnerAreaIndex].Visited;
        bool inside = Mathf.Abs(position.x) <= available.x && Mathf.Abs(position.y) <= available.y;
        marker.Rect.gameObject.SetActive(discovered && inside);
    }

    private void ApplyIconStyles()
    {
        if (!_stylesDirty) return;
        _stylesDirty = false;
        if (playerArrow != null)
        {
            playerArrow.IconSprite = playerSprite;
            playerArrow.color = playerColor;
            playerArrow.rectTransform.sizeDelta = PositiveSize(playerIconSize);
        }
        for (int i = 0; i < _markers.Count; i++) StyleMarker(_markers[i].Graphic);
        for (int i = 0; i < _enemyPool.Count; i++) StyleEnemy(_enemyPool[i]);
    }

    private void StyleMarker(DungeonMinimapIconGraphic graphic)
    {
        switch (graphic.Kind)
        {
            case DungeonMinimapIconGraphic.Symbol.Shop:
                graphic.IconSprite = shopSprite;
                graphic.color = shopColor;
                break;
            case DungeonMinimapIconGraphic.Symbol.RewardChest:
                graphic.color = new Color32(255, 207, 100, 255);
                break;
            case DungeonMinimapIconGraphic.Symbol.Boss:
                graphic.color = new Color32(255, 111, 125, 255);
                break;
            default:
                graphic.IconSprite = exitSprite;
                graphic.color = exitColor;
                break;
        }
        graphic.rectTransform.sizeDelta = graphic.Kind switch
        {
            DungeonMinimapIconGraphic.Symbol.Boss => new Vector2(40f, 40f),
            DungeonMinimapIconGraphic.Symbol.RewardChest => PositiveSize(markerIconSize)
                * (mode == MapMode.FollowPlayer ? 1f : 1.35f),
            _ => PositiveSize(markerIconSize)
        };
    }

    private static Vector2 PositiveSize(Vector2 size) => new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));

    private void MeasureDungeon()
    {
        bool hasBounds = false;
        Vector2 minimum = Vector2.zero, maximum = Vector2.zero;
        for (int i = 0; i < _model.Areas.Count; i++)
        {
            var area = _model.Areas[i];
            Encapsulate(area.Center, area.Size, area.RotationDegrees, ref hasBounds, ref minimum, ref maximum);
        }
        for (int i = 0; i < _model.Stubs.Count; i++)
        {
            var stub = _model.Stubs[i];
            Encapsulate(stub.Center, stub.Size, stub.RotationDegrees, ref hasBounds, ref minimum, ref maximum);
        }
        _dungeonCenter = (minimum + maximum) * .5f;
        _dungeonSize = hasBounds ? maximum - minimum : Vector2.one * WorldSpan;
    }

    private static void Encapsulate(Vector2 center, Vector2 size, float rotation, ref bool hasBounds,
        ref Vector2 minimum, ref Vector2 maximum)
    {
        float radians = rotation * Mathf.Deg2Rad;
        float cosine = Mathf.Abs(Mathf.Cos(radians)), sine = Mathf.Abs(Mathf.Sin(radians));
        Vector2 extent = new Vector2(size.x * cosine + size.y * sine, size.x * sine + size.y * cosine) * .5f;
        if (!hasBounds)
        {
            minimum = center - extent;
            maximum = center + extent;
            hasBounds = true;
            return;
        }
        minimum = Vector2.Min(minimum, center - extent);
        maximum = Vector2.Max(maximum, center + extent);
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
