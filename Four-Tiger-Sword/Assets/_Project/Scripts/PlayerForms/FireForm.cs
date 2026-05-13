using UnityEngine;

public class FireForm : BaseForm
{
    public override ElementType Element => ElementType.ELEMENT_FIRE;

    public override float SkillSpCost      => 200f;
    public override float SkillCooldown    => 8f;
    public override float UltimateSpCost   => 500f;
    public override float UltimateCooldown => 15f;

    private bool _explosionSpawned = false;

    public FireForm(WeaponActionDataSO weaponActionData) : base(weaponActionData) {}

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

    // Phase 0: 검 휘두르기, Phase 1: 폭발
    public override void BeginSkill()
    {
        _explosionSpawned = false;
        base.BeginSkill();
        //PlayStepSound(_weaponActionData.SkillSteps, 0);
    }

    public override void UpdateSkill(out bool isComplete)
    {
        int prevStep = _skillStep;
        base.UpdateSkill(out isComplete);

        if (_skillStep == 1 && prevStep == 0 && !_explosionSpawned)
        {
            _explosionSpawned = true;
            //SpawnStepVFX(_weaponActionData.SkillSteps, 1);
            //PlayStepSound(_weaponActionData.SkillSteps, 1);
        }
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
            //SpawnStepVFX(_weaponActionData.UltimateSteps, _ultimateStep);
            //PlayStepSound(_weaponActionData.UltimateSteps, _ultimateStep);
        }
    }
}
