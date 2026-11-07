using System;
using UnityEngine;

/// <summary>Opt-in combat state machine. The boss is engaged from activation; there is no search state.</summary>
public sealed class BossTacticalController : IPlayerState
{
    private enum State { Inactive, InvalidProfile, WaitingForTarget, Positioning, Executing, Moving, Waiting, Recovering, Repositioning, Relocating }
    private readonly Boss _boss;
    private readonly BossCombatMotor _motor;
    private readonly BossPatternSelector _selector = new BossPatternSelector();
    private BossCombatProfileSO _profile;
    private BossPatternOption _option;
    private IBossPattern _pattern;
    private MonsterSkillData _requestedSkill;
    private BossMobilityAction _mobilityAction;
    private State _state;
    private float _remaining, _decisionRemaining, _actionElapsed, _actionTimeout;
    private float _interruptedRecovery;
    private bool _recoverWithinPattern, _strafeLeft;
    private int _phaseGeneration = -1, _revision;
    private int _entryGeneration = -1;
    private BossPatternSO _pendingEntryPattern;
    private bool _executingEntryPattern;
    private bool _interruptedEntryRecovery, _skipRepositionAfterRecovery;

    public string DebugState => _state.ToString();
    public string DebugPattern { get; private set; } = "";
    public string DebugReason { get; private set; } = "";
    public string LastFailure { get; private set; } = "";
    public int DebugStep { get; private set; } = -1;
    public BossMobilityController Mobility { get; }
    private BossPatternSO PendingEntryPattern => _entryGeneration == _boss.PhaseGeneration
        ? _pendingEntryPattern : (_boss.CurrentPhase as BossPhaseBase)?.Configuration?.entryPattern;
    public bool IsEntryPending => PendingEntryPattern != null;
    public bool IsInvulnerableEntryPending
    {
        get
        {
            var pattern = PendingEntryPattern;
            if (pattern is BossTacticalPatternSO tactical)
                return tactical.TryGetFirstAttack(_boss, out var skill) && skill is BossPhaseBurstSkillSO;
            if (pattern is BossSequencePatternSO sequence && sequence.steps != null)
                foreach (var step in sequence.steps)
                    if (step != null && step.kind == BossPatternStepKind.Attack)
                        return step.skill is BossPhaseBurstSkillSO;
            return false;
        }
    }

    public BossTacticalController(Boss boss)
    { _boss = boss; _motor = new BossCombatMotor(boss); Mobility = new BossMobilityController(boss); }

    public void Enter()
    {
        var profile = _boss.ActiveCombatProfile;
        if (_entryGeneration != _boss.PhaseGeneration)
        {
            _entryGeneration = _boss.PhaseGeneration;
            _pendingEntryPattern = (_boss.CurrentPhase as BossPhaseBase)?.Configuration?.entryPattern;
        }
        if (_profile != profile || _phaseGeneration != _boss.PhaseGeneration)
        { _selector.Reset(); Mobility.ClearIntent(); _phaseGeneration = _boss.PhaseGeneration; LastFailure = ""; _interruptedRecovery = 0f; _interruptedEntryRecovery = false; }
        _profile = profile;
        _decisionRemaining = 0f;
        if (_profile == null) { _state = State.Inactive; return; }
        foreach (string error in _profile.Validate())
        {
            _state = State.InvalidProfile;
            DebugReason = LastFailure = error;
            Debug.LogWarning($"{_boss.name}: Invalid boss combat profile. {error}", _boss);
            return;
        }
        _state = State.Positioning;
        DebugReason = "Combat ready";
        if (_interruptedRecovery > 0f)
        {
            BeginRecovery(_interruptedRecovery, false, _interruptedEntryRecovery);
            _interruptedRecovery = 0f;
            _interruptedEntryRecovery = false;
            DebugReason = "Resuming recovery after hit reaction";
        }
    }

