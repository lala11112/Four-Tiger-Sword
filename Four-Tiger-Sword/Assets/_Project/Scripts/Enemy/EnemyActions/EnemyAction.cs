using UnityEngine;

public abstract class EnemyAction
{
    public MonsterSkillData SkillData { get; protected set; }
    public bool IsFinished { get; protected set; }
    public bool IsActive { get; private set; }
    public float AttackTimeline => _timer;
    public bool WasParried { get; private set; }
    public virtual bool SupportsBossPolicy => false;
    public virtual bool AllowsKnockbackDuringBossArmor => false;

    /// <summary>Per-hit player reaction metadata without replacing this action as the damage source.</summary>
    public virtual bool TryGetPlayerKnockdown(out float downSeconds, out float getUpSeconds)
    {
        downSeconds = getUpSeconds = 0f;
        if (SkillData == null || SkillData.playerHitReaction != EnemyPlayerHitReaction.Knockdown)
            return false;
        downSeconds = SkillData.playerDownSeconds;
        getUpSeconds = SkillData.playerGetUpSeconds;
        return true;
    }

    public virtual void OnKnockbackReceived() { }
    public BossAttackStage BossStage { get; private set; }
    public bool BossAimLocked => HasBossPolicy && _bossAimLocked;
    public bool HasBossArmor => HasBossPolicy && IsActive && !IsFinished && !WasParried
        && (BossStage == BossAttackStage.Startup && _bossPolicy.armorDuringStartup
            || BossStage == BossAttackStage.Active && _bossPolicy.armorDuringActive);
    public bool HasNormalAttackArmor => _normalAttackArmor && CanContinueAction;

    private BossAttackPolicy _bossPolicy;
    private bool _bossAimLocked;
    private Vector3 _bossAimPoint;
    private Quaternion _bossAimRotation;
    private bool _guardBossLifetime;
    private bool _guardNormalLifetime;
    private bool _normalAttackArmor;
    protected bool HasBossPolicy => _bossPolicy != null;
    protected Vector3 BossAimPoint => _bossAimPoint;
    protected Vector3 BossAimDirection => _bossAimRotation * Vector3.forward;
    protected virtual float BossInitialStartupDuration => float.PositiveInfinity;
    protected virtual bool UsesTimedTelegraph => true;
    protected bool CanContinueAction => !(_guardBossLifetime || _guardNormalLifetime)
        || IsActive && !IsFinished && !WasParried && _enemy != null
            && _enemy.isActiveAndEnabled && !_enemy.IsDead && _enemy.CurrentAction == this
            && !_enemy.IsHurt && !_enemy.IsGroggy && !_enemy.PendingGroggy
            && !_enemy.IsRoot && !_enemy.IsKnockbacking && !_enemy.IsSilenced
            && (!_guardNormalLifetime || !_enemy.CombatLabPassive && !_enemy.EncounterPatrolOnly && !_enemy.IsSelfDestructing)
            && !(_enemy is Boss boss && boss.IsTransitioning);
    // Retain the protected hook used by previously generated attack scripts.
    protected bool CanContinueBossAction => CanContinueAction;

    protected readonly Enemy _enemy;
    public Enemy Owner => _enemy;
    protected readonly LayerMask _playerLayer;
    protected float _timer;

    private bool _telegraphActive = false;
    // _timer는 서브클래스가 페이즈 전환 시 리셋하므로, 예고 타이밍은 독립 타이머로 관리
    private float _telegraphTimer = 0f;

    protected EnemyAction(MonsterSkillData data, Enemy enemy)
    {
        SkillData = data;
        _enemy = enemy;
        _playerLayer = LayerMask.GetMask("Player");
    }

