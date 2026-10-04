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
            direction, continuing);
    }

    protected void UpdateAttackCamera(WeaponActionData step)
    {
        if (step?.CameraProfile == null) return;
        _playerController.CombatCamera?.SetProgress(_playerController, _timer / Mathf.Max(0.001f, step.Duration));
    }

    protected bool NotifyCameraHit(WeaponActionData step, Transform target, Vector3 direction)
        => step?.CameraProfile != null && _playerController.CombatCamera != null
            && _playerController.CombatCamera.NotifyHit(_playerController, target, direction);

    protected void EndAttackCamera(bool canceled = false)
        => _playerController?.CombatCamera?.EndAttack(_playerController, canceled);
}
