using UnityEngine;
using System.Collections;

[RequireComponent(typeof(CharacterController), typeof(PlayerInputHandler), typeof(PlayerMovement))]
public class PlayerController : MonoBehaviour, IDamageable
{
    public CharacterController Controller { get; private set; }
    public PlayerInputHandler Input { get; private set; }
    public PlayerMovement Movement { get; private set; }
    public FormManager FormManager { get; private set; }
    public StateMachine StateMachine { get; private set; }
    public Transform CameraTransform;

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
    public bool CanDash => _dashCooldownTimer <= 0f && HasEnoughStaminaForDash;
    public bool IsDashing => StateMachine.CurrentState is PlayerDashState; //대쉬중일 때는 무적

    [Header("Stamina Settings")]
    public float MaxStamina = 100f;
    public float DashStaminaCost = 25f;
    public float RunStaminaCostPerSecond = 10f;
    public float StaminaRegenDelay = 2f;
    public float StaminaRegenRate = 20f;
    private float _currentStamina;
    private float _staminaRegenTimer;

    public float CurrentStamina => _currentStamina;
    public bool HasEnoughStaminaForDash => _currentStamina >= DashStaminaCost;
    public bool CanRun => _currentStamina > 0f;

    // CanSkill / CanUltimate 는 현재 폼의 쿨타임과 SP를 함께 검사합니다.
    public bool CanSkill => FormManager?.CurrentForm?.CanSkill ?? false;
    public bool CanUltimate => FormManager?.CurrentForm?.CanUltimate ?? false;

    [Header("SP Settings")]
    public float MaxSP = 1000f;
    public float SpRegenRate = 50f;   // 초당 회복량
    public float SpRegenDelay = 2f;    // 소모 후 회복 대기 시간(초)
    public PlayerStat Stat { get; private set; }

    [Header("CombatData")]
    [SerializeField] private WeaponActionDataSO _FireFormActionData;
    [SerializeField] private WeaponActionDataSO _WaterFormActionData;
    [SerializeField] private WeaponActionDataSO _WoodFormActionData;
    [SerializeField] private WeaponActionDataSO _IronFormActionData;
    [SerializeField] private WeaponActionDataSO _EarthFormActionData;

    [Tooltip("넉백 저항값. 이 값보다 작은 힘은 밀리지 않음")]
    [SerializeField] private float _knockbackResistance = 3f;
    [Tooltip("넉백 지속 시간 (초)")]
    [SerializeField] private float _knockbackDuration = 0.25f;
    private Coroutine _knockbackCoroutine;

    public Animator Animator { get; private set; }

    public void SetAnimatorTrigger(string triggerName)
    {
        foreach (var param in Animator.parameters)
        {
            if (param.type == AnimatorControllerParameterType.Trigger)
                Animator.ResetTrigger(param.name);
        }
        Animator.SetTrigger(triggerName);
    }


    private void Awake()
    {
        Animator = gameObject.GetComponentInChildren<Animator>();

        Controller = GetComponent<CharacterController>();
        Input = GetComponent<PlayerInputHandler>();
        CameraTransform = Camera.main.transform;
        Movement = GetComponent<PlayerMovement>();
        Movement.Initialize(this);
        _currentStamina = MaxStamina;
        Stat = new PlayerStat(maxHp: 100000f, maxSp: MaxSP, spRegenRate: SpRegenRate, spRegenDelay: SpRegenDelay, playerController: this);

        StateMachine = new StateMachine();
        var stateMachineSetup = new PlayerStateMachineSetup(this);
        StateMachine = stateMachineSetup.Build();

        var formManagerSetup = new FormManagerSetup(this);
        FormManager = formManagerSetup.Build(_FireFormActionData, _WaterFormActionData, _WoodFormActionData, _IronFormActionData, _EarthFormActionData);

    }

    private void Start()
    {
        DamageTextManager.Instance?.Register(this);
    }

    private void Update()
    {
        UpdateCoyoteTimer();
        UpdateDashCooldown();
        UpdateStamina();
        Stat.UpdateSpRegen(Time.deltaTime);
        StateMachine.Update();
        FormManager.Update();
        //Debug.Log(StateMachine.CurrentState.GetType().Name);
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

    private void UpdateStamina()
    {
        if (_staminaRegenTimer > 0f)
        {
            _staminaRegenTimer -= Time.deltaTime;
            return;
        }

        if (_currentStamina < MaxStamina)
            _currentStamina = Mathf.Min(_currentStamina + StaminaRegenRate * Time.deltaTime, MaxStamina);
    }

    public void ConsumeStaminaForDash()
    {
        _currentStamina = Mathf.Max(0f, _currentStamina - DashStaminaCost);
        _staminaRegenTimer = StaminaRegenDelay;
    }

    public void ConsumeStaminaForRun()
    {
        _currentStamina = Mathf.Max(0f, _currentStamina - RunStaminaCostPerSecond * Time.deltaTime);
        _staminaRegenTimer = StaminaRegenDelay;
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

    public bool IsGround()
    {
        Vector3 sphereCenter = transform.position + Controller.center + Vector3.down * (Controller.height / 2f - Controller.radius);
        return Physics.CheckSphere(sphereCenter, Controller.radius + 0.1f, LayerMask.GetMask("Ground"));
    }

    public void TakeDamage(int damage, DamageType damageType = DamageType.Normal, bool isCritical = false, Vector3 power = default, float poiseDamage = 20f)
    {
        Debug.Log("플레이어 피격!");
        Stat.TakeDamage(damage, damageType, isCritical, power, poiseDamage);
        ApplyKnockback(power);
    }

    private void ApplyKnockback(Vector3 power)
    {
        float effectiveForce = power.magnitude - _knockbackResistance;
        if (effectiveForce <= 0f) return;

        Vector3 velocity = power.normalized * effectiveForce;

        if (_knockbackCoroutine != null)
            StopCoroutine(_knockbackCoroutine);

        _knockbackCoroutine = StartCoroutine(KnockbackRoutine(velocity));
    }

    /// <summary>
    /// 초기 속도에서 0으로 감속하며 NavMesh 위에서 적을 밀어냅니다.
    /// </summary>
    private IEnumerator KnockbackRoutine(Vector3 initialVelocity)
    {
        // y를 제거하되 원래 속력(magnitude)은 XZ 평면에서 그대로 유지
        float magnitude = initialVelocity.magnitude;
        initialVelocity.y = 0f;
        if (initialVelocity != Vector3.zero)
            initialVelocity = initialVelocity.normalized * magnitude;

        float elapsed = 0f;
        while (elapsed < _knockbackDuration)
        {
            float t = 1f - (elapsed / _knockbackDuration); // 선형 감속
            Vector3 delta = initialVelocity * t * Time.deltaTime;
            transform.position += delta;

            elapsed += Time.deltaTime;
            yield return null;
        }

        _knockbackCoroutine = null;
    }
}