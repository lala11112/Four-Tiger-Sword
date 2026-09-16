using UnityEngine;

public class PlayerDieState : IPlayerState
{
    private PlayerController _playerController;

    public PlayerDieState(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void Enter()
    {
        _playerController.Animator.applyRootMotion = true;
        _playerController.Animator.CrossFade("Die", 0.1f);
        _playerController.Controller.enabled = false;
        _playerController.Input.enabled = false;
        _playerController.Movement.enabled = false;
        _playerController.FormManager.CurrentForm.Unequip(_playerController);
        Debug.Log("Player Die");
    }

    public void Update()
    {
        
    }

    public void Exit()
    {

    }
}
