using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Isolated sale/fusion checks. Temporary objects only; never edits authored content or scenes.</summary>
public static class ShopTradeRegression
{
    private static int _checks;

    [MenuItem("Tools/Studio Baekho/Shop/Run Trade Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run trade regression in Edit Mode.");
        _checks = 0;
        SettingsAndSalePricing();
        SaleOwnershipEffectsAndOverflow();
        FusionValidationAndGradeRules();
        OwnedIdentitiesAndFallback();
        PreviewAndFailureDoNotConsumeRandomness();
        AtomicExchangePreservesResources();
        ReturningIngredientsStartFresh();
        CrossOperationReentry();
        ThrowingObserversPreserveTrades();
        return $"Shop trades: {_checks} checks passed (sale, overflow, fusion grades, owned identities, "
            + "random isolation, atomic resources, timers, reentry and observer failures).";
    }

    private static void SettingsAndSalePricing()
    {
        using var f = new Fixture();
        foreach (float fraction in new[] { 0f, 1f, -0.1f, float.NaN, float.PositiveInfinity })
        {
            f.Settings.saleFraction = fraction;
            Check(!f.Settings.Validate(out _), "An invalid sale fraction was accepted.");
        }
        f.Settings.saleFraction = .5f;
        foreach (float chance in new[] { -0.1f, 1.1f, float.NaN, float.PositiveInfinity })
        {
            f.Settings.fusionUpgradeChance = chance;
            Check(!f.Settings.Validate(out _), "An invalid upgrade probability was accepted.");
        }
        f.Settings.fusionUpgradeChance = 0f;
        Check(f.Settings.Validate(out _), "A zero upgrade probability must be valid.");
        f.Settings.fusionUpgradeChance = 1f;
        Check(f.Settings.Validate(out _), "A guaranteed upgrade must be valid.");
        var a = f.Item("odd-price");
        a.purchasePrice = 21;
        f.Catalog.items = new[] { a };
        f.Give(a);
        var trades = f.Trades();
        Check(trades.TryGetSalePrice(a.itemId, out int price) && price == 10,
            "Sale price must floor the imported price times the configured fraction.");
        f.Settings.saleFraction = .75f;
        Check(trades.TryGetSalePrice(a.itemId, out price) && price == 15,
            "Changing the sale setting did not update the quoted price.");
        a.purchasePrice = 0;
        Check(!trades.TryGetSalePrice(a.itemId, out _) && trades.TrySell(a.itemId) == ShopSaleResult.InvalidItem,
            "A zero purchase price must not allow selling the item.");
        a.purchasePrice = -1;
        Check(trades.TrySell(a.itemId) == ShopSaleResult.InvalidItem && f.Items.Contains(a.itemId),
            "An invalid price consumed an item.");
    }

    private static void SaleOwnershipEffectsAndOverflow()
    {
        using (var f = new Fixture())
        {
            var item = f.Item("sale", 1, Passive(ItemStat.NormalDamage, .4f));
            f.Catalog.items = new[] { item };
            f.Give(item);
            var trades = f.Trades();
            Near(f.Items.GetNormalDamageMultiplier(ElementType.ELEMENT_WATER), 1.4f, "Sale effect setup");
            int ownedEvents = 0, goldEvents = 0;
            f.Items.OnOwnedItemsChanged += () => ownedEvents++;
            f.Wallet.GoldChanged += _ => goldEvents++;
            Check(trades.TrySell(item.itemId) == ShopSaleResult.Success, "A valid sale failed.");
            Check(!f.Items.Contains(item.itemId) && f.Wallet.Gold == 10 && ownedEvents == 1 && goldEvents == 1,
                "A sale must remove one item, credit once and publish once.");
            Near(f.Items.GetNormalDamageMultiplier(ElementType.ELEMENT_WATER), 1f, "Sold item effects remain");
            Check(trades.TrySell(item.itemId) == ShopSaleResult.NotOwned && f.Wallet.Gold == 10
                && ownedEvents == 1 && goldEvents == 1, "Repeated sale changed ownership or gold.");
        }
        using (var f = new Fixture(int.MaxValue - 9))
        {
            var item = f.Item("overflow");
            f.Catalog.items = new[] { item };
            f.Give(item);
            int ownedEvents = 0, goldEvents = 0;
            f.Items.OnOwnedItemsChanged += () => ownedEvents++;
            f.Wallet.GoldChanged += _ => goldEvents++;
            var trades = f.Trades();
            Check(trades.TrySell(item.itemId) == ShopSaleResult.GoldOverflow, "An overflowing sale was accepted.");
            Check(f.Items.Contains(item.itemId) && f.Wallet.Gold == int.MaxValue - 9
                && ownedEvents == 0 && goldEvents == 0, "Overflow consumed the item or published partial state.");
            Check(f.Wallet.TrySpend(1) && trades.TrySell(item.itemId) == ShopSaleResult.Success
                && f.Wallet.Gold == int.MaxValue, "A sale reaching exactly the maximum balance failed.");
        }
    }

