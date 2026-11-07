using UnityEngine;

// Keep serialized skill IDs stable after removing the former quick-approach skill (2).
public enum BossMobilityKind { EscapeJump = 0, DashSlash = 1, JumpSlam = 3 }

/// <summary>Movement and presentation data for one opt-in boss mobility action.</summary>
[CreateAssetMenu(fileName = "BossMobility", menuName = "SainGum/Monster/Boss Mobility")]
public sealed class BossMobilitySkillSO : MonsterSkillData
{
    public BossMobilityKind kind;
    [Min(0f)] public float startupTime = .15f;
    [Min(.01f)] public float moveSpeed = 10f;
    [Min(.1f)] public float maxDistance = 6f;
    [Min(0f)] public float stopDistance = 2f;
    [Min(0f)] public float jumpHeight = 1.2f;
    [Min(.05f)] public float jumpDuration = .6f;
    [Min(0f)] public float recoveryTime = .2f;
    [Min(0f)] public float damage = 1f;
    [Min(0f)] public float knockbackForce = 4f;
    [Min(.01f)] public float slamRadius = 3.2f;
    [Min(.01f)] public float hitBoxRadius = 1f;
    public Vector3 hitBoxOffset = new Vector3(0f, 1f, 1f);
    public string startupAnimName;
    public string liftAnimName;
    public string landingAnimName;

    public override bool SupportsBossExecutionPolicy => true;
    public bool DealsDamage => kind == BossMobilityKind.DashSlash || kind == BossMobilityKind.JumpSlam;
    public bool IsJump => kind == BossMobilityKind.EscapeJump || kind == BossMobilityKind.JumpSlam;

    /// <summary>Read-only feasibility probe; cooldowns, actions, movement and animation are untouched.</summary>
    public bool TryCreateAction(Boss boss, out BossMobilityAction action)
    {
        action = null;
        if (boss == null || !boss.isActiveAndEnabled || boss.IsDead || boss.IsTransitioning
            || boss.IsHurt || boss.IsGroggy || boss.PendingGroggy || boss.IsRoot
            || boss.IsKnockbacking || boss.IsSilenced || !Valid()
            || !BossMobilityNavigation.TryPlan(boss, this, out var plan)) return false;
        action = new BossMobilityAction(this, boss, plan);
        action.ConfigureBossPolicy(new BossAttackPolicy
        {
            enabled = true, lockFraction = 0f, turnSpeed = 0f,
            armorDuringStartup = false, armorDuringActive = false
        });
        return true;
    }

    public override EnemyAction CreateAction(Enemy enemy)
        => enemy is Boss boss && TryCreateAction(boss, out var action) ? action : null;

    private bool Valid()
    {
        if (!System.Enum.IsDefined(typeof(BossMobilityKind), kind) || !Positive(attackSpeed)
            || !Nonnegative(startupTime) || !Positive(moveSpeed) || !Positive(maxDistance)
            || !Nonnegative(stopDistance) || !Nonnegative(jumpHeight) || !Positive(jumpDuration)
            || !Nonnegative(recoveryTime) || !Nonnegative(damage) || !Nonnegative(knockbackForce)
            || !Positive(slamRadius) || !Positive(hitBoxRadius) || !BossMobilityNavigation.Finite(hitBoxOffset))
            return false;
        // A movement-only asset must never enter the base action's attack/parry telegraph channel.
        return DealsDamage || !isParryable;
    }

    private static bool Positive(float value) => Nonnegative(value) && value > 0f;
    private static bool Nonnegative(float value) => value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}
