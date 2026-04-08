using UnityEngine;

public class PlayerAttackState : IPlayerState
{
    private PlayerController _playerController;
    public bool IsAttackComplete { get; private set; }
    public PlayerAttackState(PlayerController playerController) {_playerController = playerController;}

    public void Enter()
    {
        _playerController.Input.AttackBuffer.Consume();
        IsAttackComplete = false;
        _playerController.FormManager.CurrentForm.BeginAttack();
    }

    public void Update()
    {
        _playerController.FormManager.CurrentForm.UpdateAttack(out bool isComplete);
        IsAttackComplete = isComplete;
    }

    public void Exit()
    {
        _playerController.FormManager.CurrentForm.EndAttack();
    }
}
