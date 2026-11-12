using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Finite, damage-aware mobility with fixed goals and deterministic navigation cleanup.</summary>
public sealed class BossMobilityAction : EnemyAction
{
    private enum Phase { Startup, Travel, Recovery }
    private readonly BossMobilitySkillSO _data;
    private readonly BossMobilityNavigation.Plan _plan;
    private readonly NavMeshAgent _nav;
    private readonly HashSet<IDamageable> _hitTargets = new HashSet<IDamageable>();
    private Phase _phase;
    private float _phaseElapsed;
    private bool _locked, _ownsNavigation, _oldUpdatePosition, _oldUpdateRotation, _oldStopped, _airAnimation;
    private Vector3 _lastGround, _lastClearGround, _previousHitCenter;

    public BossMobilityKind Kind => _data.kind;
    public bool DealsDamage => _data.DealsDamage;
    public Vector3 Destination => _plan.Destination;
    public Vector3 StartPosition => _plan.StartPosition;
    public bool HasStarted { get; private set; }
    public bool Failed { get; private set; }
    public string FailureReason { get; private set; } = "";
    public bool IsAirborne => _data.IsJump && _phase == Phase.Travel && IsActive;
    public float MovementProgress { get; private set; }
    public float WarningProgress
    {
        get
        {
            bool landingWarning = Kind == BossMobilityKind.JumpSlam;
            bool inFlight = landingWarning && _phase == Phase.Travel;
            if (!IsActive || IsFinished || WasParried || !CanContinueAction || !DealsDamage || Failed
                || (_phase != Phase.Startup && !inFlight))
                return 0f;

            float duration = _data.startupTime + (landingWarning ? _data.jumpDuration : 0f);
            float elapsed = _phaseElapsed + (inFlight ? _data.startupTime : 0f);
            if (!(duration > 0f) || float.IsInfinity(duration) || float.IsNaN(elapsed)) return 0f;
            return Mathf.Clamp01(elapsed / duration);
        }
    }

    public override bool SupportsBossPolicy => true;
    protected override float BossInitialStartupDuration => _data.startupTime;
    protected override bool UsesTimedTelegraph => false;
    protected override bool StartsAttackWarningOnEnter => DealsDamage;

    public BossMobilityAction(BossMobilitySkillSO data, Boss owner, BossMobilityNavigation.Plan plan)
        : base(data, owner)
    { _data = data; _plan = plan; _nav = owner.GetComponent<NavMeshAgent>(); }

    public override void Enter()
    {
        HasStarted = false; Failed = false; FailureReason = ""; MovementProgress = 0f;
        _phase = Phase.Startup; _phaseElapsed = 0f; _airAnimation = false;
        _hitTargets.Clear();
        if (!BossMobilityNavigation.CanStart(_plan)) { Fail("Mobility destination no longer safe"); return; }
        Vector3 facing = Kind == BossMobilityKind.EscapeJump && ((Boss)_enemy).CombatTarget != null
            ? ((Boss)_enemy).CombatTarget.position - StartPosition : Destination - StartPosition;
        facing.y = 0f;
        if (facing.sqrMagnitude > .001f) _enemy.transform.rotation = Quaternion.LookRotation(facing);
        HasStarted = true;
        base.Enter();
        if (!CanContinueAction) { Exit(); return; }
        _oldUpdatePosition = _nav.updatePosition;
        _oldUpdateRotation = _nav.updateRotation;
        _oldStopped = _nav.isStopped;
        _ownsNavigation = true;
        _lastGround = _nav.nextPosition;
        _lastClearGround = _lastGround;
        _nav.ResetPath(); _nav.velocity = Vector3.zero;
        _enemy.LockMovement(); _locked = true;
        _nav.updatePosition = false; _nav.updateRotation = false;
        SetBossStage(BossAttackStage.Startup);
        Animate(_data.startupAnimName);
        if (_data.startupTime <= 0f) BeginTravel();
    }

    public override void Update()
    {
        if (!IsActive || IsFinished) return;
        if (!CanContinueAction) { Exit(); return; }
        float delta = _enemy.ActionDeltaTime * _data.attackSpeed;
        if (delta <= 0f) return;
        base.Update();
        _phaseElapsed += delta;
        switch (_phase)
        {
            case Phase.Startup:
                if (_phaseElapsed >= _data.startupTime) BeginTravel();
                break;
            case Phase.Travel:
                if (_data.IsJump) TickJump();
                else TickGround(delta);
                break;
            case Phase.Recovery:
                if (_phaseElapsed >= _data.recoveryTime) Complete();
                break;
        }
    }

