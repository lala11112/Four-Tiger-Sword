using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Uses explicit settings, memory stores and disposable files; never opens the player's account.</summary>
public static class EngravingProgressionRegression
{
    private static int _checks;
    [MenuItem("Tools/Studio Baekho/Progression/Run Engraving Progression Regression")]
    private static void RunFromMenu() => Run();

    public static string Run()
    {
        _checks = 0;
        CompleteTreeAndCosts();
        ParentBalanceAndSnapshot();
        AtomicFailureAndReentry();
        LegacyTracksRemainIndependent();
        LegacyFileMigrationAndMissingArrays();
        RetiredStarterEngravingsMigrateOnce();
        UnknownIdsAndBackupRecovery();
        string result = $"Engraving progression: 7 scenarios passed; {_checks} checks.";
        Debug.Log(result); return result;
    }

    private static void CompleteTreeAndCosts()
    {
        using var f = new Fixture();
        Check(f.Engravings.Validate(out _), "Default catalog validates");
        int total = 0, paid = 0;
        int[] counts = new int[5];
        foreach (var node in f.Engravings.nodes)
        { counts[node.tier]++; total += f.Engravings.Cost(node.id); if (node.id != "origin") paid++; }
        Check(paid == 37 && total == 1260, "37 unlocks cost exactly1260");
        Check(counts[0] == 1 && counts[1] == 4 && counts[2] == 15 && counts[3] == 13 && counts[4] == 5,
            "Semantic stage counts remain1/4/15/13/5");
        Check(f.Service.IsNodeUnlocked("origin") && !f.Service.GetNodePurchaseState("origin").CanPurchase,
            "Origin starts free and cannot be purchased");
        Check(f.Service.TryGrantReward("tree-fund", total, out _), "Fund complete tree");
        var nodes = (EngravingNodeDefinition[])f.Engravings.nodes.Clone();
        Array.Sort(nodes, (a, b) => a.tier != b.tier ? a.tier.CompareTo(b.tier) : string.CompareOrdinal(a.id, b.id));
        int purchased = 0;
        foreach (var node in nodes)
        {
            if (node.id == "origin") continue;
            int before = f.Service.Balance;
            Check(f.Service.TryUnlockNode(node.id, out string error), "Unlock " + node.id + ": " + error);
            Check(f.Service.Balance == before - f.Engravings.Cost(node.id), "Exact node charge " + node.id);
            purchased++;
        }
        Check(purchased == 37 && f.Service.Balance == 0 && f.Store.Profile.unlockedNodeIds.Length == 37,
            "All single unlocks stored with zero remaining balance");
        int writes = f.Store.Writes;
        foreach (var node in nodes)
            Check(!f.Service.TryUnlockNode(node.id, out _), "Already-unlocked node cannot purchase " + node.id);
        Check(f.Store.Writes == writes && f.Service.Balance == 0, "Repeated requests never charge or save");
        var reload = new PlayerUpgradeService(f.Settings, f.Store, f.Engravings);
        Check(reload.IsReady && reload.IsNodeUnlocked("fusion") && reload.IsNodeUnlocked("critheal"),
            "Complete tree reloads without losing branches");
    }

    private static void ParentBalanceAndSnapshot()
    {
        using var f = new Fixture();
        Check(f.Service.TryGrantReward("fund", 30, out _), "Fund branch");
        int writes = f.Store.Writes;
        Check(!f.Service.TryUnlockNode("crit", out _) && !f.Service.GetNodePurchaseState("crit").CanPurchase,
            "Child locked until immediate parent");
        Check(!f.Service.TryUnlockNode("unknown", out _) && !f.Service.TryUnlockNode("origin", out _)
            && f.Service.Balance == 30 && f.Store.Writes == writes, "Invalid/free requests preserve state");
        var frozen = f.Service.CreateEngravingSnapshot();
        Check(f.Service.TryUnlockNode("atk", out _) && f.Service.Balance == 20, "First tier costs10");
        Check(!frozen.Has("atk") && f.Service.CreateEngravingSnapshot().Has("atk"),
            "Existing run snapshot is unchanged by account purchase");
        Check(f.Service.TryUnlockNode("crit", out _) && f.Service.Balance == 0, "Second tier costs20");
        writes = f.Store.Writes;
        Check(!f.Service.TryUnlockNode("gold", out _) && f.Store.Writes == writes
            && !f.Service.IsNodeUnlocked("gold"), "Insufficient balance cannot unlock");
        var snapshot = f.Service.CreateEngravingSnapshot();
        f.Engravings.Find("atk").amount = .5f;
        Check(Math.Abs(snapshot.Amount("atk") - .08f) < .0001f, "Snapshot copies tuning");
    }

