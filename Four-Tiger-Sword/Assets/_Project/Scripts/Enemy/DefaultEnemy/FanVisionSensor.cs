using UnityEngine;

/// <summary>
/// 전방 부채꼴 시야와 근거리 전방위 감지로 플레이어를 탐지합니다.
/// 두 감지 방식 모두 대상까지의 장애물을 확인합니다.
/// </summary>
[DisallowMultipleComponent]
public class FanVisionSensor : MonoBehaviour, IEnemySensor
{
    [SerializeField] private float _range = 20f;
    [SerializeField, Range(1f, 180f)] private float _halfAngle = 45f;
    [Tooltip("이 거리 안에서는 뒤에 있는 플레이어도 감지합니다. 벽은 통과하지 않으며, 전체 감지 거리 이내로 제한됩니다. 0이면 비활성화합니다.")]
    [SerializeField, Min(0f)] private float _proximityRange = 3f;
    [SerializeField, Min(1)] private int _rayCount = 7;
    [SerializeField] private float _heightOffset = 0.5f;
    [SerializeField] private LayerMask _visibilityMask = Physics.DefaultRaycastLayers;
    private Transform _player;
    private float _nextPlayerSearch;
    private readonly RaycastHit[] _visibilityHits = new RaycastHit[32];

    public Transform DetectedTarget { get; private set; }

    public bool DetectTarget()
    {
        DetectedTarget = null;
        if (!isActiveAndEnabled) return false;
        if (_player == null || !_player.gameObject.activeInHierarchy)
        {
            if (Time.time < _nextPlayerSearch) return false;
            _nextPlayerSearch = Time.time + 1f;
            _player = GameObject.FindGameObjectWithTag("Player")?.transform;
            if (_player == null) return false;
        }
        Vector3 offset = _player.position - transform.position;
        float range = Mathf.Max(0f, _range);
        if (offset.sqrMagnitude > range * range) return false;
        float proximityRange = Mathf.Clamp(_proximityRange, 0f, range);
        bool isNearby = proximityRange > 0f && offset.sqrMagnitude <= proximityRange * proximityRange;
        Vector3 flatDirection = Vector3.ProjectOnPlane(offset, Vector3.up);
        if (!isNearby && flatDirection.sqrMagnitude > 0.001f
            && Vector3.Angle(transform.forward, flatDirection) > _halfAngle) return false;

        Vector3 origin = transform.position + Vector3.up * _heightOffset;
        Vector3 ray = _player.position + Vector3.up * _heightOffset - origin;
        int count = Physics.RaycastNonAlloc(origin, ray.normalized, _visibilityHits,
            ray.magnitude, _visibilityMask, QueryTriggerInteraction.Ignore);
        // 버퍼가 가득 차면 누락된 장애물이 있을 수 있으므로 보이지 않는 것으로 처리합니다.
        if (count == _visibilityHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Transform hit = _visibilityHits[i].transform;
            if (hit.IsChildOf(transform) || hit.IsChildOf(_player)) continue;
            return false;
        }
        DetectedTarget = _player;
        return true;
    }

    private void OnDisable() => DetectedTarget = null;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, Mathf.Clamp(_proximityRange, 0f, Mathf.Max(0f, _range)));
        float totalAngle = _halfAngle * 2f;
        float step = _rayCount == 1 ? 0f : totalAngle / (_rayCount - 1);

        for (int i = 0; i < _rayCount; i++)
        {
            float angle = _rayCount == 1 ? 0f : -_halfAngle + step * i;
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * transform.forward;
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position + Vector3.up * _heightOffset, direction * _range);
        }

        // 부채꼴 양쪽 경계선
        Gizmos.color = Color.red;
        Vector3 leftBound  = Quaternion.Euler(0f, -_halfAngle, 0f) * transform.forward;
        Vector3 rightBound = Quaternion.Euler(0f,  _halfAngle, 0f) * transform.forward;
        Gizmos.DrawRay(transform.position + Vector3.up * _heightOffset, leftBound  * _range);
        Gizmos.DrawRay(transform.position + Vector3.up * _heightOffset, rightBound * _range);
    }
#endif
}
