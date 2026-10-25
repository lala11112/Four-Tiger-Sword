using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public partial class PlayerController
{
    public bool IsStageTransitioning { get; private set; }
    public event Action Died;

    private bool _deathNotified;
    private bool _stageInputEnabled, _stageMovementEnabled, _stageControllerEnabled, _stageAnimatorEnabled;
    private readonly List<CinemachineInputAxisController> _stageCameraInputs = new();

    /// <summary>Stops world actions while retaining this run's resources, items, form and timers.</summary>
    public void BeginStageTransition()
    {
        if (IsStageTransitioning) return;
        CancelFinisherSelection();
        IsStageTransitioning = true;
        _stageInputEnabled = Input != null && Input.enabled;
        _stageMovementEnabled = Movement != null && Movement.enabled;
        _stageControllerEnabled = Controller != null && Controller.enabled;
        _stageAnimatorEnabled = Animator != null && Animator.enabled;

        CancelPendingSwitchAttack();
        // Exit performs the active action's normal collision/visibility/cooldown cleanup.
        if (StatManager != null && StatManager.CurrentHp > 0f)
            StateMachine?.ChangeState(new StageTransitionState());
        FormManager?.CleanupStageEffects();
        GetComponent<FormEffectRunner>()?.CancelStageEffects();
        StopAttackTrails(true);
        _combatCamera?.Cancel(this, true);
        StopParryKnockback();
        ConsumeHitReaction();
        VerticalVelocity = 0f;
        _coyoteTimer = 0f;
        CancelHitStops();

        if (Input != null)
        {
            Input.JumpBuffer.Consume();
            Input.AttackBuffer.Consume();
            Input.DashBuffer.Consume();
            Input.SkillBuffer.Consume();
            Input.UltimateBuffer.Consume();
            Input.ConsumeFormInput();
            Input.enabled = false;
        }
        if (Movement != null) Movement.enabled = false;
        if (Controller != null) Controller.enabled = false;
        if (Animator != null)
        {
            Animator.applyRootMotion = false;
            SetAnimationSpeed(1f);
            int idle = UnityEngine.Animator.StringToHash("Idle");
            if (Animator.HasState(0, idle)) Animator.Play(idle, 0, 0f);
            Animator.enabled = false;
        }

        _stageCameraInputs.Clear();
        foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!TracksPlayer(camera)) continue;
            camera.GetComponent<CombatCameraDirector>()?.Cancel(this, true);
            foreach (var input in camera.GetComponents<CinemachineInputAxisController>())
            {
                if (!input.enabled) continue;
                _stageCameraInputs.Add(input);
                input.enabled = false;
            }
        }
        StageTransientObject.ClearScene(gameObject.scene);
    }

    /// <summary>Teleports without sweeping through the old world, then resumes the retained player.</summary>
    public void CompleteStageTransition(Vector3 position, Quaternion rotation)
    {
        if (!IsStageTransitioning) return;
        if (!KnockbackMotion.IsFinite(position.sqrMagnitude) || !KnockbackMotion.IsFinite(rotation.x)
            || !KnockbackMotion.IsFinite(rotation.y) || !KnockbackMotion.IsFinite(rotation.z)
            || !KnockbackMotion.IsFinite(rotation.w) || Quaternion.Dot(rotation, rotation) < 0.0001f)
            throw new ArgumentException("Stage spawn position and rotation must be finite.");
        if (StatManager == null || StatManager.CurrentHp <= 0f) return;

        Vector3 delta = position - transform.position;
        if (Controller != null) Controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation.normalized);
        VerticalVelocity = 0f;
        Physics.SyncTransforms();
        foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!TracksPlayer(camera)) continue;
            if (IsPlayerTarget(camera.Follow)) camera.OnTargetObjectWarped(camera.Follow, delta);
            if (camera.LookAt != camera.Follow && IsPlayerTarget(camera.LookAt))
                camera.OnTargetObjectWarped(camera.LookAt, delta);
            camera.PreviousStateIsValid = false;
            if (camera.isActiveAndEnabled) camera.UpdateCameraState(Vector3.up, -1f);
        }

        if (Animator != null) Animator.enabled = _stageAnimatorEnabled;
        StateMachine?.ChangeState(new PlayerIdleState(this));
        if (Controller != null) Controller.enabled = _stageControllerEnabled;
        if (Movement != null) Movement.enabled = _stageMovementEnabled;
        foreach (var input in _stageCameraInputs)
            if (input != null) input.enabled = true;
        _stageCameraInputs.Clear();
        IsStageTransitioning = false;
        if (Input != null) Input.enabled = _stageInputEnabled;
    }

    internal void NotifyDied()
    {
        if (_deathNotified || StatManager == null || StatManager.CurrentHp > 0f) return;
        CancelFinisherSelection();
        _deathNotified = true;
        GetComponent<FormEffectRunner>()?.CancelStageEffects();
        Died?.Invoke();
    }

    private bool IsPlayerTarget(Transform target) => target != null && (target == transform || target.IsChildOf(transform));
    private bool TracksPlayer(CinemachineCamera camera) => IsPlayerTarget(camera.Follow) || IsPlayerTarget(camera.LookAt);

    private sealed class StageTransitionState : IPlayerState
    {
        public void Enter() { }
        public void Update() { }
        public void Exit() { }
    }
}
