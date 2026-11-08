using System;
using System.Collections.Generic;

public enum ShopSaleResult
{
    Success,
    Unavailable,
    InvalidItem,
    NotOwned,
    GoldOverflow
}

public enum ShopFusionResult
{
    Success,
    Unavailable,
    InvalidMaterials,
    NotOwned,
    NoCandidates,
    ChoiceRequired,
    InvalidChoice
}

public sealed class ShopFusionChoice
{
    public int Token { get; }
    public string FirstId { get; }
    public string SecondId { get; }
    public int Grade { get; }
    public IReadOnlyList<ItemDefinition> Candidates { get; }
    internal int Sequence { get; }
    internal ShopFusionChoice(int token, string first, string second, int grade, int sequence,
        List<ItemDefinition> candidates)
    { Token = token; FirstId = first; SecondId = second; Grade = grade; Sequence = sequence;
        Candidates = candidates.AsReadOnly(); }
}

/// <summary>One stage's shared transaction gate and independent fusion random sequence.</summary>
public sealed class ShopTransactions
{
    private readonly PlayerItemRuntime _items;
    private readonly PlayerWallet _wallet;
    private readonly PlayerStatManager _stats;
    private readonly EconomySettingsSO _settings;
    private readonly int _stageSeed;
    private int _successfulFusions;
    private bool _transactionInProgress;
    private readonly EngravingSnapshot _engraving;
    private readonly Dictionary<string, ShopFusionChoice> _choices = new(StringComparer.Ordinal);
    private int _nextChoiceToken;

    public ShopTransactions(PlayerItemRuntime items, PlayerWallet wallet, PlayerStatManager stats,
        EconomySettingsSO settings, int stageSeed, EngravingSnapshot engraving = null)
    {
        _items = items;
        _wallet = wallet;
        _stats = stats;
        _settings = settings;
        _stageSeed = stageSeed;
        _engraving = engraving ?? EngravingSnapshot.Empty;
    }

    private bool Available => !_transactionInProgress && _items != null && _items.IsInitialized
        && !_items.IsMutationInProgress && _items.Catalog != null && _wallet != null && _wallet.IsInitialized
        && _stats != null && _stats.CurrentHp > 0f && _settings != null && _settings.Validate(out _);

    public ShopPurchaseResult TryBuy(ShopStock stock, string offerId)
    {
        if (!Available || stock == null) return ShopPurchaseResult.Unavailable;
        _transactionInProgress = true;
        try { return stock.TryPurchase(offerId, _wallet, _items, _stats); }
        finally { _transactionInProgress = false; }
    }

    public ShopRefreshResult TryRefresh(ShopStock stock)
    {
        if (!Available || stock == null) return ShopRefreshResult.Unavailable;
        _transactionInProgress = true;
        try { return stock.TryRefresh(_wallet, _items, _stats); }
        finally { _transactionInProgress = false; }
    }

    public bool TryGetSalePrice(string itemId, out int price)
    {
        price = 0;
        if (_items == null || _settings == null || !_settings.Validate(out _)) return false;
        var item = OwnedDefinition(itemId);
        if (!ValidDefinition(item) || item.purchasePrice <= 0) return false;
        float fraction = _engraving.Has("sell") ? _engraving.Amount("sell") : _settings.saleFraction;
        price = (int)Math.Floor((double)item.purchasePrice * fraction + .000001d);
        return true;
    }

    public ShopSaleResult TrySell(string itemId)
    {
        if (!Available) return ShopSaleResult.Unavailable;
        if (string.IsNullOrWhiteSpace(itemId)) return ShopSaleResult.InvalidItem;
        if (!_items.Contains(itemId)) return ShopSaleResult.NotOwned;
        if (!TryGetSalePrice(itemId, out int price)) return ShopSaleResult.InvalidItem;
        if (price > int.MaxValue - _wallet.Gold) return ShopSaleResult.GoldOverflow;
        _transactionInProgress = true;
        try
        {
            return _wallet.TrySale(price, () => _items.TryRemoveItem(itemId))
                ? ShopSaleResult.Success : ShopSaleResult.Unavailable;
        }
        finally { _transactionInProgress = false; }
    }

