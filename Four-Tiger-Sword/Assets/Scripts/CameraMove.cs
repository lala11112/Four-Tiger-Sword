using UnityEngine;

public class CameraMove : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Camera Settings")]
    [SerializeField] private float distance = 5f;
    [SerializeField] private float height = 2f;
    [Tooltip("마우스 감도. 새 입력 시스템 기준 (구 시스템 대비 0.1 배율 내장 적용)")]
    [SerializeField] private float mouseSensitivity = 3f;
    [SerializeField] private float minVerticalAngle = -30f;
    [SerializeField] private float maxVerticalAngle = 70f;

    [Header("Smoothing")]
    [SerializeField] private float followSmoothSpeed = 20f;

    [Header("Camera Collision")]
    [SerializeField] private float collisionOffset = 0.2f;
    [SerializeField] private LayerMask collisionLayerMask = ~0;

    [Header("FOV Settings")]
    [SerializeField] private float baseFOV = 60f;
    [SerializeField] private float fovSmoothSpeed = 10f;

    [SerializeField] private Transform playerCenterPosition;

    [Header("References")]
    [Tooltip("PlayerInput 컴포넌트. 비워두면 씬에서 자동 탐색")]
    [SerializeField] private PlayerInput playerInput;

    private Camera cam;
    private float yaw;
    private float pitch;
    public float targetFOVMultiplier = 1f;

    // Look 액션의 Mouse/delta(픽셀) → 각도 변환 배율
    private const float MouseDeltaScale = 0.1f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        cam = GetComponent<Camera>();
        if (cam != null)
            cam.fieldOfView = baseFOV;

        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;

        if (playerInput == null)
            playerInput = FindObjectOfType<PlayerInput>();
    }

    void LateUpdate()
    {
        if (target == null) return;

        ReadMouseInput();

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);

        Vector3 pivotPoint = target.position + Vector3.up * height;
        Vector3 defaultPosition = pivotPoint + rotation * Vector3.back * distance;

        // 벽 감지 시 충돌 위치로 즉시 스냅, 벽이 없으면 부드럽게 기본 위치로 추적
        Vector3 finalPosition = TryGetCollisionPosition(defaultPosition, out Vector3 collisionPos)
            ? Vector3.Lerp(transform.position, collisionPos, followSmoothSpeed * Time.deltaTime)

            : Vector3.Lerp(transform.position, defaultPosition, followSmoothSpeed * Time.deltaTime);

        transform.position = finalPosition;
        transform.rotation = Quaternion.Slerp(transform.rotation, rotation, followSmoothSpeed * Time.deltaTime);

        // z축 롤 고정: Slerp 보간 중 쿼터니언 수학으로 인해 z가 미세하게 틀어지는 것을 방지
        Vector3 euler = transform.eulerAngles;
        euler.z = 0f;
        transform.eulerAngles = euler;
        
        if (cam != null)
        {
            float targetFOV = baseFOV * targetFOVMultiplier;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, fovSmoothSpeed * Time.deltaTime);
        }
    }

    public void SetFOVMultiplier(float multiplier)
    {
        targetFOVMultiplier = multiplier;
    }

    // playerCenterPosition에서 카메라 목표 위치 방향으로 레이를 쏴
    // 사이에 물체가 있으면 충돌 지점의 플레이어 쪽 위치(collisionOffset 만큼 앞)를 반환
    private bool TryGetCollisionPosition(Vector3 desiredCameraPosition, out Vector3 adjustedPosition)
    {
        Vector3 origin = playerCenterPosition != null
            ? playerCenterPosition.position
            : target.position + Vector3.up * height;

        Vector3 toCamera = desiredCameraPosition - origin;
        float checkDistance = toCamera.magnitude;
        Vector3 direction = toCamera / checkDistance;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, checkDistance, collisionLayerMask))
        {
            // 충돌 지점에서 플레이어 방향으로 collisionOffset 만큼 앞에 카메라 배치
            adjustedPosition = hit.point - direction * collisionOffset;
            return true;
        }

        adjustedPosition = Vector3.zero;
        return false;
    }

    private void ReadMouseInput()
    {
        if (playerInput == null) return;

        Vector2 delta = playerInput.LookDelta * MouseDeltaScale;
        yaw   += delta.x * mouseSensitivity;
        pitch -= delta.y * mouseSensitivity;
        pitch  = Mathf.Clamp(pitch, minVerticalAngle, maxVerticalAngle);
    }
}