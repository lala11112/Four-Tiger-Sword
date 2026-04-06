using UnityEngine;

public abstract class BaseForm : IForm
{
    protected PlayerController _playerController;
    protected WeaponActionDataSO _weaponActionData;

    protected int _comboStep = 0;
    protected float _timer = 0;

    public BaseForm(WeaponActionDataSO weaponActionData)
    {
        _weaponActionData = weaponActionData;
    }
    


    public virtual void Equip(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public virtual void Unequip(PlayerController playerController) {}

    public virtual void BeginAttack()
    {
        _comboStep = 0;
        PlayCombo();
    }
    public abstract void UpdateAttack(out bool isComplete);
    public virtual void EndAttack()
    {
        _comboStep = 0;
    }

    protected void PlayCombo()
    {
        _timer = 0;
        _playerController.Input.AttackBuffer.Consume();

        WeaponActionData step = _weaponActionData.ComboSteps[_comboStep];

        Debug.Log("공격이름 : " + step.AnimationName + " 공격타수 : " + _comboStep);

        //_playerController.Animator.CrossFade(step.AnimationName);
    }
}
