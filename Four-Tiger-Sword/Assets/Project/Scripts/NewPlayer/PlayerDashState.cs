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

        if(_playerController.Input.MoveInput.sqrMagnitude > 0.01f)
        {
            _dashDirection = _playerController.Movement.GetMoveDirection();
            _dashDirection.y = 0;
            _dashDirection.Normalize();
        }

        else
        {
            _dashDirection = _playerController.transform.forward;
        }
    }

    public void Update()
    {
        _dashTimer += Time.deltaTime;
        if(_dashTimer >= _dashDuration)
        {
            IsDashComplete = true;
        }

        else
        {
            _playerController.Controller.Move(_dashDirection * _playerController.DashSpeed * Time.deltaTime);
        }
    }

    public void Exit(){}
}
