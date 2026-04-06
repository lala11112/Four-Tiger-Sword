using UnityEngine;

public class PlayerFallState : IPlayerState
{
    private PlayerController _playerController;

    public PlayerFallState(PlayerController playerController) {_playerController = playerController;}

    public void Enter(){}

    public void Update()
    {
        _playerController.Movement.OnAirMovement();
    }

    public void Exit(){}
}
