using UnityEngine;

public static class DamageManager
{

    public static DamageResult Apply(HitInfo info, IDamageable target, GameObject targetGO)
    {
        if (target == null || targetGO == null || !targetGO.activeInHierarchy
            || float.IsNaN(info.BaseDamage) || float.IsInfinity(info.BaseDamage) || info.BaseDamage < 0f)
            return default;
        if (target is Component receiver)
        {
            if (receiver == null) return default;
            targetGO = receiver.gameObject;
            if (!targetGO.activeInHierarchy) return default;
        }
        float damage = info.BaseDamage;
        bool critical = false;
        if (!info.TrueDamage)
        {
            if (!CombatSettings.TryGetTable(out var elementTable)) return default;
            var enemy = targetGO.GetComponent<EnemyStat>();
            var player = targetGO.GetComponent<PlayerController>();
            ElementType defenseElement = enemy != null ? enemy.ElementType
                : player != null ? player.Element : ElementType.ELEMENT_NONE;
            if (!elementTable.TryGetElementEffective(info.Element, defenseElement, out float multiplier)) return default;
            damage *= multiplier;
            critical = Random.value < Mathf.Clamp01(info.CriticalChance);
            if (critical) damage *= Mathf.Max(0f, info.CriticalMultiplier);
            if (!info.ArmorPierce)
            {
                float defense = enemy != null
                    ? (targetGO.GetComponent<FormTargetEffects>()?.EffectiveDefense ?? enemy.GetStat(EnemyStatType.DEF))
                    : player != null ? player.StatManager.GetStat(StatType.ST_DEF) : 0f;
                damage *= 100f / (100f + Mathf.Max(0f, defense));
            }
        }
        if (float.IsNaN(damage) || float.IsInfinity(damage)) return default;
        return target.TakeDamage(damage, info.Element, critical, info.Power,
            Mathf.Max(0f, info.PoiseDamage * info.PoiseDamageMultiplier),
            info.StaggerResistLevel, info.IsParryable, info.Source);
    }
}
