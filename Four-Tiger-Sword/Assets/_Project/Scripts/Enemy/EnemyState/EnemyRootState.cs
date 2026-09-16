using UnityEngine;

public class EnemyRootState : IPlayerState
{
    Enemy _enemy;
    private float _remaining;

    public EnemyRootState(Enemy enemy) { _enemy = enemy; }
    
    public void Enter()
    {
        _remaining = _enemy.RootDuration;
        _enemy.LockMovement();
    }
    public void Update()
    {
        _remaining -= Time.deltaTime;
        if (_remaining <= 0f) _enemy.IsRoot = false;
    }

    public void Exit()
    {
        _enemy.UnlockMovement();
    }

}
