using MoreMountains.Feedbacks;
using MoreMountains.FeedbacksForThirdParty;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Per-camera instance of an authored Feel preset. Feel animates an offset, while
/// CombatCameraDirector retains ownership of the lens, framing and return blend.
/// </summary>
[DisallowMultipleComponent]
public sealed class CombatFeelFeedback : MonoBehaviour
{
    public MMF_Player ZoomFeedbacks;
    public MMF_Player HitFeedbacks;
    public CanvasGroup FlashGroup;

    [Header("Hit shake direction")]
    [Tooltip("화면 기준 방향으로 흔듭니다. 끄면 공격 진행 방향을 사용합니다.")]
    public bool OverrideImpulseDirection;
    [Tooltip("화면 기준 X=오른쪽, Y=위, Z=전방. 방향만 사용하며 강도는 카메라 프로필의 Hit Impulse로 조절합니다.")]
    public Vector3 CameraSpaceImpulseDirection = Vector3.down;

    [HideInInspector] public float ZoomOffset;

    private MMF_Property _zoom;
    private MMF_CinemachineImpulseSource _impulse;
    private bool _initialized, _hitPlayed;
    private bool _zoomEnabled;

    public bool IsConfigured => ZoomFeedbacks != null && HitFeedbacks != null && FlashGroup != null
        && ZoomFeedbacks.GetFeedbackOfType<MMF_Property>() != null
        && HitFeedbacks.GetFeedbackOfType<MMF_CinemachineImpulseSource>() != null;

    public void Begin(float duration, float playbackSpeed, bool playZoom = true)
    {
        if (!IsConfigured) return;
        if (!_initialized)
        {
            _zoom = ZoomFeedbacks.GetFeedbackOfType<MMF_Property>();
            _impulse = HitFeedbacks.GetFeedbackOfType<MMF_CinemachineImpulseSource>();
            ZoomFeedbacks.Initialization();
            HitFeedbacks.Initialization();
            _initialized = true;
        }
        Stop();
        _hitPlayed = false;
        _zoomEnabled = playZoom;
        _zoom.Duration = Mathf.Max(0.01f, duration);
        SetPlaybackSpeed(playbackSpeed);
        if (_zoomEnabled) ZoomFeedbacks.PlayFeedbacks();
    }

    public void SetPlaybackSpeed(float playbackSpeed)
    {
        if (ZoomFeedbacks != null)
        {
            float speed = Mathf.Max(0.01f, playbackSpeed);
            ZoomFeedbacks.TimescaleMultiplier = speed;
            // Feel also divides FeedbackDuration by TimescaleMultiplier. Cancel that
            // division so the property curve advances at attack speed only once.
            ZoomFeedbacks.DurationMultiplier = speed;
        }
    }

    public void PlayHit(CinemachineImpulseSource source, Vector3 velocity, bool playImpulse = true, bool allowRepeat = false)
    {
        if (!_initialized || (_hitPlayed && !allowRepeat)) return;
        _hitPlayed = true;
        // Reuse the existing impulse source and listener, including their time policy.
        // The native Feel source feedback does not change the global impulse clock.
        _impulse.ImpulseSource = source;
        _impulse.Velocity = velocity;
        _impulse.Active = playImpulse && source != null && velocity.sqrMagnitude > 0.000001f;
        HitFeedbacks.PlayFeedbacks(source != null ? source.transform.position : transform.position);
    }

    public void Stop()
    {
        _hitPlayed = true;
        if (_initialized)
        {
            ZoomFeedbacks.StopFeedbacks();
            HitFeedbacks.StopFeedbacks();
            // Do not restore the impulse feedback: Feel's restore clears ALL impulses,
            // including those belonging to other attacks or environmental effects.
        }
        ZoomOffset = 0f;
        _zoomEnabled = false;
        if (FlashGroup != null) FlashGroup.alpha = 0f;
    }

    private void OnDisable() => Stop();
}
