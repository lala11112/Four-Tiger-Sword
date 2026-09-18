using UnityEngine;

public class PlayerMoveState : IPlayerState
{
    private PlayerController _playerController;

    public PlayerMoveState(PlayerController playerController) {_playerController = playerController;}

    public void Enter()
    {
        _playerController.Animator.applyRootMotion = false;
        _playerController.Animator.CrossFade("Walk", 0.01f);
    }

    public void Update()
    {
        _playerController.Movement.ApplyGravity();

        Vector3 moveDir = _playerController.Movement.GetMoveDirection();

        if(_playerController.IsGround())
        {
            moveDir = _playerController.Movement.GetDirectionSlope(moveDir);
        }

        Vector3 velocity = moveDir * _playerController.MoveSpeed;
        velocity.y += _playerController.VerticalVelocity;

        _playerController.Controller.Move(velocity * Time.deltaTime);

        if(moveDir != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(new Vector3(moveDir.x, 0, moveDir.z));
            _playerController.transform.rotation = Quaternion.Slerp(_playerController.transform.rotation, targetRotation, _playerController.RotateSpeed * Time.deltaTime);
        }
    }

    public void Exit(){}
}
