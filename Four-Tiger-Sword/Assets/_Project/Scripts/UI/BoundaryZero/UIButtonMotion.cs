using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Visible pointer/keyboard focus and an unscaled press response; preserves the authored button skin.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(UnityEngine.UI.Button))]
public sealed class UIButtonMotion : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IPointerEnterHandler, IPointerExitHandler, ISubmitHandler, ISelectHandler, IDeselectHandler
{
    private UnityEngine.UI.Button _button;
    private Vector3 _scale;
    private bool _pressed, _hovered, _selected;
    private float _submitUntil;
    private CanvasGroup _focus;
    private Vector2 _focusOffsetMin = new Vector2(2, 2);
    private Vector2 _focusOffsetMax = new Vector2(-2, -2);

    public static void Install(GameObject root)
    {
        if (root == null) return;
        foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            if (button.GetComponent<UIButtonMotion>() == null) button.gameObject.AddComponent<UIButtonMotion>();
    }

    private void Awake()
    {
        _button = GetComponent<UnityEngine.UI.Button>();
        _scale = transform.localScale;
        var colors = _button.colors;
        colors.fadeDuration = .1f;
        _button.colors = colors;
        BuildFocus();
    }

    private void BuildFocus()
    {
        var rect = new GameObject("HoverFocus", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = _focusOffsetMin; rect.offsetMax = _focusOffsetMax;
        _focus = rect.GetComponent<CanvasGroup>();
        _focus.interactable = _focus.blocksRaycasts = false;
        _focus.alpha = 0;
        Edge(rect, "Top", new Vector2(0, 1), Vector2.one, new Vector2(0, -2), new Vector2(0, 0));
        Edge(rect, "Bottom", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 2));
        Edge(rect, "Left", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(3, 0));
        Edge(rect, "Right", new Vector2(1, 0), Vector2.one, new Vector2(-2, 0), Vector2.zero);
    }

    public void SetFocusOffsets(Vector2 min, Vector2 max)
    {
        _focusOffsetMin = min;
        _focusOffsetMax = max;
        if (_focus == null) return;
        var rect = (RectTransform)_focus.transform;
        rect.offsetMin = min;
        rect.offsetMax = max;
    }

    private static void Edge(RectTransform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<UnityEngine.UI.Image>();
        image.transform.SetParent(parent, false);
        image.rectTransform.anchorMin = min; image.rectTransform.anchorMax = max;
        image.rectTransform.offsetMin = offsetMin; image.rectTransform.offsetMax = offsetMax;
        image.raycastTarget = false;
        image.color = name == "Left" ? new Color32(255, 73, 111, 255) : new Color32(255, 225, 232, 230);
    }

    private bool Available => _button != null && _button.IsActive() && _button.IsInteractable();
    public void OnPointerEnter(PointerEventData data) { _hovered = true; }
    public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left && Available) _pressed = true; }
    public void OnPointerUp(PointerEventData data) => _pressed = false;
    public void OnPointerExit(PointerEventData data) { _pressed = false; _hovered = false; }
    public void OnSelect(BaseEventData data) { _selected = true; }
    public void OnDeselect(BaseEventData data) { _pressed = _selected = false; _submitUntil = 0; }
    public void OnSubmit(BaseEventData data) { if (Available) _submitUntil = Time.unscaledTime + .08f; }

    private void Update()
    {
        bool available = Available;
        if (!available) { _pressed = false; _submitUntil = 0; }
        bool down = available && (_pressed || Time.unscaledTime < _submitUntil);
        bool focus = available && (_hovered || _selected);
        Vector3 target = _scale * (down ? .975f : focus ? 1.035f : 1f);
        transform.localScale = Vector3.MoveTowards(transform.localScale, target, Time.unscaledDeltaTime * _scale.magnitude * .6f);
        if (_focus != null) _focus.alpha = Mathf.MoveTowards(_focus.alpha, focus ? 1 : 0, Time.unscaledDeltaTime * 12f);
    }

    private void OnDisable()
    {
        _pressed = _hovered = _selected = false;
        _submitUntil = 0;
        if (_button != null) transform.localScale = _scale;
        if (_focus != null) _focus.alpha = 0;
    }
}
