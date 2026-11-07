using UnityEngine;

/// <summary>Optional hooks for a data-backed phase; runtime state belongs to each boss.</summary>
public abstract class BossPhaseBase : IBossPhase
{
    protected readonly BossPhaseData Data;

    protected BossPhaseBase(BossPhaseData data) { Data = data; }
    public BossPhaseData Configuration => Data;
    public float MaxHp => Data != null ? Data.phaseMaxHp : 0f;
    public MonsterSkillDataSO SkillSet => Data != null ? Data.skillSet : null;
    public BossPatternSO Pattern => Data != null ? Data.pattern : null;
    public virtual void OnEnter(Boss boss) { }
    public virtual void OnExit(Boss boss) { }
    public virtual void OnUpdate(Boss boss) { }
    public virtual float ModifyIncomingDamage(float rawDamage, Boss boss) => rawDamage;
}
