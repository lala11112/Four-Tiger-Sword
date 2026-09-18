using UnityEngine;
using System.Collections.Generic;

public abstract partial class BaseForm
{
    private void PlayActionAnimation(string stateName, float transition)
    {
        // TODO: 금/토 기본기, 신규 스킬/궁극기 모션 제작 후 AnimationName을 연결합니다.
        // _playerController.Animator.CrossFade("NewFormMotion", transition);
        if (string.IsNullOrWhiteSpace(stateName)) return;
        if (_playerController.Animator.HasState(0, Animator.StringToHash(stateName)))
        {
            //_playerController.Animator.applyRootMotion = true;
            _playerController.Animator.CrossFade(stateName, transition);
        }
    }

    protected void PlayCombo()
    {
        _timer = 0;
        _playerController.Input.AttackBuffer.Consume();
        ClearHitTargets();
        WeaponActionData step = _weaponActionData.ComboSteps[_comboStep];
        ResetTargetApproach(step);
        PlayActionAnimation(step.AnimationName, 0.1f);
        SpawnStepVFX(_weaponActionData.ComboSteps, _comboStep);
        PlayStepSound(_weaponActionData.ComboSteps, _comboStep);
        Debug.Log($"공격이름 : {step.AnimationName}  타수 : {_comboStep}");
    }

    protected void PlaySkillStep()
    {
        _timer = 0;
        ClearHitTargets();
        WeaponActionData step = _weaponActionData.SkillSteps[_skillStep];
        PlayActionAnimation(step.AnimationName, 0.01f);
        SpawnStepVFX(_weaponActionData.SkillSteps, _skillStep);
        PlayStepSound(_weaponActionData.SkillSteps, _skillStep);
        Debug.Log($"스킬 단계 : {step.AnimationName} ({_skillStep})");
    }

    protected void PlayUltimateStep()
    {
        _timer = 0;
        ClearHitTargets();
        WeaponActionData step = _weaponActionData.UltimateSteps[_ultimateStep];
        PlayActionAnimation(step.AnimationName, 0.01f);
        SpawnStepVFX(_weaponActionData.UltimateSteps, _ultimateStep);
        PlayStepSound(_weaponActionData.UltimateSteps, _ultimateStep);
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

    protected void SpawnHitVFX(Vector3 position)
    {
        if (_weaponActionData?.HitVFX == null) return;
        Object.Instantiate(_weaponActionData.HitVFX, position, Quaternion.identity);
    }

    protected void PlayHitSound(Vector3 position)
    {
        if (_weaponActionData?.HitSound == null) return;

        AudioClip clip = _weaponActionData.HitSound;
        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            Debug.LogWarning($"[PlayHitSound] '{clip.name}'의 Load Type이 Streaming이거나 아직 로드되지 않아 재생할 수 없습니다. Import Settings에서 Load Type을 'Decompress On Load'로 변경하세요.");
            return;
        }

        AudioSource.PlayClipAtPoint(clip, position);
    }

    protected void PlayStepSound(List<WeaponActionData> steps, int index)
    {
        if (steps == null || index >= steps.Count) return;
        WeaponActionData step = steps[index];
        if (step.SwingSound == null) return;

        AudioSource.PlayClipAtPoint(step.SwingSound, _playerController.transform.position);
    }

    // ── 소프트 타겟팅 ────────────────────────────────────────────────────────

    protected void FindSoftTarget(bool frontOnly = false)
    {
        _softTarget = null;
        _softTargetCollider = null;

        Collider[] hits = Physics.OverlapSphere(
            _playerController.transform.position, SoftTargetSearchRadius, _enemyLayer);

        float   bestDist   = float.MaxValue;
        Transform bestTr   = null;

        foreach (var hit in hits)
        {
            if (hit.GetComponentInParent<IDamageable>() == null) continue;
            var enemyStats = hit.GetComponentInParent<EnemyStat>();
            if (enemyStats != null && enemyStats.IsDead) continue;
            Vector3 toEnemy = hit.transform.position - _playerController.transform.position;
            toEnemy.y = 0f;

            float angle = Vector3.Angle(_playerController.transform.forward, toEnemy);
            if (angle > (frontOnly ? 80f : SoftTargetAngle)) continue;

            float dist = toEnemy.magnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestTr   = hit.transform;
                _softTargetCollider = hit;
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