    public void Exit()
    {
        // Hitting a recovering boss must not erase its punish window and make it attack sooner.
        bool reacting = _boss.isActiveAndEnabled && !_boss.IsDead
            && !_boss.IsTransitioning && !_boss.CombatLabPassive
            && (_boss.IsHurt || _boss.IsGroggy || _boss.IsRoot);
        var burst = _executingEntryPattern ? _boss.CurrentAction as BossPhaseBurstAction : null;
        float recovery = !reacting ? 0f : _state == State.Recovering ? _remaining : burst?.RecoveryRemaining ?? 0f;
        bool entryRecovery = recovery > 0f && (_skipRepositionAfterRecovery || burst != null);
        Cancel("Combat state exited");
        _interruptedRecovery = recovery;
        _interruptedEntryRecovery = entryRecovery;
    }

    public void Cancel(string reason = "Pattern cancelled")
        => CancelCore(reason, false);

    private void CancelCore(string reason, bool keepMobilityIntent)
    {
        // Invalidate callbacks before Exit invokes user-authored code.
        _revision++;
        _motor.Stop();
        _requestedSkill = null;
        _remaining = 0f;
        _interruptedRecovery = 0f;
        _recoverWithinPattern = false;
        _executingEntryPattern = false;
        _skipRepositionAfterRecovery = false;
        var pattern = _pattern;
        _pattern = null;
        _option = null;
        _state = State.Inactive;
        if (_mobilityAction != null)
        {
            if (_mobilityAction.HasStarted) Mobility.Complete(_mobilityAction);
            _mobilityAction = null;
        }
        if (!keepMobilityIntent) Mobility.ClearIntent();
        _boss.CancelCurrentAction();
        try { pattern?.Exit(); }
        catch (Exception exception)
        { LastFailure = "Pattern exit failed: " + exception.Message; Debug.LogException(exception, _boss); }
        DebugPattern = "";
        DebugStep = -1;
        DebugReason = reason;
    }

    public bool RequestSkill(MonsterSkillData skill)
    {
        if (skill == null || !CanAct || IsEntryPending || _executingEntryPattern) return false;
        _requestedSkill = skill;
        return true;
    }

    private bool CanAct => !_boss.IsDead && !_boss.IsTransitioning && _boss.isActiveAndEnabled
        && !_boss.CombatLabPassive && !_boss.IsHurt && !_boss.IsGroggy && !_boss.PendingGroggy
        && !_boss.IsRoot && !_boss.IsKnockbacking && !_boss.IsSilenced;

    public void Update()
    {
        if (_profile != _boss.ActiveCombatProfile) { Cancel("Profile changed"); Enter(); }
        if (_profile == null || _state == State.InvalidProfile) return;
        if (!CanAct)
        {
            if (_state != State.Inactive) Cancel("Combat interrupted");
            return;
        }
        if (_boss.CombatTarget == null)
        {
            if (_state != State.WaitingForTarget) Cancel("Combat target unavailable");
            _state = State.WaitingForTarget;
            return;
        }
        if (_state == State.Inactive || _state == State.WaitingForTarget)
        { _state = State.Positioning; _decisionRemaining = 0f; }

        // A phase opener is separate from repeatable options and survives targetless/state re-entry.
        if (_pendingEntryPattern != null && !_executingEntryPattern)
        {
            StartPattern(new BossPatternOption { pattern = _pendingEntryPattern, cooldown = 0f, recovery = 0f }, true);
            return;
        }

        if (_boss.CurrentAction?.WasParried == true)
        { FinishPattern("Parried", _profile.parryRecovery); return; }
        if (_requestedSkill != null)
        {
            var skill = _requestedSkill;
            Cancel("Explicit skill request");
            DebugPattern = "Requested: " + skill.name;
            _state = State.Executing;
            StartAttack(BossPatternStep.Attack(skill));
            return;
        }

        float delta = _boss.ActionDeltaTime;
        Mobility.Observe(delta);
        switch (_state)
        {
            case State.Positioning: TickPositioning(delta); break;
            case State.Repositioning: TickRepositioning(delta); break;
            case State.Executing: TickAttack(delta); break;
            case State.Relocating: TickMobility(delta); break;
            case State.Moving:
                var result = _motor.Tick(delta);
                if (result == BossCombatMotor.Result.Failed) Fail(_motor.Reason);
                else if (result == BossCombatMotor.Result.Completed) AdvancePattern();
                break;
            case State.Waiting:
            case State.Recovering:
                _remaining = Mathf.Max(0f, _remaining - delta);
                if (_remaining > 0f) return;
                if (_state == State.Waiting || _recoverWithinPattern)
                { _recoverWithinPattern = false; AdvancePattern(); }
                else if (_skipRepositionAfterRecovery)
                { _skipRepositionAfterRecovery = false; _state = State.Positioning; _decisionRemaining = 0f; }
                else BeginRepositioning();
                break;
        }
    }

