using UnityEngine;
using System.Collections;
using UnityEngine.AI;

public class EnemyHurtState : MonoBehaviour, IPlayerState
{
    Enemy _enemy;

    public EnemyHurtState(Enemy enemy) { _enemy = enemy; }

    public void Enter()
    {
        //피격 애니메이션 재생
        _enemy.Animator.CrossFade("Hit", 0.1f);
        _enemy.GetComponent<NavMeshAgent>().isStopped = true;
        _enemy.StartCoroutine(HurtRoutine());
    }

    public void Update()
    {
        //애니메이션 재생후, IsHurt를 false로 변경
        //Debug.Log("EnemyHurtState Exit");
    }

    public void Exit()
    {
        Debug.Log("EnemyHurtState Exit");
        _enemy.GetComponent<NavMeshAgent>().isStopped = false;
        _enemy.IsHurt = false;
        _enemy.StopAllCoroutines();

        if (_enemy.PendingGroggy)
        {
            _enemy.PendingGroggy = false;
            _enemy.IsGroggy = true;
        }
    }

    private IEnumerator HurtRoutine()
    {
        yield return new WaitForSeconds(1f);
        _enemy.IsHurt = false;
    }
}