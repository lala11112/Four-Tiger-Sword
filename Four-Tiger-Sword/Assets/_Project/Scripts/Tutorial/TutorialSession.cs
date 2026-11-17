using System;
using UnityEngine;

/// <summary>Only the second-room retry is resumable in memory; persistent tutorial progress belongs to the profile.</summary>
public sealed class TutorialCheckpoint
{
    public string[] itemIds = Array.Empty<string>();
    public int gold;
}

public static class TutorialSession
{
    public const string SceneName = "Tutorial Scene";
    public static bool IsReplay { get; set; }
    public static bool PendingHubOverview { get; set; }
    public static TutorialCheckpoint PendingCheckpoint { get; set; }
    public static bool RestoreCheckpoint { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        IsReplay = PendingHubOverview = RestoreCheckpoint = false;
        PendingCheckpoint = null;
    }

    public static bool TryBegin(bool requestedReplay, out string error)
    {
        error = null;
        if (!Application.CanStreamedLevelBeLoaded(SceneName))
        { error = "튜토리얼 씬을 불러올 수 없습니다. 빌드 씬 설정을 확인해 주세요."; return false; }
        var account = PlayerUpgradeService.Instance;
        if (!account.IsReady) { error = account.LastError; return false; }
        bool replay = requestedReplay && (account.TutorialGrantClaimed || account.TutorialStatus == TutorialProgressStatus.Completed);
        if (!replay && !account.TryStartTutorial(out error)) return false;
        Reset();
        IsReplay = replay;
        return true;
    }
}
