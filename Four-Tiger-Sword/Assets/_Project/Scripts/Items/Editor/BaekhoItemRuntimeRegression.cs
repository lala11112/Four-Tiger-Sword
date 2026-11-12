using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Isolated editor regressions. Never edits assets, settings, or open scenes.</summary>
public static class BaekhoItemRuntimeRegression
{
    [MenuItem("Tools/Studio Baekho/Items/Run Runtime Regression")]
    private static void RunFromMenu() => Run();

    public static string Run()
    {
        OwnershipAndRemoval();
        TimedRefreshAndFormFiltering();
        IndependentSourcesAndReacquisition();
        AtomicHealthAndLegacyModifiers();
        FormChangeDoesNotTemporarilyClamp();
        DisableAndDeathCleanup();
        InitiallyDisabledRuntime();
        RetiredSerializedEffectsRejected();
        int actualItems = ImportedCatalogs();
        string result = $"Baekho item runtime: 8 regression scenarios passed; {actualItems} imported items verified.";
        Debug.Log(result);
        return result;
    }

    private static void OwnershipAndRemoval()
    {
        using var f = new Fixture();
        var item = f.Item("owned", Effect(ItemStat.MaxHp, 0.25f), Effect(ItemStat.NormalDamage, 0.2f));
        f.Catalog.items = new[] { item };
        int changes = 0;
        f.Runtime.OnOwnedItemsChanged += () => changes++;
        Check(f.Runtime.OwnedItems.Count == 0, "A catalog must not grant ownership.");
        Near(f.Runtime.GetNormalDamageMultiplier(f.Form), 1f, "Unowned damage");
        Check(f.Runtime.TryAddItem(item.itemId), "First grant must succeed.");
        Check(!f.Runtime.TryAddItem(item.itemId), "Duplicate ownership must be rejected.");
        Check(!f.Runtime.TryAddItem("missing"), "Unknown content must be rejected.");
        Check(f.Runtime.Contains(item.itemId) && f.Runtime.OwnedItems.Count == 1, "Ownership view");
        Near(f.Stats.MaxHp, 1250f, "Owned max HP ratio");
        Near(f.Stats.CurrentHp, 1000f, "Increasing max HP must not heal.");
        Near(f.Runtime.GetNormalDamageMultiplier(f.Form), 1.2f, "Owned damage");
        Check(f.Runtime.TryRemoveItem(item.itemId), "Removal must succeed.");
        Check(!f.Runtime.TryRemoveItem(item.itemId), "Repeated removal must be a no-op.");
        Near(f.Stats.MaxHp, 1000f, "Removed max HP");
        Near(f.Runtime.GetNormalDamageMultiplier(f.Form), 1f, "Removed damage");
        Check(changes == 2, "Ownership notifications must occur once per real change.");
    }

    private static void TimedRefreshAndFormFiltering()
    {
        using var f = new Fixture();
        var item = f.Item("timed", Effect(ItemStat.NormalDamage, 0.1f),
            Timed(ItemStat.NormalDamage, 0.4f, 2f, ElementType.ELEMENT_FIRE),
            Timed(ItemStat.MaxHp, 0.5f, 2f, ElementType.ELEMENT_FIRE));
        f.Catalog.items = new[] { item };
        Check(f.Runtime.TryAddItem(item.itemId), "Timed source grant");
        f.Runtime.NotifyFormSwitchSucceeded(); // Water does not activate Fire-only effects.
        Near(f.Runtime.GetNormalDamageMultiplier(ElementType.ELEMENT_FIRE), 1.1f, "Trigger destination filter");
        f.ChangeForm(ElementType.ELEMENT_FIRE);
        f.Runtime.NotifyFormSwitchSucceeded();
        Near(f.Runtime.GetNormalDamageMultiplier(f.Form), 1.5f, "Timed activation");
        Near(f.Stats.MaxHp, 1500f, "Timed resource activation");
        f.Runtime.Tick(1f);
        f.Runtime.NotifyFormSwitchSucceeded();
        f.Runtime.Tick(1.5f);
        Near(f.Runtime.GetNormalDamageMultiplier(f.Form), 1.5f, "Refresh must reset duration without stacking.");
        f.ChangeForm(ElementType.ELEMENT_WATER);
        Near(f.Runtime.GetNormalDamageMultiplier(f.Form), 1.1f, "Contributions must follow the current form.");
        Near(f.Stats.MaxHp, 1000f, "Resource form filter");
        f.Runtime.Tick(0.6f); // Timers continue while their form is inactive.
        f.ChangeForm(ElementType.ELEMENT_FIRE);
        Near(f.Runtime.GetNormalDamageMultiplier(f.Form), 1.1f, "Inactive-form timer expiry");
        Near(f.Stats.MaxHp, 1000f, "Expired resource effect");
    }

