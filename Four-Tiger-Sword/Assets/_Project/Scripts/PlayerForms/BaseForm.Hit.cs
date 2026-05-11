using UnityEngine;
using System.Collections.Generic;

public abstract partial class BaseForm
{
    private readonly Dictionary<int, HashSet<Collider>> _hitEventTargets = new();

    private void ClearHitTargets() => _hitEventTargets.Clear();

    protected void ProcessHit(WeaponActionData step)
    {
        if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            for (int i = 0; i < step.HitEvents.Count; i++)
            {
                HitEvent e = step.HitEvents[i];
                if (_timer < e.StartTime || _timer > e.StartTime + e.Duration) continue;

                if (!_hitEventTargets.ContainsKey(i)) _hitEventTargets[i] = new HashSet<Collider>();
                ExecuteHit(step, e.Damage > 0 ? e.Damage : step.Damage, e.PoiseDamage > 0 ? e.PoiseDamage : step.PoiseDamage, _hitEventTargets[i]);
            }
        }
        else
        {
            if (_timer < step.HitStartTime || _timer > step.HitStartTime + step.HitDuration) return;

            if (!_hitEventTargets.ContainsKey(-1)) _hitEventTargets[-1] = new HashSet<Collider>();
            ExecuteHit(step, _playerController.StatManager.GetStat(StatType.ST_ATK), step.PoiseDamage, _hitEventTargets[-1]);
        }
    }

    private void ExecuteHit(WeaponActionData step, float baseDamage, float poiseDamage, HashSet<Collider> hitTargets)
    {
        Vector3 center = _playerController.transform.position
                       + _playerController.transform.rotation * step.HitBoxOffset;

        bool firstHit = true;
        foreach (var hit in GetOverlap(step, center))
        {
            if (hitTargets.Contains(hit)) continue;
            if (!hit.TryGetComponent<IDamageable>(out var damageable)) continue;

            if (firstHit)
            {
                _playerController.StartHitStop();
                firstHit = false;
            }

            _playerController.ImpulseSource.GenerateImpulse();
            hitTargets.Add(hit);
            float attackMult = 1f + _playerController.StatManager.GetStat(StatType.ST_ATK) * 0.01f;
            Vector3 knockbackDir = (hit.transform.position - _playerController.transform.position).normalized;
            Vector3 knockback = knockbackDir * step.KnockbackForce * attackMult;
            DamageManager.Apply(
                new HitInfo((int)(baseDamage * step.Damage), Element, step.CriticalChance, step.CriticalMultiplier, power: knockback, poiseDamage: poiseDamage),
                damageable, hit.gameObject);

            OnHitEnemy(hit.gameObject);
        }
    }

    private Collider[] GetOverlap(WeaponActionData step, Vector3 center)
    {
        switch (step.HitBoxShape)
        {
            case HitBoxShape.Sphere:
                return Physics.OverlapSphere(center, step.HitBoxRadius, _enemyLayer);

            case HitBoxShape.Box:
                return Physics.OverlapBox(center, step.HitBoxSize,
                    _playerController.transform.rotation, _enemyLayer);

            case HitBoxShape.Capsule:
                float offset = (step.HitBoxHeight / 2f) - step.HitBoxRadius;
                return Physics.OverlapCapsule(
                    center + _playerController.transform.up * offset,
                    center - _playerController.transform.up * offset,
                    step.HitBoxRadius, _enemyLayer);

            default:
                return System.Array.Empty<Collider>();
        }
    }
}
