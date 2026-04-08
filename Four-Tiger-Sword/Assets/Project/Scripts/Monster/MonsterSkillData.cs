using UnityEngine;

[CreateAssetMenu(fileName = "MonsterSkillData_", menuName = "SainGum/Monster/MonsterSkillData")]
public class MonsterSkillData : ScriptableObject
{
    [Header("스킬 기본")]
    public string skillCode; // 스킬 고유 코드 (예: "SK_SLASH_01")
    public int priority; // 스킬 우선순위 (높을수록 먼저 사용)
    public float coolTime; // 쿨타임 (초)
    public float range; // 스킬 사거리

    [Header("효과")]
    [Tooltip("경직/넉백/회복 등")]
    public string specialEffect; // 특수 효과 (경직, 넉백, 회복 등)
}
