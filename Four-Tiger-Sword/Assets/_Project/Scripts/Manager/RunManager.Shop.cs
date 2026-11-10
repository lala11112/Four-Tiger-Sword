using System;
using System.Collections.Generic;
using StudioBaekho;
using Unity.Cinemachine;
using UnityEngine;

public sealed partial class RunManager
{
    public bool IsShopOpen { get; private set; }
    public event Action<bool> ShopOpenChanged;

    private UnityEngine.Object _shopOwner;
    private PlayerController _shopPlayer;
    private StageManager _shopStage;
    private BaekhoDungeonResult _shopGeneration;
    private GameObject _shopRoot;
    private int _shopChapterIndex, _shopStageIndex;
    private readonly List<CinemachineInputAxisController> _shopCameraInputs = new();
    private CombatHitStopService.WorldScaleLease _shopWorldPause;

    /// <summary>Opens a world-pausing shop for this stage without leaving the Playing run state.</summary>
    public bool TryOpenShop(UnityEngine.Object owner)
    {
        if (IsShopOpen) return owner != null && owner == _shopOwner && ShopContextIsCurrent();
        if (!isActiveAndEnabled || !OwnerIsAvailable(owner) || State != RunState.Playing || _transition != null
            || IsMapOpen || IsInventoryOpen || IsRewardOpen || HasPendingItemChoice || IsPauseMenuOpen
            || player == null || !player.isActiveAndEnabled || player.IsStageTransitioning
            || player.IsMapPaused || player.IsInventoryPaused || player.IsPauseMenuPaused
            || player.StatManager == null || player.StatManager.CurrentHp <= 0f
            || stageManager == null || !stageManager.IsReady || stageManager.Result == null
            || stageManager.Result.root == null) return false;

        player.CancelFinisherSelection();
        // Retain the shop's existing transient-stop cleanup; owned slow motion remains independent.
        CombatHitStopService.ClearAll("shop-open");
        var pause = CombatHitStopService.AcquireWorldScale(owner, 0f);
        if (!pause.IsValid) { pause.Dispose(); return false; }
        _shopWorldPause = pause;

        _shopOwner = owner;
        _shopPlayer = player;
        _shopStage = stageManager;
        _shopGeneration = stageManager.Result;
        _shopRoot = _shopGeneration.root;
        _shopChapterIndex = CurrentChapterIndex;
        _shopStageIndex = CurrentStageIndex;
        IsShopOpen = true;
        _shopPlayer.SetShopPaused(true);
        DisableShopCameraInputs();
        RefreshRunCursor();
        NotifyShopOpenChanged(true);
        return IsShopOpen;
    }

    /// <summary>Closes only this modal's resources; never enables player components owned by death or transitions.</summary>
    public void CloseShop()
    {
        if (!IsShopOpen) return;
        if (State == RunState.Playing && _shopPlayer != null && _shopPlayer.StatManager != null
            && _shopPlayer.StatManager.CurrentHp <= 0f)
        {
            Lose();
            return;
        }
        if (!ShopContextIsCurrent())
        {
            SuspendPlayerAndCloseShop();
            RefreshRunCursor();
            return;
        }
        ClearShopSession(true);
        ReleaseShopWorldPause();
        RefreshRunCursor();
        NotifyShopOpenChanged(false);
    }

    private void Update()
    {
        UpdateMapSession();
        UpdateInventorySession();
        UpdateItemChoiceSession();
        if (!IsShopOpen) return;
        if (!ShopContextIsCurrent() || !OwnerIsAvailable(_shopOwner)
            || _shopWorldPause == null || !_shopWorldPause.IsValid) CloseShop();
    }

    private bool ShopContextIsCurrent()
        => State == RunState.Playing && _transition == null && _shopPlayer != null && player == _shopPlayer
            && _shopPlayer.isActiveAndEnabled && !_shopPlayer.IsStageTransitioning
            && _shopPlayer.StatManager != null && _shopPlayer.StatManager.CurrentHp > 0f
            && stageManager != null && stageManager == _shopStage && stageManager.IsReady
            && stageManager.Result == _shopGeneration && _shopRoot != null
            && _shopGeneration.root == _shopRoot && CurrentChapterIndex == _shopChapterIndex
            && CurrentStageIndex == _shopStageIndex;