    public void ConfigureBossPolicy(BossAttackPolicy policy)
    {
        if (IsActive) throw new System.InvalidOperationException("Configure boss attack policy before entering the action.");
        if (policy?.enabled == true)
        {
            if (!SupportsBossPolicy) throw new System.ArgumentException("This attack does not support a boss execution policy.", nameof(policy));
            foreach (string error in policy.Validate(SkillData))
                throw new System.ArgumentException(error, nameof(policy));
        }
        _bossPolicy = policy?.enabled == true ? policy.Copy() : null;
        BossStage = BossAttackStage.None;
        _bossAimLocked = false;
    }

    public virtual void Enter()
    {
        // Keep callback checks active even if a callback switches the current phase/profile.
        _guardBossLifetime = HasBossPolicy || _enemy is Boss boss && boss.ActiveCombatProfile != null;
        _guardNormalLifetime = _enemy != null && !(_enemy is Boss) && !(this is EnemySelfDestruct)
            && _enemy.CurrentAction == this && !_enemy.CombatLabPassive && !_enemy.EncounterPatrolOnly;
        _enemy.RecordAttackStarted(SkillData);
        _timer = 0f;
        _telegraphTimer = 0f;
        IsFinished = false;
        IsActive = true;
        WasParried = false;
        _telegraphActive = false;
        _normalAttackArmor = _guardNormalLifetime;
        if (_guardNormalLifetime && !CanContinueAction)
        {
            _normalAttackArmor = false;
            IsActive = false;
            IsFinished = true;
            return;
        }
        if (HasBossPolicy)
        {
            BeginBossPreparation();
            if (BossInitialStartupDuration <= 0f)
            {
                _bossAimLocked = true;
                BossStage = BossAttackStage.Active;
            }
        }
        if (_enemy.Animator != null)
            _enemy.SetAnimationSpeed(SkillData.attackSpeed);

        StartTelegraph();
    }

    public virtual void Update()
    {
        // attackSpeed 배율만큼 타이머를 빠르게 진행 → 모든 판정 타이밍이 비례해서 당겨짐
        _timer += _enemy.ActionDeltaTime * SkillData.attackSpeed;

        // 예고 타이밍 자동 관리: _telegraphTimer는 서브클래스의 _timer 리셋과 무관하게 증가
        if (_telegraphActive && SkillData.isParryable && UsesTimedTelegraph)
        {
            _telegraphTimer += _enemy.ActionDeltaTime * SkillData.attackSpeed;
            if (_telegraphTimer >= SkillData.telegraphDuration)
                EndTelegraph();
        }
    }

    public virtual void Exit()
    {
        if (!IsActive) return;
        // 중단된 공격도 패링 시스템에서 완료된 공격으로 취급해야 합니다.
        IsFinished = true;
        IsActive = false;
        EndNormalAttackArmor();
        BossStage = BossAttackStage.None;
        _bossAimLocked = false;
        if (_enemy.Animator != null)
            _enemy.SetAnimationSpeed(1f);

        EndTelegraph();
    }

    protected void EndNormalAttackArmor() => _normalAttackArmor = false;

    protected void BeginBossPreparation()
    {
        if (!HasBossPolicy) return;
        _bossAimLocked = _bossPolicy.lockFraction <= 0f;
        _bossAimPoint = _enemy.DetectedTarget != null ? _enemy.DetectedTarget.position
            : _enemy.transform.position + _enemy.transform.forward * 3f;
        _bossAimRotation = _enemy.transform.rotation;
        BossStage = BossAttackStage.Startup;
    }

