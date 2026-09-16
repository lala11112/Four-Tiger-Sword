using UnityEngine;

public abstract partial class BaseForm
{
    public virtual void BeginAttack()
    {
        _currentAction = ActionType.Attack;
        _comboStep = 0;
        _timer = 0f;
        ClearHitTargets();
        if (!HasSteps(_weaponActionData?.ComboSteps)) return;
        _playerController.Animator.speed = AttackSpeed;
        PlayCombo();
    }

    public virtual void UpdateAttack(out bool isComplete)
    {
        isComplete = false;

        if (!HasSteps(_weaponActionData?.ComboSteps))
        {
            isComplete = true;
            return;
        }

        _timer += Time.deltaTime * AttackSpeed;
        WeaponActionData currentStep = _weaponActionData.ComboSteps[_comboStep];

        ProcessHit(currentStep);

        MoveForward(currentStep);

        if (_timer >= currentStep.ComboTransitionTime &&
            _playerController.Input.AttackBuffer.IsActive &&
            _comboStep < _weaponActionData.ComboSteps.Count - 1)
        {
            _comboStep++;
            PlayCombo();
            return;
        }

        if (_timer >= currentStep.Duration)
            isComplete = true;
    }

    public virtual void EndAttack()
    {
        _comboStep = 0;
        _softTarget = null;
        ClearHitTargets();
        _playerController.Animator.speed = 1f;
    }

    // ── 공중 공격 ─────────────────────────────────────────────────────────────

    public virtual void BeginAirAttack()
    {
        _currentAction = ActionType.AirAttack;
        _timer = 0;
        FindSoftTarget();
        ClearHitTargets();
        _playerController.Animator.speed = AttackSpeed;
    }

    public virtual void UpdateAirAttack(out bool isComplete)
    {
        isComplete = false;
        if (_weaponActionData == null || _weaponActionData.AirAttackStep == null) { isComplete = true; return; }

        _timer += Time.deltaTime * AttackSpeed;
        RotateTowardSoftTarget();
        ProcessHit(_weaponActionData.AirAttackStep);
        isComplete = _timer >= _weaponActionData.AirAttackStep.Duration;
    }

    public virtual void EndAirAttack()
    {
        _softTarget = null;
        ClearHitTargets();
        _playerController.Animator.speed = 1f;
    }

    // ── 스킬 ─────────────────────────────────────────────────────────────────

    public virtual void BeginSkill()
    {
        _currentAction = ActionType.Skill;
        _playerController.StatManager.TryConsumeSp(SkillSpCost);
        _skillStep = 0;
        FindSoftTarget();
        _playerController.Animator.speed = AttackSpeed;
        PlaySkillStep();
    }

    public virtual void UpdateSkill(out bool isComplete)
    {
        isComplete = false;
        if (_weaponActionData == null || _weaponActionData.SkillSteps == null || _weaponActionData.SkillSteps.Count == 0) { isComplete = true; return; }

        _timer += Time.deltaTime * AttackSpeed;
        WeaponActionData step = _weaponActionData.SkillSteps[_skillStep];
        ProcessHit(step);
        MoveForward(step);

        if (_timer >= step.Duration)
        {
            if (_skillStep < _weaponActionData.SkillSteps.Count - 1) { _skillStep++; PlaySkillStep(); }
            else isComplete = true;
        }
    }

    public virtual void EndSkill()
    {
        _skillStep = 0;
        _softTarget = null;
        ClearHitTargets();
        _skillCooldownTimer = SkillCooldown;
        _playerController.Animator.speed = 1f;
    }

    // ── 궁극기 ───────────────────────────────────────────────────────────────

    public virtual void BeginUltimate()
    {
        _ultimateCooldownTimer = _weaponActionData != null ? _weaponActionData.UltimateCooldown : 30f;
        _currentAction = ActionType.Ultimate;
        _playerController.StatManager.ConsumeAllUltimateGauge();
        _ultimateStep = 0;
        FindSoftTarget();
        _playerController.Animator.speed = AttackSpeed;
        PlayUltimateStep();
    }

    public virtual void UpdateUltimate(out bool isComplete)
    {
        isComplete = false;
        if (_weaponActionData == null || _weaponActionData.UltimateSteps == null || _weaponActionData.UltimateSteps.Count == 0) { isComplete = true; return; }

        _timer += Time.deltaTime * AttackSpeed;
        WeaponActionData step = _weaponActionData.UltimateSteps[_ultimateStep];
        ProcessHit(step);
        MoveForward(step);

        if (_timer >= step.Duration)
        {
            if (_ultimateStep < _weaponActionData.UltimateSteps.Count - 1) { _ultimateStep++; PlayUltimateStep(); }
            else isComplete = true;
        }
    }

    public virtual void EndUltimate()
    {
        _ultimateStep = 0;
        _softTarget = null;
        ClearHitTargets();
        _playerController.Animator.speed = 1f;
    }

    // ── 반격 (패링 반격 윈도우에서만 진입 가능) ───────────────────────────────

