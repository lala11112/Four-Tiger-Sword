using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Weapon Attack Data", menuName = "Combat/Weapon Attack Data")]
public class WeaponActionDataSO : ScriptableObject
{
    public string WeaponName;
    
    [Tooltip("1타, 2타, 3타... 에 대한 데이터 리스트")]
    public List<WeaponActionData> ComboSteps;

    [Header("Air Attack")]
    [Tooltip("공중 내려찍기 공격 데이터")]
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