    private void BeginTravel()
    {
        if (!CanContinueAction || !BossMobilityNavigation.LandingSafe(_plan))
        { Fail("Mobility landing changed during startup"); return; }
        _phase = Phase.Travel; _phaseElapsed = 0f;
        SetBossStage(BossAttackStage.Active);
        Animate(_data.IsJump && !string.IsNullOrWhiteSpace(_data.liftAnimName) ? _data.liftAnimName : _data.animName);
        _previousHitCenter = HitCenter();
        if (DealsDamage) PlaySwingSound();
        if (Kind == BossMobilityKind.DashSlash)
        {
            EndAttackWarning();
            EndTelegraph();
        }
    }

    private void TickJump()
    {
        if (!BossMobilityNavigation.LandingSafe(_plan)) { Fail("Jump landing became occupied"); return; }
        float progress = Mathf.Clamp01(_phaseElapsed / _data.jumpDuration);
        Vector3 ground = Vector3.Lerp(StartPosition, Destination, progress);
        Vector3 desired = ground + Vector3.up * (_data.jumpHeight * Mathf.Sin(progress * Mathf.PI));
        if (!Move(desired, ground)) return;
        MovementProgress = progress;
        if (!_airAnimation && progress >= .35f)
        { _airAnimation = true; Animate(_data.animName); }
        if (progress < 1f) return;
        if (!BossMobilityNavigation.LandingSafe(_plan) || !BossMobilityNavigation.GroundSafe(_plan, _lastGround))
        { Fail("Jump landing is unsafe"); return; }
        BeginRecovery(true);
    }

    private void TickGround(float delta)
    {
        float budget = _data.moveSpeed * delta;
        Vector3 before = _enemy.transform.position;
        for (int steps = 0; steps < 16 && budget > .0001f; steps++)
        {
            Vector3 goal = Destination;
            Vector3 offset = BossMobilityNavigation.Flat(goal - _lastGround);
            float distance = offset.magnitude;
            if (distance < .04f)
            {
                BeginRecovery(false); break;
            }
            float step = Mathf.Min(distance, budget);
            Vector3 desired = _lastGround + offset / distance * step;
            desired.y = goal.y;
            if (!Move(desired, desired)) return;
            budget -= step;
            if (Kind == BossMobilityKind.DashSlash)
            {
                HitSweep(_previousHitCenter, HitCenter());
                _previousHitCenter = HitCenter();
                if (!CanContinueAction) { Exit(); return; }
            }
        }
        float total = BossMobilityNavigation.Flat(Destination - StartPosition).magnitude;
        MovementProgress = total > .01f ? Mathf.Clamp01(BossMobilityNavigation.Flat(_lastGround - StartPosition).magnitude / total) : 1f;
        if (_phase == Phase.Travel && Vector3.Distance(before, _enemy.transform.position) < .00001f)
            Fail("Mobility movement made no progress");
    }

    private bool Move(Vector3 desiredRoot, Vector3 desiredGround)
    {
        if (!CanContinueAction || !BossMobilityNavigation.ValidateStep(_plan, _enemy.transform.position,
            desiredRoot, desiredGround, out var projected))
        { Fail("Mobility path became blocked"); return false; }
        float height = desiredRoot.y - desiredGround.y;
        Vector3 delta = projected - _nav.nextPosition;
        _nav.velocity = Vector3.zero;
        _nav.Move(delta);
        Vector3 actual = _nav.nextPosition;
        if (!BossMobilityNavigation.Finite(actual) || BossMobilityNavigation.Flat(actual - projected).sqrMagnitude > .0025f)
        { Fail("NavMesh rejected mobility step"); return false; }
        _lastGround = actual;
        bool clearGround = BossMobilityNavigation.GroundSafe(_plan, actual);
        if (clearGround) _lastClearGround = actual;
        else if (!_data.IsJump) { Fail("Mobility ground became occupied"); return false; }
        _enemy.transform.position = actual + Vector3.up * height;
        return true;
    }

