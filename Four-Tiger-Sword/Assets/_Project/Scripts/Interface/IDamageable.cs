using UnityEngine;

public interface IDamageable
{
    void TakeDamage(int damage, ElementType damageType = ElementType.ELEMENT_NONE, bool isCritical = false, Vector3 power = default, float poiseDamage = 20f);
}   