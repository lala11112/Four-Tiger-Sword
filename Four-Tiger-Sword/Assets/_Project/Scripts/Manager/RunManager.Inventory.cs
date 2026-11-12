using System;
using System.Collections.Generic;
using StudioBaekho;
using Unity.Cinemachine;
using UnityEngine;

public sealed partial class RunManager
{
    public bool IsInventoryOpen { get; private set; }
    public bool IsInventorySessionCurrent => IsInventoryOpen && InventoryContextIsCurrent()
        && OwnerIsAvailable(_inventoryOwner) && _inventoryWorldPause != null && _inventoryWorldPause.IsValid;
    public event Action<bool> InventoryOpenChanged;

    private UnityEngine.Object _inventoryOwner;
    private PlayerController _inventoryPlayer;
    private StageManager _inventoryStage;
    private BaekhoDungeonResult _inventoryGeneration;
    private GameObject _inventoryRoot;
    private int _inventoryChapterIndex, _inventoryStageIndex;
    private readonly List<CinemachineInputAxisController> _inventoryCameraInputs = new();
    private CombatHitStopService.WorldScaleLease _inventoryWorldPause;
    private bool _inventoryOwnsCursor, _inventoryCursorVisible;
    private CursorLockMode _inventoryCursorLock;

    /// <summary>Opens the run's read-only inventory while retaining the current combat action.</summary>
    public bool TryOpenInventory(UnityEngine.Object owner)
    {
        if (IsInventoryOpen) return owner != null && owner == _inventoryOwner && IsInventorySessionCurrent;
        if (!isActiveAndEnabled || !OwnerIsAvailable(owner) || State != RunState.Playing || _transition != null
            || IsShopOpen || IsMapOpen || IsRewardOpen || HasPendingItemChoice || player == null || !player.isActiveAndEnabled || player.IsStageTransitioning
            || player.IsGameplayModalPaused || player.IsStatsOpen || player.IsFinisherSelecting || player.StatManager == null
            || player.StatManager.CurrentHp <= 0f || stageManager == null || !stageManager.IsReady
            || stageManager.Result == null || !stageManager.Result.success || stageManager.Result.root == null
            || !stageManager.Result.root.activeInHierarchy) return false;

        var pause = CombatHitStopService.AcquireWorldScale(owner, 0f);
        if (!pause.IsValid) { pause.Dispose(); return false; }
        _inventoryWorldPause = pause;
        _inventoryOwner = owner;
        _inventoryPlayer = player;
        _inventoryStage = stageManager;
        _inventoryGeneration = stageManager.Result;
        _inventoryRoot = _inventoryGeneration.root;
        _inventoryChapterIndex = CurrentChapterIndex;
        _inventoryStageIndex = CurrentStageIndex;
        _inventoryCursorVisible = Cursor.visible;
        _inventoryCursorLock = Cursor.lockState;
        _inventoryOwnsCursor = true;
        IsInventoryOpen = true;
        _inventoryPlayer.SetInventoryPaused(true);
        DisableInventoryCameraInputs();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        NotifyInventoryOpenChanged(true);
        return IsInventoryOpen;
    }

    public void CloseInventory()
    {
        if (!IsInventoryOpen) return;
        if (State == RunState.Playing && _inventoryPlayer != null && _inventoryPlayer.StatManager != null
            && _inventoryPlayer.StatManager.CurrentHp <= 0f)
        { Lose(); return; }
        ClearInventorySession(CanRestoreInventoryCameraInputs(), isActiveAndEnabled && InventoryContextIsCurrent());
        ReleaseInventoryWorldPause();
        NotifyInventoryOpenChanged(false);
    }

    private void UpdateInventorySession()
    {
        if (IsInventoryOpen && !IsInventorySessionCurrent) CloseInventory();
    }

    private bool InventoryContextIsCurrent()
        => isActiveAndEnabled && State == RunState.Playing && _transition == null && !IsShopOpen && !IsMapOpen
            && _inventoryPlayer != null && player == _inventoryPlayer && _inventoryPlayer.isActiveAndEnabled
            && _inventoryPlayer.IsInventoryPaused && !_inventoryPlayer.IsShopPaused && !_inventoryPlayer.IsMapPaused
            && !_inventoryPlayer.IsStageTransitioning && !_inventoryPlayer.IsFinisherSelecting
            && _inventoryPlayer.StatManager != null && _inventoryPlayer.StatManager.CurrentHp > 0f
            && stageManager != null && stageManager == _inventoryStage && stageManager.IsReady
            && stageManager.Result == _inventoryGeneration && _inventoryRoot != null && _inventoryRoot.activeInHierarchy
            && _inventoryGeneration.root == _inventoryRoot
            && CurrentChapterIndex == _inventoryChapterIndex && CurrentStageIndex == _inventoryStageIndex;

    private bool CanRestoreInventoryCameraInputs()
        => State == RunState.Playing && _inventoryPlayer != null && player == _inventoryPlayer
            && !_inventoryPlayer.IsStageTransitioning && _inventoryPlayer.StatManager != null
            && _inventoryPlayer.StatManager.CurrentHp > 0f;

    private void DisableInventoryCameraInputs()
    {
        _inventoryCameraInputs.Clear();
        foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!InventoryCameraTargetsPlayer(camera.Follow) && !InventoryCameraTargetsPlayer(camera.LookAt)) continue;
            foreach (var input in camera.GetComponents<CinemachineInputAxisController>())
            {
                if (!input.enabled) continue;
                _inventoryCameraInputs.Add(input);
                input.enabled = false;
            }
        }
    }

    private bool InventoryCameraTargetsPlayer(Transform target)
        => target != null && _inventoryPlayer != null
            && (target == _inventoryPlayer.transform || target.IsChildOf(_inventoryPlayer.transform));

    private void ClearInventorySession(bool restoreCameraInputs, bool restoreCursor)
    {
        IsInventoryOpen = false;
        if (_inventoryPlayer != null) _inventoryPlayer.SetInventoryPaused(false);
        if (restoreCameraInputs)
            foreach (var input in _inventoryCameraInputs)
                if (input != null) input.enabled = true;
        _inventoryCameraInputs.Clear();
        if (_inventoryOwnsCursor && restoreCursor && Cursor.visible && Cursor.lockState == CursorLockMode.None)
        { Cursor.visible = _inventoryCursorVisible; Cursor.lockState = _inventoryCursorLock; }
        _inventoryOwnsCursor = false;
        _inventoryOwner = null;
        _inventoryPlayer = null;
        _inventoryStage = null;
        _inventoryGeneration = null;
        _inventoryRoot = null;
    }

    private void ReleaseInventoryWorldPause()
    {
        _inventoryWorldPause?.Dispose();
        _inventoryWorldPause = null;
    }

    private void NotifyInventoryOpenChanged(bool open)
    {
        if (InventoryOpenChanged == null) return;
        foreach (Action<bool> callback in InventoryOpenChanged.GetInvocationList())
        {
            try { callback(open); }
            catch (Exception error) { Debug.LogException(error, this); }
        }
    }

    private void CleanupInventoryOnShutdown()
    {
        bool wasOpen = IsInventoryOpen;
        if (wasOpen) ClearInventorySession(CanRestoreInventoryCameraInputs(), InventoryContextIsCurrent());
        ReleaseInventoryWorldPause();
        if (wasOpen) NotifyInventoryOpenChanged(false);
    }
}
