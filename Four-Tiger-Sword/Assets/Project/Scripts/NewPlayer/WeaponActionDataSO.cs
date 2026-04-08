using System;
using System.Collections.Generic;
using UnityEngine;

// 1. 공격 1타에 필요한 세부 데이터


// 2. 무기 전체의 콤보를 관리하는 스크립터블 오브젝트
[CreateAssetMenu(fileName = "New Weapon Attack Data", menuName = "Combat/Weapon Attack Data")]
public class WeaponActionDataSO : ScriptableObject
{
    public string WeaponName;
    
    [Tooltip("1타, 2타, 3타... 에 대한 데이터 리스트")]
    public List<WeaponActionData> ComboSteps;
}