    private static void FusionValidationAndGradeRules()
    {
        using (var f = new Fixture(17))
        {
            var a = f.Item("a");
            var b = f.Item("b");
            var upper = f.Item("upper", 2);
            var otherGrade = f.Item("other-grade", 3);
            f.Catalog.items = new[] { a, b, upper, otherGrade };
            f.Give(a, b, otherGrade);
            var trades = f.Trades();
            int changes = 0;
            f.Items.OnOwnedItemsChanged += () => changes++;
            Check(trades.TryFuse(a.itemId, a.itemId, out _) != ShopFusionResult.Success,
                "One item was accepted in both material slots.");
            Check(trades.TryFuse(a.itemId, otherGrade.itemId, out _) != ShopFusionResult.Success,
                "Different material grades were accepted.");
            Check(trades.TryFuse(a.itemId, upper.itemId, out _) != ShopFusionResult.Success,
                "An unowned material was accepted.");
            Check(trades.TryFuse(a.itemId, "missing", out _) != ShopFusionResult.Success
                && changes == 0 && f.Items.OwnedItems.Count == 3 && f.Wallet.Gold == 17,
                "Rejected fusion changed inventory, notifications or gold.");
            f.Settings.fusionUpgradeChance = 1f;
            Check(trades.GetFusionPreview(a.itemId, b.itemId, out int grade, out float chance) == ShopFusionResult.Success
                && grade == 1 && chance == 1f, "Guaranteed upgrade preview is incorrect.");
            Check(trades.TryFuse(a.itemId, b.itemId, out var result) == ShopFusionResult.Success && result == upper,
                "A guaranteed upgrade did not grant the only eligible next-grade item.");
            Check(!f.Items.Contains(a.itemId) && !f.Items.Contains(b.itemId) && f.Items.Contains(upper.itemId)
                && f.Items.Contains(otherGrade.itemId) && f.Wallet.Gold == 17 && changes == 1,
                "Fusion must consume two materials, retain unrelated items and leave gold unchanged.");
        }
        using (var f = new Fixture())
        {
            var a = f.Item("same-a");
            var b = f.Item("same-b");
            var upper = f.Item("unused-upper", 2);
            f.Catalog.items = new[] { a, b, upper };
            f.Give(a, b);
            f.Settings.fusionUpgradeChance = 0f;
            var trades = f.Trades();
            Check(trades.GetFusionPreview(a.itemId, b.itemId, out _, out float chance) == ShopFusionResult.Success
                && chance == 0f, "A zero configured upgrade chance must remain zero with an upper candidate.");
            Check(trades.TryFuse(a.itemId, b.itemId, out var result) == ShopFusionResult.Success && result.grade == 1,
                "A zero upgrade probability unexpectedly upgraded the item.");
        }
        using (var f = new Fixture())
        {
            var a = f.Item("max-a", 4);
            var b = f.Item("max-b", 4);
            f.Catalog.items = new[] { a, b };
            f.Give(a, b);
            f.Settings.fusionUpgradeChance = 1f;
            var trades = f.Trades();
            Check(trades.GetFusionPreview(a.itemId, b.itemId, out int grade, out float chance) == ShopFusionResult.Success
                && grade == 4 && chance == 0f, "Highest-grade materials must preview same grade only.");
            Check(trades.TryFuse(a.itemId, b.itemId, out var result) == ShopFusionResult.Success && result.grade == 4
                && f.Items.OwnedItems.Count == 1, "Highest-grade fusion failed to return a consumed ingredient.");
        }
    }

