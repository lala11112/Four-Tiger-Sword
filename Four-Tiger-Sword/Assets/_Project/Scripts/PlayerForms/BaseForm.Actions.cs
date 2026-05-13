using UnityEngine;

public abstract partial class BaseForm
{
    public virtual void BeginAttack()
    {
        _currentAction = ActionType.Attack;
        _comboStep = 0;
        FindSoftTarget();
        _playerController.Animator.speed = AttackSpeed;
        PlayCombo();
    }

    public abstract void UpdateAttack(out bool isComplete);

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
        _currentAction = ActionType.Ultimate;
        _playerController.StatManager.TryConsumeSp(UltimateSpCost);
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
        _ultimateCooldownTimer = UltimateCooldown;
        _playerController.Animator.speed = 1f;
    }

    // ── 공통 이동 헬퍼 ────────────────────────────────────────────────────────

    protected void MoveForward(WeaponActionData step)
    {
        if (step.Duration <= 0f) return;

        RotateTowardSoftTarget();

        // 1. 현재 애니메이션이 몇 % 진행되었는지 구함 (0.0 ~ 1.0)
        float normalizedTime = Mathf.Clamp01(_timer / step.Duration);

        // 2. 커브에서 현재 %에 해당하는 값을 빼와서 Multiplier를 곱함
        float currentThrust = step.ThrustCurve.Evaluate(normalizedTime) * step.ThrustMultiplier;

        // 3. 이동 적용
        Vector3 vel = _playerController.transform.forward * currentThrust;
        vel.y = _playerController.VerticalVelocity;
        _playerController.Controller.Move(vel * Time.deltaTime);
    }
}
