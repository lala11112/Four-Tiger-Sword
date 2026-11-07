using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Boss-only pattern executor driven by Enemy's lifecycle and shared reaction states.</summary>
public sealed class BossBrain : EnemyBrainBase
{
    private readonly Boss _boss;
    private BossBehaviorState _behavior;
    private BossCombatState _combat;
    private BossRevivalState _revival;
    private bool _suspended;
    public BossTacticalController Tactical { get; private set; }

    public BossBrain(Boss boss) : base(boss) { _boss = boss; }

    protected override void BuildBehavior()
    {
        _behavior = new BossBehaviorState(_boss);
        Tactical = new BossTacticalController(_boss);
        _combat = new BossCombatState(_boss, _behavior, Tactical);
        _revival = new BossRevivalState(_boss);
        IdleState = ReturnState = _combat;
    }

    public override void Tick(bool canAct)
    {
        if (_boss.IsDead)
        {
            CancelPattern();
            _boss.EndPhases();
            base.Tick(canAct);
            return;
        }
        if (!_boss.isActiveAndEnabled) return;
        if (_suspended) return;
        if (_boss.IsTransitioning)
        {
            // Enter outside damage/action callbacks, even when hit stop freezes this tick.
            Machine.ChangeState(_revival);
            if (canAct && !_suspended && _boss.isActiveAndEnabled && !_boss.CombatLabPassive) _revival.Update();
            return;
        }
        if (Machine.CurrentState == _revival) Machine.ChangeState(_combat);
        // Temporary attack armor never suppresses posture breaks. Normal enemies retain their hit-to-groggy path.
        if (_boss.ActiveCombatProfile != null && _boss.PendingGroggy && !_boss.IsGroggy)
        { _boss.PendingGroggy = false; _boss.IsHurt = false; _boss.IsGroggy = true; }
        if (canAct && !_suspended && !_boss.CombatLabPassive)
        {
            _boss.TickPhase();
            if (StopAfterHook(canAct)) return;
        }
        base.Tick(canAct);
    }

    private bool StopAfterHook(bool canAct)
    {
        if (_boss.IsDead)
        {
            CancelPattern();
            _boss.EndPhases();
            base.Tick(false);
            return true;
        }
        if (!_boss.isActiveAndEnabled || _boss.IsTransitioning) return true;
        if (_boss.CombatLabPassive)
        {
            base.Tick(canAct);
            return true;
        }
        return false;
    }

    // Boss phase changes and explicit patterns replace normal-monster automatic self-destruction.
    public override bool TryStartSelfDestruct() => false;

    public override void Suspend()
    {
        _suspended = true;
        CancelPattern();
        if (_boss.IsTransitioning) return;
        base.Suspend();
    }

    public override void Resume()
    {
        _suspended = false;
        if (_boss.IsTransitioning) { _boss.SyncMovementLock(); return; }
        base.Resume();
    }

    public override void SetPassive(bool passive)
    {
        if (_boss.IsTransitioning) { _boss.SyncMovementLock(); return; }
        base.SetPassive(passive);
    }

    public void CancelPattern() => _combat?.Cancel();

    public bool RequestSkill(MonsterSkillData skill)
    {
        if (skill == null || _boss.IsDead || !_boss.isActiveAndEnabled
            || _boss.IsTransitioning || _boss.CombatLabPassive || _suspended) return false;
        return _boss.ActiveCombatProfile != null ? Tactical != null && Tactical.RequestSkill(skill)
            : _behavior != null && _behavior.RequestSkill(skill);
    }

    /// <summary>Keep unconfigured bosses on their original executor, including custom pattern callbacks.</summary>
    private sealed class BossCombatState : IPlayerState
    {
        private readonly Boss _boss;
        private readonly BossBehaviorState _legacy;
        private readonly BossTacticalController _tactical;
        private IPlayerState _active;
        private BossCombatProfileSO _profile;
        public BossCombatState(Boss boss, BossBehaviorState legacy, BossTacticalController tactical)
        { _boss = boss; _legacy = legacy; _tactical = tactical; }
        public void Enter()
        {
            _profile = _boss.ActiveCombatProfile;
            _active = _profile != null ? (IPlayerState)_tactical : _legacy;
            _active.Enter();
        }
        public void Update()
        {
            if (_profile != _boss.ActiveCombatProfile) { Exit(); Enter(); }
            _active?.Update();
        }
        public void Exit()
        {
            var active = _active;
            _active = null;
            active?.Exit();
        }
        public void Cancel()
        {
            _legacy.CancelSequence();
            _tactical.Cancel();
        }
    }