    private static bool OwnerIsAvailable(UnityEngine.Object owner)
        => owner != null && (!(owner is Behaviour behaviour) || behaviour.isActiveAndEnabled)
            && (!(owner is GameObject host) || host.activeInHierarchy);

    private void DisableShopCameraInputs()
    {
        _shopCameraInputs.Clear();
        foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!ShopCameraTracksPlayer(camera)) continue;
            foreach (var input in camera.GetComponents<CinemachineInputAxisController>())
            {
                if (!input.enabled) continue;
                _shopCameraInputs.Add(input);
                input.enabled = false;
            }
        }
    }

    private bool ShopCameraTracksPlayer(CinemachineCamera camera)
        => ShopCameraTargetsPlayer(camera.Follow) || ShopCameraTargetsPlayer(camera.LookAt);

    private bool ShopCameraTargetsPlayer(Transform target)
        => target != null && _shopPlayer != null
            && (target == _shopPlayer.transform || target.IsChildOf(_shopPlayer.transform));

    private void ClearShopSession(bool restoreCameraInputs)
    {
        IsShopOpen = false;
        if (_shopPlayer != null) _shopPlayer.SetShopPaused(false);
        if (restoreCameraInputs)
            foreach (var input in _shopCameraInputs)
                if (input != null) input.enabled = true;
        _shopCameraInputs.Clear();
        _shopOwner = null;
        _shopPlayer = null;
        _shopStage = null;
        _shopGeneration = null;
        _shopRoot = null;
    }

    private void ReleaseShopWorldPause()
    {
        _shopWorldPause?.Dispose();
        _shopWorldPause = null;
    }

    private void SuspendPlayerAndCloseShop()
    {
        CloseItemChoiceModal();
        bool wasOpen = IsShopOpen;
        bool mapWasOpen = IsMapOpen;
        bool inventoryWasOpen = IsInventoryOpen;
        // Return camera ownership before the transition captures its restoration state.
        // World time remains zero until the player is blocked; no gameplay frame can escape.
        if (mapWasOpen) ClearMapSession(_mapPlayer != null && !_mapPlayer.IsStageTransitioning, false);
        if (inventoryWasOpen) ClearInventorySession(_inventoryPlayer != null && !_inventoryPlayer.IsStageTransitioning, false);
        if (wasOpen) ClearShopSession(_shopPlayer != null && !_shopPlayer.IsStageTransitioning);
        var menuPause = _pauseMenu != null ? _pauseMenu.PrepareForTransition() : null;
        try
        {
            if (player != null) player.BeginStageTransition();
        }
        finally
        {
            menuPause?.Dispose();
            ReleaseMapWorldPause();
            ReleaseInventoryWorldPause();
            ReleaseShopWorldPause();
            if (mapWasOpen) NotifyMapOpenChanged(false);
            if (inventoryWasOpen) NotifyInventoryOpenChanged(false);
            if (wasOpen) NotifyShopOpenChanged(false);
        }
    }

    private void RefreshRunCursor()
    {
        if (player != null && player.IsFinisherSelecting) return;
        bool playing = isActiveAndEnabled && State == RunState.Playing
            && !IsShopOpen && !IsMapOpen && !IsInventoryOpen && !IsRewardOpen && !IsPauseMenuOpen;
        Cursor.visible = !playing;
        Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
    }

    private void NotifyShopOpenChanged(bool open)
    {
        if (ShopOpenChanged == null) return;
        foreach (Action<bool> callback in ShopOpenChanged.GetInvocationList())
        {
            try { callback(open); }
            catch (Exception error) { Debug.LogException(error, this); }
        }
    }

    private void CleanupShopOnShutdown()
    {
        bool wasOpen = IsShopOpen;
        if (wasOpen) ClearShopSession(ShopContextIsCurrent());
        ReleaseShopWorldPause();
        RefreshRunCursor();
        if (wasOpen) NotifyShopOpenChanged(false);
    }

    private void OnEnable()
    {
        RefreshRunCursor();
        if (Application.isPlaying && _hasPending && State == RunState.Preparing && _transition == null)
            BeginPending();
    }
    private void OnDisable()
    {
        if (_pauseMenu != null) _pauseMenu.CloseForHostShutdown();
        CloseItemChoiceModal();
        CancelStagePresentation();
        ReleaseUpgradeSavePause();
        CleanupMapOnShutdown();
        CleanupInventoryOnShutdown();
        CleanupShopOnShutdown();
    }
}
