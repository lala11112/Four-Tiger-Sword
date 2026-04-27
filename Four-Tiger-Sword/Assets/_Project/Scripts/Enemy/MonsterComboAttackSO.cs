using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class EnemyHitEvent
{
    [Tooltip("이 타격의 판정 시작 시간 (action.Enter 기준)")]
    public float hitStartTime = 0.2f;
    [Tooltip("판정 지속 시간")]
    public float hitDuration = 0.1f;
    [Tooltip("이 타격의 데미지 (0이면 baseDamage 사용)")]
    public int damage = 0;
    [Tooltip("히트박스 위치 오프셋 (타격마다 다르게 설정 가능)")]
    public Vector3 hitBoxOffset = new Vector3(0f, 0f, 1f);
}

[CreateAssetMenu(fileName = "ComboAttack", menuName = "Monster/ComboAttack")]
public class MonsterComboAttackSO : MonsterSkillData
{
    [Header("공통 데미지")]
    public int baseDamage = 10;
    [Range(0f, 1f)]
    public float criticalChance = 0.1f;
    public float criticalMultiplier = 1.5f;
    public float knockbackForce = 5f;

    [Header("타이밍")]
    [Tooltip("공격 예고(번쩍임) 지속 시간")]
    public float telegraphDuration = 0.4f;
    [Tooltip("콤보 전체 지속 시간")]
    public float attackDuration = 1.8f;

    [Header("공통 히트박스")]
    public HitBoxShape hitBoxShape = HitBoxShape.Sphere;
    public float hitBoxRadius = 1.2f;

    [Header("타격 이벤트 목록")]
    public List<EnemyHitEvent> hits = new List<EnemyHitEvent>
    {
        new EnemyHitEvent { hitStartTime = 0.2f, hitDuration = 0.1f },
        new EnemyHitEvent { hitStartTime = 0.7f, hitDuration = 0.1f },
        new EnemyHitEvent { hitStartTime = 1.2f, hitDuration = 0.1f },
    };

    public override EnemyAction CreateAction(Enemy enemy)
    {
        return new EnemyComboAttack(this, enemy);
    }
}
