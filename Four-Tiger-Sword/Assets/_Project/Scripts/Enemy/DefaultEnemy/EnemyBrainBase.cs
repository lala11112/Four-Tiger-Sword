using UnityEngine.AI;

/// <summary>Shared reaction/death lifecycle; subclasses supply autonomous behavior states.</summary>
public abstract class EnemyBrainBase : IEnemyBrain
{
    protected readonly Enemy Owner;
    protected StateMachine Machine;
    protected IPlayerState IdleState;
    protected IPlayerState ReturnState;
    private IPlayerState _passiveState;
    private EnemySelfDestructState _selfDestructState;

    protected EnemyBrainBase(Enemy owner) { Owner = owner; }
    public string StateName => Machine?.CurrentState?.GetType().Name ?? string.Empty;
    protected bool IsReacting => Machine?.CurrentState is EnemyHurtState
        || Machine?.CurrentState is EnemyGroggyState || Machine?.CurrentState is EnemyRootState;

    public virtual void Initialize()
    {
        Machine = new StateMachine();
        _passiveState = new PassiveState();
        var hurt = new EnemyHurtState(Owner);
        var groggy = new EnemyGroggyState(Owner);
        var root = new EnemyRootState(Owner);
        var die = new EnemyDieState(Owner);
        _selfDestructState = new EnemySelfDestructState(Owner);
        BuildBehavior();
        Machine.AddTransition(root, ReturnState, () => !Owner.IsRoot);
        Machine.AddTransition(hurt, ReturnState, () => !Owner.IsHurt);
        Machine.AddTransition(groggy, ReturnState, () => !Owner.IsGroggy);
        // Preserve the original global transition priority.
        Machine.AddAnyTransition(die, () => Owner.IsDead);
        Machine.AddAnyTransition(_selfDestructState, () => Owner.IsSelfDestructing);
        Machine.AddAnyTransition(hurt, () => Owner.IsHurt && !Owner.IsGroggy);
        Machine.AddAnyTransition(groggy, () => Owner.IsGroggy);
        Machine.AddAnyTransition(root, () => Owner.IsRoot);
        Machine.ChangeState(Owner.CombatLabPassive ? _passiveState : IdleState);
        if (Owner.CombatLabPassive) StopNavigation();
    }

    protected abstract void BuildBehavior();

    public virtual void Tick(bool canAct)
    {
        if (Machine == null) return;
        if (Owner.IsDead)
        {
            if (!(Machine.CurrentState is EnemyDieState)) Machine.Update();
            return;
        }
        if (canAct && !Owner.CombatLabPassive)
        {
            TryStartSelfDestruct();
            Owner.SenseTarget();
        }
        Machine.Update(canAct && !Owner.CombatLabPassive, canAct && !Owner.CombatLabPassive);
        if (Owner.CombatLabPassive && canAct)
        {
            if (IsReacting) Machine.CurrentState.Update();
            if (!Owner.IsHurt && !Owner.IsGroggy && !Owner.IsRoot && !Owner.IsDead
                && Machine.CurrentState != _passiveState)
                Machine.ChangeState(_passiveState);
        }
    }

    public virtual void SetPassive(bool passive)
    {
        if (Machine == null) return;
        if (passive)
        {
            if (!Owner.IsDead && !Owner.IsHurt && !Owner.IsGroggy && !Owner.IsRoot)
                Machine.ChangeState(_passiveState);
            StopNavigation();
        }
        else if (Machine.CurrentState == _passiveState) Machine.ChangeState(IdleState);
    }

    public virtual void Suspend()
    {
        // Keep an in-progress reaction and its movement lock/timer for reactivation.
        // Autonomous states must Exit (notably Approach's navigation override).
        if (Machine != null && !Owner.IsDead && !IsReacting)
            Machine.ChangeState(_passiveState);
    }

    public virtual void Resume()
    {
        if (Machine != null && !Owner.IsDead && !Owner.CombatLabPassive
            && Machine.CurrentState == _passiveState)
            Machine.ChangeState(IdleState);
        Owner.SyncMovementLock();
    }

    public virtual bool TryStartSelfDestruct()
    {
        if (Machine == null || Owner.IsDead || Owner.CombatLabPassive || Owner.IsSelfDestructing) return false;
        var data = Owner.ActiveSkillSet?.selfDestruct;
        if (data == null || !data.ShouldTrigger(Owner.EnemyStat)) return false;
        Owner.BeginSelfDestruct();
        Machine.ChangeState(_selfDestructState);
        return true;
    }

    private void StopNavigation()
    {
        var nav = Owner.GetComponent<NavMeshAgent>();
        if (nav != null && nav.enabled && nav.isOnNavMesh)
        { nav.ResetPath(); nav.velocity = UnityEngine.Vector3.zero; }
        Owner.SyncMovementLock();
    }

    private sealed class PassiveState : IPlayerState
    {
        public void Enter() { }
        public void Update() { }
        public void Exit() { }
    }
}
