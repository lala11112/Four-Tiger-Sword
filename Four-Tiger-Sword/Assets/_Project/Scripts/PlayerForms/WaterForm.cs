using System.Collections.Generic;
using UnityEngine;

public class WaterForm : BaseForm, IHeavyAttackForm
{
    protected override float CurrentAttackAnimationSpeed
        => _currentAction == ActionType.Ultimate ? FixedActionSpeed : AttackSpeed;
    private readonly WaterFormActionDataSO _waterData;

    public override ElementType Element => ElementType.ELEMENT_WATER;
    public override float SkillSpCost => 15f;
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
    private bool _heavyFeedbackPlayed;
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
    protected override void OnHitEnemy(GameObject enemy, WeaponActionData step = null, HitEvent hitEvent = null)
    {
        base.OnHitEnemy(enemy, step, hitEvent);
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
        BeginHeavyAttack(continuing, HeavyAttackTarget, _heavyDashDirection);
        _heavyFeedbackPlayed = false;
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
        if (!continuing || !HasHeavyChainTarget)
        {
            FindSoftTarget();
            _heavyChainTarget = _softTarget != null ? _softTarget.GetComponent<IDamageable>() as Component : null;
            _heavyChainTargetCollider = SoftTargetCollider;
        }
        if (!HasHeavyChainTarget) return;

        // Snapshot the selected target at dash start; movement input only steers untargeted dashes.
        Bounds targetBounds = _heavyChainTargetCollider.bounds;
        Vector3 toTarget = Vector3.ProjectOnPlane(
            targetBounds.center - _playerController.Controller.bounds.center, Vector3.up);
        _heavyDashDirection = toTarget.sqrMagnitude > 0.001f
            ? toTarget.normalized : Vector3.ProjectOnPlane(_playerController.transform.forward, Vector3.up).normalized;
        if (_heavyDashDirection.sqrMagnitude < 0.001f) _heavyDashDirection = Vector3.forward;

        // Clear both bodies, then leave the configurable gap beyond the target's far side.
        Vector3 extents = targetBounds.extents;
        float targetExtent = Mathf.Abs(_heavyDashDirection.x) * extents.x
            + Mathf.Abs(_heavyDashDirection.z) * extents.z;
        var controller = _playerController.Controller;
        float playerRadius = controller.radius * Mathf.Max(
            Mathf.Abs(controller.transform.lossyScale.x), Mathf.Abs(controller.transform.lossyScale.z));
        _heavyDashDistance = toTarget.magnitude + targetExtent + playerRadius
            + Mathf.Max(0f, _waterData.WaterHeavyTargetOvershoot);
    }

    public override void UpdateHeavyAttack(out bool isComplete)
    {
        var step = HeavyAttackStep;
        float previous = _timer;
        _timer = Mathf.Min(_heavyAttackDuration, _timer + _playerController.ActionDeltaTime * AttackSpeed);
        Vector3 offset = _playerController.transform.rotation * step.HitBoxOffset;
        Vector3 from = _playerController.transform.position + offset;
        float distance = _heavyDashDistance * (Mathf.Clamp01(_timer / _heavyDashDuration)
            - Mathf.Clamp01(previous / _heavyDashDuration));
        Vector3 movement = _heavyDashDirection * distance;
        movement.y = _playerController.VerticalVelocity * _playerController.ActionDeltaTime;
        _playerController.Controller.Move(movement);
        Vector3 to = _playerController.transform.position + offset;
        UpdateAttackCamera(step);
        UpdateSlashEffects(step);
        // Trail timing follows the full attack motion, including the dash recovery.
        _playerController.UpdateAttackTrails(step, _timer);

        // Sweep the actual path, including enemies crossed entirely in one frame.
        float hitStart = Mathf.Max(previous, step.HitStartTime);
        float hitEnd = Mathf.Min(_timer, Mathf.Min(_heavyDashDuration, step.HitStartTime + step.HitDuration));
        if (_timer > previous && hitEnd > hitStart)
        {
            PlaySwingSound(step);
            float moveInterval = Mathf.Min(_timer, _heavyDashDuration) - previous;
            Vector3 start = Vector3.Lerp(from, to, (hitStart - previous) / moveInterval);
            Vector3 end = Vector3.Lerp(from, to, (hitEnd - previous) / moveInterval);
            var hits = Physics.OverlapCapsule(start, end, Mathf.Max(0.1f, step.HitBoxRadius), _enemyLayer);
            System.Array.Sort(hits, (a, b) => (a.ClosestPoint(start) - start).sqrMagnitude.CompareTo(
                (b.ClosestPoint(start) - start).sqrMagnitude));
            foreach (var collider in hits)
            {
                var target = collider.GetComponentInParent<IDamageable>();
                if (!(target is Component component)) continue;
                var stats = component.GetComponent<EnemyStat>();
                if ((stats != null && stats.IsDead) || !_heavyDashTargets.Add(target)) continue;
                float attack = DamageAttackStat;
                var hit = new HitInfo(step.Damage * HeavyAttackDamageMultiplier * attack, Element,
                    _playerController.StatManager.GetStat(StatType.ST_CRT) / 100f,
                    _playerController.StatManager.GetStat(StatType.ST_CRTD) / 100f,
                    source: _playerController,
                    power: KnockbackMotion.Power(_heavyDashDirection, step.KnockbackForce),
                    poiseDamage: step.PoiseDamage, staggerResistLevel: step.StaggerResistLevel,
                    impactSound: ResolveHitSound(step), category: PlayerAttackCategory.Heavy,
                    sourceDamageMultiplier: DamageSourceMultiplier, hasSourceDamageSnapshot: true);
                var result = DamageManager.Apply(hit, target, component.gameObject);
                if (!result.Applied) continue;
                if (!HasHeavyChainTarget)
                {
                    _heavyChainTarget = component;
                    _heavyChainTargetCollider = collider;
                }
                ConfirmHitFeedback(step, component, result, direction: _heavyDashDirection);
                _heavyFeedbackPlayed = true;
                OnHitEnemy(component.gameObject, step);
            }
        }
        // Movement can finish earlier; queued attacks wait for the entire attack motion.
        _heavyAttackFinished = _timer >= _heavyAttackDuration;
        isComplete = _heavyAttackFinished;
    }

