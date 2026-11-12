using System;
using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;

/// <summary>Permanent rewards belong to a run, independently of the temporary gold wallet.</summary>
public sealed partial class RunManager
{
    private enum UpgradeRewardKind { Stage, Boss, Death, GoldConversion }
    private sealed class PendingUpgradeReward
    {
        public string Id;
        public UpgradeRewardKind Kind;
        public int Amount = -1;
        public int ClearedRooms;
        public PlayerWallet Wallet;
        public int Gold;
    }

    private readonly string _upgradeRunId = Guid.NewGuid().ToString("N");
    private readonly HashSet<(int chapter, int stage)> _queuedUpgradeStages = new();
    private readonly List<PendingUpgradeReward> _pendingUpgradeRewards = new();
    private readonly Dictionary<DungeonRoomEncounter, int> _upgradeRooms = new();
    private readonly RunUpgradeRoomLedger _upgradeRoomLedger = new();
    private GameObject _upgradeRoomRoot;
    private bool _upgradeEndRequested, _upgradeEndWon, _upgradeDeathQueued, _grantingUpgradeRewards;
    private bool _endingUpgradeRun;
    private bool _goldConversionQueued;
    private bool _advanceAfterUpgradeSave, _upgradePlayerWasTransitioning;
    private RunState _upgradeResumeState;
    private string _upgradePreviousError;
    private Vector3 _upgradeResumePosition;
    private Quaternion _upgradeResumeRotation;
    private CombatHitStopService.WorldScaleLease _upgradeSavePause;
    private PlayerUpgradeService _upgradeService;
    private PlayerUpgradeService UpgradeService => _upgradeService ??= PlayerUpgradeService.Instance;

    public bool IsReturningToHub { get; private set; }
    public bool IsUpgradeSaveBlocked { get; private set; }
    public bool HasPendingUpgradeRewards => _pendingUpgradeRewards.Count > 0 || HasPendingSupplyDepotClear;
    public long StageUpgradeRewardTotal { get; private set; }
    public int DeathUpgradeRewardTotal { get; private set; }
    public int GoldBeforeConversion { get; private set; }
    public int GoldConversionRewardTotal { get; private set; }
    public int ClearedCombatRoomCount => _upgradeRoomLedger.TotalClearedRooms;
    public RunResultSummary ResultSummary
    {
        get
        {
            var service = UpgradeService;
            return new RunResultSummary(ProgressLabel, StageUpgradeRewardTotal, DeathUpgradeRewardTotal,
                ClearedCombatRoomCount, service?.Settings != null ? service.Settings.currencyName : "강화석",
                service != null && service.IsReady ? service.Balance : (int?)null,
                GoldBeforeConversion, GoldConversionRewardTotal);
        }
    }
    public string UpgradeRewardSummary
    {
        get
        {
            var service = UpgradeService;
            string currency = service != null && service.Settings != null ? service.Settings.currencyName : "강화석";
            string balance = service != null && service.IsReady ? service.Balance.ToString("N0") : "확인 필요";
            return $"스테이지 보상  {StageUpgradeRewardTotal:N0} {currency}\n"
                + $"사망 보상  {DeathUpgradeRewardTotal:N0} {currency} (전투 방 {ClearedCombatRoomCount}개)\n"
                + $"금화 환전  {GoldConversionRewardTotal:N0} {currency} ({GoldBeforeConversion:N0} 금화)\n"
                + $"보유 {currency}  {balance}";
        }
    }

    private bool EnsureUpgradeProfile()
    {
        if (TrySaveUpgradeRewards(out string error)) return true;
        BlockUpgradeSaving(error);
        return false;
    }

    private void OnUpgradeStageClear()
    {
        if (State != RunState.Playing || stageManager == null || !stageManager.IsReady
            || !stageManager.IsCleared || definition == null || CurrentChapterIndex < 0 || CurrentStageIndex < 0
            || !_queuedUpgradeStages.Add((CurrentChapterIndex, CurrentStageIndex))) return;
        bool boss = CurrentStageIndex == definition.chapters[CurrentChapterIndex].stages.Length - 1;
        _pendingUpgradeRewards.Add(new PendingUpgradeReward
        {
            Id = _upgradeRunId + ":stage:" + CurrentChapterIndex + ":" + CurrentStageIndex,
            Kind = boss ? UpgradeRewardKind.Boss : UpgradeRewardKind.Stage
        });
        if (!TrySaveUpgradeRewards(out string error)) BlockUpgradeSaving(error);
    }

