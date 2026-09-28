using UnityEngine;

public class PlayerAttackState : IPlayerState
{
    private readonly PlayerController _playerController;
    private IForm _form;
    private IHeavyAttackForm _heavy;
    private bool _normalStarted, _normalComplete, _heavyStarted;
    private bool _pending, _heavyRequested;
    private bool _queuedHeavy;
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

    public PlayerAttackState(PlayerController playerController) => _playerController = playerController;

    public void Enter()
    {
        _form = _playerController.FormManager.CurrentForm;
        _heavy = _form as IHeavyAttackForm;
        _normalStarted = _normalComplete = _heavyStarted = false;
        _pending = _heavyRequested = IsAttackComplete = false;
        _queuedHeavy = false;
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

        if (!_pending && _normalStarted && input.AttackBuffer.IsActive
            && input.AttackPressId != _pressId && _heavy?.CanHeavyAttack == true)
            CapturePress();

        if (_normalStarted && !_normalComplete)
            _form.UpdateAttack(out _normalComplete);
        else
            _playerController.Controller.Move(Vector3.up * _playerController.VerticalVelocity * Time.deltaTime);

        if (_pending)
        {
            bool samePress = input.AttackPressId == _pressId;
            if (samePress) _heldTime = input.AttackHeldTime;
            _heavyRequested |= _heldTime >= _heavy.HeavyAttackHoldTime;
            if (!_heavy.CanHeavyAttack) _heavyRequested = false;

            if (_heavyRequested)
            {
                if (!_normalStarted || _normalComplete || _form.CanChainAttack)
                {
                    if (_normalStarted) _form.EndAttack();
                    if (_heavy.TryBeginHeavyAttack())
                    {
                        _pending = false;
                        _heavyStarted = true;
                        return;
                    }
                    // The resource may have become unavailable before execution.
                    _normalStarted = false;
                    BeginNormalAttack();
                }
            }
            else if (!_heavy.CanHeavyAttack || !samePress || !input.IsAttackHeld)
            {
                _pending = false;
                if (!_normalStarted) BeginNormalAttack();
                else input.AttackBuffer.Set(0.5f);
            }
        }

        if (_normalStarted && !_pending && input.AttackBuffer.IsActive && _form.TryContinueAttack())
        {
            input.AttackBuffer.Consume();
            _normalComplete = false;
        }
        IsAttackComplete = _normalStarted && _normalComplete && !_pending;
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
        if (_heavyStarted) _heavy.EndHeavyAttack();
        else if (_normalStarted) _form.EndAttack();
        // Canceled charges cannot leak an attack into the next state.
        if (!IsAttackComplete) _playerController.Input.AttackBuffer.Consume();
        _pending = false;
        _queuedHeavy = false;
        _playerController.Animator.speed = 1f;
    }
}
