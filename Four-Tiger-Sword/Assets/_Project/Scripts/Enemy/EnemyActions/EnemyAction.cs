public abstract class EnemyAction
{
    public MonsterSkillData SkillData { get; protected set; }
    public bool IsFinished { get; protected set; }

    public virtual void Enter() { }
    public virtual void Update() { }
    public virtual void Exit() { }
}