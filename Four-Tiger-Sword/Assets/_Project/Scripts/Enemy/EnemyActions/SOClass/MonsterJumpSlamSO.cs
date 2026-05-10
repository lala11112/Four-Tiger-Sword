using UnityEngine;

[CreateAssetMenu(fileName = "JumpSlam", menuName = "Monster/JumpSlam")]
public class MonsterJumpSlamSO : MonsterSkillData
{
    [Header("데미지")]
    public int damage = 30;
    [Range(0f, 1f)]
    public float criticalChance = 0.1f;
    public float criticalMultiplier = 1.5f;
    public float knockbackForce = 18f;

    [Header("타이밍")]
    [Tooltip("공격 예고(번쩍임) 지속 시간")]
    public float telegraphDuration = 0.8f;
    [Tooltip("도약 전 선딜 (준비 자세)")]
    public float startupTime = 0.3f;
    [Tooltip("공중에서 목표 지점까지 이동하는 시간")]
    public float jumpDuration = 0.65f;
    [Tooltip("착지 후 경직(후딜) 시간")]
    public float recoveryTime = 0.7f;

    [Header("점프")]
    [Tooltip("포물선 정점 높이")]
    public float jumpHeight = 5f;

    [Header("히트박스 (착지 충격)")]
    [Tooltip("착지 지점 기준 히트박스 오프셋")]
    public Vector3 hitBoxOffset = Vector3.zero;
    [Tooltip("착지 충격 반경")]
    public float slamRadius = 2.5f;

    public override EnemyAction CreateAction(Enemy enemy)
        => new EnemyJumpSlam(this, enemy);
}
