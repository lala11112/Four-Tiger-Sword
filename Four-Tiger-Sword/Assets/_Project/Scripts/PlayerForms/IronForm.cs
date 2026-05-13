using UnityEngine;

public class IronForm : BaseForm
{
    public override ElementType Element => ElementType.ELEMENT_GOLD;

    public override float SkillSpCost      => 200f;
    public override float SkillCooldown    => 10f;
    public override float UltimateSpCost   => 500f;
    public override float UltimateCooldown => 30f;

    public IronForm(WeaponActionDataSO weaponActionData) : base(weaponActionData) { }

    public override void UpdateAttack(out bool isComplete)
    {
        isComplete = false;
        if (_weaponActionData == null || _weaponActionData.ComboSteps.Count == 0)
        {
            isComplete = true;
            return;
        }

        _timer += Time.deltaTime * AttackSpeed;
        WeaponActionData currentStep = _weaponActionData.ComboSteps[_comboStep];

        ProcessHit(currentStep);

        Vector3 moveVelocity = _playerController.transform.forward * currentStep.ForwardThrust;
        moveVelocity.y = _playerController.VerticalVelocity;
        _playerController.Controller.Move(moveVelocity * Time.deltaTime);

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

    public override void BeginSkill()
    {
        base.BeginSkill();
        PlayStepSound(_weaponActionData.SkillSteps, 0);
    }

    public override void BeginUltimate()
    {
        base.BeginUltimate();
        SpawnStepVFX(_weaponActionData.UltimateSteps, 0);
        PlayStepSound(_weaponActionData.UltimateSteps, 0);
    }

    public override void UpdateUltimate(out bool isComplete)
    {
        int prevStep = _ultimateStep;
        base.UpdateUltimate(out isComplete);

        if (_ultimateStep != prevStep)
        {
            SpawnStepVFX(_weaponActionData.UltimateSteps, _ultimateStep);
            PlayStepSound(_weaponActionData.UltimateSteps, _ultimateStep);
        }
    }
}
