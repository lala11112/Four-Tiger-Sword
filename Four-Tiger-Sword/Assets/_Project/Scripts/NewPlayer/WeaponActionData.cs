using System;
using System.Collections.Generic;
using UnityEngine;

public enum HitBoxShape
{
    Sphere,
    Box,
    Capsule
}

[Serializable]
public class HitVfxSettings
{
    [Tooltip("비워두면 공격 기본 프리팹, 그 다음 폼의 HitVFX를 사용합니다.")]
    public GameObject Prefab;
    [Tooltip("프리팹 원본 크기에 곱할 XYZ 배율. 기본값은 (1, 1, 1)입니다.")]
    public Vector3 Scale = Vector3.one;
    [Tooltip("몬스터의 Spawn Point 회전에 더할 로컬 회전 (도)")]
    public Vector3 Rotation;
}

[Serializable]
public class HitEvent
{
    [InspectorName("밸런스 (Balance)")]
    public HitBalanceSettings Balance = new HitBalanceSettings();

    [InspectorName("동작·판정 (Execution)")]
    public HitExecutionSettings Execution = new HitExecutionSettings();

    [InspectorName("타격 피드백 (Feedback)")]
    public HitFeedbackSettings Feedback = new HitFeedbackSettings();

    // Compatibility accessors keep combat code and automation on the same serialized values.
    public float StartTime { get => Execution.StartTime; set => Execution.StartTime = value; }
    public float Duration { get => Execution.Duration; set => Execution.Duration = value; }
    public float Damage { get => Balance.Damage; set => Balance.Damage = value; }
    public float PoiseDamage { get => Balance.PoiseDamage; set => Balance.PoiseDamage = value; }
    public StaggerResistLevel StaggerResistLevel { get => Balance.StaggerResistLevel; set => Balance.StaggerResistLevel = value; }
    public bool OverrideKnockbackForce { get => Balance.OverrideKnockbackForce; set => Balance.OverrideKnockbackForce = value; }
    public float KnockbackForce { get => Balance.KnockbackForce; set => Balance.KnockbackForce = value; }
    public bool OverrideHitStop { get => Feedback.HitStop.OverrideHitStop; set => Feedback.HitStop.OverrideHitStop = value; }
    public float HitStopDuration { get => Feedback.HitStop.HitStopDuration; set => Feedback.HitStop.HitStopDuration = value; }
    public float HitStopTimeScale { get => Feedback.HitStop.HitStopTimeScale; set => Feedback.HitStop.HitStopTimeScale = value; }
    public bool OverrideHitVFX { get => Feedback.OverrideHitVFX; set => Feedback.OverrideHitVFX = value; }
    public HitVfxSettings HitEffect { get => Feedback.HitEffect; set => Feedback.HitEffect = value; }
}

[Serializable]
public class WeaponActionData
{
    [InspectorName("밸런스 (Balance)")]
    public ActionBalanceSettings Balance = new ActionBalanceSettings();

    [InspectorName("동작·판정 (Execution)")]
    public AttackExecutionSettings Execution = new AttackExecutionSettings();

    [InspectorName("타격 피드백 (Feedback)")]
    public AttackFeedbackSettings Feedback = new AttackFeedbackSettings();

    [Tooltip("여러 번 히트시키려면 여기에 추가하세요. 비어 있으면 Execution의 HitStartTime/HitDuration과 Balance의 Damage를 사용합니다.")]
    public List<HitEvent> HitEvents = new List<HitEvent>();

