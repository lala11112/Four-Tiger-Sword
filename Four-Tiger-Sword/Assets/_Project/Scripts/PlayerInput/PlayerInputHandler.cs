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
    [SerializeField] private InputActionReference _parryAction;

    public Vector2 MoveInput { get; private set; }
    public bool IsDashHeld { get; private set; }
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
    public InputBuffer ParryBuffer;

    private void OnEnable()
    {
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
        _parryAction?.action?.Enable();
    }

    private void OnDisable()
    {
        MoveInput = Vector2.zero;
        IsDashHeld = false;
        IsForm1Pressed = IsForm2Pressed = IsForm3Pressed = IsForm4Pressed = IsForm5Pressed = IsFastFormPressed = false;
        JumpBuffer.Consume();
        AttackBuffer.Consume();
        DashBuffer.Consume();
        SkillBuffer.Consume();
        UltimateBuffer.Consume();
        ParryBuffer.Consume();
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
        _parryAction?.action?.Disable();
    }

    private void Update()
    {
        MoveInput = _moveAction?.action?.ReadValue<Vector2>() ?? Vector2.zero;
        IsDashHeld = _dashAction?.action?.IsPressed() ?? false;
        IsForm1Pressed = _form1Action?.action?.WasPressedThisFrame() ?? false;
        IsForm2Pressed = _form2Action?.action?.WasPressedThisFrame() ?? false;
        IsForm3Pressed = (_form3Action?.action?.WasPressedThisFrame() ?? false);
        IsForm4Pressed = (_form4Action?.action?.WasPressedThisFrame() ?? false);
        IsForm5Pressed = (_form5Action?.action?.WasPressedThisFrame() ?? false);
        IsFastFormPressed = _fastFormAction?.action?.WasPressedThisFrame() ?? false;

        JumpBuffer.Update(Time.deltaTime);
        AttackBuffer.Update(Time.deltaTime);
        DashBuffer.Update(Time.deltaTime);
        SkillBuffer.Update(Time.deltaTime);
        UltimateBuffer.Update(Time.deltaTime);
        ParryBuffer.Update(Time.deltaTime);

        if(_jumpAction?.action?.WasPressedThisFrame() ?? false)
            JumpBuffer.Set();

        if(_attackAction?.action?.WasPressedThisFrame() ?? false)
            AttackBuffer.Set(0.5f);

        if(_dashAction?.action?.WasPressedThisFrame() ?? false)
            DashBuffer.Set();

        if(_skillAction?.action?.WasPressedThisFrame() ?? false)
            SkillBuffer.Set();

        if(_ultimateAction?.action?.WasPressedThisFrame() ?? false)
            UltimateBuffer.Set();

        if((_parryAction?.action?.WasPressedThisFrame() ?? false))
            ParryBuffer.Set();
    }
}
