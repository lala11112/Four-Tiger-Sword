using System;
using System.Collections.Generic;
using StudioBaekho;
using Unity.Cinemachine;
using UnityEngine;

public sealed partial class RunManager
{
    public bool IsMapOpen { get; private set; }
    public bool IsMapSessionCurrent => IsMapOpen && MapContextIsCurrent() && OwnerIsAvailable(_mapOwner)
        && _mapWorldPause != null && _mapWorldPause.IsValid;
    public event Action<bool> MapOpenChanged;

    private UnityEngine.Object _mapOwner;
    private PlayerController _mapPlayer;
    private StageManager _mapStage;
    private BaekhoDungeonResult _mapGeneration;
    private GameObject _mapRoot;
    private int _mapChapterIndex, _mapStageIndex;
    private readonly List<CinemachineInputAxisController> _mapCameraInputs = new();
    private CombatHitStopService.WorldScaleLease _mapWorldPause;
    private bool _mapOwnsCursor, _mapCursorVisible;
    private CursorLockMode _mapCursorLock;

    /// <summary>Pauses this live stage for a view-only map, including an active encounter.</summary>
    public bool TryOpenMap(UnityEngine.Object owner)
    {
        if (IsMapOpen) return owner != null && owner == _mapOwner && MapContextIsCurrent()
            && _mapWorldPause != null && _mapWorldPause.IsValid;
        if (!isActiveAndEnabled || !OwnerIsAvailable(owner) || State != RunState.Playing || _transition != null
            || IsShopOpen || player == null || !player.isActiveAndEnabled || player.IsStageTransitioning
            || player.IsGameplayModalPaused || player.IsFinisherSelecting || player.StatManager == null
            || player.StatManager.CurrentHp <= 0f || stageManager == null || !stageManager.IsReady
            || stageManager.Result == null || !stageManager.Result.success || stageManager.Result.root == null
            || !stageManager.Result.root.activeInHierarchy) return false;

        // Keep combat stops and other owned slow motion. Disposing this lease releases only the map pause.
        var pause = CombatHitStopService.AcquireWorldScale(owner, 0f);
        if (!pause.IsValid) { pause.Dispose(); return false; }
        _mapWorldPause = pause;
        _mapOwner = owner;
        _mapPlayer = player;
        _mapStage = stageManager;
        _mapGeneration = stageManager.Result;
        _mapRoot = _mapGeneration.root;
        _mapChapterIndex = CurrentChapterIndex;
        _mapStageIndex = CurrentStageIndex;
        _mapCursorVisible = Cursor.visible;
        _mapCursorLock = Cursor.lockState;
        _mapOwnsCursor = true;
        IsMapOpen = true;
        _mapPlayer.SetMapPaused(true);
        DisableMapCameraInputs();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        NotifyMapOpenChanged(true);
        return IsMapOpen;
    }

    /// <summary>Releases the map's input, camera, cursor and time ownership without restarting actions.</summary>
    public void CloseMap()
    {
        if (!IsMapOpen) return;
        if (State == RunState.Playing && _mapPlayer != null && _mapPlayer.StatManager != null
            && _mapPlayer.StatManager.CurrentHp <= 0f)
        { Lose(); return; }
        bool restoreCursor = isActiveAndEnabled && MapContextIsCurrent();
        ClearMapSession(CanRestoreMapCameraInputs(), restoreCursor);
        ReleaseMapWorldPause();
        NotifyMapOpenChanged(false);
    }

    private void UpdateMapSession()
    {
        if (!IsMapOpen) return;
        if (!MapContextIsCurrent() || !OwnerIsAvailable(_mapOwner) || _mapWorldPause == null || !_mapWorldPause.IsValid)
            CloseMap();
    }

    private bool MapContextIsCurrent()
        => isActiveAndEnabled && State == RunState.Playing && _transition == null && !IsShopOpen
            && _mapPlayer != null && player == _mapPlayer && _mapPlayer.isActiveAndEnabled
            && _mapPlayer.IsMapPaused && !_mapPlayer.IsShopPaused && !_mapPlayer.IsStageTransitioning && !_mapPlayer.IsFinisherSelecting
            && _mapPlayer.StatManager != null && _mapPlayer.StatManager.CurrentHp > 0f
            && stageManager != null && stageManager == _mapStage && stageManager.IsReady
            && stageManager.Result == _mapGeneration && _mapRoot != null && _mapRoot.activeInHierarchy && _mapGeneration.root == _mapRoot
            && CurrentChapterIndex == _mapChapterIndex && CurrentStageIndex == _mapStageIndex;

    private bool CanRestoreMapCameraInputs()
        => State == RunState.Playing && _mapPlayer != null && player == _mapPlayer
            && !_mapPlayer.IsStageTransitioning && _mapPlayer.StatManager != null && _mapPlayer.StatManager.CurrentHp > 0f;

    private void DisableMapCameraInputs()
    {
        _mapCameraInputs.Clear();
        foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!MapCameraTargetsPlayer(camera.Follow) && !MapCameraTargetsPlayer(camera.LookAt)) continue;
            foreach (var input in camera.GetComponents<CinemachineInputAxisController>())
            {
                if (!input.enabled) continue;
                _mapCameraInputs.Add(input);
                input.enabled = false;
            }
        }
    }

    private bool MapCameraTargetsPlayer(Transform target)
        => target != null && _mapPlayer != null && (target == _mapPlayer.transform || target.IsChildOf(_mapPlayer.transform));

    private void ClearMapSession(bool restoreCameraInputs, bool restoreCursor)
    {
        IsMapOpen = false;
        if (_mapPlayer != null) _mapPlayer.SetMapPaused(false);
        if (restoreCameraInputs)
            foreach (var input in _mapCameraInputs)
                if (input != null) input.enabled = true;
        _mapCameraInputs.Clear();
        // Another modal can take cursor ownership while the map is being closed.
        if (_mapOwnsCursor && restoreCursor && Cursor.visible && Cursor.lockState == CursorLockMode.None)
        { Cursor.visible = _mapCursorVisible; Cursor.lockState = _mapCursorLock; }
        _mapOwnsCursor = false;
        _mapOwner = null;
        _mapPlayer = null;
        _mapStage = null;
        _mapGeneration = null;
        _mapRoot = null;
    }

    private void ReleaseMapWorldPause()
    {
        _mapWorldPause?.Dispose();
        _mapWorldPause = null;
    }

    private void NotifyMapOpenChanged(bool open)
    {
        if (MapOpenChanged == null) return;
        foreach (Action<bool> callback in MapOpenChanged.GetInvocationList())
        {
            try { callback(open); }
            catch (Exception error) { Debug.LogException(error, this); }
        }
    }

    private void CleanupMapOnShutdown()
    {
        bool wasOpen = IsMapOpen;
        if (wasOpen) ClearMapSession(CanRestoreMapCameraInputs(), MapContextIsCurrent());
        ReleaseMapWorldPause();
        if (wasOpen) NotifyMapOpenChanged(false);
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) CleanupMapOnShutdown();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) CleanupMapOnShutdown();
    }
}