    // Compatibility accessors keep combat code and automation on the same serialized values.
    public string AnimationName { get => Execution.AnimationName; set => Execution.AnimationName = value; }
    public float Duration { get => Execution.Duration; set => Execution.Duration = value; }
    public float ComboTransitionTime { get => Execution.ComboTransitionTime; set => Execution.ComboTransitionTime = value; }
    public float Damage { get => Balance.Damage; set => Balance.Damage = value; }
    public float PoiseDamage { get => Balance.PoiseDamage; set => Balance.PoiseDamage = value; }
    public float HitStartTime { get => Execution.HitStartTime; set => Execution.HitStartTime = value; }
    public float HitDuration { get => Execution.HitDuration; set => Execution.HitDuration = value; }
    public Vector3 HitBoxOffset { get => Execution.Hitbox.HitBoxOffset; set => Execution.Hitbox.HitBoxOffset = value; }
    public HitBoxShape HitBoxShape { get => Execution.Hitbox.HitBoxShape; set => Execution.Hitbox.HitBoxShape = value; }
    public float HitBoxRadius { get => Execution.Hitbox.HitBoxRadius; set => Execution.Hitbox.HitBoxRadius = value; }
    public Vector3 HitBoxSize { get => Execution.Hitbox.HitBoxSize; set => Execution.Hitbox.HitBoxSize = value; }
    public float HitBoxHeight { get => Execution.Hitbox.HitBoxHeight; set => Execution.Hitbox.HitBoxHeight = value; }
    public float KnockbackForce { get => Balance.KnockbackForce; set => Balance.KnockbackForce = value; }
    public StaggerResistLevel StaggerResistLevel { get => Balance.StaggerResistLevel; set => Balance.StaggerResistLevel = value; }
    public bool OverrideHitStop { get => Feedback.HitStop.OverrideHitStop; set => Feedback.HitStop.OverrideHitStop = value; }
    public float HitStopDuration { get => Feedback.HitStop.HitStopDuration; set => Feedback.HitStop.HitStopDuration = value; }
    public float HitStopTimeScale { get => Feedback.HitStop.HitStopTimeScale; set => Feedback.HitStop.HitStopTimeScale = value; }
    public HitVfxSettings HitEffect { get => Feedback.HitEffect; set => Feedback.HitEffect = value; }
    public bool DisableTrail { get => Feedback.Trail.DisableTrail; set => Feedback.Trail.DisableTrail = value; }
    public bool UseCustomTrailTiming { get => Feedback.Trail.UseCustomTrailTiming; set => Feedback.Trail.UseCustomTrailTiming = value; }
    public float TrailStartTime { get => Feedback.Trail.TrailStartTime; set => Feedback.Trail.TrailStartTime = value; }
    public float TrailEndTime { get => Feedback.Trail.TrailEndTime; set => Feedback.Trail.TrailEndTime = value; }
    public GameObject SlashVFX { get => Feedback.Slash.SlashVFX; set => Feedback.Slash.SlashVFX = value; }
    public float SlashVFXStartTime { get => Feedback.Slash.SlashVFXStartTime; set => Feedback.Slash.SlashVFXStartTime = value; }
    public Vector3 SlashVFXOffset { get => Feedback.Slash.SlashVFXOffset; set => Feedback.Slash.SlashVFXOffset = value; }
    public Vector3 SlashVFXRotation { get => Feedback.Slash.SlashVFXRotation; set => Feedback.Slash.SlashVFXRotation = value; }
    public float SlashVFXScale { get => Feedback.Slash.SlashVFXScale; set => Feedback.Slash.SlashVFXScale = value; }
    public float SlashVFXLifetime { get => Feedback.Slash.SlashVFXLifetime; set => Feedback.Slash.SlashVFXLifetime = value; }
    public AudioClip SwingSound { get => Feedback.SwingSound; set => Feedback.SwingSound = value; }
    public AttackCameraProfile CameraProfile { get => Feedback.Camera.CameraProfile; set => Feedback.Camera.CameraProfile = value; }
    public float CameraShakeStrength { get => Feedback.Camera.CameraShakeStrength; set => Feedback.Camera.CameraShakeStrength = value; }
    public bool OverrideCameraShakeDirection { get => Feedback.Camera.OverrideCameraShakeDirection; set => Feedback.Camera.OverrideCameraShakeDirection = value; }
    public Vector3 CameraShakeDirection { get => Feedback.Camera.CameraShakeDirection; set => Feedback.Camera.CameraShakeDirection = value; }
    public bool UseDistanceMovement { get => Execution.Movement.UseDistanceMovement; set => Execution.Movement.UseDistanceMovement = value; }
    public float ForwardDistance { get => Execution.Movement.ForwardDistance; set => Execution.Movement.ForwardDistance = value; }
    public float MovementStartTime { get => Execution.Movement.MovementStartTime; set => Execution.Movement.MovementStartTime = value; }
    public float MovementEndTime { get => Execution.Movement.MovementEndTime; set => Execution.Movement.MovementEndTime = value; }
    public AnimationCurve MovementProgress { get => Execution.Movement.MovementProgress; set => Execution.Movement.MovementProgress = value; }
    public float UntargetedDistanceMultiplier { get => Execution.Movement.UntargetedDistanceMultiplier; set => Execution.Movement.UntargetedDistanceMultiplier = value; }
    public float ApproachAlignmentAngle { get => Execution.Movement.TargetApproach.ApproachAlignmentAngle; set => Execution.Movement.TargetApproach.ApproachAlignmentAngle = value; }
    public AnimationCurve ThrustCurve { get => Execution.Movement.ThrustCurve; set => Execution.Movement.ThrustCurve = value; }
    public float ThrustMultiplier { get => Execution.Movement.ThrustMultiplier; set => Execution.Movement.ThrustMultiplier = value; }
    public bool UseTargetApproach { get => Execution.Movement.TargetApproach.UseTargetApproach; set => Execution.Movement.TargetApproach.UseTargetApproach = value; }
    public bool FollowMovingTarget { get => Execution.Movement.TargetApproach.FollowMovingTarget; set => Execution.Movement.TargetApproach.FollowMovingTarget = value; }
    public float TargetFollowSpeed { get => Execution.Movement.TargetApproach.TargetFollowSpeed; set => Execution.Movement.TargetApproach.TargetFollowSpeed = value; }
    public float TargetFollowRotationSpeed { get => Execution.Movement.TargetApproach.TargetFollowRotationSpeed; set => Execution.Movement.TargetApproach.TargetFollowRotationSpeed = value; }
    public float StopDistance { get => Execution.Movement.TargetApproach.StopDistance; set => Execution.Movement.TargetApproach.StopDistance = value; }
    public float ApproachEndTime { get => Execution.Movement.TargetApproach.ApproachEndTime; set => Execution.Movement.TargetApproach.ApproachEndTime = value; }
    public float RotationEndTime { get => Execution.Movement.TargetApproach.RotationEndTime; set => Execution.Movement.TargetApproach.RotationEndTime = value; }
    public float MaxApproachDistance { get => Execution.Movement.TargetApproach.MaxApproachDistance; set => Execution.Movement.TargetApproach.MaxApproachDistance = value; }
}

