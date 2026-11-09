using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(EnemyStat), typeof(PoiseHandler))]

public class Enemy : MonoBehaviour, IDamageable
{
    public EnemyStat EnemyStat; //몬스터의 스텟
    public string stateName; //디버그 전용
    public ElementType Element => EnemyStat.ElementType; //몬스터의 속성
    public float CombatRange => EnemyStat.GetStat(EnemyStatType.CombatRange); //몬스터의 전투상태 진입거리
    public float CombatExitRange => EnemyStat.GetStat(EnemyStatType.CombatExitRange); //몬스터의 전투상태 종료거리

    public float KnockbackMoveTime => EnemyStat.KnockbackMoveTime;
    public float KnockbackMaxDistance => EnemyStat.KnockbackMaxDistance;

    [Header("피격 설정")]
    [Min(0f), Tooltip("마지막 피격 후 행동을 멈추는 시간 (초)")]
    public float HurtDuration = 0.5f;
    public uint HurtVersion { get; private set; }

    public float AttackCooldown => EnemyStat.GetStat(EnemyStatType.AttackCooldown); //몬스터의 공격 쿨타임

    public event Action<float, ElementType, bool> OnDamaged; //몬스터가 데미지를 받을 때 호출되는 이벤트
    public event Action OnDied; //몬스터가 사망할 때 호출되는 이벤트

    //public MonsterSkillDataSO MonsterSkillData; //몬스터의 스킬 데이터

    // 현재 선택된 공격의 실행 객체
    public EnemyAction CurrentAction { get; set; }
    // CombatIdle에서 공격을 선택했을 때 true → ApproachState 전환 트리거
    public bool HasSelectedAttack { get; set; }

    public IEnemyBrain Brain { get; private set; }
    public bool IsDead => _isDie;
    public bool IsSpawning => _spawnReveal != null && _spawnReveal.IsRevealing;
    [SerializeField] private EnemyRank enemyRank = EnemyRank.Normal;
    public EnemyRank Rank => this is Boss ? EnemyRank.Boss : enemyRank;
    public ItemDeathContext DeathContext { get; private set; }
    public bool EncounterPatrolOnly { get; private set; }
    internal Vector3 PatrolOrigin { get; private set; }
    public virtual MonsterSkillDataSO ActiveSkillSet => EnemyStat?.MonsterSkillData;
    private bool _combatLabPassive;
    /// <summary>Experiment policy only: retain hit reactions and motion while suppressing autonomous actions.</summary>
    public bool CombatLabPassive
    {
        get => _combatLabPassive;
        set
        {
            if (_combatLabPassive == value) return;
            _combatLabPassive = value;
            if (value)
            {
                var activeBodyColliders = new List<Collider>();
                foreach (var collider in GetComponentsInChildren<Collider>(true))
                    if (collider.enabled && collider.GetComponentInParent<Enemy>() == this)
                        activeBodyColliders.Add(collider);
                CancelCurrentAction();
                IsSelfDestructing = false;
                Brain?.SetPassive(true);
                if (_navMeshAgent != null && _navMeshAgent.enabled && _navMeshAgent.isOnNavMesh)
                { _navMeshAgent.ResetPath(); _navMeshAgent.velocity = Vector3.zero; }
                // Action Exit restores its own earlier collider snapshot. Passive mode must
                // retain body colliders that were live immediately before this transition.
                if (!_isDie && EnemyStat?.IsDead != true)
                    foreach (var collider in activeBodyColliders) if (collider != null) collider.enabled = true;
            }
            else Brain?.SetPassive(false);
            SyncMovementLock();
        }
    }
    public bool IsSelfDestructing { get; private set; }
    private IEnemySensor _sensor;
    private NavMeshAgent _navMeshAgent;
    public bool IsKnockbacking { get; private set; }
    private Vector3 _knockbackVelocity;
    private float _knockbackElapsed;
    private float _knockbackMoveDuration;
    private bool _knockbackUpdateRotation;
    private bool _knockbackRootMotion;
    private GameObject _knockbackFeedbackOwner;
    private float _knockbackRequestedDistance, _knockbackMovedDistance;
    private int _moveLockCount = 0;

