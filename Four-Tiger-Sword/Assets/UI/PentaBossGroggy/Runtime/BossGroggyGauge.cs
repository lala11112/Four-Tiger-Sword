using UnityEngine;

namespace Penta.BossUI
{
    /// <summary>A texture-free remaining-posture bar. Combat state belongs to the caller.</summary>
    [AddComponentMenu("Penta UI/Boss Groggy Gauge")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class BossGroggyGauge : UnityEngine.UI.MaskableGraphic
    {
        [SerializeField, Range(0f, 1f), Tooltip("Inspector preview; runtime setters update this same value.")]
        private float normalized;

        private Rect meshRect;

        public float Normalized => normalized;
        public bool IsBroken => normalized >= 1f;

        /// <summary>Invalid/negative maxima and NaN inputs reset groggy to zero. Positive overflow saturates.</summary>
        public void SetGroggy(float current, float maximum)
        {
            if (float.IsNaN(current) || float.IsNaN(maximum) || maximum <= 0f || float.IsInfinity(maximum))
            {
                SetNormalized(0f);
                return;
            }
            SetNormalized(current / maximum);
        }

        /// <summary>Snaps to the supplied groggy fraction; zero leaves the bar full and one empties it.</summary>
        public void SetNormalized(float fraction)
        {
            float next = float.IsNaN(fraction) ? 0f : Mathf.Clamp01(fraction);
            if (next == normalized) return;
            normalized = next;
            SetVerticesDirty();
        }

        public void ResetGauge() => SetNormalized(0f);

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnEnable()
        {
            raycastTarget = false;
            base.OnEnable();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            normalized = float.IsNaN(normalized) ? 0f : Mathf.Clamp01(normalized);
            raycastTarget = false;
            base.OnValidate();
        }
#endif

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            meshRect = GetPixelAdjustedRect();
            if (meshRect.width <= 0f || meshRect.height <= 0f) return;

            // Full-width, flat top joins the HP bar; only the lower corners are chamfered.
            // Insets scale with height so the frame stays legible at both authored sizes.
            float edge = meshRect.height * .12f;
            float chamfer = meshRect.height * .28f;
            Bar(vh, 0f, meshRect.width, 0f, meshRect.height, chamfer,
                meshRect.width, new Color32(91, 94, 98, 255));
            float left = Mathf.Min(edge, meshRect.width * .25f);
            float right = meshRect.width - left;
            float bottom = edge;
            float top = meshRect.height - edge;
            float innerChamfer = Mathf.Max(0f, chamfer - edge);
            Bar(vh, left, right, bottom, top, innerChamfer,
                right, new Color32(31, 33, 36, 255));

            float remaining = 1f - normalized;
            if (remaining <= 0f) return;
            Bar(vh, left, right, bottom, top, innerChamfer,
                Mathf.Lerp(left, right, remaining), new Color32(151, 154, 159, 255));
        }

        private void Bar(UnityEngine.UI.VertexHelper vh, float left, float right,
            float bottom, float top, float chamfer, float clipRight, Color32 tint)
        {
            if (right <= left || top <= bottom || clipRight <= left) return;
            chamfer = Mathf.Min(chamfer, (right - left) * .5f, top - bottom);
            Vector2[] outline =
            {
                new Vector2(left, bottom + chamfer),
                new Vector2(left, top),
                new Vector2(right, top),
                new Vector2(right, bottom + chamfer),
                new Vector2(right - chamfer, bottom),
                new Vector2(left + chamfer, bottom)
            };
            // Clip the same silhouette with a vertical cut: the left end never moves.
            var clipped = new System.Collections.Generic.List<Vector2>(8);
            Vector2 previous = outline[outline.Length - 1];
            bool previousInside = previous.x <= clipRight;
            foreach (Vector2 point in outline)
            {
                bool inside = point.x <= clipRight;
                if (inside != previousInside)
                {
                    float t = (clipRight - previous.x) / (point.x - previous.x);
                    clipped.Add(new Vector2(clipRight, Mathf.Lerp(previous.y, point.y, t)));
                }
                if (inside) clipped.Add(point);
                previous = point;
                previousInside = inside;
            }
            int start = vh.currentVertCount;
            Color vertexColor = (Color)tint * color;
            foreach (Vector2 point in clipped)
                vh.AddVert(new Vector3(meshRect.xMin + point.x, meshRect.yMin + point.y, 0f),
                    vertexColor, Vector2.zero);
            for (int i = 1; i < clipped.Count - 1; i++)
                vh.AddTriangle(start, start + i, start + i + 1);
        }
    }
}
