using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A combat snapshot. Visibility is an obstacle test and never applies a field of view.</summary>
public readonly struct BossCombatContext
{
    private static readonly RaycastHit[] VisibilityHits = new RaycastHit[64];
    public readonly Boss Boss;
    public readonly Transform Target;
    public readonly float Distance;
    public readonly float Angle;
    public readonly bool Visible;
    public readonly float HpPercent;

    public BossCombatContext(Boss boss, Transform target, float distance, float angle, bool visible, float hpPercent)
    {
        Boss = boss;
        Target = target;
        Distance = distance;
        Angle = angle;
        Visible = visible;
        HpPercent = hpPercent;
    }

    public static BossCombatContext Capture(Boss boss)
    {
        if (boss == null) return default;
        Transform target = boss.CombatTarget;
        if (target == null || !target.gameObject.activeInHierarchy)
            return new BossCombatContext(boss, null, float.PositiveInfinity, 180f, false, boss.HpPercent);
        Vector3 offset = target.position - boss.transform.position;
        Vector3 flat = Vector3.ProjectOnPlane(offset, Vector3.up);
        float angle = flat.sqrMagnitude > .0001f ? Vector3.Angle(boss.transform.forward, flat) : 0f;
        Vector3 origin = boss.transform.position + Vector3.up * .5f;
        Vector3 ray = target.position + Vector3.up * .5f - origin;
        bool visible = true;
        if (ray.sqrMagnitude > .0001f)
        {
            int count = Physics.RaycastNonAlloc(origin, ray.normalized, VisibilityHits,
                ray.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            visible = count < VisibilityHits.Length;
            for (int i = 0; visible && i < count; i++)
            {
                Transform hit = VisibilityHits[i].transform;
                if (hit == null || hit.IsChildOf(boss.transform) || hit.IsChildOf(target)) continue;
                visible = false;
            }
        }
        return new BossCombatContext(boss, target, offset.magnitude, angle, visible, boss.HpPercent);
    }
}

public enum BossVisibilityRequirement { Any = 0, Visible = 1, Occluded = 2 }

[Serializable]
public sealed class BossCombatCondition
{
    [Min(0f)] public float minimumDistance;
    [Min(0f)] public float maximumDistance = 100f;
    [Range(0f, 180f)] public float minimumAngle;
    [Range(0f, 180f)] public float maximumAngle = 180f;
    [Range(0f, 1f)] public float minimumHpPercent;
    [Range(0f, 1f)] public float maximumHpPercent = 1f;
    [Tooltip("시야각과 무관하게 보스와 대상 사이의 장애물만 확인합니다.")]
    public BossVisibilityRequirement visibility;
    [Tooltip("설정하면 해당 공격의 개별 쿨타임이 끝났을 때만 조건을 만족합니다.")]
    public MonsterSkillData readySkill;

    public bool Matches(BossCombatContext context)
        => context.Target != null && context.Target.gameObject.activeInHierarchy
            && BossCombatValidation.Range(minimumDistance, maximumDistance)
            && BossCombatValidation.Range(minimumAngle, maximumAngle) && maximumAngle <= 180f
            && BossCombatValidation.Unit(minimumHpPercent) && BossCombatValidation.Unit(maximumHpPercent)
            && minimumHpPercent <= maximumHpPercent
            && context.Distance >= minimumDistance && context.Distance <= maximumDistance
            && context.Angle >= minimumAngle && context.Angle <= maximumAngle
            && context.HpPercent >= minimumHpPercent && context.HpPercent <= maximumHpPercent
            && Enum.IsDefined(typeof(BossVisibilityRequirement), visibility)
            && (visibility == BossVisibilityRequirement.Any
                || context.Visible == (visibility == BossVisibilityRequirement.Visible))
            && (readySkill == null || (context.Boss != null && context.Boss.IsSkillReady(readySkill)));

    public IEnumerable<string> Validate()
    {
        if (!Enum.IsDefined(typeof(BossVisibilityRequirement), visibility)) yield return "Condition visibility is invalid.";
        if (!BossCombatValidation.Range(minimumDistance, maximumDistance)) yield return "Condition distance must be a finite, nonnegative ordered range.";
        if (!BossCombatValidation.Range(minimumAngle, maximumAngle) || maximumAngle > 180f) yield return "Condition angle must be an ordered range between 0 and 180 degrees.";
        if (!BossCombatValidation.Unit(minimumHpPercent) || !BossCombatValidation.Unit(maximumHpPercent)
            || minimumHpPercent > maximumHpPercent) yield return "Condition HP must be an ordered range between 0 and 1.";
    }
}
