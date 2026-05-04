using UnityEngine;
using System.Collections;
using UnityEngine.AI;

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
        _enemy.GetComponent<NavMeshAgent>().isStopped = true;
        _enemy.StartCoroutine(GroggyRoutine());
    }

    public void Update()
    {
        
    }

    public void Exit()
    {
        _enemy.GetComponent<NavMeshAgent>().isStopped = true;
    }
    private IEnumerator GroggyRoutine()
    {
        yield return new WaitForSeconds(_enemy.GroggyDuration);
        _enemy.IsGroggy = false;
    }

}
