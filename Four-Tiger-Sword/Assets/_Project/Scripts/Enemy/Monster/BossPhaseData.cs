using UnityEngine;

[CreateAssetMenu(fileName = "BossPhaseData_", menuName = "SainGum/Monster/BossPhaseData")]
public class BossPhaseData : ScriptableObject
{
    [Header("페이즈 식별")]
    public string phaseId; // 페이즈 고유 ID (예: "phase_1", "phase_2")

    [Header("트리거")]
    [Tooltip("이 페이즈가 시작되는 HP %")]
    public float triggerHpPercent; // 페이즈 고유 ID (예: "phase_1", "phase_2")

    [Header("스탯 배율")]
    public float atkMultiplier = 1.0f; // 공격력 배율 (2페이즈에서 1.5배 등)
    public float defMultiplier = 1.0f; // 방어력 배율

    [Header("패턴 & BGM")]
    public string patternSetId;
    public string bgmId;

    [Header("스킬셋")]
    [Tooltip("이 페이즈에서 사용할 스킬 목록. null이면 이전 페이즈 스킬셋을 유지합니다.")]
    public MonsterSkillDataSO skillSet;

    [Header("비고")]
    public string note; // 메모/비고 (개발용 노트)
}
