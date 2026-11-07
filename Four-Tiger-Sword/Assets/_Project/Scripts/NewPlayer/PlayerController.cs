using UnityEngine;
using UnityEngine.Serialization;
using System.Collections;
using Unity.Cinemachine;

[RequireComponent(typeof(CharacterController), typeof(PlayerInputHandler), typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerStatManager))]
public partial class PlayerController : MonoBehaviour, IDamageable
{
    public CharacterController Controller { get; private set; }
    public PlayerInputHandler Input { get; private set; }
    public PlayerMovement Movement { get; private set; }
    public FormManager FormManager { get; private set; }
    public StateMachine StateMachine { get; private set; }
    public PlayerStatManager StatManager { get; private set; }
    public CombatActorClock ActorClock { get; private set; }
    public float ActionDeltaTime => IsGameplayModalPaused ? 0f : ActorClock != null ? ActorClock.DeltaTime : Time.deltaTime;
    public float ActionTimeScale => IsGameplayModalPaused ? 0f : ActorClock != null ? ActorClock.TimeScale : 1f;
    public float ActionTime => ActorClock != null ? ActorClock.Time : Time.time;
    public void SetAnimationSpeed(float speed)
    {
        if (ActorClock != null) ActorClock.SetAnimationSpeed(speed);
        else if (Animator != null) Animator.speed = speed;
    }
    public Transform CameraTransform;
    [Header("Combat Camera")]
    [SerializeField] private CombatCameraDirector _combatCamera;
    public CombatCameraDirector CombatCamera
    {
        get
        {
            if (_combatCamera != null) return _combatCamera;
            foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
            {
                if (camera.Follow == null || !camera.Follow.IsChildOf(transform)) continue;
                _combatCamera = camera.GetComponent<CombatCameraDirector>();
                if (_combatCamera == null) _combatCamera = camera.gameObject.AddComponent<CombatCameraDirector>();
                break;
            }
            return _combatCamera;
        }
    }

    [Header("Player Settings")]
    public float RunSpeed = 7.0f;
    public float DashSpeed = 10.0f;
    public float JumpForce = 2.0f;
    public float Gravity = -9.81f;
    public float VerticalVelocity;
    public float RotateSpeed = 0.15f;

    [Header("Targeting")]
    [Range(1f, 89f), Tooltip("카메라 시선을 중심으로 타깃을 선택하는 콘의 반각 (도)")]
    public float TargetingHalfAngle = 60f;

    [Header("Jump Settings")]
    public float CoyoteTime = 0.15f;
    private float _coyoteTimer;
    public float AirAttackDescentSpeed = 25f;

    [Header("Dash Settings")]
    public float DashCooldown = 1.0f;
    [Tooltip("방향 입력이 있을 때 재생할 Animator 상태 이름. 클립은 해당 상태의 Motion에 연결합니다.")]
    public string DashAnimationName = "Dash";
    [Tooltip("방향 입력 없이 뒤로 회피할 때 재생할 Animator 상태 이름. 클립은 해당 상태의 Motion에 연결합니다.")]
    public string BackstepAnimationName = "Backstep";
    private float _dashCooldownTimer;
    public bool CanDash => !IsStageTransitioning && !IsGameplayModalPaused && _dashCooldownTimer <= 0f && StatManager != null
        && StatManager.CurrentHp > 0f && StatManager.HasEnoughSp(DodgeSpCost);
    public bool IsDashing => StateMachine.CurrentState is PlayerDashState;

    [Header("Form Change / Parry Settings")]
    [FormerlySerializedAs("ParryCooldown")]
    [Min(0f), Tooltip("일반 폼 변경과 패링이 공유하는 쿨타임 (초)")]
    public float FormChangeCooldown = 2f;
    [Tooltip("패링 성공 시 필살기 게이지 충전량 (막은 타격마다 적용)")]
    public float ParryUltimateGaugeReward = 30f;
    private float _formChangeCooldownTimer;
    public bool CanChangeForm => !IsStageTransitioning && !IsGameplayModalPaused && _formChangeCooldownTimer <= 0f;

    /// <summary>패링 활성 윈도우 내에 있을 때 true. TakeDamage에서 패링 판정에 사용됩니다.</summary>
    public bool IsParryActive => StateMachine.CurrentState is PlayerParryState ps && ps.IsParryWindowActive;