    private static void OwnedIdentitiesAndFallback()
    {
        using var f = new Fixture();
        var a = f.Item("a");
        var b = f.Item("b");
        var held = f.Item("held", 2);
        var alias = f.Item("held-alias", 2);
        alias.candidateKey = held.candidateKey;
        f.Catalog.items = new[] { a, b, held, alias };
        f.Give(a, b, held);
        f.Settings.fusionUpgradeChance = 1f;
        var trades = f.Trades();
        Check(trades.GetFusionPreview(a.itemId, b.itemId, out _, out float chance) == ShopFusionResult.Success && chance == 0f,
            "Owned next-grade IDs or aliases must not remain in the upgrade pool.");
        Check(trades.TryFuse(a.itemId, b.itemId, out var result) == ShopFusionResult.Success
            && (result == a || result == b) && f.Items.Contains(held.itemId) && !f.Items.Contains(alias.itemId),
            "An empty upgrade pool must fall back to an ingredient without duplicating held content.");
        var remaining = result == a ? b : a;
        f.Give(remaining);
        int notifications = 0;
        f.Items.OnOwnedItemsChanged += () => notifications++;
        Check(!f.Items.TryExchangeItems(a.itemId, b.itemId, alias.itemId) && notifications == 0
            && f.Items.Contains(a.itemId) && f.Items.Contains(b.itemId) && f.Items.Contains(held.itemId),
            "Atomic exchange accepted a result with the identity of an unrelated owned item.");
        a.effects = new ItemEffect[] { null };
        Check(trades.TryFuse(a.itemId, b.itemId, out _) != ShopFusionResult.Success
            && f.Items.Contains(a.itemId) && f.Items.Contains(b.itemId) && notifications == 0,
            "Malformed current material content was consumed.");
    }

    private static void PreviewAndFailureDoNotConsumeRandomness()
    {
        var unityRandom = UnityEngine.Random.state;
        for (int seed = 0; seed < 8; seed++)
        {
            using var control = new Fixture();
            using var previewed = new Fixture();
            var controlMaterials = RandomCatalog(control);
            var previewedMaterials = RandomCatalog(previewed);
            var controlTrades = control.Trades(seed);
            var previewedTrades = previewed.Trades(seed);
            for (int round = 0; round < 3; round++)
            {
                if (round > 0)
                {
                    RestoreMaterials(control, controlMaterials);
                    RestoreMaterials(previewed, previewedMaterials);
                }
                for (int n = 0; n < 5; n++)
                    Check(previewedTrades.GetFusionPreview("a", "b", out _, out _) == ShopFusionResult.Success,
                        "A valid preview failed.");
                Check(previewedTrades.TryFuse("a", "a", out _) != ShopFusionResult.Success,
                    "Invalid randomness probe unexpectedly fused an item.");
                Check(controlTrades.TryFuse("a", "b", out var expected) == ShopFusionResult.Success
                    && previewedTrades.TryFuse("a", "b", out var actual) == ShopFusionResult.Success
                    && actual.itemId == expected.itemId,
                    "Previews or failed fusion changed a successful seeded result sequence.");
            }
        }
        Check(UnityEngine.Random.state.Equals(unityRandom), "Fusion modified Unity's shared random state.");
    }

    private static ItemDefinition[] RandomCatalog(Fixture f)
    {
        var a = f.Item("a");
        var b = f.Item("b");
        f.Catalog.items = new[] { a, b, f.Item("c"), f.Item("upper-a", 2), f.Item("upper-b", 2) };
        f.Give(a, b);
        return new[] { a, b };
    }

    private static void RestoreMaterials(Fixture f, ItemDefinition[] materials)
    {
        var owned = new List<ItemDefinition>(f.Items.OwnedItems);
        foreach (var item in owned) Check(f.Items.TryRemoveItem(item.itemId), "Random fixture cleanup failed.");
        f.Give(materials);
    }

    private static void AtomicExchangePreservesResources()
    {
        using var f = new Fixture();
        var a = f.Item("a", 1, Passive(ItemStat.MaxHp, .5f));
        var b = f.Item("b", 1, Passive(ItemStat.MaxHp, .5f));
        var result = f.Item("result", 2, Passive(ItemStat.MaxHp, 1f));
        f.Catalog.items = new[] { a, b, result };
        f.Give(a, b);
        f.Stats.Heal(100f);
        Near(f.Stats.CurrentHp, 200f, "Atomic HP setup");
        int ownership = 0, stats = 0;
        f.Items.OnOwnedItemsChanged += () => ownership++;
        f.Stats.OnStatsChanged += () => stats++;
        f.Settings.fusionUpgradeChance = 1f;
        Check(f.Trades().TryFuse(a.itemId, b.itemId, out var granted) == ShopFusionResult.Success && granted == result,
            "Resource-preserving fusion failed.");
        Near(f.Stats.CurrentHp, 200f, "Fusion clamped HP through an intermediate lower maximum");
        Check(ownership == 1 && stats == 1, "Fusion must publish one inventory and stat replacement.");
    }

