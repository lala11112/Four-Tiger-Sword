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
    private uint _pressId;
    private float _heldTime;
    private bool _isAttackComplete;
    public bool IsAttackComplete
    {
        // Transitions run before Update: accept a fresh press on the completion frame too.
        get => _isAttackComplete && !(_heavyStarted && _heavy.CanHeavyAttack
            && _playerController.Input.AttackBuffer.IsActive
            && _playerController.Input.AttackPressId != _pressId);
        private set => _isAttackComplete = value;
    }
    public bool IsCharging => _pending && (!_normalStarted || _normalComplete);
    public bool IsHeavyAttack => _heavyStarted;
    public bool CanSelectFinisher => !_finisherOpened && !_heavyStarted && _normalStarted
        && !_normalComplete && _form is BaseForm form && form.CanSelectFinisher;
    public bool IsFinisherWindowValid => !_heavyStarted && _normalStarted && !_normalComplete
        && _form is BaseForm form && form.CanSelectFinisher;

    public PlayerAttackState(PlayerController playerController) => _playerController = playerController;

    public void Enter()
    {
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

        if (!_finisherCommitted && !_pending && _normalStarted && input.AttackBuffer.IsActive
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
            _form.UpdateAttack(out _normalComplete);
        else
            _playerController.Controller.Move(Vector3.up * _playerController.VerticalVelocity * _playerController.ActionDeltaTime);

        if (!_finisherCommitted && _normalStarted && !_pending && input.AttackBuffer.IsActive && _form.TryContinueAttack())
        {
            input.AttackBuffer.Consume();
            _normalComplete = false;
        }
        IsAttackComplete = _normalStarted && _normalComplete && !_pending;
    }

    internal void BeginFinisherSelection(Vector3 forward)
    {
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
        _pending = false;
        _playerController.Input.AttackBuffer.Consume();
        _normalStarted = true;
        _normalComplete = false;
        _form.BeginAttack();
    }

    public void Exit()
    {
        _playerController.CancelFinisherSelection();
        if (_heavyStarted) _heavy.EndHeavyAttack();
        else if (_normalStarted) _form.EndAttack();
        // Canceled charges cannot leak an attack into the next state.
        if (!IsAttackComplete) _playerController.Input.AttackBuffer.Consume();
        _pending = false;
        _queuedHeavy = false;
        _playerController.SetAnimationSpeed(1f);
    }
}
