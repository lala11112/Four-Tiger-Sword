using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>One fixed-center cylindrical burst. Floor visuals read this action; damage remains authoritative here.</summary>
public sealed class BossPhaseBurstAction : EnemyAction
{
    private readonly BossPhaseBurstSkillSO _data;
    private readonly HashSet<IDamageable> _hitTargets = new HashSet<IDamageable>();
    private bool _movementLocked, _heldPose, _released, _burstPlayed;

    public Vector3 Center { get; private set; }
    public float Radius => _data.radius;
    public float ChargeProgress => Mathf.Clamp01(_timer / _data.chargeDuration);
    public bool IsInvulnerable => IsActive && !IsFinished
        && (BossStage == BossAttackStage.Startup || BossStage == BossAttackStage.Active);
    public float RecoveryRemaining => IsActive && !IsFinished && BossStage == BossAttackStage.Recovery
        ? Mathf.Max(0f, _data.chargeDuration + _data.activeDuration + _data.recoveryTime - _timer) / _data.attackSpeed : 0f;
    public override bool SupportsBossPolicy => true;
    protected override float BossInitialStartupDuration => _data.chargeDuration;

    public BossPhaseBurstAction(BossPhaseBurstSkillSO data, Enemy enemy) : base(data, enemy)
    {
        _data = data;
    }

    public override void Enter()
    {
        Center = _enemy.transform.position;
        _hitTargets.Clear();
        _heldPose = _released = _burstPlayed = false;
        // This fixed explosion owns its stage/armor contract even if a caller supplies no optional policy.
        ConfigureBossPolicy(new BossAttackPolicy
        {
            enabled = true, turnSpeed = 0f, lockFraction = 0f,
            armorDuringStartup = true, armorDuringActive = true
        });
        base.Enter();
        if (!CanContinueAction) return;
        var navigation = _enemy.GetComponent<NavMeshAgent>();
        if (navigation != null && navigation.enabled && navigation.isOnNavMesh)
        { navigation.ResetPath(); navigation.velocity = Vector3.zero; }
        _enemy.LockMovement();
        _movementLocked = true;
        PlayAnimation(0f);
    }

    public override void Update()
    {
        if (!CanContinueAction) return;
        float previousTime = _timer;
        base.Update();
        if (!CanContinueAction) return;
        float activeEnd = _data.chargeDuration + _data.activeDuration;
        if (_timer < _data.chargeDuration)
        {
            float releaseAt = _data.chargeDuration - _data.releaseHitDelay;
            if (!_released && _timer >= releaseAt)
            {
                _released = true;
                _enemy.SetAnimationSpeed(_data.attackSpeed);
                PlayAnimation(_data.chargePoseNormalized);
            }
            else if (!_released && !_heldPose && _timer >= _data.preparationDuration)
            {
                _heldPose = true;
                PlayAnimation(_data.chargePoseNormalized);
                _enemy.SetAnimationSpeed(0f);
            }
        }
        if (CrossesHitWindow(previousTime, _timer, _data.chargeDuration, _data.activeDuration))
        {
            SetBossStage(BossAttackStage.Active);
            if (!_burstPlayed)
            {
                _burstPlayed = true;
                _enemy.SetAnimationSpeed(_data.attackSpeed);
                PlaySwingSound();
                PlaySound(_data.LandingAudio, Center);
            }
            ExecuteBurst();
            if (!CanContinueAction) return;
        }
        if (_timer >= activeEnd) SetBossStage(BossAttackStage.Recovery);
        if (_timer >= activeEnd + _data.recoveryTime) IsFinished = true;
    }

    private void ExecuteBurst()
    {
        // A horizontal cylinder includes the arena's two-metre raised platform without spherical edge shrinkage.
        foreach (var collider in Physics.OverlapBox(Center, new Vector3(Radius, _data.heightTolerance, Radius),
            Quaternion.identity, _playerLayer, QueryTriggerInteraction.Collide))
        {
            if (!CanContinueAction) return;
            Vector3 closest = collider.ClosestPoint(new Vector3(Center.x, collider.bounds.center.y, Center.z));
            Vector3 offset = closest - Center;
            offset.y = 0f;
            if (offset.sqrMagnitude > Radius * Radius) continue;
            var receiver = collider.GetComponentInParent<IDamageable>();
            if (receiver == null || !_hitTargets.Add(receiver)) continue;
            Vector3 power = collider.transform.position - Center;
            power.y = 0f;
            if (power.sqrMagnitude > .001f) power = power.normalized * _data.knockbackForce;
            DamageManager.Apply(new HitInfo(_data.damage * _enemy.EnemyStat.GetStat(EnemyStatType.ATK),
                _enemy.Element, _enemy.EnemyStat.GetStat(EnemyStatType.CriticalChance),
                _enemy.EnemyStat.GetStat(EnemyStatType.CriticalDamage), power: power,
                staggerResistLevel: _data.staggerLevel, isParryable: false,
                source: this, impactSound: ResolveHitSound()), receiver, collider.gameObject);
        }
    }

    private void PlayAnimation(float normalizedTime)
    {
        var animator = _enemy.Animator;
        if (animator == null || string.IsNullOrWhiteSpace(_data.animName)
            || animator.runtimeAnimatorController == null) return;
        int state = Animator.StringToHash(_data.animName);
        if (animator.HasState(0, state)) animator.Play(state, 0, normalizedTime);
    }

    public override void Exit()
    {
        base.Exit();
        if (_movementLocked)
        {
            _movementLocked = false;
            _enemy.UnlockMovement();
        }
    }

    public override void OnParried() { }
}
