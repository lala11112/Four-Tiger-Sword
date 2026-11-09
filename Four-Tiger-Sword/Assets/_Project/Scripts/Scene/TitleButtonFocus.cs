using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Mouse hover owns title emphasis until the player explicitly navigates with the keyboard.</summary>
[DisallowMultipleComponent]
public sealed class TitleButtonFocus : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public void OnPointerEnter(PointerEventData data)
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    public void OnPointerExit(PointerEventData data)
    {
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
            EventSystem.current.SetSelectedGameObject(null);
    }
}
