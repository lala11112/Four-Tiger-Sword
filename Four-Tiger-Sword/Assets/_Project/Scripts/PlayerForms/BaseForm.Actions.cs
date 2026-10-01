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

        MoveForward(currentStep);
        ProcessHit(currentStep);

        if (_timer >= currentStep.Duration)
            isComplete = true;
    }

    public bool CanChainAttack => HasSteps(_weaponActionData?.ComboSteps)
        && _timer >= _weaponActionData.ComboSteps[_comboStep].ComboTransitionTime;

    public bool TryContinueAttack()
    {
        if (!CanChainAttack || _comboStep >= _weaponActionData.ComboSteps.Count - 1) return false;
        _comboStep++;
        PlayCombo();
        return true;
    }

    public virtual void EndAttack()
    {
        _comboStep = 0;
        _softTarget = null;
        ClearHitTargets();
        _playerController.Animator.speed = 1f;
    }

    // ── 패링 ─────────────────────────────────────────────────────────────────

    public virtual void BeginParry()
    {
        _playerController.Animator.speed = 1f;
        PlayActionAnimation(_weaponActionData?.ParryAnimationName, 0f);
    }

    public virtual void EndParry()
    {
        _playerController.Animator.speed = 1f;
    }

    // ── 공중 공격: 시작 → 하강 반복 → 착지 마무리 ──────────────────────────

    public virtual void BeginAirAttack()
    {
        _currentAction = ActionType.AirAttack;
        _timer = 0;
        _softTarget = null;
        ClearHitTargets();
        _playerController.Animator.speed = AttackSpeed;
        PlayActionAnimation(_weaponActionData?.AirAttackStartAnimationName, 0.05f);
    }

    public virtual void UpdateAirAttackStart(out bool isComplete)
    {
        _playerController.Animator.speed = AttackSpeed;
        _timer += Time.deltaTime * AttackSpeed;
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
        ClearHitTargets();
        _playerController.Animator.speed = AttackSpeed;
        PlayActionAnimation(_weaponActionData?.AirAttackStep?.AnimationName, 0.05f);
    }

    public virtual void UpdateAirAttack(out bool isComplete)
    {
        isComplete = false;
        if (_weaponActionData == null || _weaponActionData.AirAttackStep == null) { isComplete = true; return; }

        _playerController.Animator.speed = AttackSpeed;
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
        _skillStep = 0;
        _softTarget = null;
        ClearHitTargets();
        _skillCooldownTimer = SkillCooldown;
        _playerController.Animator.speed = 1f;
    }

    // ── 궁극기 ───────────────────────────────────────────────────────────────

    public virtual void BeginUltimate()
    {
        ConsumeUltimateResources();
        _currentAction = ActionType.Ultimate;
        _ultimateStep = 0;
        FindSoftTarget();
        _playerController.Animator.speed = AttackSpeed;
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

        _timer += Time.deltaTime * AttackSpeed;
        WeaponActionData step = _weaponActionData.UltimateSteps[_ultimateStep];
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
        _motionStep = null;
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
        MoveForward(step);
        ProcessHit(step);

        if (_timer >= step.Duration)
            isComplete = true;
    }

    public virtual void EndCounter()
    {
        _softTarget = null;
        ClearHitTargets();
        _playerController.Animator.speed = 1f;
    }

}
