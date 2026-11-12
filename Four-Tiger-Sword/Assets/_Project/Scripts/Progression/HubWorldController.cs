using System.Collections.Generic;
using BoundaryZero.Inventory;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Owns the exploration cursor while the world Hub is active.</summary>
[DisallowMultipleComponent]
public sealed class HubWorldController : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private ItemCatalog itemCatalog;
    [SerializeField] private InventoryCatalog inventoryArt;

    private bool _previousCursorVisible;
    private CursorLockMode _previousCursorLock;
    private bool _ownsCursor;
    private bool _cursorModal;
    private bool _modalOpen;
    private bool _statsModalOpen;
    private PauseMenuPresenter _pauseMenu;
    private HubFieldHud _hud;
    private PlayerStatsPresenter _playerStats;
    private InventoryView _codex;
    private PlayerUpgradeService _upgrades;
    private CombatHitStopService.WorldScaleLease _codexPause;
    private HubCodexInteraction _codexOwner;
    private readonly List<CinemachineInputAxisController> _cameraInputs = new();

    public bool IsModalOpen => _modalOpen || _codexPause != null || _statsModalOpen;
    public bool IsCodexOpen => _codex != null && _codex.IsOpen;
    public bool IsPauseMenuOpen => _pauseMenu != null && _pauseMenu.IsOpen;
    public PlayerController PauseMenuPlayer => player != null ? player : GetComponentInChildren<PlayerController>();
    internal bool CanShowPlayerStats => isActiveAndEnabled && !_modalOpen && _codexPause == null;

    private bool HasCatalog => itemCatalog != null && inventoryArt != null;
    private bool PlayerAvailable => player != null && player.isActiveAndEnabled && !player.IsStageTransitioning
        && player.StatManager != null && player.StatManager.CurrentHp > 0f;

    private void Awake()
    {
        CombatHitStopService.SetExternalWorldTimeScale(1f);
        if (player == null) player = GetComponentInChildren<PlayerController>();
        _hud = new HubFieldHud(transform, inventoryArt);
        _pauseMenu = PauseMenuPresenter.Ensure(this);
        _playerStats = PlayerStatsPresenter.Ensure(this);
    }

    private void OnEnable()
    {
        _previousCursorVisible = Cursor.visible;
        _previousCursorLock = Cursor.lockState;
        _ownsCursor = true;
        _modalOpen = false;
        _hud.Root.gameObject.SetActive(true);
        _upgrades = PlayerUpgradeService.Instance;
        _upgrades.Changed += RefreshProgression;
        RefreshProgression();
        RefreshCursor();
    }

    private void OnApplicationFocus(bool focused)
    {
        if (focused && _ownsCursor && isActiveAndEnabled) RefreshCursor();
    }

    internal void SetModalOpen(bool open)
    {
        _modalOpen = open;
        if (open) _playerStats?.Close();
        if (_ownsCursor && isActiveAndEnabled) RefreshCursor();
    }

    internal void SetStatsModalOpen(bool open, bool refreshCursor = true)
    {
        _statsModalOpen = open;
        if (refreshCursor && _ownsCursor && isActiveAndEnabled) RefreshCursor();
    }

    private void RefreshCursor()
    {
        _cursorModal = IsModalOpen;
        Cursor.visible = _cursorModal;
        Cursor.lockState = _cursorModal ? CursorLockMode.None : CursorLockMode.Locked;
    }

    private void RefreshProgression()
    {
        _hud.SetBalance(_upgrades.Settings?.currencyName, _upgrades.IsReady ? _upgrades.Balance : null);
        if (!_upgrades.IsReady || player == null) return;
        // Hub purchases update the same stat contribution used by the next expedition,
        // without starting a run or resetting its conditional combat effects.
        var stats = player.GetComponent<PlayerStatManager>();
        if (stats != null)
            PlayerEngravingRuntime.ApplySnapshotStats(stats, _upgrades.CreateEngravingSnapshot());
    }

    private void Update()
    {
        if (_codexPause != null && (!PlayerAvailable || _modalOpen || !_codexPause.IsValid || !IsCodexOpen
            || Keyboard.current?.escapeKey.wasPressedThisFrame == true))
            CloseCodex();
    }

    internal bool CanOpenCodex(PlayerController targetPlayer) =>
        isActiveAndEnabled && !IsModalOpen && HasCatalog && PlayerAvailable
        && player == targetPlayer && !player.IsGameplayModalPaused && !player.IsFinisherSelecting;

    internal bool TryOpenCodex(HubCodexInteraction owner, PlayerController targetPlayer)
    {
        if (owner == null || !owner.isActiveAndEnabled || !owner.IsInRange || !CanOpenCodex(targetPlayer)) return false;
        if (_codex == null)
        {
            _codex = _hud.BuildCodex(inventoryArt, ItemDisplayText.ToInventoryData(itemCatalog));
            _codex.VisibilityChanged += OnCodexVisibilityChanged;
        }

        var pause = CombatHitStopService.AcquireWorldScale(this, 0f);
        if (!pause.IsValid)
        {
            pause.Dispose();
            return false;
        }
        _codexPause = pause;
        _codexOwner = owner;
        try
        {
            player.SetInventoryPaused(true);
            foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
            {
                if (!TracksPlayer(camera.Follow) && !TracksPlayer(camera.LookAt)) continue;
                foreach (var input in camera.GetComponents<CinemachineInputAxisController>())
                {
                    if (!input.enabled) continue;
                    _cameraInputs.Add(input);
                    input.enabled = false;
                }
            }
            _codex.Open();
            if (!IsCodexOpen)
            {
                CloseCodex();
                return false;
            }
            RefreshCursor();
            return true;
        }
        catch
        {
            CloseCodex();
            throw;
        }
    }

    private bool TracksPlayer(Transform target)
        => target != null && (target == player.transform || target.IsChildOf(player.transform));

    private void OnCodexVisibilityChanged(bool visible)
    {
        if (!visible) CloseCodex();
    }

    internal bool OwnsCodex(HubCodexInteraction owner) => _codexPause != null && _codexOwner == owner;

    internal void CloseCodex(HubCodexInteraction owner)
    {
        if (OwnsCodex(owner)) CloseCodex();
    }

    private void CloseCodex()
    {
        if (_codexPause == null) return;
        var pause = _codexPause;
        _codexPause = null;
        _codexOwner = null;
        if (player != null) player.SetInventoryPaused(false);
        if (PlayerAvailable)
            foreach (var input in _cameraInputs)
                if (input != null) input.enabled = true;
        _cameraInputs.Clear();
        pause.Dispose();
        var eventSystem = EventSystem.current;
        var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected != null && _codex != null && selected.transform.IsChildOf(_codex.transform))
            eventSystem.SetSelectedGameObject(null);
        if (_codex != null) _codex.Close();
        if (_ownsCursor && isActiveAndEnabled) RefreshCursor();
    }

    private void OnDisable()
    {
        // A scene/menu that already changed the cursor has taken ownership.
        var expectedLock = _cursorModal ? CursorLockMode.None : CursorLockMode.Locked;
        if (_ownsCursor && Cursor.visible == _cursorModal && Cursor.lockState == expectedLock)
        {
            Cursor.visible = _previousCursorVisible;
            Cursor.lockState = _previousCursorLock;
        }
        _ownsCursor = false;
        _pauseMenu?.CloseForHostShutdown();
        _playerStats?.Close();
        CloseCodex();
        if (_upgrades != null) _upgrades.Changed -= RefreshProgression;
        if (_hud != null) _hud.Root.gameObject.SetActive(false);
    }
}
