using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Checks the slot catalog and profiles on an isolated temporary filesystem.</summary>
public static class SaveSlotRegression
{
    private const string TemporaryPrefix = "FourTigerSword.SaveSlots.QA.";

    [MenuItem("Tools/Studio Baekho/Progression/Run Save Slot Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Run filesystem slot regressions in Edit Mode.");
        try
        {
            CapacityNamesAndIndependentProgress();
            LegacyBackupMigratesOnce();
            DamagedSlotsRemainOccupied();
            IoFailurePreservesExistingSlots();
            LastPlayedSlotSurvivesReload();
            LastPlayedWriteFailurePreservesSelection();
        }
        finally { TutorialSession.Reset(); }
        return "PASS: save slot names, capacity, selection, isolated progression, GUID reuse, legacy backup migration, corrupt saves, I/O recovery, and last played slot persistence.";
    }

    private static void CapacityNamesAndIndependentProgress()
    {
        using var fixture = new Fixture();
        var slots = fixture.Service;
        using var scope = SaveSlotService.OverrideForTesting(slots);
        Check(slots.TryRefresh(out var error) && slots.Slots.Count == 0 && slots.SelectedAccount == null,
            "An empty filesystem did not start with five available positions: " + error);
        Check(slots.TryCreate(1, string.Empty, out var first, out error)
            && first.Number == 1 && !string.IsNullOrWhiteSpace(first.Name) && first.IsReadable,
            "Empty slot name did not produce a usable default name: " + error);
        Check(slots.TryCreate(2, "  실험실  ", out var second, out error)
            && second.Number == 2 && second.Name == "실험실" && second.Id != first.Id,
            "Slot name was not trimmed, or slot identity was reused: " + error);
        Check(!slots.TryCreate(3, new string('N', SaveSlotService.MaxNameLength + 1), out _, out _)
            && !slots.TryCreate(3, "Two\nlines", out _, out _), "The slot accepted an invalid name.");
        for (int number = 3; number <= 5; number++)
            Check(slots.TryCreate(number, null, out _, out error), "Could not create available slot " + number + ": " + error);
        Check(slots.Slots.Count == 5 && !slots.TryCreate(2, "Already taken", out _, out _)
            && !slots.TryCreate(0, "Invalid", out _, out _) && !slots.TryCreate(6, "Too many", out _, out _),
            "The catalog accepted an occupied or out-of-range position.");

        Check(slots.TrySelect(first.Id, out var firstAccount, out error), "Could not select first slot: " + error);
        Check(slots.SelectedSlotId == first.Id && ReferenceEquals(firstAccount, slots.SelectedAccount)
            && ReferenceEquals(firstAccount, PlayerUpgradeService.Instance),
            "The selected slot was not bound as the active progression account.");
        Check(firstAccount.TryGrantReward("same-receipt", 180, out error), "Could not fund first slot: " + error);
        Check(firstAccount.TryUpgrade(StatType.ST_ATK, out error) && firstAccount.TryUnlockNode("atk", out error)
            && firstAccount.TryPurchaseSupplyDepot("I01", 100, out var receipt, out error) && receipt != null
            && firstAccount.TryStartTutorial(out error) && firstAccount.TryCompleteTutorialMap(120, out error),
            "Could not persist the first slot's independent progress: " + error);
        int firstBalance = firstAccount.Balance;

        var previousCheckpoint = SeedTutorialSession();
        Check(!slots.TrySelect("missing-slot", out _, out _) && slots.SelectedSlotId == first.Id
            && ReferenceEquals(TutorialSession.PendingCheckpoint, previousCheckpoint) && TutorialSession.IsReplay,
            "A failed selection reset the current slot's tutorial session.");
        Check(slots.TrySelect(first.Id, out _, out error) && ReferenceEquals(TutorialSession.PendingCheckpoint, previousCheckpoint)
            && TutorialSession.IsReplay, "Reselecting the same slot discarded its tutorial session: " + error);
        Check(slots.TrySelect(second.Id, out var secondAccount, out error)
            && secondAccount.Balance == 0 && secondAccount.CurrentSupplyDepotReceipt == null
            && secondAccount.GetLevel(StatType.ST_ATK) == 0 && !secondAccount.IsNodeUnlocked("atk")
            && secondAccount.TutorialStatus == TutorialProgressStatus.NotStarted,
            "A newly selected slot inherited another slot's progress: " + error);
        Check(TutorialSessionWasCleared(), "Selecting a different slot carried over its predecessor's tutorial session.");
        Check(secondAccount.TryGrantReward("same-receipt", 23, out error)
            && secondAccount.TrySkipTutorial(out error), "The second slot could not make its own commits: " + error);

        var reopened = fixture.Reopen();
        Check(reopened.TryRefresh(out error) && reopened.Slots.Count == 5, "Five slots did not survive a service reload: " + error);
        var summaries = reopened.Slots.ToDictionary(slot => slot.Id);
        Check(summaries[first.Id].Balance == firstBalance
            && summaries[first.Id].TutorialStatus == TutorialProgressStatus.HubLessons
            && summaries[second.Id].Balance == 23 && summaries[second.Id].TutorialStatus == TutorialProgressStatus.Skipped,
            "The slot summaries did not reflect their distinct saved records.");
        Check(reopened.TrySelect(first.Id, out var loadedFirst, out error)
            && loadedFirst.GetLevel(StatType.ST_ATK) == 1 && loadedFirst.IsNodeUnlocked("atk")
            && loadedFirst.CurrentSupplyDepotReceipt?.itemId == "I01",
            "Reloading the first slot did not restore upgrade, engraving and depot data: " + error);

        SeedTutorialSession();
        Check(reopened.TryDelete(first.Id, out error), "Could not delete selected slot: " + error);
        Check(reopened.SelectedAccount == null && string.IsNullOrEmpty(reopened.SelectedSlotId)
            && !reopened.TrySelect(first.Id, out _, out _) && TutorialSessionWasCleared(),
            "Deleting the selected slot kept its account or tutorial session bound.");
        Check(reopened.TryCreate(1, null, out var replacement, out error)
            && replacement.Id != first.Id && replacement.Balance == 0
            && replacement.TutorialStatus == TutorialProgressStatus.NotStarted,
            "Reusing a slot number resurrected deleted progress: " + error);
        Check(!loadedFirst.TryGrantReward("stale-account", 1, out _)
            && !Directory.Exists(Path.GetDirectoryName(ProfilePath(fixture.Root, first))),
            "A stale account recreated the deleted GUID under the reused position.");
    }

