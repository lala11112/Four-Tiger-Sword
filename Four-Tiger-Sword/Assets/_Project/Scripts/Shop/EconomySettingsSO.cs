using UnityEngine;

/// <summary>Run economy values, kept outside shop presentation and imported item content.</summary>
[CreateAssetMenu(fileName = "EconomySettings", menuName = "Four Tiger Sword/Shop/Economy Settings")]
public sealed class EconomySettingsSO : ScriptableObject
{
    [Min(0)] public int startingGold = 0;
    [Min(0)] public int ordinaryKillGold = 10;
    [Min(0)] public int bossKillGold = 50;
    [Min(1)] public int baseItemCount = 3;
    [Min(0)] public int healingPrice = 20;
    [Range(0.01f, 1f)] public float healingFraction = 0.3f;
    [Min(0.1f)] public float interactionDistance = 2.5f;

    public bool Validate(out string error)
    {
        if (startingGold < 0 || ordinaryKillGold < 0 || bossKillGold < 0 || healingPrice < 0)
            error = "Gold values and shop prices cannot be negative.";
        else if (baseItemCount < 1)
            error = "The shop's base item count must be positive.";
        else if (!Finite(healingFraction) || healingFraction <= 0f || healingFraction > 1f)
            error = "The shop healing fraction must be greater than zero and at most one.";
        else if (!Finite(interactionDistance) || interactionDistance <= 0f)
            error = "The shop interaction distance must be positive and finite.";
        else { error = null; return true; }
        return false;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
