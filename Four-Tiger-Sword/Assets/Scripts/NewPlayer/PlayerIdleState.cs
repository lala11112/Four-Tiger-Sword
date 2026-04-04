using UnityEngine;

public class PlayerIdleState : MonoBehaviour, IPlayerState
{
    private PlayerController _playerController;

    public PlayerIdleState(PlayerController playerController) {_playerController = playerController;}
    public void Enter()
    {

    }

    public void Update()
    {
        _playerController.ApplyGravity();
        _playerController.Controller.Move(new Vector3(0, _playerController.VerticalVelocity, 0) * Time.deltaTime);
    }

    public void Exit()
    {

    }
}
