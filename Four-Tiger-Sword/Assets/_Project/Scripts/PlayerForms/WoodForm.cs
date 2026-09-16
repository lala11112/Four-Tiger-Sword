using UnityEngine;

public class WoodForm : BaseForm
{
    public override ElementType Element => ElementType.ELEMENT_WOOD;
    public override float SkillSpCost => 180f;
    public override float SkillCooldown => 12f;
    public WoodForm(WeaponActionDataSO data) : base(data) { }
    public override void Equip(PlayerController player)
    {
        base.Equip(player);
        PlayerUIManager.Instance?.WoodElement?.SetActive(false);
    }
    public override void Unequip(PlayerController player)
        => PlayerUIManager.Instance?.WoodElement?.SetActive(true);
    public override void BeginSkill()
    {
        base.BeginSkill();
        Effects.StartCoroutine(Effects.Field(FirstHitTime(_weaponActionData.SkillSteps[0]) / Mathf.Max(0.1f, AttackSpeed),
            _weaponActionData.FieldDuration, _weaponActionData.AreaRadius, EffectHit(0.5f), 0f));
        // TODO: 가시 덩굴 모션.
        // _playerController.Animator.CrossFade("ThornField", 0.1f);
    }
    public override void BeginUltimate()
    {
        base.BeginUltimate();
        Effects.StartCoroutine(Effects.Field(FirstHitTime(_weaponActionData.UltimateSteps[0]) / Mathf.Max(0.1f, AttackSpeed),
            _weaponActionData.FieldDuration, _weaponActionData.AreaRadius, EffectHit(0f), _weaponActionData.HealMaxHpPerSecond));
        // TODO: 태초의 숲 모션.
        // _playerController.Animator.CrossFade("PrimordialForest", 0.1f);
    }
    protected override void OnHitEnemy(GameObject enemy)
    {
        base.OnHitEnemy(enemy);
        enemy.GetComponent<EnemyStackManager>()?.AddStack(StackType.Wood);
        if (_currentAction == ActionType.Ultimate)
        {
            var effects = enemy.GetComponent<StatusEffectHandler>() ?? enemy.AddComponent<StatusEffectHandler>();
            effects.Apply(new AirborneEffect(1.5f));
        }
    }
}
