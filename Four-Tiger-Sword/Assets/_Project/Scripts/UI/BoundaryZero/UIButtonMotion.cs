using Penta.Upgrade;
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
    private RectTransform _rect;
    private PentaUpgradeChrome _treeNode;
    private float _treeNodeFocus;

    public static void Install(GameObject root)
    {
        if (root == null) return;
        foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            if (button.GetComponent<UIButtonMotion>() == null) button.gameObject.AddComponent<UIButtonMotion>();
    }

    private void Awake()
    {
        _button = GetComponent<UnityEngine.UI.Button>();
        _rect = transform as RectTransform;
        _scale = transform.localScale;
        var colors = _button.colors;
        colors.fadeDuration = .1f;
        _button.colors = colors;
        if (_button.targetGraphic is PentaUpgradeChrome chrome && chrome.IsTreeNode)
            _treeNode = chrome;
        CenterVisualPivot();
    }

    private void CenterVisualPivot()
    {
        if (_rect == null) return;
        Rect bounds = _rect.rect;
        if (bounds.width <= 0 || bounds.height <= 0) return;
        // Keep the authored shape and its children in place while moving the scale origin.
        Vector2 center = _treeNode != null && _treeNode.rectTransform == _rect
            ? _treeNode.TreeNodeCenter : bounds.center;
        Vector2 pivot = (center - bounds.min) / bounds.size;
        if ((_rect.pivot - pivot).sqrMagnitude < .00000001f) return;
        Vector3 worldCenter = _rect.TransformPoint(center);
        _rect.pivot = pivot;
        _rect.position = worldCenter;
    }

    private bool Available => _button != null && _button.IsActive() && _button.IsInteractable();
    public void OnPointerEnter(PointerEventData data) { _hovered = true; }
    public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left && Available) _pressed = true; }
    public void OnPointerUp(PointerEventData data) => _pressed = false;
    public void OnPointerExit(PointerEventData data) { _pressed = false; _hovered = false; }
    public void OnSelect(BaseEventData data) { _selected = true; }
    public void OnDeselect(BaseEventData data) { _pressed = _selected = false; _submitUntil = 0; }
    public void OnSubmit(BaseEventData data) { if (Available) _submitUntil = Time.unscaledTime + .08f; }

    private void Update() => Tick(Time.unscaledDeltaTime);

    private void Tick(float deltaTime)
    {
        CenterVisualPivot();
        bool available = Available;
        if (!available) { _pressed = false; _submitUntil = 0; }
        bool down = available && (_pressed || Time.unscaledTime < _submitUntil);
        bool focus = available && (_hovered || _selected);
        Vector3 target = _scale * (down ? .975f : focus ? 1.035f : 1f);
        transform.localScale = Vector3.MoveTowards(transform.localScale, target, deltaTime * _scale.magnitude * .6f);
        float focusTarget = focus ? 1f : 0f;
        float focusStep = deltaTime * 12f;
        if (_treeNode != null)
        {
            _treeNodeFocus = Mathf.MoveTowards(_treeNodeFocus, focusTarget, focusStep);
            _treeNode.SetTreeNodeFocus(_treeNodeFocus);
        }
    }

    private void OnDisable()
    {
        _pressed = _hovered = _selected = false;
        _submitUntil = 0;
        if (_button != null) transform.localScale = _scale;
        _treeNodeFocus = 0;
        if (_treeNode != null) _treeNode.SetTreeNodeFocus(0);
    }
}
