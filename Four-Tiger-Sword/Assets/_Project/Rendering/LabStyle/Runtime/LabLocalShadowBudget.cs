using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FourTigerSword.Rendering
{
    /// <summary>Limits shadow atlas use without changing authored local-light intensity or assets.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Four Tiger Sword/Rendering/Lab Local Shadow Budget")]
    public sealed class LabLocalShadowBudget : MonoBehaviour
    {
        public BaekhoDungeonGenerator generator;
        public Transform explicitRoot;
        public Camera viewCamera;
        public bool includeGeneratedLights;
        public Transform focusTarget;
        [Range(0, 8)] public int maxShadowedLights = 4;
        [Min(0f)] public float maxDistance = 25f;
        [Min(0.1f)] public float refreshInterval = 0.5f;

        private sealed class LightState
        {
            public Light light;
            public LightShadows original, preferred, applied;
            public float distanceSquared;
            public bool coversFocus;
        }

        private readonly List<LightState> _lights = new();
        private readonly List<LightState> _candidates = new();
        private BaekhoDungeonGenerator _subscribedGenerator;
        private Transform _root;
        private float _nextRefresh;
        public int TrackedLightCount => _lights.Count;
        public int ShadowedLightCount { get; private set; }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (generator == null) generator = GetComponent<BaekhoDungeonGenerator>();
            _subscribedGenerator = generator;
            if (_subscribedGenerator != null) _subscribedGenerator.Completed += OnGenerated;
            ApplyCurrent();
        }

        private void OnGenerated(BaekhoDungeonResult result)
        {
            if (result != null && result.success && _subscribedGenerator != null &&
                result.root == _subscribedGenerator.CurrentRoot) ApplyCurrent();
        }

        [ContextMenu("Apply Local Shadow Budget (Scoped Preview)")]
        public void ApplyCurrent()
        {
            Transform root = generator != null && generator.CurrentRoot != null
                ? generator.CurrentRoot.transform : explicitRoot;
            if (root != _root || root == null)
            {
                RestoreOriginals();
                _root = root;
                if (root == null) return;
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    if (light.type != LightType.Spot && light.type != LightType.Point) continue;
                    if (light.bakingOutput.lightmapBakeType == LightmapBakeType.Baked) continue;
                    LightShadows original = light.shadows;
                    LightShadows preferred = original;
                    if (original == LightShadows.None && light.TryGetComponent(out BaekhoGeneratedLightShadow generated))
                        preferred = generated.authoredMode;
                    if (preferred != LightShadows.None)
                        _lights.Add(new LightState { light = light, original = original, preferred = preferred, applied = original });
                }
            }
            RefreshBudget();
        }

        private void Update()
        {
            if (!Application.isPlaying || Time.unscaledTime < _nextRefresh) return;
            if (_root == null) { RestoreOriginals(); return; }
            RefreshBudget();
        }

        private void RefreshBudget()
        {
            _nextRefresh = Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);
            _candidates.Clear();
            Camera camera = viewCamera != null ? viewCamera : Camera.main;
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool supported = pipeline != null && pipeline.supportsAdditionalLightShadows && camera != null;
            float limitSquared = Mathf.Max(0f, maxDistance) * Mathf.Max(0f, maxDistance);
            bool hasFocus = focusTarget != null && focusTarget.gameObject.activeInHierarchy;
            Vector3 focusPosition = hasFocus ? focusTarget.position : Vector3.zero;
            for (int index = _lights.Count - 1; index >= 0; index--)
            {
                LightState state = _lights[index];
                if (state.light == null || _root == null ||
                    (state.light.transform != _root && !state.light.transform.IsChildOf(_root)))
                {
                    Restore(state);
                    _lights.RemoveAt(index);
                    continue;
                }
                // Preserve a gameplay effect that has taken control of this light's shadow mode.
                if (state.light.shadows != state.applied) continue;
                if (!supported || !state.light.isActiveAndEnabled ||
                    (!includeGeneratedLights && state.original == LightShadows.None)) continue;
                state.distanceSquared = (state.light.transform.position - camera.transform.position).sqrMagnitude;
                if (state.distanceSquared > limitSquared) continue;
                state.coversFocus = hasFocus && CoversPoint(state.light, focusPosition);
                _candidates.Add(state);
            }
            _candidates.Sort((left, right) => left.coversFocus == right.coversFocus
                ? left.distanceSquared.CompareTo(right.distanceSquared)
                : right.coversFocus.CompareTo(left.coversFocus));
            ShadowedLightCount = Mathf.Min(Mathf.Max(0, maxShadowedLights), _candidates.Count);
            foreach (LightState state in _lights)
            {
                if (state.light.shadows != state.applied) continue;
                bool selected = false;
                for (int index = 0; index < ShadowedLightCount; index++)
                    if (_candidates[index] == state) { selected = true; break; }
                LightShadows next = selected ? state.preferred : LightShadows.None;
                if (next == state.applied) continue;
                state.light.shadows = next;
                state.applied = next;
            }
        }

        private static bool CoversPoint(Light light, Vector3 point)
        {
            Vector3 toPoint = point - light.transform.position;
            float distanceSquared = toPoint.sqrMagnitude;
            if (distanceSquared >= light.range * light.range) return false;
            if (light.type == LightType.Point) return true;
            float outerConeCos = Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad);
            return Vector3.Dot(light.transform.forward, toPoint) >= Mathf.Sqrt(distanceSquared) * outerConeCos;
        }

        private static void Restore(LightState state)
        {
            if (state.light == null || state.light.shadows != state.applied) return;
            state.light.shadows = state.original;
        }

        [ContextMenu("Restore Authored Local Shadows")]
        public void RestoreOriginals()
        {
            foreach (LightState state in _lights) Restore(state);
            _lights.Clear();
            _candidates.Clear();
            _root = null;
            ShadowedLightCount = 0;
            _nextRefresh = Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);
        }

        private void OnDisable()
        {
            if (_subscribedGenerator != null) _subscribedGenerator.Completed -= OnGenerated;
            _subscribedGenerator = null;
            RestoreOriginals();
        }

        private void OnDestroy() => RestoreOriginals();
    }
}
