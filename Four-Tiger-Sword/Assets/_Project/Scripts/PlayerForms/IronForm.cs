using UnityEngine;

public class IronForm : BaseForm
{
    public override ElementType Element => ElementType.ELEMENT_GOLD;

    public override float SkillSpCost   => 200f;
    public override float SkillCooldown => 10f;

    public IronForm(WeaponActionDataSO weaponActionData) : base(weaponActionData) { }

    public override void Equip(PlayerController playerController)
    {
        base.Equip(playerController);
        PlayerUIManager.Instance.IronElement.SetActive(false);
    }

    public override void Unequip(PlayerController playerController)
    {
        base.Unequip(playerController);
        PlayerUIManager.Instance.IronElement.SetActive(true);
    }

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
