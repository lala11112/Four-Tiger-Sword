using UnityEngine;

[CreateAssetMenu(fileName = "BossChargeAttack", menuName = "Monster/Boss Charge Attack")]
public sealed class MonsterBossChargeAttackSO : MonsterSkillData
{
    public override bool SupportsBossExecutionPolicy => true;

    [Header("Charge animation")]
    [Min(0f), Tooltip("Attack timeline seconds to reach the held pose.")]
    public float chargePoseTime = .35f;
    [Range(0f, 1f), Tooltip("Normalized animation position held during the charge.")]
    public float chargePoseNormalized = .25f;
    [Min(0f), Tooltip("Actor-clock seconds spent holding the charge; unaffected by attackSpeed.")]
    public float chargeSeconds = 1.2f;
    public bool interruptOnKnockback = true;

    [Header("Release timeline")]
    [Min(0f)] public float releaseHitDelay = .18f;
    [Min(.01f)] public float hitDuration = .16f;
    [Min(0f)] public float recoveryTime = .8f;

    [Header("Damage")]
    public float damage = 2.4f;
    [Min(0f)] public float knockbackForce = 8f;

    [Header("Hitbox")]
    public HitBoxShape hitBoxShape = HitBoxShape.Box;
    public Vector3 hitBoxOffset = new Vector3(0f, 1f, 2f);
    public float hitBoxRadius = 1.5f;
    public Vector3 hitBoxSize = new Vector3(3f, 2f, 4f);
    public float hitBoxHeight = 2f;

    public override EnemyAction CreateAction(Enemy enemy) => new EnemyBossChargeAttack(this, enemy);

    private void OnValidate()
    {
        foreach (string issue in BossAttackPolicy.ValidateSkill(this))
            Debug.LogWarning($"{name}: {issue}", this);
    }
}
