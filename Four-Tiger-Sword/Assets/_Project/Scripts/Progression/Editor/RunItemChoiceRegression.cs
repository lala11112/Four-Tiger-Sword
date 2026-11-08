using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Reward behavior against the installed 59-item catalog, using isolated run inventories.</summary>
public static class RunItemChoiceRegression
{
    private static int _checks;
    [MenuItem("Tools/Studio Baekho/Progression/Run Item Choice Regression")]
    private static void FromMenu() => Debug.Log(Run());
    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run item-choice checks in Edit Mode.");
        _checks = 0;
        StarterPoolsAndSynergy();
        RoomCandidateCounts();
        GlobalRerollsAndOneHeldCandidate();
        NoChangeDoesNotConsumeReroll();
        EmptyRewardAndOverflow();
        RetryReceiptLedger();
        PendingReceiptRecovery();
        NextTierWeights();
        GrantReentrancy();
        return $"PASS: item reward choices ({_checks} checks: installed 59 items, starter pools, 3/4 choices, global rerolls, held candidate, empty reward, retries, synergy and reentry).";
    }

    private static void StarterPoolsAndSynergy()
    {
        using (var f = new Fixture("heirloom", "starterelement", "reroll"))
        {
            Check(f.Catalog.items.Length == 59 && f.Catalog.items.Count(i => i.grade == 1) == 13,
                "Installed source is not the agreed 59-item/13-starter catalog.");
            Check(f.Service.StarterSynergies().Select(s => s.id).SequenceEqual(new[] { "S01", "S03", "S10", "S15", "S16", "S19" }),
                "Starter categories must contain exactly the six synergies with eligible grade-one items.");
            Check(f.Service.BeginStarterChoice() && f.Service.Current.NeedsSynergy, "Starter did not request a category.");
            string receipt = f.Service.Current.Id;
            Check(!f.Service.TryReroll(receipt) && f.Service.RerollsRemaining == 3, "Starter consumed a normal-reward reroll.");
            Check(!f.Service.TryChooseStarterSynergy(receipt, "S04") && f.Service.Current.NeedsSynergy,
                "An empty grade-one category was accepted.");
            Check(f.Service.TryChooseStarterSynergy(receipt, "S01") && f.Items.Contains("I13")
                && f.Service.Current == null && f.Items.OwnedItems.Count == 1, "Single-candidate starter did not resolve once.");
            Check(!f.Service.BeginStarterChoice() && !f.Service.TryClaim(receipt, "I13"), "Starter receipt paid twice.");
        }
        using (var f = new Fixture("heirloom", "starterelement"))
        {
            f.Service.BeginStarterChoice(); string receipt = f.Service.Current.Id;
            Check(f.Service.TryChooseStarterSynergy(receipt, "S19"), "Valid starter category failed.");
            var current = f.Service.Current;
            Check(current != null && current.Candidates.Count == 3
                && current.Candidates.All(i => i.grade == 1 && f.Catalog.synergies.First(s => s.id == "S19").members.Contains(i.itemId)),
                "Starter candidates escaped their selected synergy or grade.");
            Distinct(current);
            Check(f.Service.TryClaim(receipt, current.Candidates[0].itemId) && f.Items.OwnedItems.Count == 1,
                "Starter granted more than the selected item.");
        }
        using (var f = new Fixture("heirloom"))
        {
            Check(f.Service.BeginStarterChoice() && !f.Service.Current.NeedsSynergy
                && f.Service.Current.Candidates.Count == 3 && f.Service.Current.Candidates.All(i => i.grade == 1),
                "Basic starter should draw three distinct grade-one candidates.");
            Distinct(f.Service.Current);
        }
    }

    private static void RoomCandidateCounts()
    {
        using (var f = new Fixture())
        {
            Check(f.Service.BeginRoomReward("0:0:1") && f.Service.Current.Candidates.Count == 3, "Base room reward does not offer three choices.");
            Distinct(f.Service.Current);
            string id = f.Service.Current.Id; string item = f.Service.Current.Candidates[0].itemId;
            Check(f.Service.TryClaim(id, item) && f.Items.OwnedItems.Count == 1, "Room reward granted the wrong count.");
            Check(!f.Service.BeginRoomReward("0:0:1") && !f.Service.TryClaim(id, item), "Room receipt paid again.");
        }
        using (var f = new Fixture("choices"))
        {
            Check(f.Service.BeginRoomReward("0:0:1") && f.Service.Current.Candidates.Count == 4, "Expanded choices did not offer four candidates.");
            Distinct(f.Service.Current);
        }
    }

    private static void GlobalRerollsAndOneHeldCandidate()
    {
        using var f = new Fixture("choices", "reroll", "hold");
        Check(f.Service.RerollsRemaining == 3, "Reroll account did not start at three.");
        for (int room = 0; room < 3; room++)
        {
            Check(f.Service.BeginRoomReward("0:0:" + room), "Fresh room receipt failed.");
            var current = f.Service.Current;
            string first = current.Candidates[0].itemId, second = current.Candidates[1].itemId;
            Check(f.Service.ToggleHeldCandidate(current.Id, first) && f.Service.ToggleHeldCandidate(current.Id, second)
                && current.HeldId == second, "Changing the held candidate retained multiple candidates.");
            var old = current.Candidates.Select(i => i.itemId).ToHashSet();
            Check(f.Service.TryReroll(current.Id) && current.RerollIndex == 1
                && f.Service.RerollsRemaining == 2 - room && current.Candidates.Count == 4,
                "Reroll count reset between room receipts or changed reward capacity.");
            Check(current.Candidates.Any(i => i.itemId == second)
                && current.Candidates.Where(i => i.itemId != second).All(i => !old.Contains(i.itemId)),
                "Reroll lost the held candidate or failed to replace the remaining eligible slots.");
            Distinct(current);
            Check(f.Service.TryClaim(current.Id, second), "Rerolled choice could not be claimed.");
        }
        f.Service.BeginRoomReward("0:1:1");
        string before = Signature(f.Service.Current);
        Check(!f.Service.CanReroll && !f.Service.TryReroll(f.Service.Current.Id)
            && f.Service.RerollsRemaining == 0 && Signature(f.Service.Current) == before,
            "Exhausted global rerolls changed a later stage reward.");
    }

    private static void NoChangeDoesNotConsumeReroll()
    {
        using var f = new Fixture("reroll", "hold");
        var available = f.Catalog.items.Take(3).Select(i => i.itemId).ToHashSet();
        foreach (var item in f.Catalog.items) if (!available.Contains(item.itemId)) Check(f.Items.TryAddItem(item.itemId), "Saturation fixture grant failed.");
        Check(f.Service.BeginRoomReward("small-pool") && f.Service.Current.Candidates.Count == 3, "Small reward pool failed.");
        var current = f.Service.Current;
        f.Service.ToggleHeldCandidate(current.Id, current.Candidates[0].itemId);
        string before = Signature(current);
        Check(!f.Service.CanReroll && !f.Service.TryReroll(current.Id) && f.Service.RerollsRemaining == 3
            && current.RerollIndex == 0 && Signature(current) == before, "An unchanged reroll consumed an attempt.");
    }

    private static void EmptyRewardAndOverflow()
    {
        using var f = new Fixture();
        foreach (var item in f.Catalog.items) Check(f.Items.TryAddItem(item.itemId), "Full-catalog fixture grant failed.");
        Check(f.Service.BeginRoomReward("empty") && f.Service.Current.Candidates.Count == 0, "A fully owned catalog produced an item candidate.");
        string receipt = f.Service.Current.Id;
        Check(f.Service.TryClaimEmptyReward(receipt) && f.Wallet.Gold == 10 && f.Service.Current == null,
            "Empty reward did not pay ten gold exactly once.");
        Check(!f.Service.TryClaimEmptyReward(receipt) && !f.Service.BeginRoomReward("empty") && f.Wallet.Gold == 10,
            "An empty receipt paid again.");
        Check(f.Wallet.AddGold(int.MaxValue - 10), "Overflow fixture funding failed.");
        f.Service.BeginRoomReward("overflow"); receipt = f.Service.Current.Id;
        Check(!f.Service.TryClaimEmptyReward(receipt) && f.Wallet.Gold == int.MaxValue
            && f.Service.Current != null && !f.Service.Current.Resolved, "Overflow lost a pending empty reward.");
        f.Wallet.TrySpend(10);
        Check(f.Service.TryClaimEmptyReward(receipt) && f.Wallet.Gold == int.MaxValue, "Retrying an empty reward did not settle once.");
    }

    private static void RetryReceiptLedger()
    {
        var ledger = new RunItemRewardLedger(); ledger.BeginGeneration(0, 0);
        Check(ledger.TryRecord(1, out var first) && first == "0:0:1" && !ledger.TryRecord(1, out _), "A repeated room paid twice.");
        Check(ledger.TryRecord(2, out var second) && second == "0:0:2", "Second clear receipt is incorrect.");
        ledger.BeginGeneration(0, 0);
        Check(!ledger.TryRecord(101, out _) && !ledger.TryRecord(102, out _) && ledger.TryRecord(103, out var third)
            && third == "0:0:3", "Regeneration paid below its prior maximum or suppressed a new maximum.");
        ledger.BeginGeneration(0, 1);
        Check(ledger.TryRecord(1, out var next) && next == "0:1:1", "A new stage reused the previous stage ledger.");
        ledger.BeginGeneration(0, 0);
        Check(!ledger.TryRecord(201, out _) && !ledger.TryRecord(202, out _), "Returning to an earlier stage reset its earned maximum.");
    }

    private static void NextTierWeights()
    {
        using var f = new Fixture("synergy");
        var set = f.Catalog.synergies.First(s => s.id == "S01");
        var members = set.members.Select(f.Catalog.Find).ToArray();
        Check(f.Service.RewardWeight(members[0]) == 1f, "An unstarted synergy was weighted as a completion.");
        for (int count = 1; count <= 5; count++)
        {
            Check(f.Items.TryAddItem(members[count - 1].itemId), "Synergy fixture grant failed.");
            float expected = count % 2 == 1 ? 3f : 1f;
            Check(f.Service.RewardWeight(members[count]) == expected, "Completion weight does not follow the next 2/4/6 threshold.");
        }
        Check(f.Service.RewardWeight(f.Catalog.Find("I89")) == 1f, "Completion weighting affected an unrelated item.");
    }

    private static void PendingReceiptRecovery()
    {
        using (var f = new Fixture())
        {
            var inbox = new RunItemRewardInbox();
            Check(inbox.Enqueue("reserved") && !inbox.Enqueue("reserved"), "Pending receipt duplicated itself.");
            inbox.Flush(_ => false);
            Check(inbox.Count == 1, "Unavailable composition discarded its committed clear receipt.");
            f.Service.BeginRoomReward("active-grant"); var current = f.Service.Current;
            bool retainedWhileBusy = false;
            f.Items.OnOwnedItemsChanged += () =>
            {
                inbox.Flush(f.Service.BeginRoomReward);
                retainedWhileBusy = inbox.Count == 1;
            };
            Check(f.Service.TryClaim(current.Id, current.Candidates[0].itemId) && retainedWhileBusy
                && inbox.Count == 1 && f.Service.Current == null, "Busy fulfillment lost the pending room clear.");
            inbox.Flush(f.Service.BeginRoomReward);
            Check(inbox.Count == 0 && f.Service.Current?.Id == "room:reserved"
                && !inbox.Enqueue("reserved"), "Deferred clear did not recover exactly once after fulfillment.");
        }
        var queue = new RunItemRewardInbox(); queue.Enqueue("a"); queue.Enqueue("b");
        var accepted = new List<string>(); int nestedAccepts = 0;
        queue.Flush(receipt =>
        {
            queue.Flush(_ => { nestedAccepts++; return true; });
            if (receipt == "b") return false;
            accepted.Add(receipt); return true;
        });
        Check(queue.Count == 1 && accepted.SequenceEqual(new[] { "a" }) && nestedAccepts == 0,
            "Reentrant flushing duplicated a receipt or discarded the rejected suffix.");
        queue.Flush(receipt => { accepted.Add(receipt); return true; });
        Check(queue.Count == 0 && accepted.SequenceEqual(new[] { "a", "b" }), "Recovery changed clear receipt order.");
        queue.Enqueue("throwing");
        try { queue.Flush(_ => throw new InvalidOperationException("Simulated composition failure")); }
        catch (InvalidOperationException) { }
        Check(queue.Count == 1, "A composition exception discarded the pending receipt.");
    }

    private static void GrantReentrancy()
    {
        using (var f = new Fixture("reroll"))
        {
            f.Service.BeginRoomReward("grant"); var current = f.Service.Current;
            bool blocked = false;
            f.Items.OnOwnedItemsChanged += () => blocked = !f.Service.TryClaim(current.Id, current.Candidates[0].itemId)
                && !f.Service.TryReroll(current.Id) && !f.Service.BeginRoomReward("nested");
            Check(f.Service.TryClaim(current.Id, current.Candidates[0].itemId) && blocked
                && f.Items.OwnedItems.Count == 1 && f.Service.Current == null, "Item fulfillment observers reentered a reward grant.");
        }
        using (var f = new Fixture())
        {
            foreach (var item in f.Catalog.items) f.Items.TryAddItem(item.itemId);
            f.Service.BeginRoomReward("gold-grant"); string id = f.Service.Current.Id; bool blocked = false;
            f.Wallet.GoldChanged += _ => blocked = !f.Service.TryClaimEmptyReward(id) && !f.Service.BeginRoomReward("nested-gold");
            Check(f.Service.TryClaimEmptyReward(id) && blocked && f.Wallet.Gold == 10 && f.Service.Current == null,
                "Gold fulfillment observers reentered an empty reward grant.");
        }
    }

    private static void Distinct(RunItemChoiceSession session)
    { Check(session.Candidates.Select(i => i.itemId).Distinct().Count() == session.Candidates.Count
        && session.Candidates.Select(i => i.candidateKey).Distinct().Count() == session.Candidates.Count,
        "A candidate list duplicates an ID or immutable content identity."); }
    private static string Signature(RunItemChoiceSession session) => string.Join("|", session.Candidates.Select(i => i.itemId));
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException("Run item choices: " + message); }

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject _root;
        private readonly PlayerStatData _statsData;
        private readonly EngravingSettingsSO _settings;
        public readonly ItemCatalog Catalog;
        public readonly PlayerItemRuntime Items;
        public readonly PlayerWallet Wallet;
        public readonly RunItemChoiceService Service;
        public Fixture(params string[] nodes)
        {
            Catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Generated/BoundaryInventory/ItemCatalog.asset");
            if (Catalog == null) throw new InvalidOperationException("Install the BoundaryInventory 59-item catalog before running reward checks.");
            _root = new GameObject("Run item choice fixture"); _root.SetActive(false);
            _statsData = ScriptableObject.CreateInstance<PlayerStatData>(); _statsData.baseHP = 1000;
            var stats = _root.AddComponent<PlayerStatManager>(); stats.Initialize(_statsData);
            Items = _root.AddComponent<PlayerItemRuntime>(); Items.Catalog = Catalog;
            Items.Initialize(stats, () => ElementType.ELEMENT_NONE);
            Wallet = _root.AddComponent<PlayerWallet>(); Wallet.Initialize(0);
            _settings = ScriptableObject.CreateInstance<EngravingSettingsSO>();
            Service = new RunItemChoiceService(Items, Wallet, new EngravingSnapshot(_settings, nodes), 812763);
            _root.SetActive(true);
        }
        public void Dispose()
        { Object.DestroyImmediate(_root); Object.DestroyImmediate(_statsData); Object.DestroyImmediate(_settings); }
    }
}