    private static void AtomicFailureAndReentry()
    {
        using var f = new Fixture();
        Check(f.Service.TryGrantReward("fund", 100, out _), "Fund transactions");
        int changes = 0; f.Service.Changed += () => changes++;
        f.Store.FailSaves = true;
        Check(!f.Service.TryUnlockNode("atk", out _) && f.Service.Balance == 100
            && !f.Service.IsNodeUnlocked("atk") && changes == 0, "Failed save commits no ownership, charge or event");
        f.Store.FailSaves = false;
        bool nestedRejected = false, storeSawOld = false, observerSawCommit = false;
        f.Store.BeforeSave = next =>
        {
            storeSawOld = !f.Service.IsNodeUnlocked("atk") && f.Service.Balance == 100
                && Array.IndexOf(next.unlockedNodeIds, "atk") >= 0 && next.currency == 90;
            nestedRejected = !f.Service.TryUnlockNode("gold", out _);
        };
        f.Service.Changed += () => observerSawCommit = f.Service.IsNodeUnlocked("atk") && f.Service.Balance == 90
            && Array.IndexOf(f.Store.Profile.unlockedNodeIds, "atk") >= 0;
        Check(f.Service.TryUnlockNode("atk", out _), "Retry succeeds once");
        Check(storeSawOld && nestedRejected && observerSawCommit && changes == 1,
            "Persistence precedes event and rejects reentrant transaction");
        f.Store.BeforeSave = null;
        Check(f.Service.TryUnlockNode("crit", out _) && f.Service.Balance == 70, "Child available after completed commit");
    }

    private static void LegacyTracksRemainIndependent()
    {
        using var f = new Fixture();
        var profile = PlayerUpgradeProfile.New();
        profile.currency = 100; profile.healthLevel = 2; profile.attackLevel = 3; profile.defenseLevel = 4;
        var store = new MemoryStore { Profile = profile };
        var service = new PlayerUpgradeService(f.Settings, store, f.Engravings);
        Check(service.TryUnlockNode("atk", out _) && service.Balance == 90, "Tree spends shared currency");
        Check(service.GetLevel(StatType.ST_HP) == 2 && service.GetLevel(StatType.ST_ATK) == 3
            && service.GetLevel(StatType.ST_DEF) == 4, "Tree preserves existing workshop levels");
        int oldCost = service.GetNextCost(StatType.ST_ATK);
        Check(service.TryUpgrade(StatType.ST_ATK, out _) && service.Balance == 90 - oldCost
            && service.GetLevel(StatType.ST_ATK) == 4 && service.IsNodeUnlocked("atk"),
            "Workshop continues independently and preserves tree ownership");
        var duplicate = profile.Copy(); duplicate.unlockedNodeIds = new[] { "atk", "atk" };
        Check(!duplicate.Validate(out _), "Duplicate ownership is invalid");
        var missingParent = profile.Copy(); missingParent.unlockedNodeIds = new[] { "crit" };
        Check(!new PlayerUpgradeService(f.Settings, new MemoryStore { Profile = missingParent }, f.Engravings).IsReady,
            "Loaded orphan nodes are rejected");
    }

    private static void LegacyFileMigrationAndMissingArrays()
    {
        using var f = new Fixture(); using var files = new TemporaryFiles();
        string legacy = "{\"version\":1,\"currency\":87,\"healthLevel\":2,\"attackLevel\":3,\"defenseLevel\":4}";
        File.WriteAllText(files.Path, legacy);
        var service = new PlayerUpgradeService(f.Settings, new PlayerUpgradeFileStore(files.Path), f.Engravings);
        Check(service.IsReady && service.Balance == 87 && service.GetLevel(StatType.ST_DEF) == 4
            && !service.IsNodeUnlocked("atk"), "V1 loads with existing progress and empty tree");
        Check(File.ReadAllText(files.Path) == legacy && !File.Exists(files.Path + ".bak"), "Read does not rewrite legacy save");
        Check(service.TryUnlockNode("atk", out _), "First purchase migrates V1 atomically");
        var store = new PlayerUpgradeFileStore(files.Path);
        Check(store.TryLoad(out var migrated, out _) && migrated.version == 2 && migrated.currency == 77
            && migrated.healthLevel == 2 && migrated.attackLevel == 3 && migrated.defenseLevel == 4
            && migrated.unlockedNodeIds.Length == 1 && migrated.unlockedNodeIds[0] == "atk", "V2 migration preserves every field");
        Check(File.ReadAllText(files.Path + ".bak") == legacy, "Migration retains original V1 backup");
        File.Delete(files.Path + ".bak");
        File.WriteAllText(files.Path, "{\"version\":2,\"currency\":77,\"healthLevel\":2,\"attackLevel\":3,\"defenseLevel\":4}");
        string broken = File.ReadAllText(files.Path);
        var blocked = new PlayerUpgradeService(f.Settings, new PlayerUpgradeFileStore(files.Path), f.Engravings);
        Check(!blocked.IsReady && !blocked.TryGrantReward("do-not-wipe", 10, out _) && File.ReadAllText(files.Path) == broken,
            "V2 missing ownership array blocks writes and preserves file");
    }

