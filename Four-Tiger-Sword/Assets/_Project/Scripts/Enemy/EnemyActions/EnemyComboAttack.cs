using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 다중 타격(콤보) 공격 실행 로직.
/// MonsterComboAttackSO의 hits 리스트를 순서대로 판정하며,
/// 각 타격은 독립적인 히트 목록을 가져 중복 피해를 방지한다.
/// </summary>
public class EnemyComboAttack : EnemyAction
{
    private readonly MonsterComboAttackSO _data;
    private readonly List<HashSet<Collider>> _hitTargets = new List<HashSet<Collider>>();

    public EnemyComboAttack(MonsterComboAttackSO data, Enemy enemy)
        : base(data, enemy)
    {
        _data = data;
    }

    public override void Enter()
    {
        base.Enter();

        _hitTargets.Clear();
        for (int i = 0; i < _data.hits.Count; i++)
            _hitTargets.Add(new HashSet<Collider>());

        // TODO: _enemy.Animator?.SetTrigger(_data.animName);
    }

    public override void Update()
    {
        base.Update();

        for (int i = 0; i < _data.hits.Count; i++)
        {
            EnemyHitEvent e = _data.hits[i];
            if (_timer >= e.hitStartTime && _timer <= e.hitStartTime + e.hitDuration)
                ExecuteHit(i, e);
        }

        if (_timer >= _data.attackDuration)
            IsFinished = true;
    }

    private void ExecuteHit(int index, EnemyHitEvent e)
    {
        Vector3 center = GetHitCenter(e.hitBoxOffset);
        int damage = e.damage > 0 ? e.damage : _data.baseDamage;

        foreach (var col in Physics.OverlapSphere(center, _data.hitBoxRadius, _playerLayer))
        {
            if (_hitTargets[index].Contains(col)) continue;
            if (!col.TryGetComponent<IDamageable>(out var damageable)) continue;

            _hitTargets[index].Add(col);
            DamageManager.Apply(
                new HitInfo(damage, DamageType.Fire, _data.criticalChance,
                            _data.criticalMultiplier, power: GetKnockbackDirection(col) * _data.knockbackForce),
                damageable, col.gameObject);
        }
    }
}
