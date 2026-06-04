using UnityEngine;

[CreateAssetMenu(fileName = "EnemyData_", menuName = "SainGum/Enemy/EnemyData")]
public class EnemyData : ScriptableObject
{
    public ElementType elementType; //속성 
    public int maxHp; //최대 체력
    public float baseAtk; //기본 공격력
    public float baseDef; //기본 방어력
    public float criticalChance; //크리티컬 확률
    public float criticalDamage; //크리티컬 데미지
    public float combatRange; //전투 사거리
    public float combatExitRange; //전투 종료 사거리
    public float knockbackResistance; //넉백 저항력
    public float knockbackDuration; //넉백 지속 시간
    public float attackCooldown; //공격 쿨타임
    public MonsterSkillDataSO monsterSkillData; //몬스터 스킬 데이터
    public StaggerResistLevel staggerResistLevel; //경직 저항 등급
    
}