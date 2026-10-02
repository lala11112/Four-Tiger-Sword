using System.Collections.Generic;
using UnityEngine;

public class WaterForm : BaseForm, IHeavyAttackForm
{
    private readonly WaterFormActionDataSO _waterData;

    public override ElementType Element => ElementType.ELEMENT_WATER;
    public override float SkillSpCost => 150f;
    public override float SkillCooldown => 6f;
    private readonly WaterFlowGauge _flow;
    public float WaterGauge => _flow.CurrentGauge;
    public float WaterGaugeMax => _flow.MaxGauge;
    public float WaterGaugePerSegment => _flow.PointsPerSegment;
    public float WaterGaugeRatio => _flow.GaugeRatio;
    public int WaterGaugeSegmentCount => WaterFlowGauge.SegmentCount;
    public int HeavyAttackCharges => _flow.AvailableCharges;
    protected override WeaponActionData HeavyAttackStep => _waterData?.HeavyAttackStep;
    public bool CanHeavyAttack => HeavyAttackStep != null
        && !string.IsNullOrWhiteSpace(HeavyAttackStep.AnimationName)
        && HeavyAttackStep.Duration > 0f && HeavyAttackCharges > 0;
    public float HeavyAttackHoldTime => Mathf.Max(0.05f, _waterData.HeavyAttackHoldTime);
    public float SkillBuffRemaining { get; private set; }
    public bool IsSkillBuffActive => SkillBuffRemaining > 0f;
    public float SkillAttackBonusPercent => IsSkillBuffActive ? _appliedAttackBonus * 100f : 0f;
    public float HeavyAttackDamageMultiplier => IsSkillBuffActive ? 1f + _heavyDamageBonus : 1f;
    private float _appliedAttackBonus;
    private float _heavyDamageBonus;
    private LayerMask _savedExcludeLayers;
    private bool _skillCollisionOverride;
    private Vector3 _trailStart;
    private readonly HashSet<IDamageable> _dashHitTargets = new();
    private readonly HashSet<IDamageable> _heavyDashTargets = new();
    private LayerMask _heavySavedExcludeLayers;
    private bool _heavyCollisionOverride;
    private Vector3 _heavyDashDirection;
    private float _heavyDashDistance;
    private float _heavyDashDuration;
    private float _heavyAttackDuration;
    private bool _heavyAttackFinished;
    private Component _heavyChainTarget;
    private Collider _heavyChainTargetCollider;
    public Transform HeavyAttackTarget => HasHeavyChainTarget ? _heavyChainTarget.transform : null;
    private bool HasHeavyChainTarget => _heavyChainTarget != null && _heavyChainTarget.gameObject.activeInHierarchy
        && _heavyChainTargetCollider != null && _heavyChainTargetCollider.enabled
        && _heavyChainTargetCollider.gameObject.activeInHierarchy
        && !(_heavyChainTarget.GetComponent<EnemyStat>() is EnemyStat stats && stats.IsDead);
    private bool _ultimateActive;
    private float _ultimateElapsed;
    private int _ultimateHits;
    private Vector3 _ultimateCenter;
    private Transform _ultimateTarget;
    private readonly Dictionary<Renderer, bool> _visibility = new();

    public WaterForm(WaterFormActionDataSO data) : base(data)
    {
        _waterData = data;
        _flow = new WaterFlowGauge(data != null ? data.WaterGaugePerSegment : 100f);
    }
    public override void Equip(PlayerController player)
    {
        base.Equip(player);
        PlayerUIManager.Instance?.WaterElement?.SetActive(false);
    }
    public override void Unequip(PlayerController player)
    {
        CleanupActionEffects();
        PlayerUIManager.Instance?.WaterElement?.SetActive(true);
    }
    protected override void OnHitEnemy(GameObject enemy)
    {
        base.OnHitEnemy(enemy);
        if (_currentAction == ActionType.Attack)
            _flow.Charge(_waterData.WaterNormalHitGain);
    }

    protected override void OnTick(float dt)
    {
        Debug.Log(WaterGauge);
        if (!IsSkillBuffActive) return;
        if (_playerController == null || !_playerController.isActiveAndEnabled
            || _playerController.StatManager.CurrentHp <= 0f)
        {
            RemoveSkillBuff();
            return;
        }
        SkillBuffRemaining = Mathf.Max(0f, SkillBuffRemaining - dt);
        if (SkillBuffRemaining <= 0f) RemoveSkillBuff();
    }