    private void BeginRepositioning()
    {
        if (TryStartMobility()) return;
        // Keep the entire stationary punish window, then give locomotion a turn even
        // when an in-range attack is already ready. Existing profiles opt out with zero.
        _remaining = _profile.repositionDuration;
        _state = _remaining > 0f ? State.Repositioning : State.Positioning;
        _decisionRemaining = 0f;
        if (_state == State.Repositioning) StartRepositionMove();
    }

    private void StartRepositionMove()
    {
        Vector3 offset = _boss.CombatTarget.position - _boss.transform.position;
        offset.y = 0f;
        var mode = offset.magnitude > _profile.preferredMaxDistance ? BossMovementMode.Approach
            : offset.magnitude < _profile.preferredMinDistance ? BossMovementMode.Retreat
            : (_strafeLeft = !_strafeLeft) ? BossMovementMode.StrafeLeft : BossMovementMode.StrafeRight;
        _motor.Begin(new BossMovementSettings
        {
            mode = mode, minDistance = _profile.preferredMinDistance,
            maxDistance = _profile.preferredMaxDistance, duration = _remaining,
            timeout = _profile.movementTimeout
        }, _profile);
        DebugReason = "Repositioning after recovery: " + mode;
    }

    private void TickRepositioning(float delta)
    {
        if (TryStartMobility()) return;
        _remaining = Mathf.Max(0f, _remaining - delta);
        if (_remaining <= 0f)
        {
            _motor.Stop();
            _state = State.Positioning;
            _decisionRemaining = 0f;
            return;
        }
        var result = _motor.Tick(delta);
        if (result == BossCombatMotor.Result.Completed)
        {
            // Once the desired distance is reached, use the rest of this interval
            // to circle the target instead of stopping in place again.
            StartRepositionMove();
        }
        else if (result == BossCombatMotor.Result.Failed)
        {
            LastFailure = DebugReason = _motor.Reason;
            // A wall must not trap an otherwise attack-ready boss in recovery loops.
            _state = State.Positioning;
            _decisionRemaining = 0f;
        }
    }

