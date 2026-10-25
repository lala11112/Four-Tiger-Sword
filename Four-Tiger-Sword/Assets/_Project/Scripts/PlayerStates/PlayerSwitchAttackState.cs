/// <summary>Automatic attack from a committed form switch. It never spends E SP or starts E cooldown.</summary>
public sealed class PlayerSwitchAttackState : IPlayerState
{
    private readonly PlayerController _player;
    private BaseForm _form;
    public bool IsComplete { get; private set; }

    public PlayerSwitchAttackState(PlayerController player) => _player = player;

    public void Enter()
    {
        _form = _player.TakePendingSwitchAttack();
        IsComplete = _form == null;
        ClearInputs();
        _form?.BeginSwitchAttack();
    }

    public void Update()
    {
        ClearInputs();
        if (_form == null) return;
        _form.UpdateSwitchAttack(out bool complete);
        IsComplete = complete;
    }

    public void Exit() => Cancel();

    public void Cancel()
    {
        _form?.EndSwitchAttack();
        _form = null;
        IsComplete = true;
    }

    private void ClearInputs()
    {
        _player.Input.AttackBuffer.Consume();
        _player.Input.SkillBuffer.Consume();
        _player.Input.UltimateBuffer.Consume();
        _player.Input.DashBuffer.Consume();
        _player.Input.JumpBuffer.Consume();
        _player.Input.ConsumeFormInput();
    }
}
