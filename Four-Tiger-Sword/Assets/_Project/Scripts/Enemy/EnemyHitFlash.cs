using System.Collections.Generic;
using UnityEngine;

/// <summary>Plays AllIn1's Hit effect independently for each damaged enemy.</summary>
[DisallowMultipleComponent]
public sealed class EnemyHitFlash : MonoBehaviour
{
    private struct HitTarget
    {
        public Renderer Renderer;
        public int MaterialIndex;
        public float OriginalBlend;
    }

    [SerializeField, Min(0.01f), Tooltip("피격 시 AllIn1 Hit 효과가 사라지는 시간 (초)")]
    private float _duration = 0.18f;

    [SerializeField, Range(0f, 1f), Tooltip("피격 색상의 최대 혼합 강도. 낮출수록 원래 텍스처와 색상이 더 선명하게 보입니다.")]
    private float _maxBlend = 0.2f;

    private static readonly int HitBlend = Shader.PropertyToID("_HitBlend");
    private readonly List<HitTarget> _targets = new();
    private MaterialPropertyBlock _properties;
    private float _elapsed;
    private bool _playing;
    private bool _initialized;
    private Transform _visualRoot;

    public void RefreshTargets(Transform visualRoot)
    {
        if (_playing) SetBlend(0f);
        _playing = false;
        _elapsed = 0f;
        _targets.Clear();
        _visualRoot = visualRoot;
        _initialized = false;
    }

    public void Play()
    {
        if (!isActiveAndEnabled) return;
        if (!_initialized) CacheTargets();
        if (_targets.Count == 0) return;

        // Restart immediately even when another hit arrives before this flash finishes.
        _elapsed = 0f;
        _playing = true;
        SetBlend(1f);
    }

    private void CacheTargets()
    {
        _initialized = true;
        _properties ??= new MaterialPropertyBlock();
        var owner = GetComponent<Enemy>();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            if (_visualRoot != null && !renderer.transform.IsChildOf(_visualRoot)) continue;
            if (renderer.GetComponentInParent<Enemy>() != owner) continue;

            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                // Hit-enabled material assets retain the shader variant in player builds.
                if (material == null || !material.HasProperty(HitBlend) || !material.IsKeywordEnabled("_HIT_ON")) continue;
                ReadProperties(renderer, i);
                _targets.Add(new HitTarget
                {
                    Renderer = renderer,
                    MaterialIndex = i,
                    OriginalBlend = _properties.HasFloat(HitBlend) ? _properties.GetFloat(HitBlend) : material.GetFloat(HitBlend)
                });
            }
        }
    }

    private void Update()
    {
        if (!_playing) return;
        _elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, _duration));
        SetBlend(1f - Mathf.SmoothStep(0f, 1f, progress));
        if (progress >= 1f) _playing = false;
    }

    private void ReadProperties(Renderer renderer, int materialIndex)
    {
        renderer.GetPropertyBlock(_properties, materialIndex);
        if (_properties.isEmpty) renderer.GetPropertyBlock(_properties);
    }

    private void SetBlend(float strength)
    {
        foreach (var target in _targets)
        {
            if (target.Renderer == null) continue;
            ReadProperties(target.Renderer, target.MaterialIndex);
            // Keep tint, death fade, and other effects that also use property blocks.
            _properties.SetFloat(HitBlend, Mathf.Lerp(target.OriginalBlend, 1f, strength * Mathf.Clamp01(_maxBlend)));
            target.Renderer.SetPropertyBlock(_properties, target.MaterialIndex);
        }
    }

    private void OnDisable()
    {
        if (_playing) SetBlend(0f);
        _playing = false;
        _elapsed = 0f;
        _targets.Clear();
        _initialized = false;
    }
}
