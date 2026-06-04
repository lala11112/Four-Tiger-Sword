using UnityEngine;

[CreateAssetMenu(fileName = "DashAttack", menuName = "Monster/DashAttack")]
public class MonsterDashAttackSO : MonsterSkillData
{
    [Header("데미지")]
    public float damage = 20;
    public float knockbackForce = 12f;

    [Header("타이밍")]
    [Tooltip("공격 예고(번쩍임) 지속 시간 — EnemyAttackState가 사용")]
    public float telegraphDuration = 0.6f;
    [Tooltip("대쉬 시작 전 선딜 (준비 자세)")]
    public float startupTime = 0.2f;
    [Tooltip("대쉬 지속 시간")]
    public float dashDuration = 0.4f;
    [Tooltip("대쉬 후 경직(후딜) 시간")]
    public float recoveryTime = 0.5f;

    [Header("대쉬 이동")]
    [Tooltip("대쉬 속도 (유닛/초)")]
    public float dashSpeed = 15f;

    [Header("히트박스 (대쉬 중 판정)")]
    public HitBoxShape hitBoxShape = HitBoxShape.Sphere;
    public Vector3 hitBoxOffset = new Vector3(0f, 0f, 0.5f);
    public float hitBoxRadius = 1.2f;

    public override EnemyAction CreateAction(Enemy enemy)
    {
        return new EnemyDashAttack(this, enemy);
    }
}
