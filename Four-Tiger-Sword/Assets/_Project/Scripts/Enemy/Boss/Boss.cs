using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Enemy-compatible combat actor with independent boss phases and patterns.</summary>
[RequireComponent(typeof(BossShield))]
public abstract class Boss : Enemy
{
    [Header("보스 공통 데이터")]
    [SerializeField] protected BossData BossData;
    [Tooltip("전투 순서대로 설정합니다. 각 체력을 소진하면 다음 페이즈로 부활합니다.")]
    [SerializeField] protected List<BossPhaseData> PhaseDatas;
    [Min(.01f), Tooltip("공유 페이즈 데이터를 변경하지 않고 이 보스의 각 페이즈 최대 체력에 적용할 배율입니다.")]
    public float PhaseHealthMultiplier = 1f;

    [Header("피격 표현")]
    [Tooltip("일반 피격에도 현재 동작을 유지하고 별도 피격 오버레이를 재생합니다. 넉백 저항은 별도로 설정합니다.")]
    public bool UseOverlayForOrdinaryHits;
    [Tooltip("일반 피격의 넉백과 그로 인한 행동 취소를 막습니다. 강인도 붕괴에 의한 그로기는 유지합니다.")]
    public bool ResistDamageKnockback;

    [Header("패턴 간격")]
    [Min(0f), Tooltip("패턴을 정상 완료한 뒤 회복 시간에 더하는 초 단위 대기. 패링·실패와 콤보 내부 간격에는 적용하지 않습니다.")]
    public float AdditionalPatternRecovery;

    [Header("회피 및 접근")]
    public BossMobilitySettings Mobility = new BossMobilitySettings();

    [Header("전술 전투 대상")]
    [Tooltip("전투 시작부터 유지할 대상. 비어 있으면 Player 태그를 한 번 찾아 연결합니다. 시야/탐색 상태와 무관합니다.")]
    [SerializeField] private Transform _combatTarget;
    private bool _explicitCombatTarget;
    private float _nextTargetResolve;
    private PlayerStatManager _targetStats;
    private Transform _cachedStatsTarget;

    public BossCombatProfileSO ActiveCombatProfile => (CurrentPhase as BossPhaseBase)?.Configuration?.combatProfile;
    public Transform CombatTarget
    {
        get
        {
            if (_combatTarget == null || !_combatTarget.gameObject.activeInHierarchy) return null;
            if (_cachedStatsTarget != _combatTarget)
            {
                _cachedStatsTarget = _combatTarget;
                _targetStats = _combatTarget.GetComponentInParent<PlayerStatManager>();
            }
            return _targetStats != null && _targetStats.CurrentHp <= 0f ? null : _combatTarget;
        }
    }

    public override Transform DetectedTarget => ActiveCombatProfile != null ? CombatTarget : base.DetectedTarget;
    private bool HasBossAttackArmor => ActiveCombatProfile != null
        && !PendingGroggy && !IsGroggy && CurrentAction?.HasBossArmor == true;
    protected override bool ResistsOrdinaryHitReaction => HasBossAttackArmor
        || UseOverlayForOrdinaryHits && !PendingGroggy && !IsGroggy;
    protected override bool ResistsKnockback => ResistDamageKnockback && !PendingGroggy && !IsGroggy
        || HasBossAttackArmor && CurrentAction?.AllowsKnockbackDuringBossArmor != true;

    /// <summary>Encounter-owned target. Null explicitly suspends combat until a target is assigned.</summary>
    public void SetCombatTarget(Transform target)
    {
        _explicitCombatTarget = true;
        _combatTarget = target;
        _cachedStatsTarget = null;
        _targetStats = null;
    }

    internal override void SenseTarget()
    {
        if (ActiveCombatProfile == null) { base.SenseTarget(); return; }
        // Binding a combat participant is not a vision/patrol/search behavior.
        if (_explicitCombatTarget || _combatTarget != null || Time.time < _nextTargetResolve) return;
        _nextTargetResolve = Time.time + 1f;
        _combatTarget = GameObject.FindGameObjectWithTag("Player")?.transform;
    }

    private List<IBossPhase> _phases;
    private int _currentPhaseIndex = -1;
    private bool _transitionStarted;
    private bool _reviveStarted;
    private float _transitionElapsed;
    private float _downSeconds, _holdSeconds, _reviveSeconds;
    private float _reviveAt, _transitionDuration;
    private MonsterSkillDataSO _activeSkillSet;
    private bool _phaseExited;
    private bool _ended;
    private BossPhaseData _musicConfiguration;

    public BossData Data => BossData != null ? BossData : GetComponent<EnemyStat>()?.Data as BossData;
    public IReadOnlyList<BossPhaseData> PhaseConfiguration => PhaseDatas != null
        ? (IReadOnlyList<BossPhaseData>)PhaseDatas : Array.Empty<BossPhaseData>();

