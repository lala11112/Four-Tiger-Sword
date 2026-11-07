using UnityEngine;

[CreateAssetMenu(fileName = "FireFormAction", menuName = "Combat/Forms/Fire")]
public class FireFormActionDataSO : WeaponActionDataSO
{
    protected override FormChainSettings CreateDefaultChainSettings() => new FormChainSettings
    {
        Enabled = true,
        Trigger = FormChainTrigger.OtherFormFinalHit,
        AttackBonusPercent = 30f
    };

    [InspectorName("폼 밸런스 (Balance)")]
    public FireFormBalanceSettings FormBalance = new FireFormBalanceSettings();
    [InspectorName("폼 동작·판정 (Execution)")]
    public FireFormExecutionSettings FormExecution = new FireFormExecutionSettings();

    public float FireSkillRadius { get => FormExecution.FireSkillRadius; set => FormExecution.FireSkillRadius = value; }
    public float FireEnhancedSkillRadius { get => FormExecution.FireEnhancedSkillRadius; set => FormExecution.FireEnhancedSkillRadius = value; }
    public float FireSkillBuffDuration { get => FormBalance.FireSkillBuffDuration; set => FormBalance.FireSkillBuffDuration = value; }
    public float FireSkillAttackBonusPercent { get => FormBalance.FireSkillAttackBonusPercent; set => FormBalance.FireSkillAttackBonusPercent = value; }
    public float FireUltimateDamageDealtBonusPercent { get => FormBalance.FireUltimateDamageDealtBonusPercent; set => FormBalance.FireUltimateDamageDealtBonusPercent = value; }
    public float FireUltimateDamageTakenBonusPercent { get => FormBalance.FireUltimateDamageTakenBonusPercent; set => FormBalance.FireUltimateDamageTakenBonusPercent = value; }
    public float FireUltimateAttackSpeedBonusPercent { get => FormBalance.FireUltimateAttackSpeedBonusPercent; set => FormBalance.FireUltimateAttackSpeedBonusPercent = value; }
    public float FireUltimateNormalLifeStealPercent { get => FormBalance.FireUltimateNormalLifeStealPercent; set => FormBalance.FireUltimateNormalLifeStealPercent = value; }
    public float FireUltimateSkillLifeStealPercent { get => FormBalance.FireUltimateSkillLifeStealPercent; set => FormBalance.FireUltimateSkillLifeStealPercent = value; }
    public float FireExplosionDelay { get => FormExecution.FireExplosionDelay; set => FormExecution.FireExplosionDelay = value; }
}

[System.Serializable]
public class FireFormExecutionSettings
{
    [Header("Fire Skill")]
    [Min(0.1f), Tooltip("일반 스킬의 범위 반경(m). 중심/타격 시점/피해는 Skill Steps를 사용합니다.")]
    public float FireSkillRadius = 3f;
    [Min(0.1f), Tooltip("궁극기 중 강화 스킬 반경(m). 일반 스킬 반경 이상으로 적용됩니다.")]
    public float FireEnhancedSkillRadius = 5f;

    [Header("Fire Stack Explosion")]
    [Tooltip("화 폼 6스택 추가 피해까지의 실제 시간(초). 히트스탑과 무관하며 0이면 즉시 적용합니다.")]
    [Min(0f)] public float FireExplosionDelay = 0.15f;
}

[System.Serializable]
public class FireFormBalanceSettings
{
    [Header("Fire Skill")]
    [Min(0f)] public float FireSkillBuffDuration = 6f;
    [Min(0f), Tooltip("스킬 사용 시 공격력 증가율(%). 재사용 시 중첩 없이 지속 시간을 갱신합니다.")]
    public float FireSkillAttackBonusPercent = 20f;

    [Header("Fire Toggle Ultimate")]
    [Min(0f), Tooltip("궁극기 중 주는 피해 증가율(%)")]
    public float FireUltimateDamageDealtBonusPercent = 50f;
    [Min(0f), Tooltip("궁극기 중 받는 피해 증가율(%)")]
    public float FireUltimateDamageTakenBonusPercent = 30f;
    [Min(0f), Tooltip("궁극기 중 공격 속도 증가율(%). 기존 공속 증가율과 합산합니다.")]
    public float FireUltimateAttackSpeedBonusPercent = 30f;
    [Range(0f, 100f), Tooltip("궁극기 중 일반 공격이 실제로 깎은 적 체력의 회복 비율(%)")]
    public float FireUltimateNormalLifeStealPercent = 10f;
    [Range(0f, 100f), Tooltip("궁극기 중 스킬의 체력 흡수율(%). 평타 흡수율 이상으로 적용됩니다.")]
    public float FireUltimateSkillLifeStealPercent = 25f;
}
