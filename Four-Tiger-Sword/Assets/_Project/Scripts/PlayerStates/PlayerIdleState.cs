using UnityEngine;

public class PlayerIdleState : IPlayerState
{
    private static readonly int JumpLandHash = Animator.StringToHash("Jump_Land");
    private PlayerController _playerController;
    private readonly bool _playLandingAnimation;

    public PlayerIdleState(PlayerController playerController, bool playLandingAnimation = false)
    {
        _playerController = playerController;
        _playLandingAnimation = playLandingAnimation;
    }

    public void Enter()
    {
        var animator = _playerController.Animator;
        if (animator == null) return;

        animator.applyRootMotion = false;
        if (_playLandingAnimation && animator.runtimeAnimatorController != null && animator.HasState(0, JumpLandHash))
            animator.CrossFadeInFixedTime(JumpLandHash, 0.05f, 0, 0f);
        else
            animator.CrossFade("Idle", 0.1f);
    }

    public void Update()
    {
        _playerController.Movement.ApplyGravity();
        _playerController.Controller.Move(Vector3.up * _playerController.VerticalVelocity * _playerController.ActionDeltaTime);
    }

    public void Exit()
    {

    }
}
