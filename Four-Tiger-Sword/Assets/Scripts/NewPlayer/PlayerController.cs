using UnityEngine;

[RequireComponent(typeof(CharacterController), typeof(PlayerInputHandler), typeof(PlayerMovement))]
public class PlayerController : MonoBehaviour
{
    public CharacterController Controller { get; private set; }
    public PlayerInputHandler Input { get; private set; }
    public PlayerMovement Movement { get; private set; }
    public FormManager FormManager { get; private set; }

    [Header("Player Settings")]
    public float MoveSpeed = 5.0f;
    public float RunSpeed = 7.0f;
    public float DashSpeed = 10.0f;
    public float JumpForce = 2.0f;
    public float Gravity = -9.81f;
    public float VerticalVelocity;
    public float RotateSpeed = 0.15f;

    [Header("Jump Settings")]
    public float CoyoteTime = 0.15f;
    private float _coyoteTimer;
    public float AirAttackDescentSpeed = 25f;

    [Header("Dash Settings")]
    public float DashCooldown = 1.0f;
    private float _dashCooldownTimer;
    public bool CanDash => _dashCooldownTimer <= 0f;

    [Header("Skill Settings")]
    public float SkillCooldown = 5.0f;
    private float _skillCooldownTimer;
    public bool CanSkill => _skillCooldownTimer <= 0f;

    [Header("Ultimate Settings")]
    public float UltimateCooldown = 30.0f;
    private float _ultimateCooldownTimer;
    public bool CanUltimate => _ultimateCooldownTimer <= 0f;

    [Header("CombatData")]
    [SerializeField] private WeaponActionDataSO _FireFormActionData;
    [SerializeField] private WeaponActionDataSO _WaterFormActionData;

    public StateMachine StateMachine { get; private set; }

    private FormManager _formManager;

    public Transform CameraTransform;

    private void Awake()
    {
        Controller = GetComponent<CharacterController>();
        Input = GetComponent<PlayerInputHandler>();
        CameraTransform = Camera.main.transform;
        Movement = GetComponent<PlayerMovement>();
        Movement.Initialize(this);

        StateMachine = new StateMachine();
        var stateMachineSetup = new PlayerStateMachineSetup(this);
        StateMachine = stateMachineSetup.Build();

        FormManager = new FormManager(this);
        FormManagerSetup();

    }

    private void Update()
    {
        UpdateCoyoteTimer();
        UpdateDashCooldown();
        UpdateSkillCooldown();
        UpdateUltimateCooldown();
        StateMachine.Update();
        FormManager.Update();
    }

    private void UpdateCoyoteTimer()
    {
        if (IsGround())
            _coyoteTimer = CoyoteTime;
        else
            _coyoteTimer -= Time.deltaTime;
    }

    private void UpdateDashCooldown()
    {
        if (_dashCooldownTimer > 0f)
            _dashCooldownTimer -= Time.deltaTime;
    }

    private void UpdateSkillCooldown()
    {
        if (_skillCooldownTimer > 0f)
            _skillCooldownTimer -= Time.deltaTime;
    }

    private void UpdateUltimateCooldown()
    {
        if (_ultimateCooldownTimer > 0f)
            _ultimateCooldownTimer -= Time.deltaTime;
    }

    private void FormManagerSetup()
    {
        var fireForm = new FireForm(_FireFormActionData);
        var waterForm = new WaterForm(_WaterFormActionData);

        FormManager.CanTransition = () => StateMachine.CurrentState is PlayerIdleState || StateMachine.CurrentState is PlayerMoveState;
        FormManager.AddTransition(fireForm, () => Input.IsForm1Pressed);
        FormManager.AddTransition(waterForm, () => Input.IsForm2Pressed);
        FormManager.ChangeForm(fireForm);
    }

    private void OnDrawGizmos()
    {
        if (Application.isPlaying && FormManager != null && FormManager.CurrentForm != null)
        {
            // 현재 장착된 폼의 'DrawHitboxGizmo' 메서드를 호출합니다.
            FormManager.CurrentForm.DrawHitboxGizmo();
        }
    }

    public bool CanJump() => IsGround() || _coyoteTimer > 0f;
    public void ConsumeCoyote() => _coyoteTimer = 0f;
    public void StartDashCooldown() => _dashCooldownTimer = DashCooldown;
    public void StartSkillCooldown() => _skillCooldownTimer = SkillCooldown;
    public void StartUltimateCooldown() => _ultimateCooldownTimer = UltimateCooldown;

    public bool IsGround()
    {
        Vector3 sphereCenter = transform.position + Controller.center + Vector3.down * (Controller.height / 2f - Controller.radius);
        return Physics.CheckSphere(sphereCenter, Controller.radius + 0.1f, LayerMask.GetMask("Ground"));
    }
}