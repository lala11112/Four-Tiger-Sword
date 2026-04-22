using UnityEngine;

public class PlayerDieState : MonoBehaviour, IPlayerState
{
    private PlayerController _playerController;

    public PlayerDieState(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void Enter()
    {
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