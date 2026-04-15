using UnityEngine;

public class PlayerIdleState : IPlayerState
{
    private PlayerController _playerController;

    public PlayerIdleState(PlayerController playerController) {_playerController = playerController;}
    public void Enter()
    {

    }

    public void Update()
    {
        _playerController.Movement.ApplyGravity();
    }

    public void Exit()
    {

    }
}
