using System;
using System.Collections.Generic;
using UnityEngine;

public enum BossMovementMode { Approach, MaintainDistance, Retreat, StrafeLeft, StrafeRight }

[Serializable]
public sealed class BossMovementSettings
{
    public BossMovementMode mode;
    [Min(0f)] public float minDistance = 2f;
    [Min(0f)] public float maxDistance = 4f;
    [Min(0f)] public float duration = 1f;
    [Min(.01f)] public float timeout = 6f;

    public IEnumerable<string> Validate()
    {
        if (!Enum.IsDefined(typeof(BossMovementMode), mode)) yield return "Movement mode is invalid.";
        if (!BossCombatValidation.Range(minDistance, maxDistance)) yield return "Movement distance must be a finite, nonnegative ordered range.";
        if (!BossCombatValidation.Nonnegative(duration)) yield return "Movement duration must be finite and nonnegative.";
        if (!BossCombatValidation.Positive(timeout)) yield return "Movement timeout must be finite and positive.";
    }
}