[Serializable]
public class ActionBalanceSettings
{
    public bool EnableV2;
    [Tooltip("이 타수의 데미지")]
    public float Damage = 10;

    [Tooltip("이 타수의 강인도 데미지")]
    public float PoiseDamage = 10f;

    [Tooltip("적에게 가하는 넉백 초기 속도 (m/s). 공격력과 무관합니다. 적의 EnemyData에 설정된 이동 시간 동안 감속하며 최대 거리 이내로 이동합니다.")]
    public float KnockbackForce = 8f;

    [Tooltip("경직 저항 값")]
    public StaggerResistLevel StaggerResistLevel = StaggerResistLevel.NONE;
}

[Serializable]
public class HitBalanceSettings
{
    public bool EnableV2;
    [Tooltip("이 히트의 데미지 (0이면 WeaponActionData의 기본 Damage 사용)")]
    public float Damage = 0;

    [Tooltip("이 히트의 강인도 데미지 (0이면 WeaponActionData의 기본 PoiseDamage 사용)")]
    public float PoiseDamage = 10f;

    [Tooltip("이 히트의 경직 저항 값")]
    public StaggerResistLevel StaggerResistLevel = StaggerResistLevel.NONE;

    [Tooltip("켜면 이 히트에 아래 Knockback Force를 적용합니다. 끄면 공격의 기본 Knockback Force를 사용합니다.")]
    public bool OverrideKnockbackForce;

    [Min(0f), Tooltip("이 히트가 적에게 가하는 넉백 초기 속도 (m/s). 0이면 넉백 없음. 적의 EnemyData에 설정된 이동 시간과 최대 거리가 적용됩니다.")]
    public float KnockbackForce = 8f;
}

