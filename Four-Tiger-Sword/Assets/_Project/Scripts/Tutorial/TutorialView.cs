using System;
using BoundaryZeroUI;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TutorialView : MonoBehaviour
{
    private const float CardWidth = 590f;
    private const float CardHeight = 186f;
    private const float GameplayTop = 300f;
    private const int GuideOrder = 190;
    private const int GuideOverPauseOrder = 221;
    private static readonly Color GuideAccent = new Color32(115, 227, 250, 255);

    private readonly Vector3[] _corners = new Vector3[4];
    private Canvas _canvas;
    private RectTransform _surface, _card, _outline, _marker, _markerDiamond, _markerArrow;
    private TMP_Text _title, _body, _keys, _continueLabel, _skipHint;
    private UnityEngine.UI.Button _continueButton, _skipButton;
    private Transform _worldTarget;
    private RectTransform _uiTarget;
    private RunManager _run;
    private HubWorldController _hub;
    private Action _continue, _skip;

    public bool IsVisible => _card != null && _card.gameObject.activeInHierarchy;
    public UnityEngine.UI.Button ContinueButton => _continueButton;
    private static bool GuideControlsReachable => Cursor.lockState != CursorLockMode.Locked;

    public static TutorialView Ensure(MonoBehaviour owner)
    {
        var existing = owner.transform.Find("TutorialCanvas");
        if (existing != null && existing.TryGetComponent(out TutorialView view)) return view;

        var root = new GameObject("TutorialCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        root.transform.SetParent(owner.transform, false);
        view = root.AddComponent<TutorialView>();
        view.Build(owner);
        view.Hide();
        return view;
    }

    public void SetGoal(string title, string body, string keys)
    {
        _title.text = title ?? string.Empty;
        _body.text = body ?? string.Empty;
        _keys.text = keys ?? string.Empty;
        _card.gameObject.SetActive(true);
        ShowContinue(null, null);
    }

    public void SetWorldTarget(Transform target)
    {
        if (target != null) _uiTarget = null;
        _worldTarget = target;
    }

    public void SetUITarget(RectTransform target)
    {
        if (target != null) _worldTarget = null;
        _uiTarget = target;
    }

    public void ShowContinue(string label, Action callback)
    {
        _continue = callback;
        _continueLabel.text = label ?? string.Empty;
        _continueButton.gameObject.SetActive(callback != null);
    }

    public void SetSkipAction(Action callback)
    {
        _skip = callback;
        RefreshButtons();
    }

    public void Hide()
    {
        _worldTarget = null;
        _uiTarget = null;
        ShowContinue(null, null);
        _card.gameObject.SetActive(false);
        _outline.gameObject.SetActive(false);
        _marker.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        BoundaryZeroBackdrop.CaptureVisibilityChanged += SetCaptureVisibility;
        SetCaptureVisibility(BoundaryZeroBackdrop.IsCapturing);
    }

    private void OnDisable() => BoundaryZeroBackdrop.CaptureVisibilityChanged -= SetCaptureVisibility;

    private void Build(MonoBehaviour owner)
    {
        _run = owner.GetComponentInParent<RunManager>();
        _hub = owner.GetComponentInParent<HubWorldController>();
        _surface = (RectTransform)transform;
        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = GuideOrder;
        SetCaptureVisibility(BoundaryZeroBackdrop.IsCapturing);
        var scaler = GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2560f, 1440f);
        scaler.matchWidthOrHeight = .5f;

        _outline = UIFactory.Node(_surface, "TargetOutline", 0, 0, 0, 0);
        _marker = UIFactory.Node(_surface, "WorldMarker", 0, 0, 34, 34);
        _outline.anchorMin = _outline.anchorMax = _outline.pivot = new Vector2(.5f, .5f);
        _marker.anchorMin = _marker.anchorMax = _marker.pivot = new Vector2(.5f, .5f);
        BuildOutlineEdge("Top", true, 1f);
        BuildOutlineEdge("Bottom", true, 0f);
        BuildOutlineEdge("Left", false, 0f);
        BuildOutlineEdge("Right", false, 1f);
        var markerInk = new Color32(8, 17, 28, 240);
        _markerDiamond = UIFactory.Node(_marker, "TargetPosition", 0, 0, 34, 34);
        CenterMarkerElement(_markerDiamond);
        var markerShadow = UIFactory.Box(_markerDiamond, "MarkerShadow", 4, 4, 26, 26, markerInk).rectTransform;
        var markerCore = UIFactory.Box(_markerDiamond, "MarkerCore", 7, 7, 20, 20, GuideAccent).rectTransform;
        CenterMarkerElement(markerShadow);
        CenterMarkerElement(markerCore);
        markerShadow.localRotation = markerCore.localRotation = Quaternion.Euler(0f, 0f, 45f);
        _markerArrow = UIFactory.Node(_marker, "TargetDirection", 0, 0, 44, 44);
        CenterMarkerElement(_markerArrow);
        BuildArrowLayer("ArrowShadow", 44f, markerInk);
        BuildArrowLayer("ArrowCore", 32f, GuideAccent);

        _card = UIFactory.Node(_surface, "GoalCard", 32, GameplayTop, CardWidth, CardHeight);
        UIFactory.Box(_card, "Panel", 0, 0, CardWidth, CardHeight, new Color32(9, 19, 31, 247));
        UIFactory.Border(_card, 0, 0, CardWidth, CardHeight, new Color32(69, 117, 148, 255));
        UIFactory.Line(_card, 0, 22, 4, 33, GuideAccent);
        UIFactory.Line(_card, 22, 133, CardWidth - 44, 1, new Color32(64, 94, 117, 255));
        _title = UIFactory.Label(_card, "", 21, 17, CardWidth - 42, 39, 32, UIFactory.Ivory);
        var titleFont = Resources.Load<TMP_FontAsset>("BoundaryUI/Fonts/TitleKorean");
        if (titleFont != null)
        {
            _title.font = titleFont;
            _title.fontStyle = FontStyles.Normal;
        }
        _body = UIFactory.Label(_card, "", 22, 61, CardWidth - 44, 67, 26, UIFactory.Ivory);
        _body.enableAutoSizing = true;
        _body.fontSizeMin = 20;
        _body.fontSizeMax = 26;
        _keys = UIFactory.Label(_card, "", 22, 142, 340, 35, 24, GuideAccent, TextAlignmentOptions.MidlineLeft);
        _continueButton = UIFactory.Button(_card, "계속", 343, 141, 126, 37, Continue, true);
        _continueLabel = _continueButton.GetComponentInChildren<TMP_Text>();
        _skipButton = UIFactory.Button(_card, "건너뛰기", 475, 141, 99, 37, Skip);
        _skipButton.GetComponentInChildren<TMP_Text>().fontSize = 19;
        _skipButton.gameObject.SetActive(false);
        _skipHint = UIFactory.Label(_card, "ESC  ·  건너뛰기", 399, 142, 174, 35, 17,
            UIFactory.Muted, TextAlignmentOptions.MidlineRight);
        _skipHint.textWrappingMode = TextWrappingModes.NoWrap;
        _skipHint.gameObject.SetActive(false);
    }

    private void Continue()
    {
        if (!GuideControlsReachable) return;
        var callback = _continue;
        ShowContinue(null, null);
        callback?.Invoke();
    }

    private void Skip()
    {
        if (GuideControlsReachable) _skip?.Invoke();
    }

    private void LateUpdate()
    {
        if (!IsVisible) return;
        bool paused = (_run != null && _run.IsPauseMenuOpen) || (_hub != null && _hub.IsPauseMenuOpen);
        _canvas.sortingOrder = paused ? GuideOverPauseOrder : GuideOrder;
        RefreshButtons();
        bool moveCardToRight = false;
        bool hasOutline = !paused && TryShowOutline(out moveCardToRight);
        bool modal = paused || hasOutline || (_run != null && (_run.IsMapOpen || _run.IsShopOpen || _run.IsRewardOpen))
            || (_hub != null && _hub.IsModalOpen);
        PlaceCard(modal, moveCardToRight);
        _outline.gameObject.SetActive(hasOutline);
        _marker.gameObject.SetActive(!modal && TryShowMarker());
    }

    private void RefreshButtons()
    {
        bool cursorAvailable = GuideControlsReachable;
        _continueButton.interactable = cursorAvailable;
        _skipButton.gameObject.SetActive(_skip != null && cursorAvailable);
        _skipHint.gameObject.SetActive(_skip != null && !cursorAvailable);
    }

    private void PlaceCard(bool modal, bool alignRight)
    {
        var anchor = new Vector2(alignRight ? 1f : 0f, 1f);
        _card.anchorMin = _card.anchorMax = _card.pivot = anchor;
        _card.anchoredPosition = new Vector2(alignRight ? -32f : 32f, modal ? -12f : -GameplayTop);
        float fit = Mathf.Clamp((_surface.rect.width - 64f) / CardWidth, 0f, modal ? .92f : 1f);
        _card.localScale = new Vector3(fit, fit, 1f);
    }

    private bool TryShowOutline(out bool moveCardToRight)
    {
        moveCardToRight = false;
        if (_uiTarget == null || !_uiTarget.gameObject.activeInHierarchy) return false;
        var canvas = _uiTarget.GetComponentInParent<Canvas>()?.rootCanvas;
        if (canvas == null || !canvas.isActiveAndEnabled) return false;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera ?? Camera.main;
        _uiTarget.GetWorldCorners(_corners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in _corners)
        {
            var screenPoint = RectTransformUtility.WorldToScreenPoint(camera, corner);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_surface, screenPoint, null, out var point)) return false;
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        var bounds = _surface.rect;
        if (min.x >= bounds.xMax || max.x <= bounds.xMin || min.y >= bounds.yMax || max.y <= bounds.yMin
            || max.x - min.x < 1f || max.y - min.y < 1f) return false;
        _outline.anchoredPosition = (min + max) * .5f;
        _outline.sizeDelta = max - min + Vector2.one * 12f;
        moveCardToRight = min.x < bounds.xMin + 32f + CardWidth * .92f
            && max.y > bounds.yMax - 12f - CardHeight * .92f;
        return true;
    }

    private bool TryShowMarker()
    {
        if (_worldTarget == null || !_worldTarget.gameObject.activeInHierarchy) return false;
        var camera = Camera.main;
        if (camera == null) return false;

        var bounds = _surface.rect;
        var safeArea = Rect.MinMaxRect(bounds.xMin + 56f, bounds.yMin + 110f,
            bounds.xMax - 56f, bounds.yMax - 110f);
        if (safeArea.width <= 0f || safeArea.height <= 0f) return false;

        Vector3 targetPosition = _worldTarget.position + Vector3.up * 2f;
        Vector3 cameraSpace = camera.transform.InverseTransformPoint(targetPosition);
        Vector2 point = default;
        bool inFront = cameraSpace.z > camera.nearClipPlane && RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _surface, camera.WorldToScreenPoint(targetPosition), null, out point);
        var cardBounds = Rect.MinMaxRect(bounds.xMin + 8f, bounds.yMax - GameplayTop - CardHeight * _card.localScale.y - 24f,
            bounds.xMin + 56f + CardWidth * _card.localScale.x, bounds.yMax - GameplayTop + 24f);
        bool onScreen = inFront && safeArea.Contains(point) && !cardBounds.Contains(point);
        _markerDiamond.gameObject.SetActive(onScreen);
        _markerArrow.gameObject.SetActive(!onScreen);
        if (!onScreen)
        {
            // A behind-camera perspective projection mirrors the sides. Use camera-relative
            // horizontal position and distance behind the viewer to point around the lower edge.
            Vector2 direction = inFront ? point - safeArea.center
                : new Vector2(cameraSpace.x, -Mathf.Max(.01f, -cameraSpace.z));
            Vector2 half = safeArea.size * .5f;
            float scale = Mathf.Min(half.x / Mathf.Max(.001f, Mathf.Abs(direction.x)),
                half.y / Mathf.Max(.001f, Mathf.Abs(direction.y)));
            point = safeArea.center + direction * scale;
            if (cardBounds.Contains(point)) point.y = cardBounds.yMin;
            _markerArrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
        }
        _marker.anchoredPosition = point;
        float size = 1f + .09f * Mathf.Sin(Time.unscaledTime * 5f);
        _marker.localScale = new Vector3(size, size, 1f);
        return true;
    }

    private void BuildOutlineEdge(string name, bool horizontal, float side)
    {
        var rect = UIFactory.Box(_outline, name, 0, 0, 0, 0, GuideAccent).rectTransform;
        rect.anchorMin = horizontal ? new Vector2(0f, side) : new Vector2(side, 0f);
        rect.anchorMax = horizontal ? new Vector2(1f, side) : new Vector2(side, 1f);
        rect.pivot = horizontal ? new Vector2(.5f, side) : new Vector2(side, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = horizontal ? new Vector2(0f, 3f) : new Vector2(3f, 0f);
    }

    private void BuildArrowLayer(string name, float size, Color color)
    {
        var rect = UIFactory.Node(_markerArrow, name, 0, 0, size, size);
        CenterMarkerElement(rect);
        var arrow = rect.gameObject.AddComponent<DungeonMinimapIconGraphic>();
        arrow.SetSymbol(DungeonMinimapIconGraphic.Symbol.Player);
        arrow.color = color;
    }

    private static void CenterMarkerElement(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
    }

    private void SetCaptureVisibility(bool capturing)
    {
        if (_canvas != null) _canvas.enabled = !capturing;
    }
}
