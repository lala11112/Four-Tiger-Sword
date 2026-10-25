using UnityEngine;

public abstract partial class BaseForm
{
    public virtual void BeginAttack()
    {
        ResetFinisherAttack();
        _normalAttackActive = true;
        _currentAction = ActionType.Attack;
        _comboStep = 0;
        _timer = 0f;
        ClearHitTargets();
        if (!HasSteps(_weaponActionData?.ComboSteps)) return;
        _playerController.SetAnimationSpeed(AttackSpeed);
        PlayCombo();
    }

    public virtual void UpdateAttack(out bool isComplete)
    {
        isComplete = false;

        WeaponActionData currentStep = CurrentNormalAttackStep;
        if (currentStep == null)
        {
            isComplete = true;
            return;
        }

        _timer += _playerController.ActionDeltaTime * AttackSpeed;
        ApplyFinisherFacing();

        _playerController.UpdateAttackTrails(currentStep, _timer);
        MoveForward(currentStep);
        ProcessHit(currentStep);

        if (_timer >= currentStep.Duration)
            isComplete = true;
    }

    public bool CanChainAttack => !_finisherAttackActive && HasSteps(_weaponActionData?.ComboSteps)
        && CurrentNormalAttackStep != null && _timer >= CurrentNormalAttackStep.ComboTransitionTime;

    public bool TryContinueAttack()
    {
        if (!CanChainAttack || _comboStep >= _weaponActionData.ComboSteps.Count - 1) return false;
        _comboStep++;
        PlayCombo();
        return true;
    }

    public virtual void EndAttack()
    {
        EndAttackCamera();
        ResetFinisherAttack();
        _comboStep = 0;
        _softTarget = null;
        ClearHitTargets();
        _playerController.SetAnimationSpeed(1f);
    }

    // ── 패링 ─────────────────────────────────────────────────────────────────

    public virtual void BeginParry()
    {
        _playerController.SetAnimationSpeed(1f);
        PlayActionAnimation(_weaponActionData?.ParryAnimationName, 0f);
    }

    public virtual void EndParry()
    {
        _playerController.SetAnimationSpeed(1f);
    }

    // ── 공중 공격: 시작 → 하강 반복 → 착지 마무리 ──────────────────────────

    public virtual void BeginAirAttack()
    {
        _currentAction = ActionType.AirAttack;
        SnapshotAttackDamage();
        _timer = 0;
        _softTarget = null;
        ClearHitTargets();
        _playerController.SetAnimationSpeed(AttackSpeed);
        PlayActionAnimation(_weaponActionData?.AirAttackStartAnimationName, 0.05f);
    }

    public virtual void UpdateAirAttackStart(out bool isComplete)
    {
        _playerController.SetAnimationSpeed(AttackSpeed);
        _timer += _playerController.ActionDeltaTime * AttackSpeed;
        isComplete = _weaponActionData == null
            || string.IsNullOrWhiteSpace(_weaponActionData.AirAttackStartAnimationName)
            || _timer >= _weaponActionData.AirAttackStartDuration;
    }

    public virtual void BeginAirAttackLoop()
    {
        _timer = 0f;
        PlayActionAnimation(_weaponActionData?.AirAttackLoopAnimationName, 0.05f);
    }

    public virtual void BeginAirAttackFinish()
    {
        _timer = 0f;
        FindSoftTarget();
        BeginAttackCamera(_weaponActionData?.AirAttackStep);
        ClearHitTargets();
        _playerController.SetAnimationSpeed(AttackSpeed);
        PlayActionAnimation(_weaponActionData?.AirAttackStep?.AnimationName, 0.05f);
        _playerController.UpdateAttackTrails(_weaponActionData?.AirAttackStep, 0f);
    }

    public virtual void UpdateAirAttack(out bool isComplete)
    {
        isComplete = false;
        if (_weaponActionData == null || _weaponActionData.AirAttackStep == null) { isComplete = true; return; }

        _playerController.SetAnimationSpeed(AttackSpeed);
        _timer += _playerController.ActionDeltaTime * AttackSpeed;
        RotateTowardSoftTarget();
        _playerController.UpdateAttackTrails(_weaponActionData.AirAttackStep, _timer);
        ProcessHit(_weaponActionData.AirAttackStep);
        isComplete = _timer >= _weaponActionData.AirAttackStep.Duration;
    }

    public virtual void EndAirAttack()
    {
        EndAttackCamera();
        _softTarget = null;
        ClearHitTargets();
        _playerController.SetAnimationSpeed(1f);
    }

    // ── 스킬 ─────────────────────────────────────────────────────────────────

    public virtual void BeginSkill()
    {
        _currentAction = ActionType.Skill;
        _playerController.StatManager.TryConsumeSp(SkillSpCost);
        _skillStep = 0;
        FindSoftTarget();
        _playerController.SetAnimationSpeed(AttackSpeed);
        PlaySkillStep();
    }

