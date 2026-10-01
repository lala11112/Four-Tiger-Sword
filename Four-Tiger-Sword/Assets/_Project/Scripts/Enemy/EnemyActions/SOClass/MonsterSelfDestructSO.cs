using UnityEngine;

[CreateAssetMenu(fileName = "SelfDestruct", menuName = "Monster/SelfDestruct")]
public class MonsterSelfDestructSO : MonsterSkillData
{
    [Header("자폭 발동 조건")]
    [Tooltip("체크하면 최대 HP 대비 비율, 해제하면 고정 HP를 사용합니다.")]
    public bool useHpRatio = true;
    [Range(0f, 1f)] public float hpRatioThreshold = 0.3f;
    [Min(0f)] public float hpThreshold = 300f;

    [Header("자폭 타이밍")]
    [Tooltip("발동 후 폭발까지의 게임 시간(초). attackSpeed와 무관합니다.")]
    [Min(0f)] public float explosionDelay = 3f;

    [Header("폭발 판정")]
    [Tooltip("기존 공격과 동일하게 ATK에 곱하는 피해 배율입니다.")]
    [Min(0f)] public float damage = 2f;
    [Min(0f)] public float knockbackForce = 5f;
    [Min(0.01f)] public float explosionRadius = 3f;
    public Vector3 explosionOffset = Vector3.zero;

    [Header("이펙트")]
    [Tooltip("대기 중 위치를 표시할 프리팹. 폭발/취소 시 제거됩니다.")]
    public GameObject warningEffectPrefab;
    [Tooltip("폭발 중심 기준 월드 좌표 오프셋. 지면 표시의 높이 조절에 사용합니다.")]
    public Vector3 warningEffectOffset = Vector3.zero;
    public Vector3 warningEffectScale = Vector3.one;
    public GameObject explosionEffectPrefab;
    [Min(0.1f)] public float explosionEffectLifetime = 5f;

    public bool ShouldTrigger(EnemyStat stat)
    {
        if (stat == null || stat.CurrentHp <= 0f) return false;
        float threshold = useHpRatio
            ? stat.MaxHp * Mathf.Clamp01(hpRatioThreshold) : Mathf.Max(0f, hpThreshold);
        return stat.CurrentHp <= threshold;
    }

    public override EnemyAction CreateAction(Enemy enemy) => new EnemySelfDestruct(this, enemy);
}