    private bool SettleUpgradeRunEnd(bool won)
    {
        _upgradeEndRequested = true;
        _upgradeEndWon = won;
        _supplyDepotClearRequested = _appliedSupplyDepotTransactionId != null;
        if (!_goldConversionQueued)
        {
            _goldConversionQueued = true;
            var wallet = player != null ? player.GetComponent<PlayerWallet>() : null;
            wallet?.FinishPendingRoomGold();
            GoldBeforeConversion = wallet != null && wallet.IsInitialized ? wallet.Gold : 0;
            if (GoldBeforeConversion > 0)
                _pendingUpgradeRewards.Add(new PendingUpgradeReward
                {
                    Id = _upgradeRunId + ":gold-conversion", Kind = UpgradeRewardKind.GoldConversion,
                    Wallet = wallet, Gold = GoldBeforeConversion
                });
        }
        if (!won && !_upgradeDeathQueued)
        {
            _upgradeDeathQueued = true;
            _pendingUpgradeRewards.Add(new PendingUpgradeReward
            {
                Id = _upgradeRunId + ":death",
                Kind = UpgradeRewardKind.Death,
                ClearedRooms = ClearedCombatRoomCount
            });
        }
        if (TrySaveUpgradeRewards(out string error))
        {
            IsUpgradeSaveBlocked = false;
            ReleaseUpgradeSavePause();
            return true;
        }
        BlockUpgradeSaving(error);
        return false;
    }

    private bool TrySaveUpgradeRewards(out string error)
    {
        error = null;
        if (_grantingUpgradeRewards) { error = "보상을 저장하고 있습니다."; return false; }
        var service = UpgradeService;
        if (service == null || !service.IsReady || service.Settings == null)
        {
            error = service != null && !string.IsNullOrEmpty(service.LastError)
                ? service.LastError : "강화 데이터를 불러오지 못했습니다.";
            return false;
        }
        if (!service.Settings.Validate(out error)) return false;
        _grantingUpgradeRewards = true;
        try
        {
            if (!TryClearAppliedSupplyDepotReceipt(out error)) return false;
            while (_pendingUpgradeRewards.Count > 0)
            {
                var reward = _pendingUpgradeRewards[0];
                if (reward.Amount < 0)
                    reward.Amount = reward.Kind == UpgradeRewardKind.Stage ? service.Settings.stageClearReward
                        : reward.Kind == UpgradeRewardKind.Boss ? service.Settings.bossClearReward
                        : reward.Kind == UpgradeRewardKind.GoldConversion ? reward.Gold / service.Settings.goldPerUpgradeStone
                        : (int)Math.Min(service.Settings.deathRewardCap,
                            (long)reward.ClearedRooms * service.Settings.deathRewardPerRoom);
                if (reward.Kind == UpgradeRewardKind.GoldConversion)
                {
                    string saveError = null;
                    // Keep the wallet locked through persistence and its observers. A failed write spends nothing.
                    if (reward.Wallet == null || !reward.Wallet.TryPurchase(reward.Gold,
                        () => service.TryGrantReward(reward.Id, reward.Amount, out saveError)))
                    { error = saveError ?? "금화 환전을 완료하지 못했습니다. 다시 시도해 주세요."; return false; }
                }
                else if (!service.TryGrantReward(reward.Id, reward.Amount, out error)) return false;
                // Publish totals only after persistence accepted this exact receipt.
                _pendingUpgradeRewards.RemoveAt(0);
                if (reward.Kind == UpgradeRewardKind.Death) DeathUpgradeRewardTotal += reward.Amount;
                else if (reward.Kind == UpgradeRewardKind.GoldConversion) GoldConversionRewardTotal += reward.Amount;
                else StageUpgradeRewardTotal += reward.Amount;
            }
            return true;
        }
        finally { _grantingUpgradeRewards = false; }
    }

