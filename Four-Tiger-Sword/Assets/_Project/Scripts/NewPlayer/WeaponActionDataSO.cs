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
}