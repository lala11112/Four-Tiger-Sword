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
    private readonly HashSet<IDamageable> _hitTargets = new HashSet<IDamageable>();

    private Phase _phase;
    private Vector3 _dashDirection;
    private bool _movementUnlocked;
    private Collider _collider;
    private bool _colliderWasEnabled;

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
        _collider = _enemy.GetComponent<Collider>();
        _colliderWasEnabled = _collider != null && _collider.enabled;

        if (_nav.enabled && _nav.isOnNavMesh) _nav.ResetPath();
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
                    // telegraphDuration을 startupTime으로 설정하면 베이스 클래스가 이 시점에 자동으로 EndTelegraph() 호출
                    _dashDirection = GetDirectionToTarget();
                    _timer = 0f;
                    _phase = Phase.Dash;
                }
                break;

            case Phase.Dash:
                if (_collider != null) _collider.enabled = false;
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
                    if (_collider != null) _collider.enabled = _colliderWasEnabled;
                }
                break;
        }
    }

    public override void Exit()
    {
        base.Exit(); // 예고가 아직 활성 상태라면 여기서 종료
        if (_collider != null) _collider.enabled = _colliderWasEnabled;
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
        BroadcastHit(); // TakeDamage에서 이 액션을 소스로 식별하기 위해
        Vector3 center = GetHitCenter(_data.hitBoxOffset);

        foreach (var col in Physics.OverlapSphere(center, _data.hitBoxRadius, _playerLayer))
        {
            var damageable = col.GetComponentInParent<IDamageable>();
            if (damageable == null || !_hitTargets.Add(damageable)) continue;
            DamageManager.Apply(
                new HitInfo(_data.damage * _enemy.EnemyStat.GetStat(EnemyStatType.ATK), _enemy.Element, _enemy.EnemyStat.GetStat(EnemyStatType.CriticalChance),
                            _enemy.EnemyStat.GetStat(EnemyStatType.CriticalDamage), power: _dashDirection * _data.knockbackForce, isParryable: _data.isParryable, source: this),
                damageable, col.gameObject);
        }
    }
}
