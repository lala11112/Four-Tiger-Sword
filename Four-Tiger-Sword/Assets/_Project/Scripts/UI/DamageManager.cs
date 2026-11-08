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
        var sourcePlayer = info.Source as PlayerController;
        var targetEnemy = targetGO.GetComponent<Enemy>();
        var targetStats = targetGO.GetComponent<EnemyStat>();
        var family = ItemDamageContext.Family(info.Category);
        bool canTrigger = info.Origin == DamageOrigin.AuthoredAction && family != BoundaryAttackFamily.Any;
        if (!canTrigger) family = BoundaryAttackFamily.Any;
        var hitContext = new ItemHitContext(targetGO.GetInstanceID(), targetStats != null ? targetStats.CurrentHp : 0f,
            targetStats != null ? targetStats.MaxHp : 0f, targetEnemy != null && targetEnemy.Rank != EnemyRank.Normal,
            family, info.Element, canTrigger, info.CastId);
        var itemRuntime = sourcePlayer != null && targetEnemy != null ? sourcePlayer.ItemRuntime : null;
        var itemModifiers = itemRuntime != null ? itemRuntime.EvaluateOutgoing(hitContext)
            : new ItemOutgoingModifiers(1f, 0f, 0f, 0f);
        var engravings = sourcePlayer != null && targetEnemy != null ? sourcePlayer.Engravings : null;
        damage *= Mathf.Max(0f, itemModifiers.DamageMultiplier
            + (engravings != null ? engravings.OutgoingDamageBonus(info, targetEnemy) : 0f));
        if (info.Category == PlayerAttackCategory.Normal || info.Category == PlayerAttackCategory.SwitchAttack)
            damage *= Mathf.Max(0f, info.ItemDamageMultiplier);
        if (info.HasSourceDamageSnapshot)
            damage *= Mathf.Max(0f, info.SourceDamageMultiplier);
        else if (info.Source is PlayerController owner && owner.FormManager?.CurrentForm is BaseForm form)
        {
            damage *= form.OutgoingDamageMultiplier;
            if (info.Element != ElementType.ELEMENT_NONE)
                damage *= 1f + Mathf.Max(0f, owner.StatManager.GetStat(StatType.ST_ELM_ATK)) / 100f;
        }
        var vulnerability = targetGO.GetComponent<StatusEffectHandler>()?.Get<VulnerabilityEffect>();
        if (vulnerability != null && !vulnerability.IsExpired) damage *= vulnerability.DamageMultiplier;
        var waterChainVulnerability = targetGO.GetComponent<StatusEffectHandler>()?.Get<WaterChainVulnerabilityEffect>();
        if (waterChainVulnerability != null && !waterChainVulnerability.IsExpired)
            damage *= waterChainVulnerability.DamageMultiplier;
        var boss = targetGO.GetComponent<Boss>();
        if (boss != null && boss.IsGroggy && boss.Data != null)
            damage *= boss.Data.groggyDamageMult;
        bool critical = false;
        if (!info.TrueDamage)
        {
            if (!CombatSettings.TryGetTable(out var elementTable)) return default;
            var enemy = targetGO.GetComponent<EnemyStat>();
            var player = targetGO.GetComponent<PlayerController>();
            ElementType defenseElement = enemy != null ? enemy.ElementType
                : player != null ? player.Element : ElementType.ELEMENT_NONE;
            if (!elementTable.TryGetElementEffective(info.Element, defenseElement, out float multiplier)) return default;
            if (boss != null && boss.Data != null)
                multiplier = boss.Data.ResolveElementMultiplier(info.Element, multiplier);
            damage *= multiplier;
            critical = Random.value < Mathf.Clamp01(info.CriticalChance + itemModifiers.CriticalChanceBonus
                + (engravings != null ? engravings.OutgoingCriticalBonus(info) : 0f));
            if (critical) damage *= Mathf.Max(0f, info.CriticalMultiplier + itemModifiers.CriticalMultiplierBonus);
            if (!info.ArmorPierce)
            {
                float defense = enemy != null
                    ? (targetGO.GetComponent<FormTargetEffects>()?.EffectiveDefense ?? enemy.GetStat(EnemyStatType.DEF))
                    : player != null ? player.StatManager.GetStat(StatType.ST_DEF) : 0f;
                damage *= 100f / (100f + Mathf.Max(0f, defense) * (1f - itemModifiers.IgnoreDefenseRatio));
            }
        }
        if (float.IsNaN(damage) || float.IsInfinity(damage)) return default;
        damage = Mathf.Max(0f, damage);
        Vector3 impactPosition = targetGO.transform.position;
        DamageResult result;
        using (ItemDamageContext.Push(new ItemDeathContext(sourcePlayer, family, canTrigger,
            critical, info.IsDirectPlayerHit, info.Origin), itemRuntime, hitContext,
            () => engravings?.CommitOutgoing(info, targetEnemy, critical)))
        {
            result = target.TakeDamage(damage, info.Element, critical, info.Power,
                Mathf.Max(0f, info.PoiseDamage * info.PoiseDamageMultiplier),
                info.StaggerResistLevel, info.IsParryable, info.Source);
            if (result.Applied) ItemDamageContext.CommitOutgoing(result.HealthDamage, result.ShieldDamage);
        }
        if (result.Applied && result.CausedOrdinaryStagger && info.IsDirectPlayerHit
            && info.Category != PlayerAttackCategory.Secondary
            && info.Source is PlayerController && target is Enemy staggeredEnemy)
            staggeredEnemy.RecordDirectPlayerStagger();
        if (result.Applied && result.HealthDamage + result.ShieldDamage > 0f)
            CombatSfx.Play(info.ImpactSound, impactPosition, info.Source, impact: true);
        return result;
    }
}