    [Header("AI 기억 및 접근")]
    [SerializeField, Min(0f)] private float _targetMemoryDuration = 4f;
    [Min(0.1f)] public float ApproachTimeout = 6f;
    [Min(0.1f)] public float ApproachStuckTimeout = 1.5f;
    [Min(0.1f)] public float FailedAttackRetryDelay = 1f;
    private float _targetMemoryExpiresAt = float.NegativeInfinity;
    public Vector3 LastKnownTargetPosition { get; private set; }
    public bool HasTargetMemory => !EncounterPatrolOnly && Time.time < _targetMemoryExpiresAt;
    private readonly Dictionary<MonsterSkillData, float> _skillReadyAt = new();
    private MonsterSkillData _lastAttack;
    // 현재 선택한 공격의 실행 사거리(executeRange) 안에 플레이어가 들어왔는지
    public bool IsInExecuteRange => !EncounterPatrolOnly && !IsKnockbacking && CurrentAction != null && DetectedTarget != null
        && Vector3.Distance(transform.position, DetectedTarget.position) >= CurrentAction.SkillData.minimumRange
        && Vector3.Distance(transform.position, DetectedTarget.position) <= CurrentAction.SkillData.executeRange;
    public virtual Transform DetectedTarget
    {
        get
        {
            if (EncounterPatrolOnly) return null;
            var sensed = _sensor?.DetectedTarget;
            if (sensed != null && sensed.gameObject.activeInHierarchy) return sensed;
            return null;
        }
    }
    private float _attackCooldownTimer = 0f;
    public bool IsHurt = false;
    public bool PendingGroggy = false;
    private bool _isDie = false;
    private EnemyHitFlash _hitFlash;
    private EnemyDeathFade _spawnReveal;
    private bool _spawnRevealAttempted;

    public bool IsSilenced => GetComponent<StatusEffectHandler>()?.Get<SilenceEffect>() is { IsExpired: false };
    internal bool IsAttackCooldownReady => _attackCooldownTimer <= 0f;
    public bool CanAttack => !IsSpawning && !EncounterPatrolOnly && !IsKnockbacking && !IsSilenced && IsAttackCooldownReady;
    public bool IsAttackFinished => CurrentAction?.IsFinished ?? true;

    public Animator Animator { get; private set; }
    private RuntimeAnimatorController _directionController;
    private bool _directionParametersCached, _hasDirectionX, _hasDirectionY;
    private static readonly int DirectionX = UnityEngine.Animator.StringToHash("DirX");
    private static readonly int DirectionY = UnityEngine.Animator.StringToHash("DirY");
    public CombatActorClock ActorClock { get; private set; }
    public float ActionDeltaTime => ActorClock != null ? ActorClock.DeltaTime : Time.deltaTime;
    public float ActionTimeScale => ActorClock != null ? ActorClock.TimeScale : 1f;
    public float ActionTime => ActorClock != null ? ActorClock.Time : Time.time;
    /// <summary>Switches the visual rig without resetting actor time, navigation, or combat state.</summary>
    public void BindAnimator(Animator animator)
    {
        if (animator == null) throw new System.ArgumentNullException(nameof(animator));
        Animator = animator;
        _directionController = null;
        _directionParametersCached = false;
        ActorClock = CombatActorClock.GetOrCreate(this);
        ActorClock.BindAnimator(animator);
    }

    public void SetAnimationSpeed(float speed)
    {
        if (ActorClock != null) ActorClock.SetAnimationSpeed(speed);
        else if (Animator != null) Animator.speed = speed;
    }

    public float GroggyDuration = 5f;

    public bool IsGroggy = false;

    public StaggerResistLevel StaggerResistLevel;

