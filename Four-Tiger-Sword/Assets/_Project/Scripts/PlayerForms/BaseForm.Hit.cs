using UnityEngine;
using System.Collections.Generic;

public abstract partial class BaseForm
{
    private readonly Dictionary<int, HashSet<IDamageable>> _hitEventTargets = new();
    private readonly HashSet<(WeaponActionData step, int hitIndex)> _confirmedFeedbackEvents = new();
    private float _previousHitTime;
    protected Collider[] _overlapBuffer = new Collider[32];

    private void ClearHitTargets()
    {
        _playerController.StopAttackTrails();
        ClearSlashEffects();
        ResetAttackSounds();
        foreach (var targets in _hitEventTargets.Values) targets.Clear();
        _confirmedFeedbackEvents.Clear();
        _previousHitTime = 0f;
    }

    private static bool CrossesHitWindow(float previousTime, float currentTime, float start, float duration)
        => duration > 0f && currentTime > previousTime
            && currentTime >= start && previousTime < start + duration;

    protected virtual void ProcessHit(WeaponActionData step)
    {
        UpdateSwingSounds(step);
        UpdateSlashEffects(step);
        UpdateAttackCamera(step);
        float previousTime = _previousHitTime;
        float currentTime = Mathf.Min(_timer, step.Duration);
        _previousHitTime = currentTime;

        if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            for (int i = 0; i < step.HitEvents.Count; i++)
            {
                HitEvent e = step.HitEvents[i];
                if (e == null || !CrossesHitWindow(previousTime, currentTime, e.StartTime, e.Duration)) continue;

                if (!_hitEventTargets.ContainsKey(i)) _hitEventTargets[i] = new HashSet<IDamageable>();
                ExecuteHit(step, e.Damage > 0 ? e.Damage : step.Damage, e.PoiseDamage > 0 ? e.PoiseDamage : step.PoiseDamage, _hitEventTargets[i], e);
            }
        }
        else
        {
            if (!CrossesHitWindow(previousTime, currentTime, step.HitStartTime, step.HitDuration)) return;

            if (!_hitEventTargets.ContainsKey(-1)) _hitEventTargets[-1] = new HashSet<IDamageable>();
            ExecuteHit(step, step.Damage, step.PoiseDamage, _hitEventTargets[-1]);
        }
    }

    protected virtual void ExecuteHit(WeaponActionData step, float baseDamage, float poiseDamage, HashSet<IDamageable> hitTargets, HitEvent hitEvent = null)
    {
        Vector3 center = _playerController.transform.position
                       + _playerController.transform.rotation * step.HitBoxOffset;
        float knockbackForce = ResolveKnockbackForce(step, hitEvent);

        int hitCount = GetOverlap(step, center);
        for (int i = 0; i < hitCount; i++)
        {
            var hit = _overlapBuffer[i];
            _overlapBuffer[i] = null;
            if (hit == null) continue;
            var damageable = hit.GetComponentInParent<IDamageable>();
            if (damageable == null) continue;
            GameObject target = ((Component)damageable).gameObject;
            var enemyStats = target.GetComponent<EnemyStat>();
            if (enemyStats != null && enemyStats.IsDead) continue;
            if (!hitTargets.Add(damageable)) continue;

            Vector3 knockback = KnockbackMotion.Power(target.transform.position - _playerController.transform.position,
                knockbackForce);
            float critChance = DamageCriticalChance;
            float critMultiplier = DamageCriticalMultiplier;
            var result = DamageManager.Apply(
                new HitInfo(baseDamage * DamageAttackStat, Element, critChance, critMultiplier, trueDamage: DealsTrueDamage, source: _playerController, power: knockback, poiseDamage: poiseDamage, staggerResistLevel: step.StaggerResistLevel, impactSound: ResolveHitSound(step, hitEvent),
                    category: CurrentAttackCategory, itemDamageMultiplier: DamageItemMultiplier,
                    sourceDamageMultiplier: DamageSourceMultiplier, hasSourceDamageSnapshot: true),
                damageable, target);

            if (!result.Applied) continue;
            ConfirmHitFeedback(step, (Component)damageable, result, hitEvent);
            OnDamageDealt(target, result, step, hitEvent);
        }
    }

    /// <summary>Victim feedback runs per applied target; attacker/camera feedback runs once per hit event.</summary>
    protected void ConfirmHitFeedback(WeaponActionData step, Component target, DamageResult result,
        HitEvent hitEvent = null, int hitEventIndex = -1, Vector3 direction = default,
        bool legacyCameraFallbackOnly = false, bool legacyFeedbackDisabled = false)
    {
        if (!result.Applied || target == null || _playerController == null) return;
        if (hitEventIndex < 0 && hitEvent != null && step?.HitEvents != null)
            hitEventIndex = step.HitEvents.IndexOf(hitEvent);
        int stepIndex = _currentAction == ActionType.Attack ? _comboStep
            : _currentAction == ActionType.Ultimate ? _ultimateStep : _skillStep;
        CombatFeedbackTelemetry.Emit("confirmed-impact", _playerController.gameObject, target.gameObject,
            hitEventIndex, $"action={_currentAction};step={stepIndex}", result.HealthDamage + result.ShieldDamage);
        bool first = _confirmedFeedbackEvents.Add((step, hitEventIndex));
        bool stopEnabled = !legacyFeedbackDisabled || step?.Feedback?.HitStop?.EnableV2 == true
            || hitEvent?.Feedback?.HitStop?.EnableV2 == true;
        if (stopEnabled)
            _playerController.StartHitStop(step, ResolveStopHitEvent(step, hitEvent), result.Killed ? null : target, first);
        if (direction.sqrMagnitude < 0.001f) direction = _playerController.transform.forward;
        bool cameraEnabled = !legacyFeedbackDisabled || HasCameraV2(step, hitEvent);
        bool cameraOwned = cameraEnabled && (!legacyCameraFallbackOnly || HasCameraV2(step, hitEvent))
            && NotifyCameraHit(step, target.transform, direction, hitEvent, hitEventIndex);
        if (first && cameraEnabled && !cameraOwned)
            _playerController.PlayAttackCameraShake(step);
    }

    protected virtual float ResolveKnockbackForce(WeaponActionData step, HitEvent hitEvent)
        => hitEvent != null && hitEvent.OverrideKnockbackForce ? hitEvent.KnockbackForce : step.KnockbackForce;

    protected virtual HitEvent ResolveStopHitEvent(WeaponActionData step, HitEvent hitEvent) => hitEvent;

    protected static bool HasCameraV2(WeaponActionData step, HitEvent hitEvent = null)
        => step?.Feedback?.Camera?.V2?.HasOverrides == true
            || step?.CameraProfile?.EnableV2 == true && step.CameraProfile.V2?.HasOverrides == true
            || hitEvent?.Feedback?.CameraV2?.HasOverrides == true;

    private int GetOverlap(WeaponActionData step, Vector3 center)
    {
        // 버퍼가 가득 차면 확장 후 재조회하여 많은 적이 겹쳐도 판정을 누락하지 않습니다.
        while (true)
        {
            int count = QueryOverlap(step, center);
            if (count < _overlapBuffer.Length) return count;
            System.Array.Resize(ref _overlapBuffer, _overlapBuffer.Length * 2);
        }
    }

    protected virtual int QueryOverlap(WeaponActionData step, Vector3 center)
    {
        switch (step.HitBoxShape)
        {
            case HitBoxShape.Sphere:
                return Physics.OverlapSphereNonAlloc(center, step.HitBoxRadius, _overlapBuffer, _enemyLayer);

            case HitBoxShape.Box:
                return Physics.OverlapBoxNonAlloc(center, step.HitBoxSize, _overlapBuffer,
                    _playerController.transform.rotation, _enemyLayer);

            case HitBoxShape.Capsule:
                float offset = (step.HitBoxHeight / 2f) - step.HitBoxRadius;
                return Physics.OverlapCapsuleNonAlloc(
                    center + _playerController.transform.up * offset,
                    center - _playerController.transform.up * offset,
                    step.HitBoxRadius, _overlapBuffer, _enemyLayer);

            default:
                return 0;
        }
    }
}
