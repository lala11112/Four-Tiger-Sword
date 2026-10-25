using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Player-owned item effects; importing or editing a catalog never grants ownership.</summary>
[DisallowMultipleComponent]
public sealed class PlayerItemRuntime : MonoBehaviour
{
    public const float BaseDodgeSpCost = 20f;
    public const float BaseSwitchSpCost = 10f;

    [SerializeField] private ItemCatalog catalog;
    private PlayerStatManager _stats;
    private Func<ElementType> _formProvider;
    private bool _initialized;
    private bool _suspended;
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

    public ItemCatalog Catalog
    {
        get => catalog;
        set
        {
            if (catalog == value) { RecalculateForCurrentForm(); return; }
            bool hadItems = _owned.Count > 0;
            catalog = value;
            _owned.Clear();
            _ownedItems.Clear();
            RecalculateForCurrentForm();
            if (_initialized && hadItems) OnOwnedItemsChanged?.Invoke();
        }
    }

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
        if (!_initialized || catalog == null || string.IsNullOrWhiteSpace(itemId) || Contains(itemId)) return false;
        var definition = catalog.Find(itemId);
        if (definition == null || !string.Equals(definition.projectId, catalog.projectId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(definition.candidateKey) || definition.effects == null) return false;
        // candidateKey is the content identity, even when a malformed catalog uses two item IDs for it.
        foreach (var owned in _owned.Values)
            if (string.Equals(owned.Definition.candidateKey, definition.candidateKey, StringComparison.Ordinal)) return false;
        var active = new ActiveEffect[definition.effects.Length];
        for (int i = 0; i < active.Length; i++)
        {
            var effect = definition.effects[i];
            if (effect == null || !effect.IsValid()) return false;
            active[i] = new ActiveEffect { Definition = effect.Copy() };
        }
        _owned.Add(itemId, new OwnedItem { Definition = definition, Effects = active });
        _ownedItems.Add(definition);
        RecalculateForCurrentForm();
        OnOwnedItemsChanged?.Invoke();
        return true;
    }

    public bool TryRemoveItem(string itemId)
    {
        if (!_initialized || itemId == null || !_owned.TryGetValue(itemId, out var owned)) return false;
        _owned.Remove(itemId);
        _ownedItems.Remove(owned.Definition);
        RecalculateForCurrentForm();
        OnOwnedItemsChanged?.Invoke();
        return true;
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
        if (_stats == null || _stats.CurrentHp <= 0f) { ClearTimedEffects(); return; }
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
        if (_suspended || catalog == null) { _stats.RemoveItemResourceModifiers(this); return; }
        // Submit every resource delta as one replacement, then clamp current resources once.
        var form = CurrentForm;
        _stats.SetItemResourceModifiers(this, Sum(ItemStat.MaxHp, form), Sum(ItemStat.MaxSp, form),
            Sum(ItemStat.SpRegen, form), ValidPositive(catalog.policy?.maxSpCap, 200f),
            ValidPositive(catalog.policy?.regenCap, 40f));
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
        if (_stats != null) _stats.RemoveItemResourceModifiers(this);
    }

    private void OnDestroy()
    {
        if (_stats != null) _stats.RemoveItemResourceModifiers(this);
    }
}
