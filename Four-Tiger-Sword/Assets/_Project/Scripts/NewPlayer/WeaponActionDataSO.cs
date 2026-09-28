using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Weapon Attack Data", menuName = "Combat/Weapon Attack Data")]
public class WeaponActionDataSO : ScriptableObject
{
    public string WeaponName;
    
    [Tooltip("1타, 2타, 3타... 에 대한 데이터 리스트")]
    public List<WeaponActionData> ComboSteps;

    [Header("Heavy Attack (optional)")]
    public WeaponActionData HeavyAttackStep;
    [Min(0.05f)] public float HeavyAttackHoldTime = 0.35f;
    [Min(0f), Tooltip("수 폼 강공격 1회당 기본 대시 거리(m). 후속 대시는 기억한 타겟을 통과할 만큼 연장될 수 있습니다.")]
    public float WaterHeavyDashDistance = 6f;
    [Min(0.01f), Tooltip("대시 이동 시간(초). 공격 전체 시간은 Heavy Attack Step의 Duration이며, 그 시간이 끝난 뒤 다음 강공격을 시작합니다.")]
    public float WaterHeavyDashDuration = 0.35f;
    [Min(0f), Tooltip("기억한 타겟을 향하는 후속 대시에서 타겟 뒤로 지나가는 거리(m)")]
    public float WaterHeavyTargetOvershoot = 1f;

    [Header("Water Flow Gauge")]
    [Min(1f)] public float WaterGaugePerSegment = 100f;
    [Min(0f), Tooltip("일반 공격의 실제 적중당 게이지 획득량")]
    public float WaterNormalHitGain = 12f;
    [Min(0f), Tooltip("스킬 사용 1회당 게이지 획득량. 일반 공격보다 크게 설정합니다.")]
    public float WaterSkillUseGain = 100f;

    [Header("Water Skill Buff")]
    [Min(0f), Tooltip("스킬 사용 시 부여되는 버프 지속 시간(초). 재사용하면 중첩 없이 갱신합니다.")]
    public float WaterSkillBuffDuration = 6f;
    [Min(0f), Tooltip("버프 중 플레이어 공격력 증가율(%). 20 = +20%. 다른 공격력 증가율과 합산합니다.")]
    public float WaterSkillAttackBonusPercent = 20f;
    [Min(0f), Tooltip("버프 중 수 폼 강공격 피해 증가율(%). 30 = 최종 공격 계수에 1.3배.")]
    public float WaterSkillHeavyDamageBonusPercent = 30f;

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

    [Header("Delayed Hit Effect")]
    [Tooltip("지연 데미지가 터질 때 적 위치에 스폰할 이펙트 프리팹")]
    public GameObject DelayedHitVFX;

    [Header("Fire Stack Explosion")]
    [Tooltip("화 폼 6스택 추가 피해까지의 실제 시간(초). 히트스탑과 무관하며 0이면 즉시 적용합니다.")]
    [Min(0f)] public float FireExplosionDelay = 0.15f;

    [Header("Spec tuning (unspecified values are provisional)")]
    [Min(0f)] public float UltimateCooldown = 30f;
    [Min(0.1f)] public float AreaRadius = 4f;
    [Min(0.1f)] public float FieldDuration = 5f;
    [Range(0f, 1f)] public float HealMaxHpPerSecond = 0.05f;
    [Min(0f)] public float ShieldAmount = 300f;
    [Min(0.1f)] public float ShieldDuration = 5f;
}
