using UnityEngine;

public class PlayerFallState : IPlayerState
{
    private static readonly int JumpAirHash = Animator.StringToHash("Jump_Air");
    private PlayerController _playerController;

    public PlayerFallState(PlayerController playerController) {_playerController = playerController;}

    public void Enter()
    {
        var animator = _playerController.Animator;
        if (animator == null) return;

        animator.applyRootMotion = false;
        _playerController.SetAnimationSpeed(1f);
        if (animator.runtimeAnimatorController == null || !animator.HasState(0, JumpAirHash)) return;

        // The jump animation can enter the air pose before the player starts descending.
        var state = animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0)
            : animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != JumpAirHash)
            animator.CrossFadeInFixedTime(JumpAirHash, 0.05f, 0, 0f);
    }

    public void Update()
    {
        _playerController.Movement.OnAirMovement();
    }

    public void Exit(){}
}
