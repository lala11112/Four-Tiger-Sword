using UnityEngine;

public class PlayerMoveState : IPlayerState
{
    private PlayerController _playerController;

    public PlayerMoveState(PlayerController playerController) {_playerController = playerController;}

    public void Enter(){}

    public void Update()
    {
        _playerController.ApplyGravity();

        Vector3 moveDir = new Vector3(_playerController.Input.MoveInput.x, 0, _playerController.Input.MoveInput.y);

        Vector3 velocity = moveDir * _playerController.MoveSpeed;
        velocity.y = _playerController.VerticalVelocity;

        _playerController.Controller.Move(velocity * Time.deltaTime);

        if(moveDir != Vector3.zero)
        {
            _playerController.transform.rotation = Quaternion.Slerp(_playerController.transform.rotation, Quaternion.LookRotation(moveDir), 0.15f); //_playerController.ratateSpeed 추가 가능
        }
    }

    public void Exit(){}
}
