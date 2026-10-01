public class EnemySelfDestructState : IPlayerState
{
    private readonly Enemy _enemy;

    public EnemySelfDestructState(Enemy enemy) { _enemy = enemy; }

    public void Enter()
    {
        // 이전 상태의 Exit가 CurrentAction을 비운 다음 새 액션을 설정합니다.
        if (_enemy.CurrentAction?.IsActive == true) _enemy.CurrentAction.Exit();
        _enemy.HasSelectedAttack = false;
        _enemy.CurrentAction = _enemy.EnemyStat.MonsterSkillData.selfDestruct.CreateAction(_enemy);
        _enemy.CurrentAction.Enter();
    }

    public void Update() => _enemy.CurrentAction?.Update();

    public void Exit()
    {
        if (_enemy.CurrentAction?.IsActive == true) _enemy.CurrentAction.Exit();
        _enemy.CurrentAction = null;
    }
}