    private void BlockUpgradeSaving(string error)
    {
        if (!IsUpgradeSaveBlocked)
        {
            _upgradeResumeState = State;
            _upgradePreviousError = LastError;
            _upgradePlayerWasTransitioning = player == null || player.IsStageTransitioning;
            if (player != null)
            {
                _upgradeResumePosition = player.transform.position;
                _upgradeResumeRotation = player.transform.rotation;
            }
            IsUpgradeSaveBlocked = true;
            SuspendPlayerAndCloseShop();
            _upgradeSavePause = CombatHitStopService.AcquireWorldScale(this, 0f);
            if (State != RunState.Won && State != RunState.Lost) SetState(RunState.Error);
        }
        LastError = error ?? "강화 데이터를 저장하지 못했습니다.";
        if (!IsDeathPresentationPlaying) resultUI?.ShowRewardSaveError(LastError);
    }

    public void TryRetryPendingRewards()
    {
        if (!IsUpgradeSaveBlocked || _grantingUpgradeRewards || _endingUpgradeRun || IsDeathPresentationPlaying) return;
        var service = UpgradeService;
        string error = null;
        if (service == null || !service.TryReload(out error) || !TrySaveUpgradeRewards(out error))
        {
            BlockUpgradeSaving(error ?? "강화 데이터를 불러오지 못했습니다.");
            return;
        }
        IsUpgradeSaveBlocked = false;
        LastError = null;
        ReleaseUpgradeSavePause();
        if (_upgradeEndRequested)
        {
            resultUI?.ShowRunResult(_upgradeEndWon);
            return;
        }
        if (_upgradeResumeState == RunState.Playing)
        {
            if (!_upgradePlayerWasTransitioning && player != null)
                player.CompleteStageTransition(_upgradeResumePosition, _upgradeResumeRotation);
            SetState(RunState.Playing);
            resultUI?.ShowStage(ProgressLabel);
            if (_advanceAfterUpgradeSave)
            {
                _advanceAfterUpgradeSave = false;
                Advance();
            }
        }
        else if (_hasPending && _upgradeResumeState != RunState.Error)
        {
            SetState(RunState.Preparing);
            BeginPending();
        }
        else
        {
            LastError = _upgradePreviousError;
            SetState(RunState.Error);
            resultUI?.ShowError(LastError ?? "구역을 다시 준비해 주세요.");
        }
    }

    private bool CanLeaveUpgradeRewards()
    {
        // A save/terminal callback cannot navigate before the enclosing operation has committed its full settlement.
        if (_grantingUpgradeRewards || _endingUpgradeRun || IsDeathPresentationPlaying
            || IsReturningToHub || HubReturnTransition.IsActive) return false;
        if (IsUpgradeSaveBlocked) { resultUI?.ShowRewardSaveError(LastError); return false; }
        if (TrySaveUpgradeRewards(out string error)) return true;
        BlockUpgradeSaving(error);
        return false;
    }

    public void ReturnToHub()
    {
        if (IsReturningToHub || HubReturnTransition.IsActive) return;
        if (State != RunState.Won && State != RunState.Lost && State != RunState.Error) return;
        if (!CanLeaveUpgradeRewards()) return;
        if (!Application.isPlaying || !Application.CanStreamedLevelBeLoaded("Hub Scene"))
        {
            RestoreHubReturn("거점 씬을 불러올 수 없습니다. 빌드 씬 설정을 확인해 주세요.");
            return;
        }
        if (!TrySaveSupplyDepotExit()) return;
        IsReturningToHub = true;
        resultUI?.SetHubReturnInProgress(true);
        if (!HubReturnTransition.TryStart(resultUI != null ? resultUI.PresentationOverlay : null, RestoreHubReturn))
        {
            if (IsReturningToHub) RestoreHubReturn("The Hub transition could not start.");
            return;
        }
        // The independent loading canvas covers the source. Keep its result/backdrop for load-failure recovery.
        // Scene destruction will release the source run's presentation and pause ownership.
        SuspendPlayerAndCloseShop();
        CombatHitStopService.SetExternalWorldTimeScale(1f);
    }