    /// <summary>쿨타임 없음 + 지상 + 적의 예고가 활성 상태일 때만 패링 가능합니다.</summary>
    public bool CanParry => CanChangeForm && IsGround() && ParryEventBus.IsAnyTelegraphActive;

    public bool HasFormParryRequest
    {
        get
        {
            var requestedForm = FormManager?.GetRequestedForm();
            return requestedForm != null && CanParry && FormManager.CanSwitchForm(requestedForm);
        }
    }


    // CanSkill / CanUltimate 는 현재 폼의 쿨타임과 SP를 함께 검사합니다.
    public bool CanSkill => !IsStageTransitioning && !IsGameplayModalPaused && (FormManager?.CurrentForm?.CanSkill ?? false);
    public bool CanUltimate => !IsStageTransitioning && !IsGameplayModalPaused && (FormManager?.CurrentForm?.CanUltimate ?? false);

    public bool IsKnockbacking => _knockbackCoroutine != null
        || PendingHitReaction == PlayerHitReaction.Knockback
        || StateMachine?.CurrentState is PlayerKnockbackState knockback && !knockback.IsComplete;

    public bool IsKnockedDown => PendingHitReaction == PlayerHitReaction.Knockdown
        || StateMachine?.CurrentState is PlayerKnockdownState knockdown && !knockdown.IsComplete;

    [Header("Hit Reaction Settings")]
    [Min(0f)] public float HitDuration = 0.20f;
    [HideInInspector, Min(0f)] public float StaggerDuration = 0.20f; // Legacy serialized data; normal reactions use HitDuration.
    [Min(0f), Tooltip("피격 복귀 직전에 새 행동 입력을 보존하는 시간 (초)")]
    public float HitRecoveryInputBufferWindow = 0.05f;
    [Tooltip("이 강인도 피해 이상이면 짧은 피격 대신 경직됩니다. 누적 게이지가 아닌 1회 타격 기준입니다.")]
    [Min(0f)] public float StaggerPoiseThreshold = 20f;
    [Tooltip("공격의 경직 강도가 이 저항보다 높을 때만 피격/경직/넉백됩니다. 같거나 낮으면 피해만 받습니다.")]
    public StaggerResistLevel HitStaggerResistance = StaggerResistLevel.NONE;
    public string HitAnimationName = "Hit";
    public string StaggerAnimationName = "Stagger";
    public string KnockbackAnimationName = "Knockback";
    public string KnockdownAnimationName = "Knockdown";
    public string GetUpAnimationName = "GetUp";

    public PlayerHitReaction PendingHitReaction { get; private set; }
    private Vector3 _pendingHitVelocity;
    public float PendingKnockdownDownSeconds { get; private set; }
    public float PendingKnockdownGetUpSeconds { get; private set; }
    public float KnockbackDuration => Mathf.Max(0f, _knockbackDuration);

    [Header("CombatData")]
    [SerializeField] private FireFormActionDataSO _FireFormActionData;
    [SerializeField] private WaterFormActionDataSO _WaterFormActionData;
    [SerializeField] private WoodFormActionDataSO _WoodFormActionData;
    [SerializeField] private IronFormActionDataSO _IronFormActionData;
    [SerializeField] private EarthFormActionDataSO _EarthFormActionData;

    [Tooltip("넉백 저항값. 이 값보다 작은 힘은 밀리지 않음")]
    [SerializeField] private float _knockbackResistance = 3f;
    [Tooltip("넉백 지속 시간 (초)")]
    [SerializeField] private float _knockbackDuration = 0.12f;
    private Coroutine _knockbackCoroutine;

    [Header("Default Hit Stop Settings")]
    [Min(0f), Tooltip("패링 및 개별 히트스탑이 설정되지 않은 공격의 기본 지속 시간(실제 시간 기준 초)")]
    [SerializeField] private float _hitStopDuration  = 0.25f;
    [Range(0f, 1f), Tooltip("패링 및 개별 히트스탑이 설정되지 않은 공격의 기본 시간 배율")]
    [SerializeField] private float _hitStopTimeScale = 0f;

    public CinemachineImpulseSource ImpulseSource;

