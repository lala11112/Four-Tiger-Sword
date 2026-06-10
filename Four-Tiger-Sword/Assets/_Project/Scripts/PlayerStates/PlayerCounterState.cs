public class PlayerCounterState : IPlayerState
{
    private readonly PlayerController _playerController;

    /// <summary>반격 모션이 끝났는지 여부. PlayerStateMachineSetup에서 복귀 조건에 사용합니다.</summary>
    public bool IsComplete { get; private set; }

    public PlayerCounterState(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void Enter()
    {
        IsComplete = false;
        _playerController.Input.AttackBuffer.Consume();
        _playerController.FormManager.CurrentForm.BeginCounter();
    }

    public void Update()
    {
        _playerController.FormManager.CurrentForm.UpdateCounter(out bool isComplete);
        IsComplete = isComplete;
    }

    public void Exit()
    {
        _playerController.FormManager.CurrentForm.EndCounter();
    }
}
