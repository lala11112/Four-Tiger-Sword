using UnityEngine;
using Unity.Cinemachine;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Offsets tracking targets BEFORE Cinemachine solves body/aim/obstacles. Lens offsets
/// affect only the pipeline state, never the authored Lens or CameraMove's base FOV.
/// </summary>
[DefaultExecutionOrder(-200)]
[ExecuteAlways]
[DisallowMultipleComponent, RequireComponent(typeof(CinemachineCamera))]
public sealed class CombatCameraDirector : CinemachineExtension
{
    private CinemachineCamera _camera;
    private CameraTarget _savedTarget;
    private Transform _follow, _lookAt, _followProxy, _lookProxy;
    private PlayerController _owner;
    private Transform _target;
    private AttackCameraProfile _profile;
    private Quaternion _viewFrame, _followRotation, _lookRotation;
    private Vector3 _direction, _framing, _positionOffset;
    private float _progress, _fov, _releaseElapsed, _releaseDuration;
    private Vector3 _releaseFraming, _releasePosition;
    private float _releaseFov;
    private bool _ownsTargets, _releasing, _hitPlayed;
    private CombatFeelFeedback _feelFeedback, _feelPrefab;
    private bool _feelActive;
    private bool _feelZoom;
    private CombatCameraV2Settings _v2;
    private readonly CombatCameraV2Mixer _mixer = new CombatCameraV2Mixer();
    private readonly CombatFeedbackHitGate _confirmedHits = new CombatFeedbackHitGate();
    private Vector3 _motionEuler, _releaseEuler;
    private Quaternion _motionRotation = Quaternion.identity;
    private CombatFeedbackClock _returnClock;
    private int _attackSequence;
    private WeaponActionData _step;
    private Quaternion _releaseWorldRotation = Quaternion.identity;
    private readonly Dictionary<string, Vector2> _reportedLimits = new Dictionary<string, Vector2>();

    public bool IsPlaying => _ownsTargets && !_releasing;
    public float Progress => _progress;

    protected override void Awake()
    {
        base.Awake();
        _camera = GetComponent<CinemachineCamera>();
    }

    public bool BeginAttack(AttackCameraProfile profile, PlayerController owner,
        Transform target, Vector3 direction, bool continuing = false,
        float duration = 1f, float playbackSpeed = 1f, CombatCameraV2Settings settings = null,
        WeaponActionData step = null)
    {
        if (!isActiveAndEnabled || owner == null) return false;
        settings = CombatCameraV2Settings.Resolve(profile, settings);
        bool hitOverrides = step?.HitEvents?.Exists(hit => hit?.Feedback?.CameraV2?.HasOverrides == true) == true;
        if (profile == null && settings?.HasOverrides != true && !hitOverrides) { EndAttack(owner); return false; }
        if (_camera == null) _camera = GetComponent<CinemachineCamera>();
        bool preserve = settings?.Mode == CombatFeedbackChannelMode.Override
            ? settings.Motion.PreserveChainViewDirection : profile == null || profile.PreserveChainViewDirection;
        bool keepFrame = _ownsTargets && !_releasing && _owner == owner
            && continuing && preserve;
        if (_ownsTargets && _owner != owner) ResetPlayback("owner-changed");
        if (!_ownsTargets)
        {
            _follow = _camera.Follow;
            _lookAt = _camera.LookAt != null ? _camera.LookAt : _follow;
            if (_follow == null || _lookAt == null) return false;
            _savedTarget = _camera.Target;
            _followRotation = _follow.rotation;
            _lookRotation = _lookAt.rotation;
            EnsureProxies();
            _followProxy.SetPositionAndRotation(_follow.position, _follow.rotation);
            _lookProxy.SetPositionAndRotation(_lookAt.position, _lookAt.rotation);
            _camera.Follow = _followProxy;
            _camera.LookAt = _lookProxy;
            _ownsTargets = true;
        }
        if (!keepFrame)
        {
            _viewFrame = owner.CameraTransform != null ? owner.CameraTransform.rotation : _camera.transform.rotation;
            _followRotation = _follow.rotation;
            _lookRotation = _lookAt.rotation;
        }
        _owner = owner;
        _profile = profile;
        _v2 = settings;
        _step = step;
        _target = target;
        _direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        _progress = 0;
        _hitPlayed = _releasing = false;
        _confirmedHits.Reset();
        _reportedLimits.Clear();
        _attackSequence++;
        _feelZoom = settings?.Mode == CombatFeedbackChannelMode.Override ? settings.Motion.UseFeelZoom
            : settings?.Mode != CombatFeedbackChannelMode.Off;
        BeginFeel(profile != null ? profile.FeelFeedbackPrefab : null, duration, playbackSpeed, _feelZoom);
        CombatFeedbackTelemetry.Emit("camera-begin", owner.gameObject, target != null ? target.gameObject : null,
            detail: settings?.HasOverrides == true ? "v2" : "legacy", value: _attackSequence);
        return true;
    }

