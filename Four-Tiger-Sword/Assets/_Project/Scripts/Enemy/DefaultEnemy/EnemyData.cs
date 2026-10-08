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
    [Header("넉백 설정")]
    [Min(0f), Tooltip("넉백으로 실제 이동하는 시간 (초). 0이면 넉백 이동 없음. 피격 경직 시간은 Enemy의 Hurt Duration에서 설정합니다.")]
    public float knockbackMoveTime = 0.16f;
    [Min(0f), Tooltip("한 번 피격으로 밀려날 수 있는 최대 거리 (m). 0이면 넉백 이동 없음.")]
    public float knockbackMaxDistance = 1.2f;

    [Header("공격 설정")]
    public float attackCooldown; //공격 쿨타임
    public MonsterSkillDataSO monsterSkillData; //몬스터 스킬 데이터
    public StaggerResistLevel staggerResistLevel; //경직 저항 등급
    
}
