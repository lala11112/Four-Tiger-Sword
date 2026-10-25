using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)]
public class PlayerInputHandler : MonoBehaviour
{
    [Header("Input Action References")]
    [SerializeField] private InputActionReference _moveAction;
    [SerializeField] private InputActionReference _jumpAction;
    [SerializeField] private InputActionReference _dashAction;
    [SerializeField] private InputActionReference _attackAction;
    [SerializeField] private InputActionReference _form1Action;
    [SerializeField] private InputActionReference _form2Action;
    [SerializeField] private InputActionReference _form3Action;
    [SerializeField] private InputActionReference _form4Action;
    [SerializeField] private InputActionReference _form5Action;
    [SerializeField] private InputActionReference _fastFormAction;
    [SerializeField] private InputActionReference _skillAction;
    [SerializeField] private InputActionReference _ultimateAction;
    [SerializeField] private InputActionReference _finisherAction;

    public Vector2 MoveInput { get; private set; }
    public bool IsAttackHeld { get; private set; }
    public float AttackHeldTime { get; private set; }
    public uint AttackPressId { get; private set; }
    public bool IsForm1Pressed { get; private set; }
    public bool IsForm2Pressed { get; private set; }
    public bool IsForm3Pressed { get; private set; }
    public bool IsForm4Pressed { get; private set; }
    public bool IsForm5Pressed { get; private set; }
    public bool IsFastFormPressed { get; private set; }
    public InputBuffer JumpBuffer;
    public InputBuffer AttackBuffer;
    public InputBuffer DashBuffer;
    public InputBuffer SkillBuffer;
    public InputBuffer UltimateBuffer;
    private PlayerController _player;
    private bool _wasActionStopped;
    private bool _shopInputBlocked;
    private bool _waitForGameplayRelease;
    private bool _finisherInputBlocked;
    private bool _finisherNeedsRelease;
    private InputAction _ownedFinisherAction;
    private InputAction FinisherAction => _finisherAction != null ? _finisherAction.action
        : _attackAction?.action?.actionMap?.FindAction("FinisherSelect", false) ?? _ownedFinisherAction;
    public bool FinisherPressedThisFrame { get; private set; }
    public bool IsFinisherHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;
    public bool IsFinisherInputAvailable => isActiveAndEnabled && FinisherAction != null
        && FinisherAction.enabled && Mouse.current != null;
    public Vector2 FinisherPointerDelta => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
    private void Awake() => _player = GetComponent<PlayerController>();

    private void OnEnable()
    {
        if (FinisherAction == null)
            _ownedFinisherAction = new InputAction("FinisherSelect", InputActionType.Button, "<Mouse>/rightButton");
        FinisherAction?.Enable();
        _finisherNeedsRelease = IsFinisherHeld;
        _moveAction?.action?.Enable();
        _jumpAction?.action?.Enable();
        _dashAction?.action?.Enable();
        _attackAction?.action?.Enable();
        _form1Action?.action?.Enable();
        _form2Action?.action?.Enable();
        _form3Action?.action?.Enable();
        _form4Action?.action?.Enable();
        _form5Action?.action?.Enable();
        _fastFormAction?.action?.Enable();
        _skillAction?.action?.Enable();
        _ultimateAction?.action?.Enable();
    }

    private void OnDisable()
    {
        _player?.CancelFinisherSelection();
        FinisherPressedThisFrame = false;
        FinisherAction?.Disable();
        ClearGameplayInput();
        _moveAction?.action?.Disable();
        _jumpAction?.action?.Disable();
        _dashAction?.action?.Disable();
        _attackAction?.action?.Disable();
        _form1Action?.action?.Disable();
        _form2Action?.action?.Disable();
        _form3Action?.action?.Disable();
        _form4Action?.action?.Disable();
        _form5Action?.action?.Disable();
        _fastFormAction?.action?.Disable();
        _skillAction?.action?.Disable();
        _ultimateAction?.action?.Disable();
    }

    private void OnDestroy() => _ownedFinisherAction?.Dispose();

    internal void SetFinisherInputBlocked(bool blocked)
    {
        _finisherInputBlocked = blocked;
        FinisherPressedThisFrame = false;
        _finisherNeedsRelease = true;
        if (!blocked) _waitForGameplayRelease = true;
        ClearGameplayInput();
    }

