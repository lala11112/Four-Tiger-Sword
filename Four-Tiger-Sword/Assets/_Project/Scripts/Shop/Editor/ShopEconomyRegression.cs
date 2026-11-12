using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Isolated checks using temporary objects only; never saves scenes or edits content assets.</summary>
public static class ShopEconomyRegression
{
    private static int _checks;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Studio Baekho/Shop/Run Economy Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run economy regression in Edit Mode.");
        _checks = 0;
        SettingsValidation();
        WalletBoundariesAndInitialization();
        DeterministicUniqueStock();
        PurchaseAndReentry();
        ThrowingObserversPreserveCommittedPurchases();
        RejectedPurchasesPreserveBalance();
        HealingRules();
        ConfirmedDeathRewardsOnly();
        EarnedGoldBonuses();
        return $"Shop economy: {_checks} checks passed (settings, wallet boundaries, seeded stock, ownership, "
            + "transactions, healing, reentry, observer failures and confirmed death rewards).";
    }

    private static void SettingsValidation()
    {
        using var f = new Fixture();
        Check(f.Settings.Validate(out _), "Default economy settings must validate.");
        f.Settings.startingGold = -1;
        Check(!f.Settings.Validate(out _), "Negative starting gold was accepted.");
        f.Settings.startingGold = 0;
        f.Settings.baseItemCount = -1;
        Check(!f.Settings.Validate(out _), "Negative stock count was accepted.");
        f.Settings.baseItemCount = 0;
        Check(!f.Settings.Validate(out _), "Zero base stock count was accepted.");
        f.Settings.baseItemCount = int.MaxValue;
        Check(f.Settings.Validate(out _), "A large stock count should clamp against available candidates safely.");
        f.Settings.healingFraction = float.NaN;
        Check(!f.Settings.Validate(out _), "Nonfinite healing was accepted.");
        f.Settings.healingFraction = 1.1f;
        Check(!f.Settings.Validate(out _), "Healing above full health was accepted.");
        f.Settings.healingFraction = 0.3f;
        f.Settings.interactionDistance = float.PositiveInfinity;
        Check(!f.Settings.Validate(out _), "Nonfinite interaction distance was accepted.");
    }

    private static void WalletBoundariesAndInitialization()
    {
        using var f = new Fixture(40);
        int notifications = 0;
        f.Wallet.GoldChanged += _ => notifications++;
        f.Wallet.Initialize(999);
        Check(f.Wallet.Gold == 40 && notifications == 0, "A stage retry reset initialized gold.");
        Check(!f.Wallet.AddGold(-1) && !f.Wallet.TrySpend(-1), "Negative mutations must fail.");
        Check(f.Wallet.AddGold(0) && f.Wallet.TrySpend(0) && notifications == 0, "Zero mutations must be silent.");
        Check(f.Wallet.AddGold(int.MaxValue - 40) && f.Wallet.Gold == int.MaxValue, "Exact maximum balance failed.");
        Check(!f.Wallet.AddGold(1) && f.Wallet.Gold == int.MaxValue, "Overflow changed gold.");
        Check(f.Wallet.TrySpend(int.MaxValue) && f.Wallet.Gold == 0, "Exact balance spending failed.");
        Check(!f.Wallet.TrySpend(1) && notifications == 2, "Failed spending changed gold or published an event.");
        var uninitialized = f.Child("Uninitialized wallet").AddComponent<PlayerWallet>();
        Check(!uninitialized.AddGold(1) && !uninitialized.TrySpend(0), "An uninitialized wallet accepted a mutation.");
    }

    private static void DeterministicUniqueStock()
    {
        using var f = new Fixture();
        var definitions = new List<ItemDefinition>();
        for (int i = 0; i < 12; i++) definitions.Add(f.Item("item-" + i.ToString("00")));
        f.Catalog.items = definitions.ToArray();
        Check(f.Items.TryAddItem(definitions[0].itemId), "Owned exclusion setup failed.");
        var ownedAlias = f.Item("owned-alias");
        ownedAlias.candidateKey = definitions[0].candidateKey;
        definitions.Add(ownedAlias);
        var badPrice = f.Item("bad-price");
        badPrice.purchasePrice = -1;
        definitions.Add(badPrice);
        var badEffect = f.Item("bad-effect");
        badEffect.effects = new ItemEffect[] { null };
        definitions.Add(badEffect);
        var duplicateA = f.Item("duplicate");
        var duplicateB = f.Item("duplicate");
        definitions.Add(duplicateA);
        definitions.Add(duplicateB);
        f.Catalog.items = definitions.ToArray();
        var originalOrder = (ItemDefinition[])f.Catalog.items.Clone();
        var unityRandom = UnityEngine.Random.state;
        var seenCounts = new HashSet<int>();
        for (int seed = 0; seed < 64; seed++)
        {
            var a = ShopStock.Create(f.Catalog, f.Items, f.Settings, seed);
            var b = ShopStock.Create(f.Catalog, f.Items, f.Settings, seed);
            Check(a.Offers.Count == b.Offers.Count, "A seed changed its requested stock count.");
            int itemCount = 0, healCount = 0;
            var ids = new HashSet<string>();
            var keys = new HashSet<string>();
            for (int i = 0; i < a.Offers.Count; i++)
            {
                var offer = a.Offers[i];
                Check(offer.Id == b.Offers[i].Id, "A seed changed item selection/order.");
                if (offer.IsHealing) { healCount++; continue; }
                itemCount++;
                Check(ids.Add(offer.Item.itemId) && keys.Add(offer.Item.candidateKey), "Stock contains duplicate identities.");
                Check(offer.Item != definitions[0] && offer.Item != ownedAlias && offer.Item != badPrice
                    && offer.Item != badEffect && offer.Item != duplicateA && offer.Item != duplicateB,
                    "Owned or malformed content appeared in stock.");
                Check(offer.Price == offer.Item.purchasePrice, "Stock did not use imported item pricing.");
            }
            Check(itemCount >= 2 && itemCount <= 4 && healCount == 1, "Stock count or healing offer is incorrect.");
            seenCounts.Add(itemCount);
        }
        Check(seenCounts.Count == 3, "Seeded stock did not exercise N-1, N and N+1.");
        Check(UnityEngine.Random.state.Equals(unityRandom), "Stock modified Unity's shared random state.");
        for (int i = 0; i < originalOrder.Length; i++)
            Check(f.Catalog.items[i] == originalOrder[i], "Stock shuffled the imported catalog in place.");
        f.Settings.baseItemCount = int.MaxValue;
        Check(ShopStock.Create(f.Catalog, f.Items, f.Settings, 3).Offers.Count == 12,
            "An enormous requested stock count overflowed or exceeded available content.");
        f.Catalog.items = Array.Empty<ItemDefinition>();
        Check(ShopStock.Create(f.Catalog, f.Items, f.Settings, 3).Offers.Count == 1, "Empty catalogs must retain healing.");
    }

    private static void PurchaseAndReentry()
    {
        using var f = new Fixture(40);
        var a = f.Item("a");
        var b = f.Item("b");
        f.Catalog.items = new[] { a, b };
        f.Settings.baseItemCount = 20;
        var stock = ShopStock.Create(f.Catalog, f.Items, f.Settings, 10);
        var first = ItemOffer(stock, a);
        var second = ItemOffer(stock, b);
        int balanceEvents = 0, stockEvents = 0;
        ShopPurchaseResult nested = ShopPurchaseResult.Success;
        f.Wallet.GoldChanged += _ => balanceEvents++;
        stock.Changed += () => stockEvents++;
        f.Items.OnOwnedItemsChanged += () => nested = stock.TryPurchase(second.Id, f.Wallet, f.Items, f.Stats);
        Check(stock.TryPurchase(first.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.Success, "A valid purchase failed.");
        Check(f.Items.Contains(a.itemId) && first.SoldOut && f.Wallet.Gold == 20, "Purchase was not fulfilled exactly once.");
        Check(nested == ShopPurchaseResult.Unavailable && !second.SoldOut && !f.Items.Contains(b.itemId),
            "Item-event reentry purchased another offer during a transaction.");
        Check(balanceEvents == 1 && stockEvents == 1, "A successful purchase must publish one balance and stock event.");
        Check(stock.TryPurchase(first.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.SoldOut && f.Wallet.Gold == 20,
            "Repeated purchase charged for sold stock.");
        Check(stock.TryPurchase(second.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.Success && f.Wallet.Gold == 0,
            "Exact-balance purchase failed.");
    }

    private static void ThrowingObserversPreserveCommittedPurchases()
    {
        using (var f = new Fixture(40))
        {
            var item = f.Item("throwing-item-observer");
            f.Catalog.items = new[] { item };
            f.Settings.baseItemCount = 5;
            var stock = ShopStock.Create(f.Catalog, f.Items, f.Settings, 1);
            var offer = ItemOffer(stock, item);
            int ownershipEvents = 0, lateBalanceEvents = 0, lateStockEvents = 0;
            f.Items.OnOwnedItemsChanged += () =>
            {
                ownershipEvents++;
                throw new InvalidOperationException("Expected ownership observer failure.");
            };
            f.Wallet.GoldChanged += _ => throw new InvalidOperationException("Expected gold observer failure.");
            f.Wallet.GoldChanged += _ => lateBalanceEvents++;
            stock.Changed += () => throw new InvalidOperationException("Expected stock observer failure.");
            stock.Changed += () => lateStockEvents++;

            Check(stock.TryPurchase(offer.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.Success,
                "Throwing ownership, gold or stock observers changed a committed purchase into failure.");
            Check(f.Items.Contains(item.itemId) && offer.SoldOut && f.Wallet.Gold == 20,
                "An ownership observer exception granted an item without consuming gold and stock.");
            Check(ownershipEvents == 1 && lateBalanceEvents == 1 && lateStockEvents == 1,
                "An observer exception skipped later wallet/stock listeners or repeated fulfillment.");
            Check(stock.TryPurchase(offer.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.SoldOut
                && f.Wallet.Gold == 20 && ownershipEvents == 1 && lateBalanceEvents == 1 && lateStockEvents == 1,
                "Retrying after an observer exception charged or granted twice.");

            var wallet = f.Child("Wallet notification failures").AddComponent<PlayerWallet>();
            int lateEvents = 0;
            wallet.GoldChanged += _ => throw new InvalidOperationException("Expected standalone wallet observer failure.");
            wallet.GoldChanged += _ => lateEvents++;
            wallet.Initialize(10);
            Check(wallet.AddGold(5) && wallet.TrySpend(3) && wallet.Gold == 12 && lateEvents == 3,
                "Wallet initialization, earning or spending failed to isolate a throwing observer.");
        }

        using (var f = new Fixture(100))
        {
            f.Stats.TakeDamage(60f, ElementType.ELEMENT_NONE, false);
            var stock = ShopStock.Create(f.Catalog, f.Items, f.Settings, 1);
            var heal = stock.Offers[stock.Offers.Count - 1];
            int healthEvents = 0;
            f.Stats.OnHpChanged += (_, _) =>
            {
                healthEvents++;
                throw new InvalidOperationException("Expected healing observer failure.");
            };
            Check(stock.TryPurchase(heal.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.Success,
                "A health observer exception changed completed healing into failure.");
            Near(f.Stats.CurrentHp, 70f, "A throwing health observer changed healing quantity.");
            Check(heal.SoldOut && f.Wallet.Gold == 80 && healthEvents == 1,
                "A health observer exception granted healing without consuming gold and stock.");
            Check(stock.TryPurchase(heal.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.SoldOut
                && f.Wallet.Gold == 80 && healthEvents == 1,
                "Retrying after a health observer exception repeated healing or payment.");
        }
    }

    private static void RejectedPurchasesPreserveBalance()
    {
        using var f = new Fixture(19);
        var item = f.Item("item");
        f.Catalog.items = new[] { item };
        f.Settings.baseItemCount = 5;
        var stock = ShopStock.Create(f.Catalog, f.Items, f.Settings, 1);
        var offer = ItemOffer(stock, item);
        Check(stock.TryPurchase("missing", f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.InvalidOffer, "Unknown offer accepted.");
        Check(stock.TryPurchase(offer.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.InsufficientGold,
            "Insufficient balance was not rejected.");
        Check(f.Wallet.Gold == 19 && !offer.SoldOut && !f.Items.Contains(item.itemId), "Failed purchase changed state.");
        f.Wallet.AddGold(1);
        item.effects = new ItemEffect[] { null }; // A grant can become invalid after stock generation.
        Check(stock.TryPurchase(offer.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.Unavailable
            && f.Wallet.Gold == 20 && !offer.SoldOut, "Failed item grant consumed gold or stock.");
        item.effects = Array.Empty<ItemEffect>();
        Check(f.Items.TryAddItem(item.itemId), "External ownership setup failed.");
        Check(stock.TryPurchase(offer.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.AlreadyOwned
            && f.Wallet.Gold == 20, "An externally acquired item was charged again.");
        f.Items.TryRemoveItem(item.itemId);
        f.Catalog.items = Array.Empty<ItemDefinition>();
        Check(stock.TryPurchase(offer.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.Unavailable
            && f.Wallet.Gold == 20, "Removed catalog content was sold from a stale offer.");
        f.Catalog.items = new[] { item };
        f.Stats.TakeDamage(1000f, ElementType.ELEMENT_NONE, false);
        Check(stock.TryPurchase(offer.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.Unavailable
            && f.Wallet.Gold == 20, "A dead player purchased an item.");
    }

    private static void HealingRules()
    {
        using var f = new Fixture(100);
        var stock = ShopStock.Create(f.Catalog, f.Items, f.Settings, 1);
        var heal = stock.Offers[stock.Offers.Count - 1];
        Check(stock.TryPurchase(heal.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.FullHealth
            && f.Wallet.Gold == 100 && !heal.SoldOut, "Full-health healing charged gold or consumed stock.");
        f.Stats.TakeDamage(60f, ElementType.ELEMENT_NONE, false);
        Check(stock.TryPurchase(heal.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.Success, "Healing purchase failed.");
        Near(f.Stats.CurrentHp, 70f, "Healing must restore a fraction of maximum HP.");
        Check(heal.SoldOut && f.Wallet.Gold == 80, "Healing did not charge once or consume its offer.");
        Check(stock.TryPurchase(heal.Id, f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.SoldOut && f.Wallet.Gold == 80,
            "Healing was repeatable within one stock.");
        f.Stats.Heal(20f);
        var other = ShopStock.Create(f.Catalog, f.Items, f.Settings, 2);
        Check(other.TryPurchase("healing", f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.Success, "Clamped healing failed.");
        Near(f.Stats.CurrentHp, 100f, "Healing exceeded max HP.");
        f.Settings.baseItemCount = 1;
        f.Settings.healingPrice = 0;
        f.Stats.TakeDamage(10f, ElementType.ELEMENT_NONE, false);
        var free = ShopStock.Create(f.Catalog, f.Items, f.Settings, 3);
        Check(free.TryPurchase("healing", f.Wallet, f.Items, f.Stats) == ShopPurchaseResult.Success
            && f.Wallet.Gold == 60, "A configured free heal failed or changed gold.");
    }

    private static void ConfirmedDeathRewardsOnly()
    {
        using var f = new Fixture();
        var ordinary = f.Enemy("Ordinary", 10, out var ordinaryReward);
        ordinary.TakeDamage(100f, poiseDamage: 0f);
        Check(f.Wallet.Gold == 10, "Confirmed ordinary enemy death did not pay.");
        RaiseDeathAgain(ordinary);
        ordinaryReward.Initialize(f.Wallet, 10);
        RaiseDeathAgain(ordinary);
        Check(f.Wallet.Gold == 10, "Duplicate death or reward reinitialization paid twice.");
        var boss = f.Enemy("Boss", 50, out _);
        boss.TakeDamage(100f, poiseDamage: 0f);
        Check(f.Wallet.Gold == 60, "Configured boss reward did not pay.");
        var disabled = f.Enemy("Disabled", 10, out var disabledReward);
        disabled.gameObject.SetActive(false);
        InvokeLifecycle(disabledReward, "OnDisable");
        RaiseDeathAgain(disabled);
        Check(f.Wallet.Gold == 60, "Disabling an enemy paid a reward.");
        disabled.gameObject.SetActive(true);
        InvokeLifecycle(disabledReward, "OnEnable");
        disabled.TakeDamage(100f, poiseDamage: 0f);
        Check(f.Wallet.Gold == 70, "A reenabled living enemy lost its one death reward.");
        var destroyed = f.Enemy("Destroyed", 10, out _);
        Object.DestroyImmediate(destroyed.gameObject);
        Check(f.Wallet.Gold == 70, "Destroying a living enemy paid a reward.");
    }

    private static void EarnedGoldBonuses()
    {
        using var f = new Fixture(40);
        var rate = f.Item("gold-gain");
        rate.boundaryEffects = new[]
        {
            new BoundaryItemEffect { stat = BoundaryItemStat.GoldGainRatio, amount = .25f }
        };
        var tradeItem = f.Item("trade-item");
        f.Catalog.items = new[] { rate, tradeItem };
        Check(f.Items.TryAddItem(rate.itemId) && f.Items.TryAddItem(tradeItem.itemId), "Gold modifier setup failed.");
        int gained = 0;
        f.Wallet.GoldGained += amount => gained += amount;
        f.Wallet.Initialize(100);
        Check(f.Wallet.Gold == 40 && gained == 0, "Starting gold was affected by the gold gain modifier.");
        Check(f.Wallet.AddGold(12) && f.Wallet.Gold == 52 && gained == 12, "Raw wallet credit was amplified.");
        var trades = new ShopTransactions(f.Items, f.Wallet, f.Stats, f.Settings, 17);
        Check(trades.TrySell(tradeItem.itemId) == ShopSaleResult.Success && f.Wallet.Gold == 62 && gained == 22,
            "Shop sale proceeds were amplified.");

        for (int i = 0; i < 3; i++) Check(f.Wallet.TryAddEarnedGold(1), "A small reward was rejected.");
        Check(f.Wallet.Gold == 65 && gained == 25, "Small credits rounded up before earning a whole gold.");
        Check(f.Wallet.AddGold(int.MaxValue - f.Wallet.Gold - 1), "Overflow setup failed.");
        Check(!f.Wallet.TryAddEarnedGold(1) && f.Wallet.Gold == int.MaxValue - 1, "Overflow should reject the entire earned credit.");
        Check(f.Wallet.TrySpend(f.Wallet.Gold), "Wallet could not create room for retry.");
        gained = 0;
        Check(f.Wallet.TryAddEarnedGold(1) && f.Wallet.Gold == 2 && gained == 2,
            "Failed credit consumed the accumulated fractional bonus.");
        Check(f.Wallet.TryAddEarnedGold(1) && f.Wallet.Gold == 3 && gained == 3,
            "A retry paid the same accumulated fraction twice.");

        var ordinary = f.Enemy("Ordinary gold gain", 12, out _);
        ordinary.TakeDamage(100f, poiseDamage: 0f);
        Check(f.Wallet.Gold == 18 && gained == 18, "An ordinary enemy's base gold was not amplified.");
        var boss = f.Enemy("Boss gold gain", 20, out _);
        boss.TakeDamage(100f, poiseDamage: 0f);
        Check(f.Wallet.Gold == 43 && gained == 43, "A boss's base gold was not amplified.");
        Check(f.Wallet.TryAddEarnedGold(8, 4) && f.Wallet.Gold == 57 && gained == 57,
            "Generated gold was amplified or the earned credit was published incorrectly.");

        using var nextRun = new Fixture();
        var newRate = nextRun.Item("next-run-rate");
        newRate.boundaryEffects = rate.boundaryEffects;
        nextRun.Catalog.items = new[] { newRate };
        Check(nextRun.Items.TryAddItem(newRate.itemId) && nextRun.Wallet.TryAddEarnedGold(3)
            && nextRun.Wallet.Gold == 3, "Fractional carry crossed into a new run.");
    }

    private static ShopOffer ItemOffer(ShopStock stock, ItemDefinition item)
    {
        foreach (var offer in stock.Offers) if (offer.Item == item) return offer;
        throw new InvalidOperationException("Economy regression item was not in stock: " + item.itemId);
    }

    private static void InvokeLifecycle(EnemyGoldReward reward, string method)
        => typeof(EnemyGoldReward).GetMethod(method, Hidden).Invoke(reward, null);

    private static void RaiseDeathAgain(Enemy enemy)
        => ((Action)typeof(Enemy).GetField("OnDied", Hidden).GetValue(enemy))?.Invoke();

    private static void Near(float actual, float expected, string message)
        => Check(!float.IsNaN(actual) && Mathf.Abs(actual - expected) < 0.001f, $"{message} {actual} != {expected}");

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("Shop economy regression: " + message);
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
            _root = new GameObject("Shop economy regression") { hideFlags = HideFlags.HideAndDontSave };
            _root.SetActive(false);
            Settings = Own(ScriptableObject.CreateInstance<EconomySettingsSO>());
            Catalog = Own(ScriptableObject.CreateInstance<ItemCatalog>());
            Catalog.projectId = "shop-economy-regression";
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

        public ItemDefinition Item(string id)
        {
            var item = Own(ScriptableObject.CreateInstance<ItemDefinition>());
            item.projectId = Catalog.projectId;
            item.candidateKey = "candidate-" + id;
            item.itemId = id;
            item.displayName = id;
            item.purchasePrice = 20;
            return item;
        }

        public GameObject Child(string name)
        {
            var child = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(_root.transform, false);
            return child;
        }

        public Enemy Enemy(string name, int gold, out EnemyGoldReward reward)
        {
            var actor = Child(name);
            actor.SetActive(false);
            var enemy = actor.AddComponent<Enemy>();
            enemy.EnemyStat = actor.GetComponent<EnemyStat>();
            enemy.EnemyStat.CurrentHp = 10f;
            actor.GetComponent<PoiseHandler>().ResetPoise();
            reward = actor.AddComponent<EnemyGoldReward>();
            actor.SetActive(true);
            reward.Initialize(Wallet, gold);
            return enemy;
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
