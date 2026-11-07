using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Plays the AllIn1 Fade effect without modifying shared materials.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Enemy))]
public sealed class EnemyDeathFade : MonoBehaviour
{
    [Serializable]
    private struct FadeTarget
    {
        public Renderer Renderer;
        public Material[] Materials;
    }

    private sealed class ActiveTarget
    {
        public Renderer Renderer;
        public Material[] OriginalMaterials;
        public MaterialPropertyBlock[] OriginalProperties;
    }

    [SerializeField, Min(0f), Tooltip("사망 후 Fade를 시작하기까지의 시간 (초)")]
    private float _delay = 0.35f;
    [SerializeField, Min(0.01f), Tooltip("완전히 사라지는 데 걸리는 시간 (초)")]
    private float _duration = 1.2f;
    [SerializeField, Tooltip("각 렌더러의 슬롯 순서에 맞춘 AllIn1 사망용 재질")]
    private FadeTarget[] _targets = Array.Empty<FadeTarget>();

    private static readonly int FadeAmount = Shader.PropertyToID("_FadeAmount");
    private readonly List<ActiveTarget> _activeTargets = new();
    private MaterialPropertyBlock _properties;
    private float _elapsed;
    private bool _playing;
    private bool _materialsApplied;
    private Transform _visualRoot;

    public void BindVisualRoot(Transform visualRoot) => _visualRoot = visualRoot;

    public void BeginFade()
    {
        if (_playing || !isActiveAndEnabled) return;
        _elapsed = -Mathf.Max(0f, _delay);
        _playing = true;
    }

    private void Update()
    {
        if (!_playing) return;
        _elapsed += Time.deltaTime;
        if (_elapsed < 0f) return;

        if (!_materialsApplied) ApplyFadeMaterials();
        float progress = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, _duration));
        SetFadeAmount(Mathf.SmoothStep(0f, 1f, progress));
        if (progress >= 1f)
        {
            _playing = false;
            Destroy(gameObject);
        }
    }

    private void ApplyFadeMaterials()
    {
        _materialsApplied = true;
        _properties ??= new MaterialPropertyBlock();
        foreach (var target in _targets)
        {
            if (target.Renderer == null || target.Materials == null) continue;
            if (_visualRoot != null && !target.Renderer.transform.IsChildOf(_visualRoot)) continue;
            var originalMaterials = target.Renderer.sharedMaterials;
            if (target.Materials.Length != originalMaterials.Length) continue;

            var originalProperties = new MaterialPropertyBlock[originalMaterials.Length];
            for (int i = 0; i < originalProperties.Length; i++)
            {
                originalProperties[i] = new MaterialPropertyBlock();
                target.Renderer.GetPropertyBlock(originalProperties[i], i);
            }
            _activeTargets.Add(new ActiveTarget
            {
                Renderer = target.Renderer,
                OriginalMaterials = originalMaterials,
                OriginalProperties = originalProperties
            });
            // These serialized materials also retain the Fade shader variants in builds.
            target.Renderer.sharedMaterials = target.Materials;
        }
    }

    private void SetFadeAmount(float amount)
    {
        foreach (var target in _activeTargets)
        {
            if (target.Renderer == null) continue;
            for (int i = 0; i < target.OriginalMaterials.Length; i++)
            {
                target.Renderer.GetPropertyBlock(_properties, i);
                // Preserve renderer-wide overrides when a slot has no overrides of its own.
                if (_properties.isEmpty) target.Renderer.GetPropertyBlock(_properties);
                _properties.SetFloat(FadeAmount, amount);
                target.Renderer.SetPropertyBlock(_properties, i);
            }
        }
    }

    private void OnDisable()
    {
        foreach (var target in _activeTargets)
        {
            if (target.Renderer == null) continue;
            target.Renderer.sharedMaterials = target.OriginalMaterials;
            for (int i = 0; i < target.OriginalProperties.Length; i++)
            {
                var properties = target.OriginalProperties[i];
                target.Renderer.SetPropertyBlock(properties.isEmpty ? null : properties, i);
            }
        }
        _activeTargets.Clear();
        _playing = false;
        _materialsApplied = false;
    }
}
