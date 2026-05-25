using System.Collections;
using UnityEngine;

public class EnemyRootState : IPlayerState
{
    Enemy _enemy;

    public EnemyRootState(Enemy enemy) { _enemy = enemy; }
    
    public void Enter()
    {
        _enemy.StartCoroutine(RootRoutine());
        _enemy.LockMovement();
    }

    public void Update()
    {

    }

    public void Exit()
    {
        _enemy.UnlockMovement();
    }

    private IEnumerator RootRoutine()
    {
        yield return new WaitForSeconds(_enemy.RootDuration);
        _enemy.IsRoot = false;
    }
}