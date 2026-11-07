using UnityEngine;

namespace Baekho.Generated.a08babf2b48684e72bc1f482c3b6e8f9c
{
    [CreateAssetMenu(fileName = "DashDoubleReturn", menuName = "Monster/Generated/DashDoubleReturn")]
    public sealed class MonsterDashDoubleReturnSO : MonsterSkillData
    {
        [InspectorName("준비 시간"), Tooltip("돌진 전 준비 시간입니다. 공격 속도 배율이 적용됩니다."), Min(0f)]
        public float startupTime = 0.15f;

        [InspectorName("돌진 속도"), Tooltip("기본 돌진 속도(m/s)입니다. 공격 속도 배율이 적용됩니다."), Min(1f)]
        public float dashSpeed = 18f;

        [InspectorName("복귀 속도"), Tooltip("출발 위치로 후퇴하는 기본 속도(m/s)입니다. 공격 속도 배율이 적용됩니다."), Min(1f)]
        public float returnSpeed = 20f;

        [InspectorName("최대 돌진 거리"), Tooltip("한 번의 전진에서 이동할 수 있는 최대 거리입니다."), Min(0f)]
        public float maxDashDistance = 6f;

        [InspectorName("목표와의 정지 거리"), Tooltip("돌진 시작 시점의 플레이어 위치 앞에 남길 수평 거리입니다."), Min(0f)]
        public float stopDistance = 1.2f;

        [InspectorName("도착 후 첫 타격 대기"), Tooltip("돌진 종료부터 첫 타격 판정까지의 시간입니다."), Min(0f)]
        public float firstHitDelay = 0.05f;

        [InspectorName("타격 시작 간격"), Tooltip("첫 타격과 둘째 타격의 시작 간격입니다. 판정 지속 시간보다 최소 0.01초 길게 제한됩니다."), Min(0.01f)]
        public float hitInterval = 0.2f;

        [InspectorName("각 타격 판정 시간"), Tooltip("각 타격 판정의 지속 시간입니다. 타격마다 동일 대상에 한 번만 적용합니다."), Min(0.01f)]
        public float hitDuration = 0.08f;

        [InspectorName("마지막 타격 후 복귀 대기"), Tooltip("둘째 판정이 끝난 뒤 후퇴하기까지의 시간입니다."), Min(0f)]
        public float returnDelay = 0.05f;

        [InspectorName("복귀 후 회복 시간"), Tooltip("복귀 이동 종료 후 공격을 마칠 때까지의 시간입니다."), Min(0f)]
        public float recoveryTime = 0.15f;

        [InspectorName("첫 타격 피해 배율"), Tooltip("첫 타격 피해에 사용하는 ATK 배율입니다."), Min(0f)]
        public float firstDamage = 0.5f;

        [InspectorName("둘째 타격 피해 배율"), Tooltip("둘째 타격 피해에 사용하는 ATK 배율입니다."), Min(0f)]
        public float secondDamage = 0.5f;

        [InspectorName("넉백 힘"), Tooltip("각 타격의 전방 넉백 힘입니다. 기본값 0은 첫 타격으로 대상을 밀어내지 않습니다."), Min(0f)]
        public float knockbackForce = 0f;

        [InspectorName("타격 중심 오프셋"), Tooltip("적 루트의 회전 기준 타격 중심입니다. 두 타격에 공통으로 사용합니다.")]
        public Vector3 hitBoxOffset = new Vector3(0f, 1f, 0.9f);

        [InspectorName("타격 반경"), Tooltip("구형 근접 타격 판정의 반경입니다."), Min(0.05f)]
        public float hitBoxRadius = 0.85f;

        [InspectorName("장애물 레이어"), Tooltip("돌진·복귀의 몸체 검사와 근접 타격의 벽 차단 검사에 사용합니다. 소유자와 트리거는 제외합니다.")]
        public LayerMask obstacleMask = Physics.DefaultRaycastLayers;

        [InspectorName("돌진 애니메이션 상태"), Tooltip("돌진 시작 시 사용할 기존 Animator 상태입니다. 비우거나 상태가 없으면 전환하지 않습니다.")]
        public string dashAnimName = "Move";

        [InspectorName("후퇴 애니메이션 상태"), Tooltip("후퇴 시작 시 사용할 기존 Animator 상태입니다. 비우거나 상태가 없으면 전환하지 않습니다.")]
        public string returnAnimName = "Move";

        public override EnemyAction CreateAction(Enemy enemy)
        {
            return enemy == null ? null : new EnemyDashDoubleReturn(this, enemy);
        }
    }
}
