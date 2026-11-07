using UnityEngine;

[CreateAssetMenu(fileName = "BossPhaseData_", menuName = "SainGum/Monster/BossPhaseData")]
public class BossPhaseData : ScriptableObject
{
    [Header("페이즈 식별")]
    public string phaseId; // 페이즈 고유 ID (예: "phase_1", "phase_2")

    [Header("페이즈 체력")]
    [Tooltip("이 체력을 모두 소진하면 다음 페이즈로 부활합니다. 마지막 페이즈에서는 사망합니다.")]
    [Min(.01f)] public float phaseMaxHp = 100f;

    [Header("스탯 배율")]
    public float atkMultiplier = 1.0f; // 공격력 배율 (2페이즈에서 1.5배 등)
    public float defMultiplier = 1.0f; // 방어력 배율

    [Header("페이즈 보호막")]
    [Tooltip("켜면 페이즈 진입 시 남은 보호막을 지정한 수치로 교체합니다. 꺼져 있으면 유지합니다.")]
    public bool overrideShield;
    [Min(0f)] public float shieldAmount;

    [Header("페이즈 음악")]
    public BossMusicMode musicMode;
    public AudioClip musicClip;
    [Range(0f, 1f)] public float musicVolume = 1f;
    [Min(0f)] public float musicFadeSeconds = .5f;

    [Header("패턴 & BGM")]
    public string patternSetId;
    public string bgmId;

    [Header("스킬셋")]
    [Tooltip("이 페이즈에서 사용할 스킬 목록. null이면 이전 페이즈 스킬셋을 유지합니다.")]
    public MonsterSkillDataSO skillSet;

    [Header("보스 패턴")]
    [Tooltip("순차 패턴 또는 사용자 정의 패턴. 비어 있으면 현재 스킬셋에서 가중치로 공격을 선택합니다.")]
    public BossPatternSO pattern;

    [Header("보스 전용 전투 판단")]
    [Tooltip("설정하면 이 페이즈의 이동과 패턴 선택을 보스 전용 전투 프로필로 제어합니다.")]
    public BossCombatProfileSO combatProfile;

    [Header("페이즈 시작 패턴")]
    [Tooltip("전술 전투 프로필이 있는 페이즈에 진입하면 일반 이동/패턴보다 먼저 한 번 실행합니다. 중단되어도 같은 페이즈에서는 반복하지 않습니다.")]
    public BossPatternSO entryPattern;

    [Header("비고")]
    public string note; // 메모/비고 (개발용 노트)
}
