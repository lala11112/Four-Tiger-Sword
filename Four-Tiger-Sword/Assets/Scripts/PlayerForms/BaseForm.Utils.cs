using UnityEngine;
using System.Collections.Generic;

public abstract partial class BaseForm
{
    protected void PlayCombo()
    {
        _timer = 0;
        _playerController.Input.AttackBuffer.Consume();
        ClearHitTargets();
        WeaponActionData step = _weaponActionData.ComboSteps[_comboStep];
        Debug.Log($"공격이름 : {step.AnimationName}  타수 : {_comboStep}");
    }

    protected void PlaySkillStep()
    {
        _timer = 0;
        ClearHitTargets();
        WeaponActionData step = _weaponActionData.SkillSteps[_skillStep];
        Debug.Log($"스킬 단계 : {step.AnimationName} ({_skillStep})");
    }

    protected void PlayUltimateStep()
    {
        _timer = 0;
        ClearHitTargets();
        WeaponActionData step = _weaponActionData.UltimateSteps[_ultimateStep];
        Debug.Log($"궁극기 단계 : {step.AnimationName} ({_ultimateStep})");
    }

    protected void SpawnStepVFX(List<WeaponActionData> steps, int index)
    {
        if (steps == null || index >= steps.Count) return;
        WeaponActionData step = steps[index];
        if (step.SlashVFX == null) return;

        Vector3 pos = _playerController.transform.position
                    + _playerController.transform.rotation * step.HitBoxOffset;
        Object.Instantiate(step.SlashVFX, pos, _playerController.transform.rotation);
    }

    protected void PlayStepSound(List<WeaponActionData> steps, int index)
    {
        if (steps == null || index >= steps.Count) return;
        WeaponActionData step = steps[index];
        if (step.SwingSound == null) return;

        AudioSource.PlayClipAtPoint(step.SwingSound, _playerController.transform.position);
    }
}
