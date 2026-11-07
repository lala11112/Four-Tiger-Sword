using UnityEngine;

/// <summary>Owns the exploration cursor while the world Hub is active.</summary>
[DisallowMultipleComponent]
public sealed class HubWorldController : MonoBehaviour
{
    private bool _previousCursorVisible;
    private CursorLockMode _previousCursorLock;
    private bool _ownsCursor;
    private bool _modalOpen;

    public bool IsModalOpen => _modalOpen;

    private void Awake()
    {
        CombatHitStopService.SetExternalWorldTimeScale(1f);
    }

    private void OnEnable()
    {
        _previousCursorVisible = Cursor.visible;
        _previousCursorLock = Cursor.lockState;
        _ownsCursor = true;
        _modalOpen = false;
        RefreshCursor();
    }

    private void OnApplicationFocus(bool focused)
    {
        if (focused && _ownsCursor && isActiveAndEnabled) RefreshCursor();
    }

    internal void SetModalOpen(bool open)
    {
        _modalOpen = open;
        if (_ownsCursor && isActiveAndEnabled) RefreshCursor();
    }

    private void RefreshCursor()
    {
        Cursor.visible = _modalOpen;
        Cursor.lockState = _modalOpen ? CursorLockMode.None : CursorLockMode.Locked;
    }

    private void OnDisable()
    {
        // A scene/menu that already changed the cursor has taken ownership.
        var expectedLock = _modalOpen ? CursorLockMode.None : CursorLockMode.Locked;
        if (_ownsCursor && Cursor.visible == _modalOpen && Cursor.lockState == expectedLock)
        {
            Cursor.visible = _previousCursorVisible;
            Cursor.lockState = _previousCursorLock;
        }
        _ownsCursor = false;
    }
}
