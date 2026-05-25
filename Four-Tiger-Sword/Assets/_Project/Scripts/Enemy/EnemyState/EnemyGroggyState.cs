using UnityEngine;
using System.Collections;

public class EnemyGroggyState : IPlayerState
{
    private Enemy _enemy;

    public EnemyGroggyState(Enemy enemy)
    {
        _enemy = enemy;
    }
    public void Enter()
    {
        _enemy.Animator.CrossFade("Groggy", 0.1f);
        _enemy.LockMovement();
        _enemy.StartCoroutine(GroggyRoutine());
    }

    public void Update()
    {
        
    }

    public void Exit()
    {
        _enemy.UnlockMovement();
        _enemy.StopAllCoroutines();
        //강인도 초기화. 
        _enemy.GetComponent<PoiseHandler>().ResetPoise();
    }
    private IEnumerator GroggyRoutine()
    {
        yield return new WaitForSeconds(_enemy.GroggyDuration);
        _enemy.IsGroggy = false;
    }

}
