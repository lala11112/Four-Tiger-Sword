using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A boss's complete death reward; replaces the stage's default boss gold.</summary>
[CreateAssetMenu(fileName = "BossRewardGroup", menuName = "SainGum/Monster/Boss Reward Group")]
public sealed class BossRewardGroupSO : ScriptableObject
{
    [Min(0)] public int gold = 50;
    public List<ItemDefinition> items = new List<ItemDefinition>();

    public bool Validate(out string error)
    {
        if (gold < 0) { error = "Boss reward gold cannot be negative."; return false; }
        if (items == null) { error = "Boss reward items must be a list."; return false; }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId)
                || string.IsNullOrWhiteSpace(item.candidateKey) || string.IsNullOrWhiteSpace(item.projectId))
            { error = "Boss rewards require valid item definitions."; return false; }
            if (!ids.Add(item.itemId) || !keys.Add(item.candidateKey))
            { error = "Boss reward items must have distinct IDs and content identities."; return false; }
        }
        error = null;
        return true;
    }
}
