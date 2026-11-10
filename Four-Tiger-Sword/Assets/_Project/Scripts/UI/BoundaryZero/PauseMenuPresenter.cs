using System;
using System.Collections.Generic;
using StudioBaekho;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>One pause session and menu shared by dungeon play and the world Hub.</summary>
[DefaultExecutionOrder(-110), DisallowMultipleComponent]
public sealed partial class PauseMenuPresenter : MonoBehaviour
{
    private enum ExitChoice { None, Title, Quit }
    private RunManager _run;
    private HubWorldController _world;
    private HubGateController _gate;
    private HubWorkshopInteraction _workshop;
    private PlayerController _player;
    private StageManager _stage;
    private BaekhoDungeonResult _generation;
    private int _chapterIndex, _stageIndex;
    private CombatHitStopService.WorldScaleLease _pause;
    private readonly List<CinemachineInputAxisController> _cameraInputs = new();
    private bool _ownsCursor, _previousCursorVisible, _ownsHubModal, _closing;
    private CursorLockMode _previousCursorLock;
    private ExitChoice _choice;

    public bool IsOpen { get; private set; }
    public bool IsConfirming => IsOpen && _choice != ExitChoice.None;
    public bool IsExiting { get; private set; }
    public string LastError { get; private set; }
    public Canvas PauseCanvas => _canvas;

    // Tests exercise failure paths without navigating away or terminating the Editor.
    internal Func<string, AsyncOperation> SceneLoaderOverride;
    internal Action QuitOverride;

    public static PauseMenuPresenter Ensure(RunManager run)
    {
        if (run == null) return null;
        var menu = run.GetComponent<PauseMenuPresenter>() ?? run.gameObject.AddComponent<PauseMenuPresenter>();
        menu.Configure(run);
        return menu;
    }

    public static PauseMenuPresenter Ensure(HubWorldController world)
    {
        if (world == null) return null;
        var menu = world.GetComponent<PauseMenuPresenter>() ?? world.gameObject.AddComponent<PauseMenuPresenter>();
        menu.Configure(world);
        return menu;
    }

    public void Configure(RunManager run)
    {
        if (_run == run && _world == null) return;
        CloseForHostShutdown();
        _run = run;
        _world = null;
        _gate = null;
        _workshop = null;
    }

    public void Configure(HubWorldController world)
    {
        if (_world == world && _run == null) return;
        CloseForHostShutdown();
        _run = null;
        _world = world;
        _gate = world != null ? world.GetComponent<HubGateController>() : null;
        _workshop = world != null ? world.GetComponent<HubWorkshopInteraction>() : null;
    }

    private PlayerController CurrentPlayer => _run != null ? _run.player : _world != null ? _world.PauseMenuPlayer : null;
    private bool PlayerAvailable(PlayerController player) => player != null && player.isActiveAndEnabled
        && !player.IsStageTransitioning && !player.IsFinisherSelecting
        && player.StatManager != null && player.StatManager.CurrentHp > 0f;

    private bool HostAvailable => _run != null ? _run.IsPauseMenuContextValid
        : _world != null && _world.isActiveAndEnabled && !HubReturnTransition.IsActive
            && (_gate == null || !_gate.IsLoading) && (_workshop == null || !_workshop.IsOpen);

    private bool SessionIsCurrent => IsOpen && isActiveAndEnabled && HostAvailable
        && _pause != null && _pause.IsValid && _player == CurrentPlayer && PlayerAvailable(_player)
        && _player.IsPauseMenuPaused && !_player.IsShopPaused && !_player.IsMapPaused
        && !_player.IsInventoryPaused && !_player.IsRewardPaused
        && (_world == null || _ownsHubModal && _world.IsModalOpen)
        && (_run == null || _run.stageManager == _stage && _stage != null && _stage.Result == _generation
            && _run.CurrentChapterIndex == _chapterIndex && _run.CurrentStageIndex == _stageIndex);

