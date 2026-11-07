using UnityEngine;

[CreateAssetMenu(fileName = "WaterFormAction", menuName = "Combat/Forms/Water")]
public class WaterFormActionDataSO : WeaponActionDataSO
{
    protected override FormChainSettings CreateDefaultChainSettings() => new FormChainSettings
    {
        Enabled = true,
        Trigger = FormChainTrigger.SuccessfulDodge,
        AttackSpeedBonusPercent = 25f,
        VulnerabilityBonusPercent = 15f
    };

    [InspectorName("폼 밸런스 (Balance)")]
    public WaterFormBalanceSettings FormBalance = new WaterFormBalanceSettings();
    [InspectorName("폼 동작·판정 (Execution)")]
    public WaterFormExecutionSettings FormExecution = new WaterFormExecutionSettings();

    [Header("Heavy Attack (optional)")]
    public WeaponActionData HeavyAttackStep;

    public float HeavyAttackHoldTime { get => FormExecution.HeavyAttackHoldTime; set => FormExecution.HeavyAttackHoldTime = value; }
    public float WaterHeavyDashDistance { get => FormExecution.WaterHeavyDashDistance; set => FormExecution.WaterHeavyDashDistance = value; }
    public float WaterHeavyDashDuration { get => FormExecution.WaterHeavyDashDuration; set => FormExecution.WaterHeavyDashDuration = value; }
    public float WaterHeavyTargetOvershoot { get => FormExecution.WaterHeavyTargetOvershoot; set => FormExecution.WaterHeavyTargetOvershoot = value; }
    public float WaterGaugePerSegment { get => FormBalance.WaterGaugePerSegment; set => FormBalance.WaterGaugePerSegment = value; }
    public float WaterNormalHitGain { get => FormBalance.WaterNormalHitGain; set => FormBalance.WaterNormalHitGain = value; }
    public float WaterSkillUseGain { get => FormBalance.WaterSkillUseGain; set => FormBalance.WaterSkillUseGain = value; }
    public float WaterSkillBuffDuration { get => FormBalance.WaterSkillBuffDuration; set => FormBalance.WaterSkillBuffDuration = value; }
    public float WaterSkillAttackBonusPercent { get => FormBalance.WaterSkillAttackBonusPercent; set => FormBalance.WaterSkillAttackBonusPercent = value; }
    public float WaterSkillHeavyDamageBonusPercent { get => FormBalance.WaterSkillHeavyDamageBonusPercent; set => FormBalance.WaterSkillHeavyDamageBonusPercent = value; }
    public float AreaRadius { get => FormExecution.AreaRadius; set => FormExecution.AreaRadius = value; }
}

[System.Serializable]
public class WaterFormExecutionSettings
{
    [Header("Heavy Attack (optional)")]
    [Min(0.05f)] public float HeavyAttackHoldTime = 0.35f;
    [Min(0f), Tooltip("타겟이 없을 때 수 폼 강공격의 대시 거리(m). 타겟이 있으면 적을 통과한 뒤 지정한 간격에서 멈춥니다.")]
    public float WaterHeavyDashDistance = 6f;
    [Min(0.01f), Tooltip("대시 이동 시간(초). 공격 전체 시간은 Heavy Attack Step의 Duration이며, 그 시간이 끝난 뒤 다음 강공격을 시작합니다.")]
    public float WaterHeavyDashDuration = 0.35f;
    [Min(0f), Tooltip("첫/연속 강공격에서 타겟을 통과한 뒤 남길 간격(m). 대시 시작 시 적의 콜라이더 Bounds와 플레이어 반지름을 기준으로 계산합니다. 0이어도 적의 몸체를 통과합니다.")]
    public float WaterHeavyTargetOvershoot = 1f;

    [Min(0.1f)] public float AreaRadius = 4f;
}

[System.Serializable]
public class WaterFormBalanceSettings
{
    [Header("Water Flow Gauge")]
    [Min(1f)] public float WaterGaugePerSegment = 100f;
    [Min(0f), Tooltip("일반 공격의 실제 적중당 게이지 획득량")]
    public float WaterNormalHitGain = 12f;
    [Min(0f), Tooltip("스킬 사용 1회당 게이지 획득량. 일반 공격보다 크게 설정합니다.")]
    public float WaterSkillUseGain = 100f;

    [Header("Water Skill Buff")]
    [Min(0f), Tooltip("스킬 사용 시 부여되는 버프 지속 시간(초). 재사용하면 중첩 없이 갱신합니다.")]
    public float WaterSkillBuffDuration = 6f;
    [Min(0f), Tooltip("버프 중 플레이어 공격력 증가율(%). 20 = +20%. 다른 공격력 증가율과 합산합니다.")]
    public float WaterSkillAttackBonusPercent = 20f;
    [Min(0f), Tooltip("버프 중 수 폼 강공격 피해 증가율(%). 30 = 최종 공격 계수에 1.3배.")]
    public float WaterSkillHeavyDamageBonusPercent = 30f;
}