    private static void RetiredStarterEngravingsMigrateOnce()
    {
        using var f = new Fixture(); using var files = new TemporaryFiles();
        var profile = PlayerUpgradeProfile.New();
        profile.currency = 7;
        profile.healthLevel = 1;
        profile.attackLevel = 2;
        profile.defenseLevel = 3;
        profile.unlockedNodeIds = new[] { "heirloom", "starterelement", "reroll", "hold", "choices", "synergy", "fusion" };
        string original = JsonUtility.ToJson(profile, true);
        string backupPath = files.Path + ".bak";
        var older = PlayerUpgradeProfile.New();
        older.currency = 2;
        string oldBackup = JsonUtility.ToJson(older, true);
        File.WriteAllText(files.Path, original);
        File.WriteAllText(backupPath, oldBackup);
        var store = new PlayerUpgradeFileStore(files.Path, f.Engravings);
        var service = new PlayerUpgradeService(f.Settings, store, f.Engravings);
        Check(service.IsReady && service.Balance == 37 && service.GetLevel(StatType.ST_HP) == 1
            && service.GetLevel(StatType.ST_ATK) == 2 && service.GetLevel(StatType.ST_DEF) == 3,
            "Valid legacy primary takes priority over an older backup and preserves stat levels with one refund");
        Check(!service.IsNodeUnlocked("heirloom") && !service.TryUnlockNode("starterelement", out _)
            && service.IsNodeUnlocked("hold") && service.IsNodeUnlocked("fusion")
            && service.CreateEngravingSnapshot().Limit("reroll") == 3,
            "Retired nodes are removed and combat reward descendants are preserved");
        var reloaded = new PlayerUpgradeService(f.Settings, new PlayerUpgradeFileStore(files.Path, f.Engravings), f.Engravings);
        Check(reloaded.IsReady && reloaded.Balance == 37 && File.ReadAllText(files.Path) == original
            && File.ReadAllText(backupPath) == oldBackup, "Repeated loading neither pays again nor rewrites either file");

        File.Delete(backupPath);
        Directory.CreateDirectory(backupPath);
        try
        {
            Check(!service.TryUnlockNode("atk", out _) && service.Balance == 37 && !service.IsNodeUnlocked("atk")
                && service.IsNodeUnlocked("hold") && File.ReadAllText(files.Path) == original,
                "Failed atomic replacement preserves the legacy primary and in-memory migration");
        }
        finally { Directory.Delete(backupPath); }
        File.WriteAllText(backupPath, oldBackup);
        Check(service.TryUnlockNode("atk", out _) && service.Balance == 27,
            "Next successful purchase saves the migration atomically");
        var saved = JsonUtility.FromJson<PlayerUpgradeProfile>(File.ReadAllText(files.Path));
        var persisted = new PlayerUpgradeService(f.Settings, new PlayerUpgradeFileStore(files.Path, f.Engravings), f.Engravings);
        Check(File.ReadAllText(backupPath) == original && saved.currency == 27
            && Array.IndexOf(saved.unlockedNodeIds, "heirloom") < 0 && Array.IndexOf(saved.unlockedNodeIds, "starterelement") < 0
            && persisted.IsReady && persisted.Balance == 27 && persisted.IsNodeUnlocked("atk") && persisted.IsNodeUnlocked("fusion"),
            "Primary stores both sides of the migration and reload does not credit the retained backup");
        var heirloomOnly = profile.Copy();
        heirloomOnly.unlockedNodeIds = new[] { "heirloom" };
        var memory = new MemoryStore { Profile = heirloomOnly };
        var migrated = new PlayerUpgradeService(f.Settings, memory, f.Engravings);
        Check(migrated.IsReady && migrated.Balance == 17 && memory.Profile.currency == 7,
            "Other store implementations credit just the retired node that was owned");
        Check(f.Engravings.Cost("reroll") == 20 && f.Engravings.Cost("choices") == 20
            && f.Engravings.Find("reroll").parentId == "origin" && f.Engravings.Find("choices").parentId == "origin",
            "Combat reward branches remain directly accessible at their original costs");
        var orphan = profile.Copy();
        orphan.unlockedNodeIds = new[] { "starterelement" };
        Check(!new PlayerUpgradeService(f.Settings, new MemoryStore { Profile = orphan }, f.Engravings).IsReady,
            "The removed synergy purchase still requires a valid legacy parent");
        File.Delete(backupPath);
        File.WriteAllText(files.Path, JsonUtility.ToJson(orphan));
        Check(!store.TryLoad(out _, out _), "File store rejects the removed synergy purchase without its legacy parent");
        var unknown = profile.Copy();
        unknown.unlockedNodeIds = new[] { "heirloom", "unknown-node" };
        File.WriteAllText(files.Path, JsonUtility.ToJson(unknown));
        Check(!store.TryLoad(out _, out _)
            && !new PlayerUpgradeService(f.Settings, new MemoryStore { Profile = unknown }, f.Engravings).IsReady,
            "Migration does not make other unknown ownership IDs valid");

        var nearLimit = profile.Copy();
        nearLimit.currency = int.MaxValue - 5;
        var capped = new PlayerUpgradeService(f.Settings, new MemoryStore { Profile = nearLimit }, f.Engravings);
        Check(capped.IsReady && capped.Balance == int.MaxValue && !capped.IsNodeUnlocked("heirloom"),
            "Refund at the balance limit does not overflow or retain the retired purchase");
    }

