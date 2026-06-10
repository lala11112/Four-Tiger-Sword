using UnityEngine;

[CreateAssetMenu(fileName = "DefaultAttack", menuName = "Monster/DefaultAttack")]
public class MonsterDefaultAttackSO : MonsterSkillData
{
    [Header("데미지")]
    public float damage = 10;
    public float knockbackForce = 5f;

    [Header("타이밍")]
    // telegraphDuration은 부모 MonsterSkillData에서 관리합니다.
    // 단일 타격: 타격 직전까지 예고를 유지하려면 hitStartTime과 같거나 짧게 설정하세요.
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
