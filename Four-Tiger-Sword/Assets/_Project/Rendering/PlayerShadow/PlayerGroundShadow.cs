using UnityEngine;
using UnityEngine.Rendering;

namespace FourTigerSword.Rendering
{
    /// <summary>Draws a soft contact shadow on the walkable surface below the player's capsule.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("Four Tiger Sword/Rendering/Player Ground Shadow")]
    public sealed class PlayerGroundShadow : MonoBehaviour
    {
        [SerializeField] private Material shadowMaterial;
        [SerializeField, Min(0.01f)] private float width = 0.9f;
        [SerializeField, Min(0.01f)] private float depth = 0.65f;
        [SerializeField, Range(0f, 1f)] private float maxOpacity = 0.375f;
        [SerializeField, Min(0.01f)] private float maxGroundDistance = 3f;

        private const int SegmentCount = 16;
        private const float SurfaceOffset = 0.01f;
        private const float SurfaceBreak = 0.1f;
        private const float MinUpNormal = 0.5f;
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly Vector2[] SampleUv = BuildSampleUv();

        private readonly RaycastHit[] _hits = new RaycastHit[SegmentCount + 1];
        private readonly bool[] _valid = new bool[SegmentCount + 1];
        private readonly Vector3[] _vertices = new Vector3[SegmentCount + 1];
        private readonly int[] _indices = new int[SegmentCount * 3];

        private CharacterController _controller;
        private int _groundMask;
        private GameObject _visual;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private MaterialPropertyBlock _properties;
        private bool _fallRevealSuppressed;

        public bool IsVisible => _renderer != null && _renderer.enabled;
        public int VisibleTriangleCount { get; private set; }
        public float CurrentOpacity { get; private set; }

        internal bool OwnsRenderer(Renderer renderer) => renderer == _renderer;

        internal void SetFallRevealSuppressed(bool suppressed)
        {
            _fallRevealSuppressed = suppressed;
            if (suppressed) Hide();
        }

        private void LateUpdate()
        {
            if (Application.IsPlaying(gameObject)) RefreshShadow();
        }

        public void RefreshShadow()
        {
            if (!isActiveAndEnabled || _fallRevealSuppressed || shadowMaterial == null || maxOpacity <= 0f)
            {
                Hide();
                return;
            }

            if (_controller == null)
            {
                _controller = GetComponent<CharacterController>();
                int groundLayer = LayerMask.NameToLayer("Ground");
                _groundMask = groundLayer >= 0 ? 1 << groundLayer : 0;
            }

            if (_controller == null || !_controller.enabled || _groundMask == 0)
            {
                Hide();
                return;
            }

            Bounds capsule = _controller.bounds;
            Vector3 feet = capsule.center;
            feet.y = capsule.min.y;
            // Start above the feet to find the surface even when the capsule's skin overlaps it.
            float lift = Mathf.Max(0.1f, _controller.stepOffset + _controller.skinWidth);
            Vector3 origin = feet + Vector3.up * lift;
            if (!Sample(origin, lift + maxGroundDistance, out _hits[0]))
            {
                Hide();
                return;
            }

            float height = Mathf.Max(0f, feet.y - _hits[0].point.y);
            if (height >= maxGroundDistance)
            {
                Hide();
                return;
            }

            // Allow the edge samples to follow slopes below the center of the footprint.
            float rayLength = lift + height + Mathf.Max(width, depth);
            for (int i = 1; i <= SegmentCount; i++)
            {
                Vector2 offset = SampleUv[i] - Vector2.one * 0.5f;
                Vector3 sample = origin + new Vector3(offset.x * width, 0f, offset.y * depth);
                _valid[i] = Sample(sample, rayLength, out _hits[i]) && SameSurface(_hits[0], _hits[i]);
            }

            int indexCount = 0;
            for (int i = 1; i <= SegmentCount; i++)
            {
                int next = i == SegmentCount ? 1 : i + 1;
                if (!_valid[i] || !_valid[next] || !SameSurface(_hits[i], _hits[next])) continue;
                _indices[indexCount++] = 0;
                _indices[indexCount++] = next;
                _indices[indexCount++] = i;
            }

            if (indexCount == 0)
            {
                Hide();
                return;
            }

            EnsureVisual();
            Matrix4x4 worldToLocal = _visual.transform.worldToLocalMatrix;
            _vertices[0] = SurfaceVertex(_hits[0], worldToLocal);
            for (int i = 1; i <= SegmentCount; i++)
                _vertices[i] = _valid[i] ? SurfaceVertex(_hits[i], worldToLocal) : _vertices[0];
            _mesh.SetVertices(_vertices);
            _mesh.SetIndices(_indices, 0, indexCount, MeshTopology.Triangles, 0);

            CurrentOpacity = maxOpacity * (1f - Mathf.SmoothStep(0f, 1f, height / maxGroundDistance));
            VisibleTriangleCount = indexCount / 3;
            _properties.SetFloat(OpacityId, CurrentOpacity);
            _renderer.SetPropertyBlock(_properties);
            _renderer.enabled = true;
        }

        private bool Sample(Vector3 origin, float distance, out RaycastHit hit) =>
            Physics.Raycast(origin, Vector3.down, out hit, distance, _groundMask, QueryTriggerInteraction.Ignore)
            && hit.normal.y >= MinUpNormal;

        private static bool SameSurface(RaycastHit a, RaycastHit b)
        {
            Vector3 span = b.point - a.point;
            // A ramp stays near its tangent plane, whereas a step crosses it.
            return Mathf.Abs(Vector3.Dot(span, a.normal)) <= SurfaceBreak
                && Mathf.Abs(Vector3.Dot(span, b.normal)) <= SurfaceBreak;
        }

        private static Vector3 SurfaceVertex(RaycastHit hit, Matrix4x4 worldToLocal) =>
            worldToLocal.MultiplyPoint3x4(hit.point + hit.normal * SurfaceOffset);

        private void EnsureVisual()
        {
            if (_visual != null) return;
            _visual = new GameObject("Player Ground Shadow") { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
            _visual.transform.SetParent(transform, false);
            _mesh = new Mesh { name = "Player Ground Shadow", hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, SampleUv);
            _visual.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _visual.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = shadowMaterial;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _properties = new MaterialPropertyBlock();
        }

        private void Hide()
        {
            if (_renderer != null) _renderer.enabled = false;
            VisibleTriangleCount = 0;
            CurrentOpacity = 0f;
        }

        private void OnDisable()
        {
            Hide();
            if (_visual != null) Release(_visual);
            if (_mesh != null) Release(_mesh);
            _visual = null;
            _renderer = null;
            _mesh = null;
            _properties = null;
        }

        private static void Release(Object resource)
        {
            if (Application.isPlaying) Destroy(resource);
            else DestroyImmediate(resource);
        }

        private static Vector2[] BuildSampleUv()
        {
            var samples = new Vector2[SegmentCount + 1];
            samples[0] = Vector2.one * 0.5f;
            for (int i = 0; i < SegmentCount; i++)
            {
                float angle = i * (2f * Mathf.PI / SegmentCount);
                samples[i + 1] = samples[0] + 0.5f * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            return samples;
        }
    }
}
