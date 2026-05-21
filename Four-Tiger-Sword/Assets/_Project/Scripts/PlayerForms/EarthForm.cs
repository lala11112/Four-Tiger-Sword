using UnityEngine;

public class EarthForm : BaseForm
{
    public override ElementType Element => ElementType.ELEMENT_EARTH;

    public override float SkillSpCost      => 150f;
    public override float SkillCooldown    => 8f;
    public override float UltimateSpCost   => 600f;
    public override float UltimateCooldown => 40f;

    public EarthForm(WeaponActionDataSO weaponActionData) : base(weaponActionData) { }

    public override void Equip(PlayerController playerController)
    {
        base.Equip(playerController);
        playerController.UIManager.EarthElement.SetActive(false);
    }

    public override void Unequip(PlayerController playerController)
    {
        base.Unequip(playerController);
        playerController.UIManager.EarthElement.SetActive(true);
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
        SpawnStepVFX(_weaponActionData.SkillSteps, 0);
        PlayStepSound(_weaponActionData.SkillSteps, 0);
    }

    public override void BeginUltimate()
    {
        base.BeginUltimate();
        SpawnStepVFX(_weaponActionData.UltimateSteps, 0);
    }
}
