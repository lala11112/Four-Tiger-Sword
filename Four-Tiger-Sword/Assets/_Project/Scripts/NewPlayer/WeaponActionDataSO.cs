using System.Collections.Generic;
using UnityEngine;

// Shared action data only. Add form-specific settings to its derived SO.
public abstract class WeaponActionDataSO : ScriptableObject
{
    public string WeaponName;
    
    [Tooltip("1타, 2타, 3타... 에 대한 데이터 리스트")]
    public List<WeaponActionData> ComboSteps;

    [Header("Parry")]
    [Tooltip("이 폼의 패링 Animator 상태 이름")]
    public string ParryAnimationName = "Parry";

    [Header("Air Attack")]
    [Tooltip("공중 공격 시작 Animator 상태 이름. 비어 있으면 시작 단계를 생략합니다.")]
    public string AirAttackStartAnimationName;
    [Tooltip("시작 모션 길이(초). 공격 속도 배율이 적용됩니다.")]
    [Min(0f)] public float AirAttackStartDuration = 0.15f;
    [Tooltip("시작 모션 이후 착지할 때까지 재생할 Animator 상태 이름. 반복 클립을 사용하세요.")]
    public string AirAttackLoopAnimationName;
    [Tooltip("착지 시 마무리 공격 데이터. AnimationName은 마무리 모션, Duration은 마무리 시간입니다.")]
    public WeaponActionData AirAttackStep;

    [Header("Counter Attack (패링 반격)")]
    [Tooltip("패링 성공 후 반격 윈도우에서만 사용 가능한 반격 데이터")]
    public WeaponActionData CounterStep;

    [Header("Skill (E)")]
    [Tooltip("스킬 공격 데이터 (여러 단계 가능)")]
    public List<WeaponActionData> SkillSteps;

    [Header("Ultimate (R)")]
    [Tooltip("궁극기 공격 데이터 (여러 단계 가능)")]
    public List<WeaponActionData> UltimateSteps;

    [Header("Hit Effect")]
    [Tooltip("적을 타격했을 때 적 위치에 스폰할 이펙트 프리팹")]
    public GameObject HitVFX;

    [Tooltip("적을 타격했을 때 재생할 사운드")]
    public AudioClip HitSound;

    [Header("Ultimate Cooldown")]
    [Min(0f)] public float UltimateCooldown = 30f;
}