    private void RestoreHubReturn(string error)
    {
        if (this == null) return;
        IsReturningToHub = false;
        resultUI?.ShowHubReturnError("거점으로 이동하지 못했습니다. 다시 시도해 주세요.");
        Debug.LogWarning("[Run] Hub return failed: " + error, this);
    }

    private void BindUpgradeRooms()
    {
        UnbindUpgradeRooms();
        var result = stageManager != null ? stageManager.Result : null;
        if (result == null || result.root == null) return;
        if (_upgradeRoomRoot != result.root)
        {
            _upgradeRoomRoot = result.root;
            _rewardChoiceReadyAt = 0f;
            _upgradeRoomLedger.BeginGeneration(CurrentChapterIndex, CurrentStageIndex);
            _itemRewardLedger.BeginGeneration(CurrentChapterIndex, CurrentStageIndex);
        }
        foreach (var module in result.rooms)
        {
            if (module == null) continue;
            var identity = module.GetComponent<BaekhoGeneratedRoom>();
            var room = module.GetComponent<DungeonRoomEncounter>();
            if (identity == null || room == null || (identity.role & BaekhoRoomRole.Shop) != 0) continue;
            _upgradeRooms[room] = identity.instanceId;
            room.Cleared += OnUpgradeCombatRoomCleared;
        }
    }

    private void OnUpgradeCombatRoomCleared(DungeonRoomEncounter room)
    {
        // A completed encounter's callback can arrive after another observer pauses for a save failure.
        // Preserve that committed clear while still rejecting terminal and unrelated error states.
        bool acceptingClears = State == RunState.Playing || State == RunState.Error
            && IsUpgradeSaveBlocked && _upgradeResumeState == RunState.Playing;
        if (!acceptingClears || room == null || !room.WasCombatClearedByDefeat
            || stageManager == null || !stageManager.IsReady || stageManager.Result == null
            || stageManager.Result.root != _upgradeRoomRoot || _upgradeRoomRoot == null
            || !room.transform.IsChildOf(_upgradeRoomRoot.transform) || !_upgradeRooms.TryGetValue(room, out int id)) return;
        bool firstClear = _upgradeRoomLedger.RecordClearedRoom(id);
        var identity = room.GetComponent<BaekhoGeneratedRoom>();
        if (identity != null && identity.role == BaekhoRoomRole.Normal
            && _itemRewardLedger.TryRecord(id, out string receipt))
            DungeonRewardChest.Spawn(this, room, receipt, stageManager.Encounters?.RewardChestPrefab);
        // Reserve the receipt before healing can notify observers or change composition readiness.
        if (firstClear && player != null)
        {
            if (player.TryGetComponent(out PlayerWallet wallet)) wallet.AwardCombatRoomGold();
            player.Engravings?.NotifyRoomCleared();
        }
    }

    private void UnbindUpgradeRooms()
    {
        foreach (var pair in _upgradeRooms) if (pair.Key != null) pair.Key.Cleared -= OnUpgradeCombatRoomCleared;
        _upgradeRooms.Clear();
    }

    private void ReleaseUpgradeSavePause()
    {
        _upgradeSavePause?.Dispose();
        _upgradeSavePause = null;
    }

    private void CleanupUpgradeRewards()
    {
        UnbindUpgradeRooms();
        ReleaseUpgradeSavePause();
    }
}

/// <summary>Retries retain the best completed-room count for each stage instead of adding regenerated rooms.</summary>
internal sealed class RunUpgradeRoomLedger
{
    private readonly Dictionary<(int chapter, int stage), int> _stageMaxima = new();
    private readonly HashSet<int> _generationRooms = new();
    private (int chapter, int stage) _stage;
    public int TotalClearedRooms { get; private set; }

    public void BeginGeneration(int chapter, int stage)
    {
        _stage = (chapter, stage);
        _generationRooms.Clear();
    }

    public bool RecordClearedRoom(int instanceId)
    {
        if (!_generationRooms.Add(instanceId)) return false;
        _stageMaxima.TryGetValue(_stage, out int previous);
        if (_generationRooms.Count > previous)
        {
            TotalClearedRooms += _generationRooms.Count - previous;
            _stageMaxima[_stage] = _generationRooms.Count;
        }
        return true;
    }
}