[Serializable]
public class HitExecutionSettings
{
    [Tooltip("이 히트가 발동되는 시작 시점 (초)")]
    public float StartTime = 0.1f;

    [Tooltip("판정 지속 시간 (초)")]
    public float Duration = 0.05f;
}

public enum HitStopScope { Global = 0, Attacker = 1, Victim = 2, Both = 3 }
public enum HitStopStacking { Replace = 0, Extend = 1, Maximum = 2 }

[Serializable]
public class HitStopSettings
{
    [Tooltip("기존에 이 채널을 사용하지 않던 특수 공격에서도 명시적으로 적용합니다.")]
    public bool EnableV2;
    [Tooltip("켜면 이 항목의 히트스탑을 적용합니다. 끄면 개별 히트는 공격 설정, 공격은 PlayerController 기본값을 사용합니다.")]
    public bool OverrideHitStop;

    [Min(0f), Tooltip("실제 적중 시 히트스탑 지속 시간(실제 시간 기준 초). 0이면 이 타수의 히트스탑을 끕니다.")]
    public float HitStopDuration = 0.08f;

    [Range(0f, 1f), Tooltip("히트스탑 중 시간 배율. 0이면 완전 정지, 1이면 정상 속도입니다.")]
    public float HitStopTimeScale = 0.05f;

    public HitStopScope Scope = HitStopScope.Global;
    public HitStopStacking Stacking = HitStopStacking.Replace;
}

[Serializable]
public class AttackHitboxSettings
{
    [Tooltip("히트박스 생성위치")]
    public Vector3 HitBoxOffset = new Vector3(0f, 0f, 1f);

    [Tooltip("히트박스")]
    public HitBoxShape HitBoxShape = HitBoxShape.Sphere;

    [Tooltip("구, 캡슐 히트박스 크기")]
    public float HitBoxRadius = 1.5f;

    [Tooltip("박스 히트박스 크기")]
    public Vector3 HitBoxSize = new Vector3(1f, 1f, 1f);

    [Tooltip("캡슐 히트박스 높이")]
    public float HitBoxHeight = 2f;
}

[Serializable]
public class AttackTargetApproachSettings
{
    [Tooltip("일반/공통 강공격에 타깃 접근과 정지 간격 적용. 최대 거리는 타깃이 없어도 적용됩니다.")]
    public bool UseTargetApproach;

    [Tooltip("현재 타수에 아직 시작하지 않은 히트가 있을 때 적의 현재 위치로 회전하고 접근합니다. 마지막 히트가 시작되면 추적을 멈춥니다.")]
    public bool FollowMovingTarget = true;

    [Min(0f), Tooltip("타깃 추적 속도 (공격 속도 1 기준 m/s). 기존 전진 커브 대신 사용합니다.")]
    public float TargetFollowSpeed = 6f;

    [Min(0f), Tooltip("초기 방향 정렬 이후 타깃을 따라 회전하는 속도 (도/초, 공격 속도 1 기준)")]
    public float TargetFollowRotationSpeed = 720f;

    [Min(0f), Tooltip("플레이어 몸체와 적 콜라이더 표면 사이에 남길 간격 (m)")]
    public float StopDistance = 0.25f;

    [Range(0f, 90f), Tooltip("접근 이동을 허용하는 타깃과의 최대 방향 차이 (도). 타깃 선택 각도와는 무관합니다.")]
    public float ApproachAlignmentAngle = 15f;

    [Min(0f), Tooltip("기존 Thrust 모드의 접근 종료 시점. 거리 모드에서는 Movement End Time 사용")]
    public float ApproachEndTime = 0.3f;

    [Min(0f), Tooltip("회전 종료 시점. 이동 시작/첫 타격보다 늦으면 앞당깁니다. 0이면 시작 시 방향을 즉시 맞춥니다.")]
    public float RotationEndTime = 0.2f;

    [Min(0f), Tooltip("한 타수에서 자동 전진할 수 있는 최대 거리 (m)")]
    public float MaxApproachDistance = 2f;
}

