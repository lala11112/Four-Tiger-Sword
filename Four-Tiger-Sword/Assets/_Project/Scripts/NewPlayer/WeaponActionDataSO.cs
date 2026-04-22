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

    [Header("Skill (E)")]
    [Tooltip("스킬 공격 데이터 (여러 단계 가능)")]
    public List<WeaponActionData> SkillSteps;

    [Header("Ultimate (R)")]
    [Tooltip("궁극기 공격 데이터 (여러 단계 가능)")]
    public List<WeaponActionData> UltimateSteps;
}