    private static void ReturningIngredientsStartFresh()
    {
        for (int returnedMaterial = 0; returnedMaterial < 2; returnedMaterial++)
        {
            using var f = new Fixture();
            var a = f.Item("a", 1, Timed(ItemStat.NormalDamage, .6f, 5f));
            var b = f.Item("b", 1, Timed(ItemStat.NormalDamage, .6f, 5f));
            var unrelated = f.Item("unrelated", 2, Timed(ItemStat.NormalDamage, .3f, 5f));
            f.Catalog.items = new[] { a, b, unrelated };
            f.Give(a, b, unrelated);
            f.Items.NotifyFormSwitchSucceeded();
            f.Items.Tick(1f);
            Near(f.Items.GetNormalDamageMultiplier(ElementType.ELEMENT_WATER), 2.5f, "Active timer setup");
            int notifications = 0;
            f.Items.OnOwnedItemsChanged += () => notifications++;
            string resultId = returnedMaterial == 0 ? a.itemId : b.itemId;
            Check(f.Items.TryExchangeItems(a.itemId, b.itemId, resultId) && f.Items.Contains(resultId)
                && f.Items.OwnedItems.Count == 2 && notifications == 1,
                "A consumed ingredient could not be granted as a fresh result.");
            Near(f.Items.GetNormalDamageMultiplier(ElementType.ELEMENT_WATER), 1.3f,
                "Returned ingredient restored a consumed timer or removed an unrelated timer");
            f.Items.Tick(3.5f);
            Near(f.Items.GetNormalDamageMultiplier(ElementType.ELEMENT_WATER), 1.3f, "Unrelated timer was shortened");
            f.Items.Tick(.6f);
            Near(f.Items.GetNormalDamageMultiplier(ElementType.ELEMENT_WATER), 1f, "Unrelated timer was reset");
        }
    }

    private static void CrossOperationReentry()
    {
        using var f = new Fixture(100);
        var sale = f.Item("sale");
        var a = f.Item("a");
        var b = f.Item("b");
        var product = f.Item("product", 2);
        f.Catalog.items = new[] { sale, a, b, product };
        f.Give(sale, a, b);
        var stock = ShopStock.Create(f.Catalog, f.Items, f.Settings, 1);
        var trades = f.Trades();
        ShopOffer offer = null;
        foreach (var candidate in stock.Offers) if (candidate.Item == product) offer = candidate;
        Check(offer != null, "Reentry stock setup failed.");
        int callbacks = 0;
        bool rejectedAllNested = true;
        Action nested = () =>
        {
            callbacks++;
            // Capture failures outside the callback: observers are intentionally exception-isolated.
            rejectedAllNested &= trades.TrySell(a.itemId) == ShopSaleResult.Unavailable;
            rejectedAllNested &= trades.TryFuse(a.itemId, b.itemId, out _) == ShopFusionResult.Unavailable;
            rejectedAllNested &= trades.TryBuy(stock, offer.Id) == ShopPurchaseResult.Unavailable;
            rejectedAllNested &= !f.Items.TryRemoveItem(a.itemId);
            rejectedAllNested &= !f.Items.TryAddItem(sale.itemId);
            rejectedAllNested &= !f.Wallet.AddGold(1);
        };
        f.Items.OnOwnedItemsChanged += nested;
        Check(trades.TrySell(sale.itemId) == ShopSaleResult.Success && f.Wallet.Gold == 110 && callbacks == 1 && rejectedAllNested,
            "Nested callbacks interrupted the outer sale.");
        Check(trades.TryBuy(stock, offer.Id) == ShopPurchaseResult.Success && offer.SoldOut
            && f.Wallet.Gold == 90 && callbacks == 2 && rejectedAllNested, "Purchase did not use the common trade guard.");
        f.Items.OnOwnedItemsChanged -= nested;
        Check(trades.TrySell(product.itemId) == ShopSaleResult.Success && offer.SoldOut,
            "Selling a purchased product reset its sold-out stock entry.");
    }

