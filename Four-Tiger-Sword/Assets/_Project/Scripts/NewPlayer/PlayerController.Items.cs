using UnityEngine;

public partial class PlayerController
{
    public PlayerItemRuntime ItemRuntime { get; private set; }
    public bool HasPendingSwitchAttack => _pendingSwitchAttack != null
        && FormManager?.CurrentForm == _pendingSwitchAttack && StatManager != null && StatManager.CurrentHp > 0f;

    private BaseForm _pendingSwitchAttack;
    internal PlayerSwitchAttackState SwitchAttackState { get; set; }

    public bool TryBeginDash() => CanDash;

    internal void QueueSwitchAttack(BaseForm form) => _pendingSwitchAttack = form;

    internal BaseForm TakePendingSwitchAttack()
    {
        var form = HasPendingSwitchAttack ? _pendingSwitchAttack : null;
        _pendingSwitchAttack = null;
        return form;
    }

    internal void StartPendingSwitchAttack()
    {
        if (HasPendingSwitchAttack && SwitchAttackState != null)
            StateMachine.ChangeState(SwitchAttackState);
    }

    public void CancelPendingSwitchAttack() => _pendingSwitchAttack = null;
}
