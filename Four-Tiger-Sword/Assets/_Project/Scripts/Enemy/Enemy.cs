using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections;

public class Enemy : MonoBehaviour, IDamageable
{
    public string stateName; 
    [SerializeField] private int _maxHp = 100;
    private int _currentHp;
    public int MaxHp     => _maxHp;
    public int CurrentHp => _currentHp;

    [Tooltip("플레이어가 이 거리 이내로 들어오면 전투(CombatIdle) 상태가 됨")]
    public float CombatRange = 6f;

    [Header("넉백 설정")]
    [Tooltip("넉백 저항값. 이 값보다 작은 힘은 밀리지 않음")]
    [SerializeField] private float _knockbackResistance = 3f;
    [Tooltip("넉백 지속 시간 (초)")]
    [SerializeField] private float _knockbackDuration = 0.25f;

    public event Action<int, DamageType, bool> OnDamaged;
    public event Action OnDied;

    public MonsterSkillDataSO MonsterSkillData;

    // 현재 선택된 공격의 실행 객체
    public EnemyAction CurrentAction { get; set; }
    // CombatIdle에서 공격을 선택했을 때 true → ApproachState 전환 트리거
    public bool HasSelectedAttack { get; set; }

    private StateMachine _stateMachine;
    private IEnemySensor _sensor;
    private NavMeshAgent _navMeshAgent;
    private Coroutine _knockbackCoroutine;

    private bool _isTargetFound = false;
    private bool _isTargetInCombatRange => DetectedTarget != null
        && Vector3.Distance(transform.position, DetectedTarget.position) <= CombatRange;
    // 현재 선택한 공격의 실행 사거리(excuteRange) 안에 플레이어가 들어왔는지
    public bool IsInExecuteRange => CurrentAction != null && DetectedTarget != null
        && Vector3.Distance(transform.position, DetectedTarget.position) <= CurrentAction.SkillData.excuteRange;
    public Transform DetectedTarget => _sensor?.DetectedTarget;
    private bool _isHurt = false;
    private float _attackCooldown = 10f;
    private float _attackCooldownTimer = 0f;
    public bool IsHurt { get => _isHurt; set => _isHurt = value; }
    private bool _isDie = false;

    public bool CanAttack => _attackCooldownTimer <= 0f;
    public bool IsAttackFinished => CurrentAction?.IsFinished ?? false;
    protected virtual void Start()
    {
        _currentHp = _maxHp;
        _sensor = GetComponent<IEnemySensor>();
        _navMeshAgent = GetComponent<NavMeshAgent>();

        DamageTextManager.Instance?.Register(this);

        //_enemyStats = GetComponent<EnemyStats>();

        _stateMachine = new StateMachine();

        var idleState       = new EnemyIdleState(this);
        var traceState      = new EnemyTraceState(this);
        var combatIdleState = new EnemyCombatIdleState(this);
        var approachState   = new EnemyApproachState(this);
        var attackState     = new EnemyAttackState(this);
        var hurtState       = new EnemyHurtState(this);
        var dieState        = new EnemyDieState(this);

        // Idle → Trace: 플레이어 감지
        _stateMachine.AddTransition(idleState, traceState, () => _isTargetFound);

        // Trace → CombatIdle: 플레이어가 전투 범위 안
        _stateMachine.AddTransition(traceState, combatIdleState, () => _isTargetInCombatRange);

        // CombatIdle → Trace: 플레이어가 전투 범위 밖
        _stateMachine.AddTransition(combatIdleState, traceState, () => !_isTargetInCombatRange);
        // CombatIdle → Approach: 공격 선택 완료
        _stateMachine.AddTransition(combatIdleState, approachState, () => HasSelectedAttack);

        // Approach → Attack: 실행 사거리 안에 플레이어 진입
        _stateMachine.AddTransition(approachState, attackState, () => IsInExecuteRange);
        // Approach → Trace: 플레이어가 전투 범위 밖으로 이탈
        _stateMachine.AddTransition(approachState, traceState, () => !_isTargetInCombatRange);

        // Attack → CombatIdle: 공격 완료 (쿨타임 + 배회)
        _stateMachine.AddTransition(attackState, combatIdleState, () => IsAttackFinished);

        // Hurt → Trace 복귀
        _stateMachine.AddTransition(hurtState, traceState, () => !IsHurt);

        _stateMachine.AddAnyTransition(dieState, () => _isDie);
        _stateMachine.AddAnyTransition(hurtState, () => IsHurt);
        _stateMachine.ChangeState(idleState);
    }

    protected virtual void Update()
    {
        _isTargetFound = _sensor?.DetectTarget() ?? false;
        stateName = _stateMachine.CurrentState.GetType().ToString();
        _stateMachine.Update();
        UpdateAttackCooldown();
    }

    public virtual void TakeDamage(int damage, DamageType damageType = DamageType.Normal, bool isCritical = false, Vector3 power = default)
    {
        if (_currentHp <= 0) return;

        IsHurt = true;
        _currentHp = Mathf.Max(0, _currentHp - damage);
        OnDamaged?.Invoke(damage, damageType, isCritical);

        if (power != Vector3.zero)
            ApplyKnockback(power);

        if (_currentHp <= 0)
        {
            OnDied?.Invoke();
            Die();
        }
    }

    /// <summary>
    /// 넉백을 적용합니다. 저항값을 뺀 유효 힘이 0보다 클 때만 실제로 밀립니다.
    /// </summary>
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
        if (_navMeshAgent != null)
            _navMeshAgent.ResetPath();

        float elapsed = 0f;
        while (elapsed < _knockbackDuration)
        {
            float t = 1f - (elapsed / _knockbackDuration); // 선형 감속
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
        if(_attackCooldownTimer > 0f)
        {
            _attackCooldownTimer -= Time.deltaTime;
        }
    }

    public void StartAttackCooldown()
    {
        _attackCooldownTimer = _attackCooldown;
    }

    protected void ResetAttackCooldown()
    {
        _attackCooldownTimer = 0f;
    }
}
