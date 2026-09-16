using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections;

[RequireComponent(typeof(EnemyStat), typeof(PoiseHandler))]

public class Enemy : MonoBehaviour, IDamageable
{
    public EnemyStat EnemyStat; //몬스터의 스텟텟
    public string stateName; //디버그 전용용
    public ElementType Element => EnemyStat.ElementType; //몬스터의 속성
    public float CombatRange => EnemyStat.GetStat(EnemyStatType.CombatRange); //몬스터의 전투상태 진입거리
    public float CombatExitRange => EnemyStat.GetStat(EnemyStatType.CombatExitRange); //몬스터의 전투상태 종료거리

    [Header("넉백 설정")]
    [Tooltip("넉백 저항값. 이 값보다 작은 힘은 밀리지 않음")]
    public float KnockbackResistance => EnemyStat.GetStat(EnemyStatType.KnockbackResistance); //몬스터의 넉백 저항력
    [Tooltip("넉백 지속 시간 (초)")]
    public float KnockbackDuration => EnemyStat.GetStat(EnemyStatType.KnockbackDuration); //몬스터의 넉백 지속 시간

    public float AttackCooldown => EnemyStat.GetStat(EnemyStatType.AttackCooldown); //몬스터의 공격 쿨타임

    public event Action<float, ElementType, bool> OnDamaged; //몬스터가 데미지를 받을 때 호출되는 이벤트
    public event Action OnDied; //몬스터가 사망할 때 호출되는 이벤트

    //public MonsterSkillDataSO MonsterSkillData; //몬스터의 스킬 데이터

    // 현재 선택된 공격의 실행 객체
    public EnemyAction CurrentAction { get; set; }
    // CombatIdle에서 공격을 선택했을 때 true → ApproachState 전환 트리거
    public bool HasSelectedAttack { get; set; }

    private StateMachine _stateMachine;
    private IEnemySensor _sensor;
    private NavMeshAgent _navMeshAgent;
    private Coroutine _knockbackCoroutine;
    private int _moveLockCount = 0;

    private bool _isTargetFound = false;
    private bool _isAggroed = false; // 피격 시 플레이어를 인지하지 못했어도 추적 시작
    private bool _isTargetInCombatRange => DetectedTarget != null
        && Vector3.Distance(transform.position, DetectedTarget.position) <= CombatRange;
    private bool _isTargetOutOfCombatRange => DetectedTarget == null
        || Vector3.Distance(transform.position, DetectedTarget.position) > CombatExitRange;
    // 현재 선택한 공격의 실행 사거리(executeRange) 안에 플레이어가 들어왔는지
    public bool IsInExecuteRange => CurrentAction != null && DetectedTarget != null
        && Vector3.Distance(transform.position, DetectedTarget.position) <= CurrentAction.SkillData.executeRange;
    private Transform _aggroTarget;
    public Transform DetectedTarget
    {
        get
        {
            var sensed = _sensor?.DetectedTarget;
            if (sensed != null && sensed.gameObject.activeInHierarchy) return sensed;
            return _isAggroed && _aggroTarget != null && _aggroTarget.gameObject.activeInHierarchy
                ? _aggroTarget : null;
        }
    }
    private float _attackCooldownTimer = 0f;
    public bool IsHurt = false;
    public bool PendingGroggy = false;
    private bool _isDie = false;

    public bool CanAttack => _attackCooldownTimer <= 0f;
    public bool IsAttackFinished => CurrentAction?.IsFinished ?? true;

    public Animator Animator { get; private set; }

    public float GroggyDuration = 5f;

    public bool IsGroggy = false;

    public StaggerResistLevel StaggerResistLevel;

    public float RootDuration = 1f;
    public bool IsRoot = false;

    [SerializeField] private GameObject _telegraphEffect;
    private GameObject _telegraphInstance;


    protected virtual void Start()
    {
        EnemyStat = GetComponent<EnemyStat>();
        Animator = GetComponentInChildren<Animator>();
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

        DamageTextManager.Instance?.Register(this);
        GetComponentInChildren<EnemyHpBar>(true)?.Setup(this);

        // 상태 머신 초기화
        {
            _stateMachine = new StateMachine();

            var idleState = new EnemyIdleState(this);
            var traceState = new EnemyTraceState(this);
            var combatIdleState = new EnemyCombatIdleState(this);
            var approachState = new EnemyApproachState(this);
            var attackState = new EnemyAttackState(this);
            var hurtState = new EnemyHurtState(this);
            var dieState = new EnemyDieState(this);
            var groggyState = new EnemyGroggyState(this);
            var rootState = new EnemyRootState(this);
            // Idle → Trace: 플레이어 감지 또는 피격 어그로
            _stateMachine.AddTransition(idleState, traceState, () => _isTargetFound || _isAggroed);

            // Trace → CombatIdle: 플레이어가 전투 범위 안
            _stateMachine.AddTransition(traceState, combatIdleState, () => _isTargetInCombatRange);
            _stateMachine.AddTransition(traceState, idleState, () => DetectedTarget == null);

            // CombatIdle → Trace: 플레이어가 이탈 범위 밖
            _stateMachine.AddTransition(combatIdleState, traceState, () => _isTargetOutOfCombatRange);
            // CombatIdle → Approach: 공격 선택 완료
            _stateMachine.AddTransition(combatIdleState, approachState, () => HasSelectedAttack);

            // Approach → Attack: 실행 사거리 안에 플레이어 진입
            _stateMachine.AddTransition(approachState, attackState, () => IsInExecuteRange);
            // Approach → Trace: 플레이어가 이탈 범위 밖으로 이탈
            _stateMachine.AddTransition(approachState, traceState, () => _isTargetOutOfCombatRange);
            _stateMachine.AddTransition(approachState, combatIdleState, () => CurrentAction == null);

            // Attack → CombatIdle: 공격 완료 (쿨타임 + 배회)
            _stateMachine.AddTransition(attackState, combatIdleState, () => IsAttackFinished);

            _stateMachine.AddTransition(rootState, traceState, () => !IsRoot);

            // Hurt → Trace 복귀
            _stateMachine.AddTransition(hurtState, traceState, () => !IsHurt);
            _stateMachine.AddTransition(groggyState, traceState, () => !IsGroggy);

            _stateMachine.AddAnyTransition(dieState, () => _isDie);
            _stateMachine.AddAnyTransition(hurtState, () => IsHurt && !IsGroggy);
            _stateMachine.AddAnyTransition(groggyState, () => IsGroggy);
            _stateMachine.AddAnyTransition(rootState, () => IsRoot);

            _stateMachine.ChangeState(idleState);
        }

    }