    private static void IndependentSourcesAndReacquisition()
    {
        using var f = new Fixture();
        var first = f.Item("first", Timed(ItemStat.SwitchDamage, 0.2f, 2f));
        var second = f.Item("second", Timed(ItemStat.SwitchDamage, 0.3f, 4f));
        f.Catalog.items = new[] { first, second };
        Check(f.Runtime.TryAddItem(first.itemId) && f.Runtime.TryAddItem(second.itemId), "Independent grants");
        f.Runtime.NotifyFormSwitchSucceeded();
        Near(f.Runtime.GetSwitchDamageMultiplier(f.Form), 1.5f, "Independent timed totals");
        f.Runtime.Tick(2.1f);
        Near(f.Runtime.GetSwitchDamageMultiplier(f.Form), 1.3f, "Independent durations");
        Check(f.Runtime.TryRemoveItem(second.itemId), "Remove active source");
        Near(f.Runtime.GetSwitchDamageMultiplier(f.Form), 1f, "Removal cancels only that source.");
        Check(f.Runtime.TryAddItem(second.itemId), "Reacquisition");
        Near(f.Runtime.GetSwitchDamageMultiplier(f.Form), 1f, "Reacquisition must not restore an old timer.");
        f.Runtime.NotifyFormSwitchSucceeded();
        Near(f.Runtime.GetSwitchDamageMultiplier(f.Form), 1.5f, "A new success refreshes each owned source.");
    }

    private static void AtomicHealthAndLegacyModifiers()
    {
        using var f = new Fixture();
        f.Stats.AddModifier(StatType.ST_ATK, 25f, 0.1f);
        f.Stats.AddModifier(StatType.ST_HP, 25f, 0f);
        var item = f.Item("health", Effect(ItemStat.MaxHp, 1f));
        f.Catalog.items = new[] { item };
        int batches = 0, hpEvents = 0;
        f.Stats.OnStatsChanged += () => batches++;
        f.Stats.OnHpChanged += (_, _) => hpEvents++;
        Check(f.Runtime.TryAddItem(item.itemId), "Health item grant");
        Check(batches == 1 && hpEvents == 1, "An item replacement must publish one final HP change.");
        Near(f.Stats.MaxHp, 2050f, "Item and existing HP modifiers compose.");
        Near(f.Stats.CurrentHp, 1000f, "Increasing max HP must not refill.");
        f.Stats.Heal(2050f);
        Near(f.Stats.CurrentHp, 2050f, "Full health setup");
        Check(f.Runtime.TryRemoveItem(item.itemId), "Health item removal");
        Near(f.Stats.MaxHp, 1025f, "Existing HP modifier survives removal.");
        Near(f.Stats.CurrentHp, 1025f, "Decreasing max HP clamps current HP.");
        Near(f.Stats.GetStat(StatType.ST_ATK), 137.5f, "Legacy form modifiers survive item replacement.");
    }

    private static void FormChangeDoesNotTemporarilyClamp()
    {
        using var f = new Fixture();
        var water = f.Item("water-max", Scoped(ItemStat.MaxHp, 1f, ElementType.ELEMENT_WATER));
        var fire = f.Item("fire-max", Scoped(ItemStat.MaxHp, 1f, ElementType.ELEMENT_FIRE));
        f.Catalog.items = new[] { water, fire };
        Check(f.Runtime.TryAddItem(water.itemId) && f.Runtime.TryAddItem(fire.itemId), "Scoped grants");
        f.Stats.Heal(1000f);
        int changes = 0;
        f.Stats.OnStatsChanged += () => changes++;
        f.ChangeForm(ElementType.ELEMENT_FIRE);
        Near(f.Stats.MaxHp, 2000f, "Destination max HP");
        Near(f.Stats.CurrentHp, 2000f, "Form replacement must not clamp through an intermediate max.");
        Check(changes == 1, "Form replacement publishes once.");
    }

