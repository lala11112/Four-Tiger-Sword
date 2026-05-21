using UnityEngine;

public class PlayerAirAttackState : IPlayerState
{
    private readonly PlayerController _playerController;
    private bool _isDescending;

    public bool IsComplete { get; private set; }

    public PlayerAirAttackState(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void Enter()
    {
        IsComplete = false;
        _isDescending = true;
        _playerController.Input.AttackBuffer.Consume();
        _playerController.VerticalVelocity = -_playerController.AirAttackDescentSpeed;
        _playerController.Controller.excludeLayers |= 1 << LayerMask.NameToLayer("Enemy");

    }

    public void Update()
    {
        if (_isDescending)
        {
            _playerController.Controller.Move(new Vector3(0f, _playerController.VerticalVelocity, 0f) * Time.deltaTime);

            if (_playerController.IsGround())
            {
                _isDescending = false;
                _playerController.VerticalVelocity = 0f;
                _playerController.FormManager.CurrentForm.BeginAirAttack();
            }
        }
        else
        {
            _playerController.FormManager.CurrentForm.UpdateAirAttack(out bool isComplete);
            IsComplete = isComplete;
        }
    }

    public void Exit()
    {
        _playerController.FormManager.CurrentForm.EndAirAttack();
        _playerController.Controller.excludeLayers &= ~(1 << LayerMask.NameToLayer("Enemy"));

    }
}
