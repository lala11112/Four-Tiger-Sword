using UnityEngine;

[CreateAssetMenu(fileName = "EarthFormAction", menuName = "Combat/Forms/Earth")]
public class EarthFormActionDataSO : WeaponActionDataSO
{
    [Header("Earth Skill - Charge")]
    [Min(0f)] public float SkillSpiritCost = 150f;
    [Min(0f)] public float SkillCooldownSeconds = 8f;
    [Min(0f), Tooltip("전방 돌진 거리(m). 벽과 적의 충돌은 유지합니다.")]
    public float SkillDashDistance = 5f;
    [Min(0.01f), Tooltip("돌진 시간(초). 공격 속도와 무관합니다.")]
    public float SkillDashDuration = 0.4f;
    [Min(0f), Tooltip("돌진 공격력 계수. 한 번의 돌진은 적마다 한 번 타격합니다.")]
    public float SkillDashDamageMultiplier = 2f;
    [Min(0.1f)] public float SkillDashHitRadius = 1.2f;
    [Tooltip("돌진 판정 중심의 로컬 위치")]
    public Vector3 SkillDashHitOffset = new Vector3(0f, 1f, 0.8f);
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
