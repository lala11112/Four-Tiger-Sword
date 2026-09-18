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
    [Tooltip("패링 성공 시 필살기 게이지 충전량 (막은 타격마다 적용)")]
    public float ParryUltimateGaugeReward = 30f;
    private float _parryCooldownTimer;

    /// <summary>패링 활성 윈도우 내에 있을 때 true. TakeDamage에서 패링 판정에 사용됩니다.</summary>
    public bool IsParryActive => StateMachine.CurrentState is PlayerParryState ps && ps.IsParryWindowActive;

    /// <summary>쿨타임 없음 + 지상 + 적의 예고가 활성 상태일 때만 패링 가능합니다.</summary>
    public bool CanParry => _parryCooldownTimer <= 0f && IsGround() && ParryEventBus.IsAnyTelegraphActive;

    public float CurrentStamina          => StatManager.CurrentStamina;
    public bool  HasEnoughStaminaForDash => StatManager.HasEnoughStaminaForDash;
    public bool  CanRun                  => StatManager.CanRun;

    // CanSkill / CanUltimate 는 현재 폼의 쿨타임과 SP를 함께 검사합니다.
    public bool CanSkill => FormManager?.CurrentForm?.CanSkill ?? false;
    public bool CanUltimate => FormManager?.CurrentForm?.CanUltimate ?? false;

    public bool IsKnockbacking => _knockbackCoroutine != null
        || PendingHitReaction == PlayerHitReaction.Knockback
        || StateMachine?.CurrentState is PlayerKnockbackState knockback && !knockback.IsComplete;

    [Header("Hit Reaction Settings")]
    [Min(0f)] public float HitDuration = 0.15f;
    [Min(0f)] public float StaggerDuration = 0.4f;
    [Tooltip("이 강인도 피해 이상이면 짧은 피격 대신 경직됩니다. 누적 게이지가 아닌 1회 타격 기준입니다.")]
    [Min(0f)] public float StaggerPoiseThreshold = 20f;
    public StaggerResistLevel HitStaggerResistance = StaggerResistLevel.NONE;
    public string HitAnimationName = "Hit";
    public string StaggerAnimationName = "Stagger";
    public string KnockbackAnimationName = "Knockback";

    public PlayerHitReaction PendingHitReaction { get; private set; }
    private Vector3 _pendingHitVelocity;
    public float KnockbackDuration => Mathf.Max(0f, _knockbackDuration);

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
    private float _timeScaleBeforeHitStop;

    [Header("Hit Stop Settings")]
    [SerializeField] private float _hitStopDuration  = 0.25f;
    [SerializeField] private float _hitStopTimeScale = 0f;

    public CinemachineImpulseSource ImpulseSource;

    public Animator Animator { get; private set; }

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
        if (CameraTransform == null && Camera.main != null)
            CameraTransform = Camera.main.transform;
        Movement = GetComponent<PlayerMovement>();
        Movement.Initialize(this);
        WeaponManager = GetComponent<WeaponManager>();
        
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

    public void OnParrySuccess()
    {
        StartHitStop();
        ImpulseSource?.GenerateImpulse();
        StatManager.AddUltimateGauge(ParryUltimateGaugeReward);
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

    public bool IsGround() //자체 isGround. 일반적인 Controller는 바닥으로 Ray하나만 쏴서 한쪽 발이 걸려있어도 떨어지는걸로 판명되어 직접 작성함
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

    public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE, bool isCritical = false, Vector3 power = default, float poiseDamage = 20f, StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
    {
        if (!isActiveAndEnabled || StatManager.CurrentHp <= 0f || IsDashing
            || damage < 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return default;
        if ((FormManager.CurrentForm as BaseForm)?.BlocksIncomingDamage(damage, power, source) == true)
            return new DamageResult(DamageOutcome.Blocked);

        if (isParryable && IsParryActive)
        {
            var parryState = StateMachine.CurrentState as PlayerParryState;
            if (parryState != null)
                parryState.OnHitParried(source);

            ApplyKnockback(power);
            OnParrySuccess();
            (source as EnemyAction)?.OnParried();
            return new DamageResult(DamageOutcome.Parried);
        }

        Debug.Log("플레이어 피격!");
        var result = StatManager.TakeDamage(damage, damageType, isCritical);
        if (result.Applied && !result.Killed && result.HealthDamage + result.ShieldDamage > 0f)
            RequestHitReaction(power, poiseDamage, staggerResistLevel);
        return result;
    }

    private void RequestHitReaction(Vector3 power, float poiseDamage, StaggerResistLevel attackLevel)
    {
        if (HitStaggerResistance == StaggerResistLevel.SUPER_ARMOR || attackLevel < HitStaggerResistance)
            return;

        Vector3 velocity = GetKnockbackVelocity(power);
        PlayerHitReaction reaction = velocity.sqrMagnitude > 0f && KnockbackDuration > 0f
            ? PlayerHitReaction.Knockback
            : poiseDamage > 0f && poiseDamage >= StaggerPoiseThreshold
                ? PlayerHitReaction.Stagger : PlayerHitReaction.Hit;

        // 같은 프레임의 약한 타격이 강한 반응을 덮어쓰거나 진행 중인 넉백을 취소하지 않습니다.
        if (PendingHitReaction > reaction) return;
        if (StateMachine.CurrentState is PlayerHitState active && !active.IsComplete && active.Reaction > reaction)
            return;

        PendingHitReaction = reaction;
        _pendingHitVelocity = velocity;
    }

    public Vector3 ConsumeHitReaction()
    {
        Vector3 velocity = _pendingHitVelocity;
        PendingHitReaction = PlayerHitReaction.None;
        _pendingHitVelocity = Vector3.zero;
        return velocity;
    }

    private Vector3 GetKnockbackVelocity(Vector3 power)
    {
        if (float.IsNaN(power.sqrMagnitude) || float.IsInfinity(power.sqrMagnitude)) return Vector3.zero;
        float effectiveForce = Mathf.Max(0f, power.magnitude - _knockbackResistance);
        Vector3 horizontal = Vector3.ProjectOnPlane(power, Vector3.up);
        return horizontal.sqrMagnitude > 0f ? horizontal.normalized * effectiveForce : Vector3.zero;
    }

    // 패링 성공 시에는 반격 상태를 유지한 채 밀림만 적용합니다.
    private void ApplyKnockback(Vector3 power)
    {
        Vector3 velocity = GetKnockbackVelocity(power);
        if (velocity.sqrMagnitude <= 0f || KnockbackDuration <= 0f) return;

        if (_knockbackCoroutine != null)
            StopCoroutine(_knockbackCoroutine);

        _knockbackCoroutine = StartCoroutine(KnockbackRoutine(velocity));
    }

    public void StopParryKnockback()
    {
        if (_knockbackCoroutine == null) return;
        StopCoroutine(_knockbackCoroutine);
        _knockbackCoroutine = null;
    }

    /// <summary>기본 히트스탑 (Inspector 설정값 사용)</summary>
    public void StartHitStop()
        => StartHitStop(_hitStopDuration, _hitStopTimeScale);

    /// <summary>히트스탑 지속시간과 타임스케일을 직접 지정합니다. 퍼펙트 패링 등 특수 연출에 사용합니다.</summary>
    public void StartHitStop(float duration, float timeScale)
    {
        if (!isActiveAndEnabled || duration <= 0f) return;
        if (_hitStopCoroutine != null) StopCoroutine(_hitStopCoroutine);
        else _timeScaleBeforeHitStop = Time.timeScale;
        _hitStopCoroutine = StartCoroutine(HitStopRoutine(duration, timeScale));
    }

    private IEnumerator HitStopRoutine(float duration, float timeScale)
    {
        Time.timeScale = Mathf.Clamp01(timeScale);
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = _timeScaleBeforeHitStop;
        _hitStopCoroutine = null;
    }

    private void OnDisable()
    {
        (FormManager?.CurrentForm as BaseForm)?.CleanupTransientEffects();
        if (_hitStopCoroutine != null)
        {
            StopCoroutine(_hitStopCoroutine);
            Time.timeScale = _timeScaleBeforeHitStop;
            _hitStopCoroutine = null;
        }

        StopParryKnockback();
        ConsumeHitReaction();
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
            if (!Controller.enabled || StatManager.CurrentHp <= 0f) break;
            float t = 1f - (elapsed / _knockbackDuration); // 선형 감속
            Vector3 delta = initialVelocity * t * Time.deltaTime;
            Controller.Move(delta);

            elapsed += Time.deltaTime;
            yield return null;
        }

        _knockbackCoroutine = null;
    }
}
