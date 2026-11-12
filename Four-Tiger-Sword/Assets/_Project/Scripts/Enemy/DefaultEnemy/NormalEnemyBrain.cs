using UnityEngine;

/// <summary>Normal-enemy patrol, target pursuit and attack selection.</summary>
public sealed class NormalEnemyBrain : EnemyBrainBase
{
    private int _directStaggerCount;
    private EnemyAttackState _attack;
    public NormalEnemyBrain(Enemy owner) : base(owner) { }

    internal void RecordDirectStagger() => _directStaggerCount = Mathf.Min(3, _directStaggerCount + 1);
    internal void ClearHitChain() => _directStaggerCount = 0;

    public override void Tick(bool canAct)
    {
        if (!canAct || Owner.IsHurt || Owner.IsRoot || Owner.IsGroggy || Owner.PendingGroggy
            || Owner.IsDead || Owner.IsSelfDestructing || Owner.CombatLabPassive || Owner.EncounterPatrolOnly)
            Owner.CombatGroup?.ClearIntent(Owner);
        if (!Owner.EncounterPatrolOnly || Owner.IsDead)
        {
            if (Owner.PendingGroggy && !Owner.IsGroggy && !Owner.IsDead && !Owner.IsSelfDestructing)
            {
                ClearHitChain();
                Owner.PendingGroggy = false;
                Owner.IsHurt = false;
                Owner.IsGroggy = true;
            }
            if (Owner.IsRoot || Owner.IsGroggy || Owner.IsDead || Owner.IsSelfDestructing || Owner.CombatLabPassive)
                ClearHitChain();
            base.Tick(canAct);
            return;
        }
        if (Machine == null || Owner.CombatLabPassive) return;
        ReturnToPatrol();
        if (canAct) Machine.CurrentState.Update();
    }

    // Hurt has already interrupted the previous action and applied its cooldown.
    // Give a ready counter its turn before this tick can expire the hurt timer.
    internal bool TryCounterFromHurt()
    {
        if (!CanCounter()) return false;
        var skill = Owner.SelectAttackSkill(true);
        if (skill == null) return false;
        var action = skill.CreateAction(Owner);
        // Constructors and callbacks may change the owner. Recheck before releasing the reaction.
        if (action == null || !CanCounter() || Owner.DetectedTarget == null) return false;
        float distance = Vector3.Distance(Owner.transform.position, Owner.DetectedTarget.position);
        if (distance > skill.executeRange || Owner.GetAttackSelectionWeight(skill, distance) <= 0f) return false;
        Owner.StopKnockbackForCounter();
        Owner.CurrentAction = action;
        Owner.HasSelectedAttack = true;
        Machine.ChangeState(_attack);
        return true;
    }

    private bool CanCounter() => _directStaggerCount >= 3 && Machine.CurrentState is EnemyHurtState
        && Owner.isActiveAndEnabled && Owner.IsHurt && !Owner.IsDead && !Owner.IsGroggy
        && !Owner.PendingGroggy && !Owner.IsRoot && !Owner.IsSelfDestructing && !Owner.IsSilenced
        && !Owner.CombatLabPassive && !Owner.EncounterPatrolOnly && Owner.IsAttackCooldownReady;

    public override void SetPassive(bool passive)
    {
        if (passive) ClearHitChain();
        base.SetPassive(passive);
    }

    internal void ReturnToPatrol()
    {
        if (Machine != null && !Owner.IsDead && !Owner.CombatLabPassive)
            Machine.ChangeState(IdleState);
    }

    public override bool TryStartSelfDestruct()
        => !Owner.EncounterPatrolOnly && base.TryStartSelfDestruct();

    protected override void BuildBehavior()
    {
        var idle = new EnemyIdleState(Owner);
        var trace = new EnemyTraceState(Owner);
        var combatIdle = new EnemyCombatIdleState(Owner);
        var approach = new EnemyApproachState(Owner);
        var attack = new EnemyAttackState(Owner);
        _attack = attack;
        IdleState = idle;
        ReturnState = trace;

        Machine.AddTransition(idle, trace, () => Owner.DetectedTarget != null || Owner.HasTargetMemory);
        Machine.AddTransition(trace, combatIdle, () => InCombatRange);
        Machine.AddTransition(trace, idle, () => Owner.DetectedTarget == null && !Owner.HasTargetMemory);
        Machine.AddTransition(combatIdle, trace, () => OutOfCombatRange);
        Machine.AddTransition(combatIdle, approach, () => Owner.HasSelectedAttack);
        Machine.AddTransition(approach, attack, TryStartAttack);
        Machine.AddTransition(approach, trace, () => OutOfCombatRange);
        Machine.AddTransition(approach, combatIdle, () => Owner.CurrentAction == null);
        Machine.AddTransition(attack, combatIdle, () => Owner.IsAttackFinished);
    }

    private bool TryStartAttack()
    {
        if (!Owner.IsInExecuteRange || !Owner.CanAttack || Owner.CurrentAction == null
            || !Owner.IsSkillReady(Owner.CurrentAction.SkillData))
        {
            Owner.CombatGroup?.CancelAttackRequest(Owner);
            return false;
        }
        return Owner.CombatGroup == null || Owner.CombatGroup.TryStartAttack(Owner);
    }

    private bool InCombatRange => Owner.DetectedTarget != null
        && Vector3.Distance(Owner.transform.position, Owner.DetectedTarget.position) <= Owner.CombatRange;
    private bool OutOfCombatRange => Owner.DetectedTarget == null
        || Vector3.Distance(Owner.transform.position, Owner.DetectedTarget.position) > Owner.CombatExitRange;
}
