using UnityEngine;

[CreateAssetMenu(fileName = "WoodFormAction", menuName = "Combat/Forms/Wood")]
public class WoodFormActionDataSO : WeaponActionDataSO
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
    [UnityEngine.Serialization.FormerlySerializedAs("AreaRadius")]
    [Min(0.1f)] public float SkillRadius = 4f;
    public Vector3 SkillCenterOffset = new Vector3(0f, 0f, 2f);
    [Min(0f), Tooltip("공격력에 곱하는 스킬 피해 계수")]
    public float SkillDamageMultiplier = 0.5f;
    [Min(0f)] public float PullDuration = 0.8f;
    [Min(0f)] public float PullSpeed = 8f;
    [Min(0f)] public float PullStopDistance = 0.5f;
    [Header("Wood Ultimate - Element Buff")]
    [Min(0f), Tooltip("모든 유속성 피해 증가율(%). 무속성 피해에는 미적용")]
    public float ElementDamageBonusPercent = 30f;
    [Min(0f)] public float ElementBuffDuration = 10f;
    [Header("Wood Ultimate - Silence Field")]
    [Min(0.1f)] public float UltimateRadius = 6f;
    [UnityEngine.Serialization.FormerlySerializedAs("FieldDuration")]
    [Min(0f)] public float UltimateFieldDuration = 5f;
    [Min(0.01f), Tooltip("영역 안에서 계속 갱신되고 영역을 벗어난 뒤에도 남는 침묵 시간(초)")]
    public float SilenceDuration = 0.3f;
}