    private void BeginRecovery(bool landed)
    {
        _enemy.transform.position = _lastGround;
        _phase = Phase.Recovery; _phaseElapsed = 0f;
        MovementProgress = 1f;
        SetBossStage(BossAttackStage.Recovery);
        Animate(_data.landingAnimName);
        if (landed)
        {
            PlaySound(_data.LandingAudio, _lastGround);
            if (Kind == BossMobilityKind.JumpSlam && CanContinueAction)
            {
                EndTelegraph();
                if (CanContinueAction) HitSweep(HitCenter(), HitCenter(), _data.slamRadius);
            }
        }
        if (_data.recoveryTime <= 0f) Complete();
    }

    private Vector3 HitCenter() => _enemy.transform.TransformPoint(Kind == BossMobilityKind.JumpSlam
        ? new Vector3(0f, _data.hitBoxOffset.y, 0f) : _data.hitBoxOffset);

    private void HitSweep(Vector3 from, Vector3 to, float radius = -1f)
    {
        if (!DealsDamage || !CanContinueAction) return;
        BroadcastHit();
        if (!CanContinueAction) return;
        if (radius < 0f) radius = _data.hitBoxRadius;
        foreach (var collider in Physics.OverlapCapsule(from, to, radius, _playerLayer, QueryTriggerInteraction.Collide))
        {
            if (!CanContinueAction) return;
            var receiver = collider.GetComponentInParent<IDamageable>();
            if (receiver == null || !_hitTargets.Add(receiver)) continue;
            Vector3 power = BossMobilityNavigation.Flat(collider.transform.position - _enemy.transform.position);
            if (power.sqrMagnitude > .001f) power = power.normalized * _data.knockbackForce;
            DamageManager.Apply(new HitInfo(_data.damage * _enemy.EnemyStat.GetStat(EnemyStatType.ATK),
                _enemy.Element, _enemy.EnemyStat.GetStat(EnemyStatType.CriticalChance),
                _enemy.EnemyStat.GetStat(EnemyStatType.CriticalDamage), power: power,
                staggerResistLevel: _data.staggerLevel, isParryable: _data.isParryable,
                source: this, impactSound: ResolveHitSound()), receiver, collider.gameObject);
        }
    }

    public override void Exit()
    {
        // Clean up even if a callback already marked the action finished or interrupted it.
        RestoreNavigation();
        base.Exit();
    }

    private void RestoreNavigation()
    {
        if (_ownsNavigation)
        {
            _ownsNavigation = false;
            if (_nav != null)
            {
                // Never enable a disabled encounter agent or warp to an unrelated sampled floor.
                if (_nav.enabled && _nav.isOnNavMesh)
                {
                    if (!BossMobilityNavigation.SafeGroundFallback(_plan, _nav.nextPosition, _lastClearGround, out var ground))
                        MarkGroundRecoveryBlocked();
                    _nav.nextPosition = ground;
                    _enemy.transform.position = _nav.nextPosition;
                    _nav.ResetPath(); _nav.velocity = Vector3.zero;
                }
                else if (BossMobilityNavigation.Finite(_lastGround))
                {
                    if (!BossMobilityNavigation.SafeGroundFallback(_plan, _lastGround, _lastClearGround, out var ground))
                        MarkGroundRecoveryBlocked();
                    _enemy.transform.position = ground;
                }
                _nav.updatePosition = _oldUpdatePosition;
                _nav.updateRotation = _oldUpdateRotation;
            }
        }
        if (_locked) { _locked = false; _enemy.UnlockMovement(); }
        if (_nav != null && _nav.enabled && _nav.isOnNavMesh)
        {
            _enemy.SyncMovementLock();
            if (_oldStopped && _enemy.CurrentAction == this) _nav.isStopped = true;
        }
    }

    private void Fail(string reason)
    {
        Failed = true; FailureReason = reason;
        RestoreNavigation();
        IsFinished = true;
        if (IsActive) base.Exit();
    }

    private void Complete()
    {
        RestoreNavigation();
        IsFinished = true;
    }

    private void MarkGroundRecoveryBlocked()
    {
        Failed = true;
        FailureReason = string.IsNullOrEmpty(FailureReason) ? "Ground recovery obstructed"
            : FailureReason + "; ground recovery obstructed";
    }

    private void Animate(string state)
    {
        var animator = _enemy.Animator;
        if (animator != null && animator.runtimeAnimatorController != null && !string.IsNullOrWhiteSpace(state)
            && animator.HasState(0, Animator.StringToHash(state)))
            animator.CrossFadeInFixedTime(state, .045f, 0, 0f);
    }
}
