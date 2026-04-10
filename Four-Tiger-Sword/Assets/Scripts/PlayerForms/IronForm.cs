using UnityEngine;

public class IronForm : BaseForm
{
    protected override DamageType FormElement => DamageType.Iron;

    public IronForm(WeaponActionDataSO weaponActionData) : base(weaponActionData) { }

    public override void UpdateAttack(out bool isComplete)
    {
        isComplete = false;

        if (_weaponActionData == null || _weaponActionData.ComboSteps.Count == 0)
        {
            isComplete = true;
            return;
        }

        _timer += Time.deltaTime;
        WeaponActionData currentStep = _weaponActionData.ComboSteps[_comboStep];

        ProcessHit(currentStep);

        Vector3 moveVelocity = _playerController.transform.forward * currentStep.ForwardThrust;
        moveVelocity.y = _playerController.VerticalVelocity;
        _playerController.Controller.Move(moveVelocity * Time.deltaTime);

        if (_timer >= currentStep.ComboTransitionTime && _playerController.Input.AttackBuffer.IsActive && _comboStep < _weaponActionData.ComboSteps.Count - 1)
        {
            _comboStep++;
            PlayCombo();
            return;
        }

        if (_timer >= currentStep.Duration)
        {
            isComplete = true;
        }
    }
}
