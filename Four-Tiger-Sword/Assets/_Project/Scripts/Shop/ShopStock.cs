using System;
using System.Collections.Generic;

public enum ShopPurchaseResult
{
    Success,
    InsufficientGold,
    AlreadyOwned,
    SoldOut,
    FullHealth,
    InvalidOffer,
    Unavailable
}

/// <summary>A stable offer snapshot. Presentation can read it but cannot edit stock or prices.</summary>
public sealed class ShopOffer
{
    public string Id { get; }
    public ItemDefinition Item { get; }
    public int Price { get; }
    public bool IsHealing { get; }
    public bool SoldOut { get; private set; }
    internal float HealingFraction { get; }

    internal ShopOffer(ItemDefinition item)
    {
        Item = item;
        Id = "item:" + item.projectId + ":" + item.candidateKey;
        Price = item.purchasePrice;
    }

    internal ShopOffer(int price, float fraction)
    {
        Id = "healing";
        IsHealing = true;
        Price = price;
        HealingFraction = fraction;
    }

    internal void MarkSoldOut() => SoldOut = true;
}

/// <summary>One stage's inventory and purchase rules. It has no scene, input, or UI dependencies.</summary>
public sealed class ShopStock
{
    public IReadOnlyList<ShopOffer> Offers { get; }
    public event Action Changed;
    private bool _purchaseInProgress;

    private ShopStock(List<ShopOffer> offers) => Offers = offers.AsReadOnly();

    public static ShopStock Create(ItemCatalog catalog, PlayerItemRuntime items, EconomySettingsSO settings, int stageSeed)
    {
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (!settings.Validate(out var error)) throw new ArgumentException(error, nameof(settings));

        var candidates = new List<ItemDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (catalog.items != null)
        {
            foreach (var item in catalog.items)
            {
                if (!ValidCandidate(item, catalog) || Owned(items, item)
                    || !ids.Add(item.itemId) || !keys.Add(item.candidateKey)) continue;
                candidates.Add(item);
            }
        }
        candidates.Sort((a, b) => StringComparer.Ordinal.Compare(a.itemId, b.itemId));
        // An independent PRNG keeps opening a shop from changing dungeon or encounter generation.
        var random = new Random(unchecked(stageSeed ^ 0x53484F50));
        long requested = (long)settings.baseItemCount + random.Next(-1, 2);
        int count = (int)Math.Min(candidates.Count, requested);
        var offers = new List<ShopOffer>(count + 1);
        for (int i = 0; i < count; i++)
        {
            int selected = random.Next(i, candidates.Count);
            (candidates[i], candidates[selected]) = (candidates[selected], candidates[i]);
            offers.Add(new ShopOffer(candidates[i]));
        }
        offers.Add(new ShopOffer(settings.healingPrice, settings.healingFraction));
        return new ShopStock(offers);
    }

    public ShopPurchaseResult TryPurchase(string offerId, PlayerWallet wallet, PlayerItemRuntime items, PlayerStatManager stats)
    {
        if (_purchaseInProgress) return ShopPurchaseResult.Unavailable;
        ShopOffer offer = null;
        foreach (var candidate in Offers)
            if (string.Equals(candidate.Id, offerId, StringComparison.Ordinal)) { offer = candidate; break; }
        if (offer == null) return ShopPurchaseResult.InvalidOffer;
        if (offer.SoldOut) return ShopPurchaseResult.SoldOut;
        if (wallet == null || !wallet.IsInitialized || stats == null || stats.CurrentHp <= 0f)
            return ShopPurchaseResult.Unavailable;
        if (offer.IsHealing)
        {
            if (stats.CurrentHp >= stats.MaxHp) return ShopPurchaseResult.FullHealth;
        }
        else
        {
            if (items == null || offer.Item == null || items.Catalog == null
                || items.Catalog.Find(offer.Item.itemId) != offer.Item) return ShopPurchaseResult.Unavailable;
            if (Owned(items, offer.Item)) return ShopPurchaseResult.AlreadyOwned;
        }
        if (wallet.Gold < offer.Price) return ShopPurchaseResult.InsufficientGold;

        _purchaseInProgress = true;
        try
        {
            bool purchased = wallet.TryPurchase(offer.Price, () =>
            {
                bool granted;
                if (offer.IsHealing)
                {
                    float before = stats.CurrentHp;
                    try { stats.Heal(stats.MaxHp * offer.HealingFraction); }
                    catch (Exception error)
                    { UnityEngine.Debug.LogWarning("[Shop] Healing fulfillment observer failed: " + error); }
                    granted = stats.CurrentHp > before;
                }
                else
                {
                    try { granted = items.TryAddItem(offer.Item.itemId); }
                    catch (Exception error)
                    {
                        UnityEngine.Debug.LogWarning("[Shop] Item fulfillment observer failed: " + error);
                        // Inventory publishes ownership after mutating it. A failing observer cannot undo payment.
                        granted = items.Contains(offer.Item.itemId);
                    }
                }
                if (!granted) return false;
                offer.MarkSoldOut();
                return true;
            });
            if (!purchased) return ShopPurchaseResult.Unavailable;
            NotifyChanged();
            return ShopPurchaseResult.Success;
        }
        finally { _purchaseInProgress = false; }
    }

    private void NotifyChanged()
    {
        if (Changed == null) return;
        foreach (Action callback in Changed.GetInvocationList())
        {
            try { callback(); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("[Shop] Stock Changed observer failed: " + error); }
        }
    }

    private static bool Owned(PlayerItemRuntime items, ItemDefinition item)
    {
        if (items.Contains(item.itemId)) return true;
        foreach (var owned in items.OwnedItems)
            if (owned != null && string.Equals(owned.candidateKey, item.candidateKey, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool ValidCandidate(ItemDefinition item, ItemCatalog catalog)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.itemId) || string.IsNullOrWhiteSpace(item.candidateKey)
            || !string.Equals(item.projectId, catalog.projectId, StringComparison.Ordinal)
            || item.purchasePrice < 0 || catalog.Find(item.itemId) != item || item.effects == null) return false;
        foreach (var effect in item.effects) if (effect == null || !effect.IsValid()) return false;
        return true;
    }
}
