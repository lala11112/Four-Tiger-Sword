public enum BossPatternStepKind { Attack = 0, Wait = 1, Move = 2, Recover = 3 }

/// <summary>A pattern chooses commands; BossBrain owns their execution and timing.</summary>
public readonly struct BossPatternStep
{
    public readonly BossPatternStepKind Kind;
    public readonly MonsterSkillData Skill;
    public readonly float WaitSeconds;
    public readonly BossAttackPolicy AttackPolicy;
    public readonly float Timeout;
    public readonly BossMovementSettings Movement;

    private BossPatternStep(BossPatternStepKind kind, MonsterSkillData skill, float seconds,
        BossAttackPolicy policy = null, float timeout = 15f, BossMovementSettings movement = null)
    { Kind = kind; Skill = skill; WaitSeconds = seconds; AttackPolicy = policy; Timeout = timeout; Movement = movement; }

    public static BossPatternStep Attack(MonsterSkillData skill)
        => new BossPatternStep(BossPatternStepKind.Attack, skill, 0f);
    public static BossPatternStep Attack(MonsterSkillData skill, BossAttackPolicy policy, float timeout = 15f)
        => new BossPatternStep(BossPatternStepKind.Attack, skill, 0f, policy, timeout);
    public static BossPatternStep Wait(float seconds)
        => new BossPatternStep(BossPatternStepKind.Wait, null, seconds);
    public static BossPatternStep Move(BossMovementSettings movement)
        => new BossPatternStep(BossPatternStepKind.Move, null, 0f, movement: movement);
    public static BossPatternStep Recover(float seconds)
        => new BossPatternStep(BossPatternStepKind.Recover, null, seconds);
}

/// <summary>Create a fresh instance for each boss. Next step is requested only after completion.</summary>
public interface IBossPattern
{
    bool Loop { get; }
    void Enter();
    bool TryGetNextStep(out BossPatternStep step);
    void Exit();
}

/// <summary>Optional authored-step diagnostics without coupling the executor to a pattern implementation.</summary>
public interface IBossPatternProgress
{
    int CurrentStepIndex { get; }
}
