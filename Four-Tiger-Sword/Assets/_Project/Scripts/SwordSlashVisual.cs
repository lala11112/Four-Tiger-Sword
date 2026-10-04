using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A short, tapered ribbon; generated geometry needs no texture or VFX package.</summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class SwordSlashVisual : MonoBehaviour
{
    [ColorUsage(true, true)] public Color Tint = new Color(0.3f, 1.6f, 2.5f, 0.8f);
    [Min(0.1f)] public float Radius = 1.65f;
    [Min(0.01f)] public float Width = 0.45f;
    [Range(20f, 300f)] public float Arc = 150f;
    private Mesh _mesh;
    private MeshRenderer _renderer;
    private MaterialPropertyBlock _properties;
    private float _age;
    private float _lifetime = 0.22f;
    private static readonly int TintId = Shader.PropertyToID("_Tint");

    private void Awake()
    {
        const int segments = 48;
        var vertices = new Vector3[(segments + 1) * 3];
        var colors = new Color[vertices.Length];
        var triangles = new int[segments * 12];
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = (t - 0.5f) * Arc * Mathf.Deg2Rad;
            float taper = Mathf.Sin(t * Mathf.PI);
            var direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            for (int row = 0; row < 3; row++)
            {
                int v = i * 3 + row;
                vertices[v] = direction * (Radius - Width * taper * (1f - row * 0.5f));
                colors[v] = new Color(1f, 1f, 1f, row == 1 ? taper : 0f);
            }
            if (i == segments) continue;
            for (int row = 0; row < 2; row++)
            {
                int a = i * 3 + row;
                int tri = i * 12 + row * 6;
                triangles[tri] = a; triangles[tri + 1] = a + 3; triangles[tri + 2] = a + 1;
                triangles[tri + 3] = a + 1; triangles[tri + 4] = a + 3; triangles[tri + 5] = a + 4;
            }
        }
        _mesh = new Mesh { name = "Sword slash ribbon" };
        _mesh.vertices = vertices;
        _mesh.colors = colors;
        _mesh.triangles = triangles;
        _mesh.RecalculateBounds();
        GetComponent<MeshFilter>().sharedMesh = _mesh;
        _renderer = GetComponent<MeshRenderer>();
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _properties = new MaterialPropertyBlock();
        ApplyColor(1f);
    }

    public void Play(float lifetime)
    {
        _age = 0f;
        _lifetime = Mathf.Max(0.01f, lifetime);
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float progress = Mathf.Clamp01(_age / _lifetime);
        ApplyColor((1f - progress) * (1f - progress));
        if (progress >= 1f) Destroy(gameObject);
    }

    private void ApplyColor(float opacity)
    {
        Color color = Tint;
        color.a *= opacity;
        _properties.SetColor(TintId, color);
        _renderer.SetPropertyBlock(_properties);
    }

    private void OnDestroy()
    {
        if (_mesh == null) return;
        if (Application.isPlaying) Destroy(_mesh);
        else DestroyImmediate(_mesh);
    }
}
