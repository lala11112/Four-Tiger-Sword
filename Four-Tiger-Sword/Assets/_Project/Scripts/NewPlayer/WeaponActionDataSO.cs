using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Weapon Attack Data", menuName = "Combat/Weapon Attack Data")]
public class WeaponActionDataSO : ScriptableObject
{
    public string WeaponName;
    
    [Tooltip("1타, 2타, 3타... 에 대한 데이터 리스트")]
    public List<WeaponActionData> ComboSteps;
}