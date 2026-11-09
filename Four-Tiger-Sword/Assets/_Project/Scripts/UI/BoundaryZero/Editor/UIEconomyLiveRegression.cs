using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Exercises the authored run, HUD and result using an isolated production file store.</summary>
public static class UIEconomyLiveRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IDisposable _accountScope;
    private static PlayerUpgradeService _account;
    private static RunManager _previousRun;
    private static string _profilePath, _status = "NOT STARTED";
    private static int _startingBalance;
    private static bool _busy;

    public static string ProfilePath => _profilePath;

    public static string BeginIsolatedRun(int balance = 500)
    {
        Check(Application.isPlaying, "Enter Play Mode before starting the isolated run.");
        Check(!_busy && balance >= 0, "Wait for the previous check and provide a valid starting balance.");
        var settings = Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings");
        var engravings = EngravingSettingsSO.Default;
        var path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "StudioBaekho",
            "UILiveQA", Guid.NewGuid().ToString("N"), "player-upgrades.json");
        var store = new PlayerUpgradeFileStore(path, engravings);
        var profile = PlayerUpgradeProfile.New();
        profile.currency = balance;
        Check(store.TrySave(profile, out string error), "Could not seed the QA profile: " + error);
        var account = new PlayerUpgradeService(settings, store, engravings);
        Check(account.IsReady && account.Balance == balance, "Could not read the QA profile: " + account.LastError);

        _accountScope?.Dispose();
        _previousRun = Object.FindFirstObjectByType<RunManager>();
        _account = account;
        _accountScope = PlayerUpgradeService.OverrideForTesting(account);
        _profilePath = path;
        _startingBalance = balance;
        try { SceneManager.LoadScene("Game Scene", LoadSceneMode.Single); }
        catch { Close(); throw; }
        return _status = "LOADING: Game Scene with isolated production profile " + path;
    }

    public static string Status()
    {
        if (_busy || _status.StartsWith("PASS", StringComparison.Ordinal)
            || _status.StartsWith("FAIL", StringComparison.Ordinal) || _account == null) return _status;
        var run = Object.FindFirstObjectByType<RunManager>();
        if (run == null || run == _previousRun) return _status;
        if (run.State == RunState.Error) return "FAIL: " + run.LastError;
        return run.State == RunState.Playing && run.stageManager.IsReady && run.ItemChoices != null
            ? "READY: actual Game Scene run. QA profile: " + _profilePath : "WAIT: " + run.State;
    }

    public static string CheckSynergy()
    {
        var run = RequireRun(playing: true);
        return StartCheck("actual simultaneous synergy acquisition", async deadline =>
        {
            var items = run.player.GetComponent<PlayerItemRuntime>();
            var feed = run.GetComponent<RunRewardFeedPresenter>()?.Feed;
            Check(feed != null, "The authored run reward feed is missing.");
            var fixture = FindSimultaneousSynergy(items);
            foreach (string seed in fixture.Seeds)
                Check(items.TryAddItem(seed), "Could not acquire the catalog prerequisite " + seed);
            feed.Clear();

            var triggered = new List<ItemSynergyState>();
            void OnActivation(ItemSynergyState state) => triggered.Add(state);
            items.SynergyTierActivated += OnActivation;
            try { Check(items.TryAddItem(fixture.Item.itemId), "Could not acquire " + fixture.Item.itemId); }
            finally { items.SynergyTierActivated -= OnActivation; }
            var expected = fixture.Sets.Select(set => set.id).OrderBy(id => id, StringComparer.Ordinal);
            Check(triggered.Count == 2 && triggered.All(state => state.ActiveTier.count == 2)
                && triggered.Select(state => state.Definition.id).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(expected),
                "One acquisition did not activate both intended two-item synergies.");
            Check(feed.ActiveRowCount == 3, "The real run feed did not queue the acquisition and both synergies.");
            await Until(() => Enumerable.Range(0, 3).All(index => feed.GetRow(index) is { Alpha: > .95f }),
                deadline, "visible item and synergy notifications");
            var labels = Enumerable.Range(0, 3)
                .Select(index => feed.GetRow(index).GetComponentInChildren<TMP_Text>()).ToArray();
            Check(feed.GetComponentInParent<Canvas>() is { enabled: true }
                && labels[0].text == fixture.Item.displayName + " 획득"
                && labels.Skip(1).Select(label => label.text).SequenceEqual(
                    triggered.Select(state => $"{state.Definition.displayName} 2세트 활성화")),
                "The real feed did not show the acquisition first and the two distinct synergy rows.");
            foreach (var label in labels)
            {
                label.ForceMeshUpdate();
                Check(label.textInfo.characterInfo.Take(label.textInfo.characterCount).Any(character => character.isVisible)
                    && label.font.HasCharacters(label.text, out uint[] _, true, true), "A feed row has missing visible glyphs.");
            }
            return "PASS: one actual acquisition of " + fixture.Item.displayName + " showed both synergy tiers: "
                + string.Join(", ", triggered.Select(state => state.Definition.displayName)) + ".";
        });
    }

    public static string BeginSettlement(bool won, bool failSave = false)
    {
        var run = RequireRun(playing: true);
        Check(!run.IsRewardOpen && !run.HasPendingItemChoice && !run.player.IsGameplayModalPaused,
            "Close game UI and resolve any pending item choice before testing settlement.");
        return StartCheck(won ? "controlled win settlement" : "lethal damage and death settlement", async deadline =>
        {
            var wallet = run.player.GetComponent<PlayerWallet>();
            int rate = _account.Settings.goldPerUpgradeStone;
            int added = checked(rate * 8 + (rate - 1) - wallet.Gold % rate);
            Check(wallet.AddGold(added), "Could not seed the isolated run wallet.");
            int gold = wallet.Gold;
            int preSaveBalance = _account.Balance;
            var terminal = won ? RunState.Won : RunState.Lost;
            using (var profileLock = failSave
                ? new FileStream(_profilePath, FileMode.Open, FileAccess.Read, FileShare.Read) : null)
            {
                if (won) typeof(RunManager).GetMethod("EndRun", Hidden).Invoke(run, new object[] { true });
                else Check(run.player.TakeDamage(1_000_000f, isParryable: false).Killed, "Live lethal damage did not kill the player.");
                await Until(() => run.State == terminal, deadline, "terminal run state");
                if (failSave)
                    Check(run.IsUpgradeSaveBlocked && run.HasPendingUpgradeRewards && wallet.Gold == gold
                        && _account.Balance == preSaveBalance && ReadDiskBalance() == preSaveBalance,
                        "A blocked real file write spent gold or changed the saved balance.");
            }
            if (failSave)
            {
                await Until(() => !run.IsDeathPresentationPlaying, deadline, "death animation before retry");
                var overlay = run.resultUI.PresentationOverlay;
                Check(overlay.Title.text == "보상 저장 실패" && overlay.ActionButton.isActiveAndEnabled
                    && overlay.ActionButton.IsInteractable(), "The real save failure did not expose the retry button.");
                overlay.ActionButton.onClick.Invoke();
            }
            return await ObserveSettlement(run, won, gold, deadline, won ? "controlled EndRun(true)" : "actual lethal damage",
                failSave ? " Real file failure retained gold and balance, and the retry succeeded." : "");
        });
    }

    /// <summary>Arm before an actual stage exit, or call immediately after the terminal action.</summary>
    public static string ValidateCurrentSettlement(bool won, int expectedGold)
    {
        var run = RequireRun(playing: false);
        Check(expectedGold >= 0, "Expected gold cannot be negative.");
        return StartCheck("authored run exit settlement", deadline =>
            ObserveSettlement(run, won, expectedGold, deadline, "actual run exit", ""));
    }

    public static string Close()
    {
        Check(!_busy, "Wait for the live check to finish before removing its save override.");
        _accountScope?.Dispose();
        _accountScope = null;
        _account = null;
        _previousRun = null;
        return _status = "CLOSED: the QA profile remains under Library. Existing QA components retain their isolated account.";
    }

    private static async Task<string> ObserveSettlement(RunManager run, bool won, int gold,
        DateTime deadline, string scenario, string extra)
    {
        await Until(() => run.State == (won ? RunState.Won : RunState.Lost), deadline, "actual terminal run");
        Check(!run.IsUpgradeSaveBlocked && !run.HasPendingUpgradeRewards, "Terminal rewards are still blocked or pending.");
        var summary = run.ResultSummary;
        int converted = gold / _account.Settings.goldPerUpgradeStone;
        long total = summary.StageReward + summary.DeathReward + converted;
        Check(summary.GoldBeforeConversion == gold && summary.GoldConversionReward == converted
            && run.player.GetComponent<PlayerWallet>().Gold == 0 && summary.Balance == _startingBalance + total,
            "The live wallet, terminal reward and opening balance did not reconcile.");
        Check(ReadDiskBalance() == summary.Balance, "A fresh production FileStore read disagreed with the terminal balance.");
        var overlay = run.resultUI.PresentationOverlay;
        Check(overlay != null, "The live result is not using the authored overlay.");
        bool transferred = false;
        bool partial = false;
        while (!overlay.ResultRevealCompleted)
        {
            if (overlay.IsRewardTransferPlaying)
            {
                var amounts = ReadResult(overlay);
                Check(amounts.Stage + amounts.Death + amounts.Conversion + amounts.Balance == summary.Balance,
                    "Currency was lost or duplicated during the actual result animation.");
                transferred = true;
                partial |= amounts.Balance > _startingBalance && amounts.Balance < summary.Balance;
            }
            await NextTick(deadline, "result reward transfer");
        }
        Check((total == 0 || transferred) && (total < 2 || partial) && ReadResult(overlay) == (0L, 0L, 0L, (long)summary.Balance.Value),
            "The actual result transfer did not animate or settle correctly.");
        await Until(overlay.ActionButton.IsInteractable, deadline, "enabled result action");
        return $"PASS: {scenario}, {gold} gold to {converted} stones, authored result transfer, disk balance {summary.Balance}." + extra;
    }

    private static (long Stage, long Death, long Conversion, long Balance) ReadResult(BoundaryZeroRunOverlay overlay)
    {
        long Amount(string child) => long.Parse(overlay.ResultSummaryRoot.transform.Find(child).GetComponent<TMP_Text>().text,
            NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        return (Amount("StageRewardValue"), Amount("DeathRewardValue"), Amount("GoldConversionValue"), Amount("BalanceValue"));
    }

    private static int ReadDiskBalance()
    {
        var reloaded = new PlayerUpgradeService(_account.Settings,
            new PlayerUpgradeFileStore(_profilePath, _account.EngravingSettings), _account.EngravingSettings);
        Check(reloaded.IsReady, "Could not reload the production QA file: " + reloaded.LastError);
        return reloaded.Balance;
    }

    private static (ItemDefinition Item, List<string> Seeds, ItemSynergyDefinition[] Sets) FindSimultaneousSynergy(PlayerItemRuntime items)
    {
        var catalog = items.Catalog;
        var owned = new HashSet<string>(items.OwnedItems.Select(item => item.itemId), StringComparer.Ordinal);
        var keys = new HashSet<string>(items.OwnedItems.Select(item => item.candidateKey), StringComparer.Ordinal);
        bool Available(ItemDefinition item) => item != null && !owned.Contains(item.itemId) && !keys.Contains(item.candidateKey);
        foreach (var item in catalog.items.Where(Available))
        {
            var sets = catalog.synergies.Where(set => set?.members != null && set.members.Contains(item.itemId)).ToArray();
            if (sets.Length != 2 || sets.Any(set => set.tiers == null || !set.tiers.Any(tier => tier?.count == 2))) continue;
            var seeds = new List<string>(2);
            bool possible = true;
            for (int i = 0; i < 2; i++)
            {
                int count = sets[i].members.Count(owned.Contains);
                if (count > 1) { possible = false; break; }
                if (count == 1) continue;
                var seed = sets[i].members.Where(id => !sets[1 - i].members.Contains(id)).Select(catalog.Find).FirstOrDefault(Available);
                if (seed == null) { possible = false; break; }
                seeds.Add(seed.itemId);
            }
            if (possible) return (item, seeds, sets);
        }
        throw new InvalidOperationException("No pair of catalog synergies can be activated from the current inventory. Start a fresh QA run.");
    }

    private static RunManager RequireRun(bool playing)
    {
        var run = Object.FindFirstObjectByType<RunManager>();
        Check(Application.isPlaying && !_busy && _account != null && run != null && run != _previousRun
            && ReferenceEquals(typeof(RunManager).GetField("_upgradeService", Hidden).GetValue(run), _account),
            "Open the isolated Game Scene and wait for it to bind the production QA profile.");
        if (playing) Check(run.State == RunState.Playing && run.stageManager.IsReady, "Wait until the authored run is playing.");
        return run;
    }

    private static string StartCheck(string phase, Func<DateTime, Task<string>> check)
    {
        _busy = true;
        _status = "RUNNING: " + phase;
        _ = Run(check);
        return _status;
    }

    private static async Task Run(Func<DateTime, Task<string>> check)
    {
        try { _status = await check(DateTime.UtcNow.AddSeconds(75)); }
        catch (Exception failure) { _status = "FAIL: " + failure.GetBaseException().Message; }
        finally { _busy = false; }
    }

    private static async Task Until(Func<bool> condition, DateTime deadline, string phase)
    {
        while (!condition()) await NextTick(deadline, phase);
    }

    private static Task NextTick(DateTime deadline, string phase)
    {
        Check(Application.isPlaying && DateTime.UtcNow < deadline, "Timed out or left Play Mode during " + phase + ".");
        var completion = new TaskCompletionSource<bool>();
        void Tick() { EditorApplication.update -= Tick; completion.SetResult(true); }
        EditorApplication.update += Tick;
        return completion.Task;
    }

    private static void Check(bool passed, string error)
    {
        if (!passed) throw new InvalidOperationException(error);
    }
}
