using UnityEngine;
using Unity.Cinemachine;

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

    public bool IsPlaying => _ownsTargets && !_releasing;
    public float Progress => _progress;

    protected override void Awake()
    {
        base.Awake();
        _camera = GetComponent<CinemachineCamera>();
    }

    public bool BeginAttack(AttackCameraProfile profile, PlayerController owner,
        Transform target, Vector3 direction, bool continuing = false)
    {
        if (!isActiveAndEnabled || owner == null) return false;
        if (profile == null) { EndAttack(owner); return false; }
        if (_camera == null) _camera = GetComponent<CinemachineCamera>();
        bool keepFrame = _ownsTargets && !_releasing && _owner == owner
            && continuing && profile.PreserveChainViewDirection;
        if (_ownsTargets && _owner != owner) ResetPlayback();
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
        _target = target;
        _direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        _progress = 0;
        _hitPlayed = _releasing = false;
        return true;
    }

    public void SetProgress(PlayerController owner, float progress)
    {
        if (owner == _owner && IsPlaying) _progress = Mathf.Clamp01(progress);
    }

    // Returns true when this profile owns hit feedback, including suppressed duplicate hits.
    public bool NotifyHit(PlayerController owner, Transform target, Vector3 direction)
    {
        if (owner != _owner || !IsPlaying || _profile == null) return false;
        if (_hitPlayed) return true;
        _hitPlayed = true;
        _target = target;
        var source = owner.ImpulseSource;
        if (source != null && _profile.HitImpulse > 0 && direction.sqrMagnitude > 0.001f)
        {
            Vector3 velocity = direction.normalized;
            var listener = GetComponent<CinemachineImpulseListener>();
            if (listener != null && listener.UseCameraSpace)
                velocity = Quaternion.Inverse(_camera.State.GetFinalOrientation()) * velocity;
            source.GenerateImpulseWithVelocity(velocity * source.DefaultVelocity.magnitude * _profile.HitImpulse);
        }
        return true;
    }

    public void EndAttack(PlayerController owner, bool canceled = false)
    {
        if (!_ownsTargets || owner != _owner || _releasing) return;
        _releasing = true;
        _target = null;
        _releaseElapsed = 0;
        _releaseDuration = _profile == null ? 0 : canceled ? _profile.CancelDuration : _profile.ReturnDuration;
        _releaseFraming = _framing;
        _releasePosition = _positionOffset;
        _releaseFov = _fov;
        if (_releaseDuration <= 0) ResetPlayback();
    }

    public void Cancel(PlayerController owner, bool immediate = false)
    {
        if (owner != _owner) return;
        if (immediate) ResetPlayback();
        else EndAttack(owner, true);
    }

    private void LateUpdate() => Tick(Time.deltaTime);

    private void Tick(float dt)
    {
        if (!_ownsTargets) return;
        if (_camera == null || !_camera.isActiveAndEnabled || _follow == null || _lookAt == null || _owner == null
            || !_owner.isActiveAndEnabled || _owner.StatManager == null || _owner.StatManager.CurrentHp <= 0)
        { ResetPlayback(); return; }
        // Another camera system taking ownership must not have its targets overwritten on exit.
        if (_camera.Follow != _followProxy || _camera.LookAt != _lookProxy)
        { ResetPlayback(); return; }
        // Scaled time: pause and hit stop also freeze attack framing.
        if (_releasing)
        {
            _releaseElapsed += dt;
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(_releaseElapsed / _releaseDuration));
            _framing = _releaseFraming * (1 - t);
            _positionOffset = _releasePosition * (1 - t);
            _fov = _releaseFov * (1 - t);
            _followProxy.rotation = Quaternion.Slerp(_followRotation, _follow.rotation, t);
            _lookProxy.rotation = Quaternion.Slerp(_lookRotation, _lookAt.rotation, t);
            if (_releaseElapsed >= _releaseDuration) { ResetPlayback(); return; }
        }
        else
        {
            if (_profile == null) { ResetPlayback(); return; }
            float blend = 1 - Mathf.Exp(-dt / Mathf.Max(0.01f, _profile.ResponseTime));
            float weight = Mathf.Clamp01(_profile.FramingWeight?.Evaluate(_progress) ?? 1);
            Vector3 desired = _direction * _profile.DirectionLookAhead;
            if (IsTargetValid())
            {
                // Match player focus height rather than aiming at the enemy's feet.
                Vector3 targetPosition = _target.position + Vector3.up * (_lookAt.position.y - _owner.transform.position.y);
                desired = (targetPosition - _lookAt.position) * _profile.TargetWeight;
            }
            desired = Vector3.ClampMagnitude(desired, Mathf.Max(0, _profile.MaxFramingOffset));
            _framing = Vector3.Lerp(_framing, desired * weight, blend);
            Quaternion frame = _profile.PreserveChainViewDirection ? _viewFrame
                : (_owner.CameraTransform != null ? _owner.CameraTransform.rotation : _viewFrame);
            _positionOffset = Vector3.Lerp(_positionOffset, frame * _profile.CameraLocalOffset * weight, blend);
            _fov = Mathf.Lerp(_fov, _profile.FovOffset?.Evaluate(_progress) ?? 0, blend);
            _followProxy.rotation = _profile.PreserveChainViewDirection ? _followRotation : _follow.rotation;
            _lookProxy.rotation = _profile.PreserveChainViewDirection ? _lookRotation : _lookAt.rotation;
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
            <= _profile.MaxTargetDistance * _profile.MaxTargetDistance;
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

    private void ResetPlayback()
    {
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
        _owner = null;
        _target = null;
        _framing = _positionOffset = Vector3.zero;
        _fov = _progress = 0;
    }

    public override void PrePipelineMutateCameraStateCallback(CinemachineVirtualCameraBase vcam,
        ref CameraState state, float deltaTime)
    {
        if (!_ownsTargets) return;
        var lens = state.Lens;
        lens.FieldOfView = Mathf.Clamp(lens.FieldOfView + _fov, 1, 179);
        state.Lens = lens;
    }

    private void OnDisable() => ResetPlayback();

    protected override void OnDestroy()
    {
        ResetPlayback();
        if (_followProxy != null) DestroyProxy(_followProxy.gameObject);
        if (_lookProxy != null) DestroyProxy(_lookProxy.gameObject);
        base.OnDestroy();
    }

    private static void DestroyProxy(GameObject go)
    {
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
    }
}
