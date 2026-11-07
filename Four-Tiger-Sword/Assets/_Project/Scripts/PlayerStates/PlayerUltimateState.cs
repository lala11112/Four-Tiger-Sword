public class PlayerUltimateState : IPlayerState
{
    private readonly PlayerController _playerController;
    private IForm _form;

    public bool IsComplete { get; private set; }
    public bool CanCancelToMove => IsComplete
        || _form is BaseForm form && form.CanCancelCurrentActionToMove;
    public bool CanCancelToAction => IsComplete
        || _form is BaseForm form && form.CanCancelCurrentActionToAction;

    public PlayerUltimateState(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void Enter()
    {
        _form = _playerController.FormManager.CurrentForm;
        IsComplete = false;
        _playerController.Input.UltimateBuffer.Consume();
        _form.BeginUltimate();
    }

    public void Update()
    {
        _form.UpdateUltimate(out bool isComplete);
        IsComplete = isComplete;
    }

    public void Exit()
    {
        _form.EndUltimate();
        // 쿨타임은 BaseForm.EndUltimate() 내부에서 처리됩니다.
    }
}
