using UnityEngine;

namespace BoundaryZero.Inventory
{
    /// <summary>A passive grade border that preserves the background and selection graphics.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ItemRarityFrame : UnityEngine.UI.MaskableGraphic
    {
        public static Color ColorFor(int grade) => grade switch
        {
            1 => new Color32(218, 151, 105, 255),
            2 => new Color32(198, 211, 226, 255),
            3 => new Color32(251, 207, 103, 255),
            4 => new Color32(139, 226, 218, 255),
            _ => Color.clear
        };

        public static void Show(RectTransform card, int grade)
        {
            var edgeColor = ColorFor(grade);
            var child = card.Find("ItemRarityFrame");
            if (child == null)
            {
                if (edgeColor.a == 0f) return;
                var node = new GameObject("ItemRarityFrame", typeof(RectTransform), typeof(ItemRarityFrame));
                node.layer = card.gameObject.layer;
                child = node.transform;
                child.SetParent(card, false);
                var rect = (RectTransform)child;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(4f, 4f);
                rect.offsetMax = new Vector2(-4f, -4f);
            }
            var frame = child.GetComponent<ItemRarityFrame>();
            frame.raycastTarget = false;
            frame.color = edgeColor;
            frame.enabled = edgeColor.a > 0f;
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertices)
        {
            vertices.Clear();
            var rect = GetPixelAdjustedRect();
            const float weight = 2f;
            if (rect.width <= weight * 2 || rect.height <= weight * 2) return;
            AddEdge(vertices, rect.xMin, rect.yMin, rect.xMax, rect.yMin + weight);
            AddEdge(vertices, rect.xMin, rect.yMax - weight, rect.xMax, rect.yMax);
            AddEdge(vertices, rect.xMin, rect.yMin + weight, rect.xMin + weight, rect.yMax - weight);
            AddEdge(vertices, rect.xMax - weight, rect.yMin + weight, rect.xMax, rect.yMax - weight);
        }

        private void AddEdge(UnityEngine.UI.VertexHelper vertices, float left, float bottom, float right, float top)
        {
            int first = vertices.currentVertCount;
            vertices.AddVert(new Vector3(left, bottom), color, Vector2.zero);
            vertices.AddVert(new Vector3(left, top), color, Vector2.zero);
            vertices.AddVert(new Vector3(right, top), color, Vector2.zero);
            vertices.AddVert(new Vector3(right, bottom), color, Vector2.zero);
            vertices.AddTriangle(first, first + 1, first + 2);
            vertices.AddTriangle(first, first + 2, first + 3);
        }
    }
}