    public virtual void BeginCounter()
    {
        _currentAction = ActionType.Counter;
        _timer = 0f;
        FindSoftTarget();
        ClearHitTargets();
        _playerController.Animator.speed = AttackSpeed;
        // CounterStep이 없으면 첫 번째 콤보 스텝을 폴백으로 사용
        if (_weaponActionData?.CounterStep != null)
            PlayActionAnimation(_weaponActionData.CounterStep.AnimationName, 0.05f);
        else if (_weaponActionData?.ComboSteps?.Count > 0 && _weaponActionData.ComboSteps[0] != null)
            PlayActionAnimation(_weaponActionData.ComboSteps[0].AnimationName, 0.05f);
    }

    public virtual void UpdateCounter(out bool isComplete)
    {
        isComplete = false;
        WeaponActionData step = _weaponActionData?.CounterStep
                             ?? (_weaponActionData?.ComboSteps?.Count > 0
                                 ? _weaponActionData.ComboSteps[0] : null);

        if (step == null) { isComplete = true; return; }

        _timer += Time.deltaTime * AttackSpeed;
        ProcessHit(step);
        MoveForward(step);

        if (_timer >= step.Duration)
            isComplete = true;
    }

    public virtual void EndCounter()
    {
        _softTarget = null;
        ClearHitTargets();
        _playerController.Animator.speed = 1f;
    }

    // ── 공통 이동 헬퍼 ────────────────────────────────────────────────────────

    private Collider _softTargetCollider;
    private bool _approachingTarget;
    private float _approachTravel;
    private float _previousMoveTime;

    private void ResetTargetApproach(WeaponActionData step)
    {
        _approachTravel = 0f;
        _previousMoveTime = 0f;
        FindSoftTarget(step.UseTargetApproach);
        _approachingTarget = step.UseTargetApproach && _softTargetCollider != null;
    }

    private static float LimitApproachDistance(float requested, float surfaceGap, float stopDistance, float remainingBudget)
        => Mathf.Min(Mathf.Max(0f, requested), Mathf.Max(0f, surfaceGap - stopDistance), Mathf.Max(0f, remainingBudget));

    protected void MoveForward(WeaponActionData step)
    {
        if (step.Duration <= 0f) return;

        // 이번 단계는 일반 콤보에만 적용합니다. 스킬의 고유 돌진 처리는 유지합니다.
        bool useApproach = _currentAction == ActionType.Attack && step.UseTargetApproach && _approachingTarget;
        bool targetAlive = _softTarget != null && _softTargetCollider != null
            && _softTargetCollider.enabled && _softTargetCollider.gameObject.activeInHierarchy;
        float previousTime = _previousMoveTime;
        _previousMoveTime = _timer;

        if (!useApproach)
            RotateTowardSoftTarget();
        else if (targetAlive && _timer <= step.RotationEndTime)
        {
            Vector3 toTarget = Vector3.ProjectOnPlane(_softTarget.position - _playerController.transform.position, Vector3.up);
            if (Vector3.Dot(_playerController.transform.forward, toTarget) > 0f)
                RotateTowardSoftTarget();
        }

        // 1. 현재 애니메이션이 몇 % 진행되었는지 구함 (0.0 ~ 1.0)
        float normalizedTime = Mathf.Clamp01(_timer / step.Duration);

        // 2. 커브에서 현재 %에 해당하는 값을 빼와서 Multiplier를 곱함
        float currentThrust = (step.ThrustCurve?.Evaluate(normalizedTime) ?? 0f) * step.ThrustMultiplier;

        float distance = currentThrust * Time.deltaTime;
        if (useApproach && distance > 0f)
        {
            if (!targetAlive || previousTime >= step.ApproachEndTime)
                distance = 0f;
            else
            {
                // 종료 시점을 넘는 프레임은 남은 구간만 사용합니다.
                float interval = _timer - previousTime;
                if (interval > 0f)
                    distance *= Mathf.Clamp01((step.ApproachEndTime - previousTime) / interval);
                CharacterController controller = _playerController.Controller;
                Vector3 center = controller.bounds.center;
                Vector3 closest = _softTargetCollider.ClosestPoint(center);
                float radius = controller.radius * Mathf.Max(Mathf.Abs(controller.transform.lossyScale.x), Mathf.Abs(controller.transform.lossyScale.z));
                float gap = Vector3.ProjectOnPlane(closest - center, Vector3.up).magnitude - radius;
                distance = LimitApproachDistance(distance, gap, step.StopDistance, step.MaxApproachDistance - _approachTravel);
                if (Vector3.Dot(_playerController.transform.forward, Vector3.ProjectOnPlane(_softTarget.position - center, Vector3.up)) <= 0f)
                    distance = 0f;
            }
        }

        Vector3 before = _playerController.transform.position;
        Vector3 delta = _playerController.transform.forward * distance;
        delta.y = _playerController.VerticalVelocity * Time.deltaTime;
        _playerController.Controller.Move(delta);
        if (useApproach && distance > 0f)
            _approachTravel += Vector3.ProjectOnPlane(_playerController.transform.position - before, Vector3.up).magnitude;
    }
}
