using UnityEngine;

/// <summary>Normal-enemy patrol, target pursuit and attack selection.</summary>
public sealed class NormalEnemyBrain : EnemyBrainBase
{
    private EnemyAttackState _attack;
    public NormalEnemyBrain(Enemy owner) : base(owner) { }

    // A decision may wait through Hurt without becoming an active action or consuming skill cooldown.
    internal MonsterSkillData PendingAttackSkill { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        Owner.StartAttackCooldown();
    }

    internal void ClearScheduledAttack() => PendingAttackSkill = null;

    internal void SuspendPreparedAttackForHurt()
    {
        // Approach may have prepared an action that has not entered yet. Keep its decision,
        // but release the action slot so Hurt can continue making timed decisions.
        if (Owner.CurrentAction == null || Owner.CurrentAction.IsActive) return;
        var skill = Owner.CurrentAction.SkillData;
        Owner.CancelCurrentAction();
        PendingAttackSkill = skill;
    }

    public override void Tick(bool canAct)
    {
        if (!canAct || Owner.IsHurt || Owner.IsRoot || Owner.IsGroggy || Owner.PendingGroggy
            || Owner.IsDead || Owner.IsSelfDestructing || Owner.CombatLabPassive || Owner.EncounterPatrolOnly)
            Owner.CombatGroup?.ClearIntent(Owner);
        if (!Owner.EncounterPatrolOnly || Owner.IsDead)
        {
            if (Owner.PendingGroggy && !Owner.IsGroggy && !Owner.IsDead && !Owner.IsSelfDestructing)
            {
                ClearScheduledAttack();
                Owner.PendingGroggy = false;
                Owner.IsHurt = false;
                Owner.IsGroggy = true;
            }
            if (Owner.IsRoot || Owner.IsGroggy || Owner.IsDead || Owner.IsSelfDestructing || Owner.CombatLabPassive)
                ClearScheduledAttack();
            base.Tick(canAct);
            if (canAct) TickAttackSelection();
            return;
        }
        if (Machine == null || Owner.CombatLabPassive) return;
        ReturnToPatrol();
        if (canAct) Machine.CurrentState.Update();
    }

    private bool CanSelectAttack => Owner.isActiveAndEnabled && !Owner.IsDead && !Owner.IsSpawning
        && !Owner.IsGroggy && !Owner.PendingGroggy && !Owner.IsRoot && !Owner.IsSelfDestructing
        && !Owner.CombatLabPassive && !Owner.EncounterPatrolOnly && !Owner.IsSilenced;

    private void TickAttackSelection()
    {
        if (!CanSelectAttack || Owner.CurrentAction != null || Owner.HasSelectedAttack) return;
        if (Owner.DetectedTarget == null)
        {
            ClearScheduledAttack();
            return;
        }
        if (Owner.IsAttackCooldownReady)
        {
            // Make one decision per interval, including while repeatedly hurt. An unusable roll
            // also consumes the interval; damage never resets or rerolls this clock.
            PendingAttackSkill = Owner.SelectAttackSkill();
            Owner.StartAttackCooldown();
        }
        TryStartArmoredAttackFromHurt();
    }

    internal bool TryPrepareScheduledAttack()
    {
        if (!CanSelectAttack || Owner.IsHurt || Owner.IsKnockbacking
            || Owner.CurrentAction != null || Owner.HasSelectedAttack) return false;
        var skill = PendingAttackSkill;
        var target = Owner.DetectedTarget;
        if (skill == null || target == null) return false;
        ClearScheduledAttack();
        if (Owner.GetAttackSelectionWeight(skill, Vector3.Distance(Owner.transform.position, target.position)) <= 0f)
            return false;
        var action = skill.CreateAction(Owner);
        if (action == null || !CanSelectAttack || Owner.IsHurt || Owner.DetectedTarget != target) return false;
        Owner.CurrentAction = action;
        Owner.HasSelectedAttack = true;
        return true;
    }

    private void TryStartArmoredAttackFromHurt()
    {
        var skill = PendingAttackSkill;
        if (skill == null || !skill.superArmor || !CanSelectAttack || !IsEligibleHurt()) return;
        var target = Owner.DetectedTarget;
        if (target == null) return;
        float distance = Vector3.Distance(Owner.transform.position, target.position);
        if (distance > skill.executeRange || Owner.GetAttackSelectionWeight(skill, distance) <= 0f) return;
        ClearScheduledAttack();
        var action = skill.CreateAction(Owner);
        // Constructors and callbacks may change the owner. Recheck before releasing the reaction.
        if (action == null || !CanSelectAttack || !IsEligibleHurt() || Owner.DetectedTarget != target) return;
        distance = Vector3.Distance(Owner.transform.position, target.position);
        if (distance > skill.executeRange || Owner.GetAttackSelectionWeight(skill, distance) <= 0f) return;
        Owner.StopKnockbackForArmoredAttack();
        Owner.CurrentAction = action;
        Owner.HasSelectedAttack = true;
        Machine.ChangeState(_attack);
    }

    private bool IsEligibleHurt() => Machine.CurrentState is EnemyHurtState
        && Owner.isActiveAndEnabled && Owner.IsHurt && !Owner.IsDead && !Owner.IsGroggy
        && !Owner.PendingGroggy && !Owner.IsRoot && !Owner.IsSelfDestructing
        && !Owner.CombatLabPassive && !Owner.EncounterPatrolOnly;

    public override void SetPassive(bool passive)
    {
        if (passive) ClearScheduledAttack();
        base.SetPassive(passive);
    }

    internal void ReturnToPatrol()
    {
        ClearScheduledAttack();
        if (Machine != null && !Owner.IsDead && !Owner.CombatLabPassive)
            Machine.ChangeState(IdleState);
    }

    public override void Suspend()
    {
        ClearScheduledAttack();
        base.Suspend();
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
