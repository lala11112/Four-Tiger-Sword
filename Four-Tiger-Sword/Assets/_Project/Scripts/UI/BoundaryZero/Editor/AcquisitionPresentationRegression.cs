using System;
using System.Collections.Generic;
using System.Reflection;
using ArpgHud.PC;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Checks committed rewards and mixed notification rows using temporary objects only.</summary>
public static class AcquisitionPresentationRegression
{
    private static int _checks;
    [MenuItem("Tools/Studio Baekho/BoundaryZERO/Run Acquisition Regression")]
    private static void Menu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run acquisition regression in Edit Mode.");
        _checks = 0;
        Transactions();
        SynergyThresholds();
        MixedRows();
        return $"Acquisition presentation: {_checks} checks passed.";
    }

    private static void Transactions()
    {
        var objects = new List<Object>();
        var root = new GameObject("Acquisition regression");
        root.SetActive(false);
        try
        {
            var data = ScriptableObject.CreateInstance<PlayerStatData>(); objects.Add(data);
            data.baseHP = data.baseSP = 100;
            var stats = root.AddComponent<PlayerStatManager>(); stats.Initialize(data);
            var wallet = root.AddComponent<PlayerWallet>();
            int goldCalls = 0, gained = 0;
            wallet.GoldGained += n => { goldCalls++; gained += n; };
            wallet.Initialize(100);
            Check(goldCalls == 0, "Initial balance generated loot.");
            Check(wallet.AddGold(25) && goldCalls == 1 && gained == 25, "Credit did not publish its actual amount once.");
            Check(wallet.TrySpend(10) && wallet.AddGold(0) && !wallet.AddGold(-1)
                && !wallet.AddGold(int.MaxValue) && goldCalls == 1, "Spend/zero/invalid/overflow published a gain.");

            var catalog = ScriptableObject.CreateInstance<ItemCatalog>(); objects.Add(catalog);
            catalog.projectId = "acquisition-regression";
            var a = Item("a", catalog.projectId); var b = Item("b", catalog.projectId);
            objects.Add(a); objects.Add(b); catalog.items = new[] { a, b };
            var items = root.AddComponent<PlayerItemRuntime>();
            int itemCalls = 0; ItemDefinition last = null;
            items.ItemAcquired += item => { itemCalls++; last = item; };
            items.Catalog = catalog;
            items.Initialize(stats, () => ElementType.ELEMENT_WATER);
            Check(itemCalls == 0, "Inventory initialization generated loot.");
            Check(items.TryAddItem("a") && itemCalls == 1 && last == a, "Item grant did not publish once.");
            Check(!items.TryAddItem("a") && !items.TryAddItem("missing") && itemCalls == 1, "Rejected item generated loot.");
            Check(items.TryAddItem("b") && items.TryExchangeItems("a", "b", "a")
                && itemCalls == 3 && last == a && items.Contains("a") && !items.Contains("b"),
                "Fusion returning a consumed ingredient must still publish an acquisition.");
            Check(!items.TryExchangeItems("a", "b", "a") && itemCalls == 3, "Failed fusion published loot.");
            var settings = ScriptableObject.CreateInstance<EconomySettingsSO>(); objects.Add(settings);
            var trades = new ShopTransactions(items, wallet, stats, settings, 1);
            int before = wallet.Gold;
            Check(trades.TrySell("a") == ShopSaleResult.Success && goldCalls == 2
                && wallet.Gold > before && gained == 25 + wallet.Gold - before && itemCalls == 3,
                "Sale must publish only its credited gold.");
            Check(trades.TrySell("a") == ShopSaleResult.NotOwned && goldCalls == 2, "Rejected sale published loot.");
            Check(items.TryAddItem("b") && items.TryRemoveItem("b") && itemCalls == 4, "Removal generated an acquisition.");
            wallet.GoldGained += _ => throw new InvalidOperationException("Expected acquisition observer failure");
            wallet.GoldGained += _ => gained++;
            Check(wallet.AddGold(1) && goldCalls == 3 && gained == 25 + wallet.Gold - before + 1,
                "A throwing reward observer prevented later observers or the committed credit.");
            items.ItemAcquired += _ => throw new InvalidOperationException("Expected item observer failure");
            int tail = 0; items.ItemAcquired += _ => tail++;
            Check(items.TryAddItem("a") && tail == 1 && items.Contains("a"), "A throwing item observer disrupted acquisition.");
        }
        finally
        {
            Object.DestroyImmediate(root);
            foreach (var value in objects) Object.DestroyImmediate(value);
        }
    }

    private static ItemDefinition Item(string id, string project)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        item.itemId = item.displayName = id; item.candidateKey = id; item.projectId = project;
        item.purchasePrice = 20; item.effects = Array.Empty<ItemEffect>();
        return item;
    }

    private static void SynergyThresholds()
    {
        var root = new GameObject("Synergy acquisition regression");
        root.SetActive(false);
        var catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        var data = ScriptableObject.CreateInstance<PlayerStatData>();
        try
        {
            data.baseHP = data.baseSP = 100;
            var stats = root.AddComponent<PlayerStatManager>();
            stats.Initialize(data);
            catalog.projectId = "synergy-acquisition-regression";
            var ids = new[] { "a", "b", "c", "d", "e", "f", "g" };
            catalog.items = Array.ConvertAll(ids, id => Item(id, catalog.projectId));
            catalog.synergies = new[]
            {
                new ItemSynergyDefinition { id = "first", members = new[] { "a", "b", "c", "d" },
                    tiers = new[] { new ItemSynergyTier { count = 2 }, new ItemSynergyTier { count = 4 } } },
                new ItemSynergyDefinition { id = "second", members = new[] { "d", "g" },
                    tiers = new[] { new ItemSynergyTier { count = 2 } } }
            };
            var items = root.AddComponent<PlayerItemRuntime>();
            items.Catalog = catalog;
            items.Initialize(stats, () => ElementType.ELEMENT_WATER);
            var events = new List<string>();
            items.ItemAcquired += item => events.Add(item.itemId);
            items.SynergyTierActivated += state => events.Add(state.Definition.id + state.ActiveTier.count);
            foreach (string id in new[] { "a", "b", "c", "d", "e", "f" })
                Check(items.TryAddItem(id), "Synergy threshold fixture could not acquire an item.");
            Check(string.Join(" ", events) == "a b first2 c d first4 e f",
                "Only newly active or raised tiers should follow the committed item notification.");
            events.Clear();
            Check(items.TryExchangeItems("e", "f", "g") && string.Join(" ", events) == "g second2",
                "Atomic fusion should report only its net synergy activation.");
            events.Clear();
            Check(!items.TryAddItem("g") && !items.TryExchangeItems("e", "f", "g") && items.TryRemoveItem("b"),
                "Duplicate, failed fusion and tier loss fixture failed.");
            items.RecalculateForCurrentForm();
            Check(events.Count == 0, "Rejected grants, tier loss and stat refresh must remain quiet.");
        }
        finally
        {
            Object.DestroyImmediate(root);
            foreach (var item in catalog.items ?? Array.Empty<ItemDefinition>()) Object.DestroyImmediate(item);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(data);
        }
    }

    private static void MixedRows()
    {
        var prefab = Resources.Load<GameObject>("BoundaryUI/GoldHUD/GoldRewardFeed");
        Check(prefab != null, "Authored reward feed is missing.");
        foreach (var graphic in prefab.GetComponentsInChildren<BoundaryZeroUI.BoundaryGoldFrameGraphic>(true))
            Check(graphic.GetComponent<CanvasRenderer>() != null, "An authored reward frame or coin cannot render.");
        var root = Object.Instantiate(prefab);
        try
        {
            var feed = root.GetComponent<PentaGoldRewardFeed>();
            feed.ShowGoldGain(10); feed.ShowGoldGain(20);
            Check(feed.ActiveRowCount == 1 && feed.GetRow(0).Amount == 30, "Gold burst did not merge.");
            feed.ShowItemGain("아주 긴 한글 아이템 이름 <테스트>", null);
            feed.ShowGoldGain(5);
            Check(feed.ActiveRowCount == 3 && !feed.GetRow(1).IsGold && feed.GetRow(2).IsGold
                && feed.GetRow(2).Amount == 5, "Gold crossed an item row while merging.");
            var text = feed.GetRow(1).GetComponentInChildren<TMPro.TMP_Text>(true);
            Check(text.text.EndsWith(" 획득") && !text.richText, "Item names must display as literal text.");
            feed.ShowItemGain("두 번째 아이템", null);
            Check(feed.ActiveRowCount == 3 && !feed.GetRow(0).IsGold && !feed.GetRow(2).IsGold,
                "Overflow must recycle only the oldest row.");
            foreach (var graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                Check(!graphic.raycastTarget, "A notification intercepts input.");
            typeof(PentaGoldRewardFeed).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(feed, new object[] { Time.unscaledTimeAsDouble + 5d });
            Check(feed.ActiveRowCount == 0, "Rows did not expire.");
            feed.ShowGoldGain(7); feed.Clear();
            Check(feed.ActiveRowCount == 0, "Clear retained rows.");
            feed.ShowItemGain("유물", null);
            feed.ShowSynergyActivation("수호", 2, null);
            var synergy = feed.GetRow(1)?.GetComponentInChildren<TMPro.TMP_Text>(true);
            Check(synergy != null && synergy.text == "수호 2세트 활성화" && !synergy.richText,
                "Synergy activation must have a distinct row after the acquired item.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}
