using UnityEngine;

namespace Baekho.Generated.a3a3ce55a4e264cb9b2b31dc53f788381
{
    [CreateAssetMenu(fileName = "SequentialOrbitSpheres", menuName = "Monster/등 뒤 순차 유도 구체")]
    public sealed class MonsterSequentialOrbitSpheresSO : MonsterSkillData
    {
        [InspectorName("구체 개수"), Tooltip("처음에 생성할 구체 수입니다. 원형으로 배치한 뒤 순서대로 발사합니다."), Range(1, 16)]
        public int sphereCount = 8;

        [InspectorName("발사 전 회전 시간"), Tooltip("첫 구체를 발사하기 전 회전 시간입니다. attackSpeed 배율이 적용됩니다."), Range(0f, 10f)]
        public float orbitDuration = 2f;

        [InspectorName("구체 발사 간격"), Tooltip("연속한 두 구체의 발사 시작 간격입니다. attackSpeed 배율이 적용됩니다."), Range(0.01f, 2f)]
        public float shotInterval = 0.2f;

        [InspectorName("구체별 유도 시간"), Tooltip("각 구체가 발사된 뒤 플레이어를 추적하는 행동 시간입니다. attackSpeed와 무관합니다."), Range(0f, 10f)]
        public float homingDuration = 3f;

        [InspectorName("유도 후 직진 시간"), Tooltip("유도가 끝난 뒤 마지막 방향으로 직진하는 행동 시간입니다. 이 시간이 끝나면 소멸합니다."), Range(0.05f, 10f)]
        public float straightDuration = 3f;

        [InspectorName("회전 중심 위치"), Tooltip("적 루트의 회전 기준 오프셋입니다. 음수 Z가 등 뒤이며 원은 로컬 XY 평면에 놓입니다.")]
        public Vector3 orbitCenterOffset = new Vector3(0f, 1.5f, -1f);

        [InspectorName("회전 반경"), Tooltip("회전 중심에서 구체 중심까지의 거리입니다."), Range(0.05f, 5f)]
        public float orbitRadius = 0.8f;

        [InspectorName("회전 각속도"), Tooltip("초당 회전 각도입니다. 음수는 반대 방향이며 attackSpeed 배율이 적용됩니다."), Range(-720f, 720f)]
        public float orbitAngularSpeed = 180f;

        [InspectorName("구체 비행 속도"), Tooltip("발사 후 행동 시간 기준 이동 속도(m/s)입니다. attackSpeed와 무관합니다."), Range(0.1f, 30f)]
        public float projectileSpeed = 6f;

        [InspectorName("구체 충돌 반경"), Tooltip("이동 구간 구형 충돌 판정의 반경입니다. 기본 구체 외형에도 적용되지만 선택한 프리팹의 크기는 바꾸지 않습니다."), Range(0.02f, 1f)]
        public float projectileRadius = 0.18f;

        [InspectorName("유도 방향 전환 속도"), Tooltip("유도 중 초당 최대 회전 각도입니다. 유도 시간이 끝나면 방향이 고정됩니다."), Range(0f, 720f)]
        public float homingTurnSpeed = 180f;

        [InspectorName("플레이어 조준 높이"), Tooltip("플레이어 루트 위치에 더할 월드 Y 방향 높이입니다."), Range(0f, 5f)]
        public float targetHeight = 1.5f;

        [InspectorName("구체 피해 배율"), Tooltip("구체 하나의 피해에 사용하는 ATK 배율입니다. 같은 대상의 여러 자식 콜라이더에 중복 적용하지 않습니다."), Range(0f, 1000f)]
        public float damage = 0.3f;

        [InspectorName("넉백 힘"), Tooltip("명중한 구체의 진행 방향으로 적용할 넉백 힘입니다."), Range(0f, 100f)]
        public float knockbackForce = 0f;

        [InspectorName("장애물 레이어"), Tooltip("구체를 소멸시키는 비트리거 장애물 레이어입니다. 소유자는 제외하고 플레이어 판정은 별도로 처리합니다.")]
        public LayerMask obstacleMask = Physics.DefaultRaycastLayers;

        [InspectorName("구체 프리팹"), Tooltip("선택 가능한 외형 전용 프리팹입니다. 비워두면 기본 구체를 사용합니다. 복제본의 MonoBehaviour와 Collider는 비활성화하고 Rigidbody는 고정합니다. 충분히 오래 표시되는 구체 또는 반복 재생 외형을 사용하세요.")]
        public GameObject projectilePrefab;

        [InspectorName("프리팹 크기 배율"), Tooltip("선택한 프리팹의 원래 크기에 곱합니다. 충돌 반경 및 기본 구체 외형에는 영향을 주지 않습니다."), Range(0.01f, 10f)]
        public float projectilePrefabScale = 1f;

        public MonsterSequentialOrbitSpheresSO()
        {
            animName = "Attack";
            engageRange = 10f;
            executeRange = 8f;
            preferredRange = 6f;
            weight = 100f;
            attackSpeed = 1f;
            telegraphDuration = 2f;
        }

        public override EnemyAction CreateAction(Enemy enemy)
        {
            return enemy != null ? new EnemySequentialOrbitSpheres(this, enemy) : null;
        }
    }
}
