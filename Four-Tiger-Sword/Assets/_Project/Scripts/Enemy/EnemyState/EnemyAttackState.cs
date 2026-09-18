using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 공격 예고(번쩍임 등) 후 EnemyAction에 실행을 위임하는 상태.
/// 공격 완료 시 쿨타임을 시작하고 CombatIdleState로 돌아간다.
/// </summary>
public class EnemyAttackState : IPlayerState
{
    private readonly Enemy _enemy;
    private NavMeshAgent _nav;


    public EnemyAttackState(Enemy enemy)
    {
        _enemy = enemy;
    }

    public void Enter()
    {
        _nav = _enemy.GetComponent<NavMeshAgent>();
        if (_nav.enabled && _nav.isOnNavMesh) _nav.ResetPath();

        // 플레이어 방향 고정
        FaceTarget();
        // 예고와 선딜은 액션의 타임라인에서 한 번만 처리합니다.
        _enemy.CurrentAction?.Enter();
    }

    public void Update()
    {
        _enemy.CurrentAction?.Update();
    }

    public void Exit()
    {
        if (_enemy.CurrentAction?.IsActive == true)
            _enemy.CurrentAction?.Exit();

        _enemy.Animator.CrossFade("Move", 0.1f);
        _enemy.StartAttackCooldown();
        _enemy.HasSelectedAttack = false;
        _enemy.CurrentAction = null;
        //_enemy.GetComponent<Renderer>().material.color = Color.gray;
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────────────

    private void FaceTarget()
    {
        if (_enemy.DetectedTarget == null) return;
        Vector3 dir = _enemy.DetectedTarget.position - _enemy.transform.position;
        dir.y = 0f;
        if (dir != Vector3.zero)
            _enemy.transform.rotation = Quaternion.LookRotation(dir);
    }

}
