using UnityEngine;

public class PlayerAirAttackState : IPlayerState
{
    private const float BlockedDescentDuration = 0.15f;
    private const float BlockedDescentMovementRatio = 0.1f;
    private readonly PlayerController _playerController;
    private enum Phase { Start, Loop, Finish }
    private Phase _phase;
    private LayerMask _savedExcludeLayers;
    private bool _collisionOverrideActive;
    private float _blockedDescentTime;

    public bool IsComplete { get; private set; }
    public bool IsFinishing => _phase == Phase.Finish;

    public PlayerAirAttackState(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void Enter()
    {
        IsComplete = false;
        _phase = Phase.Start;
        _blockedDescentTime = 0f;
        _playerController.Input.AttackBuffer.Consume();
        _playerController.VerticalVelocity = 0f;
        _savedExcludeLayers = _playerController.Controller.excludeLayers;
        _playerController.Controller.excludeLayers |= LayerMask.GetMask("Enemy");
        _collisionOverrideActive = true;
        _playerController.FormManager.CurrentForm.BeginAirAttack();
    }

    public void Update()
    {
        if (_phase == Phase.Start)
        {
            // 시작 모션 동안 공중에 머문 뒤 하강합니다.
            _playerController.FormManager.CurrentForm.UpdateAirAttackStart(out bool startComplete);
            if (!startComplete) return;

            _phase = Phase.Loop;
            _playerController.VerticalVelocity = -_playerController.AirAttackDescentSpeed;
            _playerController.FormManager.CurrentForm.BeginAirAttackLoop();
        }

        if (_phase == Phase.Loop)
        {
            UpdateDescent(_playerController.ActionDeltaTime);
        }
        else
        {
            _playerController.FormManager.CurrentForm.UpdateAirAttack(out bool isComplete);
            if (isComplete || _playerController.FormManager.CurrentForm is BaseForm form
                && form.IsCurrentActionExecutionComplete)
                RestoreCollision();
            IsComplete = isComplete;
        }
    }

    public void Exit()
    {
        _playerController.FormManager.CurrentForm.EndAirAttack();
        RestoreCollision();
    }

    private void UpdateDescent(float deltaTime)
    {
        if (_phase != Phase.Loop) return;

        float requestedDescent = -_playerController.VerticalVelocity * deltaTime;
        float previousY = _playerController.transform.position.y;
        CollisionFlags flags = CollisionFlags.None;
        if (deltaTime > 0f && requestedDescent > 0f)
            flags = _playerController.Controller.Move(Vector3.down * requestedDescent);

        // Physical support can be a stair edge or a prop outside the Ground layer.
        if ((flags & CollisionFlags.Below) != 0 || _playerController.IsGround()
            || UpdateBlockedDescent(flags, requestedDescent,
                _playerController.transform.position.y - previousY, deltaTime))
            BeginFinish();
    }

    private bool UpdateBlockedDescent(CollisionFlags flags, float requestedDescent,
        float actualVerticalMovement, float deltaTime)
    {
        // Paused actor time and moves too small for the controller are not evidence of a snag.
        if (deltaTime <= 0f || requestedDescent <= 0f
            || requestedDescent < _playerController.Controller.minMoveDistance)
            return false;

        bool isBlocked = (flags & CollisionFlags.Sides) != 0
            && Mathf.Abs(actualVerticalMovement) <= requestedDescent * BlockedDescentMovementRatio;
        _blockedDescentTime = isBlocked ? _blockedDescentTime + deltaTime : 0f;
        return _blockedDescentTime >= BlockedDescentDuration;
    }

    private void BeginFinish()
    {
        if (_phase != Phase.Loop) return;
        _phase = Phase.Finish;
        _blockedDescentTime = 0f;
        _playerController.VerticalVelocity = 0f;
        _playerController.FormManager.CurrentForm.BeginAirAttackFinish();
    }

    private void RestoreCollision()
    {
        if (!_collisionOverrideActive) return;
        _playerController.Controller.excludeLayers = _savedExcludeLayers;
        _collisionOverrideActive = false;
    }
}
