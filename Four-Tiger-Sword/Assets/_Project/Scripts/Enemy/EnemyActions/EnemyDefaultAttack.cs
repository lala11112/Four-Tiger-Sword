using UnityEngine;
using System.Collections.Generic;

public class EnemyDefaultAttack : EnemyAction
{
    private readonly MonsterDefaultAttackSO _data;
    private readonly HashSet<Collider> _hitTargets = new HashSet<Collider>();
    private bool _hasHit;

    public EnemyDefaultAttack(MonsterDefaultAttackSO data, Enemy enemy)
        : base(data, enemy)
    {
        _data = data;
    }

    public override void Enter()
    {
        base.Enter();
        _hasHit = false;
        _hitTargets.Clear();
        _enemy.Animator?.CrossFade(_data.animName, 0.01f);

        // TODO: _enemy.Animator?.SetTrigger(_data.animName);
    }

    public override void Update()
    {
        base.Update();

        if (!_hasHit && _timer >= _data.hitStartTime && _timer <= _data.hitStartTime + _data.hitDuration)
        {
            // 실제 타격이 시작되는 순간 예고 종료 → 플레이어가 반응할 수 없는 구간임을 명확히
            EndTelegraph();
            BroadcastHit(); // TakeDamage에서 이 액션을 소스로 식별하기 위해
            ExecuteHit();
            _hasHit = true;
        }

        if (_timer >= _data.attackDuration)
            IsFinished = true;
    }

    private void ExecuteHit()
    {
        Vector3 center = GetHitCenter(_data.hitBoxOffset);

        foreach (var col in GetOverlap(center))
        {
            if (_hitTargets.Contains(col)) continue;
            if (!col.TryGetComponent<IDamageable>(out var damageable)) continue;

            _hitTargets.Add(col);
            DamageManager.Apply(
                new HitInfo(_data.damage * _enemy.EnemyStat.GetStat(EnemyStatType.ATK), _enemy.Element, _enemy.EnemyStat.GetStat(EnemyStatType.CriticalChance),
                            _enemy.EnemyStat.GetStat(EnemyStatType.CriticalDamage), power: GetKnockbackDirection(col) * _data.knockbackForce),
                damageable, col.gameObject);
        }
    }

    private Collider[] GetOverlap(Vector3 center)
    {
        switch (_data.hitBoxShape)
        {
            case HitBoxShape.Sphere:
                return Physics.OverlapSphere(center, _data.hitBoxRadius, _playerLayer);
            case HitBoxShape.Box:
                return Physics.OverlapBox(center, _data.hitBoxSize * 0.5f,
                           _enemy.transform.rotation, _playerLayer);
            case HitBoxShape.Capsule:
                float offset = _data.hitBoxHeight * 0.5f - _data.hitBoxRadius;
                return Physics.OverlapCapsule(
                    center + _enemy.transform.up * offset,
                    center - _enemy.transform.up * offset,
                    _data.hitBoxRadius, _playerLayer);
            default:
                return System.Array.Empty<Collider>();
        }
    }
}