    private sealed class BossRevivalState : IPlayerState
    {
        private readonly Boss _boss;
        private bool _movementLocked;
        private bool _savedRootMotion;
        private AnimatorCullingMode _savedCulling;
        private float _savedSpeed;
        private bool _hasAnimator;
        private Animator _preparedAnimator;
        private bool _prepared;
        private static readonly int Down = Animator.StringToHash("PhaseDown");
        private static readonly int Revive = Animator.StringToHash("PhaseRevive");

        public BossRevivalState(Boss boss) { _boss = boss; }

        public void Enter() => Prepare();

        private void Prepare()
        {
            if (_prepared) return;
            _boss.BeginPhaseTransition();
            if (!_boss.IsTransitioning || _boss.IsDead || !_boss.isActiveAndEnabled) return;
            _prepared = true;
            _boss.LockMovement();
            _movementLocked = true;
            var nav = _boss.GetComponent<NavMeshAgent>();
            if (nav != null && nav.enabled && nav.isOnNavMesh)
            { nav.ResetPath(); nav.velocity = Vector3.zero; }
            _savedSpeed = _boss.ActorClock != null ? _boss.ActorClock.AnimationSpeed
                : _boss.Animator != null ? _boss.Animator.speed : 1f;
            PrepareAnimator();
            Pose();
        }

        private void PrepareAnimator()
        {
            if (_preparedAnimator == _boss.Animator) return;
            RestoreAnimator();
            _preparedAnimator = _boss.Animator;
            _hasAnimator = _preparedAnimator != null && _preparedAnimator.runtimeAnimatorController != null;
            if (!_hasAnimator) return;
            _savedRootMotion = _preparedAnimator.applyRootMotion;
            _savedCulling = _preparedAnimator.cullingMode;
            _preparedAnimator.applyRootMotion = false;
            _preparedAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _boss.SetAnimationSpeed(0f);
        }

        private void RestoreAnimator()
        {
            if (_hasAnimator && _preparedAnimator != null)
            {
                _preparedAnimator.applyRootMotion = _savedRootMotion;
                _preparedAnimator.cullingMode = _savedCulling;
                _preparedAnimator.speed = _savedSpeed;
            }
        }

        public void Update()
        {
            Prepare();
            if (!_prepared || !_boss.isActiveAndEnabled) return;
            _boss.TickPhaseTransition(_boss.ActionDeltaTime);
            if (_boss.IsTransitioning && _boss.isActiveAndEnabled) Pose();
        }

        private void Pose()
        {
            PrepareAnimator();
            if (!_hasAnimator || _boss.Animator == null) return;
            int state = _boss.IsReviving ? Revive : Down;
            if (!_boss.Animator.HasState(0, state)) return;
            // Sampling explicitly keeps the held pose and revival synchronized with actor time.
            _boss.Animator.Play(state, 0, _boss.IsReviving ? _boss.ReviveProgress : _boss.DownProgress);
            _boss.Animator.Update(0f);
        }

        public void Exit()
        {
            if (_movementLocked) _boss.UnlockMovement();
            _movementLocked = false;
            RestoreAnimator();
            if (_boss.Animator != null) _boss.SetAnimationSpeed(_savedSpeed);
            _preparedAnimator = null;
            _hasAnimator = false;
            _prepared = false;
        }
    }

    private sealed class BossBehaviorState : IPlayerState
    {
        private readonly Boss _boss;
        private readonly NavMeshAgent _nav;
        private readonly EnemyApproachState _approach;
        private readonly EnemyIdleState _idle;
        private readonly List<MonsterSkillData> _candidates = new List<MonsterSkillData>();
        private readonly List<float> _weights = new List<float>();
        private IBossPattern _pattern;
        private MonsterSkillData _selectedSkill;
        private MonsterSkillData _requestedSkill;
        private bool _approaching;
        private bool _fallbackAction;
        private bool _immediatePending;
        private bool _finishedOnce;
        private bool _invalidPatternReported;
        private float _waitRemaining;
        private float _retryAt;

        public BossBehaviorState(Boss boss)
        {
            _boss = boss;
            _nav = boss.GetComponent<NavMeshAgent>();
            _approach = new EnemyApproachState(boss);
            _idle = new EnemyIdleState(boss);
        }

        public void Enter() => _idle.Enter();
        public void Exit() => CancelSequence();

        public bool RequestSkill(MonsterSkillData skill)
        {
            if (_boss.IsHurt || _boss.IsGroggy
                || _boss.IsRoot || _boss.IsKnockbacking || _boss.IsSilenced) return false;
            _requestedSkill = skill;
            return true;
        }

        public void CancelSequence()
        {
            ExitApproach();
            _boss.CancelCurrentAction();
            EndPattern();
            _selectedSkill = null;
            _requestedSkill = null;
            _waitRemaining = 0f;
            _fallbackAction = false;
            _immediatePending = false;
            _finishedOnce = false;
        }