    private void TickPositioning(float delta)
    {
        if (TryStartMobility()) return;
        _decisionRemaining -= delta;
        if (_decisionRemaining <= 0f)
        {
            _decisionRemaining = _profile.decisionInterval;
            var option = _selector.Select(_boss, _profile, out string reason);
            DebugReason = reason;
            if (option != null) { StartPattern(option); return; }
            if (!_motor.IsMoving)
            {
                float distance = Vector3.Distance(_boss.transform.position, _boss.CombatTarget.position);
                float minimum = _profile.preferredMinDistance, maximum = _profile.preferredMaxDistance;
                var hint = _selector.PositioningOption;
                if (hint != null)
                {
                    float allowedMin = hint.condition.minimumDistance, allowedMax = hint.condition.maximumDistance;
                    if (_selector.PositioningSkill != null)
                    {
                        allowedMin = Mathf.Max(allowedMin, _selector.PositioningSkill.minimumRange);
                        allowedMax = Mathf.Min(allowedMax, _selector.PositioningSkill.executeRange);
                    }
                    minimum = Mathf.Clamp(minimum, allowedMin, allowedMax);
                    maximum = Mathf.Clamp(maximum, minimum, allowedMax);
                    // Avoid stopping at a floating-point execute-range boundary.
                    if (maximum - minimum < .1f && allowedMax - allowedMin > .2f)
                    { maximum = Mathf.Clamp(maximum - .1f, allowedMin + .1f, allowedMax); minimum = maximum - .1f; }
                    DebugReason = "Positioning for " + hint.pattern.name;
                }
                var mode = distance > maximum ? BossMovementMode.Approach
                    : distance < minimum ? BossMovementMode.Retreat
                    : (_strafeLeft = !_strafeLeft) ? BossMovementMode.StrafeLeft : BossMovementMode.StrafeRight;
                _motor.Begin(new BossMovementSettings
                {
                    mode = mode, minDistance = minimum,
                    maxDistance = maximum, duration = _profile.strafeDuration,
                    timeout = _profile.movementTimeout
                }, _profile);
            }
        }
        if (!_motor.IsMoving) return;
        var result = _motor.Tick(delta);
        if (result == BossCombatMotor.Result.Failed)
        {
            LastFailure = DebugReason = _motor.Reason;
            BeginRecovery(_profile.failureRecovery, false);
        }
    }

    private void StartPattern(BossPatternOption option, bool isEntry = false)
    {
        _motor.Stop();
        if (isEntry)
            foreach (string issue in option.pattern.Validate())
            {
                _pendingEntryPattern = null;
                Fail("Invalid phase entry pattern: " + issue);
                return;
            }
        int revision = _revision;
        IBossPattern created;
        try { created = option.pattern.CreateRuntime(_boss); }
        catch (Exception exception)
        {
            if (isEntry) _pendingEntryPattern = null;
            Fail("Pattern creation failed: " + exception.Message); return;
        }
        if (!CanAct || _boss.CombatTarget == null || revision != _revision)
        { created?.Exit(); return; }
        if (created == null)
        {
            if (isEntry) _pendingEntryPattern = null;
            Fail("Pattern factory returned null"); return;
        }
        _option = option;
        _pattern = created;
        _executingEntryPattern = isEntry;
        if (isEntry) _requestedSkill = null;
        else _selector.RecordStarted(option);
        DebugPattern = option.pattern.name;
        DebugStep = -1;
        _state = State.Executing;
        try { created.Enter(); }
        catch (Exception exception) { Fail("Pattern enter failed: " + exception.Message); return; }
        if (_pattern != created || revision != _revision || !CanAct || _requestedSkill != null) return;
        if (_boss.CombatTarget == null) { Cancel("Combat target unavailable"); return; }
        AdvancePattern();
    }

    private void AdvancePattern()
    {
        _motor.Stop();
        if (!CanAct) return;
        if (_boss.CombatTarget == null) { Cancel("Combat target unavailable"); return; }
        if (_pattern == null) { FinishPattern("Action completed"); return; }
        // Custom IBossPattern implementations cannot consume an unbounded frame.
        for (int commands = 0; commands < 32; commands++)
        {
            var requesting = _pattern;
            int revision = _revision;
            bool hasStep;
            BossPatternStep step;
            try { hasStep = requesting.TryGetNextStep(out step); }
            catch (Exception exception) { Fail("Pattern command failed: " + exception.Message); return; }
            if (_pattern != requesting || revision != _revision || !CanAct || _requestedSkill != null) return;
            if (_boss.CombatTarget == null) { Cancel("Combat target unavailable"); return; }
            // A profile schedules one pattern cycle; Loop belongs to the legacy autonomous sequence executor.
            if (!hasStep) { FinishPattern("Pattern completed"); return; }
            DebugStep = requesting is IBossPatternProgress progress ? progress.CurrentStepIndex : DebugStep + 1;
            switch (step.Kind)
            {
                case BossPatternStepKind.Attack: StartAttack(step); return;
                case BossPatternStepKind.Move:
                    if (step.Movement == null) { Fail("Missing movement settings"); return; }
                    foreach (string issue in step.Movement.Validate()) { Fail(issue); return; }
                    _motor.Begin(step.Movement, _profile);
                    _state = State.Moving;
                    DebugReason = "Pattern movement: " + step.Movement.mode;
                    return;
                case BossPatternStepKind.Wait:
                case BossPatternStepKind.Recover:
                    if (!BossCombatValidation.Nonnegative(step.WaitSeconds)) { Fail("Invalid wait duration"); return; }
                    if (step.WaitSeconds == 0f) continue;
                    _remaining = step.WaitSeconds;
                    _state = step.Kind == BossPatternStepKind.Wait ? State.Waiting : State.Recovering;
                    _recoverWithinPattern = step.Kind == BossPatternStepKind.Recover;
                    DebugReason = step.Kind == BossPatternStepKind.Wait ? "Authored wait" : "Authored recovery";
                    return;
                default: Fail("Unsupported pattern command"); return;
            }
        }
        Fail("Too many immediate commands; check pattern cycle");
    }