[Serializable]
public class AttackMovementSettings
{
    [Tooltip("켜면 아래 거리/시간/누적 커브 사용. 끄면 기존 Thrust 속도 커브를 누적 거리로 자동 변환합니다.")]
    public bool UseDistanceMovement;

    [Min(0f), Tooltip("충돌과 타깃 정지 제한이 없을 때의 총 전진 거리 (m)")]
    public float ForwardDistance = 2f;

    [Min(0f), Tooltip("전진 시작 시점 (공격 시간). 시작 전 타깃 방향으로 회전합니다.")]
    public float MovementStartTime = 0.08f;

    [Min(0f), Tooltip("전진 종료 시점 (공격 시간). Duration 이내로 제한됩니다.")]
    public float MovementEndTime = 0.3f;

    [Tooltip("누적 이동 진행률: (0,0)에서 (1,1)로 증가. 속도 커브가 아닙니다.")]
    public AnimationCurve MovementProgress = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Range(0f, 1f), Tooltip("공격 시작 시 타깃이 없을 때 거리 배율. 0=제자리, 1=동일 거리")]
    public float UntargetedDistanceMultiplier = 1f;

    [Tooltip("기존 전진 속도 커브. 공격 시작 시 적분하여 누적 이동 데이터로 사용합니다.")]
    public AnimationCurve ThrustCurve = AnimationCurve.Constant(0, 1, 0);

    [Tooltip("커브 값에 곱해줄 최대 속도")]
    public float ThrustMultiplier = 10f;

    [InspectorName("타깃 접근")]
    public AttackTargetApproachSettings TargetApproach = new AttackTargetApproachSettings();
}

[Serializable]
public class AttackExecutionSettings
{
    [Tooltip("애니메이터에 재생할 트리거 또는 State 이름")]
    public string AnimationName;

    [Tooltip("이 공격 애니메이션의 총 지속 시간")]
    public float Duration = 0.5f;

    [Tooltip("다음 콤보 입력을 받아들이기 시작하는 시간 (이 시간 이후에 공격을 누르면 콤보 발동)")]
    public float ComboTransitionTime = 0.3f;

    [Tooltip("이 타수의 선딜레이")]
    public float HitStartTime = 0.1f;

    [Tooltip("판정시간")]
    public float HitDuration = 0.1f;

    [InspectorName("히트박스")]
    public AttackHitboxSettings Hitbox = new AttackHitboxSettings();

    [InspectorName("공격 이동")]
    public AttackMovementSettings Movement = new AttackMovementSettings();
}

[Serializable]
public class AttackTrailSettings
{
    [Tooltip("켜면 이 공격에서는 Trail을 재생하지 않습니다.")]
    public bool DisableTrail;

    [Tooltip("끄면 공격 시작부터 Duration까지 재생합니다. 켜면 아래 시작/종료 시간을 사용합니다. 타격 판정 시간과 무관합니다.")]
    public bool UseCustomTrailTiming;

    [Min(0f), Tooltip("공격 시작 기준 Trail 시작 시간 (공격 속도 1 기준 초)")]
    public float TrailStartTime;

    [Min(0f), Tooltip("공격 시작 기준 Trail 종료 시간 (공격 속도 1 기준 초). Duration 이내로 제한됩니다. 시작 이하이면 재생하지 않습니다.")]
    public float TrailEndTime = 0.5f;
}

[Serializable]
public class AttackSlashSettings
{
    [Tooltip("검기 또는 타격 이펙트 프리팹")]
    public GameObject SlashVFX;

    [Tooltip("음수면 각 Hit Event 시작에 재생, 0 이상이면 지정한 공격 시간에 한 번 재생")]
    public float SlashVFXStartTime = -1f;

    [Tooltip("히트박스 위치에 더할 로컬 위치")]
    public Vector3 SlashVFXOffset = new Vector3(0f, 1f, 0f);

    public Vector3 SlashVFXRotation;

    [Min(0.01f)] public float SlashVFXScale = 1f;

    [Min(0.01f), Tooltip("공격 속도 1 기준 이펙트 수명 (초)")]
    public float SlashVFXLifetime = 0.22f;

