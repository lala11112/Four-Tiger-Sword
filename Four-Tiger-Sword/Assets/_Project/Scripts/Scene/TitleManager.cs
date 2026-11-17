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
    public SaveSlotView SlotView => _slotView;
    public UnityEngine.UI.Button ContinueButton => _continueButton;
    internal static System.Func<string, AsyncOperation> TutorialSceneLoaderOverride;
    private bool _entering;
    private bool _canContinue;
    private SaveSlotView _slotView;
    private UIModalMotion[] _menuMotions;
    private UnityEngine.UI.Button[] _buttons;
    private UnityEngine.UI.Button _continueButton;

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
        ConfigureMenu();
        _entering = true;
        SetMenuInteractable(false);
    }

    private void ConfigureMenu()
    {
        if (startButton == null) return;
        var menu = startButton.transform.parent;
        var row = menu.Find("Continue");
        if (row != null)
        {
            row.gameObject.SetActive(true);
            _continueButton = row.GetComponent<UnityEngine.UI.Button>();
            if (_continueButton != null)
            {
                _continueButton.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                _continueButton.onClick.AddListener(OnContinueButton);
                _continueButton.interactable = false;
                foreach (var label in row.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    label.text = "이어하기";
            }
        }
        UpdateMenuNavigation();
    }

    private void UpdateMenuNavigation()
    {
        var middle = _canContinue && _continueButton != null ? _continueButton : null;
        if (startButton == null) return;
        startButton.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit,
            selectOnDown = middle != null ? middle : quitButton, selectOnUp = quitButton };
        if (_continueButton != null) _continueButton.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit,
            selectOnUp = startButton, selectOnDown = quitButton };
        if (quitButton != null) quitButton.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit,
            selectOnUp = middle != null ? middle : startButton, selectOnDown = startButton };
    }

    private void RefreshContinueAvailability()
    {
        _canContinue = SaveSlotService.Instance.TryGetLastPlayedSlot(out _, out _);
        UpdateMenuNavigation();
    }

    private IEnumerator Start()
    {
        if (startButton == null) yield break;
        _entering = true;
        // Resolve persistence after the initial scene lifecycle callbacks have finished.
        yield return null;
        RefreshContinueAvailability();
        var buttons = startButton.transform.parent.GetComponentsInChildren<UnityEngine.UI.Button>();
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
        OpenSlots();
    }

    public void OnContinueButton()
    {
        if (_entering || IsLoading || HubReturnTransition.IsActive || !isActiveAndEnabled || _slotView?.IsOpen == true) return;
        if (!SaveSlotService.Instance.TryGetLastPlayedSlot(out var slot, out string error))
        {
            RefreshContinueAvailability();
            ShowSlotError(error ?? "이어할 저장 슬롯이 없습니다.");
            return;
        }
        LaunchSlot(slot.Id);
    }

    private void OpenSlots()
    {
        if (_entering || IsLoading || HubReturnTransition.IsActive || !isActiveAndEnabled || _slotView?.IsOpen == true) return;
        if (_slotView == null)
        {
            _slotView = SaveSlotView.Ensure(this);
            _slotView.SlotChosen += OnSlotChosen;
            _slotView.CreateRequested += OnCreateSlot;
            _slotView.DeleteRequested += OnDeleteSlot;
            _slotView.Closed += OnSlotsClosed;
        }
        SetMenuInteractable(false);
        ClearSelection();
        var slots = SaveSlotService.Instance;
        bool ready = slots.TryRefresh(out string error);
        _slotView.Show(slots.Slots, false);
        if (!ready) _slotView.ShowError(error);
    }

    private bool CanUseSlots => !_entering && !IsLoading && !HubReturnTransition.IsActive
        && isActiveAndEnabled && _slotView?.IsOpen == true;

    public void OnCreateSlot(int number, string name)
    {
        if (!CanUseSlots) return;
        var slots = SaveSlotService.Instance;
        if (!slots.TryCreate(number, name, out var slot, out string error))
        { _slotView.ShowError(error); return; }
        // The new row remains available if account activation or scene loading fails.
        _slotView.Refresh(slots.Slots);
        OnSlotChosen(slot.Id);
    }

    public void OnDeleteSlot(string id)
    {
        if (!CanUseSlots) return;
        var slots = SaveSlotService.Instance;
        if (!slots.TryDelete(id, out string error)) { _slotView.ShowError(error); return; }
        RefreshContinueAvailability();
        _slotView.Refresh(slots.Slots);
    }

    public void OnSlotChosen(string id)
    {
        if (!CanUseSlots) return;
        LaunchSlot(id);
    }

    private void LaunchSlot(string id)
    {
        if (!SaveSlotService.Instance.TrySelect(id, out var account, out string error))
        { ShowSlotError(error); return; }
        if (!account.IsReady) { ShowSlotError(account.LastError); return; }
        if (account.TutorialStatus == TutorialProgressStatus.NotStarted
            || account.TutorialStatus == TutorialProgressStatus.InMap)
        { BeginTutorial(); return; }
        TutorialSession.Reset();
        BeginLoading();
        if (!HubReturnTransition.TryStart(loadingOverlay, RestoreMenu) && IsLoading)
            RestoreMenu("거점 이동을 시작하지 못했습니다. 다시 시도해 주세요.");
    }

    private void OnSlotsClosed()
    {
        if (IsLoading) return;
        RefreshContinueAvailability();
        SetMenuInteractable(true);
        ClearSelection();
        if (startButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(startButton.gameObject);
    }

    private void BeginLoading()
    {
        IsLoading = true;
        SetMenuInteractable(false);
        _slotView?.SetBusy(true);
        ClearSelection();
    }

    private void BeginTutorial()
    {
        if (!TutorialSession.TryBegin(false, out string error)) { RestoreMenu(error); return; }
        BeginLoading();
        if (loadingOverlay != null)
        {
            loadingOverlay.gameObject.SetActive(true);
            loadingOverlay.ShowLoading();
            loadingOverlay.ActionButton.gameObject.SetActive(false);
            loadingOverlay.ModalRoot.SetActive(true);
        }
        StartCoroutine(LoadTutorial());
    }

    private IEnumerator LoadTutorial()
    {
        yield return null;
        UnityEngine.AsyncOperation operation = null;
        string failure = null;
        try { operation = TutorialSceneLoaderOverride != null ? TutorialSceneLoaderOverride(TutorialSession.SceneName)
            : UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(TutorialSession.SceneName); }
        catch (System.Exception error) { failure = error.Message; }
        if (operation == null) { RestoreMenu(failure ?? "튜토리얼을 불러오지 못했습니다. 슬롯을 선택해 다시 시도해 주세요."); yield break; }
        while (!operation.isDone) yield return null;
    }

    public void OnQuitButton()
    {
        if (_entering || IsLoading || _slotView?.IsOpen == true) return;
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
        RefreshContinueAvailability();
        bool slotsOpen = _slotView?.IsOpen == true;
        SetMenuInteractable(!slotsOpen);
        ClearSelection();
        if (slotsOpen)
        {
            _slotView.SetBusy(false);
            var slots = SaveSlotService.Instance;
            slots.TryRefresh(out _);
            _slotView.Refresh(slots.Slots);
            _slotView.ShowError(reason);
        }
        else ShowSlotError(reason);
        Debug.LogWarning(reason, this);
    }

    private void ShowSlotError(string reason)
    {
        if (_slotView?.IsOpen != true) OpenSlots();
        _slotView?.ShowError(reason);
    }

    private void SetMenuInteractable(bool interactable)
    {
        if (startButton != null) startButton.interactable = interactable;
        if (quitButton != null) quitButton.interactable = interactable;
        if (_continueButton != null)
        {
            _continueButton.interactable = interactable && _canContinue;
            foreach (var label in _continueButton.GetComponentsInChildren<TMPro.TMP_Text>(true))
                label.color = _canContinue ? Color.white : new Color32(83, 101, 116, 255);
        }
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
        if (_entering || IsLoading || _slotView?.IsOpen == true || _buttons == null || events == null
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
        _slotView?.Hide();
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
        RefreshContinueAvailability();
        SetMenuInteractable(!IsLoading);
        if (!IsLoading) ClearSelection();
    }

    private void OnDestroy()
    {
        if (_slotView == null) return;
        _slotView.SlotChosen -= OnSlotChosen;
        _slotView.CreateRequested -= OnCreateSlot;
        _slotView.DeleteRequested -= OnDeleteSlot;
        _slotView.Closed -= OnSlotsClosed;
    }
}
