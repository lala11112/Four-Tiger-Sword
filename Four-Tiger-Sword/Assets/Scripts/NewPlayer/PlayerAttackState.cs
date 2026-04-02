using UnityEngine;

public class PlayerAttackState : IPlayerState
{
    private PlayerController _playerController;

    public PlayerAttackState(PlayerController playerController) {_playerController = playerController;}

    public void Enter()
    {
        _playerController.Input.AttackBuffer.Consume();
    }

    public void Update()
    {
        Debug.Log("공격!");
    }

    public void Exit()
    {

    }
}