    internal void ConsumeFinisherPress() => FinisherPressedThisFrame = false;

    // An action ending this frame must not replay form keys pressed during that action.
    public void ConsumeFormInput()
    {
        IsForm1Pressed = IsForm2Pressed = IsForm3Pressed = IsForm4Pressed = IsForm5Pressed = IsFastFormPressed = false;
    }

    /// <summary>Blocks gameplay without disabling shared actions used by other input consumers.</summary>
    internal void SetShopInputBlocked(bool blocked)
    {
        if (_shopInputBlocked == blocked) return;
        _shopInputBlocked = blocked;
        _waitForGameplayRelease = true;
        ClearGameplayInput();
    }

    private void ClearGameplayInput()
    {
        MoveInput = Vector2.zero;
        IsAttackHeld = false;
        AttackHeldTime = 0f;
        _wasActionStopped = false;
        ConsumeFormInput();
        JumpBuffer.Consume();
        AttackBuffer.Consume();
        DashBuffer.Consume();
        SkillBuffer.Consume();
        UltimateBuffer.Consume();
    }

    private bool AnyGameplayButtonHeld()
        => Held(_jumpAction) || Held(_dashAction) || Held(_attackAction)
            || Held(_form1Action) || Held(_form2Action) || Held(_form3Action)
            || Held(_form4Action) || Held(_form5Action) || Held(_fastFormAction)
            || Held(_skillAction) || Held(_ultimateAction) || IsFinisherHeld;

    private static bool Held(InputActionReference reference) => reference?.action?.IsPressed() ?? false;

    private void Update()
    {
        FinisherPressedThisFrame = false;
        bool waitForFinisherRelease = _finisherNeedsRelease;
        if (!IsFinisherHeld) _finisherNeedsRelease = false;
        if (_shopInputBlocked || _finisherInputBlocked || _waitForGameplayRelease)
        {
            ClearGameplayInput();
            // Ignore the entire first neutral frame so its WasPressedThisFrame flags cannot leak.
            if (!_shopInputBlocked && !_finisherInputBlocked && !AnyGameplayButtonHeld()) _waitForGameplayRelease = false;
            return;
        }
        FinisherPressedThisFrame = !waitForFinisherRelease && IsFinisherInputAvailable
            && FinisherAction.WasPressedThisFrame();
        float actionDelta = _player != null ? _player.ActionDeltaTime : Time.deltaTime;
        bool retainFormInput = actionDelta <= 0f || _wasActionStopped;
        if (!retainFormInput) ConsumeFormInput();
        MoveInput = _moveAction?.action?.ReadValue<Vector2>() ?? Vector2.zero;
        IsForm1Pressed |= _form1Action?.action?.WasPressedThisFrame() ?? false;
        IsForm2Pressed |= _form2Action?.action?.WasPressedThisFrame() ?? false;
        IsForm3Pressed |= _form3Action?.action?.WasPressedThisFrame() ?? false;
        IsForm4Pressed |= _form4Action?.action?.WasPressedThisFrame() ?? false;
        IsForm5Pressed |= _form5Action?.action?.WasPressedThisFrame() ?? false;
        IsFastFormPressed |= _fastFormAction?.action?.WasPressedThisFrame() ?? false;

        JumpBuffer.Update(actionDelta);
        AttackBuffer.Update(actionDelta);
        DashBuffer.Update(actionDelta);
        SkillBuffer.Update(actionDelta);
        UltimateBuffer.Update(actionDelta);

        if(_jumpAction?.action?.WasPressedThisFrame() ?? false)
            JumpBuffer.Set();

        IsAttackHeld = _attackAction?.action?.IsPressed() ?? false;
        if (_attackAction?.action?.WasPressedThisFrame() ?? false)
        {
            AttackPressId++;
            AttackHeldTime = 0f;
            AttackBuffer.Set(0.5f);
        }
        // Retain the duration on release for buffered taps/holds.
        if (IsAttackHeld) AttackHeldTime += actionDelta;

        if(_dashAction?.action?.WasPressedThisFrame() ?? false)
            DashBuffer.Set();

        if(_skillAction?.action?.WasPressedThisFrame() ?? false)
            SkillBuffer.Set();

        if(_ultimateAction?.action?.WasPressedThisFrame() ?? false)
            UltimateBuffer.Set();
        _wasActionStopped = actionDelta <= 0f;
    }
}
