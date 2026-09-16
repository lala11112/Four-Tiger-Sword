using UnityEngine;

public abstract class EnemyAction
{
    public MonsterSkillData SkillData { get; protected set; }
    public bool IsFinished { get; protected set; }

    protected readonly Enemy _enemy;
    public Enemy Owner => _enemy;
    protected readonly LayerMask _playerLayer;
    protected float _timer;

    private bool _telegraphActive = false;
    // _timer는 서브클래스가 페이즈 전환 시 리셋하므로, 예고 타이밍은 독립 타이머로 관리
    private float _telegraphTimer = 0f;

    protected EnemyAction(MonsterSkillData data, Enemy enemy)
    {
        SkillData = data;
        _enemy = enemy;
        _playerLayer = LayerMask.GetMask("Player");
    }

    public virtual void Enter()
    {
        _timer = 0f;
        _telegraphTimer = 0f;
        IsFinished = false;
        _telegraphActive = false;
        if (_enemy.Animator != null)
            _enemy.Animator.speed = SkillData.attackSpeed;

        StartTelegraph();
    }

    public virtual void Update()
    {
        // attackSpeed 배율만큼 타이머를 빠르게 진행 → 모든 판정 타이밍이 비례해서 당겨짐
        _timer += Time.deltaTime * SkillData.attackSpeed;

        // 예고 타이밍 자동 관리: _telegraphTimer는 서브클래스의 _timer 리셋과 무관하게 증가
        if (_telegraphActive && SkillData.isParryable)
        {
            _telegraphTimer += Time.deltaTime * SkillData.attackSpeed;
            if (_telegraphTimer >= SkillData.telegraphDuration)
                EndTelegraph();
        }
    }

    public virtual void Exit()
    {
        if (_enemy.Animator != null)
            _enemy.Animator.speed = 1f;

        EndTelegraph();
    }

    /// <summary>패링 가능한 공격임을 플레이어 패링 시스템에 알립니다. Enter 시 자동 호출됩니다.</summary>
    protected void StartTelegraph()
    {
        if (!SkillData.isParryable || _telegraphActive) return;
        _telegraphActive = true;
        ParryEventBus.BroadcastTelegraphStart(this);
        _enemy.OnTelegraphStart(this);
    }

    /// <summary>예고 구간을 종료합니다. 실제 타격 직전 또는 Exit 시 호출됩니다.</summary>
    protected void EndTelegraph()
    {
        if (!_telegraphActive) return;
        _telegraphActive = false;
        ParryEventBus.BroadcastTelegraphEnd(this);
        _enemy.OnTelegraphEnd(this);
    }

    /// <summary>
    /// ExecuteHit 직전에 호출합니다. PlayerController.TakeDamage에서 이 액션의 소스를 식별하는 데 사용됩니다.
    /// </summary>
    protected void BroadcastHit()
    {
        if (SkillData.isParryable)
            ParryEventBus.BroadcastHit(this);
    }
    

    // ── 공통 헬퍼 ────────────────────────────────────────────────────────────

    protected Vector3 GetHitCenter(Vector3 localOffset)
        => _enemy.transform.position + _enemy.transform.rotation * localOffset;

    protected Vector3 GetKnockbackDirection(Collider col)
        => (col.transform.position - _enemy.transform.position).normalized;

    protected Vector3 GetDirectionToTarget()
    {
        if (_enemy.DetectedTarget == null) return _enemy.transform.forward;
        Vector3 dir = _enemy.DetectedTarget.position - _enemy.transform.position;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.001f ? dir.normalized : _enemy.transform.forward;
    }

    protected void FaceTarget()
    {
        Vector3 dir = GetDirectionToTarget();
        if (dir != Vector3.zero)
            _enemy.transform.rotation = Quaternion.LookRotation(dir);
    }

    public virtual void OnParried()
    {
        //_enemy.IsHurt = true;
    }
}
