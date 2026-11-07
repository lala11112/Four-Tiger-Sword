using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Routes pointer events from the full-map viewport without making individual map graphics interactive.</summary>
[DisallowMultipleComponent]
public sealed class DungeonMapPointerHandler : MonoBehaviour, IPointerEnterHandler, IPointerMoveHandler,
    IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private DungeonMinimapView view;
    [SerializeField] private UnityEngine.UI.Graphic surface;

    public void Configure(DungeonMinimapView target, UnityEngine.UI.Graphic targetSurface)
    {
        view = target;
        surface = targetSurface;
        SetInputEnabled(false);
    }

    public void SetInputEnabled(bool value)
    {
        if (surface != null) surface.raycastTarget = value;
    }

    public void OnPointerEnter(PointerEventData eventData) => UpdateHover(eventData);
    public void OnPointerMove(PointerEventData eventData) => UpdateHover(eventData);
    public void OnPointerExit(PointerEventData eventData) { if (view != null) view.ClearPointer(); }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && view != null && view.IsVisible)
            view.ClickPointer(eventData.position, eventData.pressEventCamera);
    }

    private void UpdateHover(PointerEventData eventData)
    {
        if (view != null && view.IsVisible) view.UpdatePointer(eventData.position, eventData.enterEventCamera);
    }

    private void OnDisable() { if (view != null) view.ClearPointer(); }
}