    private void ApplySkillBuff()
    {
        RemoveSkillBuff();
        SkillBuffRemaining = Mathf.Max(0f, _waterData.WaterSkillBuffDuration);
        if (!IsSkillBuffActive) return;
        // Snapshot the applied modifier so later Inspector edits cannot leave residual stats.
        _appliedAttackBonus = Mathf.Max(0f, _waterData.WaterSkillAttackBonusPercent) / 100f;
        _heavyDamageBonus = Mathf.Max(0f, _waterData.WaterSkillHeavyDamageBonusPercent) / 100f;
        if (_appliedAttackBonus > 0f)
            _playerController.StatManager.AddModifier(StatType.ST_ATK, 0f, _appliedAttackBonus);
    }

    private void RemoveSkillBuff()
    {
        if (_appliedAttackBonus > 0f && _playerController != null)
            _playerController.StatManager.RemoveModifier(StatType.ST_ATK, 0f, _appliedAttackBonus);
        SkillBuffRemaining = 0f;
        _appliedAttackBonus = _heavyDamageBonus = 0f;
    }
    public bool TryBeginHeavyAttack() => StartHeavyDash(false);

    public bool TryContinueHeavyAttack() => _heavyAttackFinished && StartHeavyDash(true);

    private bool StartHeavyDash(bool continuing)
    {
        if (!CanHeavyAttack || !_flow.TryConsumeCharge()) return false;
        RestoreHeavyCollision();
        if (!continuing)
        {
            _heavyChainTarget = null;
            _heavyChainTargetCollider = null;
        }
        SelectHeavyDashDirection(continuing);
        _heavyAttackDuration = HeavyAttackStep.Duration;
        _heavyDashDuration = Mathf.Clamp(_waterData.WaterHeavyDashDuration, 0.01f, _heavyAttackDuration);
        _heavyAttackFinished = false;
        _heavyDashDirection = Vector3.ProjectOnPlane(_heavyDashDirection, Vector3.up).normalized;
        if (_heavyDashDirection.sqrMagnitude < 0.001f) _heavyDashDirection = _playerController.transform.forward;
        _playerController.transform.rotation = Quaternion.LookRotation(_heavyDashDirection);
        BeginHeavyAttack();
        _softTarget = null;
        _heavySavedExcludeLayers = _playerController.Controller.excludeLayers;
        _heavyCollisionOverride = true;
        _playerController.Controller.excludeLayers |= LayerMask.GetMask("Enemy");
        _heavyDashTargets.Clear();
        return true;
    }

    private void SelectHeavyDashDirection(bool continuing)
    {
        bool hasMoveInput = _playerController.Input.MoveInput.sqrMagnitude > 0.01f;
        _heavyDashDirection = hasMoveInput
            ? _playerController.Movement.GetMoveDirection().normalized
            : _playerController.transform.forward;
        _heavyDashDistance = Mathf.Max(0f, _waterData.WaterHeavyDashDistance);
        if (continuing && HasHeavyChainTarget)
        {
            Vector3 toTarget = Vector3.ProjectOnPlane(
                _heavyChainTargetCollider.bounds.center - _playerController.transform.position, Vector3.up);
            if (toTarget.sqrMagnitude > 0.001f)
            {
                _heavyDashDirection = toTarget.normalized;
                _heavyDashDistance = Mathf.Max(_heavyDashDistance,
                    toTarget.magnitude + Mathf.Max(0f, _waterData.WaterHeavyTargetOvershoot));
            }
        }
        else if (!hasMoveInput)
        {
            // Initial and fallback targets follow the camera view cone.
            FindSoftTarget();
            if (_softTarget != null)
            {
                Vector3 toTarget = Vector3.ProjectOnPlane(
                    _softTarget.position - _playerController.transform.position, Vector3.up);
                if (toTarget.sqrMagnitude > 0.001f) _heavyDashDirection = toTarget.normalized;
            }
        }
    }

