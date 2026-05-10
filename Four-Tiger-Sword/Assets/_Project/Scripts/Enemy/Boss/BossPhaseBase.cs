using UnityEngine;

/// <summary>
/// IBossPhase의 기본 구현체.
/// 구체 페이즈는 이 클래스를 상속하고 필요한 메서드만 오버라이드합니다. (Template Method)
/// 데이터는 BossPhaseData ScriptableObject에서 읽어옵니다.
/// </summary>
public abstract class BossPhaseBase : IBossPhase
{
    protected readonly BossPhaseData Data;

    protected BossPhaseBase(BossPhaseData data)
    {
        Data = data;
    }

    // BossPhaseData.triggerHpPercent는 0~100 범위이므로 0.0~1.0으로 변환합니다.
    public float HpThreshold => Data.triggerHpPercent / 100f;
    public MonsterSkillDataSO SkillSet => Data.skillSet;

    /// <summary>스킬셋이 지정돼 있으면 교체하고, 스탯 배율을 적용합니다.</summary>
    public virtual void OnEnter(Boss boss)
    {
        if (Data.skillSet != null)
            boss.MonsterSkillData = Data.skillSet;
    }

    public virtual void OnExit(Boss boss) { }

    public virtual void OnUpdate(Boss boss) { }

    /// <summary>기본 구현은 데미지를 변경하지 않고 그대로 반환합니다.</summary>
    public virtual int ModifyIncomingDamage(int rawDamage, Boss boss) => rawDamage;
}
