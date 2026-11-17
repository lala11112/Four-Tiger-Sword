using UnityEngine;

/// <summary>Normal-enemy patrol, target pursuit and attack selection.</summary>
public sealed class NormalEnemyBrain : EnemyBrainBase
{
    private static readonly int HitState = Animator.StringToHash("Hit");
    private static readonly int IdleStateHash = Animator.StringToHash("Idle");
    private static readonly int MoveStateHash = Animator.StringToHash("Move");
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

    // A higher-priority reaction can interrupt Hurt, leaving its pose to blend out on return.
    // Keep selecting on the world clock, but never start a new attack over that remaining pose.
    private bool IsHitAnimationActive
    {
        get
        {
            var animator = Owner.Animator;
            return animator != null && animator.isActiveAndEnabled
                && animator.runtimeAnimatorController != null && animator.layerCount > 0
                && (animator.HasState(0, IdleStateHash) || animator.HasState(0, MoveStateHash))
                && (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == HitState
                    || animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == HitState);
        }
    }

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
    }

    internal bool TryPrepareScheduledAttack()
    {
        if (!CanSelectAttack || Owner.IsHurt || Owner.IsKnockbacking || IsHitAnimationActive
            || Owner.CurrentAction != null || Owner.HasSelectedAttack) return false;
        var skill = PendingAttackSkill;
        var target = Owner.DetectedTarget;
        if (skill == null || target == null) return false;
        ClearScheduledAttack();
        if (Owner.GetAttackSelectionWeight(skill, Vector3.Distance(Owner.transform.position, target.position)) <= 0f)
            return false;
        var action = skill.CreateAction(Owner);
        if (action == null || !CanSelectAttack || Owner.IsHurt || IsHitAnimationActive || Owner.DetectedTarget != target) return false;
        Owner.CurrentAction = action;
        Owner.HasSelectedAttack = true;
        return true;
    }

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
        => !Owner.EncounterPatrolOnly && !Owner.IsHurt && !Owner.IsGroggy && !Owner.PendingGroggy
            && !Owner.IsRoot && !Owner.IsKnockbacking && !IsHitAnimationActive && base.TryStartSelfDestruct();

    protected override void BuildBehavior()
    {
        var idle = new EnemyIdleState(Owner);
        var trace = new EnemyTraceState(Owner);
        var combatIdle = new EnemyCombatIdleState(Owner);
        var approach = new EnemyApproachState(Owner);
        var attack = new EnemyAttackState(Owner);
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
        if (Owner.IsHurt || IsHitAnimationActive || !Owner.IsInExecuteRange || !Owner.CanAttack || Owner.CurrentAction == null
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