    /// <summary>Times use the same attack-speed-scaled action timeline as hit windows.</summary>
    protected void TickBossPreparation(float previousTime, float currentTime, float startupStart, float activeStart)
    {
        if (!HasBossPolicy) return;
        float lockTime = Mathf.Lerp(startupStart, activeStart, _bossPolicy.lockFraction);
        if (!_bossAimLocked)
        {
            float turnTime = Mathf.Max(0f, Mathf.Min(currentTime, lockTime) - Mathf.Max(previousTime, startupStart));
            if (turnTime > 0f)
            {
                if (_enemy.DetectedTarget != null) _bossAimPoint = _enemy.DetectedTarget.position;
                Vector3 direction = _bossAimPoint - _enemy.transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > .001f)
                    _bossAimRotation = Quaternion.RotateTowards(_bossAimRotation,
                        Quaternion.LookRotation(direction), _bossPolicy.turnSpeed * turnTime);
            }
            if (currentTime >= lockTime) _bossAimLocked = true;
        }
        _enemy.transform.rotation = _bossAimRotation;
        BossStage = currentTime < activeStart ? BossAttackStage.Startup : BossAttackStage.Active;
    }

    protected void SetBossStage(BossAttackStage stage)
    {
        if (HasBossPolicy) BossStage = stage;
    }

    /// <summary>패링 가능한 공격임을 플레이어 패링 시스템에 알립니다. Enter 시 자동 호출됩니다.</summary>
    protected void StartTelegraph()
    {
        if (!SkillData.isParryable || _telegraphActive) return;
        _telegraphActive = true;
        ParryEventBus.BroadcastTelegraphStart(this);
        if (!CanContinueAction) { EndTelegraph(); return; }
        _enemy.OnTelegraphStart(this);
    }

    /// <summary>예고 구간을 종료합니다. 실제 타격 직전 또는 Exit 시 호출됩니다.</summary>
    protected void EndTelegraph()
    {
        if (!_telegraphActive) return;
        _telegraphActive = false;
        ParryEventBus.BroadcastTelegraphEnd(this);
        _enemy.OnTelegraphEnd(this);
    }

    /// <summary>
    /// ExecuteHit 직전에 호출합니다. PlayerController.TakeDamage에서 이 액션의 소스를 식별하는 데 사용됩니다.
    /// </summary>
    protected void BroadcastHit()
    {
        if (SkillData.isParryable)
            ParryEventBus.BroadcastHit(this);
    }
    

    // ── 공통 헬퍼 ────────────────────────────────────────────────────────────

    protected void PlaySwingSound(CombatSoundSettings overrideSound = null)
    {
        CombatSoundSettings sound = overrideSound != null && overrideSound.HasSound
            ? overrideSound : SkillData.SwingAudio;
        PlaySound(sound, _enemy.transform.position);
    }

    protected void PlaySound(CombatSoundSettings sound, Vector3 position)
        => CombatSfx.Play(sound?.Resolve() ?? default, position, this);

    protected CombatSoundCue ResolveHitSound(CombatSoundSettings overrideSound = null)
        => overrideSound != null && overrideSound.HasSound
            ? overrideSound.Resolve() : SkillData.HitAudio?.Resolve() ?? default;

    protected static bool CrossesHitWindow(float previousTime, float currentTime, float start, float duration)
        => duration > 0f && currentTime > previousTime
            && currentTime >= start && previousTime < start + duration;

    protected Vector3 GetHitCenter(Vector3 localOffset)
        => _enemy.transform.position + _enemy.transform.rotation * localOffset;

    protected Vector3 GetKnockbackDirection(Collider col)
        => (col.transform.position - _enemy.transform.position).normalized;

    protected Vector3 GetDirectionToTarget()
    {
        if (_enemy.DetectedTarget == null) return _enemy.transform.forward;
        Vector3 dir = _enemy.DetectedTarget.position - _enemy.transform.position;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.001f ? dir.normalized : _enemy.transform.forward;
    }

    protected void FaceTarget()
    {
        Vector3 dir = GetDirectionToTarget();
        if (dir != Vector3.zero)
            _enemy.transform.rotation = Quaternion.LookRotation(dir);
    }

    public virtual void OnParried()
    {
        if (_guardNormalLifetime && !SkillData.isParryable) return;
        WasParried = true;
        EndNormalAttackArmor();
        if (_guardNormalLifetime && IsActive) Exit();
    }
}
