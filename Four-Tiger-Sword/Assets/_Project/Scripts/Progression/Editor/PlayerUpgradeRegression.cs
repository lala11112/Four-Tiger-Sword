using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Progression regressions use isolated stores and temporary objects, never the player's account.</summary>
public static class PlayerUpgradeRegression
{
    private static int _checks;

    [MenuItem("Tools/Studio Baekho/Progression/Run Player Upgrade Regression")]
    private static void RunFromMenu() => Run();

    public static string Run()
    {
        _checks = 0;
        FreshAccountAndIndependentTracks();
        FailedWritesAndRewardRetry();
        CommitOrderAndReentry();
        LimitsAndInvalidRequests();
        PersistenceAndLoadRecovery();
        PermanentStatsAndExistingEffects();
        ExplicitBindingAndFreshPlayer();
        AtomicFilesAndCorruptData();
        string result = $"Player upgrades: 8 regression scenarios passed; {_checks} checks.";
        Debug.Log(result);
        return result;
    }

    private static void FreshAccountAndIndependentTracks()
    {
        using var f = new Fixture();
        Check(f.Service.IsReady && f.Service.Balance == 0, "Fresh profile");
        Check(f.Service.GetLevel(StatType.ST_HP) == 0 && f.Service.GetNextCost(StatType.ST_HP) == 10, "Fresh track");
        Check(!f.Service.TryUpgrade(StatType.ST_HP, out _) && f.Store.SaveCount == 0, "Insufficient funds do not save");
        Check(f.Service.TryGrantReward("first-stage", 100, out _), "Grant currency");
        Check(f.Service.TryUpgrade(StatType.ST_HP, out _), "First health upgrade");
        Check(f.Service.TryUpgrade(StatType.ST_ATK, out _), "First attack upgrade");
        Check(f.Service.TryUpgrade(StatType.ST_HP, out _), "Second health upgrade");
        Check(f.Service.Balance == 65 && f.Service.GetLevel(StatType.ST_HP) == 2
            && f.Service.GetLevel(StatType.ST_ATK) == 1 && f.Service.GetLevel(StatType.ST_DEF) == 0, "Independent levels and costs");
        Near(f.Service.GetBonusRatio(StatType.ST_HP), .1f, "Health ratio");
        Near(f.Service.GetBonusRatio(StatType.ST_ATK), .05f, "Attack ratio");
        Near(f.Service.GetBaseValue(StatType.ST_HP), 100f, "Configured base stats");
    }

    private static void FailedWritesAndRewardRetry()
    {
        using var f = new Fixture();
        int changes = 0;
        f.Service.Changed += () => changes++;
        f.Store.FailSaves = true;
        Check(!f.Service.TryGrantReward("retry-stage", 30, out _) && f.Service.Balance == 0 && changes == 0,
            "Failed grant is not published");
        f.Store.FailSaves = false;
        Check(f.Service.TryGrantReward("retry-stage", 30, out _) && f.Service.Balance == 30 && changes == 1,
            "A failed grant ID remains retryable");
        int saves = f.Store.SaveCount;
        Check(f.Service.TryGrantReward("retry-stage", 30, out _) && f.Service.Balance == 30
            && changes == 1 && f.Store.SaveCount == saves, "A successful reward ID is idempotent");
        f.Store.FailSaves = true;
        Check(!f.Service.TryUpgrade(StatType.ST_DEF, out _) && f.Service.Balance == 30
            && f.Service.GetLevel(StatType.ST_DEF) == 0 && changes == 1, "Failed purchase keeps balance and level");
        f.Store.FailSaves = false;
        Check(f.Service.TryUpgrade(StatType.ST_DEF, out _) && f.Service.Balance == 20
            && f.Service.GetLevel(StatType.ST_DEF) == 1 && changes == 2, "Retry purchase succeeds once");
    }