    private void StartAttack(BossPatternStep step)
    {
        if (_boss.CombatTarget == null) { Cancel("Combat target unavailable"); return; }
        var skill = step.Skill;
        if (skill == null || !BossCombatValidation.Range(skill.minimumRange, skill.executeRange)
            || !BossCombatValidation.Positive(step.Timeout)) { Fail("Invalid attack command"); return; }
        if (!_boss.IsSkillReady(skill)) { Fail("Follow-up skill is on cooldown"); return; }
        // Approach must be authored or chosen by positioning, never an indefinite implicit chase inside a combo.
        float distance = Vector3.Distance(_boss.transform.position, _boss.CombatTarget.position);
        if (distance < skill.minimumRange || distance > skill.executeRange)
        { Fail("Attack execute range unavailable"); return; }
        int revision = _revision;
        EnemyAction action;
        try
        {
            action = skill.CreateAction(_boss);
            if (action == null) { Fail("Attack factory returned null"); return; }
            if (step.AttackPolicy != null) action.ConfigureBossPolicy(step.AttackPolicy);
        }
        catch (Exception exception) { Fail("Attack configuration failed: " + exception.Message); return; }
        if (!CanAct || revision != _revision || _requestedSkill != null) return;
        if (_boss.CombatTarget == null) { Cancel("Combat target unavailable"); return; }
        _motor.Stop();
        _boss.CurrentAction = action;
        _boss.HasSelectedAttack = true;
        _actionElapsed = 0f;
        _actionTimeout = step.Timeout;
        _state = State.Executing;
        DebugReason = "Executing " + skill.name;
        // Legacy actions aim once on start; policy actions own their limited preparation turn.
        if (step.AttackPolicy == null || !step.AttackPolicy.enabled)
        {
            Vector3 direction = _boss.CombatTarget.position - _boss.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > .001f) _boss.transform.rotation = Quaternion.LookRotation(direction);
        }
        // Commit only after the action exists and its owner/target remain valid.
        if (_executingEntryPattern) _pendingEntryPattern = null;
        try { action.Enter(); }
        catch (Exception exception) { Fail("Attack enter failed: " + exception.Message); return; }
        if (revision != _revision || _boss.CurrentAction != action || !CanAct) return;
    }

    private void TickAttack(float delta)
    {
        var action = _boss.CurrentAction;
        if (action == null) { AdvancePattern(); return; }
        int revision = _revision;
        _actionElapsed += delta;
        if (_actionElapsed >= _actionTimeout) { Fail("Attack timeout"); return; }
        try { if (action.IsActive && !action.IsFinished) action.Update(); }
        catch (Exception exception) { Fail("Attack update failed: " + exception.Message); return; }
        if (revision != _revision || _boss.CurrentAction != action || !CanAct || _requestedSkill != null) return;
        if (_boss.CombatTarget == null) { Cancel("Combat target unavailable"); return; }
        if (action.WasParried) { FinishPattern("Parried", _profile.parryRecovery); return; }
        if (action.IsActive && !action.IsFinished) return;
        _boss.CancelCurrentAction();
        if (!CanAct || revision != _revision || _requestedSkill != null) return;
        AdvancePattern();
    }

    private void Fail(string reason)
    {
        // A broken/unavailable opening command must not retain pending invulnerability and retry every tick.
        if (_executingEntryPattern) _pendingEntryPattern = null;
        LastFailure = reason;
        FinishPattern(reason, Mathf.Max(_profile != null ? _profile.failureRecovery : .25f, _option?.recovery ?? 0f));
    }

    private void FinishPattern(string reason, float? recovery = null)
    {
        bool completedEntry = _executingEntryPattern && !recovery.HasValue;
        float duration = recovery ?? _option?.recovery ?? _profile.failureRecovery;
        // Explicit recovery belongs to parry/failure. Add the normal completion delay once,
        // before Cancel clears the option; resumed recovery already contains this duration.
        if (!recovery.HasValue && !_executingEntryPattern && BossCombatValidation.Nonnegative(_boss.AdditionalPatternRecovery))
            duration += _boss.AdditionalPatternRecovery;
        string name = DebugPattern;
        CancelCore(reason, !recovery.HasValue);
        DebugPattern = name;
        if (completedEntry)
        {
            _pendingEntryPattern = null;
            // The opener owns its full aftermath; ordinary option/reposition delays start with the next pattern.
            if (CanAct) { _state = State.Positioning; _decisionRemaining = 0f; }
            return;
        }
        if (CanAct) BeginRecovery(duration, false);
    }

    private void BeginRecovery(float duration, bool withinPattern, bool skipReposition = false)
    {
        _motor.Stop();
        _remaining = Mathf.Max(0f, duration);
        _recoverWithinPattern = withinPattern;
        _skipRepositionAfterRecovery = skipReposition;
        _state = State.Recovering;
    }

    private bool TryStartMobility()
    {
        if (!CanAct || !Mobility.TrySelect(out var action)) return false;
        _motor.Stop();
        _mobilityAction = action;
        _boss.CurrentAction = action;
        _boss.HasSelectedAttack = true;
        _state = State.Relocating;
        _actionElapsed = 0f;
        var skill = (BossMobilitySkillSO)action.SkillData;
        bool jump = action.Kind == BossMobilityKind.EscapeJump || action.Kind == BossMobilityKind.JumpSlam;
        _actionTimeout = skill.startupTime + skill.recoveryTime
            + (jump ? skill.jumpDuration : skill.maxDistance / Mathf.Max(.01f, skill.moveSpeed)) + 2f;
        DebugPattern = "Mobility: " + action.Kind;
        DebugStep = 0;
        DebugReason = Mobility.LastReason;
        try { action.Enter(); }
        catch (Exception exception)
        { LastFailure = "Mobility start failed: " + exception.Message; Cancel("Mobility start failed"); }
        return true;
    }

    private void TickMobility(float delta)
    {
        var action = _mobilityAction;
        if (action == null) { Cancel("Mobility action unavailable"); return; }
        _actionElapsed += delta;
        if (_actionElapsed >= _actionTimeout)
        { LastFailure = "Mobility timeout"; Cancel(LastFailure); return; }
        int revision = _revision;
        try { if (action.IsActive && !action.IsFinished && _boss.CurrentAction == action) action.Update(); }
        catch (Exception exception)
        { LastFailure = "Mobility update failed: " + exception.Message; Cancel(LastFailure); return; }
        if (revision != _revision || !CanAct) return;
        if (action.WasParried) { FinishPattern("Parried", _profile.parryRecovery); return; }
        if (_boss.CurrentAction == action && action.IsActive && !action.IsFinished) return;
        if (action.Failed) LastFailure = action.FailureReason;
        Cancel(action.Failed ? action.FailureReason : "Mobility completed");
        if (CanAct) { _state = State.Positioning; _decisionRemaining = 0f; }
    }
}
