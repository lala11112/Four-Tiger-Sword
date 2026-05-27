using UnityEngine;

public class PlayerDashState : IPlayerState
{
    private PlayerController _playerController;
    private float _dashDuration = 0.2f;
    private float _dashTimer;
    private Vector3 _dashDirection;

    public bool IsDashComplete{ get; private set; }

    public PlayerDashState(PlayerController playerController) {_playerController = playerController;}

    public void Enter()
    {
        IsDashComplete = false;
        _dashTimer = 0;
        _playerController.Input.DashBuffer.Consume();
        _playerController.ConsumeStaminaForDash();

        if(_playerController.Input.MoveInput.sqrMagnitude > 0.01f)
        {
            _dashDirection = _playerController.Movement.GetMoveDirection();
            _dashDirection.y = 0;
            _dashDirection.Normalize();
        }

        else
        {
            _dashDirection = _playerController.transform.forward * -1f;
        }
    }

    public void Update()
    {
        _dashTimer += Time.deltaTime;
        if(_dashTimer >= _dashDuration)
        {
            IsDashComplete = true;
            _playerController.Movement.ApplyGravity();
            _playerController.Controller.Move(new Vector3(0, _playerController.VerticalVelocity, 0) * Time.deltaTime);
        }
        else
        {
            _playerController.Movement.ApplyGravity();
            Vector3 dashVelocity = _dashDirection * _playerController.DashSpeed;
            dashVelocity.y = _playerController.VerticalVelocity;
            _playerController.Controller.Move(dashVelocity * Time.deltaTime);
        }
    }

    public void Exit()
    {
        _playerController.StartDashCooldown();
    }
}
