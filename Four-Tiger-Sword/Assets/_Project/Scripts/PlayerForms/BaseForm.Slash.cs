using System.Collections.Generic;
using UnityEngine;

public abstract partial class BaseForm
{
    private readonly HashSet<int> _playedSlashEvents = new();
    private readonly List<GameObject> _activeSlashes = new();

    private void ClearSlashEffects()
    {
        foreach (var effect in _activeSlashes)
            if (effect != null) Object.Destroy(effect);
        _activeSlashes.Clear();
        _playedSlashEvents.Clear();
    }

    protected void UpdateSlashEffects(WeaponActionData step)
    {
        if (step == null || step.SlashVFX == null) return;
        if (step.SlashVFXStartTime >= 0f)
            TryPlaySlash(step, -1, step.SlashVFXStartTime);
        else if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            for (int i = 0; i < step.HitEvents.Count; i++)
                if (step.HitEvents[i] != null && step.HitEvents[i].Duration > 0f)
                    TryPlaySlash(step, i, step.HitEvents[i].StartTime);
        }
        else if (step.HitDuration > 0f)
            TryPlaySlash(step, -1, step.HitStartTime);
    }

    private void TryPlaySlash(WeaponActionData step, int index, float start)
    {
        if (_timer < Mathf.Max(0f, start) || start > step.Duration || !_playedSlashEvents.Add(index)) return;
        SpawnSlashEffect(step, index);
    }

    protected virtual void SpawnSlashEffect(WeaponActionData step, int index)
    {
        Transform owner = _playerController.transform;
        Vector3 angles = step.SlashVFXRotation;
        if (index >= 0 && index % 2 != 0) angles.z = -angles.z;
        var effect = Object.Instantiate(step.SlashVFX,
            owner.position + owner.rotation * (step.HitBoxOffset + step.SlashVFXOffset),
            owner.rotation * Quaternion.Euler(angles));
        effect.transform.localScale *= Mathf.Max(0.01f, step.SlashVFXScale);
        float speed = Mathf.Max(0.01f, AttackSpeed);
        float lifetime = Mathf.Max(0.01f, step.SlashVFXLifetime) / speed;
        if (effect.TryGetComponent<SwordSlashVisual>(out var slash)) slash.Play(lifetime);
        foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>())
        {
            var main = particles.main;
            main.simulationSpeed *= speed;
        }
        Object.Destroy(effect, lifetime);
        _activeSlashes.RemoveAll(item => item == null);
        _activeSlashes.Add(effect);
    }
}
