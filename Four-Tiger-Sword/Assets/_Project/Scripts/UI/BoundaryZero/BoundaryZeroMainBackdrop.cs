using UnityEngine;

/// <summary>The guide's untextured navy field and diagonal ornaments, drawn at any aspect ratio.</summary>
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(CanvasRenderer))]
public sealed class BoundaryZeroMainBackdrop : UnityEngine.UI.MaskableGraphic
{
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        const int columns = 24, rows = 16;
        for (int y = 0; y <= rows; y++)
        for (int x = 0; x <= columns; x++)
        {
            float u = x / (float)columns, v = y / (float)rows;
            float glow = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u * 1.3f, v), new Vector2(.54f, .52f)) * 1.6f);
            Color tint = Color.Lerp(new Color32(12, 21, 32, 255), new Color32(29, 45, 62, 255), glow);
            mesh.AddVert(new Vector3(rect.xMin + u * rect.width, rect.yMin + v * rect.height), tint, Vector2.zero);
            if (x == columns || y == rows) continue;
            int a = y * (columns + 1) + x;
            mesh.AddTriangle(a, a + columns + 1, a + 1);
            mesh.AddTriangle(a + 1, a + columns + 1, a + columns + 2);
        }
        float scale = Mathf.Min(rect.width / 2560f, rect.height / 1440f);
        Vector2 P(float x, float y) => rect.center + new Vector2(x - 1280, 720 - y) * scale;
        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(a, tint, Vector2.zero); mesh.AddVert(b, tint, Vector2.zero);
            mesh.AddVert(c, tint, Vector2.zero); mesh.AddVert(d, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start + 2, start + 3, start);
        }
        void Line(float x, float y, float width, float height, Color tint)
            => Quad(P(x, y), P(x + width, y), P(x + width, y + height), P(x, y + height), tint);
        var faint = new Color32(57, 76, 97, 65);
        Quad(P(515, 0), P(720, 0), P(40, 1335), P(-165, 1335), new Color32(43, 61, 80, 35));
        Quad(P(560, 0), P(562, 0), P(-158, 1440), P(-160, 1440), new Color32(68, 82, 101, 35));
        Quad(P(355, 340), P(310, 500), P(-50, 1270), P(-50, 1180), new Color32(153, 30, 58, 145));
        Line(90, 92, 450, 2, faint); Line(90, 92, 2, 52, faint);
        Line(88, 1336, 295, 1, faint); Line(2300, 1336, 155, 1, faint);
        for (int i = 0; i < 5; i++)
            Quad(P(2301 + i * 22, 92), P(2303 + i * 22, 92), P(2294 + i * 22, 110),
                P(2292 + i * 22, 110), i == 0 ? new Color32(166, 38, 65, 145) : faint);
    }
}
