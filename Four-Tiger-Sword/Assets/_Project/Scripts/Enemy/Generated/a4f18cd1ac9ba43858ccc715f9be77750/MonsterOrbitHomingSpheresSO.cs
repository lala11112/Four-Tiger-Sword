using UnityEngine;

namespace Baekho.Generated.a4f18cd1ac9ba43858ccc715f9be77750
{
    public sealed class MonsterOrbitHomingSpheresSO : MonsterSkillData
    {
        [InspectorName("구체 수"), Tooltip("등 뒤에서 함께 회전하고 동시에 발사되는 구체 수입니다."), Range(1, 16)]
        public int sphereCount = 5;

        [InspectorName("회전 준비 시간"), Tooltip("발사 전 회전 시간입니다. attackSpeed 배율이 적용됩니다."), Range(0f, 10f)]
        public float orbitDuration = 2f;

        [InspectorName("유도 시간"), Tooltip("발사 직후 플레이어를 추적하는 행동 시간입니다. attackSpeed와 무관합니다."), Range(0f, 10f)]
        public float homingDuration = 3f;

        [InspectorName("직진 유지 시간"), Tooltip("유도 종료 후 마지막 진행 방향으로 이동하다 소멸할 때까지의 행동 시간입니다."), Range(0.05f, 10f)]
        public float straightDuration = 3f;

        [InspectorName("회전 중심 위치"), Tooltip("적 루트 기준 회전 중심입니다. 음수 Z는 등 뒤이며, 원은 로컬 XY 평면에 배치됩니다.")]
        public Vector3 orbitCenterOffset = new Vector3(0f, 1.5f, -1f);

        [InspectorName("회전 반경"), Tooltip("회전 중심에서 각 구체 중심까지의 거리입니다."), Range(0.05f, 5f)]
        public float orbitRadius = 0.8f;

        [InspectorName("회전 각속도"), Tooltip("초당 회전 각도입니다. 음수는 반대 방향이며 attackSpeed 배율이 적용됩니다."), Range(-720f, 720f)]
        public float orbitAngularSpeed = 180f;

        [InspectorName("탄환 이동 속도"), Tooltip("발사 후 행동 시간 기준 이동 속도(m/s)입니다. attackSpeed와 무관합니다."), Range(0.1f, 30f)]
        public float projectileSpeed = 6f;

        [InspectorName("구체 반경"), Tooltip("구체 외형과 이동 구간 충돌 판정에 사용하는 반경입니다."), Range(0.02f, 1f)]
        public float projectileRadius = 0.18f;

        [InspectorName("유도 회전 속도"), Tooltip("유도 중 초당 최대 방향 전환 각도입니다. 유도 시간이 끝나면 방향을 더 이상 보정하지 않습니다."), Range(0f, 720f)]
        public float homingTurnSpeed = 180f;

        [InspectorName("조준 높이"), Tooltip("플레이어 루트 위치에 더할 월드 Y 방향 조준 높이입니다."), Range(0f, 5f)]
        public float targetHeight = 1.5f;

        [InspectorName("탄환 피해 배율"), Tooltip("구체 하나의 피해에 곱하는 ATK 배율입니다. 각 구체는 충돌 시 한 번만 피해를 시도합니다."), Min(0f)]
        public float damage = 0.3f;

        [InspectorName("넉백 힘"), Tooltip("탄환 진행 방향으로 적용할 넉백 힘입니다."), Range(0f, 100f)]
        public float knockbackForce = 0f;

        [InspectorName("장애물 레이어"), Tooltip("구체를 소멸시키는 비트리거 장애물 레이어입니다. 소유자는 제외하며 플레이어 판정은 별도로 포함합니다.")]
        public LayerMask obstacleMask = Physics.DefaultRaycastLayers;

        [InspectorName("구체 재질"), Tooltip("선택 사항입니다. 비워두면 Unity 기본 구체 재질을 사용합니다.")]
        public Material projectileMaterial;

        public override EnemyAction CreateAction(Enemy enemy)
        {
            return new EnemyOrbitHomingSpheres(this, enemy);
        }
    }
}
