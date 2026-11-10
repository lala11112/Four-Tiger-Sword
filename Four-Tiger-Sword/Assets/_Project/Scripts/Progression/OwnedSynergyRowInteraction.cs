using System;
using UnityEngine.EventSystems;

/// <summary>A focusable tooltip target inside the owned synergy scroll area.</summary>
internal sealed class OwnedSynergyRowInteraction : UnityEngine.UI.Selectable
{
    public Action<bool> PointerChanged;
    public Action<bool> FocusChanged;

    public override void OnPointerEnter(PointerEventData eventData)
    {
        base.OnPointerEnter(eventData);
        PointerChanged?.Invoke(true);
    }

    public override void OnPointerExit(PointerEventData eventData)
    {
        base.OnPointerExit(eventData);
        PointerChanged?.Invoke(false);
    }

    // Rows present information. Mouse down leaves the keyboard selection with its current control.
    public override void OnPointerDown(PointerEventData eventData) { }

    public override void OnSelect(BaseEventData eventData)
    {
        base.OnSelect(eventData);
        FocusChanged?.Invoke(true);
    }

    public override void OnDeselect(BaseEventData eventData)
    {
        base.OnDeselect(eventData);
        FocusChanged?.Invoke(false);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        PointerChanged?.Invoke(false);
        FocusChanged?.Invoke(false);
    }
}
