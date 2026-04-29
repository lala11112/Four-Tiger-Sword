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

    private float _timer;
    private bool _actionStarted;

    public EnemyAttackState(Enemy enemy)
    {
        _enemy = enemy;
    }

    public void Enter()
    {
        _nav = _enemy.GetComponent<NavMeshAgent>();
        _nav.ResetPath();

        _timer = 0f;
        _actionStarted = false;

        // 플레이어 방향 고정
        FaceTarget();
        Debug.Log("공격 시작!");
        _enemy.GetComponent<Renderer>().material.color = Color.red;

        // TODO: 번쩍임 이펙트 재생 (예: Outline, DOTween 깜빡임 등)
    }

    public void Update()
    {
        _timer += Time.deltaTime;

        if (!_actionStarted)
        {
            if (_timer >= GetTelegraphDuration())
            {
                _actionStarted = true;
                _enemy.CurrentAction?.Enter();
            }
        }
        else
        {
            _enemy.CurrentAction?.Update();
        }
    }

    public void Exit()
    {
        if (_actionStarted)
            _enemy.CurrentAction?.Exit();

        _enemy.StartAttackCooldown();
        _enemy.HasSelectedAttack = false;
        _enemy.CurrentAction = null;
        _enemy.GetComponent<Renderer>().material.color = Color.gray;
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

    private float GetTelegraphDuration()
    {
        return _enemy.CurrentAction?.SkillData switch
        {
            MonsterDefaultAttackSO so => so.telegraphDuration,
            MonsterDashAttackSO    so => so.telegraphDuration,
            MonsterComboAttackSO   so => so.telegraphDuration,
            MonsterJumpSlamSO      so => so.telegraphDuration,
            _                         => 0.5f
        };
    }
}