    public virtual void UpdateSkill(out bool isComplete)
    {
        isComplete = false;
        if (_weaponActionData == null || _weaponActionData.SkillSteps == null || _weaponActionData.SkillSteps.Count == 0) { isComplete = true; return; }

        _timer += _playerController.ActionDeltaTime * AttackSpeed;
        WeaponActionData step = _weaponActionData.SkillSteps[_skillStep];
        _playerController.UpdateAttackTrails(step, _timer);
        MoveForward(step);
        ProcessHit(step);

        if (_timer >= step.Duration)
        {
            if (_skillStep < _weaponActionData.SkillSteps.Count - 1) { _skillStep++; PlaySkillStep(); }
            else isComplete = true;
        }
    }

    public virtual void EndSkill()
    {
        EndAttackCamera();
        _skillStep = 0;
        _softTarget = null;
        ClearHitTargets();
        _skillCooldownTimer = SkillCooldown;
        _playerController.SetAnimationSpeed(1f);
    }

    // Shares the authored skill attack, but never consumes E resources or starts its cooldown.
    public virtual void BeginSwitchAttack()
    {
        _currentAction = ActionType.SwitchAttack;
        SnapshotAttackDamage();
        _skillStep = 0;
        FindSoftTarget();
        _playerController.SetAnimationSpeed(AttackSpeed);
        if (CanSwitchAttack) PlaySkillStep();
    }

    public virtual void UpdateSwitchAttack(out bool isComplete)
    {
        if (!CanSwitchAttack) { isComplete = true; return; }
        UpdateSkill(out isComplete);
    }

    public virtual void EndSwitchAttack()
    {
        EndAttackCamera();
        _skillStep = 0;
        _softTarget = null;
        ClearHitTargets();
        _playerController.SetAnimationSpeed(1f);
    }

    // ── 궁극기 ───────────────────────────────────────────────────────────────

    public virtual void BeginUltimate()
    {
        ConsumeUltimateResources();
        _currentAction = ActionType.Ultimate;
        _ultimateStep = 0;
        FindSoftTarget();
        _playerController.SetAnimationSpeed(AttackSpeed);
        PlayUltimateStep();
    }

    protected void ConsumeUltimateResources()
    {
        _ultimateCooldownTimer = _weaponActionData != null ? _weaponActionData.UltimateCooldown : 30f;
        _playerController.StatManager.ConsumeAllUltimateGauge();
    }

    public virtual void UpdateUltimate(out bool isComplete)
    {
        isComplete = false;
        if (_weaponActionData == null || _weaponActionData.UltimateSteps == null || _weaponActionData.UltimateSteps.Count == 0) { isComplete = true; return; }

        _timer += _playerController.ActionDeltaTime * AttackSpeed;
        WeaponActionData step = _weaponActionData.UltimateSteps[_ultimateStep];
        _playerController.UpdateAttackTrails(step, _timer);
        MoveForward(step);
        ProcessHit(step);

        if (_timer >= step.Duration)
        {
            if (_ultimateStep < _weaponActionData.UltimateSteps.Count - 1) { _ultimateStep++; PlayUltimateStep(); }
            else isComplete = true;
        }
    }

    public virtual void EndUltimate()
    {
        EndAttackCamera();
        _ultimateStep = 0;
        _softTarget = null;
        ClearHitTargets();
        _playerController.SetAnimationSpeed(1f);
    }

    // ── 반격 (패링 반격 윈도우에서만 진입 가능) ───────────────────────────────

    public virtual void BeginCounter()
    {
        _currentAction = ActionType.Counter;
        _timer = 0f;
        _motionStep = null;
        FindSoftTarget();
        BeginAttackCamera(_weaponActionData?.CounterStep
            ?? (_weaponActionData?.ComboSteps?.Count > 0 ? _weaponActionData.ComboSteps[0] : null));
        ClearHitTargets();
        _playerController.SetAnimationSpeed(AttackSpeed);
        // CounterStep이 없으면 첫 번째 콤보 스텝을 폴백으로 사용
        if (_weaponActionData?.CounterStep != null)
            PlayActionAnimation(_weaponActionData.CounterStep.AnimationName, 0.05f);
        else if (_weaponActionData?.ComboSteps?.Count > 0 && _weaponActionData.ComboSteps[0] != null)
            PlayActionAnimation(_weaponActionData.ComboSteps[0].AnimationName, 0.05f);
        _playerController.UpdateAttackTrails(_weaponActionData?.CounterStep
            ?? (_weaponActionData?.ComboSteps?.Count > 0 ? _weaponActionData.ComboSteps[0] : null), 0f);
    }

    public virtual void UpdateCounter(out bool isComplete)
    {
        isComplete = false;
        WeaponActionData step = _weaponActionData?.CounterStep
                             ?? (_weaponActionData?.ComboSteps?.Count > 0
                                 ? _weaponActionData.ComboSteps[0] : null);

        if (step == null) { isComplete = true; return; }

        _timer += _playerController.ActionDeltaTime * AttackSpeed;
        _playerController.UpdateAttackTrails(step, _timer);
        MoveForward(step);
        ProcessHit(step);

        if (_timer >= step.Duration)
            isComplete = true;
    }

    public virtual void EndCounter()
    {
        EndAttackCamera();
        _softTarget = null;
        ClearHitTargets();
        _playerController.SetAnimationSpeed(1f);
    }

}
