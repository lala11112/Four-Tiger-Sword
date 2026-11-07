using Unity.Cinemachine;
using UnityEngine;

public partial class PlayerController
{
    /// <summary>Moves within this stage without resetting resources, cooldowns or world effects.</summary>
    public bool TryFastTravel(Vector3 position, Quaternion rotation)
    {
        if (!isActiveAndEnabled || IsStageTransitioning || IsShopPaused || IsFinisherSelecting || IsInRoomCombat
            || StatManager == null || StatManager.CurrentHp <= 0f || Controller == null || !Controller.enabled
            || !KnockbackMotion.IsFinite(position.sqrMagnitude) || !KnockbackMotion.IsFinite(rotation.x)
            || !KnockbackMotion.IsFinite(rotation.y) || !KnockbackMotion.IsFinite(rotation.z)
            || !KnockbackMotion.IsFinite(rotation.w) || Quaternion.Dot(rotation, rotation) < .0001f) return false;

        CancelPendingSwitchAttack();
        StateMachine?.ChangeState(new PlayerIdleState(this));
        StopParryKnockback();
        ConsumeHitReaction();
        StopAttackTrails(true);
        _combatCamera?.Cancel(this, true);
        VerticalVelocity = 0f;
        _coyoteTimer = 0f;
        if (Input != null)
        {
            Input.JumpBuffer.Consume();
            Input.AttackBuffer.Consume();
            Input.DashBuffer.Consume();
            Input.SkillBuffer.Consume();
            Input.UltimateBuffer.Consume();
            Input.ConsumeFormInput();
        }

        var delta = position - transform.position;
        Controller.enabled = false;
        try
        {
            transform.SetPositionAndRotation(position, rotation.normalized);
            Physics.SyncTransforms();
        }
        finally { Controller.enabled = true; }
        foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!TracksPlayer(camera)) continue;
            if (IsPlayerTarget(camera.Follow)) camera.OnTargetObjectWarped(camera.Follow, delta);
            if (camera.LookAt != camera.Follow && IsPlayerTarget(camera.LookAt)) camera.OnTargetObjectWarped(camera.LookAt, delta);
            camera.PreviousStateIsValid = false;
            if (camera.isActiveAndEnabled) camera.UpdateCameraState(Vector3.up, -1f);
        }
        return true;
    }
}
