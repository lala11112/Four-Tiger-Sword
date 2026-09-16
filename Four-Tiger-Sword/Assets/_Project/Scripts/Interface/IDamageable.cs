using UnityEngine;

public interface IDamageable
{
    DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE, bool isCritical = false, Vector3 power = default, float poiseDamage = 20f, StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null);
}

public enum DamageOutcome { Rejected, Applied, Blocked, Parried }

public readonly struct DamageResult
{
    public readonly DamageOutcome Outcome;
    public readonly float HealthDamage;
    public readonly float ShieldDamage;
    public readonly bool Killed;
    public bool Applied => Outcome == DamageOutcome.Applied;
    public DamageResult(DamageOutcome outcome, float healthDamage = 0f, float shieldDamage = 0f, bool killed = false)
    {
        Outcome = outcome;
        HealthDamage = healthDamage;
        ShieldDamage = shieldDamage;
        Killed = killed;
    }
}
