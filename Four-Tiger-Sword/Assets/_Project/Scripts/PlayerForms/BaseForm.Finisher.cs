using UnityEngine;

public abstract partial class BaseForm
{
    private bool _normalAttackActive, _finisherAttackActive, _finisherFacingLocked;
    private WeaponActionData _finisherAttackStep;
    private Vector3 _finisherForward;

    /// <summary>Zero-based normal combo index; -1 when no normal attack is active.</summary>
    public int CurrentComboStage => _normalAttackActive && _currentAction == ActionType.Attack ? _comboStep : -1;
    public WeaponActionData CurrentNormalAttackStep => _finisherAttackActive ? _finisherAttackStep
        : HasNormalAttackStage(_comboStep) ? _weaponActionData.ComboSteps[_comboStep] : null;
    public bool CanBeginNormalFinisher => HasNormalAttackStage(2);
    public bool CanBeginFinisher => IsUsableNormalStep(_weaponActionData?.FinisherStep) || CanBeginNormalFinisher;
    public bool CanSelectFinisher => _normalAttackActive && !_finisherAttackActive
        && _currentAction == ActionType.Attack && _comboStep == 1
        && CurrentNormalAttackStep != null
        && _timer >= FinisherSelectionStart && _timer < CurrentNormalAttackStep.Duration;
    public float FinisherSelectionWindowRemaining => CanSelectFinisher
        ? Mathf.Max(0f, CurrentNormalAttackStep.Duration - _timer) : 0f;

    public bool HasNormalAttackStage(int zeroBasedIndex)
    {
        var steps = _weaponActionData?.Actions?.ComboSteps;
        return zeroBasedIndex >= 0 && steps != null && zeroBasedIndex < steps.Count
            && IsUsableNormalStep(steps[zeroBasedIndex]);
    }

    private static bool IsUsableNormalStep(WeaponActionData step)
        => step?.Execution?.Hitbox != null && step.Execution.Movement?.TargetApproach != null
            && step.Balance != null && step.Feedback?.Trail != null && step.Feedback.Camera != null
            && step.Duration > 0f && !float.IsInfinity(step.Duration) && !float.IsNaN(step.Duration);

    private float FinisherSelectionStart
    {
        get
        {
            var step = CurrentNormalAttackStep;
            if (step == null) return float.PositiveInfinity;
            float hitEnd = 0f;
            if (step.HitEvents != null && step.HitEvents.Count > 0)
            {
                foreach (var hit in step.HitEvents)
                {
                    if (hit?.Execution == null || hit.Duration <= 0f) continue;
                    float end = hit.StartTime + hit.Duration;
                    if (float.IsNaN(end) || float.IsInfinity(end)) return float.PositiveInfinity;
                    hitEnd = Mathf.Max(hitEnd, end);
                }
            }
            else if (step.HitDuration > 0f) hitEnd = step.HitStartTime + step.HitDuration;
            if (float.IsNaN(hitEnd) || float.IsInfinity(hitEnd))
                return float.PositiveInfinity;
            return Mathf.Max(0f, hitEnd);
        }
    }

    /// <summary>Starts terminal stage three directly, without replaying stage one or queuing stage four.</summary>
    public virtual bool BeginFinisher(Vector3 lockedForward, bool transformed)
    {
        if (!CanBeginFinisherWith(_playerController, lockedForward, transformed)) return false;
        WeaponActionData step = transformed && IsUsableNormalStep(_weaponActionData?.FinisherStep)
            ? _weaponActionData.FinisherStep : HasNormalAttackStage(2) ? _weaponActionData.ComboSteps[2] : null;
        Vector3 forward = Vector3.ProjectOnPlane(lockedForward, Vector3.up);
        _currentAction = ActionType.Attack;
        _normalAttackActive = _finisherAttackActive = true;
        _comboStep = 2;
        _finisherAttackStep = step;
        LockFinisherFacing(forward);
        _playerController.SetAnimationSpeed(AttackSpeed);
        PlayCombo();
        return true;
    }

    /// <summary>Checks a destination before it is equipped, so a malformed direction cannot commit a form swap.</summary>
    public bool CanBeginFinisherWith(PlayerController player, Vector3 lockedForward, bool transformed)
    {
        Vector3 forward = Vector3.ProjectOnPlane(lockedForward, Vector3.up);
        return (transformed ? CanBeginFinisher : CanBeginNormalFinisher)
            && player != null && player.StatManager != null && player.StatManager.CurrentHp > 0f
            && !player.IsStageTransitioning && !player.IsShopPaused
            && KnockbackMotion.IsFinite(forward.sqrMagnitude) && forward.sqrMagnitude >= 0.0001f;
    }

    private void ApplyFinisherFacing()
    {
        if (_finisherFacingLocked && _playerController != null)
            _playerController.transform.rotation = Quaternion.LookRotation(_finisherForward, Vector3.up);
    }

    /// <summary>Freezes facing during selection without rewinding the active attack or its movement.</summary>
    public void LockFinisherFacing(Vector3 forward)
    {
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (!KnockbackMotion.IsFinite(forward.sqrMagnitude) || forward.sqrMagnitude < 0.0001f) return;
        _finisherForward = forward.normalized;
        _finisherFacingLocked = true;
        ClearSoftTarget();
        _hadMotionTarget = _motionTargetLost = false;
        ApplyFinisherFacing();
    }

    private void ResetFinisherAttack()
    {
        _normalAttackActive = _finisherAttackActive = _finisherFacingLocked = false;
        _finisherAttackStep = null;
        _finisherForward = Vector3.zero;
    }
}
