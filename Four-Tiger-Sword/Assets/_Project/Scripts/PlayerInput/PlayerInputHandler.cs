using UnityEngine;
using UnityEngine.InputSystem;

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

    private void OnEnable()
    {
        _moveAction.action.Enable();
        _jumpAction.action.Enable();
        _dashAction.action.Enable();
        _attackAction.action.Enable();
        _form1Action.action.Enable();
        _form2Action.action.Enable();
        _form3Action?.action.Enable();
        _form4Action?.action.Enable();
        _form5Action?.action.Enable();
        _fastFormAction.action.Enable();
        _skillAction.action.Enable();
        _ultimateAction.action.Enable();
    }

    private void OnDisable()
    {
        _moveAction.action.Disable();
        _jumpAction.action.Disable();
        _dashAction.action.Disable();
        _attackAction.action.Disable();
        _form1Action.action.Disable();
        _form2Action.action.Disable();
        _form3Action?.action.Disable();
        _form4Action?.action.Disable();
        _form5Action?.action.Disable();
        _fastFormAction.action.Disable();
        _skillAction.action.Disable();
        _ultimateAction.action.Disable();
    }

    private void Update()
    {
        MoveInput = _moveAction.action.ReadValue<Vector2>();
        IsDashHeld = _dashAction.action.IsPressed();
        IsForm1Pressed = _form1Action.action.WasPressedThisFrame();
        IsForm2Pressed = _form2Action.action.WasPressedThisFrame();
        IsForm3Pressed = _form3Action != null && _form3Action.action.WasPressedThisFrame();
        IsForm4Pressed = _form4Action != null && _form4Action.action.WasPressedThisFrame();
        IsForm5Pressed = _form5Action != null && _form5Action.action.WasPressedThisFrame();
        IsFastFormPressed = _fastFormAction.action.WasPressedThisFrame();

        JumpBuffer.Update(Time.deltaTime);
        AttackBuffer.Update(Time.deltaTime);
        DashBuffer.Update(Time.deltaTime);
        SkillBuffer.Update(Time.deltaTime);
        UltimateBuffer.Update(Time.deltaTime);

        if(_jumpAction.action.WasPressedThisFrame())
            JumpBuffer.Set();

        if(_attackAction.action.WasPressedThisFrame())
            AttackBuffer.Set(0.5f);

        if(_dashAction.action.WasPressedThisFrame())
            DashBuffer.Set();

        if(_skillAction.action.WasPressedThisFrame())
            SkillBuffer.Set();

        if(_ultimateAction.action.WasPressedThisFrame())
            UltimateBuffer.Set();
    }
}