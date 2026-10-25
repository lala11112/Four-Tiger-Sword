using UnityEngine;

/// <summary>Small texture-free symbols that remain legible at minimap scale.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DungeonMinimapIconGraphic : UnityEngine.UI.MaskableGraphic
{
    public enum Symbol { Player, Shop, Exit }

    [SerializeField] private Symbol symbol;
    public Symbol Kind => symbol;

    public void SetSymbol(Symbol value)
    {
        raycastTarget = false;
        if (symbol == value) return;
        symbol = value;
        SetVerticesDirty();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertices)
    {
        vertices.Clear();
        Rect bounds = GetPixelAdjustedRect();
        switch (symbol)
        {
            case Symbol.Shop:
                Triangle(vertices, bounds, new Vector2(-.5f, .12f), new Vector2(0f, .47f), new Vector2(.5f, .12f));
                Quad(vertices, bounds, -.36f, -.4f, -.25f, .08f);
                Quad(vertices, bounds, .25f, -.4f, .36f, .08f);
                Quad(vertices, bounds, -.43f, -.4f, .43f, -.27f);
                Quad(vertices, bounds, -.28f, -.08f, .28f, .06f);
                break;
            case Symbol.Exit:
                Quad(vertices, bounds, -.43f, -.45f, -.3f, .45f);
                Quad(vertices, bounds, -.43f, .32f, .18f, .45f);
                Quad(vertices, bounds, .05f, .14f, .18f, .45f);
                Quad(vertices, bounds, -.43f, -.45f, .18f, -.32f);
                Quad(vertices, bounds, -.17f, -.07f, .23f, .07f);
                Triangle(vertices, bounds, new Vector2(.17f, .24f), new Vector2(.49f, 0f), new Vector2(.17f, -.24f));
                break;
            default:
                Triangle(vertices, bounds, new Vector2(0f, .5f), new Vector2(-.43f, -.42f), new Vector2(0f, -.23f));
                Triangle(vertices, bounds, new Vector2(0f, .5f), new Vector2(0f, -.23f), new Vector2(.43f, -.42f));
                break;
        }
    }

    private void Quad(UnityEngine.UI.VertexHelper vertices, Rect bounds, float left, float bottom, float right, float top)
    {
        int first = vertices.currentVertCount;
        Vertex(vertices, bounds, new Vector2(left, bottom));
        Vertex(vertices, bounds, new Vector2(left, top));
        Vertex(vertices, bounds, new Vector2(right, top));
        Vertex(vertices, bounds, new Vector2(right, bottom));
        vertices.AddTriangle(first, first + 1, first + 2);
        vertices.AddTriangle(first, first + 2, first + 3);
    }

    private void Triangle(UnityEngine.UI.VertexHelper vertices, Rect bounds, Vector2 a, Vector2 b, Vector2 c)
    {
        int first = vertices.currentVertCount;
        Vertex(vertices, bounds, a);
        Vertex(vertices, bounds, b);
        Vertex(vertices, bounds, c);
        vertices.AddTriangle(first, first + 1, first + 2);
    }

    private void Vertex(UnityEngine.UI.VertexHelper vertices, Rect bounds, Vector2 point)
    {
        var position = new Vector3(bounds.center.x + point.x * bounds.width, bounds.center.y + point.y * bounds.height, 0f);
        vertices.AddVert(position, color, new Vector2(.5f, .5f));
    }
}