    private void Update()
    {
        if (IsOpen && (!SessionIsCurrent || _canvas == null || !_canvas.gameObject.activeInHierarchy || !_canvas.enabled))
        {
            CloseForHostShutdown();
            return;
        }
        // Runs before gameplay and other modal consumers. A modal that closes later this
        // frame therefore cannot let this same Escape press also open the pause menu.
        if (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false) HandleEscape();
    }

    public void HandleEscape()
    {
        if (IsExiting || _closing) return;
        if (!IsOpen) Open();
        else if (IsConfirming) CancelConfirmation();
        else Resume();
    }

    public bool Open()
    {
        if (IsOpen) return SessionIsCurrent;
        var player = CurrentPlayer;
        if (!isActiveAndEnabled || !HostAvailable || !PlayerAvailable(player) || player.IsGameplayModalPaused
            || _world != null && _world.IsModalOpen) return false;
        BuildView();
        _pause = CombatHitStopService.AcquireWorldScale(this, 0f);
        if (!_pause.IsValid) { _pause.Dispose(); _pause = null; return false; }
        _player = player;
        _stage = _run != null ? _run.stageManager : null;
        _generation = _stage != null ? _stage.Result : null;
        _chapterIndex = _run != null ? _run.CurrentChapterIndex : 0;
        _stageIndex = _run != null ? _run.CurrentStageIndex : 0;
        _previousCursorVisible = Cursor.visible;
        _previousCursorLock = Cursor.lockState;
        _ownsCursor = true;
        IsOpen = true;
        _closing = IsExiting = false;
        LastError = null;
        _choice = ExitChoice.None;
        try
        {
            _player.SetPauseMenuPaused(true);
            DisableCameraInputs();
            if (_world != null) { _ownsHubModal = true; _world.SetModalOpen(true); }
            ShowCursor();
            ShowMenu();
            _canvas.gameObject.SetActive(true);
            _motion.PlayShow();
            Select(_continueButton);
            return true;
        }
        catch
        {
            CloseForHostShutdown();
            throw;
        }
    }

    public void Resume()
    {
        if (!IsOpen || IsExiting || _closing) return;
        _closing = true;
        SetButtonsInteractable(false);
        _motion.PlayClose(() => CloseSession(true));
    }

    public void RequestTitle() => RequestExit(ExitChoice.Title);
    public void RequestQuit() => RequestExit(ExitChoice.Quit);

    private void RequestExit(ExitChoice choice)
    {
        if (!SessionIsCurrent || IsExiting || _closing) return;
        _choice = choice;
        LastError = null;
        ShowConfirmation();
        Select(_cancelButton);
    }

    public void CancelConfirmation()
    {
        if (!IsConfirming || IsExiting || _closing) return;
        _choice = ExitChoice.None;
        LastError = null;
        ShowMenu();
        Select(_continueButton);
    }