    private static void CommitOrderAndReentry()
    {
        using var f = new Fixture();
        Check(f.Service.TryGrantReward("fund", 40, out _), "Fund transaction test");
        bool sawBeforeCommit = false, sawCommitted = false, reentryRejected = false;
        f.Store.BeforeSave = next =>
        {
            sawBeforeCommit = f.Service.Balance == 40 && f.Service.GetLevel(StatType.ST_ATK) == 0
                && next.currency == 30 && next.attackLevel == 1;
            reentryRejected = !f.Service.TryGrantReward("nested-store", 100, out _);
        };
        f.Service.Changed += () =>
        {
            sawCommitted = f.Service.Balance == 30 && f.Service.GetLevel(StatType.ST_ATK) == 1
                && f.Store.Profile.currency == 30 && f.Store.Profile.attackLevel == 1;
            reentryRejected &= !f.Service.TryUpgrade(StatType.ST_HP, out _);
        };
        Check(f.Service.TryUpgrade(StatType.ST_ATK, out _), "Purchase commits");
        Check(sawBeforeCommit && sawCommitted && reentryRejected, "Storage precedes publication and callbacks cannot reenter");
        Check(f.Service.LastError == null, "Rejected callback reentry does not overwrite successful transaction status");
    }

    private static void LimitsAndInvalidRequests()
    {
        using var f = new Fixture();
        f.Settings.maxLevel = 2;
        Check(f.Service.TryGrantReward("fund", 100, out _), "Fund limits");
        Check(f.Service.TryUpgrade(StatType.ST_DEF, out _) && f.Service.TryUpgrade(StatType.ST_DEF, out _), "Reach maximum");
        int balance = f.Service.Balance, saves = f.Store.SaveCount;
        Check(f.Service.GetNextCost(StatType.ST_DEF) == 0 && !f.Service.TryUpgrade(StatType.ST_DEF, out _), "Reject maximum level");
        Check(!f.Service.TryUpgrade((StatType)1, out _) && !f.Service.TryGrantReward("", 1, out _)
            && !f.Service.TryGrantReward("negative", -1, out _), "Invalid requests");
        Check(f.Service.Balance == balance && f.Store.SaveCount == saves, "Invalid requests have no state or file effects");
        var large = PlayerUpgradeProfile.New();
        large.currency = int.MaxValue;
        var overflowStore = new MemoryStore { Profile = large };
        var overflow = new PlayerUpgradeService(f.Settings, overflowStore);
        Check(!overflow.TryGrantReward("overflow", 1, out _) && overflow.Balance == int.MaxValue
            && overflowStore.SaveCount == 0, "Overflow rejected");
    }

    private static void PersistenceAndLoadRecovery()
    {
        using var f = new Fixture();
        Check(f.Service.TryGrantReward("fund", 50, out _) && f.Service.TryUpgrade(StatType.ST_HP, out _), "Save progression");
        var loaded = new PlayerUpgradeService(f.Settings, f.Store);
        Check(loaded.IsReady && loaded.Balance == 40 && loaded.GetLevel(StatType.ST_HP) == 1, "New service reloads progression");
        f.Store.Profile.currency = 999;
        Check(loaded.Balance == 40, "Loaded state does not alias store-owned profile");
        var unavailable = new MemoryStore { FailLoads = true };
        var recovery = new PlayerUpgradeService(f.Settings, unavailable);
        Check(!recovery.IsReady && !string.IsNullOrEmpty(recovery.LastError)
            && !recovery.TryGrantReward("pending", 10, out _) && unavailable.SaveCount == 0, "Failed load never wipes progression");
        unavailable.FailLoads = false;
        Check(recovery.TryReload(out _) && recovery.TryGrantReward("pending", 10, out _) && recovery.Balance == 10,
            "Explicit retry recovers a load failure");
        var missingSettings = new PlayerUpgradeService(null, unavailable);
        Check(!missingSettings.IsReady && !missingSettings.TryUpgrade(StatType.ST_HP, out _), "Missing configuration is visible");
        var corrupt = new MemoryStore { Profile = new PlayerUpgradeProfile { version = 800 } };
        var blocked = new PlayerUpgradeService(f.Settings, corrupt);
        Check(!blocked.IsReady && !blocked.TryGrantReward("unsafe", 2, out _) && corrupt.SaveCount == 0,
            "Unsupported profiles are preserved");
    }

