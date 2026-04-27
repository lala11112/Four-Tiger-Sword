using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// 대쉬 공격 실행 로직.
/// Startup(선딜) → Dash(돌진 + 히트 판정) → Recovery(후딜) 순으로 진행된다.
/// </summary>
public class EnemyDashAttack : EnemyAction
{
    private enum Phase { Startup, Dash, Recovery }

    private readonly MonsterDashAttackSO _data;
    private readonly Enemy _enemy;
    private readonly NavMeshAgent _nav;
    private readonly LayerMask _playerLayer;
    private readonly HashSet<Collider> _hitTargets = new HashSet<Collider>();

    private Phase _phase;
    private float _timer;
    private Vector3 _dashDirection;

    public EnemyDashAttack(MonsterDashAttackSO data, Enemy enemy)
    {
        SkillData = data;
        _data = data;
        _enemy = enemy;
        _nav = enemy.GetComponent<NavMeshAgent>();
        _playerLayer = LayerMask.GetMask("Player");
    }

    public override void Enter()
    {
        _timer = 0f;
        _phase = Phase.Startup;
        IsFinished = false;
        _hitTargets.Clear();

        _nav.ResetPath();
        _nav.isStopped = true;
        // TODO: _enemy.Animator?.SetTrigger(_data.animName);
    }

    public override void Update()
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Startup:
                FaceTarget();
                if (_timer >= _data.startupTime)
                {
                    // 대쉬 방향을 이 시점에 확정 — 이후 플레이어가 움직여도 방향 고정
                    _dashDirection = GetDirectionToTarget();
                    _timer = 0f;
                    _phase = Phase.Dash;
                }
                break;

            case Phase.Dash:
                _enemy.GetComponent<Collider>().enabled = false;
                PerformDash();
                CheckHit();
                if (_timer >= _data.dashDuration)
                {
                    _nav.isStopped = false;
                    _timer = 0f;
                    _phase = Phase.Recovery;
                }
                break;

            case Phase.Recovery:
                if (_timer >= _data.recoveryTime){
                    IsFinished = true;
                    _enemy.GetComponent<Collider>().enabled = true;
                }
                break;
        }
    }

    public override void Exit()
    {
        _nav.isStopped = false;
    }

    // ── 대쉬 이동 ────────────────────────────────────────────────────────────

    private void PerformDash()
    {
        Vector3 delta = _dashDirection * _data.dashSpeed * Time.deltaTime;
        if (_nav.isOnNavMesh)
            _nav.Move(delta);
        else
            _enemy.transform.position += delta;
    }

    // ── 히트 판정 (대쉬 중 매 프레임 체크) ──────────────────────────────────

    private void CheckHit()
    {
        Vector3 center = _enemy.transform.position
                       + _enemy.transform.rotation * _data.hitBoxOffset;

        foreach (var col in Physics.OverlapSphere(center, _data.hitBoxRadius, _playerLayer))
        {
            if (_hitTargets.Contains(col)) continue;
            if (!col.TryGetComponent<IDamageable>(out var damageable)) continue;

            _hitTargets.Add(col);
            DamageManager.Apply(
                new HitInfo(_data.damage, DamageType.Normal, _data.criticalChance,
                            _data.criticalMultiplier, power: _dashDirection * _data.knockbackForce),
                damageable, col.gameObject);
        }
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────────────

    private void FaceTarget()
    {
        Vector3 dir = GetDirectionToTarget();
        if (dir != Vector3.zero)
            _enemy.transform.rotation = Quaternion.LookRotation(dir);
    }

    private Vector3 GetDirectionToTarget()
    {
        if (_enemy.DetectedTarget == null) return _enemy.transform.forward;
        Vector3 dir = _enemy.DetectedTarget.position - _enemy.transform.position;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.001f ? dir.normalized : _enemy.transform.forward;
    }
}