    public void ConfirmExit()
    {
        if (!SessionIsCurrent || !IsConfirming || IsExiting || _closing) return;
        IsExiting = true;
        SetButtonsInteractable(false);
        if (_run != null && !_run.TryPreparePauseMenuExit())
        {
            // Saving may have handed pause ownership to the existing reward-save error UI.
            if (IsOpen) RestoreExitFailure("영구 보상을 저장하지 못했습니다. 다시 시도해 주세요.");
            return;
        }
        if (!SessionIsCurrent) { CloseForHostShutdown(); return; }
        try
        {
            if (_choice == ExitChoice.Title)
            {
                if (SceneLoaderOverride == null && !Application.CanStreamedLevelBeLoaded("TitleScene"))
                { RestoreExitFailure("타이틀 씬을 찾을 수 없습니다."); return; }
                _heading.text = "이동 중...";
                var operation = SceneLoaderOverride != null ? SceneLoaderOverride("TitleScene")
                    : SceneManager.LoadSceneAsync("TitleScene", LoadSceneMode.Single);
                if (operation == null) RestoreExitFailure("타이틀로 이동하지 못했습니다. 다시 시도해 주세요.");
            }
            else
            {
                _heading.text = "종료 중...";
                if (QuitOverride != null) QuitOverride();
                else
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                }
            }
        }
        catch (Exception error)
        {
            RestoreExitFailure(_choice == ExitChoice.Title
                ? "타이틀로 이동하지 못했습니다. 다시 시도해 주세요."
                : "게임을 종료하지 못했습니다. 다시 시도해 주세요.");
            Debug.LogWarning("[Pause] Exit failed: " + error.Message, this);
        }
        // Accepted exits retain this lease and gameplay gate until scene/application teardown.
    }

    private void RestoreExitFailure(string error)
    {
        IsExiting = false;
        LastError = error;
        if (!IsOpen) return;
        ShowConfirmation();
        Select(_cancelButton);
    }

    private void DisableCameraInputs()
    {
        _cameraInputs.Clear();
        foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!TracksPlayer(camera.Follow) && !TracksPlayer(camera.LookAt)
                && !(camera.GetComponent<CombatCameraDirector>()?.IsOwnedBy(_player) ?? false)) continue;
            foreach (var input in camera.GetComponents<CinemachineInputAxisController>())
                if (input.enabled) { _cameraInputs.Add(input); input.enabled = false; }
        }
    }

    private bool TracksPlayer(Transform target) => target != null && _player != null
        && (target == _player.transform || target.IsChildOf(_player.transform));

    private void RestoreCameraInputs()
    {
        if (PlayerAvailable(_player))
            foreach (var input in _cameraInputs)
                if (input != null) input.enabled = true;
        _cameraInputs.Clear();
    }

    private void ShowCursor() { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }

    private void ClearSession(bool restoreCursor)
    {
        bool canRestoreCursor = restoreCursor && !IsExiting && HostAvailable
            && _player == CurrentPlayer && PlayerAvailable(_player);
        IsOpen = false;
        IsExiting = _closing = false;
        _choice = ExitChoice.None;
        if (_motion != null) _motion.HideImmediate();
        if (_canvas != null)
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && selected.transform.IsChildOf(_canvas.transform))
                EventSystem.current.SetSelectedGameObject(null);
            _canvas.gameObject.SetActive(false);
        }
        if (_player != null) _player.SetPauseMenuPaused(false);
        RestoreCameraInputs();
        if (_ownsHubModal && _world != null && _world.isActiveAndEnabled
            && (_gate == null || !_gate.IsLoading) && (_workshop == null || !_workshop.IsOpen)
            && !HubReturnTransition.IsActive)
        {
            // The logical modal must also be released if its player disappeared. Do not
            // reclaim a cursor already handed to another screen while doing that cleanup.
            bool cursorVisible = Cursor.visible;
            CursorLockMode cursorLock = Cursor.lockState;
            _world.SetModalOpen(false);
            if (!canRestoreCursor || !cursorVisible || cursorLock != CursorLockMode.None)
            { Cursor.visible = cursorVisible; Cursor.lockState = cursorLock; }
        }
        if (_ownsCursor && canRestoreCursor && Cursor.visible && Cursor.lockState == CursorLockMode.None)
        { Cursor.visible = _previousCursorVisible; Cursor.lockState = _previousCursorLock; }
        _ownsHubModal = _ownsCursor = false;
        _player = null;
        _stage = null;
        _generation = null;
    }

    private void CloseSession(bool restoreCursor)
    {
        ClearSession(restoreCursor);
        _pause?.Dispose();
        _pause = null;
    }

    public void CloseForHostShutdown() => CloseSession(true);

    /// <summary>Returns camera ownership before transition capture; caller releases time after blocking the player.</summary>
    public CombatHitStopService.WorldScaleLease PrepareForTransition()
    {
        ClearSession(false);
        var lease = _pause;
        _pause = null;
        return lease;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (focused && SessionIsCurrent) ShowCursor();
    }

    private void OnDisable() => CloseForHostShutdown();
    private void OnDestroy() => CloseForHostShutdown();
}
