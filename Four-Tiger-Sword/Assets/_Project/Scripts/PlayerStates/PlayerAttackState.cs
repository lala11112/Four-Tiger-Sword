using UnityEngine;

public class PlayerAttackState : IPlayerState
{
    private readonly PlayerController _playerController;
    private IForm _form;
    private IHeavyAttackForm _heavy;
    private bool _normalStarted, _normalComplete, _heavyStarted;
    private bool _pending, _heavyRequested;
    private bool _queuedHeavy;
    private bool _finisherOpened;
    private bool _finisherCommitted;
    private BaseForm _queuedSkillForm;
    private int _queuedSkillStage = -1;
    private uint _pressId;
    private float _heldTime;
    private bool _isAttackComplete;
    public bool IsAttackComplete
    {
        // Transitions run before Update: accept a fresh press on the completion frame too.
        get => _isAttackComplete && !HasQueuedSkill && !HasReadyNormalFollowUp
            && !(_heavyStarted && _heavy.CanHeavyAttack
                && _playerController.Input.AttackBuffer.IsActive
                && _playerController.Input.AttackPressId != _pressId);
        private set => _isAttackComplete = value;
    }
    public bool IsCharging => _pending && (!_normalStarted || _normalComplete);
    public bool IsHeavyAttack => _heavyStarted;
    internal bool HasQueuedSkill => _queuedSkillForm != null;
    public bool HasSkillRequest => HasQueuedSkill || _playerController.Input.SkillBuffer.IsActive;
    // A chain skill may replace normal recovery, but cannot interrupt a charge or finisher hit.
    public bool CanChainSwitch => _normalStarted && !_heavyStarted && !_pending
        && !_playerController.IsFinisherSelecting
        && (_normalComplete || !_finisherCommitted
            && _form is BaseForm form && form.CanCancelCurrentActionToAction);
    // Charge decisions and committed finishers retain their existing skill/ultimate exits.
    public bool CanCancelToAction => !_heavyStarted
        && (_pending || _finisherCommitted || !_normalStarted || _normalComplete
            || _form is BaseForm form && form.CanCancelCurrentActionToAction);
    // State transitions run before Update, so a ready attack reservation must win over movement.
    public bool CanCancelToMove => !HasQueuedSkill && _normalStarted && !_normalComplete && !_pending && !_heavyStarted
        && !_finisherCommitted && !_playerController.IsFinisherSelecting
        && _form is BaseForm form && form.CanCancelCurrentActionToMove
        && !HasReadyNormalFollowUp;
    private bool HasReadyNormalFollowUp => !HasQueuedSkill && _normalStarted && !_pending && !_heavyStarted
        && !_finisherCommitted && !_playerController.IsFinisherSelecting
        && _playerController.Input.AttackBuffer.IsActive && _form is BaseForm form
        && ((form.CanChainAttack && form.HasNormalAttackStage(form.CurrentComboStage + 1))
            || form.CanRestartNormalCombo);
    public bool CanSelectFinisher => !_finisherOpened && !_heavyStarted && _normalStarted
        && !_normalComplete && _form is BaseForm form && form.CanSelectFinisher;
    public bool IsFinisherWindowValid => !_heavyStarted && _normalStarted && !_normalComplete
        && _form is BaseForm form && form.CanSelectFinisher;

    public PlayerAttackState(PlayerController playerController) => _playerController = playerController;

    // Capture before state transitions: an earlier movement/combo gate must not steal this hit.
    internal void PrepareSkillReservation()
    {
        var form = _form as BaseForm;
        bool canReserve = _normalStarted && !_heavyStarted && !_finisherCommitted
            && !_playerController.IsFinisherSelecting && form != null && form.CurrentComboStage >= 0
            && ReferenceEquals(form, _playerController.FormManager.CurrentForm);
        if (HasQueuedSkill && (!canReserve || !ReferenceEquals(form, _queuedSkillForm)
            || form.CurrentComboStage != _queuedSkillStage || !_playerController.CanSkill))
            CancelSkillReservation();

        if (!canReserve) return;
        var input = _playerController.Input;
        if (input.SkillBuffer.IsActive)
        {
            input.SkillBuffer.Consume();
            // Unavailable skills never reserve the hit or block locomotion.
            if (_playerController.CanSkill)
            {
                _queuedSkillForm = form;
                _queuedSkillStage = form.CurrentComboStage;
            }
        }
        if (!HasQueuedSkill) return;

        // A reservation replaces all follow-up taps/holds, but preserves the current attack.
        input.AttackBuffer.Consume();
        _pending = _heavyRequested = false;
        _heldTime = 0f;
        _pressId = input.AttackPressId;
    }

    internal void CancelSkillReservation()
    {
        _queuedSkillForm = null;
        _queuedSkillStage = -1;
    }

    public void Enter()
    {
        CancelSkillReservation();
        _form = _playerController.FormManager.CurrentForm;
        _heavy = _form as IHeavyAttackForm;
        _normalStarted = _normalComplete = _heavyStarted = false;
        _pending = _heavyRequested = IsAttackComplete = false;
        _queuedHeavy = false;
        _finisherOpened = _finisherCommitted = false;
        _pressId = _playerController.Input.AttackPressId;
        if (_heavy?.CanHeavyAttack == true)
        {
            CapturePress();
            // Ground locomotion must not keep playing while waiting for tap/hold.
            _playerController.Animator.CrossFade("Idle", 0.1f);
        }
        else BeginNormalAttack();
    }