    public Animator Animator { get; private set; }

    public ElementType Element => FormManager?.CurrentForm?.Element ?? ElementType.ELEMENT_NONE;

    public WeaponManager WeaponManager { get; private set; }

    private LayerMask _groundLayer;

    private void Awake()
    {
        StatManager = GetComponent<PlayerStatManager>();
        GetComponent<PlayerUpgradeBinding>()?.BindForSpawn();
        ItemRuntime = GetComponent<PlayerItemRuntime>();
        Animator = gameObject.GetComponentInChildren<Animator>();
        ActorClock = CombatActorClock.GetOrCreate(this);
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
        ItemRuntime?.Initialize(this);
        DamageTextManager.Instance?.Register(this);
        PlayerUIManager.Instance?.Register(this);
    }

    private void Update()
    {
        if (IsStageTransitioning) return;
        if (IsGameplayModalPaused)
        {
            // A scripted lethal hit must still terminate the run while a gameplay modal owns world time.
            if (StatManager != null && StatManager.CurrentHp <= 0f)
                StateMachine?.Update(false, false);
            return;
        }
        TryOpenFinisherFromInput();
        UpdateCoyoteTimer();
        UpdateDashCooldown();
        UpdateFormChangeCooldown();
        ItemRuntime?.Tick(Time.deltaTime);
        StatManager.UpdateSpRegen(Time.deltaTime);
        FormManager.UpdateFormSp(Time.deltaTime);
        FormManager.UpdateChainReadiness(Time.deltaTime);
        bool canAct = ActionDeltaTime > 0f;
        if (canAct) TryDeactivateUltimate();
        if (StateMachine.CurrentState is PlayerAttackState attack) attack.PrepareSkillReservation();
        // Death and external hit reactions have priority even while actor time is stopped.
        StateMachine.Update(canAct, canAct);
        if (IsStageTransitioning) return;
        FormManager.Update();
    }

    private void TryDeactivateUltimate()
    {
        // Turning a stance off remains available while attacking, dashing or reacting to a hit.
        if (Input.UltimateBuffer.IsActive && FormManager.CurrentForm is IToggleUltimateForm toggle
            && toggle.IsUltimateActive)
        {
            Input.UltimateBuffer.Consume();
            toggle.DeactivateUltimate();
        }
    }

    private void UpdateCoyoteTimer()
    {
        if (IsGround())
            _coyoteTimer = CoyoteTime;
        else
            _coyoteTimer -= ActionDeltaTime;
    }

    private void UpdateDashCooldown()
    {
        if (_dashCooldownTimer > 0f)
            _dashCooldownTimer -= Time.deltaTime;
    }

    private void UpdateFormChangeCooldown()
    {
        if (_formChangeCooldownTimer > 0f)
            _formChangeCooldownTimer = Mathf.Max(0f, _formChangeCooldownTimer - Time.deltaTime);
    }


    public void StartFormChangeCooldown() => _formChangeCooldownTimer = Mathf.Max(0f, FormChangeCooldown);

    public void OnParrySuccess()
    {
        StartHitStop();
        ImpulseSource?.GenerateImpulse();
        StatManager.AddUltimateGauge(ParryUltimateGaugeReward);
    }

    public void PlayAttackCameraShake(WeaponActionData step)
    {
        if (ImpulseSource == null) return;
        float strength = step != null ? step.CameraShakeStrength : 1f;
        if (!KnockbackMotion.IsFinite(strength) || strength <= 0f) return;
        Vector3 velocity = ImpulseSource.DefaultVelocity;
        if (step != null && step.OverrideCameraShakeDirection)
        {
            Vector3 direction = step.CameraShakeDirection;
            if (!KnockbackMotion.IsFinite(direction.sqrMagnitude) || direction.sqrMagnitude <= 0f) return;
            velocity = direction.normalized * velocity.magnitude;
        }
        velocity *= strength;
        if (!KnockbackMotion.IsFinite(velocity.sqrMagnitude) || velocity.sqrMagnitude <= 0f) return;
        ImpulseSource.GenerateImpulseWithVelocity(velocity);
    }


    private void OnDrawGizmos()
    {
        if (Application.isPlaying && FormManager != null && FormManager.CurrentForm != null)
        {
            // 현재 장착된 폼의 'DrawHitboxGizmo' 메서드를 호출합니다.
            FormManager.CurrentForm.DrawHitboxGizmo();
        }
    }

