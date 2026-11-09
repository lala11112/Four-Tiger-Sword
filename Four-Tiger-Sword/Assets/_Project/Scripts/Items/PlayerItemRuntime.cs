using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Player-owned item effects; importing or editing a catalog never grants ownership.</summary>
[DisallowMultipleComponent]
public sealed partial class PlayerItemRuntime : MonoBehaviour
{
    public const float BaseDodgeSpCost = 20f;
    public const float BaseSwitchSpCost = 10f;

    [SerializeField] private ItemCatalog catalog;
    private PlayerStatManager _stats;
    private Func<ElementType> _formProvider;
    private bool _initialized;
    private bool _suspended;
    private bool _mutationInProgress;
    private readonly Dictionary<string, OwnedItem> _owned = new(StringComparer.Ordinal);
    private readonly List<ItemDefinition> _ownedItems = new();
    private IReadOnlyList<ItemDefinition> _ownedView;

    private sealed class OwnedItem
    {
        public ItemDefinition Definition;
        // One slot per source effect index makes refresh independent from other effects/items.
        public ActiveEffect[] Effects;
    }

    private sealed class ActiveEffect
    {
        public ItemEffect Definition;
        public float Remaining;
        public bool Active => Definition.trigger == ItemEffectTrigger.WhileOwned || Remaining > 0f;
    }

    public event Action OnOwnedItemsChanged;
    public event Action<ItemDefinition> ItemAcquired;
    public event Action<ItemSynergyState> SynergyTierActivated;

    public ItemCatalog Catalog
    {
        get => catalog;
        set
        {
            if (_mutationInProgress) return;
            if (catalog == value) { RecalculateForCurrentForm(); return; }
            _mutationInProgress = true;
            try
            {
                bool hadItems = _owned.Count > 0;
                ResetBoundaryForCatalogChange();
                catalog = value;
                _owned.Clear();
                _ownedItems.Clear();
                RecalculateCommittedChange();
                if (_initialized && hadItems) NotifyOwnedItemsChanged();
            }
            finally { _mutationInProgress = false; }
        }
    }

    internal bool IsInitialized => _initialized;
    internal bool IsMutationInProgress => _mutationInProgress;
    public IReadOnlyList<ItemDefinition> OwnedItems => _ownedView ??= _ownedItems.AsReadOnly();
    public float DodgeSpCost => EffectiveCost(ItemStat.DodgeSpCost, BaseDodgeSpCost,
        ValidMinimum(catalog != null ? catalog.policy?.minDodgeCost : null, 8f));
    public float SwitchSpCost => EffectiveCost(ItemStat.SwitchSpCost, BaseSwitchSpCost,
        ValidMinimum(catalog != null ? catalog.policy?.minSwitchCost : null, 4f));

    public void Initialize(PlayerController player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        // A component serialized disabled has never received OnDisable.
        _suspended = !isActiveAndEnabled;
        Initialize(player.StatManager != null ? player.StatManager : player.GetComponent<PlayerStatManager>(),
            () => player != null ? player.Element : ElementType.ELEMENT_NONE);
    }

    /// <summary>Explicit dependency setup also supports isolated runtime regression checks.</summary>
    public void Initialize(PlayerStatManager stats, Func<ElementType> formProvider)
    {
        if (stats == null) throw new ArgumentNullException(nameof(stats));
        if (formProvider == null) throw new ArgumentNullException(nameof(formProvider));
        if (_stats != null && _stats != stats) _stats.RemoveItemResourceModifiers(this);
        _stats = stats;
        _formProvider = formProvider;
        _stats.EnsureInitialized();
        _initialized = true;
        RecalculateForCurrentForm();
    }

    public bool Contains(string itemId) => itemId != null && _owned.ContainsKey(itemId);

    public bool TryAddItem(string itemId)
    {
        if (!_initialized || _mutationInProgress || catalog == null || string.IsNullOrWhiteSpace(itemId)
            || Contains(itemId)) return false;
        var definition = catalog.Find(itemId);
        // candidateKey is the content identity, even when a malformed catalog uses two item IDs for it.
        if (definition == null) return false;
        foreach (var owned in _owned.Values)
            if (string.Equals(owned.Definition.candidateKey, definition.candidateKey, StringComparison.Ordinal)) return false;
        if (!TryCreateOwnedItem(definition, out var added)) return false;
        _mutationInProgress = true;
        try
        {
            var previousTiers = CaptureActiveSynergyTiers();
            _owned.Add(itemId, added);
            _ownedItems.Add(definition);
            RecalculateCommittedChange();
            NotifyOwnedItemsChanged();
            NotifyItemAcquired(definition);
            NotifyActivatedSynergyTiers(previousTiers);
            return true;
        }
        finally { _mutationInProgress = false; }
    }

