using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Plays the AllIn1 spawn and death effects without modifying shared materials.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Enemy))]
public sealed class EnemyDeathFade : MonoBehaviour
{
    private enum FadeMode { None, Spawn, Death }

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
    [SerializeField, Min(0.01f), Tooltip("몬스터가 생성되는 데 걸리는 시간 (초)")]
    private float _spawnDuration = 1f;
    [SerializeField, Tooltip("각 렌더러의 슬롯 순서에 맞춘 AllIn1 연출용 재질")]
    private FadeTarget[] _targets = Array.Empty<FadeTarget>();

    private static readonly int FadeAmount = Shader.PropertyToID("_FadeAmount");
    private static readonly int HologramColor = Shader.PropertyToID("_HologramColor");
    private static readonly int HologramBaseAlpha = Shader.PropertyToID("_HologramBaseAlpha");
    private static readonly int HologramAlpha = Shader.PropertyToID("_HologramAlpha");
    private static readonly int GlitchAmount = Shader.PropertyToID("_GlitchAmount");
    private static readonly Color SpawnRed = new(3.8f, .22f, .16f, 1f);
    private readonly List<ActiveTarget> _activeTargets = new();
    private MaterialPropertyBlock _properties;
    private float _elapsed;
    private FadeMode _mode;
    private bool _materialsApplied;
    private Transform _visualRoot;
    private Enemy _spawnOwner;
    private Animator _spawnRootMotionAnimator;

    public bool IsRevealing => _mode == FadeMode.Spawn;

    public void BindVisualRoot(Transform visualRoot) => _visualRoot = visualRoot;

    internal void BeginSpawnReveal(Enemy owner)
    {
        if (_mode != FadeMode.None || !isActiveAndEnabled) return;

        _mode = FadeMode.Spawn;
        ApplyFadeMaterials();
        if (_activeTargets.Count == 0)
        {
            _mode = FadeMode.None;
            _materialsApplied = false;
            return;
        }

        _elapsed = 0f;
        _spawnOwner = owner;
        owner.LockMovement();
        var animator = owner.Animator != null ? owner.Animator : owner.GetComponentInChildren<Animator>();
        if (animator != null && animator.applyRootMotion)
        {
            _spawnRootMotionAnimator = animator;
            _spawnRootMotionAnimator.applyRootMotion = false;
        }
        SetFadeAmount(1f);
    }

    internal void EndSpawnReveal()
    {
        if (!IsRevealing) return;
        RestoreMaterials();
        if (_spawnRootMotionAnimator != null)
            _spawnRootMotionAnimator.applyRootMotion = true;
        _spawnRootMotionAnimator = null;
        _mode = FadeMode.None;
        if (_spawnOwner != null) _spawnOwner.UnlockMovement();
        _spawnOwner = null;
    }

    public void BeginFade()
    {
        if (_mode == FadeMode.Death || !isActiveAndEnabled) return;
        EndSpawnReveal();
        _elapsed = -Mathf.Max(0f, _delay);
        _mode = FadeMode.Death;
    }

    private void Update() => Advance(Time.deltaTime);

    private void Advance(float deltaTime)
    {
        if (_mode == FadeMode.None) return;
        _elapsed += deltaTime;
        if (IsRevealing)
        {
            float revealProgress = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, _spawnDuration));
            if (revealProgress >= 1f)
            {
                EndSpawnReveal();
                return;
            }
            float settle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.65f, 1f, revealProgress));
            SetFadeAmount(1f - Mathf.SmoothStep(0f, 1f, revealProgress), settle);
            return;
        }
        if (_elapsed < 0f) return;

        if (!_materialsApplied) ApplyFadeMaterials();
        float progress = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, _duration));
        SetFadeAmount(Mathf.SmoothStep(0f, 1f, progress));
        if (progress >= 1f)
        {
            _mode = FadeMode.None;
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
            if (IsRevealing && (!target.Renderer.enabled || !target.Renderer.gameObject.activeInHierarchy
                || !SupportsSpawnReveal(target.Materials))) continue;

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

    private static bool SupportsSpawnReveal(Material[] materials)
    {
        if (materials.Length == 0) return false;
        foreach (var material in materials)
            if (material == null || !material.IsKeywordEnabled("_FADE_ON")
                || !material.IsKeywordEnabled("_GLITCH_ON") || !material.IsKeywordEnabled("_HOLOGRAM_ON"))
                return false;
        return true;
    }

    private void SetFadeAmount(float amount, float settle = 0f)
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
                if (IsRevealing)
                {
                    _properties.SetColor(HologramColor, Color.Lerp(SpawnRed, Color.white, settle));
                    _properties.SetFloat(HologramBaseAlpha, Mathf.Lerp(.2f, 1f, settle));
                    _properties.SetFloat(HologramAlpha, 1f);
                    _properties.SetFloat(GlitchAmount, Mathf.Lerp(.25f, 0f, settle));
                }
                target.Renderer.SetPropertyBlock(_properties, i);
            }
        }
    }

    private void RestoreMaterials()
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
        _materialsApplied = false;
    }

    private void OnDisable()
    {
        if (IsRevealing)
        {
            EndSpawnReveal();
            return;
        }
        RestoreMaterials();
        _mode = FadeMode.None;
    }
}
