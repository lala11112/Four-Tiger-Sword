using UnityEngine;

/// <summary>Keeps an authored, centered popup inside its current canvas while preserving its design scale.</summary>
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
public sealed class BoundaryZeroWindowFit : MonoBehaviour
{
    [SerializeField] private Vector2 designSize = new Vector2(1920f, 1120f);
    [SerializeField, Min(0.01f)] private float preferredScale = .88f;
    [SerializeField, Min(0f)] private float margin = 32f;
    private RectTransform _rect;

    public void Configure(Vector2 size, float scale, float screenMargin = 32f)
    {
        designSize = size;
        preferredScale = Mathf.Max(.01f, scale);
        margin = Mathf.Max(0f, screenMargin);
        Refresh();
    }

    private void OnEnable() => Refresh();
    private void LateUpdate() => Refresh();
    private void OnRectTransformDimensionsChange() => Refresh();

    public void Refresh()
    {
        if (_rect == null) _rect = GetComponent<RectTransform>();
        var parent = _rect.parent as RectTransform;
        if (parent == null || designSize.x <= 0f || designSize.y <= 0f) return;
        Vector2 available = parent.rect.size - Vector2.one * (margin * 2f);
        if (available.x <= 0f || available.y <= 0f) return;
        float scale = Mathf.Max(.01f, Mathf.Min(preferredScale, available.x / designSize.x, available.y / designSize.y));
        if (_rect.sizeDelta != designSize) _rect.sizeDelta = designSize;
        Vector3 target = new Vector3(scale, scale, 1f);
        if (_rect.localScale != target) _rect.localScale = target;
    }
}
