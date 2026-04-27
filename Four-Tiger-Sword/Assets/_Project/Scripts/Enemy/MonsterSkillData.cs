using UnityEngine;

public abstract class MonsterSkillData : ScriptableObject
{
    [Tooltip("애니메이터 트리거 이름")]
    public string animName;

    [Tooltip("이 공격을 선택할 수 있는 최대 거리 (공격선택거리)")]
    public float engageRange = 5f;

    [Tooltip("공격을 실행하는 거리 (히트박스 사거리)")]
    public float excuteRange = 2f;

    [Tooltip("공격 선택 가중치. 낮을수록 드물게 선택됨")]
    public float weight = 10f;

    public abstract EnemyAction CreateAction(Enemy enemy);
}