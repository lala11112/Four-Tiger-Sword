using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Temporary-object checks for complete boss rewards and scoped music ownership.</summary>
public static class BossMusicRewardRegression
{
    private static int checks;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Studio Baekho/Enemy/Run Boss Reward Regression")]
    private static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run reward regression in Edit Mode.");
        checks = 0;
        GroupValidation();
        CompleteRewardReplacesLegacyGold();
        NonfinalDepletionDoesNotPay();
        DuplicateOwnershipAndSnapshot();
        ReentrantReinitialization();
        OverflowAndLifecycle();
        return $"Boss rewards: {checks} checks passed.";
    }

    public static string RunMusic()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run music regression in Play Mode.");
        checks = 0;
        var first = new GameObject("BossMusicRegression_A");
        var second = new GameObject("BossMusicRegression_B");
        var a = AudioClip.Create("BossMusicRegression_TrackA", 44100 * 4, 1, 44100, false);
        var b = AudioClip.Create("BossMusicRegression_TrackB", 44100 * 4, 1, 44100, false);
        int initialOwners = BossMusicService.ActiveOwnerCount;
        var initialOwner = BossMusicService.CurrentOwner;
        var initialClip = BossMusicService.CurrentClip;
        try
        {
            BossMusicService.Set(first, BossMusicMode.Keep, null);
            Check(BossMusicService.ActiveOwnerCount == initialOwners, "Keep created a music owner.");
            BossMusicService.Set(first, BossMusicMode.Play, a, 0.7f, 0f);
            Check(BossMusicService.CurrentOwner == first && BossMusicService.CurrentClip == a,
                "First music request did not become current.");
            var service = Object.FindFirstObjectByType<BossMusicService>(FindObjectsInactive.Include);
            var source = FindSource(service, a);
            Check(source != null && source.loop && source.spatialBlend == 0f && source.volume == 0.7f,
                "Music output is not a looping two-dimensional source.");
            source.timeSamples = 22050;
            float position = BossMusicService.PlaybackTime;
            BossMusicService.Set(first, BossMusicMode.Play, a, 0.6f, 0f);
            Check(BossMusicService.PlaybackTime >= position - 0.01f, "Same-clip phase request restarted the song.");
            BossMusicService.Set(second, BossMusicMode.Play, b, 0.8f, 0.5f);
            Check(BossMusicService.CurrentOwner == second && BossMusicService.CurrentClip == b,
                "Last explicit owner did not take priority.");
            Check(FindSource(service, a) != null && FindSource(service, b) != null,
                "Crossfade discarded the previous source immediately.");
            BossMusicService.Set(first, BossMusicMode.Keep, null);
            Check(BossMusicService.CurrentOwner == second, "Keep stole another boss's music.");
            typeof(BossMusicService).GetField("fadeElapsed", Hidden).SetValue(service, 0.5f);
            Invoke(service, "Update");
            Check(FindSource(service, a) == null && FindSource(service, b).volume == 0.8f,
                "Crossfade did not finish or release its outgoing source.");
            BossMusicService.Release(first, 0f);
            Check(BossMusicService.CurrentOwner == second, "Releasing a covered owner stopped the active boss.");
            BossMusicService.Set(first, BossMusicMode.Stop, null, 1f, 0f);
            Check(BossMusicService.CurrentOwner == first && BossMusicService.CurrentClip == null,
                "Stop did not claim explicit silence.");
            BossMusicService.Release(first, 0f);
            Check(BossMusicService.CurrentOwner == second && BossMusicService.CurrentClip == b,
                "Releasing Stop did not restore the surviving boss.");
            var currentSource = FindSource(service, b);
            currentSource.timeSamples = 33075;
            position = BossMusicService.PlaybackTime;
            BossMusicService.Set(first, BossMusicMode.Play, b, 0.4f, 0f);
            Check(BossMusicService.PlaybackTime >= position - 0.01f,
                "A different owner requesting the same song reset playback.");
            BossMusicService.Release(first, 0f);
            Check(BossMusicService.CurrentOwner == second && BossMusicService.PlaybackTime >= position - 0.01f,
                "Restoring the same song reset its playback position.");
            BossMusicService.Set(first, BossMusicMode.Play, a, float.NaN, 0f);
            BossMusicService.Set(first, BossMusicMode.Play, null, 1f, 0f);
            Check(BossMusicService.CurrentOwner == second, "An invalid request replaced valid music.");
            second.SetActive(false);
            Invoke(service, "Update");
            Check(BossMusicService.ActiveOwnerCount == initialOwners,
                "Inactive boss ownership was not pruned.");
            Check(BossMusicService.CurrentOwner == initialOwner && BossMusicService.CurrentClip == initialClip,
                "Last boss cleanup did not restore the pre-test music owner.");
            return $"Boss music: {checks} checks passed.";
        }
        finally
        {
            // Finish fades involving generated clips before destroying those clips.
            BossMusicService.Set(first, BossMusicMode.Stop, null, 1f, 0f);
            BossMusicService.Release(first, 0f); BossMusicService.Release(second, 0f);
            Object.Destroy(first); Object.Destroy(second); Object.Destroy(a); Object.Destroy(b);
        }
    }

    private static AudioSource FindSource(BossMusicService service, AudioClip clip)
    {
        foreach (var source in service.GetComponentsInChildren<AudioSource>())
            if (source.clip == clip) return source;
        return null;
    }

    private static void GroupValidation()
    {
        using var f = new Fixture();
        Check(f.Group.Validate(out _), "Default boss reward group is invalid.");
        f.Group.gold = -1;
        Check(!f.Group.Validate(out _), "Negative reward gold was accepted.");
        f.Group.gold = 0;
        f.Group.items.Add(null);
        Check(!f.Group.Validate(out _), "Missing item definitions were accepted.");
        f.Group.items.Clear();
        var a = f.Item("a"); var b = f.Item("b");
        f.Group.items.Add(a); f.Group.items.Add(b);
        Check(f.Group.Validate(out _), "Distinct item rewards were rejected.");
        b.candidateKey = a.candidateKey;
        Check(!f.Group.Validate(out _), "Duplicate reward identities were accepted.");
    }

    private static void CompleteRewardReplacesLegacyGold()
    {
        using var f = new Fixture();
        var item = f.Item("complete");
        f.Catalog.items = new[] { item };
        f.Group.gold = 75; f.Group.items.Add(item);
        var enemy = f.Enemy(out var reward);
        reward.Initialize(f.Wallet, 50);
        reward.Initialize(f.Wallet, f.Items, f.Group);
        enemy.TakeDamage(100f, poiseDamage: 0f);
        Check(f.Wallet.Gold == 75 && f.Items.Contains(item.itemId),
            "Boss reward did not replace default gold and grant its item.");
        RaiseDeath(enemy);
        reward.Initialize(f.Wallet, 50);
        RaiseDeath(enemy);
        Check(f.Wallet.Gold == 75, "Duplicate death/reinitialization paid the boss twice.");
    }

    private static void DuplicateOwnershipAndSnapshot()
    {
        using var f = new Fixture();
        var owned = f.Item("owned"); var external = f.Item("external");
        var alias = f.Item("alias"); alias.candidateKey = external.candidateKey;
        var fresh = f.Item("fresh");
        f.Catalog.items = new[] { owned, external, alias, fresh };
        Check(f.Items.TryAddItem(owned.itemId) && f.Items.TryAddItem(external.itemId), "Ownership setup failed.");
        f.Group.gold = 42; f.Group.items.AddRange(new[] { owned, alias, fresh });
        var enemy = f.Enemy(out var reward);
        reward.Initialize(f.Wallet, f.Items, f.Group);
        f.Group.gold = 100; f.Group.items.Clear();
        enemy.TakeDamage(100f, poiseDamage: 0f);
        Check(f.Wallet.Gold == 42 && f.Items.OwnedItems.Count == 3 && f.Items.Contains(fresh.itemId)
            && !f.Items.Contains(alias.itemId), "Rewards were not snapshotted or duplicate ownership was compensated.");
    }

    private static void NonfinalDepletionDoesNotPay()
    {
        using var f = new Fixture();
        var item = f.Item("final-phase");
        f.Catalog.items = new[] { item };
        f.Group.gold = 75; f.Group.items.Add(item);
        var boss = f.Boss(out var reward);
        reward.Initialize(f.Wallet, f.Items, f.Group);
        Check(!boss.TakeDamage(1000f, poiseDamage: 0f).Killed && boss.IsTransitioning
            && f.Wallet.Gold == 0 && !f.Items.Contains(item.itemId),
            "Nonfinal phase depletion must not pay gold or grant items.");
        typeof(Boss).GetMethod("TickPhaseTransition", Hidden).Invoke(boss, new object[] { 3f });
        Check(boss.PhaseIndex == 1 && f.Wallet.Gold == 0 && !f.Items.Contains(item.itemId),
            "Revival must keep rewards pending until the final confirmed death.");
        Check(boss.TakeDamage(1000f, poiseDamage: 0f).Killed && f.Wallet.Gold == 75 && f.Items.Contains(item.itemId),
            "Final-phase depletion must grant the complete boss reward once.");
        RaiseDeath(boss);
        Check(f.Wallet.Gold == 75 && f.Items.OwnedItems.Count == 1,
            "Repeating a final death notification must never pay a revived boss twice.");
    }

    private static void ReentrantReinitialization()
    {
        using var f = new Fixture();
        var a = f.Item("a"); var b = f.Item("b");
        f.Catalog.items = new[] { a, b };
        f.Group.gold = 25; f.Group.items.AddRange(new[] { a, b });
        var enemy = f.Enemy(out var reward);
        reward.Initialize(f.Wallet, f.Items, f.Group);
        f.Wallet.GoldChanged += _ => reward.Initialize(f.Wallet, 999);
        f.Items.OnOwnedItemsChanged += () => reward.Initialize(f.Wallet, 999);
        enemy.TakeDamage(100f, poiseDamage: 0f);
        Check(f.Wallet.Gold == 25 && f.Items.Contains(a.itemId) && f.Items.Contains(b.itemId),
            "Observer reinitialization changed the committed death reward.");
    }

    private static void OverflowAndLifecycle()
    {
        using var f = new Fixture(int.MaxValue);
        var item = f.Item("overflow-item"); f.Catalog.items = new[] { item };
        f.Group.gold = 1; f.Group.items.Add(item);
        var enemy = f.Enemy(out var reward);
        reward.Initialize(f.Wallet, f.Items, f.Group);
        enemy.TakeDamage(100f, poiseDamage: 0f);
        Check(f.Wallet.Gold == int.MaxValue && f.Items.Contains(item.itemId),
            "Gold overflow wrapped the wallet or prevented independent item rewards.");
        var inactive = f.Enemy(out var inactiveReward);
        inactiveReward.Initialize(f.Wallet, f.Items, f.Group);
        inactiveReward.enabled = false; Invoke(inactiveReward, "OnDisable");
        RaiseDeath(inactive);
        Check(f.Items.OwnedItems.Count == 1, "Disabled rewards granted items.");
        var outsider = f.Item("outsider"); f.Group.items.Clear(); f.Group.items.Add(outsider);
        bool rejected = false;
        try { inactiveReward.Initialize(f.Wallet, f.Items, f.Group); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "An item outside the run catalog was accepted.");
    }

    private static void RaiseDeath(Enemy enemy)
        => ((Action)typeof(Enemy).GetField("OnDied", Hidden).GetValue(enemy))?.Invoke();
    private static void Invoke(object target, string method)
        => target.GetType().GetMethod(method, Hidden).Invoke(target, null);
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException("Boss music/reward regression: " + message); }

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject root;
        private readonly List<Object> owned = new List<Object>();
        public readonly ItemCatalog Catalog;
        public readonly BossRewardGroupSO Group;
        public readonly PlayerWallet Wallet;
        public readonly PlayerItemRuntime Items;

        public Fixture(int gold = 0)
        {
            root = new GameObject("Boss reward regression") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            Catalog = Own(ScriptableObject.CreateInstance<ItemCatalog>()); Catalog.projectId = "boss-reward-regression";
            Group = Own(ScriptableObject.CreateInstance<BossRewardGroupSO>());
            var data = Own(ScriptableObject.CreateInstance<PlayerStatData>()); data.baseHP = 100f;
            var stats = root.AddComponent<PlayerStatManager>(); stats.Initialize(data);
            Wallet = root.AddComponent<PlayerWallet>(); Wallet.Initialize(gold);
            Items = root.AddComponent<PlayerItemRuntime>(); Items.Catalog = Catalog;
            Items.Initialize(stats, () => ElementType.ELEMENT_WATER);
            root.SetActive(true);
        }

        public ItemDefinition Item(string id)
        {
            var item = Own(ScriptableObject.CreateInstance<ItemDefinition>());
            item.itemId = item.displayName = id; item.candidateKey = "candidate-" + id;
            item.projectId = Catalog.projectId;
            return item;
        }

        public Enemy Enemy(out EnemyGoldReward reward)
        {
            var actor = new GameObject("Reward actor") { hideFlags = HideFlags.HideAndDontSave };
            actor.SetActive(false); actor.transform.SetParent(root.transform, false);
            var enemy = actor.AddComponent<Enemy>(); enemy.EnemyStat = actor.GetComponent<EnemyStat>();
            enemy.EnemyStat.CurrentHp = 10f;
            actor.GetComponent<PoiseHandler>().ResetPoise();
            reward = actor.AddComponent<EnemyGoldReward>(); actor.SetActive(true);
            return enemy;
        }

        public ConfiguredBoss Boss(out EnemyGoldReward reward)
        {
            var actor = new GameObject("Reward revival boss") { hideFlags = HideFlags.HideAndDontSave };
            actor.SetActive(false); actor.transform.SetParent(root.transform, false);
            var boss = actor.AddComponent<ConfiguredBoss>(); boss.EnemyStat = actor.GetComponent<EnemyStat>();
            var data = Own(ScriptableObject.CreateInstance<BossData>()); data.maxHp = 100;
            var first = Own(ScriptableObject.CreateInstance<BossPhaseData>()); first.phaseMaxHp = 60f;
            var second = Own(ScriptableObject.CreateInstance<BossPhaseData>()); second.phaseMaxHp = 40f;
            typeof(Boss).GetField("BossData", Hidden).SetValue(boss, data);
            typeof(Boss).GetField("PhaseDatas", Hidden).SetValue(boss, new List<BossPhaseData> { first, second });
            typeof(EnemyStat).GetField("baseData", Hidden).SetValue(boss.EnemyStat, data);
            typeof(EnemyStat).GetMethod("InitializeFromData", Hidden).Invoke(boss.EnemyStat, null);
            boss.StaggerResistLevel = StaggerResistLevel.SUPER_ARMOR;
            reward = actor.AddComponent<EnemyGoldReward>(); actor.SetActive(true);
            typeof(Boss).GetMethod("CreateBrain", Hidden).Invoke(boss, null);
            return boss;
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
        public void Dispose()
        {
            Object.DestroyImmediate(root);
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
        }
    }
}