    private static void ThrowingObserversPreserveTrades()
    {
        using var f = new Fixture();
        var a = f.Item("a", 1, Passive(ItemStat.MaxHp, .5f));
        var b = f.Item("b", 1, Passive(ItemStat.MaxHp, .5f));
        var result = f.Item("result", 2, Passive(ItemStat.MaxHp, 1f));
        f.Catalog.items = new[] { a, b, result };
        f.Give(a, b);
        f.Settings.fusionUpgradeChance = 1f;
        var trades = f.Trades();
        int ownership = 0, gold = 0;
        Action badStats = () => throw new InvalidOperationException("Expected trade stat observer failure.");
        Action badOwned = () => throw new InvalidOperationException("Expected trade ownership observer failure.");
        Action lateOwned = () => ownership++;
        Action<int> badGold = _ => throw new InvalidOperationException("Expected trade gold observer failure.");
        Action<int> lateGold = _ => gold++;
        f.Stats.OnStatsChanged += badStats;
        f.Items.OnOwnedItemsChanged += badOwned;
        f.Items.OnOwnedItemsChanged += lateOwned;
        f.Wallet.GoldChanged += badGold;
        f.Wallet.GoldChanged += lateGold;
        try
        {
            Check(trades.TryFuse(a.itemId, b.itemId, out var granted) == ShopFusionResult.Success && granted == result
                && f.Items.Contains(result.itemId) && f.Items.OwnedItems.Count == 1 && ownership == 1,
                "Throwing observers changed a committed fusion or skipped later ownership observers.");
            Check(trades.TrySell(result.itemId) == ShopSaleResult.Success && !f.Items.Contains(result.itemId)
                && f.Wallet.Gold == 10 && ownership == 2 && gold == 1,
                "Throwing observers consumed a sold item without credit or duplicated notifications.");
        }
        finally
        {
            f.Stats.OnStatsChanged -= badStats;
            f.Items.OnOwnedItemsChanged -= badOwned;
            f.Items.OnOwnedItemsChanged -= lateOwned;
            f.Wallet.GoldChanged -= badGold;
            f.Wallet.GoldChanged -= lateGold;
        }
    }

    private static ItemEffect Passive(ItemStat stat, float amount)
        => new() { stat = stat, amount = amount, allForms = true, trigger = ItemEffectTrigger.WhileOwned };

    private static ItemEffect Timed(ItemStat stat, float amount, float duration)
        => new() { stat = stat, amount = amount, allForms = true, trigger = ItemEffectTrigger.FormSwitchSucceeded,
            durationSeconds = duration };

    private static void Near(float actual, float expected, string message)
        => Check(!float.IsNaN(actual) && Mathf.Abs(actual - expected) < .001f, $"{message}: {actual} != {expected}");

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("Shop trade regression: " + message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject _root;
        private readonly List<Object> _owned = new();
        public readonly EconomySettingsSO Settings;
        public readonly ItemCatalog Catalog;
        public readonly PlayerWallet Wallet;
        public readonly PlayerStatManager Stats;
        public readonly PlayerItemRuntime Items;

        public Fixture(int startingGold = 0)
        {
            _root = new GameObject("Shop trade regression") { hideFlags = HideFlags.HideAndDontSave };
            _root.SetActive(false);
            Settings = Own(ScriptableObject.CreateInstance<EconomySettingsSO>());
            Catalog = Own(ScriptableObject.CreateInstance<ItemCatalog>());
            Catalog.projectId = "shop-trade-regression";
            var data = Own(ScriptableObject.CreateInstance<PlayerStatData>());
            data.baseHP = 100f;
            Stats = _root.AddComponent<PlayerStatManager>();
            Stats.Initialize(data);
            Wallet = _root.AddComponent<PlayerWallet>();
            Wallet.Initialize(startingGold);
            Items = _root.AddComponent<PlayerItemRuntime>();
            Items.Catalog = Catalog;
            Items.Initialize(Stats, () => ElementType.ELEMENT_WATER);
            _root.SetActive(true);
        }

        public ShopTransactions Trades(int seed = 1) => new(Items, Wallet, Stats, Settings, seed);

        public ItemDefinition Item(string id, int grade = 1, params ItemEffect[] effects)
        {
            var item = Own(ScriptableObject.CreateInstance<ItemDefinition>());
            item.projectId = Catalog.projectId;
            item.candidateKey = "candidate-" + id;
            item.itemId = item.displayName = id;
            item.grade = grade;
            item.purchasePrice = 20;
            item.effects = effects;
            return item;
        }

        public void Give(params ItemDefinition[] items)
        {
            foreach (var item in items) Check(Items.TryAddItem(item.itemId), "Fixture grant failed: " + item.itemId);
        }

        private T Own<T>(T value) where T : Object
        {
            value.hideFlags = HideFlags.HideAndDontSave;
            _owned.Add(value);
            return value;
        }

        public void Dispose()
        {
            Object.DestroyImmediate(_root);
            foreach (var value in _owned) if (value != null) Object.DestroyImmediate(value);
        }
    }
}
