using UnityEngine;

[CreateAssetMenu(fileName = "EarthFormAction", menuName = "Combat/Forms/Earth")]
public class EarthFormActionDataSO : WeaponActionDataSO
{
    [InspectorName("폼 밸런스 (Balance)")]
    public EarthFormBalanceSettings FormBalance = new EarthFormBalanceSettings();
    [InspectorName("폼 동작·판정 (Execution)")]
    public EarthFormExecutionSettings FormExecution = new EarthFormExecutionSettings();

    public float SkillCooldownSeconds { get => FormBalance.SkillCooldownSeconds; set => FormBalance.SkillCooldownSeconds = value; }
    public float SkillDashDistance { get => FormExecution.SkillDashDistance; set => FormExecution.SkillDashDistance = value; }
    public float SkillDashDuration { get => FormExecution.SkillDashDuration; set => FormExecution.SkillDashDuration = value; }
    public float SkillDashDamageMultiplier { get => FormBalance.SkillDashDamageMultiplier; set => FormBalance.SkillDashDamageMultiplier = value; }
    public float SkillDashHitRadius { get => FormExecution.SkillDashHitRadius; set => FormExecution.SkillDashHitRadius = value; }
    public Vector3 SkillDashHitOffset { get => FormExecution.SkillDashHitOffset; set => FormExecution.SkillDashHitOffset = value; }
    public float ShieldAmount { get => FormBalance.ShieldAmount; set => FormBalance.ShieldAmount = value; }
    public float ShieldDuration { get => FormBalance.ShieldDuration; set => FormBalance.ShieldDuration = value; }
    public float UltimateShieldAmount { get => FormBalance.UltimateShieldAmount; set => FormBalance.UltimateShieldAmount = value; }
    public float UltimateShieldDuration { get => FormBalance.UltimateShieldDuration; set => FormBalance.UltimateShieldDuration = value; }
    public float UltimateBuffDuration { get => FormBalance.UltimateBuffDuration; set => FormBalance.UltimateBuffDuration = value; }
    public float UltimateShieldDamageBonusPercent { get => FormBalance.UltimateShieldDamageBonusPercent; set => FormBalance.UltimateShieldDamageBonusPercent = value; }
}

[System.Serializable]
public class EarthFormBalanceSettings
{
    [Header("Earth Skill - Charge")]
    [Min(0f)] public float SkillCooldownSeconds = 8f;
    [Min(0f), Tooltip("돌진 공격력 계수. 한 번의 돌진은 적마다 한 번 타격합니다.")]
    public float SkillDashDamageMultiplier = 2f;
    [Header("Earth Skill - Shield")]
    [Min(0f)] public float ShieldAmount = 300f;
    [Min(0.1f)] public float ShieldDuration = 5f;
    [Header("Earth Ultimate - Shield")]
    [Min(0f)] public float UltimateShieldAmount = 600f;
    [Min(0f)] public float UltimateShieldDuration = 10f;
    [Header("Earth Ultimate - Damage Buff")]
    [Min(0f), Tooltip("궁극기 버프의 지속 시간(초). 방어막이 없어도 시간은 흐릅니다.")]
    public float UltimateBuffDuration = 10f;
    [Min(0f), Tooltip("버프 시간 중 스킬 또는 궁극기 방어막이 남아 있을 때 주는 피해 증가율(%)")]
    public float UltimateShieldDamageBonusPercent = 30f;
}

[System.Serializable]
public class EarthFormExecutionSettings
{
    [Header("Earth Skill - Charge")]
    [Min(0f), Tooltip("전방 돌진 거리(m). 벽과 적의 충돌은 유지합니다.")]
    public float SkillDashDistance = 5f;
    [Min(0.01f), Tooltip("돌진 시간(초). 공격 속도와 무관합니다.")]
    public float SkillDashDuration = 0.4f;
    [Min(0.1f)] public float SkillDashHitRadius = 1.2f;
    [Tooltip("돌진 판정 중심의 로컬 위치")]
    public Vector3 SkillDashHitOffset = new Vector3(0f, 1f, 0.8f);
}
