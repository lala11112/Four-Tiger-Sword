using UnityEngine;
using System.Collections.Generic;

public class EnemyDefaultAttack : EnemyAction
{
    private readonly MonsterDefaultAttackSO _data;
    private readonly Enemy _enemy;
    private readonly LayerMask _playerLayer;
    private readonly HashSet<Collider> _hitTargets = new HashSet<Collider>();
    private float _timer;
    private bool _hasHit;

    public EnemyDefaultAttack(MonsterDefaultAttackSO data, Enemy enemy)
    {
        SkillData = data;
        _data = data;
        _enemy = enemy;
        _playerLayer = LayerMask.GetMask("Player");
    }

    public override void Enter()
    {
        _timer = 0f;
        _hasHit = false;
        IsFinished = false;
        _hitTargets.Clear();
        // TODO: _enemy.Animator?.SetTrigger(_data.animName);
    }

    public override void Update()
    {
        _timer += UnityEngine.Time.deltaTime;

        if (!_hasHit && _timer >= _data.hitStartTime && _timer <= _data.hitStartTime + _data.hitDuration)
        {
            ExecuteHit();
            _hasHit = true;
        }

        if (_timer >= _data.attackDuration)
            IsFinished = true;
    }

    public override void Exit() { }

    private void ExecuteHit()
    {
        Vector3 center = _enemy.transform.position
                       + _enemy.transform.rotation * _data.hitBoxOffset;

        foreach (var col in GetOverlap(center))
        {
            if (_hitTargets.Contains(col)) continue;
            if (!col.TryGetComponent<IDamageable>(out var damageable)) continue;

            _hitTargets.Add(col);
            Vector3 knockbackDir = (col.transform.position - _enemy.transform.position).normalized;
            DamageManager.Apply(
                new HitInfo(_data.damage, DamageType.Normal, _data.criticalChance,
                            _data.criticalMultiplier, power: knockbackDir * _data.knockbackForce),
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
