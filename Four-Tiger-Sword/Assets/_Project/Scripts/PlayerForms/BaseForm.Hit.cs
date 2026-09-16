using UnityEngine;
using System.Collections.Generic;

public abstract partial class BaseForm
{
    private readonly Dictionary<int, HashSet<IDamageable>> _hitEventTargets = new();
    private float _previousHitTime;
    private Collider[] _overlapBuffer = new Collider[32];

    private void ClearHitTargets()
    {
        foreach (var targets in _hitEventTargets.Values) targets.Clear();
        _previousHitTime = 0f;
    }

    private static bool CrossesHitWindow(float previousTime, float currentTime, float start, float duration)
        => duration > 0f && currentTime > previousTime
            && currentTime >= start && previousTime < start + duration;

    protected virtual void ProcessHit(WeaponActionData step)
    {
        float previousTime = _previousHitTime;
        float currentTime = Mathf.Min(_timer, step.Duration);
        _previousHitTime = currentTime;

        if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            for (int i = 0; i < step.HitEvents.Count; i++)
            {
                HitEvent e = step.HitEvents[i];
                if (e == null || !CrossesHitWindow(previousTime, currentTime, e.StartTime, e.Duration)) continue;

                //사운드 재생
                //PlayStepSound();

                if (!_hitEventTargets.ContainsKey(i)) _hitEventTargets[i] = new HashSet<IDamageable>();
                ExecuteHit(step, e.Damage > 0 ? e.Damage : step.Damage, e.PoiseDamage > 0 ? e.PoiseDamage : step.PoiseDamage, _hitEventTargets[i]);
            }
        }
        else
        {
            if (!CrossesHitWindow(previousTime, currentTime, step.HitStartTime, step.HitDuration)) return;

            if (!_hitEventTargets.ContainsKey(-1)) _hitEventTargets[-1] = new HashSet<IDamageable>();
            ExecuteHit(step, step.Damage, step.PoiseDamage, _hitEventTargets[-1]);
        }
    }

    protected virtual void ExecuteHit(WeaponActionData step, float baseDamage, float poiseDamage, HashSet<IDamageable> hitTargets)
    {
        Vector3 center = _playerController.transform.position
                       + _playerController.transform.rotation * step.HitBoxOffset;

        bool firstHit = true;
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

            float attackMult = 1f + _playerController.StatManager.GetStat(StatType.ST_ATK) * 0.01f;
            Vector3 knockbackDir = (hit.transform.position - _playerController.transform.position).normalized;
            Vector3 knockback = knockbackDir * step.KnockbackForce * attackMult; //아아
            float critChance = _playerController.StatManager.GetStat(StatType.ST_CRT)/100f;
            float critMultiplier = _playerController.StatManager.GetStat(StatType.ST_CRTD)/100f;
            var result = DamageManager.Apply(
                new HitInfo(baseDamage * _playerController.StatManager.GetStat(StatType.ST_ATK), Element, critChance, critMultiplier, trueDamage: DealsTrueDamage, source: _playerController, power: knockback, poiseDamage: poiseDamage, staggerResistLevel: step.StaggerResistLevel),
                damageable, target);

            if (!result.Applied) continue;
            if (firstHit)
            {
                _playerController.StartHitStop();
                firstHit = false;
            }
            _playerController.ImpulseSource?.GenerateImpulse();
            OnHitEnemy(target);
        }
    }

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

    private int QueryOverlap(WeaponActionData step, Vector3 center)
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
