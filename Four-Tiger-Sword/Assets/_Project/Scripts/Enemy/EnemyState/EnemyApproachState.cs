using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 공격이 선택된 후, 해당 공격의 실행 사거리(excuteRange)까지 플레이어에게 접근하는 상태.
/// IsInExecuteRange가 true가 되면 EnemyAttackState로 전환된다.
/// </summary>
public class EnemyApproachState : IPlayerState
{
    private readonly Enemy _enemy;
    private NavMeshAgent _nav;

    public EnemyApproachState(Enemy enemy)
    {
        _enemy = enemy;
    }

    public void Enter()
    {
        _nav = _enemy.GetComponent<NavMeshAgent>();
    }

    public void Update()
    {
        if (_enemy.DetectedTarget == null || _enemy.CurrentAction == null) return;
        _nav.SetDestination(_enemy.DetectedTarget.position);
    }

    public void Exit()
    {
        _nav.ResetPath();
    }
}
