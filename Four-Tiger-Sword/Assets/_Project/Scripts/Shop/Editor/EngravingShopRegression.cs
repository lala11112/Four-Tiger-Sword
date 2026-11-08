using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Isolated progression commerce checks; never opens a player save or changes scene assets.</summary>
public static class EngravingShopRegression
{
    private static int _checks;
    [MenuItem("Tools/Studio Baekho/Progression/Run Engraving Shop Regression")]
    private static void FromMenu() => Debug.Log(Run());
    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run commerce checks in Edit Mode.");
        _checks = 0;
        PricesAndSale();
        PaidRefreshRetainsPotion();
        EmptyRefreshDoesNotCharge();
        FusionChoiceCommitsOnce();
        StaleChoiceIsRejected();
        return $"PASS: engraving commerce ({_checks} checks: rounded prices, sale, paid seeded stock, potion state, choice cancellation and atomic fusion).";
    }

    private static void PricesAndSale()
    {
        using var f = new Fixture("discount", "sell");
        var item = f.Item("odd-price", 1, 21);
        f.SetCatalog(item, f.Item("other", 1, 40));
        var stock = f.Stock();
        ShopOffer offer = null;
        foreach (var candidate in stock.Offers) if (candidate.Item == item) offer = candidate;
        Check(offer != null && offer.Price == 19, "10% discount did not round purchase upward.");
        var trades = f.Trades();
        int before = f.Wallet.Gold;
        Check(trades.TryBuy(stock, offer.Id) == ShopPurchaseResult.Success && f.Wallet.Gold == before - 19,
            "Discounted fulfillment and payment disagree.");
        Check(trades.TryGetSalePrice(item.itemId, out int sale) && sale == 12,
            "60% sale did not round downward.");
        Check(trades.TrySell(item.itemId) == ShopSaleResult.Success && !f.Items.Contains(item.itemId)
            && f.Wallet.Gold == before - 7, "Sale repeated a bonus or created a purchase/sale profit.");
    }

    private static void PaidRefreshRetainsPotion()
    {
        using var f = new Fixture("discount", "refresh", "stock");
        var definitions = new ItemDefinition[10];
        for (int i = 0; i < definitions.Length; i++) definitions[i] = f.Item("item-" + i, 1, 20);
        f.SetCatalog(definitions);
        var stock = f.Stock(); var twin = f.Stock(); var plain = ShopStock.Create(f.Catalog, f.Items, f.Settings, 51);
        Check(stock.ItemSlotCount == plain.ItemSlotCount + 1 && stock.RefreshCost == 15,
            "Extra display slot or refresh discount is missing.");
        ShopOffer item = null, potion = null;
        foreach (var offer in stock.Offers) { if (offer.IsHealing) potion = offer; else item ??= offer; }
        f.Stats.ConsumeHealth(50f);
        var trades = f.Trades();
        Check(trades.TryBuy(stock, item.Id) == ShopPurchaseResult.Success, "Stock fixture purchase failed.");
        Check(trades.TryBuy(stock, potion.Id) == ShopPurchaseResult.Success && potion.SoldOut,
            "Potion fixture purchase failed.");
        int gold = f.Wallet.Gold;
        Check(trades.TryRefresh(stock) == ShopRefreshResult.Success && f.Wallet.Gold == gold - 15
            && stock.RefreshCount == 1, "Refresh did not charge exactly once.");
        int slots = 0; var identities = new HashSet<string>(); bool samePotion = false;
        foreach (var offer in stock.Offers)
        {
            if (offer.IsHealing) { samePotion = ReferenceEquals(offer, potion) && offer.SoldOut; continue; }
            slots++;
            Check(!f.Items.Contains(offer.Item.itemId) && identities.Add(offer.Item.candidateKey),
                "Paid stock contains an owned item or duplicate content identity.");
        }
        Check(slots == stock.ItemSlotCount && samePotion, "Refresh changed fixed slots or restored its potion.");
        Check(trades.TryRefresh(twin) == ShopRefreshResult.Success && Signature(stock) == Signature(twin),
            "The same stage, ownership and committed refresh index produced different products.");
    }

    private static void EmptyRefreshDoesNotCharge()
    {
        using var f = new Fixture();
        var item = f.Item("only", 1, 20); f.SetCatalog(item);
        var stock = f.Stock(); var trades = f.Trades(); int gold = f.Wallet.Gold;
        Check(trades.TryRefresh(stock) == ShopRefreshResult.NoCandidates && f.Wallet.Gold == gold
            && stock.RefreshCount == 0, "An unchanged stock charged a refresh.");
        Check(f.Items.TryAddItem(item.itemId), "Saturation fixture grant failed.");
        Check(trades.TryRefresh(stock) == ShopRefreshResult.NoCandidates && f.Wallet.Gold == gold,
            "A fully owned catalog charged a refresh.");
    }

    private static void FusionChoiceCommitsOnce()
    {
        using var f = new Fixture("fusion");
        var a = f.Item("a", 2, 40); var b = f.Item("b", 2, 40);
        f.SetCatalog(a, b, f.Item("c", 3, 80), f.Item("d", 3, 80), f.Item("e", 3, 80), f.Item("f", 3, 80));
        f.Settings.fusionUpgradeChance = 1f;
        Check(f.Items.TryAddItem("a") && f.Items.TryAddItem("b"), "Fusion material fixture failed.");
        var trades = f.Trades(); int gold = f.Wallet.Gold;
        Check(trades.BeginFusionChoice("a", "b", out var first) == ShopFusionResult.Success
            && first.Candidates.Count == 3 && first.Grade == 3, "Fusion selection did not freeze three same-grade results.");
        Check(f.Items.OwnedItems.Count == 2 && f.Wallet.Gold == gold, "Preparing candidates consumed resources.");
        Check(trades.BeginFusionChoice("b", "a", out var reopened) == ShopFusionResult.Success
            && ReferenceEquals(first, reopened), "Cancel/reopen or reversed slots rerolled the candidates.");
        Check(trades.CommitFusionChoice(first.Token, "not-a-candidate", out _) == ShopFusionResult.InvalidChoice
            && f.Items.Contains("a") && f.Items.Contains("b"), "Invalid result consumed materials.");
        string selected = first.Candidates[0].itemId;
        Check(trades.CommitFusionChoice(first.Token, selected, out var result) == ShopFusionResult.Success
            && result.itemId == selected && f.Items.OwnedItems.Count == 1 && f.Items.Contains(selected)
            && f.Wallet.Gold == gold, "Chosen fusion was not one atomic two-for-one exchange.");
        Check(trades.CommitFusionChoice(first.Token, selected, out _) == ShopFusionResult.InvalidChoice
            && f.Items.OwnedItems.Count == 1, "A stale fusion token repeated fulfillment.");
    }

    private static void StaleChoiceIsRejected()
    {
        using var f = new Fixture("fusion");
        f.SetCatalog(f.Item("a", 1, 20), f.Item("b", 1, 20), f.Item("c", 2, 40), f.Item("d", 2, 40));
        f.Settings.fusionUpgradeChance = 1f;
        f.Items.TryAddItem("a"); f.Items.TryAddItem("b");
        var trades = f.Trades();
        Check(trades.BeginFusionChoice("a", "b", out var choice) == ShopFusionResult.Success,
            "Stale choice fixture failed.");
        string selected = choice.Candidates[0].itemId;
        Check(f.Items.TryAddItem(selected), "External acquisition fixture failed.");
        Check(trades.CommitFusionChoice(choice.Token, selected, out _) == ShopFusionResult.InvalidChoice
            && f.Items.Contains("a") && f.Items.Contains("b"), "An already-owned result consumed materials.");
    }

    private static string Signature(ShopStock stock)
    { var values = new List<string>(); foreach (var offer in stock.Offers) if (!offer.IsHealing) values.Add(offer.Id + ":" + offer.SoldOut); return string.Join("|", values); }
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException("Engraving shop: " + message); }

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject _root;
        private readonly List<Object> _assets = new();
        public readonly EconomySettingsSO Settings;
        public readonly ItemCatalog Catalog;
        public readonly PlayerStatManager Stats;
        public readonly PlayerWallet Wallet;
        public readonly PlayerItemRuntime Items;
        public readonly EngravingSnapshot Engravings;
        public Fixture(params string[] nodes)
        {
            _root = new GameObject("Engraving commerce fixture"); _root.SetActive(false);
            Settings = Own(ScriptableObject.CreateInstance<EconomySettingsSO>());
            Catalog = Own(ScriptableObject.CreateInstance<ItemCatalog>()); Catalog.projectId = "engraving-commerce";
            var tuning = Own(ScriptableObject.CreateInstance<EngravingSettingsSO>());
            Engravings = new EngravingSnapshot(tuning, nodes);
            var data = Own(ScriptableObject.CreateInstance<PlayerStatData>()); data.baseHP = 100f;
            Stats = _root.AddComponent<PlayerStatManager>(); Stats.Initialize(data);
            Wallet = _root.AddComponent<PlayerWallet>(); Wallet.Initialize(1000);
            Items = _root.AddComponent<PlayerItemRuntime>(); Items.Catalog = Catalog;
            Items.Initialize(Stats, () => ElementType.ELEMENT_NONE); _root.SetActive(true);
        }
        public ItemDefinition Item(string id, int grade, int price)
        { var item = Own(ScriptableObject.CreateInstance<ItemDefinition>()); item.projectId = Catalog.projectId;
            item.itemId = item.displayName = id; item.candidateKey = "key-" + id; item.grade = grade; item.purchasePrice = price; return item; }
        public void SetCatalog(params ItemDefinition[] definitions) => Catalog.items = definitions;
        public ShopStock Stock() => ShopStock.Create(Catalog, Items, Settings, 51, Engravings);
        public ShopTransactions Trades() => new(Items, Wallet, Stats, Settings, 51, Engravings);
        private T Own<T>(T asset) where T : Object { _assets.Add(asset); return asset; }
        public void Dispose() { Object.DestroyImmediate(_root); foreach (var asset in _assets) Object.DestroyImmediate(asset); }
    }
}
