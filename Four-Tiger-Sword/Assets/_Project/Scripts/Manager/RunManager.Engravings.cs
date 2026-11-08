using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public sealed partial class RunManager
{
    public RunItemChoiceService ItemChoices { get; private set; }
    public bool IsRewardOpen { get; private set; }
    public bool HasPendingItemChoice => _itemRewardInbox.Count > 0 || ItemChoices?.Current != null;
    private bool _engravingsInitialized;
    private EngravingSnapshot _runEngravings;
    private RunItemChoiceView _itemChoiceView;
    private CombatHitStopService.WorldScaleLease _rewardPause;
    private readonly List<CinemachineInputAxisController> _rewardCameraInputs = new();
    private readonly RunItemRewardLedger _itemRewardLedger = new();
    private readonly RunItemRewardInbox _itemRewardInbox = new();

    private void EnsureRunEngravings()
    {
        if (_engravingsInitialized || player == null || UpgradeService?.IsReady != true) return;
        _runEngravings = UpgradeService.CreateEngravingSnapshot();
        PlayerEngravingRuntime.Ensure(player).InitializeRun(_runEngravings);
        _engravingsInitialized = true;
    }
    private void EnsureRunItemChoices()
    {
        if (ItemChoices != null) { FlushItemRewardReceipts(); return; }
        if (!_engravingsInitialized || player == null) return;
        var wallet = player.GetComponent<PlayerWallet>();
        var items = player.GetComponent<PlayerItemRuntime>();
        if (wallet == null || !wallet.IsInitialized || items == null || !items.IsInitialized || items.Catalog == null) return;
        ItemChoices = new RunItemChoiceService(items, wallet, _runEngravings, RunSeed);
        _itemChoiceView = RunItemChoiceView.Ensure(this, ItemChoices);
        ItemChoices.BeginStarterChoice();
        FlushItemRewardReceipts();
    }
    private void FlushItemRewardReceipts()
    {
        if (ItemChoices != null) _itemRewardInbox.Flush(ItemChoices.BeginRoomReward);
    }
    private void UpdateItemChoiceSession()
    {
        FlushItemRewardReceipts();
        bool hasPresentableChoice = ItemChoices?.Current != null;
        bool playable = isActiveAndEnabled && State == RunState.Playing && _transition == null && !IsUpgradeSaveBlocked
            && player != null && player.isActiveAndEnabled && !player.IsStageTransitioning
            && player.StatManager != null && player.StatManager.CurrentHp > 0f && stageManager != null && stageManager.IsReady;
        if (IsRewardOpen)
        {
            if (!playable || !hasPresentableChoice || _rewardPause == null || !_rewardPause.IsValid)
                CloseItemChoiceModal();
            return;
        }
        if (!playable || !hasPresentableChoice || IsMapOpen || IsInventoryOpen || IsShopOpen
            || player.IsGameplayModalPaused || player.IsFinisherSelecting) return;
        var pause = CombatHitStopService.AcquireWorldScale(this, 0f);
        if (!pause.IsValid) { pause.Dispose(); return; }
        _rewardPause = pause;
        IsRewardOpen = true;
        player.SetRewardPaused(true);
        foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!RewardCameraTracks(camera.Follow) && !RewardCameraTracks(camera.LookAt)) continue;
            foreach (var input in camera.GetComponents<CinemachineInputAxisController>())
                if (input.enabled) { _rewardCameraInputs.Add(input); input.enabled = false; }
        }
        _itemChoiceView?.Show();
        RefreshRunCursor();
    }
    private bool RewardCameraTracks(Transform target)
        => target != null && player != null && (target == player.transform || target.IsChildOf(player.transform));
    private void CloseItemChoiceModal()
    {
        _itemChoiceView?.Hide();
        if (IsRewardOpen)
        {
            IsRewardOpen = false;
            player?.SetRewardPaused(false);
            if (player != null && player.isActiveAndEnabled && !player.IsStageTransitioning
                && player.StatManager != null && player.StatManager.CurrentHp > 0f)
                foreach (var input in _rewardCameraInputs) if (input != null) input.enabled = true;
        }
        _rewardCameraInputs.Clear();
        _rewardPause?.Dispose(); _rewardPause = null;
        RefreshRunCursor();
    }
}