    private static void LegacyBackupMigratesOnce()
    {
        using var fixture = new Fixture();
        string legacy = Path.Combine(fixture.Root, "player-upgrades.json");
        const string broken = "{truncated legacy file";
        File.WriteAllText(legacy, broken);
        var previous = PlayerUpgradeProfile.New();
        previous.currency = 71;
        previous.attackLevel = 2;
        previous.unlockedNodeIds = new[] { "atk" };
        previous.supplyDepotItemId = "I01";
        previous.supplyDepotTransactionId = "previous-expedition";
        previous.tutorialStatus = TutorialProgressStatus.InMap;
        Check(new PlayerUpgradeFileStore(legacy + ".bak").TrySave(previous, out var error),
            "Could not set up isolated legacy backup: " + error);
        byte[] backup = File.ReadAllBytes(legacy + ".bak");

        var migrated = fixture.Reopen();
        Check(migrated.TryRefresh(out error) && migrated.Slots.Count == 1, "Legacy backup did not import: " + error);
        var slot = migrated.Slots.Single();
        Check(slot.Number == 1 && slot.Name == "기존 데이터" && slot.IsReadable && slot.Balance == previous.currency
            && slot.TutorialStatus == TutorialProgressStatus.InMap, "Legacy slot summary lost data.");
        Check(migrated.TrySelect(slot.Id, out var account, out error) && account.GetLevel(StatType.ST_ATK) == 2
            && account.IsNodeUnlocked("atk") && account.CurrentSupplyDepotReceipt?.transactionId == "previous-expedition",
            "Migration lost permanent progression or an outstanding receipt: " + error);
        Check(File.ReadAllText(legacy) == broken && File.ReadAllBytes(legacy + ".bak").SequenceEqual(backup),
            "Import rewrote the source legacy account.");

        var reopened = fixture.Reopen();
        Check(reopened.TryRefresh(out error) && reopened.Slots.Single().Id == slot.Id,
            "Reopening the catalog imported a duplicate legacy slot: " + error);
        Check(reopened.TryDelete(slot.Id, out error), "Could not delete imported slot: " + error);
        var afterDeletion = fixture.Reopen();
        Check(afterDeletion.TryRefresh(out error) && afterDeletion.Slots.Count == 0,
            "Deleted legacy progress reappeared during catalog refresh: " + error);
    }