    public bool TryRemoveItem(string itemId)
    {
        if (!_initialized || _mutationInProgress || itemId == null || !_owned.TryGetValue(itemId, out var owned)) return false;
        _mutationInProgress = true;
        try
        {
            _owned.Remove(itemId);
            _ownedItems.Remove(owned.Definition);
            RecalculateCommittedChange();
            NotifyOwnedItemsChanged();
            return true;
        }
        finally { _mutationInProgress = false; }
    }

    /// <summary>Consumes two distinct owned items and grants one fresh item as a single stat replacement.</summary>
    public bool TryExchangeItems(string firstId, string secondId, string resultId)
    {
        if (!_initialized || _mutationInProgress || catalog == null
            || string.IsNullOrWhiteSpace(firstId) || string.IsNullOrWhiteSpace(secondId)
            || string.Equals(firstId, secondId, StringComparison.Ordinal)
            || !_owned.TryGetValue(firstId, out var first) || !_owned.TryGetValue(secondId, out var second)
            || string.IsNullOrWhiteSpace(resultId)) return false;
        var definition = catalog.Find(resultId);
        if (!TryCreateOwnedItem(definition, out var replacement)) return false;
        foreach (var pair in _owned)
        {
            if (pair.Key == firstId || pair.Key == secondId) continue;
            if (pair.Key == resultId || string.Equals(pair.Value.Definition.candidateKey,
                definition.candidateKey, StringComparison.Ordinal)) return false;
        }
        _mutationInProgress = true;
        try
        {
            var previousTiers = CaptureActiveSynergyTiers();
            _owned.Remove(firstId);
            _owned.Remove(secondId);
            _ownedItems.Remove(first.Definition);
            _ownedItems.Remove(second.Definition);
            _owned.Add(resultId, replacement);
            _ownedItems.Add(definition);
            RecalculateCommittedChange();
            NotifyOwnedItemsChanged();
            NotifyItemAcquired(definition);
            NotifyActivatedSynergyTiers(previousTiers);
            return true;
        }
        finally { _mutationInProgress = false; }
    }