    protected virtual void Update()
    {
        if (_stateMachine == null) return;
        _isTargetFound = _sensor?.DetectTarget() ?? false;
        // 피격 어그로로 추적할 때도 거리/공격 판단이 같은 대상을 사용해야 합니다.
        if (_isAggroed && DetectedTarget == null)
            _aggroTarget = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (_isAggroed && DetectedTarget == null) _isAggroed = false;
        _stateMachine.Update();
        stateName = _stateMachine.CurrentState.GetType().ToString(); //디버그 전용

        Vector3 worldVelocity = _navMeshAgent.enabled && _navMeshAgent.isOnNavMesh
            ? _navMeshAgent.velocity : Vector3.zero;
        Vector3 localVelocity = transform.InverseTransformDirection(worldVelocity);
        Animator.SetFloat("DirY", localVelocity.z);
        Animator.SetFloat("DirX", localVelocity.x);
        UpdateAttackCooldown();

        //Debug.Log(_stateMachine.CurrentState.GetType().ToString());


    }

    public virtual DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE, bool isCritical = false, Vector3 power = default, float poiseDamage = 100f, StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
    {
        if (!isActiveAndEnabled || EnemyStat.CurrentHp <= 0 || damage < 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return default;
        float hpBefore = EnemyStat.CurrentHp;

        GetComponent<PoiseHandler>()?.TakePoiseDamage(poiseDamage);

        _isAggroed = true;

        if (!IsGroggy && (staggerResistLevel >= StaggerResistLevel || PendingGroggy))
        {
            IsHurt = true;
        }
            
        EnemyStat.CurrentHp = Mathf.Max(0f, EnemyStat.CurrentHp - damage);
        float healthDamage = hpBefore - EnemyStat.CurrentHp;
        OnDamaged?.Invoke(healthDamage, damageType, isCritical);

        if (power != Vector3.zero)
            ApplyKnockback(power);

        if (EnemyStat.CurrentHp <= 0)
        {
            OnDied?.Invoke();
            Die();
        }
        return new DamageResult(DamageOutcome.Applied, healthDamage, killed: EnemyStat.CurrentHp <= 0f);
    }

    /// <summary>
    /// 넉백을 적용합니다. 저항값을 뺀 유효 힘이 0보다 클 때만 실제로 밀립니다.
    /// </summary>
    private void ApplyKnockback(Vector3 power)
    {
        float effectiveForce = power.magnitude - EnemyStat.GetStat(EnemyStatType.KnockbackResistance);
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
        if (_navMeshAgent != null && _navMeshAgent.isOnNavMesh)
            _navMeshAgent.ResetPath();

        float elapsed = 0f;
        while (elapsed < EnemyStat.GetStat(EnemyStatType.KnockbackDuration))
        {
            float t = 1f - (elapsed / EnemyStat.GetStat(EnemyStatType.KnockbackDuration)); // 선형 감속
            Vector3 delta = initialVelocity * t * Time.deltaTime;

            if (_navMeshAgent != null && _navMeshAgent.isOnNavMesh)
                _navMeshAgent.Move(delta);
            else
                transform.position += delta;

            elapsed += Time.deltaTime;
            yield return null;
        }

        _knockbackCoroutine = null;
    }

    private void Die()
    {
        _isDie = true;
    }

    public void UpdateAttackCooldown()
    {
        if (_attackCooldownTimer > 0f)
        {
            _attackCooldownTimer -= Time.deltaTime;
        }
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
        if (_moveLockCount == 0 && _navMeshAgent != null && _navMeshAgent.enabled && _navMeshAgent.isOnNavMesh)
            _navMeshAgent.isStopped = false;
    }

    /// <summary>NavMeshAgent 재활성화 직후 등 isStopped 상태를 현재 잠금 수에 맞게 동기화합니다.</summary>
    public void SyncMovementLock()
    {
        if (_navMeshAgent != null && _navMeshAgent.enabled && _navMeshAgent.isOnNavMesh)
            _navMeshAgent.isStopped = _moveLockCount > 0;
    }

    protected void ResetAttackCooldown()
    {
        _attackCooldownTimer = 0f;
    }

    public virtual void OnTelegraphStart(EnemyAction action)
    {
        if (_telegraphInstance != null) Destroy(_telegraphInstance);
        if (_telegraphEffect != null)
            _telegraphInstance = Instantiate(_telegraphEffect, transform.position, transform.rotation);
    }

    public virtual void OnTelegraphEnd(EnemyAction action)
    {
        if (_telegraphInstance != null) Destroy(_telegraphInstance);
        _telegraphInstance = null;
    }
}