    public bool CanJump() => !IsStageTransitioning && !IsGameplayModalPaused && (IsGround() || _coyoteTimer > 0f);
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
        if (!isActiveAndEnabled || IsStageTransitioning || StatManager.CurrentHp <= 0f
            || damage < 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return default;
        if (StateMachine.CurrentState is PlayerDashState dash)
        {
            if (damage > 0f && source is EnemyAction && dash.TryConfirmSuccessfulDodge())
                FormManager.NotifySuccessfulDodge();
            return default;
        }
        if ((FormManager.CurrentForm as BaseForm)?.BlocksIncomingDamage(damage, power, source) == true)
            return new DamageResult(DamageOutcome.Blocked);

        if (isParryable && IsParryActive)
        {
            var parryState = StateMachine.CurrentState as PlayerParryState;
            if (parryState != null && parryState.OnHitParried(source))
            {
                if (CanReactToHit(staggerResistLevel))
                    ApplyKnockback(power);
                OnParrySuccess();
                (source as EnemyAction)?.OnParried();
                return new DamageResult(DamageOutcome.Parried);
            }
        }

        Debug.Log("플레이어 피격!");
        damage *= (FormManager.CurrentForm as BaseForm)?.IncomingDamageMultiplier ?? 1f;
        var result = StatManager.TakeDamage(damage, damageType, isCritical);
        if (result.Applied && result.HealthDamage + result.ShieldDamage > 0f)
        {
            CancelPendingSwitchAttack();
            bool knockdown = ResolveIncomingKnockdown(source, out float downSeconds, out float getUpSeconds);
            PlayIncomingHitCameraShake(source, knockdown);
            if (!result.Killed)
                RequestHitReaction(power, poiseDamage, staggerResistLevel, knockdown, downSeconds, getUpSeconds);
        }
        return result;
    }

    private bool CanReactToHit(StaggerResistLevel attackLevel)
        => HitStaggerResistance != StaggerResistLevel.SUPER_ARMOR
            && attackLevel > HitStaggerResistance;

    private static bool ResolveIncomingKnockdown(object source, out float downSeconds, out float getUpSeconds)
    {
        downSeconds = getUpSeconds = 0f;
        return source is EnemyAction enemyAttack
            && enemyAttack.TryGetPlayerKnockdown(out downSeconds, out getUpSeconds)
            && KnockbackMotion.IsFinite(downSeconds) && downSeconds >= 0f
            && KnockbackMotion.IsFinite(getUpSeconds) && getUpSeconds >= 0f
            && KnockbackMotion.IsFinite(downSeconds + getUpSeconds) && downSeconds + getUpSeconds > 0f;
    }

    private void RequestHitReaction(Vector3 power, float poiseDamage, StaggerResistLevel attackLevel,
        bool knockdown, float downSeconds, float getUpSeconds)
    {
        // Damage and camera feedback still apply, but a committed down/get-up is never restarted.
        if (IsKnockedDown || !CanReactToHit(attackLevel))
            return;

        Vector3 velocity = GetKnockbackVelocity(power);
        PlayerHitReaction reaction = velocity.sqrMagnitude > 0f && KnockbackDuration > 0f
            ? PlayerHitReaction.Knockback
            : poiseDamage > 0f && poiseDamage >= StaggerPoiseThreshold
                ? PlayerHitReaction.Stagger : PlayerHitReaction.Hit;

        if (knockdown)
            reaction = PlayerHitReaction.Knockdown;

        // 같은 프레임의 약한 타격이 강한 반응을 덮어쓰거나 진행 중인 넉백을 취소하지 않습니다.
        if (PendingHitReaction > reaction) return;
        if (StateMachine.CurrentState is PlayerHitState active && !active.IsComplete && active.Reaction > reaction)
            return;

        PendingHitReaction = reaction;
        _pendingHitVelocity = velocity;
        PendingKnockdownDownSeconds = reaction == PlayerHitReaction.Knockdown ? downSeconds : 0f;
        PendingKnockdownGetUpSeconds = reaction == PlayerHitReaction.Knockdown ? getUpSeconds : 0f;
    }

