using System;
using System.Collections.Generic;
using FourTigerSword.Rendering;
using UnityEngine;

/// <summary>Owns the player's hidden and hologram rendering during fall recovery.</summary>
[DisallowMultipleComponent]
public sealed class PlayerFallReveal : MonoBehaviour
{
    [Serializable]
    private struct MaterialBinding
    {
        public Material Source;
        public Material Reveal;
    }

    private sealed class Target
    {
        public Renderer Renderer;
        public bool OriginalForceRenderingOff;
        public Material[] OriginalMaterials;
        public Material[] RevealMaterials;
        public MaterialPropertyBlock[] OriginalSlots;
    }

    [SerializeField, Min(.01f)] private float _duration = 1f;
    [SerializeField, ColorUsage(false, true)] private Color _hologramColor = new(.18f, 2.7f, 3.6f, 1f);
    [SerializeField, Range(0f, 1f)] private float _glitchAmount = .25f;
    [SerializeField] private Transform[] _visualRoots = Array.Empty<Transform>();
    [SerializeField] private MaterialBinding[] _materials = Array.Empty<MaterialBinding>();

    private static readonly int FadeAmount = Shader.PropertyToID("_FadeAmount");
    private static readonly int HologramColor = Shader.PropertyToID("_HologramColor");
    private static readonly int HologramBaseAlpha = Shader.PropertyToID("_HologramBaseAlpha");
    private static readonly int HologramAlpha = Shader.PropertyToID("_HologramAlpha");
    private static readonly int GlitchAmount = Shader.PropertyToID("_GlitchAmount");

    private readonly List<Target> _targets = new();
    private MaterialPropertyBlock _properties;
    private PlayerGroundShadow _shadow;
    private bool _ownsPresentation;
    private float _elapsed;

    public bool IsRevealing { get; private set; }

    /// <summary>Validates all visible slots before hiding the player at the return position.</summary>
    public bool Hide(out string error)
    {
        error = null;
        if (_ownsPresentation && !IsRevealing) return true;
        Restore();

        var variants = new Dictionary<Material, Material>();
        foreach (var binding in _materials)
            if (binding.Source != null && binding.Reveal != null)
                variants[binding.Source] = binding.Reveal;

        _shadow = GetComponent<PlayerGroundShadow>();
        var renderers = new HashSet<Renderer>();
        var roots = _visualRoots.Length > 0 ? _visualRoots : new[] { transform };
        foreach (var root in roots)
        {
            if (root == null) continue;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) || !renderer.enabled
                    || !renderer.gameObject.activeInHierarchy
                    || renderer.forceRenderingOff || (_shadow != null && _shadow.OwnsRenderer(renderer))
                    || !renderers.Add(renderer)) continue;
                if (CaptureTarget(renderer, variants, out error)) continue;
                _targets.Clear();
                _shadow = null;
                return false;
            }
        }

        _ownsPresentation = true;
        foreach (var target in _targets) target.Renderer.forceRenderingOff = true;
        if (_shadow != null) _shadow.SetFallRevealSuppressed(true);
        return true;
    }

    private bool CaptureTarget(Renderer renderer, Dictionary<Material, Material> variants, out string error)
    {
        error = null;
        var original = renderer.sharedMaterials;
        if (original.Length == 0) return true;

        var reveal = new Material[original.Length];
        var originalSlots = new MaterialPropertyBlock[original.Length];
        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] == null || !variants.TryGetValue(original[i], out var variant) || !SupportsReveal(variant))
            {
                error = $"Fall reveal material is missing for {renderer.name}, slot {i} ({original[i]?.name ?? "none"}).";
                return false;
            }
            reveal[i] = variant;
            originalSlots[i] = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(originalSlots[i], i);
        }

        _targets.Add(new Target
        {
            Renderer = renderer,
            OriginalForceRenderingOff = renderer.forceRenderingOff,
            OriginalMaterials = original,
            RevealMaterials = reveal,
            OriginalSlots = originalSlots
        });
        return true;
    }

    private static bool SupportsReveal(Material material) => material != null
        && material.IsKeywordEnabled("_FADE_ON") && material.IsKeywordEnabled("_HOLOGRAM_ON")
        && material.IsKeywordEnabled("_GLITCH_ON");

    public void BeginReveal()
    {
        if (!_ownsPresentation || IsRevealing) return;
        _elapsed = 0f;
        IsRevealing = true;
        foreach (var target in _targets)
            if (target.Renderer != null) target.Renderer.sharedMaterials = target.RevealMaterials;

        // Set the fully dissolved frame before releasing the visibility lock.
        SetRevealProgress(0f);
        foreach (var target in _targets)
            if (target.Renderer != null) target.Renderer.forceRenderingOff = target.OriginalForceRenderingOff;
    }

    public bool Advance(float unscaledDeltaTime)
    {
        if (!IsRevealing) return true;
        if (KnockbackMotion.IsFinite(unscaledDeltaTime) && unscaledDeltaTime > 0f) _elapsed += unscaledDeltaTime;
        float duration = KnockbackMotion.IsFinite(_duration) ? Mathf.Max(.01f, _duration) : 1f;
        if (_elapsed >= duration)
        {
            Restore();
            return true;
        }
        SetRevealProgress(_elapsed / duration);
        return false;
    }

    private void SetRevealProgress(float progress)
    {
        _properties ??= new MaterialPropertyBlock();
        float dissolve = 1f - Mathf.SmoothStep(0f, 1f, progress);
        float settle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.65f, 1f, progress));
        foreach (var target in _targets)
        {
            var renderer = target.Renderer;
            if (renderer == null) continue;
            for (int i = 0; i < target.OriginalSlots.Length; i++)
            {
                renderer.GetPropertyBlock(_properties, i);
                if (_properties.isEmpty) renderer.GetPropertyBlock(_properties);
                _properties.SetFloat(FadeAmount, dissolve);
                _properties.SetColor(HologramColor, Color.Lerp(_hologramColor, Color.white, settle));
                _properties.SetFloat(HologramBaseAlpha, Mathf.Lerp(.2f, 1f, settle));
                _properties.SetFloat(HologramAlpha, 1f);
                _properties.SetFloat(GlitchAmount, Mathf.Lerp(_glitchAmount, 0f, settle));
                renderer.SetPropertyBlock(_properties, i);
            }
        }
    }

    public void Restore()
    {
        if (!_ownsPresentation) return;
        foreach (var target in _targets)
        {
            var renderer = target.Renderer;
            if (renderer == null) continue;
            if (IsRevealing)
            {
                renderer.sharedMaterials = target.OriginalMaterials;
                for (int i = 0; i < target.OriginalSlots.Length; i++)
                {
                    var original = target.OriginalSlots[i];
                    renderer.SetPropertyBlock(original.isEmpty ? null : original, i);
                }
            }
            renderer.forceRenderingOff = target.OriginalForceRenderingOff;
        }
        _targets.Clear();
        if (_shadow != null) _shadow.SetFallRevealSuppressed(false);
        _shadow = null;
        IsRevealing = false;
        _ownsPresentation = false;
    }

    private void OnDisable() => Restore();
}
