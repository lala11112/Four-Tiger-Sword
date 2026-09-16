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
        _target = _enemy.DetectedTarget;
        _enemy.Animator.CrossFade("Move", 0.1f);
    }

    public void Update()
    {
        _target = _enemy.DetectedTarget;
        if (_target != null && _navMeshAgent.enabled && _navMeshAgent.isOnNavMesh)
            _navMeshAgent.SetDestination(_target.position);
        //플레이어를 추적하는 로직  
    }

    public void Exit()
    {
        
    }
}