    public void SetProgress(PlayerController owner, float progress, float playbackSpeed = 1f)
    {
        if (owner != _owner || !IsPlaying) return;
        _progress = Mathf.Clamp01(progress);
        ReportLimit("AttackProgress", progress, _progress, "normalized progress is limited to 0..1");
        if (_feelActive) _feelFeedback.SetPlaybackSpeed(playbackSpeed);
    }

    // Returns true when this profile owns hit feedback, including suppressed duplicate hits.
    public bool NotifyHit(PlayerController owner, Transform target, Vector3 direction,
        CombatCameraHitSettings hitSettings = null, int hitEventIndex = -1)
    {
        if (owner != _owner || !IsPlaying) return false;
        bool v2 = _v2?.HasOverrides == true || hitSettings?.HasOverrides == true;
        if (!v2 && _profile == null) return false;
        if (!v2 && _hitPlayed) return true;
        if (v2)
        {
            var policy = hitSettings?.HasOverrides == true ? hitSettings.TriggerPolicy
                : _v2 != null ? _v2.TriggerPolicy : CombatFeedbackTriggerPolicy.FirstPerHitEvent;
            if (!_confirmedHits.Accept(policy, hitEventIndex, target != null ? target.GetInstanceID() : 0)) return true;
        }
        bool firstLegacyHit = !_hitPlayed;
        _hitPlayed = true;
        _target = target;
        var impact = hitSettings?.Impact != null && hitSettings.Impact.Mode != CombatFeedbackChannelMode.Inherit
            ? hitSettings.Impact : _v2?.Impact;
        var shake = hitSettings?.Shake != null && hitSettings.Shake.Mode != CombatFeedbackChannelMode.Inherit
            ? hitSettings.Shake : _v2?.Shake;
        Quaternion frame = owner.CameraTransform != null ? owner.CameraTransform.rotation : _camera.State.GetFinalOrientation();
        if (impact?.Mode == CombatFeedbackChannelMode.Override)
        {
            _mixer.AddImpact(impact, frame, direction);
            CombatFeedbackTelemetry.Emit("camera-impact-trigger", owner.gameObject,
                target != null ? target.gameObject : null, hitEventIndex, "v2", impact.Duration);
        }
        if (shake?.Mode == CombatFeedbackChannelMode.Override)
        {
            _mixer.AddShake(shake, frame, direction);
            CombatFeedbackTelemetry.Emit("camera-shake-trigger", owner.gameObject,
                target != null ? target.gameObject : null, hitEventIndex, "v2-absolute", shake.Duration);
        }
        bool legacyImpulse = (shake == null || shake.Mode == CombatFeedbackChannelMode.Inherit)
            && (_profile == null || firstLegacyHit);
        if (legacyImpulse && _profile == null && _step != null) owner.PlayAttackCameraShake(_step);
        var source = owner.ImpulseSource;
        Vector3 velocity = Vector3.zero;
        if (legacyImpulse && source != null && _profile != null && _profile.HitImpulse > 0 && direction.sqrMagnitude > 0.001f)
        {
            velocity = direction.normalized;
            var listener = GetComponent<CinemachineImpulseListener>();
            Quaternion cameraOrientation = _camera.State.GetFinalOrientation();
            // A forward dash otherwise produces mostly depth motion, hidden by the zoom.
            // Only opted-in Feel presets replace that direction with a screen-space kick.
            if (_feelActive && _feelFeedback.OverrideImpulseDirection
                && _feelFeedback.CameraSpaceImpulseDirection.sqrMagnitude > 0.000001f)
                velocity = cameraOrientation * _feelFeedback.CameraSpaceImpulseDirection.normalized;
            if (listener != null && listener.UseCameraSpace)
                velocity = Quaternion.Inverse(cameraOrientation) * velocity;
            velocity *= source.DefaultVelocity.magnitude * _profile.HitImpulse;
        }
        bool preserveFlash = !v2 || (hitSettings?.HasOverrides == true ? hitSettings.PreserveFeelFlash : _v2 == null || _v2.PreserveFeelFlash);
        if (_feelActive && preserveFlash) _feelFeedback.PlayHit(source, velocity, legacyImpulse, v2);
        else if (legacyImpulse && source != null && velocity.sqrMagnitude > 0f) source.GenerateImpulseWithVelocity(velocity);
        CombatFeedbackTelemetry.Emit("camera-hit", owner.gameObject, target != null ? target.gameObject : null,
            hitEventIndex, v2 ? "v2" : "legacy", velocity.magnitude);
        return true;
    }

