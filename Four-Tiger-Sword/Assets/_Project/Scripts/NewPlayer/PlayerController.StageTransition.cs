using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public partial class PlayerController
{
    public bool IsStageTransitioning { get; private set; }
    public bool IsStageIdleReady { get; private set; }
    public event Action Died;

    private bool _deathNotified;
    private Animator _deathAnimator;
    private AnimatorUpdateMode _deathPreviousUpdateMode;
    private AnimatorCullingMode _deathPreviousCullingMode;
    private bool _stageInputEnabled, _stageMovementEnabled, _stageControllerEnabled, _stageAnimatorEnabled;
    private Animator _stageIdleAnimator;
    private AnimatorUpdateMode _stageIdlePreviousUpdateMode;
    private AnimatorCullingMode _stageIdlePreviousCullingMode;
    private readonly List<CinemachineInputAxisController> _stageCameraInputs = new();

    private const double DeathAnimationStallTimeout = 2d;
    private static readonly int StageIdleHash = UnityEngine.Animator.StringToHash("Idle");

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
            if (Animator.HasState(0, StageIdleHash)) Animator.Play(StageIdleHash, 0, 0f);
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
        PlaceStageTransition(position, rotation);
        ResumeStageTransition();
    }

    /// <summary>Places the frozen player and camera while a stage introduction still covers the world.</summary>
    public void PlaceStageTransition(Vector3 position, Quaternion rotation)
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
    }

    /// <summary>Evaluates Idle behind the loading cover without releasing player or camera controls.</summary>
    public bool PrepareStageIdleForReveal()
    {
        if (IsStageIdleReady) return true;
        var animator = Animator;
        if (!IsStageTransitioning || StatManager == null || StatManager.CurrentHp <= 0f
            || !_stageAnimatorEnabled || animator == null || !animator.gameObject.activeInHierarchy
            || animator.runtimeAnimatorController == null) return false;

        animator.enabled = true;
        if (animator.layerCount == 0 || !animator.HasState(0, StageIdleHash))
        {
            animator.enabled = false;
            return false;
        }
        _stageIdleAnimator = animator;
        _stageIdlePreviousUpdateMode = animator.updateMode;
        _stageIdlePreviousCullingMode = animator.cullingMode;
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;
        SetAnimationSpeed(1f);
        // Player.Update stays suspended. Enter Idle now so Resume doesn't restart its animation.
        StateMachine?.ChangeState(new PlayerIdleState(this));
        animator.Play(StageIdleHash, 0, 0f);
        animator.Update(0f);
        IsStageIdleReady = animator.GetCurrentAnimatorStateInfo(0).shortNameHash == StageIdleHash
            && !animator.IsInTransition(0);
        if (!IsStageIdleReady) CancelStageIdlePresentation();
        return IsStageIdleReady;
    }

    internal void CancelStageIdlePresentation() => RestoreStageIdlePresentation(false);

    private void RestoreStageIdlePresentation(bool resuming)
    {
        var animator = _stageIdleAnimator;
        _stageIdleAnimator = null;
        IsStageIdleReady = false;
        if (animator == null) return;
        animator.updateMode = _stageIdlePreviousUpdateMode;
        animator.cullingMode = _stageIdlePreviousCullingMode;
        if (!resuming && IsStageTransitioning)
        {
            animator.enabled = false;
            if (StatManager != null && StatManager.CurrentHp > 0f)
                StateMachine?.ChangeState(new StageTransitionState());
        }
    }

    /// <summary>Releases only the input and movement captured by BeginStageTransition.</summary>
    public void ResumeStageTransition()
    {
        if (!IsStageTransitioning) return;
        if (StatManager == null || StatManager.CurrentHp <= 0f)
        {
            CancelStageIdlePresentation();
            return;
        }
        RestoreStageIdlePresentation(true);
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

    internal void BeginDeathPresentation()
    {
        if (_deathAnimator != null || StatManager == null || StatManager.CurrentHp > 0f) return;

        // A lethal hit during stage preparation or a modal can reach the run before the state machine ticks.
        StateMachine?.Update(false, false);
        var animator = Animator;
        if (animator == null || !animator.gameObject.activeInHierarchy || animator.runtimeAnimatorController == null) return;

        bool wasEnabled = animator.enabled;
        animator.enabled = true;
        if (animator.layerCount == 0 || !animator.HasState(0, PlayerDieState.AnimationHash))
        {
            animator.enabled = wasEnabled;
            return;
        }

        CancelHitStops();
        SetAnimationSpeed(1f);
        _deathAnimator = animator;
        _deathPreviousUpdateMode = animator.updateMode;
        _deathPreviousCullingMode = animator.cullingMode;
        animator.applyRootMotion = true;
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Play(PlayerDieState.AnimationHash, 0, 0f);
    }

    internal IEnumerator WaitForDeathAnimation()
    {
        var animator = _deathAnimator;
        float progress = -1f;
        double deadline = Time.realtimeSinceStartupAsDouble + DeathAnimationStallTimeout;
        while (animator != null && animator == _deathAnimator && animator.isActiveAndEnabled && animator.layerCount > 0
            && Time.realtimeSinceStartupAsDouble < deadline)
        {
            var state = animator.GetCurrentAnimatorStateInfo(0);
            if (!animator.IsInTransition(0) && state.shortNameHash == PlayerDieState.AnimationHash)
            {
                if (state.normalizedTime >= 1f) yield break;
                if (state.normalizedTime > progress)
                {
                    progress = state.normalizedTime;
                    deadline = Time.realtimeSinceStartupAsDouble + DeathAnimationStallTimeout;
                }
            }
            yield return null;
        }
    }

    internal void CancelDeathPresentation()
    {
        var animator = _deathAnimator;
        _deathAnimator = null;
        if (animator == null) return;

        SetAnimationSpeed(0f);
        animator.updateMode = _deathPreviousUpdateMode;
        animator.cullingMode = _deathPreviousCullingMode;
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
