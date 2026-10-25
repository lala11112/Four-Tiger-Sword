using System.Collections.Generic;
using UnityEngine;

public abstract partial class BaseForm
{
    private readonly HashSet<int> _playedSlashEvents = new();
    private readonly List<GameObject> _activeSlashes = new();

    private void ClearSlashEffects()
    {
        foreach (var effect in _activeSlashes)
        {
            if (effect == null) continue;
            effect.SetActive(false);
            Object.Destroy(effect);
        }
        _activeSlashes.Clear();
        _playedSlashEvents.Clear();
    }

    protected void UpdateSlashEffects(WeaponActionData step)
    {
        if (step == null) return;
        if (_playerController?.Animator != null) _playerController.SetAnimationSpeed(AttackSpeed);
        AttackSlashSettings defaults = step.Feedback?.Slash;
        if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            bool playSingleDefault = false;
            for (int i = 0; i < step.HitEvents.Count; i++)
            {
                HitEvent hit = step.HitEvents[i];
                if (hit == null || hit.Duration <= 0f) continue;
                bool overrides = hit.Feedback?.OverrideSlashVFX == true;
                if (!overrides && defaults != null && defaults.SlashVFXStartTime >= 0f)
                    playSingleDefault = true;
                else
                    TryPlaySlash(step, i, overrides ? hit.Feedback.Slash : defaults, hit.StartTime);
            }
            if (playSingleDefault) TryPlaySlash(step, -1, defaults, step.HitStartTime);
        }
        else if (step.HitDuration > 0f)
            TryPlaySlash(step, -1, defaults, step.HitStartTime);
        else if (defaults != null && defaults.SlashVFXStartTime >= 0f)
            TryPlaySlash(step, -1, defaults, step.HitStartTime);
    }

    private static float SlashStartTime(AttackSlashSettings settings, float hitStart)
        => Mathf.Max(0f, settings.SlashVFXStartTime >= 0f
            ? settings.SlashVFXStartTime : hitStart + settings.TimeOffset);

    private AttackSlashSettings ResolveSlashSettings(WeaponActionData step, int index)
    {
        if (index >= 0 && step.HitEvents != null && index < step.HitEvents.Count)
        {
            HitFeedbackSettings feedback = step.HitEvents[index]?.Feedback;
            if (feedback?.OverrideSlashVFX == true) return feedback.Slash;
        }
        return step.Feedback?.Slash;
    }

    private void TryPlaySlash(WeaponActionData step, int index, AttackSlashSettings settings, float hitStart)
    {
        if (settings?.SlashVFX == null) return;
        float start = SlashStartTime(settings, hitStart);
        if (_timer < start || start > step.Duration || !_playedSlashEvents.Add(index)) return;
        // A skipped frame can cross the whole effect. Consume it without flashing an expired slash.
        if (_timer - start >= Mathf.Max(0.01f, settings.SlashVFXLifetime)) return;
        SpawnSlashEffect(step, index);
    }

    protected virtual void SpawnSlashEffect(WeaponActionData step, int index)
    {
        AttackSlashSettings settings = ResolveSlashSettings(step, index);
        if (settings?.SlashVFX == null) return;
        Transform owner = _playerController.transform;
        Vector3 angles = settings.SlashVFXRotation;
        if (settings.AlternateDirection && index >= 0 && index % 2 != 0) angles.z = -angles.z;
        var effect = Object.Instantiate(settings.SlashVFX,
            owner.position + owner.rotation * (step.HitBoxOffset + settings.SlashVFXOffset),
            owner.rotation * Quaternion.Euler(angles) * settings.SlashVFX.transform.localRotation);
        StageTransientObject.Track(effect);
        effect.transform.localScale *= Mathf.Max(0.01f, settings.SlashVFXScale);
        if (settings.FollowPlayer) effect.transform.SetParent(owner, true);

        float hitStart = index >= 0 ? step.HitEvents[index].StartTime : step.HitStartTime;
        float start = SlashStartTime(settings, hitStart);
        float elapsed = Mathf.Max(0f, _timer - start);
        if (!effect.TryGetComponent<AttackSlashPlayback>(out var playback))
            playback = effect.AddComponent<AttackSlashPlayback>();
        playback.Play(settings.SlashVFXLifetime, elapsed, settings.PlaybackSpeed, () => _timer - start);
        _activeSlashes.RemoveAll(item => item == null);
        _activeSlashes.Add(effect);
    }
}
