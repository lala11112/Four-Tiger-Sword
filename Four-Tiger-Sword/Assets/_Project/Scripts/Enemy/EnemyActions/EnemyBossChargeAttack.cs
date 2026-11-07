using System.Collections.Generic;
using UnityEngine;

/// <summary>A readable held windup with a knockback interruption window and committed release.</summary>
public sealed class EnemyBossChargeAttack : EnemyAction
{
    private enum Phase { Preparation, Holding, Release }
    private readonly MonsterBossChargeAttackSO _data;
    private readonly HashSet<IDamageable> _hitTargets = new HashSet<IDamageable>();
    private Phase _phase;
    private float _heldSeconds;
    private bool _movementLocked;
    private bool _swingPlayed;

    public override bool SupportsBossPolicy => true;
    public bool IsCharging => IsActive && !IsFinished && (_phase == Phase.Preparation || _phase == Phase.Holding);
    public override bool AllowsKnockbackDuringBossArmor => IsCharging && _data.interruptOnKnockback;
    protected override float BossInitialStartupDuration => _data.chargePoseTime;
    protected override bool UsesTimedTelegraph => false;

    public EnemyBossChargeAttack(MonsterBossChargeAttackSO data, Enemy enemy) : base(data, enemy) => _data = data;

    public override void Enter()
    {
        _phase = Phase.Preparation;
        _heldSeconds = 0f;
        _movementLocked = false;
        _swingPlayed = false;
        _hitTargets.Clear();
        base.Enter();
        if (!CanContinueAction) return;
        _enemy.LockMovement();
        _movementLocked = true;
        SetBossStage(BossAttackStage.Startup);
        if (!string.IsNullOrWhiteSpace(_data.animName))
            _enemy.Animator?.CrossFade(_data.animName, .03f, 0, 0f);
    }

    public override void Update()
    {
        if (!CanContinueAction) return;
        float previousTime = _timer;
        base.Update();
        if (!CanContinueAction) return;

        switch (_phase)
        {
            case Phase.Preparation:
                TickBossPreparation(previousTime, _timer, 0f, _data.chargePoseTime);
                SetBossStage(BossAttackStage.Startup);
                if (_timer >= _data.chargePoseTime)
                {
                    _phase = Phase.Holding;
                    _timer = _data.chargePoseTime;
                    if (_enemy.Animator != null && !string.IsNullOrWhiteSpace(_data.animName))
                    {
                        _enemy.Animator.Play(_data.animName, 0, _data.chargePoseNormalized);
                        _enemy.Animator.Update(0f);
                        if (!CanContinueAction) return;
                    }
                    _enemy.SetAnimationSpeed(0f);
                    if (_data.chargeSeconds <= 0f) BeginRelease();
                }
                break;

            case Phase.Holding:
                _timer = _data.chargePoseTime;
                _heldSeconds += _enemy.ActionDeltaTime;
                if (_heldSeconds >= _data.chargeSeconds) BeginRelease();
                break;

            case Phase.Release:
                if (_timer >= _data.releaseHitDelay + _data.hitDuration)
                    SetBossStage(BossAttackStage.Recovery);
                else if (_timer >= _data.releaseHitDelay)
                    SetBossStage(BossAttackStage.Active);
                if (CrossesHitWindow(previousTime, _timer, _data.releaseHitDelay, _data.hitDuration))
                {
                    if (!_swingPlayed)
                    {
                        _swingPlayed = true;
                        EndTelegraph();
                        if (!CanContinueAction) return;
                        PlaySwingSound();
                    }
                    BroadcastHit();
                    if (!CanContinueAction) return;
                    ExecuteHit();
                    if (!CanContinueAction) return;
                }
                if (_timer >= _data.releaseHitDelay + _data.hitDuration) EndNormalAttackArmor();
                if (_timer >= _data.releaseHitDelay + _data.hitDuration + _data.recoveryTime)
                    IsFinished = true;
                break;
        }
    }

    private void BeginRelease()
    {
        _phase = Phase.Release;
        _timer = 0f;
        _enemy.SetAnimationSpeed(_data.attackSpeed);
        SetBossStage(BossAttackStage.Startup);
    }

    public override void OnKnockbackReceived()
    {
        if (!IsCharging || !_data.interruptOnKnockback) return;
        _enemy.GetComponent<PoiseHandler>()?.ForceBreak();
        _enemy.PendingGroggy = true;
    }

    public override void Exit()
    {
        if (!IsActive) return;
        base.Exit();
        if (_movementLocked)
        {
            _enemy.UnlockMovement();
            _movementLocked = false;
        }
    }

    private void ExecuteHit()
    {
        Vector3 center = GetHitCenter(_data.hitBoxOffset);
        foreach (Collider col in GetOverlap(center))
        {
            if (!CanContinueAction) return;
            IDamageable target = col.GetComponentInParent<IDamageable>();
            if (target == null || !_hitTargets.Add(target)) continue;
            DamageManager.Apply(new HitInfo(_data.damage * _enemy.EnemyStat.GetStat(EnemyStatType.ATK),
                _enemy.Element, _enemy.EnemyStat.GetStat(EnemyStatType.CriticalChance),
                _enemy.EnemyStat.GetStat(EnemyStatType.CriticalDamage),
                power: GetKnockbackDirection(col) * _data.knockbackForce, staggerResistLevel: _data.staggerLevel,
                isParryable: _data.isParryable, source: this, impactSound: ResolveHitSound()), target, col.gameObject);
            if (!CanContinueAction) return;
        }
    }

    private Collider[] GetOverlap(Vector3 center)
    {
        switch (_data.hitBoxShape)
        {
            case HitBoxShape.Sphere:
                return Physics.OverlapSphere(center, _data.hitBoxRadius, _playerLayer);
            case HitBoxShape.Box:
                return Physics.OverlapBox(center, _data.hitBoxSize * .5f, _enemy.transform.rotation, _playerLayer);
            case HitBoxShape.Capsule:
                float half = Mathf.Max(0f, _data.hitBoxHeight * .5f - _data.hitBoxRadius);
                return Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half,
                    _data.hitBoxRadius, _playerLayer);
            default: return System.Array.Empty<Collider>();
        }
    }
}