        public void Update()
        {
            if (_boss.IsDead || !_boss.isActiveAndEnabled || _boss.CombatLabPassive)
            { CancelSequence(); return; }
            if (_boss.IsKnockbacking || _boss.IsSilenced)
            { CancelSequence(); return; }

            // Requests from an action callback are committed only after that callback's stack unwinds.
            if (_requestedSkill != null && !_boss.IsTransitioning)
            {
                ExitApproach();
                _boss.CancelCurrentAction();
                _selectedSkill = _requestedSkill;
                _requestedSkill = null;
                _immediatePending = true;
                _waitRemaining = 0f;
                _finishedOnce = false;
            }

            if (_boss.CurrentAction != null)
            {
                if (_approaching) TickApproach();
                else TickAction();
                return;
            }
            if (_boss.IsTransitioning) return;

            if (_selectedSkill != null && _immediatePending)
            { TryPrepareAction(); return; }

            if (_boss.DetectedTarget == null)
            {
                CancelSequence();
                FollowRememberedTarget();
                return;
            }
            FaceTarget();
            if (Time.time < _retryAt || (!_boss.CanAttack && !_immediatePending) || _finishedOnce) return;

            if (_selectedSkill != null) { TryPrepareAction(); return; }
            if (_waitRemaining > 0f)
            {
                _waitRemaining = Mathf.Max(0f, _waitRemaining - _boss.ActionDeltaTime);
                if (_waitRemaining > 0f) return;
            }

            var asset = _boss.CurrentPhase?.Pattern;
            if (asset == null)
            {
                SelectWeightedAttack();
                return;
            }
            if (_pattern == null)
            {
                var created = asset.CreateRuntime(_boss);
                if (!CanStartAction()) return;
                _pattern = created;
                if (_pattern == null)
                {
                    ReportInvalidPattern("CreateRuntime returned null.");
                    _finishedOnce = true;
                    return;
                }
                var entering = _pattern;
                entering.Enter();
                if (_pattern != entering || !CanStartAction() || _requestedSkill != null) return;
            }

            // A custom pattern cannot consume unlimited zero-duration commands in one frame.
            for (int commands = 0; commands < 32; commands++)
            {
                var requesting = _pattern;
                bool hasStep = requesting.TryGetNextStep(out var step);
                if (_pattern != requesting || !CanStartAction() || _requestedSkill != null) return;
                if (!hasStep)
                {
                    bool repeat = _pattern.Loop;
                    EndPattern();
                    _finishedOnce = !repeat;
                    _boss.StartAttackCooldown();
                    return;
                }
                if (step.Kind == BossPatternStepKind.Wait)
                {
                    if (float.IsNaN(step.WaitSeconds) || float.IsInfinity(step.WaitSeconds)
                        || step.WaitSeconds < 0f)
                    { FailSequence("Wait duration must be finite and nonnegative."); return; }
                    _waitRemaining = step.WaitSeconds;
                    if (_waitRemaining > 0f) return;
                    continue;
                }
                _selectedSkill = step.Skill;
                if (_selectedSkill == null)
                { FailSequence("Attack step has no skill."); return; }
                TryPrepareAction();
                return;
            }
        }

        private void TryPrepareAction()
        {
            bool immediate = _immediatePending;
            if (_selectedSkill == null || (!immediate && _boss.DetectedTarget == null)
                || !CanStartAction()) return;
            // Explicit sequence steps do not consult random weights or engageRange.
            if (!immediate && !_boss.IsSkillReady(_selectedSkill)) return;
            if (!immediate && (float.IsNaN(_selectedSkill.minimumRange) || float.IsInfinity(_selectedSkill.minimumRange)
                || float.IsNaN(_selectedSkill.executeRange) || float.IsInfinity(_selectedSkill.executeRange)
                || _selectedSkill.minimumRange < 0f
                || _selectedSkill.executeRange < _selectedSkill.minimumRange))
            { FailSequence("Skill executeRange must be finite and at least minimumRange."); return; }

            var action = _selectedSkill.CreateAction(_boss);
            _selectedSkill = null;
            if (!CanStartAction()) return;
            if (action == null) { FailSequence("Skill CreateAction returned null."); return; }
            _boss.CurrentAction = action;
            _boss.HasSelectedAttack = true;
            _immediatePending = false;
            if (immediate || _boss.IsInExecuteRange) BeginAction(!immediate);
            else
            {
                _approaching = true;
                _approach.Enter();
            }
        }

