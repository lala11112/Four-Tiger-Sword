using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class MonsterSkillData
{
    [Header("Animation & Timing")]
    [Tooltip("애니메이터에 재생할 트리거 또는 State 이름")]
    public string AnimationName; 
    
    [Tooltip("이 공격 애니메이션의 총 지속 시간")]
    public float Duration = 0.5f;

    [Tooltip("이 공격의 가중치")]
    public float Weight = 30f;

    [Header("Combat & Physics")]
    [Tooltip("이 타수의 데미지")]
    public int Damage = 10;

    [Tooltip("치명타 발생 확률 (0 ~ 1)")]
    [Range(0f, 1f)]
    public float CriticalChance = 0.1f; //나중에 몬스터 스텟 안으로 따로 뺄 예정 

    [Tooltip("치명타 데미지 배율")]
    public float CriticalMultiplier = 1.5f; //치명타 확율과 동일 

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
    
    [Tooltip("공격 시 앞으로 살짝 전진하는 힘 (타격감 상승)")]
    public float ForwardThrust = 2f; //일단 해봄 

    [Header("Multi-Hit (선택사항)")]
    [Tooltip("여러 번 히트시키려면 여기에 추가하세요. 비어있으면 위의 HitStartTime/HitDuration/Damage를 사용합니다.")]
    public List<HitEvent> HitEvents = new List<HitEvent>();

    [Header("Effects (선택사항)")]
    [Tooltip("검기 또는 타격 이펙트 프리팹")]
    public GameObject SlashVFX;
    
    [Tooltip("휘두르는 사운드")]
    public AudioClip SwingSound;

    [Tooltip("시간에 따른 전진 속도 그래프 (X축: 0~1 진행도, Y축: 속도 비율)")]
    public AnimationCurve ThrustCurve = AnimationCurve.Constant(0, 1, 0); // 기본값: 0
    
    [Tooltip("커브 값에 곱해줄 최대 속도")]
    public float ThrustMultiplier = 10f; 

    public float MaxDistance = 5f;
    public float MinDistance = 1f;
    public float Cooldown = 1f;

    public bool CanUse(float currentDistance, float lastUseTime, float currentTime)
    {
        bool inRange = currentDistance >= MinDistance && currentDistance <= MaxDistance;
        bool isCoolDownFinished = currentTime >= Cooldown + lastUseTime;
        return inRange && isCoolDownFinished;
    }
}