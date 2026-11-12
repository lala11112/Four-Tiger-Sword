using UnityEngine;

namespace BoundaryZero.ShortcutHints
{
    /// <summary>Draws the small chamfered keycaps below the minimap.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BoundaryZeroShortcutKeycap : UnityEngine.UI.MaskableGraphic
    {
        private const int CornerCount = 8;
        private static readonly Color32 TopBorder = new Color32(130, 133, 142, 218);
        private static readonly Color32 BottomBorder = new Color32(141, 24, 43, 234);
        private static readonly Color32 TopFill = new Color32(35, 33, 41, 248);
        private static readonly Color32 BottomFill = new Color32(32, 11, 21, 248);

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 2f || rect.height <= 2f) return;

            AddOutline(mesh, rect, 0f, 5.775f, TopBorder, BottomBorder);
            AddOutline(mesh, rect, 1.4025f, 4.95f, TopFill, BottomFill);

            for (int i = 0; i < CornerCount; i++)
            {
                int next = (i + 1) % CornerCount;
                mesh.AddTriangle(i, next, next + CornerCount);
                mesh.AddTriangle(i, next + CornerCount, i + CornerCount);
            }

            for (int i = 1; i < CornerCount - 1; i++)
                mesh.AddTriangle(CornerCount, CornerCount + i, CornerCount + i + 1);
        }

        private static void AddOutline(UnityEngine.UI.VertexHelper mesh, Rect rect, float inset, float bevel,
            Color32 top, Color32 bottom)
        {
            float left = rect.xMin + inset;
            float right = rect.xMax - inset;
            float upper = rect.yMax - inset;
            float lower = rect.yMin + inset;
            bevel = Mathf.Min(bevel, Mathf.Min(right - left, upper - lower) * .5f);
            Color32 upperCorner = Color32.Lerp(top, bottom, bevel / (upper - lower));
            Color32 lowerCorner = Color32.Lerp(bottom, top, bevel / (upper - lower));
            mesh.AddVert(new Vector3(left + bevel, upper), top, Vector2.zero);
            mesh.AddVert(new Vector3(right - bevel, upper), top, Vector2.zero);
            mesh.AddVert(new Vector3(right, upper - bevel), upperCorner, Vector2.zero);
            mesh.AddVert(new Vector3(right, lower + bevel), lowerCorner, Vector2.zero);
            mesh.AddVert(new Vector3(right - bevel, lower), bottom, Vector2.zero);
            mesh.AddVert(new Vector3(left + bevel, lower), bottom, Vector2.zero);
            mesh.AddVert(new Vector3(left, lower + bevel), lowerCorner, Vector2.zero);
            mesh.AddVert(new Vector3(left, upper - bevel), upperCorner, Vector2.zero);
        }
    }
}
