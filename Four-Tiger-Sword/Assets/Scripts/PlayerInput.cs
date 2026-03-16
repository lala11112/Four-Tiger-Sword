using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInput : MonoBehaviour
{
    // ──────────────────────────────────────────────
    // Serialized Fields
    // ──────────────────────────────────────────────

    [Header("Sprint / Dash Timing")]
    [Tooltip("Shift를 이 시간(초) 이내에 떼면 대시, 계속 누르면 달리기로 전환")]
    [SerializeField] private float runHoldThreshold = 0.15f;

    // ──────────────────────────────────────────────
    // Public Properties (PlayerMovement에서 읽어감)
    // ──────────────────────────────────────────────

    /// <summary>정규화된 이동 입력 방향 (XZ 평면)</summary>
    public Vector3 MoveDirection     { get; private set; }

    /// <summary>이번 프레임에 점프 버튼이 눌렸는지</summary>
    public bool    JumpPressed        { get; private set; }

    /// <summary>이번 프레임의 마우스/스틱 Look 입력 델타 (픽셀 단위)</summary>
    public Vector2 LookDelta          { get; private set; }

    /// <summary>Shift(스프린트)를 현재 누르고 있는지</summary>
    public bool    SprintHeld         { get; private set; }

    /// <summary>Shift를 누른 시각 (Time.time 기준)</summary>
    public float   SprintPressedTime  { get; private set; }

    /// <summary>대시 판정 임계 시간 (읽기 전용)</summary>
    public float   RunHoldThreshold   => runHoldThreshold;

    /// <summary>대시 트리거 플래그 — PlayerMovement가 소비 후 ConsumeDash() 호출 필요</summary>
    public bool    DashTriggered      { get; private set; }

    // ──────────────────────────────────────────────
    // Input Actions Asset (에디터에서 바인딩 관리)
    // ──────────────────────────────────────────────

    private PlayerInputActions actions;

    // ──────────────────────────────────────────────
    // Unity Lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        actions = new PlayerInputActions();
        actions.Player.Sprint.started  += OnSprintStarted;
        actions.Player.Sprint.canceled += OnSprintCanceled;
    }

    private void OnEnable()  => actions.Player.Enable();
    private void OnDisable() => actions.Player.Disable();

    private void OnDestroy()
    {
        actions.Player.Sprint.started  -= OnSprintStarted;
        actions.Player.Sprint.canceled -= OnSprintCanceled;
        actions.Dispose();
    }

    private void Update()
    {
        Vector2 raw   = actions.Player.Move.ReadValue<Vector2>();
        MoveDirection = new Vector3(raw.x, 0f, raw.y).normalized;
        JumpPressed   = actions.Player.Jump.WasPressedThisFrame();
        LookDelta     = actions.Player.Look.ReadValue<Vector2>();
    }

    // ──────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────

    /// <summary>PlayerMovement에서 대시를 처리한 뒤 반드시 호출해 플래그를 소비합니다.</summary>
    public void ConsumeDash() => DashTriggered = false;

    // ──────────────────────────────────────────────
    // Input Callbacks
    // ──────────────────────────────────────────────

    private void OnSprintStarted(InputAction.CallbackContext ctx)
    {
        SprintPressedTime = Time.time;
        SprintHeld        = true;
    }

    private void OnSprintCanceled(InputAction.CallbackContext ctx)
    {
        if (Time.time - SprintPressedTime < runHoldThreshold)
            DashTriggered = true;

        SprintHeld = false;
    }
}
