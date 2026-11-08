using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Penta.Upgrade
{
    public sealed class PentaUpgradeViewport : UnityEngine.UI.ScrollRect
    {
        public float Zoom { get; private set; } = 1f;
        public event Action ZoomChanged;
        private readonly Vector3[] contentCorners = new Vector3[4];

        // Zoom is the actual content scale. The large tree is explored by dragging,
        // never shrunk automatically to fit the entire graph into the viewport.
        public void Home()
        {
            if (!content) return;
            SetZoom(1f);
            // The tree grows down from the root; dragging upward reveals later tiers.
            FocusPoint(new Vector2(content.rect.width * .5f, 280f), new Vector2(.5f, .78f));
        }

        // Retained for existing callers of the former fit-to-canvas command.
        public void Fit() => Home();

        public void SetZoom(float value)
        {
            if (!content || !viewport) return;
            Zoom = Mathf.Clamp(value, .65f, 1.35f);
            content.localScale = Vector3.one * Zoom;
            StopMovement();
            ClampContent();
            ZoomChanged?.Invoke();
        }

        public void FocusPoint(Vector2 topLeftContentPoint, Vector2 viewportNormalizedAnchor)
        {
            if (!content || !viewport) return;
            Rect graphRect = content.rect;
            Vector2 graphPoint = new Vector2(graphRect.xMin + topLeftContentPoint.x,
                graphRect.yMax - topLeftContentPoint.y);
            Rect viewRect = viewport.rect;
            Vector2 target = new Vector2(
                Mathf.Lerp(viewRect.xMin, viewRect.xMax, Mathf.Clamp01(viewportNormalizedAnchor.x)),
                Mathf.Lerp(viewRect.yMin, viewRect.yMax, Mathf.Clamp01(viewportNormalizedAnchor.y)));
            Vector2 current = viewport.InverseTransformPoint(content.TransformPoint(graphPoint));
            content.anchoredPosition += target - current;
            StopMovement();
            ClampContent();
        }

        public override void OnScroll(PointerEventData eventData)
        {
            if (!IsActive() || !content || !viewport) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, eventData.position, eventData.pressEventCamera, out var pointer);
            Vector3 graphPoint = content.InverseTransformPoint(viewport.TransformPoint(pointer));
            SetZoom(Zoom * Mathf.Pow(1.12f, eventData.scrollDelta.y));
            Vector2 movedPoint = viewport.InverseTransformPoint(content.TransformPoint(graphPoint));
            content.anchoredPosition += pointer - movedPoint;
            ClampContent();
            eventData.Use();
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            // Also constrain the graph after viewport/layout resizing.
            ClampContent();
        }

        private void ClampContent()
        {
            if (!content || !viewport) return;
            content.GetWorldCorners(contentCorners);
            Vector2 min = viewport.InverseTransformPoint(contentCorners[0]);
            Vector2 max = min;
            for (int i = 1; i < contentCorners.Length; i++)
            {
                Vector2 corner = viewport.InverseTransformPoint(contentCorners[i]);
                min = Vector2.Min(min, corner);
                max = Vector2.Max(max, corner);
            }
            Rect bounds = viewport.rect;
            Vector2 correction = Vector2.zero;
            correction.x = max.x - min.x <= bounds.width
                ? bounds.center.x - (min.x + max.x) * .5f
                : min.x > bounds.xMin ? bounds.xMin - min.x
                : max.x < bounds.xMax ? bounds.xMax - max.x : 0f;
            correction.y = max.y - min.y <= bounds.height
                ? bounds.center.y - (min.y + max.y) * .5f
                : min.y > bounds.yMin ? bounds.yMin - min.y
                : max.y < bounds.yMax ? bounds.yMax - max.y : 0f;
            if (correction.sqrMagnitude > .0001f)
                content.anchoredPosition += correction;
        }
    }
}
