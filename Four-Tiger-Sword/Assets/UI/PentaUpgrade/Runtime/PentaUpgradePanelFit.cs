using UnityEngine;
using UnityEngine.EventSystems;

namespace Penta.Upgrade
{
    /// <summary>Keeps the design proportions inside an existing Canvas without changing its scaler.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class PentaUpgradePanelFit : UIBehaviour
    {
        [SerializeField] RectTransform panel;
        public RectTransform Panel => panel;

        public void Configure(RectTransform target)
        {
            panel = target;
            Refresh();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Refresh();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            Refresh();
        }

        public void Refresh()
        {
            if (!panel) return;
            var bounds = ((RectTransform)transform).rect;
            if (bounds.width <= 0 || bounds.height <= 0) return;
            float scale = Mathf.Min(bounds.width / 1920f, bounds.height / 1080f);
            var targetScale = new Vector3(scale, scale, 1);
            if (panel.localScale != targetScale) panel.localScale = targetScale;
        }
    }
}
