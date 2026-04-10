using UnityEngine;

public class PlayerJumpState : IPlayerState
{
    private PlayerController _playerController;

    public PlayerJumpState(PlayerController playerController) {_playerController = playerController;}

    public void Enter()
    {
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