        private void TickApproach()
        {
            if (_boss.DetectedTarget == null)
            { CancelSequence(); FollowRememberedTarget(); return; }
            if (_boss.IsInExecuteRange)
            {
                ExitApproach();
                BeginAction();
                return;
            }
            _approach.Update();
            if (_boss.CurrentAction == null)
            {
                // Approach records the failed skill's retry cooldown before clearing the action.
                CancelSequence();
                _retryAt = Time.time + Mathf.Max(0.1f, _boss.FailedAttackRetryDelay);
            }
        }

        private void BeginAction(bool requireTarget = true)
        {
            if (!CanStartAction() || (requireTarget && _boss.DetectedTarget == null))
            { CancelSequence(); return; }
            FaceTarget();
            if (_nav != null && _nav.enabled && _nav.isOnNavMesh) _nav.ResetPath();
            var action = _boss.CurrentAction;
            action?.Enter();
            if (_boss.CurrentAction != action || !CanContinueAction()) CancelSequence();
        }

        private void TickAction()
        {
            var action = _boss.CurrentAction;
            if (action == null) return;
            if (!action.IsFinished && action.IsActive) action.Update();
            if (_boss.CurrentAction != action)
            { CancelSequence(); return; }
            if (_boss.IsDead || _boss.IsHurt || _boss.IsGroggy
                || _boss.IsRoot || _boss.IsKnockbacking || _boss.IsSilenced || action.WasParried)
            { CancelSequence(); return; }
            if (!action.IsFinished && action.IsActive) return;
            _boss.CancelCurrentAction();
            if (_boss.IsTransitioning) return;
            if (_fallbackAction)
            {
                _fallbackAction = false;
                _boss.StartAttackCooldown();
            }
        }

        private void SelectWeightedAttack()
        {
            var skills = _boss.ActiveSkillSet?.skillData;
            if (skills == null || _boss.DetectedTarget == null) return;
            float distance = Vector3.Distance(_boss.transform.position, _boss.DetectedTarget.position);
            float total = 0f;
            _candidates.Clear();
            _weights.Clear();
            foreach (var skill in skills)
            {
                float weight = _boss.GetAttackSelectionWeight(skill, distance);
                if (weight <= 0f) continue;
                _candidates.Add(skill);
                _weights.Add(weight);
                total += weight;
            }
            if (_candidates.Count == 0)
            {
                bool outsideAllRanges = true;
                foreach (var skill in skills)
                    if (skill != null && skill.weight > 0f && distance <= skill.engageRange)
                        outsideAllRanges = false;
                if (outsideAllRanges && _nav != null && _nav.enabled && _nav.isOnNavMesh)
                    _nav.SetDestination(_boss.DetectedTarget.position);
                return;
            }
            float roll = UnityEngine.Random.Range(0f, total);
            int selected = _candidates.Count - 1;
            for (int i = 0; i < _candidates.Count; i++)
            {
                roll -= _weights[i];
                if (roll <= 0f) { selected = i; break; }
            }
            _selectedSkill = _candidates[selected];
            _fallbackAction = true;
            TryPrepareAction();
        }

        private void FailSequence(string message)
        {
            ReportInvalidPattern(message);
            CancelSequence();
            _retryAt = Time.time + Mathf.Max(0.1f, _boss.FailedAttackRetryDelay);
        }

        private void ReportInvalidPattern(string message)
        {
            if (_invalidPatternReported) return;
            _invalidPatternReported = true;
            Debug.LogWarning($"{_boss.name}: Invalid boss pattern. {message}", _boss);
        }

        private void EndPattern()
        {
            var pattern = _pattern;
            _pattern = null;
            pattern?.Exit();
        }

        private bool CanContinueAction() => !_boss.IsDead && !_boss.IsTransitioning && _boss.isActiveAndEnabled
            && !_boss.CombatLabPassive && !_boss.IsHurt && !_boss.IsGroggy
            && !_boss.IsRoot && !_boss.IsKnockbacking && !_boss.IsSilenced;

        private bool CanStartAction() => CanContinueAction();

        private void ExitApproach()
        {
            if (!_approaching) return;
            _approaching = false;
            _approach.Exit();
        }

        private void FollowRememberedTarget()
        {
            if (_boss.HasTargetMemory && _nav != null && _nav.enabled && _nav.isOnNavMesh)
                _nav.SetDestination(_boss.LastKnownTargetPosition);
        }

        private void FaceTarget()
        {
            if (_boss.DetectedTarget == null) return;
            Vector3 direction = _boss.DetectedTarget.position - _boss.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
                _boss.transform.rotation = Quaternion.RotateTowards(_boss.transform.rotation,
                    Quaternion.LookRotation(direction), 360f * _boss.ActionDeltaTime);
        }
    }
}
