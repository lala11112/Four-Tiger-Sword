using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// 점프 내리찍기 공격 실행 로직.
/// Startup(선딜) → Jump(포물선 이동) → Slam(착지 충격 판정) → Recovery(후딜) 순으로 진행된다.
/// 도약 중에는 NavMeshAgent를 비활성화하고 transform을 직접 제어한다.
/// 착지 시 Warp로 NavMesh에 복귀한다.
/// </summary>
public class EnemyJumpSlam : EnemyAction
{
    private enum Phase { Startup, Jump, Slam, Recovery }

    private readonly MonsterJumpSlamSO _data;
    private readonly NavMeshAgent _nav;
    private readonly HashSet<IDamageable> _hitTargets = new HashSet<IDamageable>();

    private Phase _phase;
    private Vector3 _jumpStartPos;
    private Vector3 _jumpTargetPos;
    private bool _movementUnlocked;

    public EnemyJumpSlam(MonsterJumpSlamSO data, Enemy enemy)
        : base(data, enemy)
    {
        _data = data;
        _nav = enemy.GetComponent<NavMeshAgent>();
    }

    public override void Enter()
    {
        base.Enter();
        _phase = Phase.Startup;
        _hitTargets.Clear();
        _movementUnlocked = false;

        if (_nav.enabled && _nav.isOnNavMesh) _nav.ResetPath();
        _enemy.LockMovement();
        _enemy.Animator?.CrossFade(_data.animName, 0.01f);
        // TODO: _enemy.Animator?.SetTrigger(_data.animName);
    }

    public override void Update()
    {
        base.Update();

        switch (_phase)
        {
            case Phase.Startup:
                FaceTarget();
                if (_timer >= _data.startupTime)
                {
                    BeginJump();
                    _timer = 0f;
                    _phase = Phase.Jump;
                }
                break;

            case Phase.Jump:
                PerformJump();
                if (_timer >= _data.jumpDuration)
                {
                    Land();
                    _timer = 0f;
                    _phase = Phase.Recovery;
                }
                break;

            case Phase.Recovery:
                if (_timer >= _data.recoveryTime)
                    IsFinished = true;
                break;
        }
    }

    public override void Exit()
    {
        base.Exit(); // 예고가 아직 활성 상태라면 여기서 종료
        // 상태가 중단될 경우에도 NavMeshAgent 복구
        if (!_nav.enabled)
        {
            RestoreNavigation(_enemy.transform.position);
        }
        _enemy.transform.rotation = Quaternion.Euler(0f, _enemy.transform.eulerAngles.y, 0f);

        if (!_movementUnlocked)
        {
            _enemy.UnlockMovement();
            _movementUnlocked = true;
        }
        _enemy.SyncMovementLock();
    }

    // ── 도약 시작 ────────────────────────────────────────────────────────────

    private void BeginJump()
    {
        _jumpStartPos = _enemy.transform.position;

        // 도약 시점의 플레이어 위치를 목표로 고정 (이후 플레이어가 움직여도 방향 불변)
        if (_enemy.DetectedTarget != null)
        {
            Vector3 t = _enemy.DetectedTarget.position;
            _jumpTargetPos = new Vector3(t.x, _jumpStartPos.y, t.z);
        }
        else
        {
            _jumpTargetPos = _jumpStartPos + _enemy.transform.forward * 3f;
        }

        // 공중 이동 중 NavMesh 간섭 차단
        _nav.enabled = false;
    }

    // ── 포물선 이동 ──────────────────────────────────────────────────────────

    private void PerformJump()
    {
        float t = Mathf.Clamp01(_timer / Mathf.Max(0.01f, _data.jumpDuration));

        // 수평: 선형 보간 / 수직: sin 포물선
        Vector3 flat = Vector3.Lerp(_jumpStartPos, _jumpTargetPos, t);
        float height = _data.jumpHeight * Mathf.Sin(t * Mathf.PI);

        _enemy.transform.position = new Vector3(flat.x, _jumpStartPos.y + height, flat.z);

        // 낙하 방향으로 적 회전 (선택)
        Vector3 moveDir = (_jumpTargetPos - _jumpStartPos).normalized;
        if (moveDir != Vector3.zero)
            _enemy.transform.rotation = Quaternion.LookRotation(moveDir);
    }

    // ── 착지 + 충격 판정 ────────────────────────────────────────────────────

    private void Land()
    {
        _enemy.transform.position = _jumpTargetPos;

        RestoreNavigation(_jumpTargetPos);

        if (!_movementUnlocked)
        {
            _enemy.UnlockMovement();
            _movementUnlocked = true;
        }
        _enemy.SyncMovementLock();
        _enemy.transform.rotation = Quaternion.Euler(0f, _enemy.transform.eulerAngles.y, 0f);
        // telegraphDuration을 startupTime + jumpDuration으로 설정하면 베이스 클래스가 착지 직전에 자동으로 EndTelegraph() 호출
        BroadcastHit(); // TakeDamage에서 이 액션을 소스로 식별하기 위해
        ExecuteSlam();
    }

    private void RestoreNavigation(Vector3 desiredPosition)
    {
        // 공중에서 중단된 경우 먼저 지면을 찾은 뒤 에이전트를 켭니다.
        var filter = new NavMeshQueryFilter { agentTypeID = _nav.agentTypeID, areaMask = _nav.areaMask };
        float searchRadius = Mathf.Max(2f, _data.jumpHeight + 1f);
        if (!NavMesh.SamplePosition(desiredPosition, out var hit, searchRadius, filter)
            && !NavMesh.SamplePosition(_jumpStartPos, out hit, searchRadius, filter))
        {
            _enemy.transform.position = _jumpStartPos;
            _nav.enabled = true;
            Debug.LogWarning($"{_enemy.name}: 점프 복귀 위치의 NavMesh를 찾지 못했습니다.", _enemy);
            return;
        }
        _enemy.transform.position = hit.position;
        _nav.enabled = true;
        _nav.Warp(hit.position);
    }

    private void ExecuteSlam()
    {
        Vector3 center = GetHitCenter(_data.hitBoxOffset);

        foreach (var col in Physics.OverlapSphere(center, _data.slamRadius, _playerLayer))
        {
            var damageable = col.GetComponentInParent<IDamageable>();
            if (damageable == null || !_hitTargets.Add(damageable)) continue;

            // 착지 충격은 외부로 퍼지는 방향이 아닌 위→아래 방향 넉백 포함
            Vector3 knockback = GetKnockbackDirection(col) * _data.knockbackForce;
            DamageManager.Apply(
                new HitInfo(_data.damage * _enemy.EnemyStat.GetStat(EnemyStatType.ATK), _enemy.Element, _enemy.EnemyStat.GetStat(EnemyStatType.CriticalChance),
                            _enemy.EnemyStat.GetStat(EnemyStatType.CriticalDamage), power: knockback, isParryable: _data.isParryable, source: this),
                damageable, col.gameObject);
        }
    }
}
