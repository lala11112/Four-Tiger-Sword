using UnityEngine;

public enum DamageType
{
    Normal,
    Fire,
    Water,
    Wood,
    Earth,
    Iron
}

public interface IDamageable
{
    void TakeDamage(int damage, DamageType damageType = DamageType.Normal, bool isCritical = false, Vector3 power = default, float poiseDamage = 20f);
}   