    public override void UpdateHeavyAttack(out bool isComplete)
    {
        var step = HeavyAttackStep;
        float previous = _timer;
        _timer = Mathf.Min(_heavyAttackDuration, _timer + Time.deltaTime * AttackSpeed);
        Vector3 offset = _playerController.transform.rotation * step.HitBoxOffset;
        Vector3 from = _playerController.transform.position + offset;
        float distance = _heavyDashDistance * (Mathf.Clamp01(_timer / _heavyDashDuration)
            - Mathf.Clamp01(previous / _heavyDashDuration));
        Vector3 movement = _heavyDashDirection * distance;
        movement.y = _playerController.VerticalVelocity * Time.deltaTime;
        _playerController.Controller.Move(movement);
        Vector3 to = _playerController.transform.position + offset;

        // Sweep the actual path, including enemies crossed entirely in one frame.
        float hitStart = Mathf.Max(previous, step.HitStartTime);
        float hitEnd = Mathf.Min(_timer, Mathf.Min(_heavyDashDuration, step.HitStartTime + step.HitDuration));
        if (_timer > previous && hitEnd > hitStart)
        {
            float moveInterval = Mathf.Min(_timer, _heavyDashDuration) - previous;
            Vector3 start = Vector3.Lerp(from, to, (hitStart - previous) / moveInterval);
            Vector3 end = Vector3.Lerp(from, to, (hitEnd - previous) / moveInterval);
            bool firstHit = true;
            var hits = Physics.OverlapCapsule(start, end, Mathf.Max(0.1f, step.HitBoxRadius), _enemyLayer);
            System.Array.Sort(hits, (a, b) => (a.ClosestPoint(start) - start).sqrMagnitude.CompareTo(
                (b.ClosestPoint(start) - start).sqrMagnitude));
            foreach (var collider in hits)
            {
                var target = collider.GetComponentInParent<IDamageable>();
                if (!(target is Component component)) continue;
                var stats = component.GetComponent<EnemyStat>();
                if ((stats != null && stats.IsDead) || !_heavyDashTargets.Add(target)) continue;
                float attack = _playerController.StatManager.GetStat(StatType.ST_ATK);
                var hit = new HitInfo(step.Damage * HeavyAttackDamageMultiplier * attack, Element,
                    _playerController.StatManager.GetStat(StatType.ST_CRT) / 100f,
                    _playerController.StatManager.GetStat(StatType.ST_CRTD) / 100f,
                    source: _playerController,
                    power: KnockbackMotion.Power(_heavyDashDirection, step.KnockbackForce),
                    poiseDamage: step.PoiseDamage, staggerResistLevel: step.StaggerResistLevel);
                var result = DamageManager.Apply(hit, target, component.gameObject);
                if (!result.Applied) continue;
                if (!HasHeavyChainTarget)
                {
                    _heavyChainTarget = component;
                    _heavyChainTargetCollider = collider;
                }
                if (firstHit) { _playerController.StartHitStop(); firstHit = false; }
                _playerController.ImpulseSource?.GenerateImpulse();
                OnHitEnemy(component.gameObject);
            }
        }
        // Movement can finish earlier; queued attacks wait for the entire attack motion.
        _heavyAttackFinished = _timer >= _heavyAttackDuration;
        isComplete = _heavyAttackFinished;
    }

    public override void EndHeavyAttack()
    {
        RestoreHeavyCollision();
        _heavyChainTarget = null;
        _heavyChainTargetCollider = null;
        _heavyAttackFinished = false;
        base.EndHeavyAttack();
    }

    private void RestoreHeavyCollision()
    {
        if (!_heavyCollisionOverride) return;
        _playerController.Controller.excludeLayers = _heavySavedExcludeLayers;
        _heavyCollisionOverride = false;
    }
    public override void BeginSkill()
    {
        base.BeginSkill();
        _flow.Charge(_waterData.WaterSkillUseGain);
        ApplySkillBuff();
        _savedExcludeLayers = _playerController.Controller.excludeLayers;
        _skillCollisionOverride = true;
        _playerController.Controller.excludeLayers |= LayerMask.GetMask("Enemy");
        _softTarget = null; // 스킬은 사용 시 플레이어가 바라보는 전방으로 진행합니다.
        _trailStart = _playerController.Controller.bounds.center;
        _dashHitTargets.Clear();
    }
    protected override void ExecuteHit(WeaponActionData step, float damage, float poiseDamage, HashSet<IDamageable> targets)
        => base.ExecuteHit(step,
            damage * (_currentAction == ActionType.HeavyAttack ? HeavyAttackDamageMultiplier : 1f),
            poiseDamage, _currentAction == ActionType.Skill ? _dashHitTargets : targets);

