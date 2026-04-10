public class PlayerUltimateState : IPlayerState
{
    private readonly PlayerController _playerController;

    public bool IsComplete { get; private set; }

    public PlayerUltimateState(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void Enter()
    {
        IsComplete = false;
        _playerController.Input.UltimateBuffer.Consume();
        _playerController.FormManager.CurrentForm.BeginUltimate();
    }

    public void Update()
    {
        _playerController.FormManager.CurrentForm.UpdateUltimate(out bool isComplete);
        IsComplete = isComplete;
    }

    public void Exit()
    {
        _playerController.FormManager.CurrentForm.EndUltimate();
        _playerController.StartUltimateCooldown();
    }
}
