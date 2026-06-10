using UnityEngine;

[CreateAssetMenu(fileName = "JumpSlam", menuName = "Monster/JumpSlam")]
public class MonsterJumpSlamSO : MonsterSkillData
{
    [Header("데미지")]
    public float damage = 30;
    public float knockbackForce = 18f;

    [Header("타이밍")]
    // telegraphDuration은 부모 MonsterSkillData에서 관리합니다.
    // 점프 내리찍기: 준비 자세(선딜) 중 번쩍임이므로 startupTime 이하로 설정하세요.
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
