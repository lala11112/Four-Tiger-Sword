using UnityEngine;

public abstract partial class BaseForm
{
    private bool _actionExecutionComplete;
    private string _actionAnimationName;
    private int _actionAnimationHash;
    private bool _actionAnimationObserved;
    private bool _actionAnimationRestartPending;
    private float _previousAnimationTime;
    private int _actionAnimationStartFrame;
    private readonly System.Collections.Generic.List<AnimatorClipInfo> _actionAnimationClips = new(2);

    public bool IsCurrentActionExecutionComplete => _actionExecutionComplete;

    protected virtual WeaponActionData CurrentRecoveryStep => _currentAction == ActionType.Attack
        ? (_normalAttackActive && !_finisherAttackActive ? CurrentNormalAttackStep : null)
        : _currentAction == ActionType.Skill ? StepAt(_weaponActionData?.SkillSteps, _skillStep)
        : _currentAction == ActionType.Ultimate ? StepAt(_weaponActionData?.UltimateSteps, _ultimateStep) : null;

    protected virtual bool IsFinalRecoveryStep => _currentAction == ActionType.Attack
        || (_currentAction == ActionType.Skill && _skillStep == _weaponActionData.SkillSteps.Count - 1)
        || (_currentAction == ActionType.Ultimate && _ultimateStep == _weaponActionData.UltimateSteps.Count - 1);

    protected virtual float CurrentActionElapsed => _timer;
    protected virtual bool HasCompletedRequiredActionHits
        => CurrentActionElapsed >= LastExecutableHitEnd(CurrentRecoveryStep);

    public virtual bool CanCancelCurrentActionToMove => CanCancelCurrentAction(false);
    public virtual bool CanCancelCurrentActionToAction => CanCancelCurrentAction(true);

    private bool CanCancelCurrentAction(bool toAction)
    {
        var step = CurrentRecoveryStep;
        if (step == null || !IsFinalRecoveryStep) return false;
        float time = toAction ? step.ActionCancelTime : step.RecoveryCancelTime;
        return time >= 0f && KnockbackMotion.IsFinite(time)
            && CurrentActionElapsed >= time && HasCompletedRequiredActionHits;
    }

    private static WeaponActionData StepAt(System.Collections.Generic.List<WeaponActionData> steps, int index)
        => steps != null && index >= 0 && index < steps.Count ? steps[index] : null;

    // ProcessHit clips authored hit windows to Duration. Recovery preserves that same executable range.
    internal static float LastExecutableHitEnd(WeaponActionData step)
    {
        if (step == null || !KnockbackMotion.IsFinite(step.Duration)) return float.PositiveInfinity;
        float end = 0f;
        if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            foreach (var hit in step.HitEvents)
            {
                if (hit?.Execution == null || hit.Duration <= 0f) continue;
                float hitEnd = hit.StartTime + hit.Duration;
                if (!KnockbackMotion.IsFinite(hitEnd)) return float.PositiveInfinity;
                if (hit.StartTime <= step.Duration) end = Mathf.Max(end, hitEnd);
            }
        }
        else if (step.HitDuration > 0f && step.HitStartTime <= step.Duration)
            end = step.HitStartTime + step.HitDuration;
        return KnockbackMotion.IsFinite(end) ? Mathf.Clamp(end, 0f, Mathf.Max(0f, step.Duration))
            : float.PositiveInfinity;
    }

    protected void ResetActionTiming()
    {
        _actionExecutionComplete = false;
        _actionAnimationName = null;
        _actionAnimationObserved = _actionAnimationRestartPending = false;
    }

    // Keep the mechanical limit independent of the visible tail. Never call EndSkill/EndUltimate here:
    // those own cooldowns, animation speed and state-exit cleanup.
    protected bool CompleteActionExecution(bool executionComplete, bool waitForAnimation = true)
    {
        if (executionComplete && !_actionExecutionComplete)
        {
            _actionExecutionComplete = true;
            _playerController.StopAttackTrails();
            ClearSlashEffects();
            ResetAttackSounds();
        }
        bool animationComplete = !waitForAnimation || IsActionAnimationComplete();
        return _actionExecutionComplete && animationComplete;
    }

    private bool MatchesActionAnimation(AnimatorStateInfo state)
        => state.shortNameHash == _actionAnimationHash || state.fullPathHash == _actionAnimationHash
            || state.IsName(_actionAnimationName);

    private bool IsActionAnimationComplete()
    {
        var animator = _playerController?.Animator;
        if (string.IsNullOrEmpty(_actionAnimationName) || animator == null
            || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return true;

        // The destination is authoritative even when restarting a state that is still blending out.
        bool next = animator.IsInTransition(0) && MatchesActionAnimation(animator.GetNextAnimatorStateInfo(0));
        var state = next ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
        if (!MatchesActionAnimation(state))
            return _actionAnimationObserved || Time.frameCount > _actionAnimationStartFrame;

        if (_actionAnimationRestartPending && !next && !_actionAnimationObserved
            && state.normalizedTime >= _previousAnimationTime
            && Time.frameCount == _actionAnimationStartFrame) return false;

        _actionAnimationObserved = true;
        _actionAnimationRestartPending = false;
        _actionAnimationClips.Clear();
        if (next) animator.GetNextAnimatorClipInfo(0, _actionAnimationClips);
        else animator.GetCurrentAnimatorClipInfo(0, _actionAnimationClips);
        bool hasClip = false;
        foreach (var clip in _actionAnimationClips)
            if (clip.clip != null && clip.clip.length > 0f) { hasClip = true; break; }
        // A valid state with no Motion must not trap the player in an attack.
        return !hasClip || state.normalizedTime >= 1f;
    }
}