    private static void UnknownIdsAndBackupRecovery()
    {
        using var f = new Fixture(); using var files = new TemporaryFiles();
        var backup = PlayerUpgradeProfile.New(); backup.currency = 37; backup.healthLevel = 1;
        backup.attackLevel = 2; backup.defenseLevel = 3; backup.unlockedNodeIds = new[] { "atk" };
        string valid = JsonUtility.ToJson(backup, true);
        var invalid = backup.Copy(); invalid.currency = 999; invalid.unlockedNodeIds = new[] { "unknown-node" };
        string unknown = JsonUtility.ToJson(invalid, true);
        File.WriteAllText(files.Path, unknown); File.WriteAllText(files.Path + ".bak", valid);
        var service = new PlayerUpgradeService(f.Settings, new PlayerUpgradeFileStore(files.Path), f.Engravings);
        Check(service.IsReady && service.Balance == 37 && service.IsNodeUnlocked("atk")
            && service.GetLevel(StatType.ST_DEF) == 3, "Unknown primary ID recovers semantically valid backup");
        Check(service.TryUnlockNode("crit", out _) && service.Balance == 17, "Purchase after backup recovery");
        Check(File.ReadAllText(files.Path + ".bak") == valid, "Unknown primary cannot replace known-good backup");
        File.WriteAllText(files.Path, unknown); File.WriteAllText(files.Path + ".bak", unknown);
        var blocked = new PlayerUpgradeService(f.Settings, new PlayerUpgradeFileStore(files.Path), f.Engravings);
        Check(!blocked.IsReady && !blocked.TryGrantReward("blocked", 1, out _), "Both semantic failures block mutation");
        Check(File.ReadAllText(files.Path) == unknown && File.ReadAllText(files.Path + ".bak") == unknown,
            "Both invalid files remain intact");
    }

    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        public PlayerUpgradeProfile Profile = PlayerUpgradeProfile.New();
        public bool FailSaves;
        public int Writes;
        public Action<PlayerUpgradeProfile> BeforeSave;
        public bool TryLoad(out PlayerUpgradeProfile profile, out string error)
        { profile = Profile.Copy(); error = null; return true; }
        public bool TrySave(PlayerUpgradeProfile profile, out string error)
        {
            BeforeSave?.Invoke(profile);
            error = FailSaves ? "Simulated engraving save failure" : null;
            if (FailSaves) return false;
            Profile = profile.Copy(); Writes++; return true;
        }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly PlayerStatData Data = ScriptableObject.CreateInstance<PlayerStatData>();
        public readonly PlayerUpgradeSettingsSO Settings = ScriptableObject.CreateInstance<PlayerUpgradeSettingsSO>();
        public readonly EngravingSettingsSO Engravings = ScriptableObject.CreateInstance<EngravingSettingsSO>();
        public readonly MemoryStore Store = new();
        public readonly PlayerUpgradeService Service;
        public Fixture() { Settings.basePlayerStats = Data; Service = new PlayerUpgradeService(Settings, Store, Engravings); }
        public void Dispose() { Object.DestroyImmediate(Engravings); Object.DestroyImmediate(Settings); Object.DestroyImmediate(Data); }
    }
    private sealed class TemporaryFiles : IDisposable
    {
        private readonly string _folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EngravingRegression-" + Guid.NewGuid().ToString("N")));
        public string Path => System.IO.Path.Combine(_folder, "profile.json");
        public TemporaryFiles() => Directory.CreateDirectory(_folder);
        public void Dispose()
        {
            if (!Directory.Exists(_folder)) return;
            foreach (string file in Directory.GetFiles(_folder)) File.Delete(file);
            Directory.Delete(_folder);
        }
    }
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException("Engraving progression: " + message); }
}
