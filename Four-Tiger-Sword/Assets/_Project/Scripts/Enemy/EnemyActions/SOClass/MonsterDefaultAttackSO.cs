using UnityEngine;

[CreateAssetMenu(fileName = "DefaultAttack", menuName = "Monster/DefaultAttack")]
public class MonsterDefaultAttackSO : MonsterSkillData
{
    [Header("데미지")]
    public int damage = 10;
    [Range(0f, 1f)]
    public float criticalChance = 0.1f;
    public float criticalMultiplier = 1.5f;
    public float knockbackForce = 5f;

    [Header("타이밍")]
    [Tooltip("공격 예고(번쩍임) 지속 시간")]
    public float telegraphDuration = 0.5f;
    [Tooltip("히트 판정 시작 시간 (action.Enter 기준)")]
    public float hitStartTime = 0.1f;
    [Tooltip("히트 판정 지속 시간")]
    public float hitDuration = 0.2f;
    [Tooltip("공격 애니메이션 총 지속 시간")]
    public float attackDuration = 1.0f;

    [Header("히트박스")]
    public HitBoxShape hitBoxShape = HitBoxShape.Sphere;
    public Vector3 hitBoxOffset = new Vector3(0f, 0f, 1f);
    public float hitBoxRadius = 1.5f;
    public Vector3 hitBoxSize = new Vector3(1f, 1f, 1f);
    public float hitBoxHeight = 2f;

    public override EnemyAction CreateAction(Enemy enemy)
    {
        return new EnemyDefaultAttack(this, enemy);
    }
}