    private static void PermanentStatsAndExistingEffects()
    {
        using var f = new Fixture();
        var stats = f.Stats;
        stats.SetPermanentUpgradeRatios(.5f, .5f, .5f, true);
        Near(stats.MaxHp, 150f, "Upgraded health");
        Near(stats.CurrentHp, 150f, "Full health at spawn");
        Near(stats.GetStat(StatType.ST_ATK), 15f, "Upgraded attack");
        Near(stats.GetStat(StatType.ST_DEF), 7.5f, "Upgraded defense");
        stats.SetPermanentUpgradeRatios(.5f, .5f, .5f);
        Near(stats.MaxHp, 150f, "Reapplying ratios cannot stack");
        stats.AddModifier(StatType.ST_ATK, 5f, .2f);
        Near(stats.GetStat(StatType.ST_ATK), 24f, "Growth precedes flat and multiplicative skill effects");
        stats.RemoveModifier(StatType.ST_ATK, 5f, .2f);
        Near(stats.GetStat(StatType.ST_ATK), 15f, "Skill expiration preserves growth");
        var item = new object();
        stats.SetItemResourceModifiers(item, .2f);
        Near(stats.MaxHp, 180f, "Item health scales upgraded basis");
        Near(stats.CurrentHp, 150f, "Item maximum increase does not heal");
        stats.TakeDamage(50f, ElementType.ELEMENT_NONE, false);
        stats.SetPermanentUpgradeRatios(.6f, .5f, .5f);
        Near(stats.CurrentHp, 100f, "Live upgrade replacement does not refill health");
        stats.RemoveItemResourceModifiers(item);
        Near(stats.MaxHp, 160f, "Removing an item preserves permanent growth");
        stats.Initialize(f.Data);
        Near(stats.MaxHp, 100f, "Explicit test initialization resets upgrades without global account access");
        Near(stats.GetStat(StatType.ST_ATK), 10f, "Explicit initialization restores test attack");
    }

    private static void ExplicitBindingAndFreshPlayer()
    {
        using var f = new Fixture();
        Check(f.Service.TryGrantReward("fund", 100, out _) && f.Service.TryUpgrade(StatType.ST_HP, out _), "Fund bound player");
        var binding = f.Root.AddComponent<PlayerUpgradeBinding>();
        binding.Bind(f.Service, true);
        Near(f.Stats.MaxHp, 105f, "Injected spawn binding");
        Near(f.Stats.CurrentHp, 105f, "Injected spawn starts full");
        // Inactive bindings deliberately do not subscribe to future changes.
        Check(f.Service.TryUpgrade(StatType.ST_ATK, out _), "Upgrade before run begins");
        binding.Bind(f.Service, true);
        Near(f.Stats.GetStat(StatType.ST_ATK), 10.5f, "Explicit rebind reads current profile");
        Near(f.Stats.CurrentHp, 105f, "Rebinding does not stack growth");
        f.Root.SetActive(true);
        InvokeBinding(binding, "OnEnable");
        f.Stats.TakeDamage(5f, ElementType.ELEMENT_NONE, false);
        Check(f.Service.TryUpgrade(StatType.ST_HP, out _), "Upgrade an actively bound player");
        Near(f.Stats.MaxHp, 110f, "Changed event replaces the permanent contribution");
        Near(f.Stats.CurrentHp, 100f, "Changed event preserves current health");
        f.Root.SetActive(false);
        InvokeBinding(binding, "OnDisable");
        Check(f.Service.TryUpgrade(StatType.ST_ATK, out _), "Upgrade while binding is disabled");
        f.Root.SetActive(true);
        InvokeBinding(binding, "OnEnable");
        Near(f.Stats.GetStat(StatType.ST_ATK), 11f, "Reenable reconciles missed account changes without global coupling");

        var recoveryStore = new MemoryStore { FailLoads = true };
        recoveryStore.Profile.healthLevel = 2;
        var recovery = new PlayerUpgradeService(f.Settings, recoveryStore);
        f.Stats.Initialize(f.Data);
        binding.Bind(recovery, true);
        Near(f.Stats.CurrentHp, 100f, "A blocked initial load leaves the base player intact");
        recoveryStore.FailLoads = false;
        Check(recovery.TryReload(out _), "Recover an initially unavailable spawn account");
        Near(f.Stats.MaxHp, 110f, "Recovered spawn applies stored maximum");
        Near(f.Stats.CurrentHp, 110f, "Initial refill survives until the first successful apply");
        f.Stats.TakeDamage(5f, ElementType.ELEMENT_NONE, false);
        Check(recovery.TryGrantReward("later-notification", 1, out _), "Subsequent account notification");
        Near(f.Stats.CurrentHp, 105f, "Later account notifications do not repeat the spawn refill");
    }

    private static void InvokeBinding(PlayerUpgradeBinding binding, string method)
        => typeof(PlayerUpgradeBinding).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(binding, null);

