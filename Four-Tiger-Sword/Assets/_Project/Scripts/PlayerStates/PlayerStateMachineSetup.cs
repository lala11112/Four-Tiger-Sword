public class PlayerStateMachineSetup
{
    private readonly PlayerController _playerController;

    public PlayerStateMachineSetup(PlayerController playerController) {_playerController = playerController;}

    public StateMachine Build()
    {
        var stateMachine = new StateMachine();
        var idle = new PlayerIdleState(_playerController);
        // Use the same Idle transitions while the landing animation plays.
        var landingIdle = new PlayerIdleState(_playerController, playLandingAnimation: true);
        var move = new PlayerRunState(_playerController);
        var jump = new PlayerJumpState(_playerController);
        var fall = new PlayerFallState(_playerController);
        var dash = new PlayerDashState(_playerController);
        var attack = new PlayerAttackState(_playerController);
        var airAttack = new PlayerAirAttackState(_playerController);
        var skill = new PlayerSkillState(_playerController);
        var ultimate = new PlayerUltimateState(_playerController);
        var counter = new PlayerCounterState(_playerController);
        var switchAttack = new PlayerSwitchAttackState(_playerController);
        _playerController.SwitchAttackState = switchAttack;
        var die = new PlayerDieState(_playerController);
        var hit = new PlayerHitState(_playerController);
        var stagger = new PlayerStaggerState(_playerController);
        var knockback = new PlayerKnockbackState(_playerController);
        var knockdown = new PlayerKnockdownState(_playerController);

        // The F reservation wins over locomotion and normal actions; death/hit reactions still win.
        ChainSkillTransitions(stateMachine, idle, move, attack, dash, switchAttack);
        GroundTransitions(stateMachine, idle, move, jump, fall, dash, attack);
        AirTransitions(stateMachine, idle, landingIdle, move, jump, fall);
        DashTransitions(stateMachine, idle, move, dash);
        AttackTransitions(stateMachine, idle, move, jump, attack, dash, skill, ultimate);
        AirAttackTransitions(stateMachine, idle, move, jump, fall, airAttack);
        SkillTransitions(stateMachine, idle, move, skill, jump, attack, ultimate);
        UltimateTransitions(stateMachine, idle, move, ultimate, attack, skill);
        CounterTransitions(stateMachine, idle, move, counter);
        stateMachine.AddTransition(switchAttack, idle, () => switchAttack.IsComplete
            && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(switchAttack, move, () => switchAttack.IsComplete
            && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
        AnyTransitions(stateMachine, die);
        foreach (var reaction in new PlayerHitState[] { hit, stagger, knockback, knockdown })
            HitReactionTransitions(stateMachine, reaction, idle, move, fall,
                jump, dash, attack, airAttack, skill, ultimate, switchAttack);
        stateMachine.AddTransition(attack, counter, () =>
            _playerController.FormManager.CurrentForm is EarthForm earth && earth.GuardCounterReady);
        stateMachine.ChangeState(idle);
        return stateMachine;    
    }

    //지상 이동
    private void GroundTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerRunState move, PlayerJumpState jump, PlayerFallState fall, PlayerDashState dash, PlayerAttackState attack)
    {
        stateMachine.AddTransition(idle, move, () => _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
        stateMachine.AddTransition(move, idle, () => _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
    }

    //점프 및 공중
    private void AirTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerIdleState landingIdle,
        PlayerRunState move, PlayerJumpState jump, PlayerFallState fall)
    {
        // 코요테 타임 포함한 점프 조건
        stateMachine.AddTransition(idle, jump, () => _playerController.Input.JumpBuffer.IsActive && _playerController.CanJump());
        stateMachine.AddTransition(move, jump, () => _playerController.Input.JumpBuffer.IsActive && _playerController.CanJump());
        stateMachine.AddTransition(fall, jump, () => _playerController.Input.JumpBuffer.IsActive && _playerController.CanJump());

        stateMachine.AddTransition(idle, fall, () => !_playerController.IsGround() && _playerController.VerticalVelocity < 0f);
        stateMachine.AddTransition(move, fall, () => !_playerController.IsGround());
        stateMachine.AddTransition(jump, fall, () => _playerController.VerticalVelocity < 0f);

        // 점프 중 착지 (올라가다 바닥에 닿는 엣지케이스)
        //stateMachine.AddTransition(jump, idle, () => _playerController.IsGround() && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        //ㅉstateMachine.AddTransition(jump, move, () => _playerController.IsGround() && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);

        stateMachine.AddTransition(fall, landingIdle, () => _playerController.IsGround() && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(fall, move, () => _playerController.IsGround() && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    //대시
    private void DashTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerRunState move, PlayerDashState dash)
    {
        stateMachine.AddTransition(idle, dash, () => _playerController.Input.DashBuffer.IsActive && _playerController.CanDash);
        stateMachine.AddTransition(move, dash, () => _playerController.Input.DashBuffer.IsActive && _playerController.CanDash);
        stateMachine.AddTransition(dash, idle, () => dash.IsDashComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(dash, move, () => dash.IsDashComplete && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    private void AttackTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerRunState move, PlayerJumpState jump, PlayerAttackState attack, PlayerDashState dash, PlayerSkillState skill, PlayerUltimateState ultimate)
    {
        stateMachine.AddTransition(idle, attack, () => _playerController.Input.AttackBuffer.IsActive);
        stateMachine.AddTransition(move, attack, () => _playerController.Input.AttackBuffer.IsActive);

        // 공격 도중 점프 캔슬
        stateMachine.AddTransition(attack, jump, () => _playerController.Input.JumpBuffer.IsActive && _playerController.CanJump());
        stateMachine.AddTransition(attack, dash, () => _playerController.Input.DashBuffer.IsActive && _playerController.CanDash);

        // Other combat actions use their own gate and take priority over movement or Idle.
        stateMachine.AddTransition(attack, skill, () => attack.CanCancelToAction
            && attack.HasSkillRequest && _playerController.CanSkill);
        stateMachine.AddTransition(attack, ultimate, () => attack.CanCancelToAction
            && _playerController.Input.UltimateBuffer.IsActive && _playerController.CanUltimate);

        stateMachine.AddTransition(attack, move, () => attack.CanCancelToMove
            && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);

        stateMachine.AddTransition(attack, idle, () => attack.IsAttackComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(attack, move, () => attack.IsAttackComplete && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    private void AirAttackTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerRunState move, PlayerJumpState jump, PlayerFallState fall, PlayerAirAttackState airAttack)
    {
        stateMachine.AddTransition(jump, airAttack, () => _playerController.Input.AttackBuffer.IsActive);
        stateMachine.AddTransition(fall, airAttack, () => _playerController.Input.AttackBuffer.IsActive);

        stateMachine.AddTransition(airAttack, idle, () => airAttack.IsComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(airAttack, move, () => airAttack.IsComplete && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    private void SkillTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerRunState move,
        PlayerSkillState skill, PlayerJumpState jump, PlayerAttackState attack, PlayerUltimateState ultimate)
    {
        stateMachine.AddTransition(idle, skill, () => _playerController.Input.SkillBuffer.IsActive && _playerController.CanSkill);
        stateMachine.AddTransition(move, skill, () => _playerController.Input.SkillBuffer.IsActive && _playerController.CanSkill);

        stateMachine.AddTransition(skill, jump, () => _playerController.Input.JumpBuffer.IsActive && _playerController.CanJump());

        stateMachine.AddTransition(skill, ultimate, () => skill.CanCancelToAction
            && _playerController.Input.UltimateBuffer.IsActive && _playerController.CanUltimate);
        stateMachine.AddTransition(skill, attack, () => skill.CanCancelToAction
            && _playerController.Input.AttackBuffer.IsActive);
        stateMachine.AddTransition(skill, move, () => skill.CanCancelToMove
            && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);

        stateMachine.AddTransition(skill, idle, () => skill.IsComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
    }

    private void UltimateTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerRunState move,
        PlayerUltimateState ultimate, PlayerAttackState attack, PlayerSkillState skill)
    {
        stateMachine.AddTransition(idle, ultimate, () => _playerController.Input.UltimateBuffer.IsActive && _playerController.CanUltimate);
        stateMachine.AddTransition(move, ultimate, () => _playerController.Input.UltimateBuffer.IsActive && _playerController.CanUltimate);

        stateMachine.AddTransition(ultimate, skill, () => ultimate.CanCancelToAction
            && _playerController.Input.SkillBuffer.IsActive && _playerController.CanSkill);
        stateMachine.AddTransition(ultimate, attack, () => ultimate.CanCancelToAction
            && _playerController.Input.AttackBuffer.IsActive);
        stateMachine.AddTransition(ultimate, move, () => ultimate.CanCancelToMove
            && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);

        stateMachine.AddTransition(ultimate, idle, () => ultimate.IsComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
    }

    private void ChainSkillTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerRunState move,
        PlayerAttackState attack, PlayerDashState dash, PlayerSwitchAttackState switchAttack)
    {
        bool Requested() => _playerController.Input.ChainSkillBuffer.IsActive
            && _playerController.FormManager.CanUseReadyChainSkill;
        stateMachine.AddTransition(idle, switchAttack, Requested);
        stateMachine.AddTransition(move, switchAttack, Requested);
        stateMachine.AddTransition(attack, switchAttack, () => attack.CanChainSwitch && Requested());
        stateMachine.AddTransition(dash, switchAttack, () => dash.IsDashComplete && Requested());
    }

    private void CounterTransitions(StateMachine stateMachine, PlayerIdleState idle, PlayerRunState move, PlayerCounterState counter)
    {
        // 반격 모션 종료 후 복귀
        stateMachine.AddTransition(counter, idle, () => counter.IsComplete && _playerController.Input.MoveInput.sqrMagnitude <= 0.01f);
        stateMachine.AddTransition(counter, move, () => counter.IsComplete && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
    }

    private void HitReactionTransitions(StateMachine stateMachine, PlayerHitState reaction,
        PlayerIdleState idle, PlayerRunState move, PlayerFallState fall,
        PlayerJumpState jump, PlayerDashState dash, PlayerAttackState attack,
        PlayerAirAttackState airAttack, PlayerSkillState skill, PlayerUltimateState ultimate,
        PlayerSwitchAttackState switchAttack)
    {
        // 사망 전이 이후 등록되어 치명타는 피격 모션보다 사망을 우선합니다.
        stateMachine.AddAnyTransition(reaction, () => _playerController.PendingHitReaction == reaction.Reaction);
        // Buffered actions exit directly, without an extra Idle/Run frame consuming recovery inputs.
        bool ReadyOnGround() => reaction.IsComplete && _playerController.IsGround();
        stateMachine.AddTransition(reaction, switchAttack, () => ReadyOnGround()
            && _playerController.Input.ChainSkillBuffer.IsActive && _playerController.FormManager.CanUseReadyChainSkill);
        stateMachine.AddTransition(reaction, jump, () => reaction.IsComplete
            && _playerController.Input.JumpBuffer.IsActive && _playerController.CanJump());
        stateMachine.AddTransition(reaction, dash, () => ReadyOnGround()
            && _playerController.Input.DashBuffer.IsActive && _playerController.CanDash);
        stateMachine.AddTransition(reaction, airAttack, () => reaction.IsComplete && !_playerController.IsGround()
            && _playerController.Input.AttackBuffer.IsActive);
        stateMachine.AddTransition(reaction, attack, () => ReadyOnGround() && _playerController.Input.AttackBuffer.IsActive);
        stateMachine.AddTransition(reaction, skill, () => ReadyOnGround()
            && _playerController.Input.SkillBuffer.IsActive && _playerController.CanSkill);
        stateMachine.AddTransition(reaction, ultimate, () => ReadyOnGround()
            && _playerController.Input.UltimateBuffer.IsActive && _playerController.CanUltimate);
        stateMachine.AddTransition(reaction, fall, () => reaction.IsComplete && !_playerController.IsGround());
        stateMachine.AddTransition(reaction, move, () => reaction.IsComplete
            && _playerController.Input.MoveInput.sqrMagnitude > 0.01f);
        stateMachine.AddTransition(reaction, idle, () => reaction.IsComplete);
    }

    private void AnyTransitions(StateMachine stateMachine, PlayerDieState die)
    {
        stateMachine.AddAnyTransition(die, () => _playerController.StatManager.CurrentHp <= 0);
    }
}