    public int PhaseIndex => _currentPhaseIndex;
    public IBossPhase CurrentPhase => _phases != null && _currentPhaseIndex >= 0
        ? _phases[_currentPhaseIndex] : null;
    public bool IsTransitioning { get; private set; }
    public bool IsInvulnerable => isActiveAndEnabled && !IsDead && (IsTransitioning
        || CurrentAction is BossPhaseBurstAction burst && burst.IsInvulnerable
        || !CombatLabPassive && ActiveCombatProfile != null && CombatTarget != null
            && (Brain as BossBrain)?.Tactical?.IsInvulnerableEntryPending == true);
    public int PhaseGeneration { get; private set; }
    public float ReviveProgress => IsTransitioning
        ? Mathf.Clamp01((_transitionElapsed - _reviveAt) / _reviveSeconds) : 0f;
    internal bool IsReviving => _transitionElapsed >= _reviveAt;
    internal float DownProgress => _downSeconds > 0f ? Mathf.Clamp01(_transitionElapsed / _downSeconds) : 1f;
    public float HpPercent => EnemyStat != null && EnemyStat.MaxHp > 0f
        ? EnemyStat.CurrentHp / EnemyStat.MaxHp : 0f;
    public override MonsterSkillDataSO ActiveSkillSet => _activeSkillSet != null
        ? _activeSkillSet : base.ActiveSkillSet;
    protected BossShield Shield { get; private set; }
    public event Action<int, IBossPhase> OnPhaseChanged;
    public event Action<int> OnPhaseDepleted;
    /// <summary>Raised once before the next rig is sampled in its get-up pose.</summary>
    public event Action<int> OnPhaseReviveStarted;

    protected virtual void Awake() => Shield = GetComponent<BossShield>();

    protected virtual void OnDestroy() => EndPhases();

    protected override void OnEnable()
    {
        base.OnEnable();
        if (!IsDead && _musicConfiguration != null) SetPhaseMusic(_musicConfiguration);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        BossMusicService.Release(this);
    }

    protected override IEnemyBrain CreateBrain()
    {
        _ended = false;
        if (Shield == null) Shield = GetComponent<BossShield>();
        if (Data != null) Shield?.SetAbsorption(Data.initialShield);
        _activeSkillSet = base.ActiveSkillSet;
        _phases = CreatePhases() ?? new List<IBossPhase>();
        _phases.RemoveAll(phase => phase == null);
        if (float.IsNaN(PhaseHealthMultiplier) || float.IsInfinity(PhaseHealthMultiplier) || PhaseHealthMultiplier <= 0f)
            throw new InvalidOperationException($"{name}: Boss phase health multiplier must be finite and positive.");
        foreach (var phase in _phases)
        {
            float maxHp = phase.MaxHp * PhaseHealthMultiplier;
            if (float.IsNaN(maxHp) || float.IsInfinity(maxHp) || maxHp <= 0f)
                throw new InvalidOperationException($"{name}: Boss phase HP must be finite and positive.");
        }
        _currentPhaseIndex = -1;
        IsTransitioning = false;
        _transitionStarted = false;
        PhaseGeneration = 0;
        _phaseExited = false;
        if (_phases.Count > 0) EnterPhase(0);
        return new BossBrain(this);
    }

    protected abstract List<IBossPhase> CreatePhases();

    public override DamageResult TakeDamage(float damage,
        ElementType damageType = ElementType.ELEMENT_NONE, bool isCritical = false,
        Vector3 power = default, float poiseDamage = 100f,
        StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE,
        bool isParryable = true, object source = null)
    {
        // Validate before touching shield state or invoking a phase's damage hook.
        if (IsInvulnerable || !CanReceiveDamage(damage)) return default;
        float modified = CurrentPhase?.ModifyIncomingDamage(damage, this) ?? damage;
        if (float.IsNaN(modified) || float.IsInfinity(modified) || modified < 0f) return default;
        if (Shield == null) Shield = GetComponent<BossShield>();
        float remaining = Shield != null
            ? Shield.Absorb(modified, absorbedDamage => ItemDamageContext.CommitOutgoing(0f, absorbedDamage)) : modified;
        float absorbed = modified - remaining;
        DamageResult result = base.TakeDamage(remaining, damageType, isCritical, power,
            poiseDamage, staggerResistLevel, isParryable, source);
        (Brain as BossBrain)?.Tactical?.Mobility.RecordHit(result.HealthDamage, Time.frameCount);
        if (IsDead)
        {
            (Brain as BossBrain)?.CancelPattern();
            EndPhases();
        }
        return new DamageResult(result.Outcome, result.HealthDamage,
            result.ShieldDamage + absorbed, result.Killed);
    }