    private static void AtomicFilesAndCorruptData()
    {
        using var f = new Fixture();
        string folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PlayerUpgradeRegression-" + Guid.NewGuid().ToString("N")));
        string path = Path.Combine(folder, "profile.json");
        try
        {
            var store = new PlayerUpgradeFileStore(path);
            Check(store.TryLoad(out var fresh, out _) && fresh.currency == 0 && !Directory.Exists(folder), "A fresh read creates no save");
            fresh.currency = 10;
            Check(store.TrySave(fresh, out _), "Initial atomic file write");
            fresh.currency = 20;
            Check(store.TrySave(fresh, out _) && File.Exists(path + ".bak"), "Replacement retains backup");
            Check(store.TryLoad(out var latest, out _) && latest.currency == 20, "Load primary");
            File.WriteAllText(path, "{broken");
            Check(store.TryLoad(out var recovered, out _) && recovered.currency == 10, "Recover valid backup");
            recovered.currency = 15;
            Check(store.TrySave(recovered, out _), "Commit after backup recovery");
            File.WriteAllText(path, "{broken-again");
            Check(store.TryLoad(out recovered, out _) && recovered.currency == 10, "Corrupt primary never replaces known-good backup");
            File.WriteAllText(path + ".bak", "{also-broken");
            var blocked = new PlayerUpgradeService(f.Settings, new PlayerUpgradeFileStore(path));
            Check(!blocked.IsReady && !blocked.TryGrantReward("do-not-wipe", 10, out _), "All corrupt files block writes");
            Check(File.ReadAllText(path) == "{broken-again" && File.ReadAllText(path + ".bak") == "{also-broken",
                "Corrupt existing files are preserved for recovery");
            File.WriteAllText(path, "{\"version\":1}");
            Check(!new PlayerUpgradeFileStore(path).TryLoad(out _, out _), "A profile missing required fields is rejected");
        }
        finally
        {
            // Only this freshly named temporary directory is touched; no recursive deletion is needed.
            if (Directory.Exists(folder))
            {
                foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
                Directory.Delete(folder);
            }
        }
    }

    private static void Near(float actual, float expected, string message)
        => Check(!float.IsNaN(actual) && Mathf.Abs(actual - expected) < .001f, $"{message}: {actual} != {expected}");

    private static void Check(bool success, string message)
    {
        _checks++;
        if (!success) throw new InvalidOperationException("Player upgrade regression: " + message);
    }

    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        public PlayerUpgradeProfile Profile = PlayerUpgradeProfile.New();
        public bool FailLoads, FailSaves;
        public int SaveCount;
        public Action<PlayerUpgradeProfile> BeforeSave;
        public bool TryLoad(out PlayerUpgradeProfile profile, out string error)
        {
            profile = FailLoads ? null : Profile.Copy();
            error = FailLoads ? "Simulated read failure." : null;
            return !FailLoads;
        }
        public bool TrySave(PlayerUpgradeProfile profile, out string error)
        {
            BeforeSave?.Invoke(profile);
            error = FailSaves ? "Simulated write failure." : null;
            if (FailSaves) return false;
            Profile = profile.Copy();
            SaveCount++;
            return true;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly PlayerUpgradeSettingsSO Settings;
        public readonly PlayerStatData Data;
        public readonly PlayerUpgradeService Service;
        public readonly MemoryStore Store = new();
        public readonly GameObject Root;
        public readonly PlayerStatManager Stats;
        public Fixture()
        {
            Data = ScriptableObject.CreateInstance<PlayerStatData>();
            Data.hideFlags = HideFlags.HideAndDontSave;
            Data.baseHP = 100f;
            Data.baseATK = 10f;
            Data.baseDEF = 5f;
            Settings = ScriptableObject.CreateInstance<PlayerUpgradeSettingsSO>();
            Settings.hideFlags = HideFlags.HideAndDontSave;
            Settings.basePlayerStats = Data;
            Service = new PlayerUpgradeService(Settings, Store);
            Root = new GameObject("Player upgrade regression") { hideFlags = HideFlags.HideAndDontSave };
            Root.SetActive(false);
            Stats = Root.AddComponent<PlayerStatManager>();
            Stats.Initialize(Data);
        }
        public void Dispose()
        {
            Object.DestroyImmediate(Root);
            Object.DestroyImmediate(Settings);
            Object.DestroyImmediate(Data);
        }
    }
}
