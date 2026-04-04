using UnityEngine;

[RequireComponent(typeof(CharacterController), typeof(PlayerInputHandler))]
public class PlayerController : MonoBehaviour
{
    public CharacterController Controller{ get; private set; }
    public PlayerInputHandler Input{ get; private set; }

    [Header("Player Settings")]
    public float MoveSpeed = 5.0f;
    public float DashSpeed = 10.0f;
    public float JumpForce = 2.0f;
    public float Gravity = -9.81f;
    public float VerticalVelocity;

    private StateMachine _stateMachine;

    private void Awake()
    {
        Controller = GetComponent<CharacterController>();
        Input = GetComponent<PlayerInputHandler>();

        _stateMachine = new StateMachine();

        var idleState = new PlayerIdleState(this);
        var moveState = new PlayerMoveState(this);
        var jumpState = new PlayerJumpState(this);
        var dashState = new PlayerDashState(this);
        var attackState = new PlayerAttackState(this);

        _stateMachine.AddTransition(idleState, moveState, () => Input.MoveInput.sqrMagnitude > 0.01f);
        _stateMachine.AddTransition(moveState, idleState, () => Input.MoveInput.sqrMagnitude <= 0.01f);

        _stateMachine.AddTransition(idleState, jumpState, () => Input.JumpBuffer.IsActive && Controller.isGrounded);
        _stateMachine.AddTransition(moveState, jumpState, () => Input.JumpBuffer.IsActive && Controller.isGrounded);
        
        _stateMachine.AddTransition(jumpState, idleState, () => Controller.isGrounded && VerticalVelocity < 0f && Input.MoveInput.sqrMagnitude <= 0.01f);
        _stateMachine.AddTransition(jumpState, moveState, () => Controller.isGrounded && VerticalVelocity < 0f && Input.MoveInput.sqrMagnitude > 0.01f);

        _stateMachine.AddTransition(idleState, dashState, () => Input.IsDashPressed);
        _stateMachine.AddTransition(moveState, dashState, () => Input.IsDashPressed);
        
        _stateMachine.AddTransition(dashState, idleState, () => dashState.IsDashComplete && Input.MoveInput.sqrMagnitude <= 0.01f);
        _stateMachine.AddTransition(dashState, moveState, () => dashState.IsDashComplete && Input.MoveInput.sqrMagnitude > 0.01f);

        _stateMachine.AddTransition(idleState, attackState, () => Input.AttackBuffer.IsActive);
        _stateMachine.AddTransition(moveState, attackState, () => Input.AttackBuffer.IsActive);

        _stateMachine.AddTransition(attackState, idleState, () => Input.MoveInput.sqrMagnitude <= 0.01f);
        _stateMachine.AddTransition(attackState, moveState, () => Input.MoveInput.sqrMagnitude > 0.01f);

        _stateMachine.ChangeState(idleState);
    }

    private void Update()
    {
        _stateMachine.Update();
    }

    public void ApplyGravity()
    {
        if(Controller.isGrounded && VerticalVelocity < 0)
        {
            VerticalVelocity = -0.2f;
            
        }

        VerticalVelocity += Gravity * Time.deltaTime;
    }
}