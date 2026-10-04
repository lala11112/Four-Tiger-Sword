using UnityEngine;

/// <summary>Shared authoring data only. Playback state belongs to CombatCameraDirector.</summary>
[CreateAssetMenu(fileName = "AttackCamera", menuName = "Combat/Attack Camera Profile")]
public sealed class AttackCameraProfile : ScriptableObject
{
    [Header("Attack progress (X = 0..1, independent of attack speed)")]
    [Tooltip("기본 시야각에 더할 각도. 양수는 시야를 넓힙니다.")]
    public AnimationCurve FovOffset = new AnimationCurve(
        new Keyframe(0, 0), new Keyframe(0.15f, 5), new Keyframe(0.45f, 3), new Keyframe(1, 0));
    public AnimationCurve FramingWeight = AnimationCurve.Linear(0, 1, 1, 1);

    [Header("Framing")]
    [Range(0, 0.5f)] public float TargetWeight = 0.35f;
    [Min(0)] public float MaxTargetDistance = 12f;
    [Min(0)] public float MaxFramingOffset = 2.5f;
    [Tooltip("타깃이 없을 때 진행 방향으로 보여 줄 거리(m)")]
    [Min(0)] public float DirectionLookAhead = 0.8f;
    [Tooltip("촬영 방향 기준 카메라 위치 보정(m). X=오른쪽, Y=위, Z=전방")]
    public Vector3 CameraLocalOffset = new Vector3(0, 0.15f, -0.3f);
    [Tooltip("연속 공격 중 보정 기준 방향을 유지합니다. 사용자 카메라 입력은 차단하지 않습니다.")]
    public bool PreserveChainViewDirection = true;
    [Min(0.01f)] public float ResponseTime = 0.08f;
    [Min(0)] public float ReturnDuration = 0.3f;
    [Min(0)] public float CancelDuration = 0.12f;

    [Header("Actual hit only")]
    [Min(0)] public float HitImpulse = 0.3f;
}
