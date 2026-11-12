using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class PlayerController
{
    [Header("Finisher Selection")]
    [SerializeField, Range(0.01f, 1f)] private float _finisherTimeScale = 0.05f;
    public bool IsFinisherSelecting => _finisherAttack != null;
    public float FinisherSelectionTimeScale => _finisherTimeScale;

    private PlayerAttackState _finisherAttack;
    private BaseForm _finisherSource;
    private FinisherWheelView _finisherWheel;
    private CombatHitStopService.WorldScaleLease _finisherTimeLease;
    private readonly List<CinemachineInputAxisController> _finisherCameraInputs = new();
    private Vector2 _finisherCursor;
    private Vector3 _finisherForward;
    private bool _finisherCursorVisible;
    private CursorLockMode _finisherCursorLock;
    private bool _finisherOwnsCursor;
    private bool _finisherResolving;

    // Runs before normal state transitions so RMB consumes simultaneous action reservations.
    private void TryOpenFinisherFromInput()
    {
        if (Input == null || !Input.FinisherPressedThisFrame) return;
        Input.ConsumeFinisherPress();
        if (StateMachine?.CurrentState is PlayerAttackState attack) TryOpenFinisherSelection(attack);
    }

    internal bool TryOpenFinisherSelection(PlayerAttackState attack)
    {
        if (IsFinisherSelecting || !isActiveAndEnabled || IsStageTransitioning || IsGameplayModalPaused
            || StatManager == null || StatManager.CurrentHp <= 0f || PendingHitReaction != PlayerHitReaction.None
            || !IsGround() || attack == null || StateMachine?.CurrentState != attack || !attack.CanSelectFinisher
            || Input == null || !Input.IsFinisherInputAvailable || !(FormManager.CurrentForm is BaseForm source)) return false;
        _finisherAttack = attack;
        _finisherSource = source;
        _finisherForward = transform.forward;
        _finisherCursor = Vector2.zero;
        try
        {
            _finisherTimeLease = CombatHitStopService.AcquireWorldScale(this, Mathf.Clamp(_finisherTimeScale, .01f, 1f));
            if (!_finisherTimeLease.IsValid) { CancelFinisherSelection(); return false; }
            if (_finisherWheel == null) _finisherWheel = FinisherWheelView.Create(transform);
            attack.BeginFinisherSelection(_finisherForward);
            CancelPendingSwitchAttack();
            Input.SetFinisherInputBlocked(true);
            _finisherCursorVisible = Cursor.visible;
            _finisherCursorLock = Cursor.lockState;
            _finisherOwnsCursor = true;
            // A virtual cursor can leave the wheel without hitting a monitor edge.
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            _finisherCameraInputs.Clear();
            foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
            {
                if (!TracksPlayer(camera) && !(camera.GetComponent<CombatCameraDirector>()?.IsOwnedBy(this) ?? false)) continue;
                foreach (var axis in camera.GetComponents<CinemachineInputAxisController>())
                {
                    if (!axis.enabled) continue;
                    _finisherCameraInputs.Add(axis);
                    axis.enabled = false;
                }
            }
            _finisherWheel.Show(source.Element, FormManager.IsElementAvailable);
            return true;
        }
        catch (System.Exception error)
        {
            CancelFinisherSelection();
            Debug.LogException(error, this);
            return false;
        }
    }

    private bool FinisherSessionIsValid()
        => isActiveAndEnabled && !IsStageTransitioning && !IsGameplayModalPaused
            && StatManager != null && StatManager.CurrentHp > 0f
            && PendingHitReaction == PlayerHitReaction.None && IsGround()
            && _finisherAttack != null && StateMachine?.CurrentState == _finisherAttack
            && ReferenceEquals(FormManager?.CurrentForm, _finisherSource)
            && _finisherAttack.IsFinisherWindowValid && _finisherTimeLease != null && _finisherTimeLease.IsValid
            && Input != null && Input.IsFinisherInputAvailable;

    private void LateUpdate()
    {
        UpdateDashAfterimages(ActionDeltaTime);
        if (!IsFinisherSelecting || _finisherResolving) return;
        // Evaluate after gameplay updates: death, interruption and timeout win over release.
        if ((Keyboard.current?.escapeKey.wasPressedThisFrame ?? false) || !FinisherSessionIsValid())
        {
            CancelFinisherSelection();
            return;
        }
        _finisherCursor += Input.FinisherPointerDelta / Mathf.Max(.01f, _finisherWheel.ScaleFactor);
        _finisherWheel.UpdateSelection(_finisherCursor);
        if (!Input.IsFinisherHeld) ResolveFinisherSelection();
    }

    internal bool ResolveFinisherSelection()
    {
        if (_finisherResolving || !IsFinisherSelecting) return false;
        if ((Keyboard.current?.escapeKey.wasPressedThisFrame ?? false) || !FinisherSessionIsValid())
        {
            CancelFinisherSelection();
            return false;
        }
        _finisherResolving = true;
        try
        {
            ElementType selected = _finisherWheel.SelectedElement;
            if (selected == ElementType.ELEMENT_NONE || !FormManager.IsElementAvailable(selected)) return false;
            var target = FormManager.GetForm(selected);
            return _finisherAttack.CommitFinisher(target, _finisherForward);
        }
        finally
        {
            CancelFinisherSelection();
            _finisherResolving = false;
        }
    }

    public void CancelFinisherSelection()
    {
        bool hadSession = IsFinisherSelecting;
        _finisherAttack = null;
        _finisherSource = null;
        _finisherWheel?.Hide();
        _finisherTimeLease?.Dispose();
        _finisherTimeLease = null;
        if (hadSession) Input?.SetFinisherInputBlocked(false);
        foreach (var axis in _finisherCameraInputs)
            if (axis != null && !IsStageTransitioning && !IsGameplayModalPaused) axis.enabled = true;
        _finisherCameraInputs.Clear();
        if (_finisherOwnsCursor)
        {
            _finisherOwnsCursor = false;
            // Only restore the state this session still owns; an external modal may have taken it.
            if (!Cursor.visible && Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.visible = _finisherCursorVisible;
                Cursor.lockState = _finisherCursorLock;
            }
        }
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) CancelFinisherSelection();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) CancelFinisherSelection();
    }
}
