using UnityEngine;

public abstract partial class BaseForm
{
    public virtual void BeginAttack()
    {
        _currentAction = ActionType.Attack;
        _comboStep = 0;
        PlayCombo();
    }

    public abstract void UpdateAttack(out bool isComplete);

    public virtual void EndAttack()
    {
        _comboStep = 0;
        ClearHitTargets();
    }

    // ── 공중 공격 ─────────────────────────────────────────────────────────────

    public virtual void BeginAirAttack()
    {
        _currentAction = ActionType.AirAttack;
        _timer = 0;
        ClearHitTargets();
    }

    public virtual void UpdateAirAttack(out bool isComplete)
    {
        isComplete = false;
        if (_weaponActionData == null || _weaponActionData.AirAttackStep == null) { isComplete = true; return; }

        _timer += Time.deltaTime;
        ProcessHit(_weaponActionData.AirAttackStep);
        isComplete = _timer >= _weaponActionData.AirAttackStep.Duration;
    }

    public virtual void EndAirAttack() => ClearHitTargets();

    // ── 스킬 ─────────────────────────────────────────────────────────────────

    public virtual void BeginSkill()
    {
        _currentAction = ActionType.Skill;
        _playerController.Stat.TryConsumeSp(SkillSpCost);
        _skillStep = 0;
        PlaySkillStep();
    }

    public virtual void UpdateSkill(out bool isComplete)
    {
        isComplete = false;
        if (_weaponActionData == null || _weaponActionData.SkillSteps == null || _weaponActionData.SkillSteps.Count == 0) { isComplete = true; return; }

        _timer += Time.deltaTime;
        WeaponActionData step = _weaponActionData.SkillSteps[_skillStep];
        ProcessHit(step);
        MoveForward(step.ForwardThrust);

        if (_timer >= step.Duration)
        {
            if (_skillStep < _weaponActionData.SkillSteps.Count - 1) { _skillStep++; PlaySkillStep(); }
            else isComplete = true;
        }
    }

    public virtual void EndSkill()
    {
        _skillStep = 0;
        ClearHitTargets();
        _skillCooldownTimer = SkillCooldown;
    }

    // ── 궁극기 ───────────────────────────────────────────────────────────────

    public virtual void BeginUltimate()
    {
        _currentAction = ActionType.Ultimate;
        _playerController.Stat.TryConsumeSp(UltimateSpCost);
        _ultimateStep = 0;
        PlayUltimateStep();
    }

    public virtual void UpdateUltimate(out bool isComplete)
    {
        isComplete = false;
        if (_weaponActionData == null || _weaponActionData.UltimateSteps == null || _weaponActionData.UltimateSteps.Count == 0) { isComplete = true; return; }

        _timer += Time.deltaTime;
        WeaponActionData step = _weaponActionData.UltimateSteps[_ultimateStep];
        ProcessHit(step);
        MoveForward(step.ForwardThrust);

        if (_timer >= step.Duration)
        {
            if (_ultimateStep < _weaponActionData.UltimateSteps.Count - 1) { _ultimateStep++; PlayUltimateStep(); }
            else isComplete = true;
        }
    }

    public virtual void EndUltimate()
    {
        _ultimateStep = 0;
        ClearHitTargets();
        _ultimateCooldownTimer = UltimateCooldown;
    }

    // ── 공통 이동 헬퍼 ────────────────────────────────────────────────────────

    private void MoveForward(float thrust)
    {
        Vector3 vel = _playerController.transform.forward * thrust;
        vel.y = _playerController.VerticalVelocity;
        _playerController.Controller.Move(vel * Time.deltaTime);
    }
}
