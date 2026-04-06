using UnityEngine;

public class PlayerStateMachineSetup : MonoBehaviour
{
    private readonly PlayerController _playerController;

    public PlayerStateMachineSetup(PlayerController playerController) {_playerController = playerController;}

    public StateMachine Build()
    {
        var stateMachine = new StateMachine();
        var idle = new PlayerIdleState(_playerController);
        var move = new PlayerMoveState(_playerController);
        var jump = new PlayerJumpState(_playerController);
        var fall = new PlayerFallState(_playerController);
        var dash = new PlayerDashState(_playerController);
        var attack = new PlayerAttackState(_playerController);

        GroundTransitions(stateMachine, idle, move, jump, fall, dash, attack);
        AirTransitions(stateMachine, idle, move, jump, fall);
        DashTransitions(stateMachine, idle, move, dash);
        AttackTransitions(stateMachine, idle, move, attack);

        stateMachine.ChangeState(idle);
        return stateMachine;    
    }

    //지상 이동
    private void GroundTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerMoveState move, PlayerJumpState jump, PlayerFallState fall, PlayerDashState dash, PlayerAttackState attack)
    {
        stateMachine.AddTransition(idle, move, () => _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
        stateMachine.AddTransition(move, idle, () => _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
    }

    //점프 및 공중
    private void AirTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerMoveState move, PlayerJumpState jump, PlayerFallState fall)
    {
        stateMachine.AddTransition(idle, jump, () => _playerController.Input.JumpBuffer.IsActive && _playerController.Controller.isGrounded);
        stateMachine.AddTransition(move, jump, () => _playerController.Input.JumpBuffer.IsActive && _playerController.Controller.isGrounded);
        stateMachine.AddTransition(idle, fall, () => !_playerController.Controller.isGrounded);
        stateMachine.AddTransition(move, fall, () => !_playerController.Controller.isGrounded);
        stateMachine.AddTransition(jump, fall, () => _playerController.VerticalVelocity < 0f);
        stateMachine.AddTransition(fall, idle, () => _playerController.Controller.isGrounded && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(fall, move, () => _playerController.Controller.isGrounded && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    //대시
    private void DashTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerMoveState move, PlayerDashState dash)
    {
        stateMachine.AddTransition(idle, dash, () => _playerController.Input.DashBuffer.IsActive);
        stateMachine.AddTransition(move, dash, () => _playerController.Input.DashBuffer.IsActive);
        stateMachine.AddTransition(dash, idle, () => dash.IsDashComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(dash, move, () => dash.IsDashComplete && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    private void AttackTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerMoveState move, PlayerAttackState attack)
    {
        stateMachine.AddTransition(idle, attack, () => _playerController.Input.AttackBuffer.IsActive);
        stateMachine.AddTransition(move, attack, () => _playerController.Input.AttackBuffer.IsActive);
        stateMachine.AddTransition(attack, idle, () => attack.IsAttackComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(attack, move, () => attack.IsAttackComplete && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }


}
