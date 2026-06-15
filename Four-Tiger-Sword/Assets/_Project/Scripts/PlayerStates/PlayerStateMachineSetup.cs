public class PlayerStateMachineSetup
{
    private readonly PlayerController _playerController;

    public PlayerStateMachineSetup(PlayerController playerController) {_playerController = playerController;}

    public StateMachine Build()
    {
        var stateMachine = new StateMachine();
        var idle = new PlayerIdleState(_playerController);
        var move = new PlayerMoveState(_playerController);
        var jump = new PlayerJumpState(_playerController);
        var fall = new PlayerFallState(_playerController);
        var dash = new PlayerDashState(_playerController);
        var run = new PlayerRunState(_playerController);
        var attack = new PlayerAttackState(_playerController);
        var airAttack = new PlayerAirAttackState(_playerController);
        var skill = new PlayerSkillState(_playerController);
        var ultimate = new PlayerUltimateState(_playerController);
        var parry = new PlayerParryState(_playerController);
        var counter = new PlayerCounterState(_playerController);
        var die = new PlayerDieState(_playerController);

        GroundTransitions(stateMachine, idle, move, jump, fall, dash, attack);
        AirTransitions(stateMachine, idle, move, jump, fall, run);
        DashTransitions(stateMachine, idle, move, dash, run);
        AttackTransitions(stateMachine, idle, move, jump, attack, parry, dash);
        AirAttackTransitions(stateMachine, idle, move, jump, fall, airAttack);
        SkillTransitions(stateMachine, idle, move, skill, jump);
        UltimateTransitions(stateMachine, idle, move, ultimate);
        RunTransitions(stateMachine, idle, move, run, dash, attack);
        ParryTransitions(stateMachine, idle, move, run, parry, counter);
        CounterTransitions(stateMachine, idle, move, counter);
        AnyTransitions(stateMachine, die);
        stateMachine.ChangeState(idle);
        return stateMachine;    
    }

    //지상 이동
    private void GroundTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerMoveState move, PlayerJumpState jump, PlayerFallState fall, PlayerDashState dash, PlayerAttackState attack)
    {
        stateMachine.AddTransition(idle, move, () => _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
        stateMachine.AddTransition(move, idle, () => _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
    }

    //점프 및 공중
    private void AirTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerMoveState move, PlayerJumpState jump, PlayerFallState fall, PlayerRunState run)
    {
        // 코요테 타임 포함한 점프 조건
        stateMachine.AddTransition(idle, jump, () => _playerController.Input.JumpBuffer.IsActive && _playerController.CanJump());
        stateMachine.AddTransition(move, jump, () => _playerController.Input.JumpBuffer.IsActive && _playerController.CanJump());
        stateMachine.AddTransition(run, jump, () => _playerController.Input.JumpBuffer.IsActive && _playerController.CanJump());
        stateMachine.AddTransition(fall, jump, () => _playerController.Input.JumpBuffer.IsActive && _playerController.CanJump());

        stateMachine.AddTransition(idle, fall, () => !_playerController.IsGround() && _playerController.VerticalVelocity < 0f);
        stateMachine.AddTransition(move, fall, () => !_playerController.IsGround());
        stateMachine.AddTransition(run, fall, () => !_playerController.IsGround());
        stateMachine.AddTransition(jump, fall, () => _playerController.VerticalVelocity < 0f);

        // 점프 중 착지 (올라가다 바닥에 닿는 엣지케이스)
        //stateMachine.AddTransition(jump, idle, () => _playerController.IsGround() && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        //ㅉstateMachine.AddTransition(jump, move, () => _playerController.IsGround() && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);

        stateMachine.AddTransition(fall, run, () => _playerController.IsGround() && _playerController.Input.MoveInput.sqrMagnitude > 0.01f && _playerController.Input.IsDashHeld && _playerController.CanRun);
        stateMachine.AddTransition(fall, idle, () => _playerController.IsGround() && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(fall, move, () => _playerController.IsGround() && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    //대시
    private void DashTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerMoveState move, PlayerDashState dash, PlayerRunState run)
    {
        stateMachine.AddTransition(idle, dash, () => _playerController.Input.DashBuffer.IsActive && _playerController.CanDash);
        stateMachine.AddTransition(move, dash, () => _playerController.Input.DashBuffer.IsActive && _playerController.CanDash);
        // 달리기 전환: 대시 완료 후 대쉬 키 홀드 + 방향 입력 + 스테미나 있을 시
        stateMachine.AddTransition(dash, run, () => dash.IsDashComplete && _playerController.Input.IsDashHeld && _playerController.Input.MoveInput.sqrMagnitude > 0.01f && _playerController.CanRun);
        stateMachine.AddTransition(dash, idle, () => dash.IsDashComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(dash, move, () => dash.IsDashComplete && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    private void AttackTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerMoveState move, PlayerJumpState jump, PlayerAttackState attack, PlayerParryState parry, PlayerDashState dash)
    {
        stateMachine.AddTransition(idle, attack, () => _playerController.Input.AttackBuffer.IsActive);
        stateMachine.AddTransition(move, attack, () => _playerController.Input.AttackBuffer.IsActive);

        // 공격 도중 점프 캔슬
        stateMachine.AddTransition(attack, jump, () => _playerController.Input.JumpBuffer.IsActive && _playerController.CanJump());
        stateMachine.AddTransition(attack, parry, () => _playerController.Input.ParryBuffer.IsActive);
        stateMachine.AddTransition(attack, dash, () => _playerController.Input.DashBuffer.IsActive && _playerController.CanDash);

        stateMachine.AddTransition(attack, idle, () => attack.IsAttackComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(attack, move, () => attack.IsAttackComplete && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    private void AirAttackTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerMoveState move, PlayerJumpState jump, PlayerFallState fall, PlayerAirAttackState airAttack)
    {
        stateMachine.AddTransition(jump, airAttack, () => _playerController.Input.AttackBuffer.IsActive);
        stateMachine.AddTransition(fall, airAttack, () => _playerController.Input.AttackBuffer.IsActive);

        stateMachine.AddTransition(airAttack, idle, () => airAttack.IsComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(airAttack, move, () => airAttack.IsComplete && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    private void SkillTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerMoveState move, PlayerSkillState skill, PlayerJumpState jump)
    {
        stateMachine.AddTransition(idle, skill, () => _playerController.Input.SkillBuffer.IsActive && _playerController.CanSkill);
        stateMachine.AddTransition(move, skill, () => _playerController.Input.SkillBuffer.IsActive && _playerController.CanSkill);

        stateMachine.AddTransition(skill, jump, () => _playerController.Input.JumpBuffer.IsActive && _playerController.CanJump());

        stateMachine.AddTransition(skill, idle, () => skill.IsComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(skill, move, () => skill.IsComplete && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    private void UltimateTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerMoveState move, PlayerUltimateState ultimate)
    {
        stateMachine.AddTransition(idle, ultimate, () => _playerController.Input.UltimateBuffer.IsActive && _playerController.CanUltimate);
        stateMachine.AddTransition(move, ultimate, () => _playerController.Input.UltimateBuffer.IsActive && _playerController.CanUltimate);

        stateMachine.AddTransition(ultimate, idle, () => ultimate.IsComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(ultimate, move, () => ultimate.IsComplete && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    //달리기
    private void RunTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerMoveState move, PlayerRunState run, PlayerDashState dash, PlayerAttackState attack)
    {
        stateMachine.AddTransition(run, dash, () => _playerController.Input.DashBuffer.IsActive && _playerController.CanDash);
        stateMachine.AddTransition(run, attack, () => _playerController.Input.AttackBuffer.IsActive);
        stateMachine.AddTransition(run, move, () => (!_playerController.Input.IsDashHeld || !_playerController.CanRun) && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
        stateMachine.AddTransition(run, idle, () => _playerController.Input.MoveInput.sqrMagnitude <= 0.01f || !_playerController.CanRun);
    }

    private void ParryTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerMoveState move, PlayerRunState run, PlayerParryState parry, PlayerCounterState counter)
    {
        // Idle/Move/Run 에서 패링 진입 (CanParry = 쿨타임 + 지상 + 예고 활성)
        stateMachine.AddTransition(idle, parry, () => _playerController.Input.ParryBuffer.IsActive && _playerController.CanParry);
        stateMachine.AddTransition(move, parry, () => _playerController.Input.ParryBuffer.IsActive && _playerController.CanParry);
        stateMachine.AddTransition(run,  parry, () => _playerController.Input.ParryBuffer.IsActive && _playerController.CanParry);

        // 반격 윈도우 중 공격 입력 → 반격 상태 전환 (idle/move 복귀보다 우선 평가)
        stateMachine.AddTransition(parry, counter, () => parry.IsCounterWindowActive && _playerController.Input.AttackBuffer.IsActive && !_playerController.IsKnockbacking);

        // 반격 미사용 시 모션 종료 후 복귀
        stateMachine.AddTransition(parry, idle, () => parry.IsComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(parry, move, () => parry.IsComplete && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    private void CounterTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerMoveState move, PlayerCounterState counter)
    {
        // 반격 모션 종료 후 복귀
        stateMachine.AddTransition(counter, idle, () => counter.IsComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(counter, move, () => counter.IsComplete && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    private void AnyTransitions(StateMachine stateMachine, PlayerDieState die)
    {
        stateMachine.AddAnyTransition(die, () => _playerController.StatManager.CurrentHp <= 0);
    }
}
