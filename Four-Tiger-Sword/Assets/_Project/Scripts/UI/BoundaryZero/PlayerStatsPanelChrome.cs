using UnityEngine;

/// <summary>Draws the beveled frame at the canvas resolution.</summary>
[RequireComponent(typeof(CanvasRenderer))]
internal sealed class PlayerStatsPanelChrome : UnityEngine.UI.MaskableGraphic
{
    private const float Bevel = 24f;
    private static readonly Color32 Top = new Color32(20, 36, 56, 247);
    private static readonly Color32 Bottom = new Color32(9, 20, 33, 247);
    private static readonly Color32 Border = new Color32(169, 191, 211, 102);
    private static readonly Color32 Silver = new Color32(221, 232, 243, 255);
    private static readonly Color32 Red = new Color32(231, 43, 70, 255);
    private bool _selection;

    public void UseSelectionFill()
    {
        _selection = true;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (_selection)
        {
            var left = new Color32(173, 66, 87, 46);
            var right = new Color32(153, 181, 206, 10);
            mesh.AddVert(new Vector3(rect.xMin, rect.yMax), left, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMax), right, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMin), right, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMin, rect.yMin), left, Vector2.zero);
            mesh.AddTriangle(0, 1, 2);
            mesh.AddTriangle(0, 2, 3);
            return;
        }

        float w = rect.width;
        float h = rect.height;
        Vector2[] outline =
        {
            new Vector2(Bevel, 1), new Vector2(w - Bevel, 1), new Vector2(w - 1, Bevel),
            new Vector2(w - 1, h - Bevel), new Vector2(w - Bevel, h - 1),
            new Vector2(Bevel, h - 1), new Vector2(1, h - Bevel), new Vector2(1, Bevel)
        };
        Polygon(mesh, rect, outline, Top, Bottom);
        for (int i = 0; i < outline.Length; i++)
            Stroke(mesh, rect, outline[i], outline[(i + 1) % outline.Length], 1, Border);

        Stroke(mesh, rect, new Vector2(2, 59), new Vector2(2, Bevel), 2, Silver);
        Stroke(mesh, rect, new Vector2(2, Bevel), new Vector2(Bevel, 2), 2, Silver);
        Stroke(mesh, rect, new Vector2(Bevel, 2), new Vector2(105, 2), 2, Silver);
        Stroke(mesh, rect, new Vector2(w - 2, h - 59), new Vector2(w - 2, h - Bevel), 2, Silver);
        Stroke(mesh, rect, new Vector2(w - 2, h - Bevel), new Vector2(w - Bevel, h - 2), 2, Silver);
        Stroke(mesh, rect, new Vector2(w - Bevel, h - 2), new Vector2(w - 118, h - 2), 2, Silver);
        Polygon(mesh, rect, new Vector2[]
        {
            new Vector2(8, 29), new Vector2(29, 8), new Vector2(76, 8),
            new Vector2(64, 17), new Vector2(33, 17), new Vector2(8, 45)
        }, Silver, Silver);
        Polygon(mesh, rect, new Vector2[]
        {
            new Vector2(113, 0), new Vector2(173, 0), new Vector2(164, 5), new Vector2(109, 5)
        }, Red, Red);
        Polygon(mesh, rect, new Vector2[]
        {
            new Vector2(w - 8, h - 40), new Vector2(w - 8, h - 23),
            new Vector2(w - 24, h - 7), new Vector2(w - 41, h - 7)
        }, Red, Red);
    }

    public override bool Raycast(Vector2 screenPoint, Camera eventCamera)
    {
        if (!base.Raycast(screenPoint, eventCamera)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out var local))
            return false;

        var rect = rectTransform.rect;
        float x = Mathf.Min(local.x - rect.xMin, rect.xMax - local.x);
        float y = Mathf.Min(local.y - rect.yMin, rect.yMax - local.y);
        return x + y >= Bevel;
    }

    private static void Stroke(UnityEngine.UI.VertexHelper mesh, Rect rect,
        Vector2 start, Vector2 end, float width, Color32 tint)
    {
        Vector2 direction = (end - start).normalized;
        Vector2 normal = new Vector2(-direction.y, direction.x) * (width * .5f);
        Polygon(mesh, rect, new[] { start - normal, end - normal, end + normal, start + normal }, tint, tint);
    }

    private static void Polygon(UnityEngine.UI.VertexHelper mesh, Rect rect,
        Vector2[] points, Color32 top, Color32 bottom)
    {
        int first = mesh.currentVertCount;
        foreach (Vector2 point in points)
        {
            var tint = Color32.Lerp(top, bottom, point.y / rect.height);
            mesh.AddVert(new Vector3(rect.xMin + point.x, rect.yMax - point.y), tint, Vector2.zero);
        }
        for (int i = 1; i < points.Length - 1; i++)
            mesh.AddTriangle(first, first + i, first + i + 1);
    }
}
