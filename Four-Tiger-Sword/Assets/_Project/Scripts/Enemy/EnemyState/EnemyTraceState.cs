using UnityEngine;
using UnityEngine.AI;

public class EnemyTraceState : IPlayerState
{
    Enemy _enemy;

    public Transform _target;

    NavMeshAgent _navMeshAgent;

    public EnemyTraceState(Enemy enemy) {_enemy = enemy;}

    public void Enter()
    {
        _navMeshAgent = _enemy.GetComponent<NavMeshAgent>();
        _target = GameObject.FindGameObjectWithTag("Player").transform;
        _navMeshAgent.SetDestination(_target.position);
        _enemy.Animator.CrossFade("Run", 0.1f);
    }

    public void Update()
    {
        _navMeshAgent.SetDestination(_target.position);
        //플레이어를 추적하는 로직
    }

    public void Exit()
    {
        
    }
}