    public void EndAttack(PlayerController owner, bool canceled = false)
    {
        if (!_ownsTargets || owner != _owner || _releasing) return;
        _releasing = true;
        _target = null;
        _releaseElapsed = 0;
        bool motionOverride = _v2?.Mode == CombatFeedbackChannelMode.Override;
        _releaseDuration = motionOverride ? canceled ? _v2.Motion.CancelDuration : _v2.Motion.ReturnDuration
            : _profile == null ? 0 : canceled ? _profile.CancelDuration : _profile.ReturnDuration;
        _returnClock = motionOverride ? _v2.Motion.ReturnClock : CombatFeedbackClock.World;
        _releaseFraming = _framing;
        _releasePosition = _positionOffset;
        _releaseFov = _fov;
        _releaseEuler = _motionEuler;
        _releaseWorldRotation = _motionRotation;
        if (canceled)
        {
            // Blend our in-flight feedback away; never clear other systems' impulses.
            _releasePosition += _mixer.Position;
            _releaseFov += _mixer.Fov;
            _releaseWorldRotation *= _mixer.Rotation;
            _mixer.Clear();
        }
        StopFeel();
        CombatFeedbackTelemetry.Emit("camera-return-start", owner.gameObject,
            detail: (canceled ? "cancel" : "normal") + ";clock=" + _returnClock + ";attack=" + _attackSequence,
            value: _releaseDuration);
        if (_releaseDuration <= 0 && !_mixer.Active) ResetPlayback("return-complete");
    }

    public void Cancel(PlayerController owner, bool immediate = false)
    {
        if (owner != _owner) return;
        if (immediate) ResetPlayback("cancel-immediate");
        else EndAttack(owner, true);
    }

    public bool IsOwnedBy(PlayerController player) => _ownsTargets && _owner == player;

    private void LateUpdate() => Tick(Time.deltaTime);

