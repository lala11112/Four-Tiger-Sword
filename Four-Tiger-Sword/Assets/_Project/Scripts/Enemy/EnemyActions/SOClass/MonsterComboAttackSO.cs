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

    [Tooltip("Optional Animator state for this swing. Empty keeps the combo's animation.")]
    public string animationState;
    [Min(0f), Tooltip("Absolute action timeline time at which this swing's animation starts.")]
    public float animationStartTime;

    [Min(0f), Tooltip("이 타격에 맞춰 앞으로 내딛는 최대 거리. 0이면 제자리 공격입니다.")]
    public float stepDistance;
    [Min(0f), Tooltip("전진을 시작하는 절대 공격 타임라인 시간.")]
    public float stepStartTime;
    [Min(0f), Tooltip("전진 시간. 공격 속도가 바뀌어도 총 전진 거리는 유지됩니다.")]
    public float stepDuration = .3f;

    [Tooltip("Use this hit's push, stagger and player knockdown settings instead of the combo defaults.")]
    public bool overrideReaction;
    [Min(0f)] public float knockbackForce = 5f;
    public StaggerResistLevel staggerLevel = StaggerResistLevel.LOW;
    [Tooltip("An applied, unblocked hit knocks the player down. Dodge and parry still prevent it.")]
    public bool knockdown;
    [Min(0f)] public float downSeconds = .3f;
    [Min(0f)] public float getUpSeconds = .5f;

    [Tooltip("클립을 지정하면 이 타격의 공통 휘두름 사운드를 덮어씁니다.")]
    public CombatSoundSettings SwingAudio = new CombatSoundSettings();
    [Tooltip("클립을 지정하면 이 타격의 공통 명중 사운드를 덮어씁니다.")]
    public CombatSoundSettings HitAudio = new CombatSoundSettings();
}

[CreateAssetMenu(fileName = "ComboAttack", menuName = "Monster/ComboAttack")]
public class MonsterComboAttackSO : MonsterSkillData
{
    public override bool SupportsBossExecutionPolicy => true;

    [Header("공통 데미지")]
    public float baseDamage = 10;
    public float knockbackForce = 5f;

    [Header("공격 중 전진")]
    [Min(0f), Tooltip("전진할 때 대상과 유지할 최소 중심 거리. NavMesh 경계에서도 전진을 멈춥니다.")]
    public float stepStopDistance = 1.4f;

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
