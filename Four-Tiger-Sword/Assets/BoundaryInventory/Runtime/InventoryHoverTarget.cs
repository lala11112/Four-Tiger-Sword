using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoundaryZero.Inventory
{
    [DisallowMultipleComponent]
    public sealed class InventoryHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private InventoryView view;
        [SerializeField] private string id;
        [SerializeField] private bool isSynergy;
        [SerializeField] private Graphic surface;
        [SerializeField] private Graphic accent;
        [SerializeField] private Color normal;
        [SerializeField] private Color highlight;

        internal void Configure(InventoryView owner, string targetId, bool synergy, Graphic fill,
            Graphic mark, Color idle, Color hover)
        {
            view = owner;
            id = targetId;
            isSynergy = synergy;
            surface = fill;
            accent = mark;
            normal = idle;
            highlight = hover;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (view != null)
                view.ShowTooltip(this, id, isSynergy);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (view != null)
                view.HideTooltip(this);
        }

        private void OnDisable()
        {
            if (view != null)
                view.HideTooltip(this);
        }

        internal void SetHighlight(bool value)
        {
            if (surface != null)
                surface.color = value ? highlight : normal;
            if (accent != null)
                accent.enabled = value;
        }
    }
}
