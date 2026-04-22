using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MonsterSkillData", menuName = "MonsterSkillDataSO")]
public class MonsterSkillDataSO : ScriptableObject
{
    public string monsterName;
    public List<MonsterSkillData> skillData;
}