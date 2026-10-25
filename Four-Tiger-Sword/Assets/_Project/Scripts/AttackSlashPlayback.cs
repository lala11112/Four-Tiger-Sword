using System;
using UnityEngine;

/// <summary>Plays an authored slash once on the same scaled clock as its attack.</summary>
[DisallowMultipleComponent]
public sealed class AttackSlashPlayback : MonoBehaviour
{
    private ParticleSystem[] _particles;
    private float[] _particleSpeeds;
    private SwordSlashVisual[] _ribbons;
    private Renderer[] _ribbonRenderers;
    private MaterialPropertyBlock _properties;
    private Func<float> _actionElapsed;
    private float _elapsed;
    private float _lifetime;
    private float _playbackSpeed;
    private bool _playing;
    private static readonly int TintId = Shader.PropertyToID("_Tint");

    public void Play(float lifetime, float elapsed, float playbackSpeed, Func<float> actionElapsed)
    {
        _lifetime = Mathf.Max(0.01f, lifetime);
        _elapsed = Mathf.Clamp(elapsed, 0f, _lifetime);
        _playbackSpeed = Mathf.Max(0.01f, playbackSpeed);
        _actionElapsed = actionElapsed;
        _particles = GetComponentsInChildren<ParticleSystem>(true);
        _particleSpeeds = new float[_particles.Length];
        for (int i = 0; i < _particles.Length; i++)
        {
            ParticleSystem particles = _particles[i];
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            _particleSpeeds[i] = Mathf.Max(0f, main.simulationSpeed);
            main.loop = false;
            main.prewarm = false;
            main.playOnAwake = false;
            main.useUnscaledTime = false;
            main.stopAction = ParticleSystemStopAction.None;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            // Simulation is advanced explicitly below; keep authored child speed multipliers separately.
            main.simulationSpeed = 1f;
            if (particles.gameObject.activeInHierarchy)
                particles.Simulate(Mathf.Max(0.00001f, _elapsed) * _playbackSpeed * _particleSpeeds[i],
                    false, true, false);
        }

        // The procedural fallback otherwise fades and destroys itself on an independent wall clock.
        _ribbons = GetComponentsInChildren<SwordSlashVisual>(true);
        _ribbonRenderers = new Renderer[_ribbons.Length];
        if (_ribbons.Length > 0) _properties = new MaterialPropertyBlock();
        for (int i = 0; i < _ribbons.Length; i++)
        {
            _ribbons[i].enabled = false;
            _ribbonRenderers[i] = _ribbons[i].GetComponent<Renderer>();
        }
        _playing = true;
        ApplyRibbonOpacity();
        if (_elapsed >= _lifetime) Finish();
    }

    private void LateUpdate()
    {
        if (!_playing) return;
        // Read the attack's clock after Update, including its speed changes, pauses and heavy dash clamps.
        // Initial seeking already includes this frame, so reading it again cannot advance twice.
        float current = _actionElapsed?.Invoke() ?? _elapsed + Time.deltaTime;
        float delta = Mathf.Max(0f, current - _elapsed);
        if (delta <= 0f) return;
        delta = Mathf.Min(delta, _lifetime - _elapsed);
        _elapsed += delta;
        for (int i = 0; i < _particles.Length; i++)
        {
            ParticleSystem particles = _particles[i];
            if (particles == null || !particles.gameObject.activeInHierarchy) continue;
            particles.Simulate(delta * _playbackSpeed * _particleSpeeds[i], false, false, false);
        }
        ApplyRibbonOpacity();
        if (_elapsed >= _lifetime) Finish();
    }

    private void ApplyRibbonOpacity()
    {
        float opacity = 1f - Mathf.Clamp01(_elapsed / _lifetime);
        opacity *= opacity;
        for (int i = 0; i < _ribbons.Length; i++)
        {
            if (_ribbons[i] == null || _ribbonRenderers[i] == null) continue;
            Color color = _ribbons[i].Tint;
            color.a *= opacity;
            _ribbonRenderers[i].GetPropertyBlock(_properties);
            _properties.SetColor(TintId, color);
            _ribbonRenderers[i].SetPropertyBlock(_properties);
        }
    }

    private void Finish()
    {
        _playing = false;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
