using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모든 보스의 추상 기반 클래스.
///
/// [SOLID 설계 원칙]
/// - SRP : 페이즈 전환은 Boss, 보호막은 BossShield, 패시브는 IBossPhase가 각각 담당.
/// - OCP : 새 보스는 Boss를 상속하고 CreatePhases()만 구현하면 되며, 이 클래스는 수정 불필요.
/// - LSP : Boss는 Enemy의 모든 컨텍스트에서 투명하게 대체 가능.
/// - ISP : IBossPhase는 페이즈에 필요한 메서드만 포함.
/// - DIP : Boss는 IBossPhase 추상화에만 의존하며 구체 페이즈 타입을 알지 못함.
///
/// [사용법]
/// 1. Boss를 상속한 구체 보스 클래스를 생성합니다.
/// 2. CreatePhases()를 오버라이드하여 페이즈 목록을 반환합니다.
///    (HP 임계값 내림차순 정렬 권장 — 아니어도 내부에서 자동 정렬)
/// 3. BossShield 컴포넌트가 자동 부착됩니다.
/// </summary>
[RequireComponent(typeof(BossShield))]
public abstract class Boss : Enemy
{
    [Header("보스 공통 데이터")]
    [SerializeField] protected BossData BossData;

    [Tooltip("HP 임계값 내림차순으로 나열합니다. (높은 HP% 페이즈 먼저)")]
    [SerializeField] protected List<BossPhaseData> PhaseDatas;

    // ── 런타임 상태 ──────────────────────────────────────────────────────────

    private List<IBossPhase> _phases;
    private int _currentPhaseIndex = -1;

    /// <summary>현재 활성 페이즈. 아직 어떤 페이즈도 진입하지 않았으면 null.</summary>
    protected IBossPhase CurrentPhase
        => (_phases != null && _currentPhaseIndex >= 0) ? _phases[_currentPhaseIndex] : null;

    protected BossShield Shield { get; private set; }

    /// <summary>현재 HP 비율 (0.0 ~ 1.0).</summary>
    public float HpPercent => MaxHp > 0 ? (float)CurrentHp / MaxHp : 0f;

    // ── 이벤트 ───────────────────────────────────────────────────────────────

    /// <summary>페이즈가 전환될 때 발생합니다. (새 페이즈 인덱스, 새 IBossPhase)</summary>
    public event Action<int, IBossPhase> OnPhaseChanged;

    // ── Unity 생명주기 ────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        Shield = GetComponent<BossShield>();
    }

    protected override void Start()
    {
        base.Start();

        _phases = CreatePhases();

        // 임계값 내림차순 정렬 (높은 HP% 페이즈가 먼저 오도록)
        _phases.Sort((a, b) => b.HpThreshold.CompareTo(a.HpThreshold));

        // 게임 시작 시 초기 페이즈 결정 (통상 HP 100%이므로 인덱스 0)
        CheckPhaseTransition();
    }

    protected override void Update()
    {
        base.Update();
        CurrentPhase?.OnUpdate(this);
    }

    // ── 페이즈 팩토리 (구체 보스가 반드시 구현) ──────────────────────────────

    /// <summary>
    /// 이 보스의 페이즈 목록을 생성하여 반환합니다.
    /// HP 임계값 내림차순(높은 값 먼저)으로 반환하는 것을 권장합니다.
    /// </summary>
    protected abstract List<IBossPhase> CreatePhases();

    // ── 데미지 처리 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 데미지 처리 순서:
    /// 1. 현재 페이즈의 패시브 보정 (감소율 등)
    /// 2. BossShield 흡수
    /// 3. 실제 HP 차감 (base.TakeDamage)
    /// 4. 페이즈 전환 체크
    /// </summary>
    public override void TakeDamage(int damage, DamageType damageType = DamageType.Normal,
                                    bool isCritical = false, Vector3 power = default)
    {
        int modified = CurrentPhase?.ModifyIncomingDamage(damage, this) ?? damage;
        modified = Shield.Absorb(modified);

        if (modified > 0)
            base.TakeDamage(modified, damageType, isCritical, power);

        CheckPhaseTransition();
    }

    // ── 연계 공격 요청 ────────────────────────────────────────────────────────

    /// <summary>
    /// 쿨타임을 무시하고 즉시 다음 스킬을 실행합니다.
    /// EnemyAction 내부에서 콤보 연계에 사용합니다.
    ///
    /// [동작 원리]
    /// EnemyAttackState는 CurrentAction.IsFinished를 감지해 FSM을 전환합니다.
    /// 이 메서드는 현재 액션을 새 액션으로 교체하고 Enter()를 즉시 호출합니다.
    /// 새 액션의 IsFinished가 false이므로 AttackState는 계속 실행되며,
    /// 다음 프레임부터 새 액션의 Update()가 호출됩니다. (Telegraph 없이 즉시 연계)
    /// </summary>
    public void RequestImmediateSkill(MonsterSkillData skill)
    {
        if (skill == null) return;

        CurrentAction?.Exit();

        var newAction = skill.CreateAction(this);
        newAction.Enter();
        CurrentAction = newAction;
    }

    // ── 페이즈 전환 내부 로직 ────────────────────────────────────────────────

    private void CheckPhaseTransition()
    {
        if (_phases == null || _phases.Count == 0) return;

        // 현재 HP%에서 진입 가능한 최고 인덱스 페이즈를 찾습니다.
        int target = _currentPhaseIndex;
        for (int i = 0; i < _phases.Count; i++)
        {
            if (HpPercent <= _phases[i].HpThreshold)
                target = i;
        }

        // 여러 페이즈를 한 번에 건너뛰는 경우도 OnExit/OnEnter를 순서대로 호출합니다.
        while (_currentPhaseIndex < target)
            TransitionToPhase(_currentPhaseIndex + 1);
    }

    private void TransitionToPhase(int newIndex)
    {
        CurrentPhase?.OnExit(this);
        _currentPhaseIndex = newIndex;
        CurrentPhase.OnEnter(this);

        OnPhaseChanged?.Invoke(newIndex, CurrentPhase);

        Debug.Log($"<color=cyan>[Boss:{name}] 페이즈 {newIndex + 1} 진입 " +
                  $"(HP {HpPercent * 100f:0.0}%)</color>");
    }
}
