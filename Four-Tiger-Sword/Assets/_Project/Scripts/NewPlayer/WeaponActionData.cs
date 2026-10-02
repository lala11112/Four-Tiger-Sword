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
public class HitEvent
{
    [Tooltip("이 히트가 발동되는 시작 시점 (초)")]
    public float StartTime = 0.1f;

    [Tooltip("판정 지속 시간 (초)")]
    public float Duration = 0.05f;

    [Tooltip("이 히트의 데미지 (0이면 WeaponActionData의 기본 Damage 사용)")]
    public float Damage = 0;
    [Tooltip("이 히트의 강인도 데미지 (0이면 WeaponActionData의 기본 PoiseDamage 사용)")]
    public float PoiseDamage = 10f;
    
    [Tooltip("이 히트의 경직 저항 값")]
    public StaggerResistLevel StaggerResistLevel = StaggerResistLevel.NONE;
}

[Serializable]
public class WeaponActionData
{
    [Header("Animation & Timing")]
    [Tooltip("애니메이터에 재생할 트리거 또는 State 이름")]
    public string AnimationName; 
    
    [Tooltip("이 공격 애니메이션의 총 지속 시간")]
    public float Duration = 0.5f;
    
    [Tooltip("다음 콤보 입력을 받아들이기 시작하는 시간 (이 시간 이후에 공격을 누르면 콤보 발동)")]
    public float ComboTransitionTime = 0.3f;

    [Header("Combat & Physics")]
    [Tooltip("이 타수의 데미지")]
    public float Damage = 10;

    [Tooltip("이 타수의 강인도 데미지")]
    public float PoiseDamage = 10f;

    [Tooltip("이 타수의 선딜레이")]
    public float HitStartTime = 0.1f;

    [Tooltip("판정시간")]
    public float HitDuration = 0.1f;

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

    [Tooltip("적에게 가하는 넉백 초기 속도 (m/s). 공격력과 무관합니다. 적의 저항을 뺀 뒤 감속하며 이동합니다.")]
    public float KnockbackForce = 8f;

    [Tooltip("경직 저항 값")]
    public StaggerResistLevel StaggerResistLevel = StaggerResistLevel.NONE;

    [Header("Multi-Hit (선택사항)")]
    [Tooltip("여러 번 히트시키려면 여기에 추가하세요. 비어있으면 위의 HitStartTime/HitDuration/Damage를 사용합니다.")]
    public List<HitEvent> HitEvents = new List<HitEvent>();

    [Header("Effects (선택사항)")]
    [Tooltip("검기 또는 타격 이펙트 프리팹")]
    public GameObject SlashVFX;
    
    [Tooltip("휘두르는 사운드")]
    public AudioClip SwingSound;

    [Header("Attack Movement")]
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
    [Range(0f, 90f), Tooltip("접근 이동을 허용하는 타깃과의 최대 방향 차이 (도). 타깃 선택 각도와는 무관합니다.")]
    public float ApproachAlignmentAngle = 15f;

    [Header("Legacy Movement (Use Distance Movement off)")]
    [Tooltip("기존 전진 속도 커브. 공격 시작 시 적분하여 누적 이동 데이터로 사용합니다.")]
    public AnimationCurve ThrustCurve = AnimationCurve.Constant(0, 1, 0); // 기본값: 0
    
    [Tooltip("커브 값에 곱해줄 최대 속도")]
    public float ThrustMultiplier = 10f; 

    [Header("Soft Target Approach")]
    [Tooltip("일반/공통 강공격에 타깃 접근과 정지 간격 적용. 최대 거리는 타깃이 없어도 적용됩니다.")]
    public bool UseTargetApproach;
    [Tooltip("각 타수의 마지막 타격이 끝날 때까지 적의 현재 위치로 회전하고 접근합니다.")]
    public bool FollowMovingTarget = true;
    [Min(0f), Tooltip("타깃 추적 속도 (공격 속도 1 기준 m/s). 기존 전진 커브 대신 사용합니다.")]
    public float TargetFollowSpeed = 6f;
    [Min(0f), Tooltip("초기 방향 정렬 이후 타깃을 따라 회전하는 속도 (도/초, 공격 속도 1 기준)")]
    public float TargetFollowRotationSpeed = 720f;
    [Min(0f), Tooltip("플레이어 몸체와 적 콜라이더 표면 사이에 남길 간격 (m)")]
    public float StopDistance = 0.25f;
    [Min(0f), Tooltip("기존 Thrust 모드의 접근 종료 시점. 거리 모드에서는 Movement End Time 사용")]
    public float ApproachEndTime = 0.3f;
    [Min(0f), Tooltip("회전 종료 시점. 이동 시작/첫 타격보다 늦으면 앞당깁니다. 0이면 시작 시 방향을 즉시 맞춥니다.")]
    public float RotationEndTime = 0.2f;
    [Min(0f), Tooltip("한 타수에서 자동 전진할 수 있는 최대 거리 (m)")]
    public float MaxApproachDistance = 2f;
}