    public float RootDuration = 1f;
    public bool IsRoot = false;

    [SerializeField] private GameObject _telegraphEffect;
    private GameObject _telegraphInstance;

    Rigidbody[] _rigs;


    protected virtual void Start()
    {
        EnemyStat = GetComponent<EnemyStat>();
        Animator = GetComponentInChildren<Animator>();
        ActorClock = CombatActorClock.GetOrCreate(this);
        if (Animator == null)
        {
            Debug.LogError("Animator is null");
            enabled = false;
            return;
        }

        _sensor = GetComponent<IEnemySensor>();
        _navMeshAgent = GetComponent<NavMeshAgent>();
        if (_navMeshAgent == null)
        {
            Debug.LogError($"{name}: Enemy에 NavMeshAgent가 필요합니다.", this);
            enabled = false;
            return;
        }
        PatrolOrigin = transform.position;

        DamageTextManager.Instance?.Register(this);
        GetComponentInChildren<EnemyHpBar>(true)?.Setup(this);

        // Components must be ready before a specialized brain enters its first phase.
        Brain = CreateBrain();
        Brain.Initialize();
        if (IsSpawning) SyncMovementLock();

        _rigs = GetComponentsInChildren<Rigidbody>();

        foreach (Rigidbody rb in _rigs)
        {
            rb.isKinematic = true;
        }
    }

    protected virtual IEnemyBrain CreateBrain() => new NormalEnemyBrain(this);

    protected virtual void OnEnable()
    {
        // Reinforcements can be enabled during another object's Update and render before their Start.
        if (!_spawnRevealAttempted)
        {
            _spawnRevealAttempted = true;
            if (!IsDead && Rank != EnemyRank.Boss && TryGetComponent(out _spawnReveal))
            {
                _navMeshAgent = GetComponent<NavMeshAgent>();
                _spawnReveal.BeginSpawnReveal(this);
            }
        }
        Brain?.Resume();
    }

    protected virtual void Update()
    {
        if (Brain == null) return;
        if (_isDie)
        {
            Brain.Tick(false);
            stateName = Brain.StateName;
            SetDirectionAnimation(0f, 0f);
            return;
        }
        if (IsSpawning) return;
        UpdateAttackCooldown(); // World clock: local impact stops never lengthen cooldowns.
        bool canAct = ActionDeltaTime > 0f;
        if (canAct)
            UpdateKnockback(ActionDeltaTime);
        Brain.Tick(canAct);
        stateName = Brain.StateName;

        Vector3 worldVelocity = !IsKnockbacking && !IsHurt && !IsGroggy && _navMeshAgent.enabled && _navMeshAgent.isOnNavMesh
            ? _navMeshAgent.velocity : Vector3.zero;
        Vector3 localVelocity = transform.InverseTransformDirection(worldVelocity);
        SetDirectionAnimation(localVelocity.x, localVelocity.z);

    }

