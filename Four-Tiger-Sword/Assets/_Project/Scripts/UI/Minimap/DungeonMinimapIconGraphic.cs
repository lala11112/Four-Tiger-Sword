using UnityEngine;

/// <summary>Replaceable map icons with simple built-in symbols when no sprite is assigned.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DungeonMinimapIconGraphic : UnityEngine.UI.MaskableGraphic
{
    public enum Symbol { Player, Shop, Exit, Enemy, RewardChest, Boss }

    [SerializeField] private Symbol symbol;
    [SerializeField] private Sprite iconSprite;
    [SerializeField] private bool preserveAspect = true;
    public Symbol Kind => symbol;
    public Sprite IconSprite
    {
        get => iconSprite;
        set
        {
            if (iconSprite == value) return;
            iconSprite = value;
            SetAllDirty();
        }
    }
    public bool PreserveAspect
    {
        get => preserveAspect;
        set
        {
            if (preserveAspect == value) return;
            preserveAspect = value;
            SetVerticesDirty();
        }
    }
    public override Texture mainTexture => iconSprite != null ? iconSprite.texture : Texture2D.whiteTexture;

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
        if (iconSprite != null)
        {
            SpriteQuad(vertices, bounds);
            return;
        }
        switch (symbol)
        {
            case Symbol.Enemy:
                Dot(vertices, bounds);
                break;
            case Symbol.Shop:
                Triangle(vertices, bounds, new Vector2(-.5f, .12f), new Vector2(0f, .47f), new Vector2(.5f, .12f));
                Quad(vertices, bounds, -.36f, -.4f, -.25f, .08f);
                Quad(vertices, bounds, .25f, -.4f, .36f, .08f);
                Quad(vertices, bounds, -.43f, -.4f, .43f, -.27f);
                Quad(vertices, bounds, -.28f, -.08f, .28f, .06f);
                break;
            case Symbol.RewardChest:
                Quad(vertices, bounds, -.46f, -.02f, .46f, .31f);
                Quad(vertices, bounds, -.42f, -.35f, .42f, -.11f);
                Quad(vertices, bounds, -.09f, -.19f, .09f, .12f);
                break;
            case Symbol.Boss:
                Triangle(vertices, bounds, new Vector2(-.45f, .47f), new Vector2(-.30f, -.01f), new Vector2(.04f, .16f));
                Triangle(vertices, bounds, new Vector2(.45f, .47f), new Vector2(.30f, -.01f), new Vector2(-.04f, .16f));
                Quad(vertices, bounds, -.31f, -.16f, .31f, .12f);
                Triangle(vertices, bounds, new Vector2(-.30f, -.16f), new Vector2(0f, -.48f), new Vector2(.30f, -.16f));
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

    private void Dot(UnityEngine.UI.VertexHelper vertices, Rect bounds)
    {
        const int segments = 16;
        Vertex(vertices, bounds, Vector2.zero);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * (Mathf.PI * 2f / segments);
            Vertex(vertices, bounds, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .5f);
        }
        for (int i = 0; i < segments; i++) vertices.AddTriangle(0, i + 1, (i + 1) % segments + 1);
    }

    private void SpriteQuad(UnityEngine.UI.VertexHelper vertices, Rect bounds)
    {
        if (preserveAspect && bounds.width > 0f && bounds.height > 0f)
        {
            float aspect = iconSprite.rect.width / Mathf.Max(.0001f, iconSprite.rect.height);
            Vector2 center = bounds.center;
            if (bounds.width / bounds.height > aspect) bounds.width = bounds.height * aspect;
            else bounds.height = bounds.width / aspect;
            bounds.center = center;
        }
        Vector4 uv = UnityEngine.Sprites.DataUtility.GetOuterUV(iconSprite);
        vertices.AddVert(new Vector3(bounds.xMin, bounds.yMin, 0f), color, new Vector2(uv.x, uv.y));
        vertices.AddVert(new Vector3(bounds.xMin, bounds.yMax, 0f), color, new Vector2(uv.x, uv.w));
        vertices.AddVert(new Vector3(bounds.xMax, bounds.yMax, 0f), color, new Vector2(uv.z, uv.w));
        vertices.AddVert(new Vector3(bounds.xMax, bounds.yMin, 0f), color, new Vector2(uv.z, uv.y));
        vertices.AddTriangle(0, 1, 2);
        vertices.AddTriangle(0, 2, 3);
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