    private static void DisableAndDeathCleanup()
    {
        using var f = new Fixture();
        var item = f.Item("cleanup", Effect(ItemStat.MaxHp, 0.25f), Timed(ItemStat.NormalDamage, 0.5f, 5f));
        f.Catalog.items = new[] { item };
        Check(f.Runtime.TryAddItem(item.itemId), "Cleanup source");
        f.Runtime.NotifyFormSwitchSucceeded();
        InvokeLifecycle(f.Runtime, "OnDisable");
        Near(f.Stats.MaxHp, 1000f, "Disable removes resource contribution.");
        Near(f.Runtime.GetNormalDamageMultiplier(f.Form), 1f, "Disable suspends timed effects.");
        InvokeLifecycle(f.Runtime, "OnEnable");
        Near(f.Stats.MaxHp, 1250f, "Enable reapplies while-owned effects.");
        Near(f.Runtime.GetNormalDamageMultiplier(f.Form), 1f, "Enable must not resurrect timed effects.");
        f.Runtime.NotifyFormSwitchSucceeded();
        f.Stats.TakeDamage(100000f, ElementType.ELEMENT_NONE, false);
        f.Runtime.ClearTimedEffects();
        Near(f.Runtime.GetNormalDamageMultiplier(f.Form), 1f, "Death clears timed effects.");
        f.Runtime.NotifyFormSwitchSucceeded();
        Near(f.Runtime.GetNormalDamageMultiplier(f.Form), 1f, "A dead player cannot activate effects.");
        Near(f.Stats.CurrentHp, 0f, "Effect cleanup cannot revive a player.");
    }

    private static void RetiredSerializedEffectsRejected()
    {
        using var f = new Fixture();
        Check((int)ItemStat.MaxHp == 0 && (int)ItemStat.NormalDamage == 3 && (int)ItemStat.SwitchDamage == 4,
            "Existing item effects must keep their serialized stat IDs.");
        foreach (int retired in new[] { 1, 2, 5, 6 })
        {
            var item = f.Item("retired-" + retired, Effect((ItemStat)retired, 10f));
            f.Catalog.items = new[] { item };
            Check(!f.Runtime.TryAddItem(item.itemId) && f.Runtime.OwnedItems.Count == 0,
                "An unmigrated resource effect must not be reinterpreted as a live stat.");
        }
    }

    private static void InitiallyDisabledRuntime()
    {
        using var f = new Fixture();
        var item = f.Item("initially-disabled", Effect(ItemStat.MaxHp, 0.25f),
            Effect(ItemStat.NormalDamage, 0.3f), Timed(ItemStat.SwitchDamage, 0.5f, 5f));
        f.Catalog.items = new[] { item };
        // The fixture is inactive: adding a player cannot run its gameplay Awake/Start.
        // Disabling the runtime here also cannot invoke OnDisable, matching scene startup.
        f.Runtime.enabled = false;
        var player = f.Runtime.gameObject.AddComponent<PlayerController>();
        f.Runtime.Initialize(player);
        Check(f.Runtime.TryAddItem(item.itemId), "Disabled ownership can be recorded for later enable.");
        f.Runtime.NotifyFormSwitchSucceeded();
        f.Runtime.Tick(0.25f);
        Near(f.Stats.MaxHp, 1000f, "Initially disabled runtime must not apply owned resources.");
        Near(f.Runtime.GetNormalDamageMultiplier(f.Form), 1f, "Initially disabled runtime must not apply normal damage.");
        Near(f.Runtime.GetSwitchDamageMultiplier(f.Form), 1f, "Initially disabled runtime must not activate timed damage.");
        Check(f.Runtime.Contains(item.itemId), "Suspension must preserve ownership.");
        InvokeLifecycle(f.Runtime, "OnEnable");
        Near(f.Stats.MaxHp, 1250f, "Enable reapplies an initially disabled owned resource effect.");
        Near(f.Runtime.GetSwitchDamageMultiplier(f.Form), 1f, "Initial disabled notifications cannot leave a queued timer.");
    }

