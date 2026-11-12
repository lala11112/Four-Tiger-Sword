using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A fixed, non-parryable phase-entry explosion with an actor-clock warning and recovery.</summary>
[CreateAssetMenu(fileName = "BossPhaseBurst", menuName = "SainGum/Monster/Boss Phase Burst")]
public sealed class BossPhaseBurstSkillSO : MonsterSkillData
{
    [Min(.01f)] public float radius = 12f;
    [Min(.01f)] public float heightTolerance = 4f;
    [Min(.01f), Tooltip("Total warning time, including preparation, held pose and release, before damage.")]
    public float chargeDuration = 2.5f;
    [Min(.01f)] public float activeDuration = .15f;
    [Min(0f)] public float recoveryTime = 1.2f;
    [Min(0f)] public float damage = 2f;
    [Min(0f)] public float knockbackForce = 38f;
    [Min(0f), Tooltip("Camera impulse strength at the burst, even when dodged. Zero disables it; the player's impulse source supplies duration and direction.")]
    public float burstCameraShakeStrength = .45f;
    [Min(0f)] public float preparationDuration = .53333336f;
    [Range(0f, 1f)] public float chargePoseNormalized = .19512194f;
    [Min(0f)] public float releaseHitDelay = .93333334f;

    public override bool SupportsBossExecutionPolicy => true;

    public IEnumerable<string> Validate()
    {
        if (!BossCombatValidation.Positive(radius) || !BossCombatValidation.Positive(heightTolerance))
            yield return "Burst radius and height tolerance must be finite and positive.";
        if (!BossCombatValidation.Positive(chargeDuration) || !BossCombatValidation.Positive(activeDuration)
            || !BossCombatValidation.Nonnegative(recoveryTime) || !BossCombatValidation.Positive(attackSpeed))
            yield return "Burst warning/active timing and speed must be finite and positive; recovery must be nonnegative.";
        if (!BossCombatValidation.Nonnegative(preparationDuration) || !BossCombatValidation.Nonnegative(releaseHitDelay)
            || preparationDuration + releaseHitDelay > chargeDuration || !BossCombatValidation.Unit(chargePoseNormalized))
            yield return "Burst preparation and release must fit inside the total warning duration.";
        if (!BossCombatValidation.Nonnegative(damage) || !BossCombatValidation.Nonnegative(knockbackForce))
            yield return "Burst damage and knockback must be finite and nonnegative.";
        if (!BossCombatValidation.Nonnegative(burstCameraShakeStrength))
            yield return "Burst camera shake strength must be finite and nonnegative.";
        if (isParryable) yield return "The phase burst must be non-parryable; its floor warning is independent of parry events.";
    }

    public override EnemyAction CreateAction(Enemy enemy)
    {
        if (!(enemy is Boss)) throw new ArgumentException("The phase burst requires a boss owner.", nameof(enemy));
        foreach (string issue in Validate()) throw new InvalidOperationException(issue);
        return new BossPhaseBurstAction(this, enemy);
    }
}
