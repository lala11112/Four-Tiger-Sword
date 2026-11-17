using System;
using StudioBaekho;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class RunManager
{
    public bool IsTutorial => definition != null && definition.mode == RunMode.Tutorial;
    public bool CanRetryTutorialCheckpoint => IsTutorial && State == RunState.Lost && TutorialSession.PendingCheckpoint != null;
    public event Action<int> FastTravelCompleted;
    private bool _tutorialCompletionPending;

    internal void NotifyFastTravelCompleted(int roomId) => FastTravelCompleted?.Invoke(roomId);
    internal void ReportTutorialFailure(string error) => OnStageFailure(error);

    private bool EnsureTutorialLaunch()
    {
        if (!IsTutorial || TutorialSession.IsReplay) return true;
        var service = UpgradeService;
        if (service.TutorialGrantClaimed && service.TutorialStatus != TutorialProgressStatus.InMap)
        { TutorialSession.IsReplay = true; return true; }
        if (service.TryStartTutorial(out string error)) return true;
        ShowError(error);
        return false;
    }

    private bool PrepareTutorialPlacement(ref Vector3 position, ref Quaternion rotation, out string error)
    {
        error = null;
        if (!IsTutorial || !TutorialSession.RestoreCheckpoint || TutorialSession.PendingCheckpoint == null) return true;
        var tutorial = GetComponent<TutorialDirector>();
        var data = tutorial != null ? tutorial.definition : null;
        if (data == null) { error = "튜토리얼 설정이 없습니다."; return false; }
        BaekhoMapModule target = null;
        foreach (var room in stageManager.Result.rooms)
            if (room.GetComponent<BaekhoGeneratedRoom>()?.instanceId == data.freeCombatRoomId) target = room;
        if (target == null) { error = "재도전할 전투방이 없습니다."; return false; }
        var checkpoint = TutorialSession.PendingCheckpoint;
        var items = player.GetComponent<PlayerItemRuntime>();
        var wallet = player.GetComponent<PlayerWallet>();
        if (items == null || wallet == null || !items.IsInitialized || !wallet.IsInitialized)
        { error = "재도전 아이템과 골드를 준비하지 못했습니다."; return false; }
        var existing = new System.Collections.Generic.List<string>();
        foreach (var item in items.OwnedItems) existing.Add(item.itemId);
        foreach (string id in existing) if (!items.TryRemoveItem(id))
        { error = "연습 아이템을 복원하지 못했습니다."; return false; }
        foreach (string id in checkpoint.itemIds) if (!items.TryAddItem(id))
        { error = "재도전 아이템을 복원하지 못했습니다: " + id; return false; }
        if (wallet.Gold > checkpoint.gold) wallet.TrySpend(wallet.Gold - checkpoint.gold);
        else if (wallet.Gold < checkpoint.gold) wallet.AddGold(checkpoint.gold - wallet.Gold);
        player.StatManager.Heal(player.StatManager.MaxHp);
        var marker = new GameObject("TutorialRetrySpawn").transform;
        marker.SetParent(target.transform, false);
        marker.localPosition = data.retrySpawnLocal;
        try
        {
            return DungeonFastTravelLanding.TryResolve(target, marker, player.Controller, out position, out rotation, out error);
        }
        finally { Destroy(marker.gameObject); }
    }

    public void RetryTutorialCheckpoint()
    {
        if (!CanRetryTutorialCheckpoint || !CanLeaveUpgradeRewards()) return;
        TutorialSession.RestoreCheckpoint = true;
        RestartRun();
    }

    public void SkipTutorial()
    {
        if (!IsTutorial || IsReturningToHub || HubReturnTransition.IsActive || IsDeathPresentationPlaying) return;
        if (!TutorialSession.IsReplay && !UpgradeService.TrySkipTutorial(out string error))
        { GetComponent<TutorialDirector>()?.ShowSaveFailure(error); return; }
        TutorialSession.Reset();
        CancelStagePresentation();
        SuspendPlayerAndCloseShop();
        stageManager?.Suspend();
        SetState(RunState.Won);
        resultUI?.ShowTutorialExit();
        ReturnToHub();
    }
}