    private static void DamagedSlotsRemainOccupied()
    {
        using var fixture = new Fixture();
        var slots = fixture.Service;
        Check(slots.TryCreate(1, "Corrupt", out var corrupt, out var error), "Could not create corrupt-file fixture: " + error);
        Check(slots.TryCreate(2, "Missing", out var missing, out error), "Could not create missing-file fixture: " + error);
        string corruptPath = ProfilePath(fixture.Root, corrupt);
        string missingPath = ProfilePath(fixture.Root, missing);
        File.WriteAllText(corruptPath, "{invalid");
        File.Delete(corruptPath + ".bak");
        File.Delete(missingPath);
        File.Delete(missingPath + ".bak");
        Check(slots.TryRefresh(out error) && slots.Slots.Count == 2 && slots.Slots.All(slot => !slot.IsReadable
            && !string.IsNullOrWhiteSpace(slot.Error)), "Damaged occupied slots disappeared or appeared readable: " + error);
        Check(!slots.TrySelect(corrupt.Id, out _, out _) && !slots.TrySelect(missing.Id, out _, out _)
            && !slots.TryCreate(1, null, out _, out _) && !slots.TryCreate(2, null, out _, out _),
            "A corrupt or missing profile was treated as a new save.");
        Check(File.ReadAllText(corruptPath) == "{invalid" && !File.Exists(missingPath),
            "Trying to load damaged saves silently overwrote their files.");
    }

    private static void IoFailurePreservesExistingSlots()
    {
        using var fixture = new Fixture();
        var slots = fixture.Service;
        Check(slots.TryCreate(1, "Existing", out var existing, out var error)
            && slots.TrySelect(existing.Id, out var account, out error)
            && account.TryGrantReward("before-failure", 13, out error), "Could not prepare I/O fixture: " + error);
        string blocked = Path.Combine(fixture.Root, "save-slots", "slot-2");
        File.WriteAllText(blocked, "A file blocks directory creation.");
        Check(!slots.TryCreate(2, "Retry", out _, out error) && !string.IsNullOrWhiteSpace(error),
            "Directory creation error did not surface.");
        var afterFailure = fixture.Reopen();
        Check(afterFailure.TryRefresh(out error) && afterFailure.Slots.Count == 1
            && afterFailure.Slots.Single().Id == existing.Id && afterFailure.Slots.Single().Balance == 13,
            "A failed creation changed existing slot data: " + error);
        File.Delete(blocked);
        Check(slots.TryCreate(2, "Retry", out var retry, out error) && retry.IsReadable && retry.Balance == 0,
            "The same slot was not retryable after clearing the I/O error: " + error);
    }

    private static void LastPlayedSlotSurvivesReload()
    {
        using var fixture = new Fixture();
        var slots = fixture.Service;
        Check(!slots.TryGetLastPlayedSlot(out var empty, out var error) && empty == null && error == null,
            "An empty catalog reported a last played slot.");
        Check(slots.TryCreate(1, "First", out var first, out error), "Could not create the first resume fixture: " + error);
        Check(slots.TryCreate(2, "Second", out var second, out error), "Could not create the second resume fixture: " + error);
        Check(!slots.TryGetLastPlayedSlot(out _, out _), "Creating slots marked one as played before selection.");
        Check(slots.TrySelect(first.Id, out var firstAccount, out error), "Could not play the first slot: " + error);
        Check(firstAccount.TryGrantReward("resume-balance", 39, out error), "Could not fund the first resumed account: " + error);

        var reopened = fixture.Reopen();
        var checkpoint = SeedTutorialSession();
        Check(reopened.TryGetLastPlayedSlot(out var last, out error) && last.Id == first.Id && last.Balance == 39
            && reopened.SelectedAccount == null && reopened.SelectedSlotId == null && TutorialSession.IsReplay
            && ReferenceEquals(checkpoint, TutorialSession.PendingCheckpoint),
            "Read-only lookup after reload did not identify the first played slot or changed the tutorial session: " + error);
        Check(reopened.TrySelect(second.Id, out _, out error), "Could not change the most recent slot: " + error);
        Check(fixture.Reopen().TryGetLastPlayedSlot(out last, out error) && last.Id == second.Id,
            "Changing the most recent slot did not persist: " + error);

        Check(reopened.TryDelete(second.Id, out error), "Could not delete the most recent slot: " + error);
        Check(reopened.TryCreate(2, "Replacement", out var replacement, out error) && replacement.Id != second.Id,
            "Could not recreate the deleted slot number with a distinct identity: " + error);
        Check(!fixture.Reopen().TryGetLastPlayedSlot(out last, out _) && last == null,
            "The deleted saved identity was replaced by another occupied slot.");

        File.WriteAllText(LastPlayedPath(fixture.Root), "invalid-slot-id");
        Check(!fixture.Reopen().TryGetLastPlayedSlot(out last, out error) && last == null && !string.IsNullOrEmpty(error),
            "A malformed last played record selected an unrelated slot.");
        Check(reopened.TrySelect(first.Id, out _, out error), "Selecting a readable slot did not repair the malformed record: " + error);
        string firstPath = ProfilePath(fixture.Root, first);
        File.WriteAllText(firstPath, "{invalid");
        File.Delete(firstPath + ".bak");
        Check(!fixture.Reopen().TryGetLastPlayedSlot(out last, out error) && last == null && !string.IsNullOrEmpty(error),
            "An unreadable last played slot was returned or fell back to the other slot.");
    }

