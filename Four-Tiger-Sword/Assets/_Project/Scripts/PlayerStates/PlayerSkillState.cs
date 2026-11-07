public class PlayerSkillState : IPlayerState
{
    private readonly PlayerController _playerController;
    private IForm _form;

    public bool IsComplete { get; private set; }
    public bool CanCancelToMove => IsComplete
        || _form is BaseForm form && form.CanCancelCurrentActionToMove;
    public bool CanCancelToAction => IsComplete
        || _form is BaseForm form && form.CanCancelCurrentActionToAction;

    public PlayerSkillState(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void Enter()
    {
        _form = _playerController.FormManager.CurrentForm;
        IsComplete = false;
        _playerController.Input.SkillBuffer.Consume();
        _form.BeginSkill();
    }

    public void Update()
    {
        _form.UpdateSkill(out bool isComplete);
        IsComplete = isComplete;
    }

    public void Exit()
    {
        _form.EndSkill();
        // 쿨타임은 BaseForm.EndSkill() 내부에서 처리됩니다.
    }
}
