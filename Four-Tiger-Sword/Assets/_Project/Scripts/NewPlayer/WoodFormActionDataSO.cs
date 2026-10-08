using UnityEngine;

[CreateAssetMenu(fileName = "WoodFormAction", menuName = "Combat/Forms/Wood")]
public class WoodFormActionDataSO : WeaponActionDataSO
{
    [InspectorName("폼 밸런스 (Balance)")]
    public WoodFormBalanceSettings FormBalance = new WoodFormBalanceSettings();
    [InspectorName("폼 동작·판정 (Execution)")]
    public WoodFormExecutionSettings FormExecution = new WoodFormExecutionSettings();

    public int MaximumStacks { get => FormBalance.MaximumStacks; set => FormBalance.MaximumStacks = value; }
    public int StacksPerHit { get => FormBalance.StacksPerHit; set => FormBalance.StacksPerHit = value; }
    public float StackLifetime { get => FormBalance.StackLifetime; set => FormBalance.StackLifetime = value; }
    public float VulnerabilityDamageBonusPercent { get => FormBalance.VulnerabilityDamageBonusPercent; set => FormBalance.VulnerabilityDamageBonusPercent = value; }
    public float VulnerabilityDuration { get => FormBalance.VulnerabilityDuration; set => FormBalance.VulnerabilityDuration = value; }
    public float SkillSpiritCost { get => FormBalance.SkillSpiritCost; set => FormBalance.SkillSpiritCost = value; }
    public float SkillCooldownSeconds { get => FormBalance.SkillCooldownSeconds; set => FormBalance.SkillCooldownSeconds = value; }
    public float SkillRadius { get => FormExecution.SkillRadius; set => FormExecution.SkillRadius = value; }
    public Vector3 SkillCenterOffset { get => FormExecution.SkillCenterOffset; set => FormExecution.SkillCenterOffset = value; }
    public float SkillDamageMultiplier { get => FormBalance.SkillDamageMultiplier; set => FormBalance.SkillDamageMultiplier = value; }
    public float PullDuration { get => FormExecution.PullDuration; set => FormExecution.PullDuration = value; }
    public float PullSpeed { get => FormExecution.PullSpeed; set => FormExecution.PullSpeed = value; }
    public float PullStopDistance { get => FormExecution.PullStopDistance; set => FormExecution.PullStopDistance = value; }
    public float ElementDamageBonusPercent { get => FormBalance.ElementDamageBonusPercent; set => FormBalance.ElementDamageBonusPercent = value; }
    public float ElementBuffDuration { get => FormBalance.ElementBuffDuration; set => FormBalance.ElementBuffDuration = value; }
    public float UltimateRadius { get => FormExecution.UltimateRadius; set => FormExecution.UltimateRadius = value; }
    public float UltimateFieldDuration { get => FormBalance.UltimateFieldDuration; set => FormBalance.UltimateFieldDuration = value; }
    public float SilenceDuration { get => FormBalance.SilenceDuration; set => FormBalance.SilenceDuration = value; }
}

[System.Serializable]
public class WoodFormBalanceSettings
{
    [Header("Wood Passive - Vulnerability")]
    [Min(1)] public int MaximumStacks = 5;
    [Min(1)] public int StacksPerHit = 1;
    [Min(0.1f), Tooltip("추가 타격 없이 스택이 유지되는 시간(초)")]
    public float StackLifetime = 5f;
    [Min(0f)] public float VulnerabilityDamageBonusPercent = 20f;
    [Min(0.1f)] public float VulnerabilityDuration = 6f;
    [Header("Wood Skill - Gather")]
    [Min(0f)] public float SkillSpiritCost = 180f;
    [Min(0f)] public float SkillCooldownSeconds = 12f;
    [Min(0f), Tooltip("공격력에 곱하는 스킬 피해 계수")]
    public float SkillDamageMultiplier = 0.5f;
    [Header("Wood Ultimate - Element Buff")]
    [Min(0f), Tooltip("모든 유속성 피해 증가율(%). 무속성 피해에는 미적용")]
    public float ElementDamageBonusPercent = 30f;
    [Min(0f)] public float ElementBuffDuration = 10f;
    [Header("Wood Ultimate - Silence Field")]
    [UnityEngine.Serialization.FormerlySerializedAs("FieldDuration")]
    [Min(0f)] public float UltimateFieldDuration = 5f;
    [Min(0.01f), Tooltip("영역 안에서 계속 갱신되고 영역을 벗어난 뒤에도 남는 침묵 시간(초)")]
    public float SilenceDuration = 0.3f;
}

[System.Serializable]
public class WoodFormExecutionSettings
{
    [Header("Wood Skill - Gather")]
    [UnityEngine.Serialization.FormerlySerializedAs("AreaRadius")]
    [Min(0.1f)] public float SkillRadius = 4f;
    public Vector3 SkillCenterOffset = new Vector3(0f, 0f, 2f);
    [Min(0f)] public float PullDuration = 0.8f;
    [Min(0f)] public float PullSpeed = 8f;
    [Min(0f)] public float PullStopDistance = 0.5f;
    [Header("Wood Ultimate - Silence Field")]
    [Min(0.1f)] public float UltimateRadius = 6f;
}