    private void Tick(float dt)
    {
        if (!_ownsTargets) return;
        if (_camera == null || !_camera.isActiveAndEnabled || _follow == null || _lookAt == null || _owner == null
            || !_owner.isActiveAndEnabled || _owner.StatManager == null || _owner.StatManager.CurrentHp <= 0)
        { ResetPlayback("owner-or-camera-unavailable"); return; }
        // Another camera system taking ownership must not have its targets overwritten on exit.
        if (_camera.Follow != _followProxy || _camera.LookAt != _lookProxy)
        { ResetPlayback("camera-target-ownership-changed"); return; }
        float actorDelta = _owner.ActionDeltaTime;
        _mixer.Tick(dt, actorDelta, (Time.unscaledDeltaTime * CombatHitStopService.ModalTimeMultiplier));
        // Scaled time: pause and hit stop also freeze attack framing.
        if (_releasing)
        {
            _releaseElapsed += CombatCameraV2Mixer.Delta(_returnClock, dt, actorDelta, (Time.unscaledDeltaTime * CombatHitStopService.ModalTimeMultiplier));
            float t = _releaseDuration <= 0 ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01(_releaseElapsed / _releaseDuration));
            _framing = _releaseFraming * (1 - t);
            _positionOffset = _releasePosition * (1 - t);
            _fov = _releaseFov * (1 - t);
            _motionEuler = _releaseEuler * (1 - t);
            _motionRotation = Quaternion.Slerp(_releaseWorldRotation, Quaternion.identity, t);
            _followProxy.rotation = Quaternion.Slerp(_followRotation, _follow.rotation, t);
            _lookProxy.rotation = Quaternion.Slerp(_lookRotation, _lookAt.rotation, t);
            if (_releaseElapsed >= _releaseDuration && !_mixer.Active) { ResetPlayback("return-complete"); return; }
        }
        else
        {
            bool overridden = _v2?.Mode == CombatFeedbackChannelMode.Override;
            bool disabled = _v2?.Mode == CombatFeedbackChannelMode.Off;
            var motion = overridden ? _v2.Motion : null;
            float response = motion != null ? motion.ResponseTime : _profile != null ? _profile.ResponseTime : 0.01f;
            float motionDelta = motion != null ? CombatCameraV2Mixer.Delta(motion.Clock, dt, actorDelta, (Time.unscaledDeltaTime * CombatHitStopService.ModalTimeMultiplier)) : dt;
            ReportLimit("ResponseTime", response, Mathf.Max(0.01f, response), "minimum camera response time is 0.01s");
            float blend = 1 - Mathf.Exp(-motionDelta / Mathf.Max(0.01f, response));
            float requestedWeight = motion != null ? motion.FramingWeight?.Evaluate(_progress) ?? 1
                : _profile != null ? _profile.FramingWeight?.Evaluate(_progress) ?? 1 : 0;
            float weight = disabled ? 0 : Mathf.Clamp01(requestedWeight);
            if (!disabled) ReportLimit("FramingWeight", requestedWeight, weight, "framing blend weight is limited to 0..1");
            float lookAhead = motion != null ? motion.DirectionLookAhead : _profile != null ? _profile.DirectionLookAhead : 0;
            Vector3 desired = _direction * lookAhead;
            if (IsTargetValid())
            {
                // Match player focus height rather than aiming at the enemy's feet.
                Vector3 targetPosition = _target.position + Vector3.up * (_lookAt.position.y - _owner.transform.position.y);
                desired = (targetPosition - _lookAt.position) * (motion != null ? motion.TargetWeight : _profile != null ? _profile.TargetWeight : 0);
            }
            float requestedFraming = desired.magnitude;
            desired = Vector3.ClampMagnitude(desired, Mathf.Max(0, motion != null ? motion.MaxFramingOffset : _profile != null ? _profile.MaxFramingOffset : 0));
            if (!disabled) ReportLimit("FramingOffsetMagnitude", requestedFraming, desired.magnitude, "authored MaxFramingOffset bounds target framing");
            _framing = Vector3.Lerp(_framing, desired * weight, blend);
            bool preserve = motion != null ? motion.PreserveChainViewDirection : _profile == null || _profile.PreserveChainViewDirection;
            Quaternion frame = preserve ? _viewFrame
                : (_owner.CameraTransform != null ? _owner.CameraTransform.rotation : _viewFrame);
            if (motion != null) frame = CombatCameraV2Mixer.Frame(motion.Space, frame, _direction);
            Vector3 position = motion != null ? CombatCameraV2Mixer.EvaluateVector(motion.PositionX, motion.PositionY, motion.PositionZ, _progress)
                : _profile != null ? _profile.CameraLocalOffset : Vector3.zero;
            _positionOffset = Vector3.Lerp(_positionOffset, disabled ? Vector3.zero : frame * position * (motion != null ? 1 : weight), blend);
            float desiredFov = disabled ? 0 : _feelActive && _feelZoom ? _feelFeedback.ZoomOffset
                : motion != null ? CombatCameraV2Mixer.Evaluate(motion.FovOffset, _progress) : _profile?.FovOffset?.Evaluate(_progress) ?? 0;
            _fov = Mathf.Lerp(_fov, desiredFov, blend);
            Vector3 desiredEuler = motion != null ? CombatCameraV2Mixer.EvaluateVector(motion.RotationX, motion.RotationY, motion.RotationZ, _progress) : Vector3.zero;
            _motionEuler = Vector3.Lerp(_motionEuler, disabled ? Vector3.zero : desiredEuler, blend);
            _motionRotation = CombatCameraV2Mixer.WorldRotation(frame, _motionEuler);
            _followProxy.rotation = preserve ? _followRotation : _follow.rotation;
            _lookProxy.rotation = preserve ? _lookRotation : _lookAt.rotation;
        }
        _followProxy.position = _follow.position + _framing + _positionOffset;
        _lookProxy.position = _lookAt.position + _framing;
    }

    private bool IsTargetValid()
    {
        if (_target == null || !_target.gameObject.activeInHierarchy) return false;
        var stats = _target.GetComponentInParent<EnemyStat>();
        if (stats != null && stats.IsDead) return false;
        return (_target.position - _owner.transform.position).sqrMagnitude
            <= Mathf.Pow(_v2?.Mode == CombatFeedbackChannelMode.Override ? _v2.Motion.MaxTargetDistance
                : _profile != null ? _profile.MaxTargetDistance : 0, 2);
    }

    private void EnsureProxies()
    {
        if (_followProxy == null) _followProxy = CreateProxy("Combat camera follow");
        if (_lookProxy == null) _lookProxy = CreateProxy("Combat camera aim");
    }

    private Transform CreateProxy(string label)
    {
        var go = new GameObject(label) { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        go.transform.SetParent(transform.parent, false);
        return go.transform;
    }

    private void ResetPlayback(string reason = "reset")
    {
        if (_ownsTargets)
            CombatFeedbackTelemetry.Emit(_releasing ? "camera-return-end" : "camera-end",
                _owner != null ? _owner.gameObject : null, detail: reason + ";attack=" + _attackSequence,
                value: _releaseElapsed);
        StopFeel();
        if (_ownsTargets && _camera != null)
        {
            var target = _camera.Target;
            if (target.TrackingTarget == _followProxy) target.TrackingTarget = _savedTarget.TrackingTarget;
            if (target.LookAtTarget == _lookProxy)
            {
                target.LookAtTarget = _savedTarget.LookAtTarget;
                target.CustomLookAtTarget = _savedTarget.CustomLookAtTarget;
            }
            _camera.Target = target;
        }
        _ownsTargets = _releasing = _hitPlayed = false;
        _profile = null;
        _v2 = null;
        _step = null;
        _confirmedHits.Reset();
        _mixer.Clear();
        _owner = null;
        _target = null;
        _framing = _positionOffset = Vector3.zero;
        _fov = _progress = 0;
        _motionEuler = _releaseEuler = Vector3.zero;
        _motionRotation = Quaternion.identity;
        _releaseWorldRotation = Quaternion.identity;
        _reportedLimits.Clear();
    }

    public override void PrePipelineMutateCameraStateCallback(CinemachineVirtualCameraBase vcam,
        ref CameraState state, float deltaTime)
    {
        if (!_ownsTargets) return;
        var lens = state.Lens;
        float requestedFov = lens.FieldOfView + _fov + _mixer.Fov;
        lens.FieldOfView = Mathf.Clamp(requestedFov, 1, 179);
        ReportLimit("FOV", requestedFov, lens.FieldOfView, "Cinemachine perspective FOV is limited to 1..179 degrees");
        state.Lens = lens;
    }

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (!_ownsTargets || stage != CinemachineCore.Stage.Finalize) return;
        state.PositionCorrection += _mixer.Position;
        Quaternion orientation = state.GetFinalOrientation();
        Quaternion world = _motionRotation * _mixer.Rotation;
        state.OrientationCorrection *= Quaternion.Inverse(orientation) * world * orientation;
        if (CombatFeedbackTelemetry.HasListeners)
            CombatFeedbackTelemetry.Emit("camera-output", _owner != null ? _owner.gameObject : null,
                detail: _releasing ? "return-final-pipeline" : "attack-final-pipeline",
                position: state.GetFinalPosition(), rotation: state.GetFinalOrientation().eulerAngles, fov: state.Lens.FieldOfView);
    }

    private void ReportLimit(string field, float requested, float applied, string reason)
    {
        if (!CombatFeedbackTelemetry.HasListeners || _owner == null) return;
        if (Mathf.Approximately(requested, applied)) { _reportedLimits.Remove(field); return; }
        if (_reportedLimits.TryGetValue(field, out var previous)
            && Mathf.Approximately(previous.x, requested) && Mathf.Approximately(previous.y, applied)) return;
        _reportedLimits[field] = new Vector2(requested, applied);
        CombatFeedbackTelemetry.Emit("value-limited", _owner.gameObject,
            detail: field + " requested=" + requested.ToString("R", CultureInfo.InvariantCulture)
                + " applied=" + applied.ToString("R", CultureInfo.InvariantCulture) + " reason=" + reason,
            value: applied);
    }

    private void OnDisable() => ResetPlayback("component-disabled");

    private void BeginFeel(CombatFeelFeedback prefab, float duration, float playbackSpeed, bool playZoom = true)
    {
        StopFeel();
        if (!Application.isPlaying || prefab == null) return;
        if (_feelFeedback == null || _feelPrefab != prefab)
        {
            if (_feelFeedback != null) DestroyProxy(_feelFeedback.gameObject);
            _feelFeedback = Instantiate(prefab, transform);
            _feelFeedback.name = prefab.name + " (Runtime)";
            _feelPrefab = prefab;
        }
        _feelActive = _feelFeedback.IsConfigured;
        if (_feelActive) _feelFeedback.Begin(duration, playbackSpeed, playZoom);
    }

    private void StopFeel()
    {
        if (_feelFeedback != null) _feelFeedback.Stop();
        _feelActive = false;
    }

    protected override void OnDestroy()
    {
        ResetPlayback("destroyed");
        if (_followProxy != null) DestroyProxy(_followProxy.gameObject);
        if (_lookProxy != null) DestroyProxy(_lookProxy.gameObject);
        if (_feelFeedback != null) DestroyProxy(_feelFeedback.gameObject);
        base.OnDestroy();
    }

    private static void DestroyProxy(GameObject go)
    {
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
    }
}
