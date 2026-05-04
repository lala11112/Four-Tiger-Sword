public class EnemyIdleState : IPlayerState
{
    Enemy _enemy;

    public EnemyIdleState(Enemy enemy) {_enemy = enemy;}

    public void Enter()
    {
        _enemy.Animator.CrossFade("Idle", 0.1f);
    }

    public void Update()
    {
        //그냥 대기 또는 아직 미정.
    }

    public void Exit()
    {
        
    }

}
