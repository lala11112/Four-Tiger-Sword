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
            // Every action clock starts at zero, including repeated attacks using the same state.
            _playerController.Animator.CrossFade(stateName, transition, 0, 0f);
        }
    }

    protected void PlayCombo()
    {
        SnapshotAttackDamage();
        _timer = 0;
        _playerController.Input.AttackBuffer.Consume();
        ClearHitTargets();
        WeaponActionData step = CurrentNormalAttackStep;
        if (_finisherAttackActive)
        {
            ClearSoftTarget();
            ApplyFinisherFacing();
            ResetMotion(step);
        }
        else ResetTargetApproach(step, _comboStep > 0);
        BeginAttackCamera(step, _comboStep > 0);
        PlayActionAnimation(step.AnimationName, 0.1f);
        _playerController.UpdateAttackTrails(step, 0f);
        Debug.Log($"공격이름 : {step.AnimationName}  타수 : {_comboStep}");
    }

    protected void PlaySkillStep()
    {
        _timer = 0;
        ClearHitTargets();
        WeaponActionData step = _weaponActionData.SkillSteps[_skillStep];
        ResetMotion(step);
        BeginAttackCamera(step, _skillStep > 0);
        PlayActionAnimation(step.AnimationName, 0.01f);
        _playerController.UpdateAttackTrails(step, 0f);
        Debug.Log($"스킬 단계 : {step.AnimationName} ({_skillStep})");
    }

    protected void PlayUltimateStep()
    {
        _timer = 0;
        ClearHitTargets();
        WeaponActionData step = _weaponActionData.UltimateSteps[_ultimateStep];
        ResetMotion(step);
        BeginAttackCamera(step, _ultimateStep > 0);
        PlayActionAnimation(step.AnimationName, 0.01f);
        _playerController.UpdateAttackTrails(step, 0f);
        Debug.Log($"궁극기 단계 : {step.AnimationName} ({_ultimateStep})");
    }

    protected void SpawnHitVFX(GameObject target, WeaponActionData step = null, HitEvent hitEvent = null)
    {
        if (target == null) return;
        HitVfxSettings settings = hitEvent != null && hitEvent.OverrideHitVFX && hitEvent.HitEffect != null
            ? hitEvent.HitEffect : step?.HitEffect;
        GameObject prefab = settings?.Prefab;
        if (prefab == null) prefab = step?.HitEffect?.Prefab;
        if (prefab == null) prefab = _weaponActionData?.HitVFX;
        if (prefab == null) return;

        var anchor = target.GetComponentInParent<HitVfxAnchor>();
        Transform point = anchor != null ? anchor.SpawnPoint : target.transform;
        Quaternion rotation = point.rotation * Quaternion.Euler(settings?.Rotation ?? Vector3.zero);
        // Spawn independently so knockback or death does not move/remove the impact effect.
        GameObject effect = StageTransientObject.Track(Object.Instantiate(prefab, point.position, rotation));
        effect.transform.localScale = Vector3.Scale(effect.transform.localScale, settings?.Scale ?? Vector3.one);
        foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = particles.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }
    }

    private readonly HashSet<int> _playedSwingEvents = new();
    private float _previousSwingTime;

    protected void ResetAttackSounds()
    {
        _playedSwingEvents.Clear();
        _previousSwingTime = 0f;
    }

    protected void UpdateSwingSounds(WeaponActionData step)
        => UpdateSwingSounds(step, _timer);

    protected void UpdateSwingSounds(WeaponActionData step, float elapsed)
    {
        if (step == null) return;
        float currentTime = Mathf.Min(elapsed, step.Duration);
        if (currentTime <= _previousSwingTime) return;
        _previousSwingTime = currentTime;
        if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            for (int i = 0; i < step.HitEvents.Count; i++)
            {
                HitEvent hit = step.HitEvents[i];
                if (hit != null && hit.Duration > 0f && hit.StartTime <= step.Duration
                    && currentTime >= Mathf.Max(0f, hit.StartTime))
                    PlaySwingSound(step, hit, i);
            }
        }
        else if (step.HitDuration > 0f && step.HitStartTime <= step.Duration
            && currentTime >= Mathf.Max(0f, step.HitStartTime))
            PlaySwingSound(step);
    }

    protected void PlaySwingSound(WeaponActionData step, HitEvent hitEvent = null, int eventIndex = -1)
    {
        if (_playerController == null || step == null || !_playedSwingEvents.Add(eventIndex)) return;
        CombatSoundSettings settings = hitEvent?.Feedback?.SwingAudio;
        if (settings == null || !settings.HasSound) settings = step.Feedback?.SwingAudio;
        CombatSoundCue cue = settings != null
            ? settings.Resolve(step.SwingSound)
            : new CombatSoundSettings().Resolve(step.SwingSound);
        CombatSfx.Play(cue, _playerController.transform.position, _playerController);
    }

    protected virtual CombatSoundCue ResolveHitSound(WeaponActionData step = null, HitEvent hitEvent = null)
    {
        CombatSoundSettings settings = hitEvent?.Feedback?.HitAudio;
        if (settings != null && settings.HasSound) return settings.Resolve();
        settings = step?.Feedback?.HitAudio;
        if (settings != null && settings.HasSound) return settings.Resolve();
        settings = _weaponActionData?.CommonFeedback?.HitAudio;
        return settings != null
            ? settings.Resolve(_weaponActionData.HitSound)
            : new CombatSoundSettings().Resolve(_weaponActionData?.HitSound);
    }

    protected CombatSoundCue ResolveSecondaryHitSound()
    {
        CombatSoundSettings settings = _weaponActionData?.CommonFeedback?.SecondaryHitAudio;
        return settings != null && settings.HasSound ? settings.Resolve() : ResolveHitSound();
    }

    protected void PlayUltimateActivationSound()
    {
        ResetAttackSounds();
        var steps = _weaponActionData?.UltimateSteps;
        if (steps != null && steps.Count > 0) PlaySwingSound(steps[0]);
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
        Component nearestOwner = null;
        Collider nearestCollider = null;
        float nearestDistanceSquared = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var hit = _targetSearchBuffer[i];
            var owner = hit.GetComponentInParent<IDamageable>() as Component;
            if (!IsTargetValid(owner, hit)) continue;
            Vector3 toEnemy = owner.transform.position - _playerController.transform.position;
            toEnemy.y = 0f;
            float distanceSquared = toEnemy.sqrMagnitude;

            // Keep a nearest valid fallback before applying camera visibility filters.
            bool sameNearestDistance = Mathf.Abs(distanceSquared - nearestDistanceSquared) <= 0.0001f;
            if (distanceSquared < nearestDistanceSquared
                || (sameNearestDistance && owner == previousOwner))
            {
                nearestOwner = owner;
                nearestCollider = hit;
                nearestDistanceSquared = distanceSquared;
            }

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
            // Prefer the camera's center ray; world distance only breaks equal-angle ties.
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

        // Camera candidates always win; only fall back when none qualified.
        if (_softTarget == null && nearestOwner != null)
        {
            _softTargetOwner = nearestOwner;
            _softTarget = nearestOwner.transform;
            _softTargetCollider = nearestCollider;
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
            SoftTargetRotationSpeed * (animationDelta >= 0f ? animationDelta : _playerController.ActionDeltaTime * AttackSpeed));
    }
}
