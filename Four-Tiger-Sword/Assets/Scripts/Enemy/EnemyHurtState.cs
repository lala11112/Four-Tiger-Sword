using UnityEngine;

public class EnemyHurtState : IPlayerState
{
    Enemy _enemy;

    public EnemyHurtState(Enemy enemy) { _enemy = enemy; }

    public void Enter()
    {
        //피격 애니메이션 재생
    }

    public void Update()
    {
        //애니메이션 재생후, IsHurt를 false로 변경
        _enemy.IsHurt = false;
        Debug.Log("EnemyHurtState Exit");
    }

    public void Exit()
    {

    }
}