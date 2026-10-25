using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;

namespace FourTigerSword.Rendering
{
    /// <summary>
    /// Applies an explicit visual style after generation and encounter-door creation.
    /// Work is event driven, scoped to one root, and reversible without material instances.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Four Tiger Sword/Rendering/Lab Visual Style Controller")]
    public sealed class LabVisualStyleController : MonoBehaviour
    {
        public BaekhoDungeonGenerator generator;
        public LabVisualStyle style;

        [Tooltip("Optional static-scene or edit-preview scope. Used only while the generator has no CurrentRoot.")]
        public Transform explicitRoot;

        private sealed class MaterialChange
        {
            public Material original;
            public Material applied;
        }

        private sealed class LightChange
        {
            public float original;
            public float applied;
        }

        private readonly Dictionary<Renderer, Dictionary<int, MaterialChange>> _materials = new();
        private readonly Dictionary<Light, LightChange> _lights = new();
        private readonly Dictionary<Material, Material> _replacements = new();
        private readonly List<Renderer> _rendererRemoval = new();
        private readonly List<Light> _lightRemoval = new();
        private BaekhoDungeonGenerator _subscribedGenerator;
        private DungeonEncounterDirector _subscribedDirector;
        private Transform _appliedRoot;

        public int ChangedRendererCount => _materials.Count;
        public int ChangedLightCount => _lights.Count;

        private void Reset()
        {
            generator = GetComponent<BaekhoDungeonGenerator>();
        }

        private void OnEnable()
        {
            // Editing a scene does not silently start a material preview.
            if (!Application.isPlaying) return;
            Subscribe();
            ApplyCurrent();
        }

        private void Subscribe()
        {
            if (generator == null) generator = GetComponent<BaekhoDungeonGenerator>();
            _subscribedGenerator = generator;
            if (_subscribedGenerator == null) return;
            _subscribedGenerator.Completed += OnGenerated;
            _subscribedDirector = _subscribedGenerator.GetComponent<DungeonEncounterDirector>();
            if (_subscribedDirector != null) _subscribedDirector.Ready += OnEncountersReady;
        }

        private void OnGenerated(BaekhoDungeonResult result)
        {
            if (result != null && result.success && result.root != null &&
                _subscribedGenerator != null && result.root == _subscribedGenerator.CurrentRoot)
                ApplyCurrent();
        }

        private void OnEncountersReady(DungeonEncounterDirector director)
        {
            if (director == _subscribedDirector) ApplyCurrent();
        }

        [ContextMenu("Apply Current Lab Style (Scoped Preview)")]
        public void ApplyCurrent()
        {
            Transform root = generator != null && generator.CurrentRoot != null
                ? generator.CurrentRoot.transform : explicitRoot;
            if (style == null || root == null)
            {
                RestoreOriginals();
                return;
            }

            if (_appliedRoot != root) RestoreOriginals();
            _appliedRoot = root;
            PruneOutsideRoot(root);
            BuildReplacementMap();

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                ApplyMaterials(renderer);

            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.type != LightType.Point && light.type != LightType.Spot) continue;
                if (!_lights.TryGetValue(light, out LightChange change))
                {
                    change = new LightChange { original = light.intensity, applied = light.intensity };
                    _lights.Add(light, change);
                }
                // A gameplay effect that changed intensity since our last pass retains control.
                if (!Mathf.Approximately(light.intensity, change.applied)) continue;
                change.applied = style.EvaluateLocalLightIntensity(change.original);
                light.intensity = change.applied;
            }
        }

        private void BuildReplacementMap()
        {
            _replacements.Clear();
            if (style.materialReplacements == null) return;
            foreach (LabVisualStyle.MaterialReplacement entry in style.materialReplacements)
            {
                if (entry == null || entry.source == null || entry.replacement == null ||
                    entry.source == entry.replacement || _replacements.ContainsKey(entry.source)) continue;
                _replacements.Add(entry.source, entry.replacement);
            }
        }

        private void ApplyMaterials(Renderer renderer)
        {
            Material[] materials = renderer.sharedMaterials;
            _materials.TryGetValue(renderer, out Dictionary<int, MaterialChange> slots);
            bool changed = false;
            for (int index = 0; index < materials.Length; index++)
            {
                Material current = materials[index];
                if (slots != null && slots.TryGetValue(index, out MaterialChange tracked))
                {
                    if (current == tracked.applied)
                    {
                        if (tracked.original != null && _replacements.TryGetValue(tracked.original, out Material updated))
                        {
                            if (current != updated) { materials[index] = updated; changed = true; }
                            tracked.applied = updated;
                        }
                        else
                        {
                            materials[index] = tracked.original;
                            changed = true;
                            slots.Remove(index);
                        }
                        continue;
                    }
                    // Do not restore over a death-fade or other external material substitution.
                    slots.Remove(index);
                }
                if (current == null || !_replacements.TryGetValue(current, out Material replacement)) continue;
                if (slots == null)
                {
                    slots = new Dictionary<int, MaterialChange>();
                    _materials.Add(renderer, slots);
                }
                slots[index] = new MaterialChange { original = current, applied = replacement };
                materials[index] = replacement;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
            if (slots != null && slots.Count == 0) _materials.Remove(renderer);
        }

        private void PruneOutsideRoot(Transform root)
        {
            _rendererRemoval.Clear();
            foreach (var pair in _materials)
            {
                if (pair.Key != null && (pair.Key.transform == root || pair.Key.transform.IsChildOf(root))) continue;
                RestoreRenderer(pair.Key, pair.Value);
                _rendererRemoval.Add(pair.Key);
            }
            foreach (Renderer renderer in _rendererRemoval) _materials.Remove(renderer);

            _lightRemoval.Clear();
            foreach (var pair in _lights)
            {
                if (pair.Key != null && (pair.Key.transform == root || pair.Key.transform.IsChildOf(root)) &&
                    (pair.Key.type == LightType.Point || pair.Key.type == LightType.Spot)) continue;
                RestoreLight(pair.Key, pair.Value);
                _lightRemoval.Add(pair.Key);
            }
            foreach (Light light in _lightRemoval) _lights.Remove(light);
        }

        [ContextMenu("Restore Original Lab Materials And Lights")]
        public void RestoreOriginals()
        {
            foreach (var pair in _materials) RestoreRenderer(pair.Key, pair.Value);
            foreach (var pair in _lights) RestoreLight(pair.Key, pair.Value);
            _materials.Clear();
            _lights.Clear();
            _replacements.Clear();
            _rendererRemoval.Clear();
            _lightRemoval.Clear();
            _appliedRoot = null;
        }

        private static void RestoreRenderer(Renderer renderer, Dictionary<int, MaterialChange> slots)
        {
            if (renderer == null) return;
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            foreach (var pair in slots)
            {
                if (pair.Key >= materials.Length || materials[pair.Key] != pair.Value.applied) continue;
                materials[pair.Key] = pair.Value.original;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }

        private static void RestoreLight(Light light, LightChange change)
        {
            if (light != null && Mathf.Approximately(light.intensity, change.applied))
                light.intensity = change.original;
        }

        private void OnDisable()
        {
            if (_subscribedGenerator != null) _subscribedGenerator.Completed -= OnGenerated;
            if (_subscribedDirector != null) _subscribedDirector.Ready -= OnEncountersReady;
            _subscribedGenerator = null;
            _subscribedDirector = null;
            RestoreOriginals();
        }

        private void OnDestroy()
        {
            RestoreOriginals();
        }
    }
}
