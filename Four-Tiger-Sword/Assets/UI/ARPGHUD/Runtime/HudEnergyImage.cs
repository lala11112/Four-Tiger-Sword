using UnityEngine;
using UnityEngine.UI;

namespace ArpgHud
{
    /// <summary>A circular energy reservoir. The game owns charge; waves only animate its surface.</summary>
    [AddComponentMenu("UI/PENTA HUD Energy Image")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HudEnergyImage : MaskableGraphic
    {
        [SerializeField, Range(0, 1)] float charge;
        const int Segments = 80;
        public float Charge => charge;

        public void SetCharge(float value, Color elementColor)
        {
            value = HudMath.IsFinite(value) ? Mathf.Clamp01(value) : 0;
            if (charge != value)
            {
                charge = value;
                SetVerticesDirty();
            }
            elementColor.a = 1;
            if (color != elementColor) color = elementColor;
        }

        void Update()
        {
            if (Application.isPlaying && Time.timeScale > 0 && charge > 0 && charge < 1)
                SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float radius = Mathf.Min(rect.width, rect.height) * .5f;
            if (radius <= 0) return;
            Vector2 center = rect.center;

            // A subdued circular backing makes the empty reservoir readable over bright scenery.
            Color backing = new Color(.025f, .035f, .05f, .64f);
            mesh.AddVert(center, backing, Vector2.zero);
            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * Mathf.PI * 2 / Segments;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                    backing, Vector2.zero);
                if (i > 0) mesh.AddTriangle(0, i, i + 1);
            }
            if (charge <= 0) return;

            float level = Mathf.Lerp(-radius, radius, charge);
            float waveSize = radius * .035f * Mathf.Sin(charge * Mathf.PI);
            float phase = Application.isPlaying ? Time.time * 1.5f : 0;
            Color bottom = color; bottom.a = .25f;
            Color top = Color.Lerp(color, Color.white, .14f); top.a = .7f;
            Color surface = Color.Lerp(color, Color.white, .65f); surface.a = .82f;

            for (int i = 0; i < Segments; i++)
            {
                float x0 = Mathf.Lerp(-radius, radius, i / (float)Segments);
                float x1 = Mathf.Lerp(-radius, radius, (i + 1) / (float)Segments);
                float edge0 = Mathf.Sqrt(Mathf.Max(0, radius * radius - x0 * x0));
                float edge1 = Mathf.Sqrt(Mathf.Max(0, radius * radius - x1 * x1));
                float wave0 = level + Wave(x0 / radius, phase) * waveSize;
                float wave1 = level + Wave(x1 / radius, phase) * waveSize;
                float y0 = Mathf.Clamp(wave0, -edge0, edge0);
                float y1 = Mathf.Clamp(wave1, -edge1, edge1);
                Quad(mesh, center, x0, x1, -edge0, -edge1, y0, y1, bottom, top);

                // Highlight only the liquid surface, never the circular edge or an empty/full disc.
                if (charge < 1 && wave0 > -edge0 && wave0 < edge0 && wave1 > -edge1 && wave1 < edge1)
                    Quad(mesh, center, x0, x1, Mathf.Max(-edge0, y0 - radius * .018f),
                        Mathf.Max(-edge1, y1 - radius * .018f), y0, y1, surface, surface);
            }
        }

        static float Wave(float x, float phase)
            => Mathf.Sin(x * 4 + phase) + .35f * Mathf.Sin(x * 7 - phase * .7f);

        static void Quad(VertexHelper mesh, Vector2 center, float x0, float x1,
            float bottom0, float bottom1, float top0, float top1, Color bottom, Color top)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(center + new Vector2(x0, bottom0), bottom, Vector2.zero);
            mesh.AddVert(center + new Vector2(x0, top0), top, Vector2.zero);
            mesh.AddVert(center + new Vector2(x1, top1), top, Vector2.zero);
            mesh.AddVert(center + new Vector2(x1, bottom1), bottom, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
