using System;
using System.Collections.Generic;
using UnityEngine;

public enum BossAttackStage { None, Startup, Active, Recovery }

/// <summary>Opt-in execution rules copied into one action; shared attack assets are never changed.</summary>
[Serializable]
public sealed class BossAttackPolicy
{
    [Tooltip("Enable boss-specific aiming and armor for this attack step.")]
    public bool enabled;
    [Min(0f), Tooltip("Maximum turn speed in degrees per attack-timeline second during early startup.")]
    public float turnSpeed = 180f;
    [Range(0f, 1f), Tooltip("Aim locks at this fraction of startup. A combo starts a new startup after each hit window.")]
    public float lockFraction = .7f;
    public bool armorDuringStartup;
    public bool armorDuringActive;

    public BossAttackPolicy Copy() => new BossAttackPolicy
    {
        enabled = enabled,
        turnSpeed = turnSpeed,
        lockFraction = lockFraction,
        armorDuringStartup = armorDuringStartup,
        armorDuringActive = armorDuringActive
    };

    public IEnumerable<string> Validate(MonsterSkillData skill)
    {
        if (!enabled) yield break;
        if (!Finite(turnSpeed) || turnSpeed < 0f) yield return "Boss attack turnSpeed must be finite and nonnegative.";
        if (!Finite(lockFraction) || lockFraction < 0f || lockFraction > 1f) yield return "Boss attack lockFraction must be between zero and one.";
        if (skill == null) { yield return "Boss attack policy requires a skill."; yield break; }
        if (!skill.SupportsBossExecutionPolicy)
            yield return "This skill does not declare support for boss execution policies.";
        foreach (string issue in ValidateSkill(skill)) yield return issue;
    }

    /// <summary>Built-in execution safety checks, independent of an optional boss policy.</summary>
    public static IEnumerable<string> ValidateSkill(MonsterSkillData skill)
    {
        if (skill == null) { yield return "Boss attack requires a skill."; yield break; }
        if (!Finite(skill.attackSpeed) || skill.attackSpeed <= 0f) yield return "Boss attack attackSpeed must be finite and positive.";

        if (skill is MonsterDefaultAttackSO)
        {
            var attack = (MonsterDefaultAttackSO)skill;
            if (!ValidWindow(attack.hitStartTime, attack.hitDuration, attack.attackDuration))
                yield return "Boss default attack requires a finite, positive hit window inside attackDuration.";
            if (!Nonnegative(attack.animationStartDelay) || attack.animationStartDelay > attack.hitStartTime)
                yield return "Boss default animationStartDelay must be finite, nonnegative and no later than hitStartTime.";
        }
        else if (skill is MonsterComboAttackSO)
        {
            var attack = (MonsterComboAttackSO)skill;
            if (!Finite(attack.attackDuration) || attack.attackDuration <= 0f)
                yield return "Boss combo attackDuration must be finite and positive.";
            if (!Nonnegative(attack.stepStopDistance))
                yield return "Boss combo stepStopDistance must be finite and nonnegative.";
            if (attack.hits == null || attack.hits.Count == 0)
                yield return "Boss combo requires at least one hit.";
            else
            {
                float previousEnd = 0f;
                foreach (var hit in attack.hits)
                {
                    if (hit == null || !ValidWindow(hit.hitStartTime, hit.hitDuration, attack.attackDuration)
                        || hit.hitStartTime < previousEnd)
                        yield return "Boss combo hits must be ordered, nonoverlapping, finite hit windows inside attackDuration.";
                    if (hit != null) previousEnd = hit.hitStartTime + hit.hitDuration;
                    if (hit != null && (!Nonnegative(hit.animationStartTime) || hit.animationStartTime > hit.hitStartTime))
                        yield return "Boss combo swing animations must start at a finite time before their hit.";
                    if (hit != null && (!Nonnegative(hit.stepDistance) || !Nonnegative(hit.stepStartTime)
                        || !Nonnegative(hit.stepDuration)))
                        yield return "Boss combo step distance and timing must be finite and nonnegative.";
                    if (hit != null && hit.stepDistance > 0f && (!Positive(hit.stepDuration)
                        || hit.stepStartTime < hit.animationStartTime
                        || hit.stepStartTime + hit.stepDuration > hit.hitStartTime + hit.hitDuration))
                        yield return "Boss combo steps must start within their swing and end by the hit window's end.";
                    if (hit != null && hit.overrideReaction && !Nonnegative(hit.knockbackForce))
                        yield return "Boss combo per-hit knockbackForce must be finite and nonnegative.";
                    if (hit != null && hit.knockdown && (!Nonnegative(hit.downSeconds) || !Nonnegative(hit.getUpSeconds)
                        || !Positive(hit.downSeconds + hit.getUpSeconds)))
                        yield return "Boss combo knockdown requires finite nonnegative down/get-up times with a positive total.";
                }
            }
        }
        else if (skill is MonsterBossChargeAttackSO)
        {
            var attack = (MonsterBossChargeAttackSO)skill;
            if (!Nonnegative(attack.chargePoseTime) || !Nonnegative(attack.chargeSeconds)
                || !Nonnegative(attack.releaseHitDelay) || !Positive(attack.hitDuration) || !Nonnegative(attack.recoveryTime))
                yield return "Boss charge requires finite nonnegative preparation, charge, release/recovery times and a positive hitDuration.";
            if (!Finite(attack.chargePoseNormalized) || attack.chargePoseNormalized < 0f || attack.chargePoseNormalized > 1f)
                yield return "Boss charge chargePoseNormalized must be between zero and one.";
            if (!Nonnegative(attack.knockbackForce) || !Nonnegative(attack.damage))
                yield return "Boss charge damage and knockbackForce must be finite and nonnegative.";
            if (!Positive(attack.hitBoxRadius) || !Positive(attack.hitBoxHeight)
                || !Positive(attack.hitBoxSize.x) || !Positive(attack.hitBoxSize.y) || !Positive(attack.hitBoxSize.z))
                yield return "Boss charge hitbox dimensions must be finite and positive.";
        }
        else if (skill is MonsterDashAttackSO)
        {
            var attack = (MonsterDashAttackSO)skill;
            if (!Nonnegative(attack.startupTime) || !Positive(attack.dashDuration) || !Nonnegative(attack.recoveryTime))
                yield return "Boss dash requires finite startup/recovery times and a positive dashDuration.";
            if (!Nonnegative(attack.dashSpeed)) yield return "Boss dashSpeed must be finite and nonnegative.";
        }
        else if (skill is MonsterJumpSlamSO)
        {
            var attack = (MonsterJumpSlamSO)skill;
            if (!Nonnegative(attack.startupTime) || !Positive(attack.jumpDuration) || !Nonnegative(attack.recoveryTime))
                yield return "Boss jump requires finite startup/recovery times and a positive jumpDuration.";
            if (!Nonnegative(attack.jumpHeight)) yield return "Boss jumpHeight must be finite and nonnegative.";
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Nonnegative(float value) => Finite(value) && value >= 0f;
    private static bool Positive(float value) => Finite(value) && value > 0f;
    private static bool ValidWindow(float start, float duration, float total)
        => Nonnegative(start) && Positive(duration) && Positive(total)
            && Finite(start + duration) && start + duration <= total;
}
