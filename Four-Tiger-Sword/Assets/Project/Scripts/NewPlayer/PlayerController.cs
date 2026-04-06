using UnityEngine;

[RequireComponent(typeof(CharacterController), typeof(PlayerInputHandler), typeof(PlayerMovement))]
public class PlayerController : MonoBehaviour
{
    public CharacterController Controller{ get; private set; }
    public PlayerInputHandler Input{ get; private set; }
    public PlayerMovement Movement{ get; private set; }
    public FormManager FormManager{ get; private set; }

    [Header("Player Settings")]
    public float MoveSpeed = 5.0f;
    public float RunSpeed = 7.0f;
    public float DashSpeed = 10.0f;
    public float JumpForce = 2.0f;
    public float Gravity = -9.81f;
    public float VerticalVelocity;
    public float RotateSpeed = 0.15f;    

    [Header("CombatData")]
    [SerializeField] private WeaponActionDataSO _FireFormActionData;
    [SerializeField] private WeaponActionDataSO _WaterFormActionData;
    
    private StateMachine _stateMachine;

    private FormManager _formManager;

    public Transform CameraTransform;

    private void Awake()
    {
        Controller = GetComponent<CharacterController>();
        Input = GetComponent<PlayerInputHandler>();
        CameraTransform = Camera.main.transform;
        Movement = GetComponent<PlayerMovement>();
        Movement.Initialize(this);

        _stateMachine = new StateMachine();
        var stateMachineSetup = new PlayerStateMachineSetup(this);
        _stateMachine = stateMachineSetup.Build();

        FormManager = new FormManager(this);
        FormManagerSetup();

    }

    private void Update()
    {
        _stateMachine.Update();
        FormManager.Update();
    }

    private void FormManagerSetup()
    {
        var fireForm = new FireForm(_FireFormActionData);
        var waterForm = new WaterForm(_WaterFormActionData);

        FormManager.CanTransition = ()=> _stateMachine.CurrentState is PlayerIdleState || _stateMachine.CurrentState is PlayerMoveState;
        FormManager.AddTransition(fireForm, () => Input.IsForm1Pressed);
        FormManager.AddTransition(waterForm, () => Input.IsForm2Pressed);
        FormManager.ChangeForm(fireForm);
    }
}