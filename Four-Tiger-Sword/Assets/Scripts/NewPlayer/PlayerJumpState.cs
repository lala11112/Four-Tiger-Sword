using UnityEngine;

public class PlayerJumpState : IPlayerState
{
    private PlayerController _playerController;

    public PlayerJumpState(PlayerController playerController) {_playerController = playerController;}

    public void Enter()
    {
        _playerController.Input.JumpBuffer.Consume();
        _playerController.VerticalVelocity = Mathf.Sqrt(_playerController.JumpForce * -2f * _playerController.Gravity);
    }

    public void Update()
    {
        _playerController.ApplyGravity();
        
        Vector3 moveDir = new Vector3(_playerController.Input.MoveInput.x, 0, _playerController.Input.MoveInput.y);
        Vector3 velocity = moveDir * _playerController.MoveSpeed;
        velocity.y = _playerController.VerticalVelocity;

        _playerController.Controller.Move(velocity * Time.deltaTime);
    }

    public void Exit(){}
}
