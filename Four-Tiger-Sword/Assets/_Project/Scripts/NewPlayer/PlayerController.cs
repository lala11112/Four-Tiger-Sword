using UnityEngine;
using System.Collections;
using Unity.Cinemachine;

[RequireComponent(typeof(CharacterController), typeof(PlayerInputHandler), typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerStatManager))]
public class PlayerController : MonoBehaviour, IDamageable
{
    public CharacterController Controller { get; private set; }
    public PlayerInputHandler Input { get; private set; }
    public PlayerMovement Movement { get; private set; }
    public FormManager FormManager { get; private set; }
    public StateMachine StateMachine { get; private set; }
    public PlayerStatManager StatManager { get; private set; }
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
    public bool CanDash => _dashCooldownTimer <= 0f && StatManager.HasEnoughStaminaForDash;
    public bool IsDashing => StateMachine.CurrentState is PlayerDashState;

    [Header("Parry Settings")]
    public float ParryCooldown            = 0.8f;
    [Tooltip("패링 성공 시 필살기 게이지 충전량")]
    public float ParryUltimateGaugeReward = 30f;
    [Tooltip("퍼펙트 패링 추가 필살기 게이지 (일반 보상에 더해짐)")]
    public float PerfectParryUltimateGaugeBonus = 15f;
    private float _parryCooldownTimer;

    [Header("Perfect Parry Hit Stop")]
    [Tooltip("퍼펙트 패링 히트스탑 지속 시간 (초)")]
    [SerializeField] private float _perfectParryHitStopDuration  = 0.35f;
    [Tooltip("퍼펙트 패링 히트스탑 타임스케일 (0 = 완전 정지)")]
    [SerializeField] private float _perfectParryHitStopTimeScale = 0f;

    /// <summary>패링 활성 윈도우 내에 있을 때 true. TakeDamage에서 패링 판정에 사용됩니다.</summary>
    public bool IsParryActive => StateMachine.CurrentState is PlayerParryState ps && ps.IsParryWindowActive;

    /// <summary>쿨타임이 없고 지상에 있을 때만 패링 가능합니다.</summary>
    public bool CanParry => _parryCooldownTimer <= 0f && IsGround();

    public float CurrentStamina          => StatManager.CurrentStamina;
    public bool  HasEnoughStaminaForDash => StatManager.HasEnoughStaminaForDash;
    public bool  CanRun                  => StatManager.CanRun;

    // CanSkill / CanUltimate 는 현재 폼의 쿨타임과 SP를 함께 검사합니다.
    public bool CanSkill => FormManager?.CurrentForm?.CanSkill ?? false;
    public bool CanUltimate => FormManager?.CurrentForm?.CanUltimate ?? false;

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
    private Coroutine _hitStopCoroutine;

    [Header("Hit Stop Settings")]
    [SerializeField] private float _hitStopDuration  = 0.08f;
    [SerializeField] private float _hitStopTimeScale = 0.05f;

    public CinemachineImpulseSource ImpulseSource;

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

    public ElementType Element => FormManager?.CurrentForm?.Element ?? ElementType.ELEMENT_NONE;

    public WeaponManager WeaponManager { get; private set; }

    private LayerMask _groundLayer;

    private void Awake()
    {
        StatManager = GetComponent<PlayerStatManager>();
        Animator = gameObject.GetComponentInChildren<Animator>();
        ImpulseSource = GetComponent<CinemachineImpulseSource>();
        Controller = GetComponent<CharacterController>();
        Input = GetComponent<PlayerInputHandler>();
        CameraTransform = Camera.main.transform;
        Movement = GetComponent<PlayerMovement>();
        Movement.Initialize(this);
        WeaponManager = GetComponent<WeaponManager>();
        
        StateMachine = new StateMachine();
        var stateMachineSetup = new PlayerStateMachineSetup(this);
        StateMachine = stateMachineSetup.Build();

        var formManagerSetup = new FormManagerSetup(this);
        FormManager = formManagerSetup.Build(_FireFormActionData, _WaterFormActionData, _WoodFormActionData, _IronFormActionData, _EarthFormActionData);

        _groundLayer = LayerMask.GetMask("Ground");
    }

