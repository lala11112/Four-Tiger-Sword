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
        _playerController.Animator.CrossFade(step.AnimationName, 0.01f);
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

    // ── 소프트 타겟팅 ────────────────────────────────────────────────────────

    protected void FindSoftTarget()
    {
        _softTarget = null;

        Collider[] hits = Physics.OverlapSphere(
            _playerController.transform.position, SoftTargetSearchRadius, _enemyLayer);

        float   bestDist   = float.MaxValue;
        Transform bestTr   = null;

        foreach (var hit in hits)
        {
            Vector3 toEnemy = hit.transform.position - _playerController.transform.position;
            toEnemy.y = 0f;

            float angle = Vector3.Angle(_playerController.transform.forward, toEnemy);
            if (angle > SoftTargetAngle) continue;

            float dist = toEnemy.magnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestTr   = hit.transform;
            }
        }

        _softTarget = bestTr;
    }

    protected void RotateTowardSoftTarget()
    {
        if (_softTarget == null) return;

        Vector3 dir = _softTarget.position - _playerController.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir);
        _playerController.transform.rotation = Quaternion.RotateTowards(
            _playerController.transform.rotation,
            targetRot,
            SoftTargetRotationSpeed * Time.deltaTime);
    }
}
