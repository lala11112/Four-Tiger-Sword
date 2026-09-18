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
    private float _elapsed;
    private float _stuckTime;
    private float _repathTimer;
    private Vector3 _progressPosition;

    public EnemyApproachState(Enemy enemy)
    {
        _enemy = enemy;
    }

    public void Enter()
    {
        _nav = _enemy.GetComponent<NavMeshAgent>();
        _elapsed = _stuckTime = _repathTimer = 0f;
        _progressPosition = _enemy.transform.position;
        _previousStoppingDistance = _nav.stoppingDistance;
        if (_nav.enabled && _nav.isOnNavMesh) _nav.ResetPath();
        if (_enemy.CurrentAction != null)
            _nav.stoppingDistance = Mathf.Min(_previousStoppingDistance,
                Mathf.Max(0f, _enemy.CurrentAction.SkillData.executeRange * 0.8f));
        _enemy.Animator.CrossFade("Move", 0.1f);
    }

    public void Update()
    {
        if (_enemy.DetectedTarget == null || _enemy.CurrentAction == null) return;
        _elapsed += Time.deltaTime;
        _stuckTime += Time.deltaTime;
        if ((_enemy.transform.position - _progressPosition).sqrMagnitude >= 0.01f)
        {
            _progressPosition = _enemy.transform.position;
            _stuckTime = 0f;
        }
        if (!_nav.enabled || !_nav.isOnNavMesh
            || _elapsed >= _enemy.ApproachTimeout || _stuckTime >= _enemy.ApproachStuckTimeout)
        {
            _enemy.CancelFailedApproach();
            return;
        }
        if (_elapsed > 0.2f && !_nav.pathPending
            && _nav.pathStatus != NavMeshPathStatus.PathComplete)
        {
            _enemy.CancelFailedApproach();
            return;
        }
        _repathTimer -= Time.deltaTime;
        if (_repathTimer > 0f) return;
        _repathTimer = 0.2f;
        Vector3 destination = _enemy.DetectedTarget.position;
        float minRange = _enemy.CurrentAction.SkillData.minimumRange;
        Vector3 away = _enemy.transform.position - destination;
        away.y = 0f;
        if (away.magnitude < minRange)
        {
            if (away.sqrMagnitude < 0.001f) away = -_enemy.transform.forward;
            float desiredRange = (minRange + _enemy.CurrentAction.SkillData.executeRange) * 0.5f;
            destination += away.normalized * desiredRange;
        }
        if (!_nav.SetDestination(destination)) _enemy.CancelFailedApproach();
    }

    public void Exit()
    {
        _nav.stoppingDistance = _previousStoppingDistance;
        if (_nav.enabled && _nav.isOnNavMesh) _nav.ResetPath();
    }
}