    /// <summary>Queues an action replacement at the brain's next safe update boundary.</summary>
    public void RequestImmediateSkill(MonsterSkillData skill)
        => (Brain as BossBrain)?.RequestSkill(skill);

    protected override bool TryHandleHealthDepleted()
    {
        if (IsTransitioning) return true;
        if (IsDead || _ended || _phases == null || _currentPhaseIndex < 0
            || _currentPhaseIndex + 1 >= _phases.Count) return false;
        IsTransitioning = true;
        PhaseGeneration++;
        _transitionStarted = false;
        _reviveStarted = false;
        _transitionElapsed = 0f;
        _downSeconds = Duration(Data != null ? Data.phaseDownSeconds : 1f, 1f);
        _holdSeconds = Duration(Data != null ? Data.phaseHoldSeconds : .5f, .5f);
        _reviveSeconds = Mathf.Max(.01f, Duration(Data != null ? Data.phaseReviveSeconds : 1.5f, 1.5f));
        _reviveAt = _downSeconds + _holdSeconds;
        _transitionDuration = _reviveAt + _reviveSeconds;
        OnPhaseDepleted?.Invoke(_currentPhaseIndex);
        return true;
    }

    // Called only after the previous action/reaction state has exited its callback stack.
    internal void BeginPhaseTransition()
    {
        if (!IsTransitioning || _transitionStarted || IsDead) return;
        _transitionStarted = true;
        CancelCurrentAction();
        GetComponent<StatusEffectHandler>()?.ClearAll();
        GetComponent<FormTargetEffects>()?.ResetForNewPhase();
        GetComponent<WoodTargetEffects>()?.ResetForNewPhase();
        ResetForBossPhase();
        ExitCurrentPhase();
    }

    internal void TickPhaseTransition(float deltaTime)
    {
        if (!IsTransitioning || IsDead || !isActiveAndEnabled
            || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f) return;
        BeginPhaseTransition();
        if (!IsTransitioning || IsDead || !isActiveAndEnabled || CombatLabPassive || deltaTime == 0f) return;
        _transitionElapsed = Mathf.Min(_transitionElapsed + deltaTime, _transitionDuration);
        if (!_reviveStarted && _transitionElapsed >= _reviveAt)
        {
            _reviveStarted = true;
            OnPhaseReviveStarted?.Invoke(_currentPhaseIndex + 1);
            if (!IsTransitioning || IsDead || !isActiveAndEnabled) return;
        }
        if (_transitionElapsed < _transitionDuration) return;
        ResetForBossPhase();
        // End the invulnerability latch before invoking the next phase's enter hooks.
        IsTransitioning = false;
        _transitionStarted = false;
        EnterPhase(_currentPhaseIndex + 1);
        if (!IsDead && !IsTransitioning) StartAttackCooldown();
    }

    private static float Duration(float value, float fallback)
        => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f ? value : fallback;

    internal void TickPhase() => CurrentPhase?.OnUpdate(this);

    internal void EndPhases()
    {
        if (_ended) return;
        _ended = true;
        IsTransitioning = false;
        ExitCurrentPhase();
        EnemyStat?.SetPhaseMultipliers(1f, 1f);
        Shield?.SetAbsorption(0f);
        BossMusicService.Release(this);
    }

    private void EnterPhase(int index)
    {
        _currentPhaseIndex = index;
        _phaseExited = false;
        EnemyStat?.SetPhaseMaxHp(CurrentPhase.MaxHp * PhaseHealthMultiplier);
        if (EnemyStat != null) EnemyStat.CurrentHp = EnemyStat.MaxHp;
        if (CurrentPhase.SkillSet != null) _activeSkillSet = CurrentPhase.SkillSet;
        var configuration = (CurrentPhase as BossPhaseBase)?.Configuration;
        EnemyStat?.SetPhaseMultipliers(configuration != null ? configuration.atkMultiplier : 1f,
            configuration != null ? configuration.defMultiplier : 1f);
        if (configuration != null)
        {
            if (configuration.overrideShield) Shield?.SetAbsorption(configuration.shieldAmount);
            if (configuration.musicMode != BossMusicMode.Keep)
            {
                _musicConfiguration = configuration;
                SetPhaseMusic(configuration);
            }
        }
        CurrentPhase.OnEnter(this);
        OnPhaseChanged?.Invoke(index, CurrentPhase);
    }

    private void ExitCurrentPhase()
    {
        if (_phaseExited || CurrentPhase == null) return;
        _phaseExited = true;
        CurrentPhase.OnExit(this);
    }

    private void SetPhaseMusic(BossPhaseData configuration)
        => BossMusicService.Set(this, configuration.musicMode, configuration.musicClip,
            configuration.musicVolume, configuration.musicFadeSeconds);
}