    private void SetDirectionAnimation(float x, float y)
    {
        if (Animator == null) return;
        if (!_directionParametersCached || _directionController != Animator.runtimeAnimatorController)
        {
            _directionParametersCached = true;
            _directionController = Animator.runtimeAnimatorController;
            _hasDirectionX = _hasDirectionY = false;
            foreach (var parameter in Animator.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Float) continue;
                if (parameter.nameHash == DirectionX) _hasDirectionX = true;
                if (parameter.nameHash == DirectionY) _hasDirectionY = true;
            }
            if (!_hasDirectionX || !_hasDirectionY)
                Debug.LogWarning($"{name}: Animator has no float parameter(s) {(!_hasDirectionX ? "DirX " : "")}{(!_hasDirectionY ? "DirY" : "")}; unavailable direction channels are skipped.", Animator);
        }
        if (_hasDirectionX) Animator.SetFloat(DirectionX, x);
        if (_hasDirectionY) Animator.SetFloat(DirectionY, y);
    }

    public virtual DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE, bool isCritical = false, Vector3 power = default, float poiseDamage = 100f, StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
    {
        if (!CanReceiveDamage(damage)) return default;
        float hpBefore = EnemyStat.CurrentHp;
        var hitContext = ItemDamageContext.Current;

        GetComponent<PoiseHandler>()?.TakePoiseDamage(poiseDamage);

        // 피격 순간의 위치만 기억하고, 벽 뒤의 플레이어를 계속 추적하지 않습니다.
        var attacker = GameObject.FindGameObjectWithTag("Player");
        if (attacker != null) RememberTargetPosition(attacker.transform.position);

        bool awaitingNormalGroggy = !(this is Boss) && PendingGroggy && !IsSelfDestructing;
        bool inflictedStagger = !awaitingNormalGroggy && !ResistsOrdinaryHitReaction && !IsGroggy
            && StaggerResistLevel != StaggerResistLevel.SUPER_ARMOR
            && (staggerResistLevel >= StaggerResistLevel || PendingGroggy);
        if (inflictedStagger)
        {
            IsHurt = true;
            HurtVersion++;
        }

        EnemyStat.CurrentHp = Mathf.Max(0f, EnemyStat.CurrentHp - damage);
        float healthDamage = hpBefore - EnemyStat.CurrentHp;
        // Phase depletion also publishes callbacks; commit this contact before those can dispatch another hit.
        ItemDamageContext.CommitOutgoing(healthDamage, 0f);
        // A boss may consume this health bar without confirming the actor's death.
        // Latch before callbacks so reentrant hits cannot consume another phase.
        bool healthDepletionHandled = EnemyStat.CurrentHp <= 0f && TryHandleHealthDepleted();
        if (healthDamage > 0f)
        {
            if (_hitFlash == null)
                _hitFlash = GetComponent<EnemyHitFlash>() ?? gameObject.AddComponent<EnemyHitFlash>();
            _hitFlash.Play();
        }
        OnDamaged?.Invoke(healthDamage, damageType, isCritical);

        if (EnemyStat.CurrentHp > 0f) TryStartSelfDestruct();

        // Preserve the enemy's existing equal-level reaction rule; super armor resists displacement.
        if (power != Vector3.zero && CanReceiveKnockback(staggerResistLevel) && !IsSelfDestructing && EnemyStat.CurrentHp > 0f)
            ApplyKnockback(power, source);

        if (EnemyStat.CurrentHp <= 0f && !healthDepletionHandled && !_isDie)
        {
            DeathContext = hitContext;
            Die();
            hitContext.Player?.Engravings?.NotifyKill(this, hitContext);
            OnDied?.Invoke();
        }
        return new DamageResult(DamageOutcome.Applied, healthDamage, killed: IsDead,
            causedOrdinaryStagger: inflictedStagger);
    }

    /// <summary>Return true to consume an exhausted health bar without a final death.</summary>
    protected virtual bool TryHandleHealthDepleted() => false;

    protected bool CanReceiveDamage(float damage)
    {
        if (EnemyStat == null) EnemyStat = GetComponent<EnemyStat>();
        return isActiveAndEnabled && !IsSpawning && !EncounterPatrolOnly && !_isDie && EnemyStat != null && EnemyStat.CurrentHp > 0f
            && damage >= 0f && !float.IsNaN(damage) && !float.IsInfinity(damage);
    }

    private void TryStartSelfDestruct() => Brain?.TryStartSelfDestruct();

    internal void BeginSelfDestruct()
    {
        ClearNormalHitChain();
        IsSelfDestructing = true;
        StopKnockback();
    }

    public void CompleteSelfDestruct(EnemySelfDestruct action)
    {
        if (_isDie || !IsSelfDestructing || CurrentAction != action) return;
        float remainingHp = EnemyStat.CurrentHp;
        EnemyStat.CurrentHp = 0f;
        DeathContext = default;
        Die();
        OnDamaged?.Invoke(remainingHp, Element, false);
        OnDied?.Invoke();
    }

    /// <summary>
    /// 공격의 경직 등급에 따라 넉백 허용 여부를 확인합니다.
    /// </summary>
    protected virtual bool ResistsOrdinaryHitReaction => !PendingGroggy && !IsGroggy
        && CurrentAction?.HasNormalAttackArmor == true;
    protected virtual bool ResistsKnockback => ResistsOrdinaryHitReaction;

    private bool CanReceiveKnockback(StaggerResistLevel attackLevel)
        => !ResistsKnockback && StaggerResistLevel != StaggerResistLevel.SUPER_ARMOR
            && (IsGroggy || PendingGroggy || attackLevel >= StaggerResistLevel);

    /// <summary>Displaces without manufacturing a damage/poise/hit event.</summary>
    public bool TryApplyExternalKnockback(Vector3 power, StaggerResistLevel attackLevel, object source = null)
    {
        if (!isActiveAndEnabled || IsDead || IsSpawning || EncounterPatrolOnly || IsSelfDestructing || EnemyStat == null
            || EnemyStat.CurrentHp <= 0f || !CanReceiveKnockback(attackLevel)
            || !KnockbackMotion.IsFinite(power.sqrMagnitude) || power.sqrMagnitude <= 0f) return false;
        if (_navMeshAgent == null) _navMeshAgent = GetComponent<NavMeshAgent>();
        if (_navMeshAgent == null || !_navMeshAgent.enabled || !_navMeshAgent.isOnNavMesh) return false;
        ApplyKnockback(power, source);
        return IsKnockbacking;
    }

    private void ApplyKnockback(Vector3 power, object source = null)
    {
        var feedbackOwner = source is Component component ? component.gameObject
            : source is EnemyAction action && action.Owner != null ? action.Owner.gameObject : null;
        Vector3 velocity = KnockbackMotion.Power(power, power.magnitude);
        float duration = KnockbackMoveTime;
        float maxDistance = KnockbackMaxDistance;
        if (velocity.sqrMagnitude <= 0f || !KnockbackMotion.IsFinite(duration) || duration <= 0f
            || !KnockbackMotion.IsFinite(maxDistance) || maxDistance <= 0f) return;
        CurrentAction?.OnKnockbackReceived();
        // Stop manual dash/jump displacement before taking ownership of movement.
        if (CurrentAction?.IsActive == true) CurrentAction.Exit();
        if (_navMeshAgent == null) _navMeshAgent = GetComponent<NavMeshAgent>();
        // Do not teleport through geometry when navigation is unavailable.
        if (_navMeshAgent == null || !_navMeshAgent.enabled || !_navMeshAgent.isOnNavMesh) return;
        _navMeshAgent.ResetPath();
        _navMeshAgent.velocity = Vector3.zero;
        if (IsKnockbacking) RecordKnockback("interrupted");
        _knockbackFeedbackOwner = feedbackOwner;
        BeginKnockback(velocity, duration, maxDistance);
    }

    private void BeginKnockback(Vector3 velocity, float duration, float maxDistance)
    {
        if (!IsKnockbacking)
        {
            IsKnockbacking = true;
            LockMovement();
            if (_navMeshAgent != null)
            {
                _knockbackUpdateRotation = _navMeshAgent.updateRotation;
                _navMeshAgent.updateRotation = false;
            }
            if (Animator != null)
            {
                _knockbackRootMotion = Animator.applyRootMotion;
                Animator.applyRootMotion = false;
            }
        }
        _knockbackMoveDuration = duration;
        // Integral of the impact profile is v0*T/3. Cap distance without extending sliding time.
        float maxSpeed = _knockbackMoveDuration > 0f
            ? maxDistance * 3f / _knockbackMoveDuration : 0f;
        _knockbackVelocity = Vector3.ClampMagnitude(velocity, maxSpeed);
        _knockbackRequestedDistance = velocity.magnitude * duration / 3f;
        _knockbackMovedDistance = 0f;
        float limitedDistance = _knockbackVelocity.magnitude * duration / 3f;
        if (limitedDistance + .0001f < _knockbackRequestedDistance)
            RecordKnockbackLimit(_knockbackRequestedDistance, limitedDistance, "enemy-knockback-max-distance");
        _knockbackElapsed = 0f;
    }

    /// <summary>
    /// SO의 이동 시간 동안 급감속하며 밀립니다. 피격 경직은 HurtDuration으로 별도 처리합니다.
    /// </summary>
    private void UpdateKnockback(float deltaTime)
    {
        if (!IsKnockbacking) return;
        if (_navMeshAgent == null || !_navMeshAgent.enabled || !_navMeshAgent.isOnNavMesh)
        {
            StopKnockback();
            return;
        }
        Vector3 delta = KnockbackMotion.ImpactDisplacement(_knockbackVelocity, _knockbackElapsed, deltaTime, _knockbackMoveDuration);
        _navMeshAgent.velocity = Vector3.zero;
        Vector3 before = _navMeshAgent.nextPosition;
        _navMeshAgent.Move(delta);
        float actual = Vector3.ProjectOnPlane(_navMeshAgent.nextPosition - before, Vector3.up).magnitude;
        _knockbackMovedDistance += actual;
        if (delta.magnitude > .0001f && actual + .001f < Vector3.ProjectOnPlane(delta, Vector3.up).magnitude)
            RecordKnockbackLimit(delta.magnitude, actual, "navmesh-boundary");
        _knockbackElapsed += Mathf.Max(0f, deltaTime);
        if (_knockbackElapsed >= _knockbackMoveDuration) StopKnockback("completed");
    }

    private void Die()
    {
        _isDie = true;
        if (_spawnReveal != null) _spawnReveal.EndSpawnReveal();
        ClearNormalHitChain();
        CombatHitStopService.ReleaseOwner(this);
        ActorClock?.ClearStops();
        StopKnockback();
        // 다음 Update 전에도 패링 예고와 공격 판정의 생명주기를 종료합니다.
        if (CurrentAction?.IsActive == true) CurrentAction.Exit();
    }

    protected virtual void OnDisable()
    {
        if (_spawnReveal != null) _spawnReveal.EndSpawnReveal();
        ClearNormalHitChain();
        CombatHitStopService.ReleaseOwner(this);
        ActorClock?.ClearStops();
        CancelCurrentAction();
        Brain?.Suspend();
        StopKnockback();
        OnTelegraphEnd(null);
    }

    private void StopKnockback(string reason = "interrupted")
    {
        if (!IsKnockbacking) return;
        RecordKnockback(reason);
        IsKnockbacking = false;
        _knockbackVelocity = Vector3.zero;
        if (_navMeshAgent != null)
        {
            _navMeshAgent.updateRotation = _knockbackUpdateRotation;
            if (_navMeshAgent.enabled && _navMeshAgent.isOnNavMesh)
                _navMeshAgent.velocity = Vector3.zero;
        }
        if (Animator != null) Animator.applyRootMotion = _knockbackRootMotion;
        UnlockMovement();
    }

    private void RecordKnockbackLimit(float requested, float applied, string reason)
    {
        CombatFeedbackTelemetry.Emit("value-limited", _knockbackFeedbackOwner, gameObject,
            detail: FormattableString.Invariant($"channel=knockback.distance;requested={requested:R};applied={applied:R};reason={reason};unit=m"),
            value: applied, position: transform.position);
    }

    private void RecordKnockback(string reason)
    {
        CombatFeedbackTelemetry.Emit("knockback-complete", _knockbackFeedbackOwner, gameObject,
            detail: FormattableString.Invariant($"channel=knockback.distance;requested={_knockbackRequestedDistance:R};applied={_knockbackMovedDistance:R};reason={reason};unit=m"),
            value: _knockbackMovedDistance, position: transform.position);
    }

    public void UpdateAttackCooldown()
    {
        if (_attackCooldownTimer > 0f)
        {
            _attackCooldownTimer -= Time.deltaTime;
        }
    }

    private void RememberTargetPosition(Vector3 position)
    {
        LastKnownTargetPosition = position;
        _targetMemoryExpiresAt = Time.time + _targetMemoryDuration;
    }

    internal virtual void SenseTarget()
    {
        if (EncounterPatrolOnly) return;
        _sensor?.DetectTarget();
        if (DetectedTarget != null) RememberTargetPosition(DetectedTarget.position);
    }

    /// <summary>Lets the first encounter wave patrol safely before the player enters its room.</summary>
    public void SetEncounterPatrolOnly(bool patrolOnly)
    {
        if (EncounterPatrolOnly == patrolOnly) return;
        EncounterPatrolOnly = patrolOnly;
        if (!patrolOnly) return;
        ClearNormalHitChain();
        _targetMemoryExpiresAt = float.NegativeInfinity;
        CancelCurrentAction();
        (Brain as NormalEnemyBrain)?.ReturnToPatrol();
    }

    /// <summary>Records an entry cue once. Attacking still requires ordinary target detection.</summary>
    public void RememberEncounterPosition(Vector3 position)
    {
        if (EncounterPatrolOnly || !KnockbackMotion.IsFinite(position.x)
            || !KnockbackMotion.IsFinite(position.y) || !KnockbackMotion.IsFinite(position.z)) return;
        RememberTargetPosition(position);
    }

    public bool IsSkillReady(MonsterSkillData skill) => skill != null
        && (!_skillReadyAt.TryGetValue(skill, out float readyAt) || Time.time >= readyAt);

    public void CancelCurrentAction()
    {
        if (CurrentAction?.IsActive == true) CurrentAction.Exit();
        CurrentAction = null;
        HasSelectedAttack = false;
    }

    public float GetAttackSelectionWeight(MonsterSkillData skill, float distance)
    {
        if (EncounterPatrolOnly || skill == null || skill is MonsterSelfDestructSO || skill.weight <= 0f || distance < skill.minimumRange
            || distance > skill.engageRange || skill.minimumRange > skill.executeRange
            || !IsSkillReady(skill))
            return 0f;
        float preferred = skill.preferredRange > 0f ? skill.preferredRange : skill.executeRange;
        float span = Mathf.Max(0.1f, skill.engageRange - skill.minimumRange);
        float suitability = 1f - Mathf.Clamp01(Mathf.Abs(distance - preferred) / span);
        return skill.weight * Mathf.Lerp(0.25f, 1f, suitability)
            * (skill == _lastAttack ? Mathf.Clamp01(skill.repeatWeightMultiplier) : 1f);
    }

    internal MonsterSkillData SelectAttackSkill(bool executeRangeOnly = false)
    {
        var target = DetectedTarget;
        var skills = ActiveSkillSet?.skillData;
        if (target == null || skills == null) return null;
        float distance = Vector3.Distance(transform.position, target.position);
        float totalWeight = 0f;
        foreach (var skill in skills)
            if (!executeRangeOnly || skill != null && distance <= skill.executeRange)
                totalWeight += GetAttackSelectionWeight(skill, distance);
        if (totalWeight <= 0f) return null;
        float roll = UnityEngine.Random.Range(0f, totalWeight);
        MonsterSkillData fallback = null;
        foreach (var skill in skills)
        {
            if (executeRangeOnly && (skill == null || distance > skill.executeRange)) continue;
            float weight = GetAttackSelectionWeight(skill, distance);
            if (weight <= 0f) continue;
            fallback = skill;
            roll -= weight;
            if (roll <= 0f) return skill;
        }
        return fallback;
    }

    /// <summary>Receives credit only for a confirmed direct player hit that caused this call's stagger.</summary>
    public void RecordDirectPlayerStagger()
    {
        if (!isActiveAndEnabled || this is Boss || IsDead || EnemyStat?.CurrentHp <= 0f
            || !IsHurt || IsGroggy || PendingGroggy || IsRoot || IsSelfDestructing
            || CombatLabPassive || EncounterPatrolOnly) return;
        (Brain as NormalEnemyBrain)?.RecordDirectStagger();
    }

    internal void ClearNormalHitChain() => (Brain as NormalEnemyBrain)?.ClearHitChain();

    internal void StopKnockbackForCounter() => StopKnockback("counter-started");

    internal void OnPoiseBroken()
    {
        PendingGroggy = true;
        if (this is Boss || IsSelfDestructing) return;
        ClearNormalHitChain();
        // Stop current hit windows immediately. The brain enters groggy on its next safe tick.
        CancelCurrentAction();
    }

    public void RecordAttackStarted(MonsterSkillData skill)
    {
        if (skill == null) return;
        _lastAttack = skill;
        _skillReadyAt[_lastAttack] = Time.time + Mathf.Max(0f, _lastAttack.cooldown);
    }

    public void CancelFailedApproach()
    {
        if (CurrentAction != null)
            _skillReadyAt[CurrentAction.SkillData] = Time.time + Mathf.Max(0.1f, FailedAttackRetryDelay);
        CurrentAction = null;
        HasSelectedAttack = false;
    }

    public void StartAttackCooldown()
    {
        _attackCooldownTimer = EnemyStat.GetStat(EnemyStatType.AttackCooldown);
    }

    public void LockMovement()
    {
        _moveLockCount++;
        if (_navMeshAgent != null && _navMeshAgent.enabled && _navMeshAgent.isOnNavMesh)
            _navMeshAgent.isStopped = true;
    }

    public void UnlockMovement()
    {
        _moveLockCount = Mathf.Max(0, _moveLockCount - 1);
        if (_moveLockCount == 0 && !CombatLabPassive && _navMeshAgent != null && _navMeshAgent.enabled && _navMeshAgent.isOnNavMesh)
            _navMeshAgent.isStopped = false;
    }

    /// <summary>NavMeshAgent 재활성화 직후 등 isStopped 상태를 현재 잠금 수에 맞게 동기화합니다.</summary>
    public void SyncMovementLock()
    {
        if (_navMeshAgent != null && _navMeshAgent.enabled && _navMeshAgent.isOnNavMesh)
            _navMeshAgent.isStopped = _moveLockCount > 0 || CombatLabPassive;
    }

    internal void SetNavigationRotation(bool updateRotation)
    {
        if (_navMeshAgent == null) return;
        // Knockback owns the live rotation flag until it releases its movement lock.
        if (IsKnockbacking) _knockbackUpdateRotation = updateRotation;
        else _navMeshAgent.updateRotation = updateRotation;
    }

    protected void ResetAttackCooldown()
    {
        _attackCooldownTimer = 0f;
    }

    /// <summary>Called after the brain has exited its previous reaction/action state.</summary>
    internal void ResetForBossPhase()
    {
        StopKnockback();
        IsHurt = IsGroggy = IsRoot = PendingGroggy = IsSelfDestructing = false;
        GetComponent<PoiseHandler>()?.ResetPoise();
        _skillReadyAt.Clear();
        _lastAttack = null;
        ResetAttackCooldown();
    }

    public virtual void OnTelegraphStart(EnemyAction action)
    {
        if (_telegraphInstance != null) Destroy(_telegraphInstance);
        if (_telegraphEffect != null)
            _telegraphInstance = StageTransientObject.Track(Instantiate(_telegraphEffect, transform.position, transform.rotation));
    }

    public virtual void OnTelegraphEnd(EnemyAction action)
    {
        if (_telegraphInstance != null) Destroy(_telegraphInstance);
        _telegraphInstance = null;
    }

}
