using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public class TitleManager : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Button startButton;
    [SerializeField] private UnityEngine.UI.Button quitButton;
    [SerializeField] private BoundaryZeroRunOverlay loadingOverlay;

    public bool IsLoading { get; private set; }
    private bool _entering;
    private UIModalMotion[] _menuMotions;
    private UnityEngine.UI.Button[] _buttons;

    public void Configure(UnityEngine.UI.Button start, UnityEngine.UI.Button quit, BoundaryZeroRunOverlay overlay)
    {
        startButton = start;
        quitButton = quit;
        loadingOverlay = overlay;
    }

    private void Awake()
    {
        CombatHitStopService.SetExternalWorldTimeScale(1f);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        if (loadingOverlay != null) loadingOverlay.Hide();
    }

    private IEnumerator Start()
    {
        if (startButton == null) yield break;
        _entering = true;
        var buttons = startButton.transform.parent.GetComponentsInChildren<UnityEngine.UI.Button>(true);
        _buttons = buttons;
        foreach (var button in buttons)
        {
            var focus = button.GetComponent<TitleButtonFocus>();
            if (focus == null) focus = button.gameObject.AddComponent<TitleButtonFocus>();
        }
        _menuMotions = new UIModalMotion[buttons.Length];
        UIButtonMotion.Install(startButton.transform.parent.gameObject);
        SetMenuInteractable(false);
        ClearSelection();
        for (int i = 0; i < buttons.Length; i++)
        {
            _menuMotions[i] = UIModalMotion.Ensure(buttons[i].gameObject,
                buttons[i].transform as RectTransform, .24f, .16f, 14f);
            _menuMotions[i]?.HideImmediate();
        }
        foreach (var motion in _menuMotions)
        {
            motion?.PlayShow();
            yield return new WaitForSecondsRealtime(.08f);
        }
        while (System.Array.Exists(_menuMotions, m => m != null && m.IsAnimating)) yield return null;
        _entering = false;
        SetMenuInteractable(true);
        ClearSelection();
    }

    public void OnStartButton()
    {
        if (_entering || IsLoading || HubReturnTransition.IsActive || !isActiveAndEnabled) return;
        IsLoading = true;
        SetMenuInteractable(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        HubReturnTransition.TryStart(loadingOverlay, RestoreMenu);
    }

    public void OnQuitButton()
    {
        if (_entering || IsLoading) return;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void RestoreMenu(string reason)
    {
        if (this == null) return;
        if (loadingOverlay != null) loadingOverlay.Hide();
        IsLoading = false;
        SetMenuInteractable(true);
        ClearSelection();
        Debug.LogWarning(reason, this);
    }

    private void SetMenuInteractable(bool interactable)
    {
        if (startButton != null) startButton.interactable = interactable;
        if (quitButton != null) quitButton.interactable = interactable;
    }

    private void ClearSelection()
    {
        if (EventSystem.current == null) return;
        EventSystem.current.firstSelectedGameObject = null;
        EventSystem.current.SetSelectedGameObject(null);
    }

    private void LateUpdate()
    {
        var events = EventSystem.current;
        if (_entering || IsLoading || _buttons == null || events == null
            || events.currentSelectedGameObject != null) return;
        bool forward = Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)
            || Input.GetKeyDown(KeyCode.Tab);
        bool backward = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W);
        if (!forward && !backward)
        {
            var move = (events.currentInputModule as InputSystemUIInputModule)?.move?.action;
            if (move != null && move.WasPerformedThisFrame())
            {
                float vertical = move.ReadValue<Vector2>().y;
                forward = vertical < -.5f;
                backward = vertical > .5f;
            }
        }
        if (!forward && !backward) return;
        for (int n = 0; n < _buttons.Length; n++)
        {
            int index = backward ? _buttons.Length - n - 1 : n;
            var button = _buttons[index];
            if (button.IsActive() && button.IsInteractable())
            { events.SetSelectedGameObject(button.gameObject); break; }
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (_menuMotions != null) foreach (var motion in _menuMotions) motion?.ShowImmediate();
        _entering = false;
        IsLoading = HubReturnTransition.IsActive;
        if (!IsLoading && loadingOverlay != null) loadingOverlay.Hide();
        SetMenuInteractable(!IsLoading);
    }

    private void OnEnable()
    {
        // Start runs only once. A re-enabled menu must not retain its entrance input lock.
        if (_menuMotions == null) return;
        IsLoading = HubReturnTransition.IsActive;
        SetMenuInteractable(!IsLoading);
        if (!IsLoading) ClearSelection();
    }
}
