using UnityEngine;

public class PlayerIdleState : IPlayerState
{
    private PlayerController _playerController;

    public PlayerIdleState(PlayerController playerController) {_playerController = playerController;}
    public void Enter()
    {
        _playerController.Animator.applyRootMotion = false;
        _playerController.Animator.CrossFade("Idle", 0.1f);
    }

    public void Update()
    {
        _playerController.Movement.ApplyGravity();
        _playerController.Controller.Move(Vector3.up * _playerController.VerticalVelocity * Time.deltaTime);
    }

    public void Exit()
    {

    }
}
