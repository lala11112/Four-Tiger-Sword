using UnityEngine;

public class PlayerDashState : IPlayerState
{
    private PlayerController _playerController;
    private float _dashDuration = 0.2f;
    private float _dashTimer;
    private Vector3 _dashDirection;
    private bool _started;
    private bool _successfulDodge;

    public bool IsDashComplete{ get; private set; }

    public PlayerDashState(PlayerController playerController) {_playerController = playerController;}

    public void Enter()
    {
        IsDashComplete = false;
        _successfulDodge = false;
        _dashTimer = 0;
        _playerController.Input.DashBuffer.Consume();
        _started = _playerController.TryBeginDash();
        if (!_started) { IsDashComplete = true; return; }

        // Use the same entry-time input for both movement and animation, including backward input.
        bool hasMoveInput = _playerController.Input.MoveInput.sqrMagnitude > 0.01f;
        if(hasMoveInput)
        {
            _dashDirection = _playerController.Movement.GetMoveDirection();
            _dashDirection.y = 0;
            _dashDirection.Normalize();
        }

        else
        {
            _dashDirection = _playerController.transform.forward * -1f;
        }

        _playerController.SetAnimationSpeed(1f);
        if (_playerController.Animator != null)
        {
            _playerController.Animator.applyRootMotion = false;
            string animationName = hasMoveInput
                ? _playerController.DashAnimationName
                : _playerController.BackstepAnimationName;
            if (!TryPlayAnimation(animationName)) TryPlayAnimation("Idle");
        }
    }

    private bool TryPlayAnimation(string stateName)
    {
        if (string.IsNullOrWhiteSpace(stateName)) return false;
        var animator = _playerController.Animator;
        if (animator.runtimeAnimatorController == null) return false;
        int hash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, hash)) return false;
        animator.CrossFade(hash, 0.05f, 0, 0f);
        return true;
    }

    public void Update()
    {
        if (!_started) return;
        _dashTimer += _playerController.ActionDeltaTime;
        if(_dashTimer >= _dashDuration)
        {
            IsDashComplete = true;
            _playerController.Movement.ApplyGravity();
            _playerController.Controller.Move(new Vector3(0, _playerController.VerticalVelocity, 0) * _playerController.ActionDeltaTime);
        }
        else
        {
            _playerController.Movement.ApplyGravity();
            Vector3 dashVelocity = _dashDirection * _playerController.DashSpeed;
            dashVelocity.y = _playerController.VerticalVelocity;
            _playerController.Controller.Move(dashVelocity * _playerController.ActionDeltaTime);
        }
    }

    public void Exit()
    {
        if (_started) _playerController.StartDashCooldown();
    }

    /// <summary>Only the first real attack avoided during this dash readies the water chain.</summary>
    public bool TryConfirmSuccessfulDodge()
    {
        if (!_started || IsDashComplete || _successfulDodge) return false;
        _successfulDodge = true;
        return true;
    }
}
