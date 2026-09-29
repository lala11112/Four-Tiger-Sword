using UnityEngine;

[CreateAssetMenu(fileName = "WaterFormAction", menuName = "Combat/Forms/Water")]
public class WaterFormActionDataSO : WeaponActionDataSO
{
    [Header("Heavy Attack (optional)")]
    public WeaponActionData HeavyAttackStep;
    [Min(0.05f)] public float HeavyAttackHoldTime = 0.35f;
    [Min(0f), Tooltip("수 폼 강공격 1회당 기본 대시 거리(m). 후속 대시는 기억한 타겟을 통과할 만큼 연장될 수 있습니다.")]
    public float WaterHeavyDashDistance = 6f;
    [Min(0.01f), Tooltip("대시 이동 시간(초). 공격 전체 시간은 Heavy Attack Step의 Duration이며, 그 시간이 끝난 뒤 다음 강공격을 시작합니다.")]
    public float WaterHeavyDashDuration = 0.35f;
    [Min(0f), Tooltip("기억한 타겟을 향하는 후속 대시에서 타겟 뒤로 지나가는 거리(m)")]
    public float WaterHeavyTargetOvershoot = 1f;

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

    [Header("Delayed Hit Effect")]
    [Tooltip("지연 데미지가 터질 때 적 위치에 스폰할 이펙트 프리팹")]
    public GameObject DelayedHitVFX;

    [Min(0.1f)] public float AreaRadius = 4f;
}
