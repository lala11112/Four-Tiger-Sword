using UnityEngine;

public abstract partial class BaseForm
{
    protected void BeginAttackCamera(WeaponActionData step, bool continuing = false,
        Transform target = null, Vector3 direction = default)
    {
        if (_playerController == null) return;
        var camera = _playerController.CombatCamera;
        if (camera == null) return;
        if (direction.sqrMagnitude < 0.001f) direction = _playerController.transform.forward;
        camera.BeginAttack(step?.CameraProfile, _playerController, target != null ? target : _softTarget,
            direction, continuing, step?.Duration ?? 1f, AttackSpeed, CameraV2(step), step);
    }

    protected void UpdateAttackCamera(WeaponActionData step)
    {
        if (step?.CameraProfile == null && CameraV2(step)?.HasOverrides != true) return;
        _playerController.CombatCamera?.SetProgress(_playerController,
            _timer / Mathf.Max(0.001f, step.Duration), AttackSpeed);
    }

    protected void UpdateAttackCamera(WeaponActionData step, float progress01)
    {
        if (step?.CameraProfile == null && CameraV2(step)?.HasOverrides != true) return;
        _playerController.CombatCamera?.SetProgress(_playerController, progress01, AttackSpeed);
    }

    protected bool NotifyCameraHit(WeaponActionData step, Transform target, Vector3 direction,
        HitEvent hitEvent = null, int hitEventIndex = -1)
    {
        var settings = CameraV2(step);
        var hitSettings = hitEvent?.Feedback?.CameraV2;
        if (step?.CameraProfile == null && settings?.HasOverrides != true && hitSettings?.HasOverrides != true) return false;
        var camera = _playerController.CombatCamera;
        if (camera == null) return false;
        if (hitEventIndex < 0 && hitEvent != null && step?.HitEvents != null) hitEventIndex = step.HitEvents.IndexOf(hitEvent);
        // A hit-only override can be authored without an attack motion profile.
        if (!camera.IsPlaying && (settings?.HasOverrides == true || hitSettings?.HasOverrides == true))
            camera.BeginAttack(step?.CameraProfile, _playerController,
            target, direction, false, step?.Duration ?? 1f, AttackSpeed, settings, step);
        return camera.NotifyHit(_playerController, target, direction, hitSettings, hitEventIndex);
    }

    private static CombatCameraV2Settings CameraV2(WeaponActionData step)
    {
        var authored = step?.Feedback?.Camera?.V2;
        return authored?.HasOverrides == true ? authored
            : step?.CameraProfile?.EnableV2 == true ? step.CameraProfile.V2 : null;
    }

    protected void EndAttackCamera(bool canceled = false)
        => _playerController?.CombatCamera?.EndAttack(_playerController, canceled);
}
