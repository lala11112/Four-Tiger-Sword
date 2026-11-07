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
    private bool _movementUnlocked = true;
    private Collider _collider;
    private bool _colliderWasEnabled;
    public override bool SupportsBossPolicy => true;
    protected override float BossInitialStartupDuration => _data.startupTime;

    public EnemyDashAttack(MonsterDashAttackSO data, Enemy enemy)
        : base(data, enemy)
    {
        _data = data;
        _nav = enemy.GetComponent<NavMeshAgent>();
    }

    public override void Enter()
    {
        _movementUnlocked = true;
        _collider = _enemy.GetComponent<Collider>();
        _colliderWasEnabled = _collider != null && _collider.enabled;
        base.Enter();
        if (!CanContinueAction) return;
        _phase = Phase.Startup;
        _hitTargets.Clear();
        _movementUnlocked = false;

        if (_nav.enabled && _nav.isOnNavMesh) _nav.ResetPath();
        _enemy.LockMovement();
        if (!string.IsNullOrWhiteSpace(_data.startupAnimName))
            _enemy.Animator?.CrossFade(_data.startupAnimName, 0.01f, 0, 0f);
        else if (!string.IsNullOrWhiteSpace(_data.animName))
            _enemy.Animator?.CrossFade(_data.animName, 0.01f);
    }

    public override void Update()
    {
        if (!CanContinueAction) return;
        float previousTime = _timer;
        base.Update();
        if (!CanContinueAction) return;

        switch (_phase)
        {
            case Phase.Startup:
                if (HasBossPolicy) TickBossPreparation(previousTime, _timer, 0f, _data.startupTime);
                else FaceTarget();
                if (_timer >= _data.startupTime)
                {
                    // telegraphDuration을 startupTime으로 설정하면 베이스 클래스가 이 시점에 자동으로 EndTelegraph() 호출
                    _dashDirection = HasBossPolicy ? BossAimDirection : GetDirectionToTarget();
                    _timer = 0f;
                    _phase = Phase.Dash;
                    SetBossStage(BossAttackStage.Active);
                    if (!string.IsNullOrWhiteSpace(_data.startupAnimName) && !string.IsNullOrWhiteSpace(_data.animName))
                        _enemy.Animator?.CrossFade(_data.animName, .03f, 0, 0f);
                    PlaySwingSound();
                }
                break;

            case Phase.Dash:
                // Ordinary attacks and tactical bosses retain their health and poise hit receivers.
                if (_collider != null && _enemy is Boss boss && boss.ActiveCombatProfile == null)
                    _collider.enabled = false;
                PerformDash();
                CheckHit();
                if (!CanContinueAction) return;
                if (_timer >= _data.dashDuration)
                {
                    EndNormalAttackArmor();
                    if (_collider != null) _collider.enabled = _colliderWasEnabled;
                    _enemy.UnlockMovement();
                    _movementUnlocked = true;
                    _timer = 0f;
                    _phase = Phase.Recovery;
                    SetBossStage(BossAttackStage.Recovery);
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
        if (!IsActive) return;
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
        Vector3 delta = _dashDirection * _data.dashSpeed * _data.attackSpeed * _enemy.ActionDeltaTime;
        if (_nav.enabled && _nav.isOnNavMesh)
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
            if (!CanContinueAction) return;
            var damageable = col.GetComponentInParent<IDamageable>();
            if (damageable == null || !_hitTargets.Add(damageable)) continue;
            DamageManager.Apply(
                new HitInfo(_data.damage * _enemy.EnemyStat.GetStat(EnemyStatType.ATK), _enemy.Element, _enemy.EnemyStat.GetStat(EnemyStatType.CriticalChance),
                            _enemy.EnemyStat.GetStat(EnemyStatType.CriticalDamage), power: _dashDirection * _data.knockbackForce, staggerResistLevel: _data.staggerLevel, isParryable: _data.isParryable, source: this, impactSound: ResolveHitSound()),
                damageable, col.gameObject);
            if (!CanContinueAction) return;
        }
    }
}
