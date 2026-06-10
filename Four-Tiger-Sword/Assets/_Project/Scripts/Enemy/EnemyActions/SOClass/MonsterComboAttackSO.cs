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
    public float damage = 0;
    [Tooltip("히트박스 위치 오프셋 (타격마다 다르게 설정 가능)")]
    public Vector3 hitBoxOffset = new Vector3(0f, 0f, 1f);
}

[CreateAssetMenu(fileName = "ComboAttack", menuName = "Monster/ComboAttack")]
public class MonsterComboAttackSO : MonsterSkillData
{
    [Header("공통 데미지")]
    public float baseDamage = 10;
    public float knockbackForce = 5f;

    [Header("타이밍")]
    // telegraphDuration은 부모 MonsterSkillData에서 관리합니다.
    // 콤보 공격: 첫 번째 타격 전 짧은 번쩍임 시간으로 설정 (hits[0].hitStartTime 이하).
    // 예고가 끝난 뒤에도 OnHitParried의 타이머 리셋으로 모든 후속 타수를 자동으로 막을 수 있습니다.
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