    public Vector3 ConsumeHitReaction()
    {
        Vector3 velocity = _pendingHitVelocity;
        PendingHitReaction = PlayerHitReaction.None;
        _pendingHitVelocity = Vector3.zero;
        PendingKnockdownDownSeconds = PendingKnockdownGetUpSeconds = 0f;
        return velocity;
    }

    private Vector3 GetKnockbackVelocity(Vector3 power)
    {
        Vector3 velocity = KnockbackMotion.Velocity(power, _knockbackResistance);
        if (KnockbackMotion.IsFinite(power.sqrMagnitude) && power.magnitude > velocity.magnitude + .0001f)
            CombatFeedbackTelemetry.Emit("value-limited", gameObject, gameObject,
                detail: System.FormattableString.Invariant($"channel=knockback.speed;requested={power.magnitude:R};applied={velocity.magnitude:R};reason=player-knockback-resistance;unit=m/s"),
                value: velocity.magnitude, position: transform.position);
        return velocity;
    }

    // 패링 성공 시에는 반격 상태를 유지한 채 밀림만 적용합니다.
    private void ApplyKnockback(Vector3 power)
    {
        Vector3 velocity = GetKnockbackVelocity(power);
        if (velocity.sqrMagnitude <= 0f || !KnockbackMotion.IsFinite(KnockbackDuration) || KnockbackDuration <= 0f
            || Controller == null || !Controller.enabled || StatManager.CurrentHp <= 0f) return;

        if (_knockbackCoroutine != null)
            StopCoroutine(_knockbackCoroutine);

        _knockbackCoroutine = StartCoroutine(KnockbackRoutine(velocity, KnockbackDuration));
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

    /// <summary>히트별 설정, 타수별 설정, 플레이어 기본값 순서로 히트스탑을 적용합니다.</summary>
    public void StartHitStop(WeaponActionData step, HitEvent hitEvent = null, Component victim = null, bool includeAttacker = true)
    {
        var settings = hitEvent?.Feedback?.HitStop;
        if (settings == null || !settings.OverrideHitStop) settings = step?.Feedback?.HitStop;
        if (settings == null || !settings.OverrideHitStop)
            settings = new HitStopSettings { HitStopDuration = _hitStopDuration, HitStopTimeScale = _hitStopTimeScale };
        if (!isActiveAndEnabled || IsStageTransitioning) return;
        CombatHitStopService.Apply(this, this, victim, settings, includeAttacker);
    }

    /// <summary>히트스탑 지속시간과 타임스케일을 직접 지정합니다. 퍼펙트 패링 등 특수 연출에 사용합니다.</summary>
    public void StartHitStop(float duration, float timeScale)
    {
        if (!isActiveAndEnabled || IsStageTransitioning) return;
        CombatHitStopService.Apply(this, this, null,
            new HitStopSettings { HitStopDuration = duration, HitStopTimeScale = timeScale });
    }

    public void CancelHitStops()
    {
        CombatHitStopService.ReleaseOwner(this);
        ActorClock?.ClearStops();
    }

    private void OnDisable()
    {
        if (StateMachine?.CurrentState is PlayerHitState activeReaction) activeReaction.Cancel();
        if (StateMachine?.CurrentState is PlayerAttackState attack) attack.CancelSkillReservation();
        CancelFinisherSelection();
        CancelPendingSwitchAttack();
        if (StateMachine?.CurrentState is PlayerSwitchAttackState activeSwitch)
            activeSwitch.Cancel();
        StopAttackTrails(true);
        _combatCamera?.Cancel(this, true);
        FormManager?.CleanupTransientEffects();
        CancelHitStops();

        StopParryKnockback();
        ConsumeHitReaction();
    }

    private IEnumerator KnockbackRoutine(Vector3 initialVelocity, float duration)
    {
        // Defer the first move so a completed iterator cannot leave a stale coroutine handle.
        yield return null;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (!Controller.enabled || StatManager.CurrentHp <= 0f) break;
            Vector3 delta = KnockbackMotion.Displacement(initialVelocity, elapsed, ActionDeltaTime, duration);
            Controller.Move(delta);

            elapsed += ActionDeltaTime;
            yield return null;
        }

        _knockbackCoroutine = null;
    }
}
