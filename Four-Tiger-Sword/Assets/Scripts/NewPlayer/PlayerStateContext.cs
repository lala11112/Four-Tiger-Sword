using UnityEngine;

public class PlayerStateContext : MonoBehaviour
{
    public IPlayerState CurrentState;

    private readonly PlayerController _playerController;

    public PlayerStateContext(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void Transition(IPlayerState state)
    {
        CurrentState = state;
        //CurrentState.Handle(_playerController);
    }
}
