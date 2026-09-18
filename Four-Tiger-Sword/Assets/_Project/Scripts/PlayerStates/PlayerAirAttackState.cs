using UnityEngine;

public class PlayerAirAttackState : IPlayerState
{
    private readonly PlayerController _playerController;
    private enum Phase { Start, Loop, Finish }
    private Phase _phase;
    private LayerMask _savedExcludeLayers;

    public bool IsComplete { get; private set; }
    public bool IsFinishing => _phase == Phase.Finish;

    public PlayerAirAttackState(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void Enter()
    {
        IsComplete = false;
        _phase = Phase.Start;
        _playerController.Input.AttackBuffer.Consume();
        _playerController.VerticalVelocity = 0f;
        _savedExcludeLayers = _playerController.Controller.excludeLayers;
        _playerController.Controller.excludeLayers |= LayerMask.GetMask("Enemy");
        _playerController.FormManager.CurrentForm.BeginAirAttack();
    }

    public void Update()
    {
        if (_phase == Phase.Start)
        {
            // 시작 모션 동안 공중에 머문 뒤 하강합니다.
            _playerController.FormManager.CurrentForm.UpdateAirAttackStart(out bool startComplete);
            if (!startComplete) return;

            _phase = Phase.Loop;
            _playerController.VerticalVelocity = -_playerController.AirAttackDescentSpeed;
            _playerController.FormManager.CurrentForm.BeginAirAttackLoop();
        }

        if (_phase == Phase.Loop)
        {
            _playerController.Controller.Move(new Vector3(0f, _playerController.VerticalVelocity, 0f) * Time.deltaTime);

            if (_playerController.IsGround())
            {
                _phase = Phase.Finish;
                _playerController.VerticalVelocity = 0f;
                _playerController.FormManager.CurrentForm.BeginAirAttackFinish();
            }
        }
        else
        {
            _playerController.FormManager.CurrentForm.UpdateAirAttack(out bool isComplete);
            IsComplete = isComplete;
        }
    }

    public void Exit()
    {
        _playerController.FormManager.CurrentForm.EndAirAttack();
        _playerController.Controller.excludeLayers = _savedExcludeLayers;
    }
}
