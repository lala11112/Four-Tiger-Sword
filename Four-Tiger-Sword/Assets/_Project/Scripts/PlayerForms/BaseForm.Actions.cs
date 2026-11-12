using UnityEngine;

public abstract partial class BaseForm
{
    public virtual void BeginAttack()
    {
        ResetActionTiming();
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

        _playerController.SetAnimationSpeed(AttackSpeed);
        _timer += _playerController.ActionDeltaTime * AttackSpeed;
        ApplyFinisherFacing();

        if (!IsCurrentActionExecutionComplete)
        {
            _playerController.UpdateAttackTrails(currentStep, _timer);
            MoveForward(currentStep);
            ProcessHit(currentStep);
        }
        isComplete = CompleteActionExecution(_timer >= currentStep.Duration, !_finisherAttackActive);
    }

    public bool CanChainAttack => !_finisherAttackActive && HasSteps(_weaponActionData?.ComboSteps)
        && CurrentNormalAttackStep != null && _timer >= CurrentNormalAttackStep.ComboTransitionTime
        && _timer >= LastExecutableHitEnd(CurrentNormalAttackStep);

    public bool CanCancelAttackRecovery => _currentAction == ActionType.Attack && CanCancelCurrentActionToMove;

    public bool CanRestartNormalCombo => _currentAction == ActionType.Attack && CanCancelCurrentActionToAction
        && HasSteps(_weaponActionData?.ComboSteps) && _comboStep == _weaponActionData.ComboSteps.Count - 1;

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
            || CompleteActionExecution(_timer >= _weaponActionData.AirAttackStartDuration);
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
        if (!IsCurrentActionExecutionComplete)
        {
            RotateTowardSoftTarget();
            _playerController.UpdateAttackTrails(_weaponActionData.AirAttackStep, _timer);
            ProcessHit(_weaponActionData.AirAttackStep);
        }
        isComplete = CompleteActionExecution(_timer >= _weaponActionData.AirAttackStep.Duration);
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
        BeginItemCast();
        _currentAction = ActionType.Skill;
        _skillStep = 0;
        FindSoftTarget();
        _playerController.SetAnimationSpeed(AttackSpeed);
        PlaySkillStep();
    }

    public virtual void UpdateSkill(out bool isComplete)
        => UpdateSkillSteps(_weaponActionData?.SkillSteps, out isComplete);

    private void UpdateSkillSteps(System.Collections.Generic.List<WeaponActionData> steps, out bool isComplete)
    {
        isComplete = false;
        if (!HasSteps(steps)) { isComplete = true; return; }

        _playerController.SetAnimationSpeed(AttackSpeed);
        _timer += _playerController.ActionDeltaTime * AttackSpeed;
        WeaponActionData step = steps[_skillStep];
        if (!IsCurrentActionExecutionComplete)
        {
            _playerController.UpdateAttackTrails(step, _timer);
            MoveForward(step);
            ProcessHit(step);
        }

        if (_timer >= step.Duration)
        {
            if (_skillStep < steps.Count - 1) { _skillStep++; PlaySkillStep(); }
            else isComplete = CompleteActionExecution(true, _currentAction != ActionType.SwitchAttack);
        }
        else CompleteActionExecution(false, _currentAction != ActionType.SwitchAttack);
    }

    public virtual void EndSkill()
    {
        EndAttackCamera();
        _skillStep = 0;
        _softTarget = null;
        ClearHitTargets();
        _skillCooldownTimer = Mathf.Max(0f, SkillCooldown);
        _playerController.SetAnimationSpeed(1f);
    }

    // Uses independently authored switch steps and never consumes E resources or starts its cooldown.
    public virtual void BeginSwitchAttack()
    {
        if (!CanSwitchAttack) return;
        BeginItemCast();
        _currentAction = ActionType.SwitchAttack;
        ApplyChainBuff();
        SnapshotAttackDamage();
        _skillStep = 0;
        FindSoftTarget();
        _playerController.SetAnimationSpeed(AttackSpeed);
        PlaySkillStep();
    }

    public virtual void UpdateSwitchAttack(out bool isComplete)
    {
        if (!CanSwitchAttack) { isComplete = true; return; }
        UpdateSkillSteps(_weaponActionData.SwitchAttackSteps, out isComplete);
    }

    public virtual void EndSwitchAttack()
    {
        _chainVulnerabilityBonusPercent = _chainVulnerabilityDuration = 0f;
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
        BeginItemCast();
        ResetActionTiming();
        _ultimateCooldownTimer = BaseUltimateCooldown;
        _playerController.StatManager.ConsumeAllUltimateGauge();
    }

    public virtual void UpdateUltimate(out bool isComplete)
    {
        isComplete = false;
        if (_weaponActionData == null || _weaponActionData.UltimateSteps == null || _weaponActionData.UltimateSteps.Count == 0) { isComplete = true; return; }

        _playerController.SetAnimationSpeed(AttackSpeed);
        _timer += _playerController.ActionDeltaTime * AttackSpeed;
        WeaponActionData step = _weaponActionData.UltimateSteps[_ultimateStep];
        if (!IsCurrentActionExecutionComplete)
        {
            _playerController.UpdateAttackTrails(step, _timer);
            MoveForward(step);
            ProcessHit(step);
        }

        if (_timer >= step.Duration)
        {
            if (_ultimateStep < _weaponActionData.UltimateSteps.Count - 1) { _ultimateStep++; PlayUltimateStep(); }
            else isComplete = CompleteActionExecution(true);
        }
        else CompleteActionExecution(false);
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
        ResetActionTiming();
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

        _playerController.SetAnimationSpeed(AttackSpeed);
        _timer += _playerController.ActionDeltaTime * AttackSpeed;
        if (!IsCurrentActionExecutionComplete)
        {
            _playerController.UpdateAttackTrails(step, _timer);
            MoveForward(step);
            ProcessHit(step);
        }
        isComplete = CompleteActionExecution(_timer >= step.Duration);
    }

    public virtual void EndCounter()
    {
        EndAttackCamera();
        _softTarget = null;
        ClearHitTargets();
        _playerController.SetAnimationSpeed(1f);
    }

}
