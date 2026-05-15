using UnityEngine;

/// <summary>
/// Enemy가 바라보는 방향을 기준으로 부채꼴 범위 안의 플레이어를 탐지합니다.
/// Inspector에서 탐지 거리·각도·레이 수를 조정할 수 있습니다.
/// </summary>
[DisallowMultipleComponent]
public class FanVisionSensor : MonoBehaviour, IEnemySensor
{
    [SerializeField] private float _range = 20f;
    [SerializeField, Range(1f, 180f)] private float _halfAngle = 45f;
    [SerializeField, Min(1)] private int _rayCount = 7;
    [SerializeField] private float _heightOffset = 0.5f;

    public Transform DetectedTarget { get; private set; }

    public bool DetectTarget()
    {
        float totalAngle = _halfAngle * 2f;
        float step = _rayCount == 1 ? 0f : totalAngle / (_rayCount - 1);

        for (int i = 0; i < _rayCount; i++)
        {
            float angle = -_halfAngle + step * i;
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * transform.forward;

            if (Physics.Raycast(transform.position + Vector3.up * _heightOffset, direction, out RaycastHit hit, _range))
            {
                if (hit.collider.CompareTag("Player"))
                {
                    DetectedTarget = hit.collider.transform;
                    return true;
                }
            }
        }

        //DetectedTarget = null;
        return false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        float totalAngle = _halfAngle * 2f;
        float step = _rayCount == 1 ? 0f : totalAngle / (_rayCount - 1);

        for (int i = 0; i < _rayCount; i++)
        {
            float angle = -_halfAngle + step * i;
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