    private static int ImportedCatalogs()
    {
        const string root = "Assets/_Project/Generated/BaekhoItems";
        if (!AssetDatabase.IsValidFolder(root)) return 0;
        int tested = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:ItemCatalog", new[] { root }))
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(AssetDatabase.GUIDToAssetPath(guid));
            if (catalog == null) continue;
            using var f = new Fixture();
            f.Runtime.Catalog = catalog;
            Check(f.Runtime.OwnedItems.Count == 0, "Loading imported content must not grant items.");
            foreach (var item in catalog.items)
            {
                Check(item != null && f.Runtime.TryAddItem(item.itemId), "Imported item must be grantable.");
                Check(!f.Runtime.TryAddItem(item.itemId), "Imported item must not stack ownership.");
                f.ChangeForm(item.element);
                f.Runtime.NotifyFormSwitchSucceeded();
                f.Runtime.Tick(0.25f);
                Check(f.Runtime.TryRemoveItem(item.itemId), "Imported item must be removable.");
                Near(f.Runtime.GetNormalDamageMultiplier(f.Form), 1f, "Imported normal effect removal");
                Near(f.Runtime.GetSwitchDamageMultiplier(f.Form), 1f, "Imported switch effect removal");
                tested++;
            }
        }
        return tested;
    }

    private static ItemEffect Effect(ItemStat stat, float amount)
        => new ItemEffect { stat = stat, amount = amount, allForms = true, trigger = ItemEffectTrigger.WhileOwned };
    private static ItemEffect Scoped(ItemStat stat, float amount, ElementType form)
    {
        var effect = Effect(stat, amount);
        effect.allForms = false;
        effect.targetForms = new[] { form };
        return effect;
    }
    private static ItemEffect Timed(ItemStat stat, float amount, float duration, ElementType form = ElementType.ELEMENT_NONE)
    {
        var effect = form == ElementType.ELEMENT_NONE ? Effect(stat, amount) : Scoped(stat, amount, form);
        effect.trigger = ItemEffectTrigger.FormSwitchSucceeded;
        effect.durationSeconds = duration;
        return effect;
    }
    private static void InvokeLifecycle(PlayerItemRuntime runtime, string method)
        => typeof(PlayerItemRuntime).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(runtime, null);
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Item runtime regression: " + message); }
    private static void Near(float actual, float expected, string message)
        => Check(!float.IsNaN(actual) && Mathf.Abs(actual - expected) <= 0.001f, $"{message}: {actual} != {expected}");

    private sealed class Fixture : IDisposable
    {
        private readonly List<Object> _owned = new();
        private readonly GameObject _root;
        public readonly PlayerStatManager Stats;
        public readonly PlayerItemRuntime Runtime;
        public readonly ItemCatalog Catalog;
        public ElementType Form = ElementType.ELEMENT_WATER;

        public Fixture()
        {
            _root = new GameObject("Baekho item regression") { hideFlags = HideFlags.HideAndDontSave };
            _root.SetActive(false);
            var data = ScriptableObject.CreateInstance<PlayerStatData>();
            _owned.Add(data);
            data.baseHP = 1000f;
            data.baseATK = 100f;
            Stats = _root.AddComponent<PlayerStatManager>();
            Stats.Initialize(data);
            Catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            _owned.Add(Catalog);
            Catalog.projectId = "item-runtime-regression";
            Runtime = _root.AddComponent<PlayerItemRuntime>();
            Runtime.Catalog = Catalog;
            Runtime.Initialize(Stats, () => Form);
        }

        public ItemDefinition Item(string id, params ItemEffect[] effects)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            _owned.Add(item);
            item.projectId = Catalog.projectId;
            item.candidateKey = "candidate-" + id;
            item.itemId = id;
            item.effects = effects;
            return item;
        }

        public void ChangeForm(ElementType form) { Form = form; Runtime.RecalculateForCurrentForm(); }
        public void Dispose()
        {
            Object.DestroyImmediate(_root);
            foreach (var value in _owned) Object.DestroyImmediate(value);
        }
    }
}
