using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// 대쉬 공격 실행 로직.
/// Startup(선딜) → Dash(돌진 + 히트 판정) → Recovery(후딜) 순으로 진행된다.
/// </summary>
public class EnemyDashAttack : EnemyAction
{
    private enum Phase { Startup, Dash, Recovery }

    private readonly MonsterDashAttackSO _data;
    private readonly NavMeshAgent _nav;
    private readonly HashSet<Collider> _hitTargets = new HashSet<Collider>();

    private Phase _phase;
    private Vector3 _dashDirection;
    private bool _movementUnlocked;

    public EnemyDashAttack(MonsterDashAttackSO data, Enemy enemy)
        : base(data, enemy)
    {
        _data = data;
        _nav = enemy.GetComponent<NavMeshAgent>();
    }

    public override void Enter()
    {
        base.Enter();
        _phase = Phase.Startup;
        _hitTargets.Clear();
        _movementUnlocked = false;

        _nav.ResetPath();
        _enemy.LockMovement();
        // TODO: _enemy.Animator?.SetTrigger(_data.animName);
    }

    public override void Update()
    {
        base.Update();

        switch (_phase)
        {
            case Phase.Startup:
                FaceTarget();
                if (_timer >= _data.startupTime)
                {
                    // 대쉬 방향을 이 시점에 확정 — 이후 플레이어가 움직여도 방향 고정
                    _dashDirection = GetDirectionToTarget();
                    _timer = 0f;
                    _phase = Phase.Dash;
                }
                break;

            case Phase.Dash:
                _enemy.GetComponent<Collider>().enabled = false; //???
                PerformDash();
                CheckHit();
                if (_timer >= _data.dashDuration)
                {
                    _enemy.UnlockMovement();
                    _movementUnlocked = true;
                    _timer = 0f;
                    _phase = Phase.Recovery;
                }
                break;

            case Phase.Recovery:
                if (_timer >= _data.recoveryTime)
                {
                    IsFinished = true;
                    _enemy.GetComponent<Collider>().enabled = true;
                }
                break;
        }
    }

    public override void Exit()
    {
        if (!_movementUnlocked)
        {
            _enemy.UnlockMovement();
            _movementUnlocked = true;
        }
    }

    private void PerformDash()
    {
        // 타이머가 attackSpeed 배율로 빠르게 진행되므로, 실제 이동 거리를 유지하려면
        // 프레임당 이동량도 attackSpeed만큼 곱해야 한다.
        Vector3 delta = _dashDirection * _data.dashSpeed * _data.attackSpeed * Time.deltaTime;
        if (_nav.isOnNavMesh)
            _nav.Move(delta);
        else
            _enemy.transform.position += delta;
    }

    private void CheckHit()
    {
        Vector3 center = GetHitCenter(_data.hitBoxOffset);

        foreach (var col in Physics.OverlapSphere(center, _data.hitBoxRadius, _playerLayer))
        {
            if (_hitTargets.Contains(col)) continue;
            if (!col.TryGetComponent<IDamageable>(out var damageable)) continue;

            _hitTargets.Add(col);
            DamageManager.Apply(
                new HitInfo(_data.damage, ElementType.ELEMENT_NONE, _data.criticalChance,
                            _data.criticalMultiplier, power: _dashDirection * _data.knockbackForce),
                damageable, col.gameObject);
        }
    }
}
