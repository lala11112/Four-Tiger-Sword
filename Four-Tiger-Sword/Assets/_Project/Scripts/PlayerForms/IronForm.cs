using UnityEngine;

public class IronForm : BaseForm
{
    public override ElementType Element => ElementType.ELEMENT_GOLD;
    public override float SkillSpCost => 200f;
    public override float SkillCooldown => 10f;
    protected override bool DealsTrueDamage => _currentAction == ActionType.Ultimate;
    public IronForm(WeaponActionDataSO data) : base(data) { }
    public override void Equip(PlayerController player)
    {
        base.Equip(player);
        PlayerUIManager.Instance?.IronElement?.SetActive(false);
    }
    public override void Unequip(PlayerController player)
        => PlayerUIManager.Instance?.IronElement?.SetActive(true);
    protected override void OnHitEnemy(GameObject enemy)
    {
        base.OnHitEnemy(enemy);
        if (_currentAction == ActionType.Attack) TargetEffects(enemy).ApplyArmorShred(false);
        if (_currentAction == ActionType.Skill) TargetEffects(enemy).ApplyArmorShred(true);
        // TODO: 집중 찌르기/철쇄 격파/금강 파동포 Animator 모션 제작 후 연결.
        // _playerController.Animator.CrossFade("IronLaser", 0.1f);
    }
}
