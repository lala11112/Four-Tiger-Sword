using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Retains Button's authored tint/selection and adds a small unscaled press response.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(UnityEngine.UI.Button))]
public sealed class UIButtonMotion : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IPointerExitHandler, ISubmitHandler, IDeselectHandler
{
    private UnityEngine.UI.Button _button;
    private Vector3 _scale;
    private bool _pressed;
    private float _submitUntil;

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
    }

    private bool Available => _button != null && _button.IsActive() && _button.IsInteractable();
    public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left && Available) _pressed = true; }
    public void OnPointerUp(PointerEventData data) => _pressed = false;
    public void OnPointerExit(PointerEventData data) => _pressed = false;
    public void OnDeselect(BaseEventData data) { _pressed = false; _submitUntil = 0; }
    public void OnSubmit(BaseEventData data) { if (Available) _submitUntil = Time.unscaledTime + .08f; }

    private void Update()
    {
        bool down = Available && (_pressed || Time.unscaledTime < _submitUntil);
        Vector3 target = _scale * (down ? .98f : 1f);
        transform.localScale = Vector3.MoveTowards(transform.localScale, target, Time.unscaledDeltaTime * _scale.magnitude * .02f / .12f);
    }

    private void OnDisable()
    {
        _pressed = false;
        _submitUntil = 0;
        if (_button != null) transform.localScale = _scale;
    }
}