    private bool TryCreateOwnedItem(ItemDefinition definition, out OwnedItem owned)
    {
        owned = null;
        if (definition == null || catalog == null
            || !string.Equals(definition.projectId, catalog.projectId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(definition.candidateKey) || definition.effects == null) return false;
        if (definition.boundaryEffects != null)
            foreach (var effect in definition.boundaryEffects)
                if (effect == null || !effect.IsValid()) return false;
        var active = new ActiveEffect[definition.effects.Length];
        for (int i = 0; i < active.Length; i++)
        {
            var effect = definition.effects[i];
            if (effect == null || !effect.IsValid()) return false;
            active[i] = new ActiveEffect { Definition = effect.Copy() };
        }
        owned = new OwnedItem { Definition = definition, Effects = active };
        return true;
    }

    private void RecalculateCommittedChange()
    {
        try { RecalculateForCurrentForm(); }
        catch (Exception error) { Debug.LogWarning("[Items] Committed stat update observer failed: " + error, this); }
    }

    private void NotifyItemAcquired(ItemDefinition definition)
    {
        if (ItemAcquired == null) return;
        foreach (Action<ItemDefinition> callback in ItemAcquired.GetInvocationList())
        {
            try { callback(definition); }
            catch (Exception error) { Debug.LogWarning("[Items] Acquisition observer failed: " + error, this); }
        }
    }

    private void NotifyOwnedItemsChanged()
    {
        if (OnOwnedItemsChanged == null) return;
        foreach (Action callback in OnOwnedItemsChanged.GetInvocationList())
        {
            try { callback(); }
            catch (Exception error) { Debug.LogWarning("[Items] Ownership observer failed: " + error, this); }
        }
    }

    public void NotifyFormSwitchSucceeded()
    {
        if (!_initialized || _suspended || _stats == null || _stats.CurrentHp <= 0f) return;
        var form = CurrentForm;
        foreach (var owned in _owned.Values)
            foreach (var effect in owned.Effects)
                if (effect.Definition.trigger == ItemEffectTrigger.FormSwitchSucceeded && effect.Definition.AppliesTo(form))
                    effect.Remaining = effect.Definition.durationSeconds;
        RecalculateForCurrentForm();
    }

    public void Tick(float scaledDt)
    {
        if (!_initialized || _suspended || !ItemEffect.IsFinite(scaledDt) || scaledDt <= 0f) return;
        if (_stats == null || _stats.CurrentHp <= 0f) { ClearTimedEffects(); ClearBoundaryTransient(); return; }
        TickBoundary(scaledDt);
        bool changed = false;
        foreach (var owned in _owned.Values)
        {
            foreach (var effect in owned.Effects)
            {
                if (effect.Definition.trigger != ItemEffectTrigger.FormSwitchSucceeded || effect.Remaining <= 0f) continue;
                effect.Remaining = Mathf.Max(0f, effect.Remaining - scaledDt);
                changed |= effect.Remaining <= 0f;
            }
        }
        if (changed) RecalculateForCurrentForm();
    }

    public void ClearTimedEffects()
    {
        bool changed = false;
        foreach (var owned in _owned.Values)
            foreach (var effect in owned.Effects)
                if (effect.Remaining > 0f) { effect.Remaining = 0f; changed = true; }
        if (changed) RecalculateForCurrentForm();
    }

    public void RecalculateForCurrentForm()
    {
        if (!_initialized || _stats == null) return;
        SyncBoundarySources();
        if (_suspended || catalog == null) { _stats.RemoveItemResourceModifiers(this); return; }
        // Submit every resource delta as one replacement, then clamp current resources once.
        var form = CurrentForm;
        _stats.SetItemResourceModifiers(this, Sum(ItemStat.MaxHp, form) + PassiveBoundaryStat(BoundaryItemStat.MaxHpRatio), Sum(ItemStat.MaxSp, form),
            Sum(ItemStat.SpRegen, form), ValidPositive(catalog.policy?.maxSpCap, 200f),
            ValidPositive(catalog.policy?.regenCap, 40f), PassiveBoundaryStat(BoundaryItemStat.AttackRatio),
            PassiveBoundaryStat(BoundaryItemStat.DefenseRatio), PassiveBoundaryStat(BoundaryItemStat.CriticalChancePoints),
            PassiveBoundaryStat(BoundaryItemStat.CriticalDamagePoints), PassiveBoundaryStat(BoundaryItemStat.ShieldGainRatio));
    }

    public float GetNormalDamageMultiplier(ElementType form) => DamageMultiplier(ItemStat.NormalDamage, form);
    public float GetSwitchDamageMultiplier(ElementType form) => DamageMultiplier(ItemStat.SwitchDamage, form);

    private ElementType CurrentForm => _formProvider != null ? _formProvider() : ElementType.ELEMENT_NONE;
    private float DamageMultiplier(ItemStat stat, ElementType form)
        => Mathf.Max(0f, 1f + Sum(stat, form));
    private float EffectiveCost(ItemStat stat, float basis, float minimum)
        => Mathf.Max(minimum, basis + Sum(stat, CurrentForm));

    private float Sum(ItemStat stat, ElementType form)
    {
        if (!_initialized || _suspended) return 0f;
        double sum = 0d;
        foreach (var owned in _owned.Values)
            foreach (var effect in owned.Effects)
                if (effect.Active && effect.Definition.stat == stat && effect.Definition.AppliesTo(form))
                    sum += effect.Definition.amount;
        return (float)Math.Max(-float.MaxValue / 4d, Math.Min(float.MaxValue / 4d, sum));
    }

    private static float ValidMinimum(float? value, float fallback)
        => value.HasValue && ItemEffect.IsFinite(value.Value) && value.Value >= 0f ? value.Value : fallback;
    private static float ValidPositive(float? value, float fallback)
        => value.HasValue && ItemEffect.IsFinite(value.Value) && value.Value > 0f ? value.Value : fallback;

    private void OnEnable()
    {
        _suspended = false;
        RecalculateForCurrentForm();
    }

    private void OnDisable()
    {
        _suspended = true;
        ClearTimedEffects();
        ClearBoundaryTransient();
        if (_stats != null) _stats.RemoveItemResourceModifiers(this);
    }

    private void OnDestroy()
    {
        if (_stats != null) _stats.RemoveItemResourceModifiers(this);
    }
}
