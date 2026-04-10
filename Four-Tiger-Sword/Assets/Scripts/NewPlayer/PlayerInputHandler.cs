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
    [SerializeField] private InputActionReference _skillAction;
    [SerializeField] private InputActionReference _ultimateAction;

    public Vector2 MoveInput { get; private set; }
    public bool IsDashHeld { get; private set; }
    public bool IsForm1Pressed { get; private set; }
    public bool IsForm2Pressed { get; private set; }

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
        _skillAction.action.Disable();
        _ultimateAction.action.Disable();
    }

    private void Update()
    {
        MoveInput = _moveAction.action.ReadValue<Vector2>();
        IsDashHeld = _dashAction.action.IsPressed();
        IsForm1Pressed = _form1Action.action.WasPressedThisFrame();
        IsForm2Pressed = _form2Action.action.WasPressedThisFrame();

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