    public override void EndHeavyAttack()
    {
        EndAttackCamera(!_heavyAttackFinished);
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
        PrepareSkillAttack();
    }
    public override void BeginSwitchAttack()
    {
        base.BeginSwitchAttack();
        PrepareSkillAttack();
    }
    private void PrepareSkillAttack()
    {
        _savedExcludeLayers = _playerController.Controller.excludeLayers;
        _skillCollisionOverride = true;
        _playerController.Controller.excludeLayers |= LayerMask.GetMask("Enemy");
        _softTarget = null; // 스킬은 사용 시 플레이어가 바라보는 전방으로 진행합니다.
        _trailStart = _playerController.Controller.bounds.center;
        _dashHitTargets.Clear();
    }
    protected override void ExecuteHit(WeaponActionData step, float damage, float poiseDamage, HashSet<IDamageable> targets, HitEvent hitEvent = null)
        => base.ExecuteHit(step,
            damage * (_currentAction == ActionType.HeavyAttack ? HeavyAttackDamageMultiplier : 1f),
            poiseDamage, _currentAction == ActionType.Skill || _currentAction == ActionType.SwitchAttack ? _dashHitTargets : targets, hitEvent);

    public override void UpdateSkill(out bool isComplete)
    {
        base.UpdateSkill(out isComplete);
        var step = _weaponActionData.SkillSteps[_skillStep];
        if (_playerController.ActionDeltaTime <= 0f || _timer < FirstHitTime(step)) return;
        Vector3 end = _playerController.Controller.bounds.center;
        // 선딜 중 이미 통과한 구간도 첫 판정에 포함합니다. 매 적당 최초 돌진 1회만 적용.
        foreach (var collider in Physics.OverlapCapsule(_trailStart, end, Mathf.Max(0.1f, step.HitBoxRadius), _enemyLayer))
        {
            var target = collider.GetComponentInParent<IDamageable>();
            if (!(target is Component component) || !_dashHitTargets.Add(target)) continue;
            HitEvent hitEvent = FirstHitEvent(step, out int hitIndex);
            var result = DamageManager.Apply(EffectHit(step.Damage, step: step,
                power: CustomKnockbackPower(step, hitEvent, component.transform)), target, component.gameObject);
            if (result.Applied)
            {
                ConfirmHitFeedback(step, component, result, hitEvent, hitIndex, legacyFeedbackDisabled: true);
                OnHitEnemy(component.gameObject, step);
            }
        }
        _trailStart = end;
    }
    public override void EndSkill()
    {
        RestoreSkillCollision();
        base.EndSkill();
    }
    public override void EndSwitchAttack()
    {
        RestoreSkillCollision();
        _dashHitTargets.Clear();
        base.EndSwitchAttack();
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
        _playerController.SetAnimationSpeed(FixedActionSpeed);
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
    public override void UpdateUltimate(out bool isComplete) => AdvanceUltimate(_playerController.ActionDeltaTime, out isComplete);
    private void AdvanceUltimate(float dt, out bool isComplete)
    {
        _playerController.SetAnimationSpeed(FixedActionSpeed);
        _ultimateElapsed += Mathf.Max(0f, dt) * FixedActionSpeed;
        var cameraStep = _weaponActionData.UltimateSteps?.Count > 0 ? _weaponActionData.UltimateSteps[0] : null;
        if (HasCameraV2(cameraStep))
            UpdateAttackCamera(cameraStep, _ultimateElapsed / 2f);
        if (_ultimateTarget != null) _ultimateCenter = _ultimateTarget.position;
        while (_ultimateHits < 10 && _ultimateElapsed >= (_ultimateHits + 1) * 0.2f)
        {
            var steps = _weaponActionData.UltimateSteps;
            var step = steps != null && steps.Count > 0 ? steps[0] : null;
            var hitEvent = step?.HitEvents != null && _ultimateHits < step.HitEvents.Count
                ? step.HitEvents[_ultimateHits] : null;
            PlaySwingSound(step, hitEvent, _ultimateHits);
            foreach (var target in FormEffectRunner.Targets(_ultimateCenter, _waterData.AreaRadius))
            {
                if (target == null) continue;
                var hit = EffectHit(1.2f, step: step, hitEvent: hitEvent,
                    power: CustomKnockbackPower(step, hitEvent, target.transform));
                var result = DamageManager.Apply(hit, (IDamageable)target, target.gameObject);
                if (!result.Applied) continue;

                ConfirmHitFeedback(step, target, result, hitEvent, _ultimateHits, legacyCameraFallbackOnly: true);
            }
            _ultimateHits++;
            // A newly applied attacker stop postpones any remaining scheduled hits.
            if (_playerController.ActionDeltaTime <= 0f) break;
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

    public override void CleanupStageEffects()
    {
        CleanupActionEffects();
        _ultimateTarget = null;
        _dashHitTargets.Clear();
        _heavyDashTargets.Clear();
        base.CleanupStageEffects();
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