    /// <summary>Returns the material grade and the effective +1 chance after filtering current ownership.</summary>
    public ShopFusionResult GetFusionPreview(string firstId, string secondId, out int grade, out float upgradeChance)
    {
        grade = 0;
        upgradeChance = 0f;
        if (!Available) return ShopFusionResult.Unavailable;
        return FusionCandidates(firstId, secondId, out grade, out upgradeChance, out _, out _);
    }

    public ShopFusionResult TryFuse(string firstId, string secondId, out ItemDefinition resultItem)
    {
        resultItem = null;
        if (!Available) return ShopFusionResult.Unavailable;
        if (_engraving.Has("fusion")) return ShopFusionResult.ChoiceRequired;
        var validation = FusionCandidates(firstId, secondId, out _, out float upgradeChance,
            out var sameGrade, out var higherGrade);
        if (validation != ShopFusionResult.Success) return validation;

        _transactionInProgress = true;
        try
        {
            // Failed attempts and previews do not advance the committed sequence or Unity's shared RNG.
            var random = new Random(unchecked(_stageSeed ^ 0x46555345 ^ (_successfulFusions * 0x1F123BB5)));
            var pool = random.NextDouble() < upgradeChance ? higherGrade : sameGrade;
            var selected = pool[random.Next(pool.Count)];
            if (!_items.TryExchangeItems(firstId, secondId, selected.itemId)) return ShopFusionResult.Unavailable;
            _successfulFusions = unchecked(_successfulFusions + 1);
            _choices.Clear();
            resultItem = selected;
            return ShopFusionResult.Success;
        }
        finally { _transactionInProgress = false; }
    }

    /// <summary>Preparing or closing a choice never advances RNG or consumes either material.</summary>
    public ShopFusionResult BeginFusionChoice(string firstId, string secondId, out ShopFusionChoice choice)
    {
        choice = null;
        if (!Available || !_engraving.Has("fusion")) return ShopFusionResult.Unavailable;
        var validation = FusionCandidates(firstId, secondId, out _, out float chance,
            out var same, out var higher);
        if (validation != ShopFusionResult.Success) return validation;
        if (StringComparer.Ordinal.Compare(firstId, secondId) > 0) (firstId, secondId) = (secondId, firstId);
        string key = firstId + "\n" + secondId;
        if (_choices.TryGetValue(key, out choice) && choice.Sequence == _successfulFusions) return ShopFusionResult.Success;
        var random = new Random(unchecked(_stageSeed ^ 0x46555345 ^ (_successfulFusions * 0x1F123BB5)));
        var pool = random.NextDouble() < chance ? higher : same;
        int count = Math.Min(Math.Min(3, Math.Max(1, _engraving.Limit("fusion"))), pool.Count);
        var candidates = new List<ItemDefinition>(count);
        for (int i = 0; i < count; i++)
        {
            int index = random.Next(pool.Count);
            candidates.Add(pool[index]); pool.RemoveAt(index);
        }
        choice = new ShopFusionChoice(++_nextChoiceToken, firstId, secondId, candidates[0].grade,
            _successfulFusions, candidates);
        _choices[key] = choice;
        return ShopFusionResult.Success;
    }

    public ShopFusionResult CommitFusionChoice(int token, string resultId, out ItemDefinition resultItem)
    {
        resultItem = null;
        if (!Available || !_engraving.Has("fusion")) return ShopFusionResult.Unavailable;
        ShopFusionChoice choice = null;
        foreach (var entry in _choices.Values) if (entry.Token == token) { choice = entry; break; }
        if (choice == null || choice.Sequence != _successfulFusions) return ShopFusionResult.InvalidChoice;
        var validation = FusionCandidates(choice.FirstId, choice.SecondId, out _, out _, out var same, out var higher);
        if (validation != ShopFusionResult.Success) return validation;
        ItemDefinition selected = null;
        foreach (var item in choice.Candidates)
            if (item.itemId == resultId && _items.Catalog.Find(item.itemId) == item
                && (same.Contains(item) || higher.Contains(item))) { selected = item; break; }
        if (selected == null) return ShopFusionResult.InvalidChoice;
        _transactionInProgress = true;
        try
        {
            if (!_items.TryExchangeItems(choice.FirstId, choice.SecondId, selected.itemId)) return ShopFusionResult.Unavailable;
            _successfulFusions = unchecked(_successfulFusions + 1);
            _choices.Clear();
            resultItem = selected;
            return ShopFusionResult.Success;
        }
        finally { _transactionInProgress = false; }
    }

