using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : MonoBehaviour
{
    [Header("Input Action References")]
    [SerializeField] private InputActionReference _moveAction;
    [SerializeField] private InputActionReference _jumpAction;
    [SerializeField] private InputActionReference _dashAction;
    [SerializeField] private InputActionReference _attackAction;

    public Vector2 MoveInput { get; private set; }
    public bool IsJumpPressed { get; private set; }
    public bool IsDashPressed { get; private set; }

    public InputBuffer JumpBuffer;

    public InputBuffer AttackBuffer;

    private void OnEnable()
    {
        _moveAction.action.Enable();
        _jumpAction.action.Enable();
        _dashAction.action.Enable();
        _attackAction.action.Enable();
    }

    private void OnDisable()
    {
        _moveAction.action.Disable();
        _jumpAction.action.Disable();
        _dashAction.action.Disable();
        _attackAction.action.Disable();
    }

    private void Update()
    {
        MoveInput = _moveAction.action.ReadValue<Vector2>();
        IsDashPressed = _dashAction.action.WasPressedThisFrame();

        JumpBuffer.Update(Time.deltaTime);
        AttackBuffer.Update(Time.deltaTime);

        if(_jumpAction.action.WasPressedThisFrame())
        {
            JumpBuffer.Set();
        }

        if(_attackAction.action.WasPressedThisFrame())
        {
            AttackBuffer.Set();
        }
    }
}