    public void Update()
    {
        var input = _playerController.Input;
        if (!_playerController.IsGround()) _playerController.Movement.ApplyGravity();

        if (_playerController.IsFinisherSelecting)
        {
            _form.UpdateAttack(out _normalComplete);
            IsAttackComplete = _normalComplete;
            return;
        }

        if (_heavyStarted)
        {
            // A new press during the dash queues one follow-up without another hold delay.
            if (input.AttackBuffer.IsActive && input.AttackPressId != _pressId && _heavy.CanHeavyAttack)
            {
                _pressId = input.AttackPressId;
                _queuedHeavy = true;
                input.AttackBuffer.Consume();
            }
            _heavy.UpdateHeavyAttack(out bool finished);
            if (finished && _queuedHeavy && _heavy.CanHeavyAttack)
            {
                _queuedHeavy = false;
                if (_heavy.TryContinueHeavyAttack())
                {
                    IsAttackComplete = false;
                    return;
                }
            }
            IsAttackComplete = finished;
            return;
        }

        if (!HasQueuedSkill && !_finisherCommitted && !_pending && _normalStarted && input.AttackBuffer.IsActive
            && input.AttackPressId != _pressId && _heavy?.CanHeavyAttack == true)
            CapturePress();

        if (_pending)
        {
            bool samePress = input.AttackPressId == _pressId;
            if (samePress) _heldTime = input.AttackHeldTime;
            _heavyRequested |= _heldTime >= _heavy.HeavyAttackHoldTime;
            if (!_heavy.CanHeavyAttack) _heavyRequested = false;

            if (_heavyRequested)
            {
                // Cancel before updating the normal attack so it cannot hit again this frame.
                if (_normalStarted) _form.EndAttack();
                if (_heavy.TryBeginHeavyAttack())
                {
                    _pending = false;
                    _heavyStarted = true;
                    IsAttackComplete = false;
                    return;
                }
                // The resource may have become unavailable before execution.
                _normalStarted = false;
                BeginNormalAttack();
            }
            else if (!_heavy.CanHeavyAttack || !samePress || !input.IsAttackHeld)
            {
                _pending = false;
                if (!_normalStarted) BeginNormalAttack();
                else input.AttackBuffer.Set(0.5f);
            }
        }

        if (_normalStarted && !_normalComplete)
        {
            // The visible tail stops attack movement, but gravity must still move the actor.
            if (!_finisherCommitted && _form is BaseForm normalForm
                && normalForm.IsCurrentActionExecutionComplete)
                _playerController.Controller.Move(Vector3.up * _playerController.VerticalVelocity * _playerController.ActionDeltaTime);
            _form.UpdateAttack(out _normalComplete);
        }
        else
            _playerController.Controller.Move(Vector3.up * _playerController.VerticalVelocity * _playerController.ActionDeltaTime);

        if (!HasQueuedSkill && !_finisherCommitted && _normalStarted && !_pending && input.AttackBuffer.IsActive)
        {
            if (_form.TryContinueAttack())
            {
                input.AttackBuffer.Consume();
                _normalComplete = false;
            }
            else if (_form is BaseForm form && form.CanRestartNormalCombo)
            {
                _form.EndAttack();
                BeginNormalAttack();
            }
        }
        IsAttackComplete = _normalStarted && _normalComplete && !_pending;
    }

    internal void BeginFinisherSelection(Vector3 forward)
    {
        CancelSkillReservation();
        _finisherOpened = true;
        _pending = _heavyRequested = _queuedHeavy = false;
        (_form as BaseForm)?.LockFinisherFacing(forward);
    }

    internal bool CommitFinisher(BaseForm target, Vector3 forward)
    {
        if (!IsFinisherWindowValid || target == null) return false;
        bool transformed = !ReferenceEquals(target, _form);
        if (!target.CanBeginFinisherWith(_playerController, forward, transformed)) return false;
        if (transformed)
        {
            // Validate the destination before ending the source action. This route has no E follow-up.
            if (!_playerController.FormManager.TrySwitchForFinisher(target)) return false;
            _form.EndAttack();
        }
        _form = target;
        _heavy = target as IHeavyAttackForm;
        if (!target.BeginFinisher(forward, transformed))
        {
            target.EndAttack();
            _normalStarted = _normalComplete = IsAttackComplete = true;
            _pending = _heavyRequested = _heavyStarted = _queuedHeavy = false;
            return false;
        }
        _normalStarted = true;
        _normalComplete = IsAttackComplete = false;
        _pending = _heavyRequested = _heavyStarted = _queuedHeavy = false;
        _finisherCommitted = true;
        _pressId = _playerController.Input.AttackPressId;
        _playerController.Input.AttackBuffer.Consume();
        return true;
    }

    private void CapturePress()
    {
        var input = _playerController.Input;
        _pressId = input.AttackPressId;
        _heldTime = input.AttackHeldTime;
        _pending = true;
        _heavyRequested = false;
        input.AttackBuffer.Consume();
    }

    private void BeginNormalAttack()
    {
        CancelSkillReservation();
        // A restart after the last normal hit is a new combo, including its finisher opportunity.
        _pending = _heavyRequested = _heavyStarted = _queuedHeavy = false;
        _finisherOpened = _finisherCommitted = false;
        _pressId = _playerController.Input.AttackPressId;
        _heldTime = 0f;
        _playerController.Input.AttackBuffer.Consume();
        _normalStarted = true;
        _normalComplete = IsAttackComplete = false;
        _form.BeginAttack();
    }

    public void Exit()
    {
        bool completed = IsAttackComplete;
        CancelSkillReservation();
        _playerController.CancelFinisherSelection();
        if (_heavyStarted) _heavy.EndHeavyAttack();
        else if (_normalStarted) _form.EndAttack();
        // Canceled charges cannot leak an attack into the next state.
        if (!completed) _playerController.Input.AttackBuffer.Consume();
        _pending = false;
        _queuedHeavy = false;
        _playerController.SetAnimationSpeed(1f);
    }
}