    private void Start()
    {
        DamageTextManager.Instance?.Register(this);
        PlayerUIManager.Instance?.Register(this);
    }

    private void Update()
    {
        UpdateCoyoteTimer();
        UpdateDashCooldown();
        UpdateParryCooldown();
        StatManager.UpdateStamina(Time.deltaTime);
        StatManager.UpdateSpRegen(Time.deltaTime);
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

    private void UpdateParryCooldown()
    {
        if (_parryCooldownTimer > 0f)
            _parryCooldownTimer -= Time.deltaTime;
    }

    public void ConsumeStaminaForDash() => StatManager.ConsumeStaminaForDash();
    public void ConsumeStaminaForRun()  => StatManager.ConsumeStaminaForRun(Time.deltaTime);

    public void StartParryCooldown() => _parryCooldownTimer = ParryCooldown;

    /// <summary>
    /// 패링 성공 시 호출됩니다.
    /// isPerfect=true(퍼펙트 패링) 일 때는 더 긴 히트스탑, 강한 카메라 흔들림, 추가 게이지를 제공합니다.
    /// </summary>
    public void OnParrySuccess(bool isPerfect = false)
    {
        if (isPerfect)
        {
            Debug.Log("퍼펙트 패링!");
            StartHitStop(_perfectParryHitStopDuration, _perfectParryHitStopTimeScale);
            ImpulseSource.GenerateImpulse();
            ImpulseSource.GenerateImpulse(); // 2중 임펄스로 더 강한 카메라 흔들림
            StatManager.AddUltimateGauge(ParryUltimateGaugeReward + PerfectParryUltimateGaugeBonus);
        }
        else
        {
            Debug.Log("패링 성공!");
            StartHitStop();
            ImpulseSource.GenerateImpulse();
            StatManager.AddUltimateGauge(ParryUltimateGaugeReward);
        }
        SetAnimatorTrigger("ParrySuccess");
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
        RaycastHit[] hits = Physics.SphereCastAll(sphereCenter, Controller.radius, Vector3.down, 0.1f, _groundLayer);

        foreach (var hit in hits)
        {
            if (Vector3.Angle(hit.normal, Vector3.up) <= 45f)
                return true;
        }
        return false;
    }

    public void TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE, bool isCritical = false, Vector3 power = default, float poiseDamage = 20f, StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true)
    {
        if (IsDashing) return;

        if (isParryable && IsParryActive)
        {
            var parryState = StateMachine.CurrentState as PlayerParryState;
            bool isPerfect = false;
            if (parryState != null)
            {
                bool isFirstHit = parryState.ParriedHitCount == 0;
                // CurrentAttackSource는 BroadcastHit()으로 ExecuteHit 직전에 설정됨
                parryState.OnHitParried(ParryEventBus.CurrentAttackSource);
                // 퍼펙트 연출은 콤보 1타에만 적용 (극적인 히트스탑)
                isPerfect = parryState.IsPerfectParry && isFirstHit;
            }
            OnParrySuccess(isPerfect);
            return;
        }

        Debug.Log("플레이어 피격!");
        StatManager.TakeDamage((int)damage, damageType, isCritical);
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
    /// <summary>기본 히트스탑 (Inspector 설정값 사용)</summary>
    public void StartHitStop()
        => StartHitStop(_hitStopDuration, _hitStopTimeScale);

    /// <summary>히트스탑 지속시간과 타임스케일을 직접 지정합니다. 퍼펙트 패링 등 특수 연출에 사용합니다.</summary>
    public void StartHitStop(float duration, float timeScale)
    {
        if (_hitStopCoroutine != null) StopCoroutine(_hitStopCoroutine);
        _hitStopCoroutine = StartCoroutine(HitStopRoutine(duration, timeScale));
    }

    private IEnumerator HitStopRoutine(float duration, float timeScale)
    {
        Time.timeScale = timeScale;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
        _hitStopCoroutine = null;
    }

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