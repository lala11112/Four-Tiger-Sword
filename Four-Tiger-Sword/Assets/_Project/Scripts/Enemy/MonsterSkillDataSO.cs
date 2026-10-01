using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MonsterSkillData", menuName = "MonsterSkillDataSO")]
public class MonsterSkillDataSO : ScriptableObject
{
    public string monsterName;
    public List<MonsterSkillData> skillData;
    [Tooltip("HP 조건으로 자동 발동하는 자폭. 일반 공격 목록에는 넣지 않습니다.")]
    public MonsterSelfDestructSO selfDestruct;
}
