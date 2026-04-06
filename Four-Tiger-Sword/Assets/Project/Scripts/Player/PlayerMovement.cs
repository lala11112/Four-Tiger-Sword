using UnityEngine;
/*

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
public class PlayerMovement : MonoBehaviour
{
    // ──────────────────────────────────────────────
    // Serialized Fields
    // ──────────────────────────────────────────────

    [Header("Movement Settings")]
    [Tooltip("지상 이동 최고 속도 (걷기)")]
    [SerializeField] private float moveSpeed = 5f;
    [Tooltip("Shift를 길게 눌렀을 때의 달리기 속도")]
    [SerializeField] private float runSpeed = 9f;
    [Tooltip("이동 방향으로 캐릭터가 회전하는 속도. 값이 클수록 즉각적으로 회전")]
    [SerializeField] private float turnSpeed = 10f;
    [Tooltip("입력이 없을 때 수평 속도가 0으로 줄어드는 속도")]
    [SerializeField] private float decelerationRate = 3f;
    [Tooltip("공중에 있을 때 방향 전환 제어력 비율 (0 = 제어 불가, 1 = 지상과 동일)")]
    [SerializeField] private float airControlMultiplier = 0.5f;

    [Header("Jump & Gravity Settings")]
    [Tooltip("점프 시 적용되는 초기 상승 속도")]
    [SerializeField] private float jumpForce = 8f;
    [Tooltip("하강 중 중력에 곱해지는 배율. 값이 클수록 빠르게 떨어짐")]
    [SerializeField] private float fallMultiplier = 3f;
    [Tooltip("상승 중 중력에 곱해지는 배율. 값이 클수록 점프 정점에 빨리 도달")]
    [SerializeField] private float jumpRiseMultiplier = 2f;
    [Tooltip("경사면에서 캐릭터가 뜨지 않도록 지면 방향으로 누르는 힘 (음수 권장)")]
    [SerializeField] private float groundStickForce = -2f;

    [Header("Dash Settings")]
    [Tooltip("대시 중 이동 속도")]
    [SerializeField] private float dashSpeed = 20f;
    [Tooltip("대시가 지속되는 시간 (초)")]
    [SerializeField] private float dashDuration = 0.2f;
    [Tooltip("대시 후 재사용 대기 시간 (초)")]
    [SerializeField] private float dashCooldown = 1f;

    [Header("Ground Detection")]
    [Tooltip("지면으로 인식할 레이어. 반드시 지면 오브젝트의 레이어와 일치시켜야 함")]
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("지면 감지 Raycast의 최대 거리. 캐릭터 발 아래 여유를 포함해 설정")]
    [SerializeField] private float groundRayLength = 1.1f;

    [Header("Camera FOV")]
    [Tooltip("대시 중 카메라 FOV 배율 (기본값 대비 비율)")]
    [SerializeField] private float fovDash = 1.2f;
    [Tooltip("달리기 중 카메라 FOV 배율 (기본값 대비 비율)")]
    [SerializeField] private float fovRun = 1.1f;
    [Tooltip("기본 상태의 카메라 FOV 배율 (1 = 변화 없음)")]
    [SerializeField] private float fovDefault = 1f;

    [Header("References")]
    [Tooltip("이동 방향 계산에 사용할 카메라 Transform. 비워두면 Camera.main으로 자동 설정")]
    [SerializeField] private Transform cameraTransform;
    [Tooltip("FOV 조작을 위한 CameraMove 컴포넌트. 비워두면 Camera.main에서 자동 탐색")]
    [SerializeField] private CameraMove cameraMove;

    // ──────────────────────────────────────────────
    // Private State
    // ──────────────────────────────────────────────

    private CharacterController controller;
    private PlayerInput playerInput;

    private Vector3 horizontalVelocity = Vector3.zero; //좌우양옆 움직임
    private float verticalVelocity = 0f; //상하 움직임

    private Vector3 inputDirection; // 정규화된 입력 방향 (XZ 평면)
    private float currentFriction = 1f; //마찰력
    private bool isGrounded; //지면 충돌 여부

    // 달리기 / 대시 상태
    private bool isRunning;
    private bool isDashing;
    private Vector3 dashDirection;
    private float dashTimer;
    private float dashCooldownTimer;

    // Ground 캐싱 (매 프레임 GetComponent 방지)
    private Collider lastGroundCollider;
    private float cachedFriction = 1f;

    // FOV 변경 감지 (불필요한 호출 방지)
    private float lastFOV = -1f;

    // Raycast 결과 재사용
    private RaycastHit groundHit;

    private const float AccelerationFactor = 20f;

    // ──────────────────────────────────────────────
    // Unity Lifecycle
    // ──────────────────────────────────────────────

    private void Start()
    {
        controller  = GetComponent<CharacterController>();
        playerInput = GetComponent<PlayerInput>();

        if (cameraTransform == null)
            cameraTransform = Camera.main.transform;

        if (cameraMove == null && Camera.main != null)
            cameraMove = Camera.main.GetComponent<CameraMove>();
    }

    private void Update()
    {
        inputDirection = playerInput.MoveDirection;

        if (playerInput.JumpPressed && isGrounded)
            verticalVelocity = jumpForce;

        if (playerInput.DashTriggered)
        {
            TryDash();
            playerInput.ConsumeDash();
        }

        CheckGround();
        UpdateDashAndRunState();
        UpdateCameraFOV();
        Move();
    }

    // ──────────────────────────────────────────────
    // State Updates
    // ──────────────────────────────────────────────

    private void UpdateDashAndRunState()
    {
        if (isDashing)
        {
            dashTimer -= Time.deltaTime;
            if (dashTimer <= 0f)
            {
                isDashing = false;
                horizontalVelocity = dashDirection * moveSpeed;
            }
        }

        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;

        isRunning = playerInput.SprintHeld && !isDashing &&
                    Time.time - playerInput.SprintPressedTime >= playerInput.RunHoldThreshold;
    }

    // FOV 값이 바뀔 때만 카메라에 전달
    private void UpdateCameraFOV()
    {
        if (cameraMove == null) return;

        float targetFOV = isDashing ? fovDash
            : (isRunning && inputDirection.sqrMagnitude > 0f) ? fovRun
            : fovDefault;

        if (Mathf.Approximately(targetFOV, lastFOV)) return;
        lastFOV = targetFOV;
        cameraMove.SetFOVMultiplier(targetFOV);
    }

    // ──────────────────────────────────────────────
    // Movement
    // ──────────────────────────────────────────────

    private void Move()
    {
        ApplyGravity();

        Vector3 motion = isDashing ? dashDirection * dashSpeed : BuildWalkRunMotion();
        motion.y = verticalVelocity;

        CollisionFlags flags = controller.Move(motion * Time.deltaTime);

        // 천장에 부딪히면 상승 속도를 즉시 제거
        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            verticalVelocity = 0f;
    }

    private Vector3 BuildWalkRunMotion()
    {
        float speed = isRunning ? runSpeed : moveSpeed;

        if (inputDirection.sqrMagnitude > 0f)
        {
            Vector3 moveDir = GetCameraRelativeMoveDirection();
            RotateTowards(moveDir);

            Vector3 targetVelocity = moveDir * speed;
            horizontalVelocity = Vector3.MoveTowards(
                horizontalVelocity,
                targetVelocity,
                speed * currentFriction * Time.deltaTime * AccelerationFactor
            );
        }
        else
        {
            horizontalVelocity = Vector3.Lerp(horizontalVelocity, Vector3.zero, decelerationRate * Time.deltaTime);
        }

        return isGrounded ? horizontalVelocity : horizontalVelocity * airControlMultiplier;
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private Vector3 GetCameraRelativeMoveDirection()
    {
        Vector3 camForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 camRight   = Vector3.ProjectOnPlane(cameraTransform.right,   Vector3.up).normalized;
        return (camForward * inputDirection.z + camRight * inputDirection.x).normalized;
    }

    private void RotateTowards(Vector3 direction)
    {
        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * turnSpeed);
    }

    private void ApplyGravity()
    {
        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = groundStickForce;
            return;
        }

        float multiplier = verticalVelocity < 0f ? fallMultiplier : jumpRiseMultiplier;
        verticalVelocity += Physics.gravity.y * multiplier * Time.deltaTime;
    }

    // Ground 컴포넌트 캐싱으로 매 프레임 GetComponent 방지
    private void CheckGround()
    {
        if (Physics.Raycast(transform.position, Vector3.down, out groundHit, groundRayLength, groundLayer))
        {
            isGrounded = true;

            if (groundHit.collider != lastGroundCollider)
            {
                lastGroundCollider = groundHit.collider;
                Ground ground = groundHit.collider.GetComponent<Ground>();
                cachedFriction = ground != null ? ground.friction : 1f;
            }
            currentFriction = cachedFriction;
        }
        else
        {
            isGrounded = false;
            lastGroundCollider = null;
        }
    }

    private void TryDash()
    {
        if (isDashing || dashCooldownTimer > 0f) return;

        dashDirection = inputDirection.sqrMagnitude > 0f
            ? GetCameraRelativeMoveDirection()
            : transform.forward;

        isDashing = true;
        dashTimer = dashDuration;
        dashCooldownTimer = dashCooldown;
    }
}
*/