    [Min(0.01f), Tooltip("원본 파티클 재생 배율. 공격 속도와 함께 적용합니다.")]
    public float PlaybackSpeed = 1f;

    [Tooltip("자동 타격 동기화 시 Hit Start Time에 더할 시간. 음수면 검기 시작을 앞당깁니다.")]
    public float TimeOffset;

    [Tooltip("켜면 검기가 공격 중 플레이어의 이동/회전을 따라갑니다.")]
    public bool FollowPlayer;

    [Tooltip("다단 공격의 홀수 타격에서 Z 회전을 반대로 적용합니다. 개별 타격 회전 설정 시 끕니다.")]
    public bool AlternateDirection = true;
}

[Serializable]
public class AttackCameraSettings
{
    public CombatCameraV2Settings V2 = new CombatCameraV2Settings();
    public AttackCameraProfile CameraProfile;

    [Min(0f), Tooltip("적중 시 화면 흔들림 배율. 0=끄기, 1=Impulse Source 기본 세기, 2=2배")]
    public float CameraShakeStrength = 1f;

    [Tooltip("켜면 이 공격의 방향을 사용하고, 끄면 Impulse Source의 Default Velocity 방향을 사용합니다.")]
    public bool OverrideCameraShakeDirection;

    [Tooltip("방향만 사용합니다. Listener의 Use Camera Space가 켜져 있으면 X=화면 좌우, Y=상하, Z=앞뒤. 꺼져 있으면 월드 축입니다.")]
    public Vector3 CameraShakeDirection = Vector3.down;
}

[Serializable]
public class AttackFeedbackSettings
{
    [Tooltip("신규 피드백 실행 경로를 사용합니다. 기존 특수 공격의 연출은 기본값에서 유지됩니다.")]
    public bool EnableV2;
    [Tooltip("이 공격의 기본 피격 이펙트. HitEvents가 없어도 적용됩니다.")]
    public HitVfxSettings HitEffect = new HitVfxSettings();

    [Tooltip("휘두르는 사운드")]
    public AudioClip SwingSound;

    [Tooltip("각 타격 판정이 시작될 때 재생할 휘두름 사운드. 비워두면 기존 Swing Sound를 사용합니다.")]
    public CombatSoundSettings SwingAudio = new CombatSoundSettings();

    [Tooltip("실제 적중 시 재생할 사운드. 비워두면 폼의 공통 타격음을 사용합니다.")]
    public CombatSoundSettings HitAudio = new CombatSoundSettings();

    [InspectorName("히트스탑")]
    public HitStopSettings HitStop = new HitStopSettings();

    [InspectorName("트레일")]
    public AttackTrailSettings Trail = new AttackTrailSettings();

    [InspectorName("검기 이펙트")]
    public AttackSlashSettings Slash = new AttackSlashSettings();

    [InspectorName("카메라")]
    public AttackCameraSettings Camera = new AttackCameraSettings();
}

[Serializable]
public class HitFeedbackSettings
{
    public CombatCameraV2Settings CameraV2 = new CombatCameraV2Settings();
    [Tooltip("켜면 이 타격의 검기 프리팹/시간/위치를 공격 기본값 대신 사용합니다.")]
    public bool OverrideSlashVFX;

    [InspectorName("검기 이펙트")]
    public AttackSlashSettings Slash = new AttackSlashSettings();

    [Tooltip("지정하면 이 타격의 휘두름 사운드를 공격 기본값 대신 사용합니다.")]
    public CombatSoundSettings SwingAudio = new CombatSoundSettings();

    [Tooltip("지정하면 이 타격의 적중 사운드를 공격 기본값 대신 사용합니다.")]
    public CombatSoundSettings HitAudio = new CombatSoundSettings();

    [Tooltip("켜면 이 히트의 크기/회전을 아래 설정으로 덮어씁니다. 끄면 공격의 Hit Effect 설정을 사용합니다.")]
    public bool OverrideHitVFX;

    public HitVfxSettings HitEffect = new HitVfxSettings();

    [InspectorName("히트스탑")]
    public HitStopSettings HitStop = new HitStopSettings();
}