    private static void LastPlayedWriteFailurePreservesSelection()
    {
        using var fixture = new Fixture();
        var slots = fixture.Service;
        using var scope = SaveSlotService.OverrideForTesting(slots);
        Check(slots.TryCreate(1, "Current", out var first, out var error), "Could not create the current account fixture: " + error);
        Check(slots.TryCreate(2, "Candidate", out var second, out error), "Could not create the candidate account fixture: " + error);
        Check(slots.TrySelect(first.Id, out var current, out error), "Could not select the current account fixture: " + error);
        var checkpoint = SeedTutorialSession();
        string record = LastPlayedPath(fixture.Root);
        string original = record + ".test-original";
        File.Move(record, original);
        Directory.CreateDirectory(record);
        try
        {
            Check(!slots.TrySelect(second.Id, out var rejected, out error) && rejected == null && !string.IsNullOrEmpty(error),
                "Selecting a new slot succeeded despite a blocked last played file.");
            Check(slots.SelectedSlotId == first.Id && ReferenceEquals(slots.SelectedAccount, current)
                && ReferenceEquals(PlayerUpgradeService.Instance, current) && TutorialSession.IsReplay
                && ReferenceEquals(TutorialSession.PendingCheckpoint, checkpoint),
                "A failed last played commit changed the account or its tutorial session.");
        }
        finally
        {
            Directory.Delete(record);
            File.Move(original, record);
        }
        Check(fixture.Reopen().TryGetLastPlayedSlot(out var last, out error) && last.Id == first.Id,
            "The original last played slot did not remain usable after clearing the obstruction: " + error);
        Check(slots.TrySelect(second.Id, out _, out error) && TutorialSessionWasCleared()
            && fixture.Reopen().TryGetLastPlayedSlot(out last, out error) && last.Id == second.Id,
            "The same selection could not be retried after restoring write access: " + error);
    }

    private static string LastPlayedPath(string root) => Path.Combine(root, "save-slots", "last-played-slot");

    internal static string CreateTemporaryRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), TemporaryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    internal static void DeleteTemporaryRoot(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        string root = Path.GetFullPath(path);
        string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(root), temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith(TemporaryPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to delete a path outside the save-slot regression fixture.");
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    internal static string ProfilePath(string root, SaveSlotInfo slot)
        => Path.Combine(root, "save-slots", "slot-" + slot.Number, slot.Id, "player-upgrades.json");

    private static TutorialCheckpoint SeedTutorialSession()
    {
        TutorialSession.IsReplay = TutorialSession.PendingHubOverview = TutorialSession.RestoreCheckpoint = true;
        return TutorialSession.PendingCheckpoint = new TutorialCheckpoint { gold = 11 };
    }

    private static bool TutorialSessionWasCleared() => TutorialSession.PendingCheckpoint == null && !TutorialSession.IsReplay
        && !TutorialSession.PendingHubOverview && !TutorialSession.RestoreCheckpoint;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerUpgradeSettingsSO _settings = Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings");
        public readonly string Root;
        public readonly SaveSlotService Service;
        public Fixture()
        {
            Root = CreateTemporaryRoot();
            Service = Reopen();
        }
        public SaveSlotService Reopen() => new SaveSlotService(Root, _settings);
        public void Dispose() => DeleteTemporaryRoot(Root);
    }
}
