using UnityEngine;

public class FireForm : BaseForm
{
    public override ElementType Element => ElementType.ELEMENT_FIRE;
    public override float SkillSpCost => 200f;
    public override float SkillCooldown => 8f;
    public FireForm(WeaponActionDataSO data) : base(data) { }
    public override void Equip(PlayerController player)
    {
        base.Equip(player);
        PlayerUIManager.Instance?.FireElement?.SetActive(false);
        player.WeaponManager?.FireWeapon?.SetActive(true);
    }
    public override void Unequip(PlayerController player)
    {
        PlayerUIManager.Instance?.FireElement?.SetActive(true);
        player.WeaponManager?.FireWeapon?.SetActive(false);
    }
    protected override void OnHitEnemy(GameObject enemy)
    {
        base.OnHitEnemy(enemy);
        TargetEffects(enemy).AddFireStack(EffectHit(2.5f), _weaponActionData.FireExplosionDelay);
    }
    public override void BeginSkill()
    {
        base.BeginSkill();
        float impactDelay = FirstHitTime(_weaponActionData.SkillSteps[0]) / Mathf.Max(0.1f, AttackSpeed);
        Effects.StartCoroutine(Effects.DelayedExplosion(impactDelay, 1f, _weaponActionData.AreaRadius, EffectHit(3.5f)));
        // TODO: 폭렬참 내리찍기 모션은 기존 연결 모션을 유지하고 신규 모션 제작 후 교체.
    }
}
