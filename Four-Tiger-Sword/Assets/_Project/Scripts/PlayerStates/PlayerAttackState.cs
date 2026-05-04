using UnityEngine;

public class PlayerAttackState : IPlayerState
{
    private PlayerController _playerController;
    public bool IsAttackComplete { get; private set; }
    public PlayerAttackState(PlayerController playerController) {_playerController = playerController;}

    public void Enter()
    {
        //  _playerController.Animator.CrossFade("Punching", 0.01f);
        _playerController.Input.AttackBuffer.Consume();
        IsAttackComplete = false;
        _playerController.FormManager.CurrentForm.BeginAttack();
    }

    public void Update()
    {
        if (!_playerController.IsGround())
        {
            _playerController.Movement.ApplyGravity();
            _playerController.Controller.Move(new Vector3(0f, _playerController.VerticalVelocity, 0f) * Time.deltaTime);
        }

        _playerController.FormManager.CurrentForm.UpdateAttack(out bool isComplete);
        IsAttackComplete = isComplete;
    }

    public void Exit()
    {
        _playerController.FormManager.CurrentForm.EndAttack();
    }
}
