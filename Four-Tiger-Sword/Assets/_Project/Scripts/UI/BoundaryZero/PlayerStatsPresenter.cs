using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-110)]
[DisallowMultipleComponent]
public sealed class PlayerStatsPresenter : MonoBehaviour
{
    private RunManager _run;
    private HubWorldController _hub;
    private PlayerController _openPlayer;
    private PlayerStatManager _stats;
    private PlayerItemRuntime _items;
    private PlayerStatsPanelView _view;
    private bool _sessionOpen;
    private readonly List<CinemachineInputAxisController> _cameraInputs = new();
    private Scene _openScene;
    private GameObject _openStageRoot;
    private bool _cursorVisible;
    private CursorLockMode _cursorLock;
    private bool _dirty;
    private int _openedFrame;

    public bool IsOpen => _sessionOpen && _view != null && _view.IsOpen;

    private PlayerController Player => _run != null ? _run.player
        : _hub != null ? _hub.PauseMenuPlayer : null;

    public static PlayerStatsPresenter Ensure(RunManager owner)
    {
        if (owner == null) return null;
        var presenter = GetOrAdd(owner.gameObject);
        presenter._run = owner;
        return presenter;
    }

    public static PlayerStatsPresenter Ensure(HubWorldController owner)
    {
        if (owner == null) return null;
        var presenter = GetOrAdd(owner.gameObject);
        presenter._hub = owner;
        return presenter;
    }

    private static PlayerStatsPresenter GetOrAdd(GameObject owner)
        => owner.GetComponent<PlayerStatsPresenter>() ?? owner.AddComponent<PlayerStatsPresenter>();

    private static bool PlayerIsAvailable(PlayerController player)
        => player != null && player.isActiveAndEnabled && !player.IsStageTransitioning
            && !player.IsFinisherSelecting && player.StatManager != null && player.StatManager.CurrentHp > 0f;

    private bool ContextIsAvailable(PlayerController player)
        => PlayerIsAvailable(player) && (_run != null ? _run.CanShowPlayerStats : _hub != null && _hub.CanShowPlayerStats);

    private bool OpenStageIsCurrent => _run == null || (_run.State == RunState.Playing
        && _openStageRoot != null && _openStageRoot.activeInHierarchy && _run.stageManager?.Result?.root == _openStageRoot);

    private bool CanOpen(PlayerController player)
        => isActiveAndEnabled && ContextIsAvailable(player) && !player.IsGameplayModalPaused && !player.IsStatsOpen;

    private void EnsureView()
    {
        if (_view != null) return;
        var host = new GameObject("PlayerStatsPanel", typeof(RectTransform));
        host.transform.SetParent(transform, false);
        _view = host.AddComponent<PlayerStatsPanelView>();
        _view.SetOpenerOffset(new Vector2(-55f, _hub != null ? -112f : -430f));
        _view.Build(showOpener: _hub != null);
        _view.OpenRequested += OnOpenRequested;
        _view.CloseRequested += Close;
    }

    private void OnOpenRequested() => TryOpen();

    public bool TryOpen()
    {
        if (IsOpen) return true;
        if (_sessionOpen) Close();
        var player = Player;
        if (!CanOpen(player)) return false;
        EnsureView();
        _sessionOpen = true;
        _openPlayer = player;
        _openedFrame = Time.frameCount;
        _openScene = SceneManager.GetActiveScene();
        _openStageRoot = _run != null ? _run.stageManager.Result.root : null;
        _cursorVisible = Cursor.visible;
        _cursorLock = Cursor.lockState;
        try
        {
            player.SetStatsOpen(true);
            DisableCameraInputs(player.transform);
            BindStats(player);
            if (_hub != null) _hub.SetStatsModalOpen(true);
            else
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            RefreshStats();
            _view.SetAvailable(true);
            _view.Show();
            player.Input?.SetStatsPointerRegion(_view.PanelRoot);
            if (IsOpen) return true;
            Close();
            return false;
        }
        catch
        {
            Close();
            throw;
        }
    }

