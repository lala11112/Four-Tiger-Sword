using UnityEngine;

public class PlayerJumpState : IPlayerState
{
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private PlayerController _playerController;

    public PlayerJumpState(PlayerController playerController) {_playerController = playerController;}

    public void Enter()
    {
        var animator = _playerController.Animator;
        if (animator != null)
        {
            animator.applyRootMotion = false;
            _playerController.SetAnimationSpeed(1f);
            if (animator.runtimeAnimatorController != null && animator.HasState(0, JumpHash))
                animator.CrossFadeInFixedTime(JumpHash, 0.025f, 0, 0f);
        }

        _playerController.Input.JumpBuffer.Consume();
        _playerController.ConsumeCoyote();
        _playerController.VerticalVelocity = Mathf.Sqrt(_playerController.JumpForce * -2f * _playerController.Gravity);
    }

    public void Update()
    {
        _playerController.Movement.OnAirMovement();
    }

    public void Exit(){}
}