    public override void UpdateSkill(out bool isComplete)
    {
        base.UpdateSkill(out isComplete);
        var step = _weaponActionData.SkillSteps[_skillStep];
        if (_timer < FirstHitTime(step)) return;
        Vector3 end = _playerController.Controller.bounds.center;
        // 선딜 중 이미 통과한 구간도 첫 판정에 포함합니다. 매 적당 최초 돌진 1회만 적용.
        foreach (var collider in Physics.OverlapCapsule(_trailStart, end, Mathf.Max(0.1f, step.HitBoxRadius), _enemyLayer))
        {
            var target = collider.GetComponentInParent<IDamageable>();
            if (!(target is Component component) || !_dashHitTargets.Add(target)) continue;
            var result = DamageManager.Apply(EffectHit(step.Damage), target, component.gameObject);
            if (result.Applied) OnHitEnemy(component.gameObject);
        }
        _trailStart = end;
    }
    public override void EndSkill()
    {
        RestoreSkillCollision();
        base.EndSkill();
    }
    private void RestoreSkillCollision()
    {
        if (!_skillCollisionOverride) return;
        _playerController.Controller.excludeLayers = _savedExcludeLayers;
        _skillCollisionOverride = false;
    }
    public override void BeginUltimate()
    {
        base.BeginUltimate();
        _ultimateActive = true;
        _ultimateElapsed = 0f; _ultimateHits = 0;
        _ultimateTarget = _softTarget;
        _ultimateCenter = _softTarget != null ? _softTarget.position : _playerController.transform.position;
        _visibility.Clear();
        foreach (var renderer in _playerController.GetComponentsInChildren<Renderer>())
        {
            _visibility[renderer] = renderer.enabled;
            renderer.enabled = false;
        }
        // TODO: 만조의 춤 잔상 교차 베기/납도 모션.
        // _playerController.Animator.CrossFade("WaterDance", 0.1f);
    }
    public override bool BlocksIncomingDamage(float damage, Vector3 power, object source = null) => _ultimateActive;
    public override void UpdateUltimate(out bool isComplete)
    {
        _ultimateElapsed += Time.deltaTime;
        if (_ultimateTarget != null) _ultimateCenter = _ultimateTarget.position;
        while (_ultimateHits < 10 && _ultimateElapsed >= (_ultimateHits + 1) * 0.2f)
        {
            var hit = EffectHit(1.2f);
            bool firstHit = true;
            foreach (var target in FormEffectRunner.Targets(_ultimateCenter, _waterData.AreaRadius))
            {
                if (target == null) continue;
                var result = DamageManager.Apply(hit, (IDamageable)target, target.gameObject);
                if (!result.Applied) continue;

                // 일반 공격과 동일하게 실제 적중 시에만 타격 피드백을 재생합니다.
                if (firstHit)
                {
                    _playerController.StartHitStop();
                    firstHit = false;
                }
                _playerController.ImpulseSource?.GenerateImpulse();
            }
            _ultimateHits++;
        }
        isComplete = _ultimateHits >= 10;
    }
    public override void EndUltimate()
    {
        if (_ultimateTarget != null && _playerController.Controller.enabled)
        {
            Vector3 desired = _ultimateTarget.position - _ultimateTarget.forward * 1.5f;
            _playerController.Controller.Move(Vector3.ProjectOnPlane(desired - _playerController.transform.position, Vector3.up));
        }
        CleanupActionEffects();
        base.EndUltimate();
    }
    public override void CleanupTransientEffects()
    {
        CleanupActionEffects();
        RemoveSkillBuff();
    }

    private void CleanupActionEffects()
    {
        _ultimateActive = false;
        foreach (var item in _visibility) if (item.Key != null) item.Key.enabled = item.Value;
        _visibility.Clear();
        RestoreSkillCollision();
        RestoreHeavyCollision();
        _heavyChainTarget = null;
        _heavyChainTargetCollider = null;
        _heavyAttackFinished = false;
    }
}