    public void Close()
    {
        if (!_sessionOpen) return;
        _sessionOpen = false;
        var player = _openPlayer;
        bool sameWorld = _openScene == SceneManager.GetActiveScene() && OpenStageIsCurrent;
        bool otherRunModal = _run != null && (_run.IsShopOpen || _run.IsMapOpen || _run.IsInventoryOpen || _run.IsRewardOpen);
        bool otherHubModal = _hub != null && _hub.isActiveAndEnabled && !_hub.CanShowPlayerStats;
        bool restoreCamera = sameWorld && !otherRunModal && !otherHubModal
            && !BlocksControlRestore(player) && !BlocksControlRestore(Player);
        bool ownsSceneCursor = restoreCamera
            && (_run != null ? _run.CanShowPlayerStats : _hub != null && _hub.CanShowPlayerStats);
        _openPlayer = null;
        _openStageRoot = null;
        if (player != null)
        {
            player.SetStatsOpen(false);
            player.Input?.SetStatsPointerRegion(null);
        }
        if (restoreCamera)
            foreach (var input in _cameraInputs)
                if (input != null) input.enabled = true;
        _cameraInputs.Clear();
        UnbindStats(player);
        if (_hub != null)
            _hub.SetStatsModalOpen(false, ownsSceneCursor);
        else if (ownsSceneCursor && Cursor.visible && Cursor.lockState == CursorLockMode.None)
        {
            Cursor.visible = _cursorVisible;
            Cursor.lockState = _cursorLock;
        }
        if (_view == null) return;
        var eventSystem = EventSystem.current;
        var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected != null && selected.transform.IsChildOf(_view.transform)) eventSystem.SetSelectedGameObject(null);
        _view.Hide();

        static bool BlocksControlRestore(PlayerController candidate)
            => candidate != null && (candidate.IsStageTransitioning || candidate.IsFinisherSelecting
                || candidate.IsShopPaused || candidate.IsMapPaused || candidate.IsInventoryPaused || candidate.IsRewardPaused
                || (candidate.StatManager != null && candidate.StatManager.CurrentHp <= 0f));
    }

    private void BindStats(PlayerController player)
    {
        _stats = player.StatManager;
        _items = player.ItemRuntime;
        _stats.OnStatsChanged += MarkDirty;
        if (_items != null) _items.OnOwnedItemsChanged += MarkDirty;
        player.Died += Close;
        _dirty = true;
    }

    private void UnbindStats(PlayerController player)
    {
        if (_stats != null) _stats.OnStatsChanged -= MarkDirty;
        if (_items != null) _items.OnOwnedItemsChanged -= MarkDirty;
        if (player != null) player.Died -= Close;
        _stats = null;
        _items = null;
        _dirty = false;
    }

    private void MarkDirty() => _dirty = true;

    private void RefreshStats()
    {
        _dirty = false;
        _view.Render(_stats);
    }

    private void DisableCameraInputs(Transform player)
    {
        foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!TracksPlayer(camera.Follow, player) && !TracksPlayer(camera.LookAt, player)) continue;
            foreach (var input in camera.GetComponents<CinemachineInputAxisController>())
            {
                if (!input.enabled) continue;
                _cameraInputs.Add(input);
                input.enabled = false;
            }
        }
    }

    private static bool TracksPlayer(Transform target, Transform player)
        => target != null && (target == player || target.IsChildOf(player));

    private void Update()
    {
        // Release cursor/camera ownership before the finisher can claim them later this frame.
        var mouse = Mouse.current;
        if (IsOpen && mouse != null && mouse.rightButton.wasPressedThisFrame
            && !RectTransformUtility.RectangleContainsScreenPoint(_view.PanelRoot, mouse.position.ReadValue()))
            Close();
        var keyboard = Keyboard.current;
        if (keyboard != null && !IsOpen && keyboard.cKey.wasPressedThisFrame) TryOpen();
    }

    private void LateUpdate()
    {
        var player = Player;
        if (_sessionOpen)
        {
            var keyboard = Keyboard.current;
            bool closePressed = keyboard != null
                && (keyboard.escapeKey.wasPressedThisFrame || (_openedFrame != Time.frameCount && keyboard.cKey.wasPressedThisFrame));
            if (closePressed || !IsOpen || player != _openPlayer || !ContextIsAvailable(player)
                || !player.IsStatsOpen || !OpenStageIsCurrent)
                Close();
            else if (_dirty) RefreshStats();
        }
        bool available = IsOpen || CanOpen(player);
        if (available) EnsureView();
        if (_view != null) _view.SetAvailable(available);
    }

    private void OnEnable() => SceneManager.activeSceneChanged += OnActiveSceneChanged;

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        Close();
        if (_view != null) _view.SetAvailable(false);
    }

    private void OnActiveSceneChanged(Scene previous, Scene current)
    {
        Close();
        if (_view != null) _view.SetAvailable(false);
    }
    private void OnApplicationFocus(bool focused) { if (!focused) Close(); }
    private void OnApplicationPause(bool paused) { if (paused) Close(); }
}
