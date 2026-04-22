using UnityEngine;
using System.Collections.Generic;

public class EnemyAttackState : IPlayerState
{
    private Enemy _enemy;
    private MonsterSkillData _skill;
    private float _timer;
    private LayerMask _playerLayer;

    // 히트 이벤트별 이미 타격한 콜라이더 집합 (한 공격에 동일 대상 중복 타격 방지)
    private readonly Dictionary<int, HashSet<Collider>> _hitTargets = new();

    public EnemyAttackState(Enemy enemy)
    {
        _enemy = enemy;
        _playerLayer = LayerMask.GetMask("Player");
    }

    public void Enter()
    {
        _skill = _enemy.CurrentSkillData;
        _timer = 0f;
        _hitTargets.Clear();
    }

    public void Update()
    {
        if (_skill == null) return;

        _timer += Time.deltaTime;

        ProcessHit();

        if (_timer >= _skill.Duration)
        {
            _enemy.CanUseSkill = false;
            _enemy.StartAttackCooldown();
        }
    }

    public void Exit()
    {

    }

    // ── 히트 처리 ────────────────────────────────────────────────────────────

    private void ProcessHit()
    {
        if (_skill.HitEvents != null && _skill.HitEvents.Count > 0)
        {
            for (int i = 0; i < _skill.HitEvents.Count; i++)
            {
                HitEvent e = _skill.HitEvents[i];
                if (_timer < e.StartTime || _timer > e.StartTime + e.Duration) continue;

                if (!_hitTargets.ContainsKey(i)) _hitTargets[i] = new HashSet<Collider>();
                int damage = e.Damage > 0 ? e.Damage : _skill.Damage;
                ExecuteHit(damage, _hitTargets[i]);
            }
        }
        else
        {
            if (_timer < _skill.HitStartTime || _timer > _skill.HitStartTime + _skill.HitDuration) return;

            if (!_hitTargets.ContainsKey(-1)) _hitTargets[-1] = new HashSet<Collider>();
            ExecuteHit(_skill.Damage, _hitTargets[-1]);
        }
    }

    private void ExecuteHit(int damage, HashSet<Collider> alreadyHit)
    {
        Vector3 center = _enemy.transform.position
                       + _enemy.transform.rotation * _skill.HitBoxOffset;


        foreach (var col in GetOverlap(center))
        {
            if (alreadyHit.Contains(col)) continue;
            if (!col.TryGetComponent<IDamageable>(out var damageable)) continue;

            alreadyHit.Add(col);

            Vector3 knockbackDir = (col.transform.position - _enemy.transform.position).normalized;
            Vector3 knockback = knockbackDir * _skill.ForwardThrust;

            DamageManager.Apply(
                new HitInfo(damage, DamageType.Normal, _skill.CriticalChance, _skill.CriticalMultiplier, power: knockback),
                damageable, col.gameObject);
        }
    }

    private Collider[] GetOverlap(Vector3 center)
    {
        switch (_skill.HitBoxShape)
        {
            case HitBoxShape.Sphere:
                return Physics.OverlapSphere(center, _skill.HitBoxRadius, _playerLayer);

            case HitBoxShape.Box:
                return Physics.OverlapBox(center, _skill.HitBoxSize * 0.5f,
                    _enemy.transform.rotation, _playerLayer);

            case HitBoxShape.Capsule:
                float offset = (_skill.HitBoxHeight / 2f) - _skill.HitBoxRadius;
                return Physics.OverlapCapsule(
                    center + _enemy.transform.up * offset,
                    center - _enemy.transform.up * offset,
                    _skill.HitBoxRadius, _playerLayer);

            default:
                return System.Array.Empty<Collider>();
        }
    }
}