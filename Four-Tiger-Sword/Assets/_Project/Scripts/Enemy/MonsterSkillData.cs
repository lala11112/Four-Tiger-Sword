using UnityEngine;

public abstract class MonsterSkillData : ScriptableObject
{
    [Tooltip("애니메이터 트리거 이름")]
    public string animName;

    [Tooltip("이 공격을 선택할 수 있는 최대 거리 (공격선택거리)")]
    public float engageRange = 5f;

    [Tooltip("공격을 실행하는 거리 (히트박스 사거리)")]
    [UnityEngine.Serialization.FormerlySerializedAs("excuteRange")]
    public float executeRange = 2f;

    [Tooltip("공격 선택 가중치. 낮을수록 드물게 선택됨")]
    public float weight = 10f;

    [Header("AI 공격 선택")]
    [Tooltip("이 거리보다 가까우면 선택/실행하지 않습니다. executeRange 이하로 설정하세요.")]
    [Min(0f)] public float minimumRange = 0f;
    [Tooltip("선호하는 선택 거리. 0이면 executeRange를 사용합니다.")]
    [Min(0f)] public float preferredRange = 0f;
    [Tooltip("공격 시작부터 계산하는 개체별 스킬 쿨타임")]
    [Min(0f)] public float cooldown = 0f;
    [Tooltip("직전에 사용한 공격의 선택 가중치 배율")]
    [Range(0.1f, 1f)] public float repeatWeightMultiplier = 0.5f;

    [Header("공격 속도")]
    [Tooltip("공격 애니메이션 재생 속도 및 피격 판정 타이밍 배율. 1.0 = 기본, 2.0 = 2배 빠름")]
    [Min(0.01f)]
    public float attackSpeed = 1.0f;

    [Header("패링")]
    [Tooltip("false로 설정하면 플레이어의 패링으로 이 공격을 막을 수 없습니다.")]
    public bool isParryable = true;

    [Tooltip("패링 예고 지속 시간 (초, attackSpeed 배율 적용). 이 시간이 지나면 자동으로 예고가 종료됩니다.\n" +
             "단일 타격 = hitStartTime, 콤보 = attackDuration, 대쉬 = startupTime 등을 참고해 설정하세요.")]
    public float telegraphDuration = 1.0f;

    public abstract EnemyAction CreateAction(Enemy enemy);
}
