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
    public string patternSetId; // 이 페이즈에서 사용할 공격 패턴 세트 ID
    public string bgmId; // 이 페이즈 전용 BGM ID (페이즈마다 음악 바뀔 때)

    [Header("비고")]
    public string note; // 메모/비고 (개발용 노트)
}
