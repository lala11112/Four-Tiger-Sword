using UnityEngine;

public class PlayerDieState : IPlayerState
{
    internal static readonly int AnimationHash = Animator.StringToHash("Die");

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
        var animator = _playerController.Animator;
        if (animator != null && animator.isActiveAndEnabled && animator.layerCount > 0
            && animator.HasState(0, AnimationHash))
        {
            animator.applyRootMotion = true;
            animator.CrossFade(AnimationHash, 0.1f, 0, 0f);
        }
        if (_playerController.Controller != null) _playerController.Controller.enabled = false;
        if (_playerController.Input != null) _playerController.Input.enabled = false;
        if (_playerController.Movement != null) _playerController.Movement.enabled = false;
        _playerController.FormManager?.CleanupTransientEffects();
        _playerController.FormManager?.CurrentForm?.Unequip(_playerController);
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