    private ShopFusionResult FusionCandidates(string firstId, string secondId, out int grade,
        out float upgradeChance, out List<ItemDefinition> sameGrade, out List<ItemDefinition> higherGrade)
    {
        grade = 0;
        upgradeChance = 0f;
        sameGrade = new List<ItemDefinition>();
        higherGrade = new List<ItemDefinition>();
        if (string.IsNullOrWhiteSpace(firstId) || string.IsNullOrWhiteSpace(secondId)
            || string.Equals(firstId, secondId, StringComparison.Ordinal)) return ShopFusionResult.InvalidMaterials;
        if (!_items.Contains(firstId) || !_items.Contains(secondId)) return ShopFusionResult.NotOwned;
        var first = OwnedDefinition(firstId);
        var second = OwnedDefinition(secondId);
        if (!ValidDefinition(first) || !ValidDefinition(second) || first.grade != second.grade
            || string.Equals(first.candidateKey, second.candidateKey, StringComparison.Ordinal))
            return ShopFusionResult.InvalidMaterials;

        grade = first.grade;
        var sorted = new List<ItemDefinition>();
        if (_items.Catalog.items != null)
            foreach (var candidate in _items.Catalog.items)
                if (ValidDefinition(candidate) && (candidate.grade == grade || candidate.grade == grade + 1))
                    sorted.Add(candidate);
        sorted.Sort((a, b) => StringComparer.Ordinal.Compare(a.itemId, b.itemId));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in sorted)
        {
            if (StillOwned(candidate, firstId, secondId) || !ids.Add(candidate.itemId)
                || !keys.Add(candidate.candidateKey)) continue;
            if (candidate.grade == grade) sameGrade.Add(candidate);
            else higherGrade.Add(candidate);
        }
        if (sameGrade.Count == 0 && higherGrade.Count == 0) return ShopFusionResult.NoCandidates;
        upgradeChance = higherGrade.Count == 0 ? 0f : sameGrade.Count == 0 ? 1f : _settings.fusionUpgradeChance;
        return ShopFusionResult.Success;
    }

    private ItemDefinition OwnedDefinition(string itemId)
    {
        if (_items == null || string.IsNullOrWhiteSpace(itemId) || !_items.Contains(itemId)) return null;
        foreach (var item in _items.OwnedItems)
            if (item != null && string.Equals(item.itemId, itemId, StringComparison.Ordinal)) return item;
        return null;
    }

    private bool StillOwned(ItemDefinition candidate, string firstId, string secondId)
    {
        foreach (var owned in _items.OwnedItems)
        {
            if (owned == null || owned.itemId == firstId || owned.itemId == secondId) continue;
            if (string.Equals(owned.itemId, candidate.itemId, StringComparison.Ordinal)
                || string.Equals(owned.candidateKey, candidate.candidateKey, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private bool ValidDefinition(ItemDefinition item)
    {
        var catalog = _items != null ? _items.Catalog : null;
        if (item == null || catalog == null || string.IsNullOrWhiteSpace(item.itemId)
            || string.IsNullOrWhiteSpace(item.candidateKey) || item.grade < 1 || item.grade > 4
            || !string.Equals(item.projectId, catalog.projectId, StringComparison.Ordinal)
            || item.purchasePrice < 0 || catalog.Find(item.itemId) != item || item.effects == null) return false;
        foreach (var effect in item.effects)
            if (effect == null || !effect.IsValid()) return false;
        foreach (var effect in item.boundaryEffects ?? Array.Empty<BoundaryItemEffect>())
            if (effect == null || !effect.IsValid()) return false;
        return true;
    }
}
