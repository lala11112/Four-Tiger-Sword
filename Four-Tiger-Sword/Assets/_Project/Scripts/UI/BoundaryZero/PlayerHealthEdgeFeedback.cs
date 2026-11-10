using UnityEngine;

/// <summary>Draws a low-opacity edge tint while preserving the full central play area.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PlayerHealthEdgeFeedback : UnityEngine.UI.MaskableGraphic
{
    private const float FlashSeconds = .42f;
    private const float LowHealthThreshold = .3f;
    private PlayerStatManager _stats;
    private float _flash;
    private float _lowHealth;
    private float _opacity;

    internal static PlayerHealthEdgeFeedback Create()
    {
        var root = new GameObject("PlayerHealthFeedback", typeof(RectTransform), typeof(Canvas));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 75;

        var edge = new GameObject("HealthEdge", typeof(RectTransform), typeof(PlayerHealthEdgeFeedback))
            .GetComponent<PlayerHealthEdgeFeedback>();
        edge.rectTransform.SetParent(root.transform, false);
        edge.rectTransform.anchorMin = Vector2.zero;
        edge.rectTransform.anchorMax = Vector2.one;
        edge.rectTransform.offsetMin = edge.rectTransform.offsetMax = Vector2.zero;
        edge.raycastTarget = false;
        edge.color = new Color32(180, 31, 51, 255);
        return edge;
    }

    internal void SetStats(PlayerStatManager stats)
    {
        if (_stats == stats) return;
        Unbind();
        _stats = stats;
        if (_stats != null) _stats.OnDamageTaken += OnDamageTaken;
    }

    private void OnDamageTaken(float damage, ElementType element, bool critical)
    {
        if (damage > 0f) _flash = 1f;
    }

    internal void Tick()
    {
        float delta = Time.unscaledDeltaTime;
        float hpRatio = _stats != null && _stats.MaxHp > 0f ? _stats.CurrentHp / _stats.MaxHp : 1f;
        float lowTarget = hpRatio > 0f ? Mathf.InverseLerp(LowHealthThreshold, .07f, hpRatio) : 0f;
        _lowHealth = Mathf.MoveTowards(_lowHealth, lowTarget, delta * 4f);
        float pulse = .85f + .15f * Mathf.Sin(Time.unscaledTime * 3.5f);
        SetOpacity(Mathf.Min(.4f, _flash * .34f + _lowHealth * pulse * .20f));
        _flash = Mathf.MoveTowards(_flash, 0f, delta / FlashSeconds);
    }

    private void Unbind()
    {
        if (_stats != null) _stats.OnDamageTaken -= OnDamageTaken;
        _stats = null;
        _lowHealth = 0f;
        _flash = 0f;
        SetOpacity(0f);
    }

    protected override void OnDisable()
    {
        Unbind();
        base.OnDisable();
    }

    internal void Dispose() => Destroy(transform.parent.gameObject);

    private void SetOpacity(float value)
    {
        if (Mathf.Approximately(_opacity, value)) return;
        _opacity = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertices)
    {
        vertices.Clear();
        if (_opacity <= 0f) return;
        Rect rect = GetPixelAdjustedRect();
        Vector2 inset = new Vector2(rect.width * .15f, rect.height * .18f);
        Vector2 outerBottomLeft = rect.min, outerTopRight = rect.max;
        Vector2 outerTopLeft = new Vector2(rect.xMin, rect.yMax);
        Vector2 outerBottomRight = new Vector2(rect.xMax, rect.yMin);
        Vector2 innerBottomLeft = outerBottomLeft + inset;
        Vector2 innerTopLeft = outerTopLeft + new Vector2(inset.x, -inset.y);
        Vector2 innerTopRight = outerTopRight - inset;
        Vector2 innerBottomRight = outerBottomRight + new Vector2(-inset.x, inset.y);

        Color outer = color;
        outer.a *= _opacity;
        Color inner = new Color(outer.r, outer.g, outer.b, 0f);
        AddEdge(vertices, outerBottomLeft, outerTopLeft, innerTopLeft, innerBottomLeft, outer, inner);
        AddEdge(vertices, outerTopLeft, outerTopRight, innerTopRight, innerTopLeft, outer, inner);
        AddEdge(vertices, outerTopRight, outerBottomRight, innerBottomRight, innerTopRight, outer, inner);
        AddEdge(vertices, outerBottomRight, outerBottomLeft, innerBottomLeft, innerBottomRight, outer, inner);
    }

    private static void AddEdge(UnityEngine.UI.VertexHelper vertices, Vector2 outerA, Vector2 outerB,
        Vector2 innerB, Vector2 innerA, Color outer, Color inner)
    {
        int first = vertices.currentVertCount;
        vertices.AddVert(outerA, outer, Vector2.zero);
        vertices.AddVert(outerB, outer, Vector2.zero);
        vertices.AddVert(innerB, inner, Vector2.zero);
        vertices.AddVert(innerA, inner, Vector2.zero);
        vertices.AddTriangle(first, first + 1, first + 2);
        vertices.AddTriangle(first, first + 2, first + 3);
    }
}
