using UnityEngine;

namespace StudioBaekho
{
    /// <summary>Optional demo-only camera. Gameplay cameras do not need this component.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class BaekhoDungeonOverviewCamera : MonoBehaviour
    {
        public BaekhoDungeonGenerator generator;
        private void OnEnable() { if (generator != null) generator.Completed += Frame; }
        private void OnDisable() { if (generator != null) generator.Completed -= Frame; }
        private void Frame(BaekhoDungeonResult result)
        {
            if (result.root == null) return;
            var renderers = result.root.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var camera = GetComponent<Camera>();
            camera.orthographic = true;
            transform.rotation = Quaternion.Euler(65f, 0f, 0f);
            float halfWidth = 0f, halfHeight = 0f;
            for (int i = 0; i < 8; i++)
            {
                var offset = Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(transform.right, offset)));
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(transform.up, offset)));
            }
            camera.orthographicSize = Mathf.Max(halfHeight, halfWidth / Mathf.Max(0.1f, camera.aspect)) * 1.1f + 2f;
            float distance = Mathf.Max(100f, bounds.size.magnitude * 2f);
            transform.position = bounds.center - transform.forward * distance;
            camera.farClipPlane = distance * 3f;
        }
    }
}
