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
        ResetTargetApproach(step, _comboStep > 0);
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
        ResetMotion(step);
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
        ResetMotion(step);
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

    private Component _softTargetOwner;
    private Collider[] _targetSearchBuffer = new Collider[32];
    private RaycastHit[] _targetSightBuffer = new RaycastHit[16];

    private void ClearSoftTarget()
    {
        _softTarget = null;
        _softTargetCollider = null;
        _softTargetOwner = null;
    }

    private bool IsTargetValid(Component owner, Collider collider)
    {
        if (owner == null || !owner.gameObject.activeInHierarchy || collider == null
            || !collider.enabled || !collider.gameObject.activeInHierarchy) return false;
        var stats = owner.GetComponentInParent<EnemyStat>();
        if (stats != null && stats.IsDead) return false;
        Vector3 offset = owner.transform.position - _playerController.transform.position;
        if (Mathf.Abs(offset.y) > SoftTargetMaxHeight || offset.sqrMagnitude > SoftTargetSearchRadius * SoftTargetSearchRadius)
            return false;

        Vector3 origin = _playerController.Controller.bounds.center;
        Vector3 direction = collider.bounds.center - origin;
        float distance = direction.magnitude;
        if (distance < 0.001f) return true;
        int count;
        while (true)
        {
            count = Physics.RaycastNonAlloc(origin, direction / distance, _targetSightBuffer,
                distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count < _targetSightBuffer.Length) break;
            System.Array.Resize(ref _targetSightBuffer, _targetSightBuffer.Length * 2);
        }
        for (int i = 0; i < count; i++)
        {
            var blocker = _targetSightBuffer[i].collider;
            if (blocker.transform.IsChildOf(_playerController.transform)) continue;
            // Other monsters may overlap the line of sight; only level geometry blocks assistance.
            if (blocker.GetComponentInParent<IDamageable>() != null) continue;
            return false;
        }
        return true;
    }

    protected void FindSoftTarget(bool keepCurrent = false)
    {
        // Re-evaluate the view at each attack/chain boundary. Never switch mid-swing.
        Component previousOwner = keepCurrent ? _softTargetOwner : null;
        ClearSoftTarget();
        Transform view = _playerController.CameraTransform;
        if (view == null && Camera.main != null) view = Camera.main.transform;
        Vector3 viewOrigin = view != null ? view.position : _playerController.Controller.bounds.center;
        Vector3 viewForward = view != null ? view.forward : _playerController.transform.forward;
        Camera camera = view != null ? view.GetComponent<Camera>() : null;
        float minAlignment = Mathf.Cos(Mathf.Clamp(_playerController.TargetingHalfAngle, 1f, 89f) * Mathf.Deg2Rad);
        int count;
        while (true)
        {
            count = Physics.OverlapSphereNonAlloc(_playerController.transform.position,
                SoftTargetSearchRadius, _targetSearchBuffer, _enemyLayer, QueryTriggerInteraction.Collide);
            if (count < _targetSearchBuffer.Length) break;
            System.Array.Resize(ref _targetSearchBuffer, _targetSearchBuffer.Length * 2);
        }

        float bestDistanceSquared = float.MaxValue;
        float bestAlignment = -1f;

        for (int i = 0; i < count; i++)
        {
            var hit = _targetSearchBuffer[i];
            var owner = hit.GetComponentInParent<IDamageable>() as Component;
            if (!IsTargetValid(owner, hit)) continue;
            Vector3 aimPoint = hit.bounds.center;
            Vector3 viewOffset = aimPoint - viewOrigin;
            if (viewOffset.sqrMagnitude < 0.0001f) continue;
            float alignment = Vector3.Dot(viewForward, viewOffset.normalized);
            if (alignment < minAlignment) continue;
            if (camera != null)
            {
                Vector3 viewport = camera.WorldToViewportPoint(aimPoint);
                if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f
                    || viewport.y < 0f || viewport.y > 1f) continue;
            }
            Vector3 toEnemy = owner.transform.position - _playerController.transform.position;
            toEnemy.y = 0f;

            // Prefer the camera's center ray; world distance only breaks equal-angle ties.
            float distanceSquared = toEnemy.sqrMagnitude;
            bool sameAlignment = Mathf.Abs(alignment - bestAlignment) <= 0.00001f;
            bool sameDistance = Mathf.Abs(distanceSquared - bestDistanceSquared) <= 0.0001f;
            if (alignment > bestAlignment + 0.00001f
                || (sameAlignment && (distanceSquared < bestDistanceSquared
                    || (sameDistance && owner == previousOwner))))
            {
                bestAlignment = alignment;
                bestDistanceSquared = distanceSquared;
                _softTargetOwner = owner;
                _softTarget = owner.transform;
                _softTargetCollider = hit;
            }
        }
    }

    protected void RotateTowardSoftTarget(float animationDelta = -1f)
    {
        if (_softTarget == null) return;
        if (!IsTargetValid(_softTargetOwner, _softTargetCollider))
        {
            ClearSoftTarget();
            return;
        }

        Vector3 dir = _softTarget.position - _playerController.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir);
        _playerController.transform.rotation = Quaternion.RotateTowards(
            _playerController.transform.rotation,
            targetRot,
            SoftTargetRotationSpeed * (animationDelta >= 0f ? animationDelta : Time.deltaTime * AttackSpeed));
    }
}
