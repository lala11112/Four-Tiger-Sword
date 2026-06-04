using UnityEngine;

/// <summary>
/// 항상 메인 카메라를 향하도록 오브젝트를 회전시킵니다.
/// 적 HP바 루트 오브젝트에 붙여서 사용합니다.
/// </summary>
public class Billboard : MonoBehaviour
{
    private Transform _cam;

    private void Start()
    {
        if (Camera.main != null)
            _cam = Camera.main.transform;
    }

    private void LateUpdate()
    {
        if (_cam == null) return;

        transform.LookAt(
            transform.position + _cam.rotation * Vector3.forward,
            _cam.rotation * Vector3.up
        );
    }
}
