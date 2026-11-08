using System;
using UnityEngine;

public enum ItemStat
{
    MaxHp,
    MaxSp,
    SpRegen,
    NormalDamage,
    SwitchDamage,
    DodgeSpCost,
    SwitchSpCost
}

public enum ItemEffectTrigger
{
    WhileOwned,
    FormSwitchSucceeded
}

[Serializable]
public sealed class ItemEffect
{
    public ItemStat stat;
    public float amount;
    public ItemEffectTrigger trigger;
    public bool allForms = true;
    public ElementType[] targetForms = Array.Empty<ElementType>();
    public float durationSeconds;

    public bool AppliesTo(ElementType form)
    {
        if (allForms) return true;
        if (targetForms == null) return false;
        foreach (var target in targetForms)
            if (target == form) return true;
        return false;
    }

    internal ItemEffect Copy()
        => new ItemEffect
        {
            stat = stat,
            amount = amount,
            trigger = trigger,
            allForms = allForms,
            targetForms = targetForms != null ? (ElementType[])targetForms.Clone() : Array.Empty<ElementType>(),
            durationSeconds = durationSeconds
        };

    internal bool IsValid()
    {
        if (!Enum.IsDefined(typeof(ItemStat), stat) || !Enum.IsDefined(typeof(ItemEffectTrigger), trigger)
            || !IsFinite(amount) || !IsFinite(durationSeconds)) return false;
        if (trigger == ItemEffectTrigger.WhileOwned ? durationSeconds != 0f : durationSeconds <= 0f) return false;
        if (allForms) return true;
        if (targetForms == null || targetForms.Length == 0) return false;
        foreach (var form in targetForms)
            if (form == ElementType.ELEMENT_NONE || !Enum.IsDefined(typeof(ElementType), form)) return false;
        return true;
    }

    internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>Imported, immutable-at-runtime item content. Ownership lives on PlayerItemRuntime.</summary>
[CreateAssetMenu(fileName = "Item", menuName = "Four Tiger Sword/Items/Item Definition")]
public sealed class ItemDefinition : ScriptableObject
{
    public string projectId;
    public string candidateKey;
    public string itemId;
    public string displayName;
    [TextArea] public string description;
    [TextArea] public string effectText;
    public string[] synergyIds = Array.Empty<string>();
    public string[] synergyNames = Array.Empty<string>();
    public string primarySynergyId;
    public string primarySynergyName;
    public Sprite primarySynergyIcon;
    public ElementType element;
    [Range(1, 4)] public int grade = 1;
    public int purchasePrice;
    public ItemEffect[] effects = Array.Empty<ItemEffect>();
    public BoundaryItemEffect[] boundaryEffects = Array.Empty<BoundaryItemEffect>();
    public Sprite icon;
    public string contentStatus;
    public int contentRevision;
    [HideInInspector, TextArea] public string sourceJson;
}
