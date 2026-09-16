using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 공격이 선택된 후, 해당 공격의 실행 사거리(executeRange)까지 플레이어에게 접근하는 상태.
/// IsInExecuteRange가 true가 되면 EnemyAttackState로 전환된다.
/// </summary>
public class EnemyApproachState : IPlayerState
{
    private readonly Enemy _enemy;
    private NavMeshAgent _nav;
    private float _previousStoppingDistance;

    public EnemyApproachState(Enemy enemy)
    {
        _enemy = enemy;
    }

    public void Enter()
    {
        _nav = _enemy.GetComponent<NavMeshAgent>();
        _previousStoppingDistance = _nav.stoppingDistance;
        if (_enemy.CurrentAction != null)
            _nav.stoppingDistance = Mathf.Min(_previousStoppingDistance,
                Mathf.Max(0f, _enemy.CurrentAction.SkillData.executeRange * 0.8f));
        _enemy.Animator.CrossFade("Move", 0.1f);
    }

    public void Update()
    {
        if (_enemy.DetectedTarget == null || _enemy.CurrentAction == null) return;
        if (_nav.enabled && _nav.isOnNavMesh)
            _nav.SetDestination(_enemy.DetectedTarget.position);
    }

    public void Exit()
    {
        _nav.stoppingDistance = _previousStoppingDistance;
        if (_nav.enabled && _nav.isOnNavMesh) _nav.ResetPath();
    }
}
