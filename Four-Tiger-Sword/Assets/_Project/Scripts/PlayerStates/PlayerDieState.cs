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
        _playerController.CancelHitStops();
        _playerController.SetAnimationSpeed(1f);
        _playerController.CancelPendingSwitchAttack();
        _playerController.ItemRuntime?.ClearTimedEffects();
        _playerController.Animator.applyRootMotion = true;
        _playerController.Animator.CrossFade("Die", 0.1f);
        _playerController.Controller.enabled = false;
        _playerController.Input.enabled = false;
        _playerController.Movement.enabled = false;
        _playerController.FormManager.CleanupTransientEffects();
        _playerController.FormManager.CurrentForm.Unequip(_playerController);
        Debug.Log("Player Die");
        _playerController.NotifyDied();
    }

    public void Update()
    {
        
    }

    public void Exit()
    {

    }
}
