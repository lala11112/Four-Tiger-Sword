public class PlayerSkillState : IPlayerState
{
    private readonly PlayerController _playerController;

    public bool IsComplete { get; private set; }

    public PlayerSkillState(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void Enter()
    {
        IsComplete = false;
        _playerController.Input.SkillBuffer.Consume();
        _playerController.FormManager.CurrentForm.BeginSkill();
    }

    public void Update()
    {
        _playerController.FormManager.CurrentForm.UpdateSkill(out bool isComplete);
        IsComplete = isComplete;
    }

    public void Exit()
    {
        _playerController.FormManager.CurrentForm.EndSkill();
        _playerController.StartSkillCooldown();
    }
}
