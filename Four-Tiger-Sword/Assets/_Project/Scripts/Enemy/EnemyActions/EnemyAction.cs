using UnityEngine;

public abstract class EnemyAction
{
    public MonsterSkillData SkillData { get; protected set; }
    public bool IsFinished { get; protected set; }

    protected readonly Enemy _enemy;
    protected readonly LayerMask _playerLayer;
    protected float _timer;

    protected EnemyAction(MonsterSkillData data, Enemy enemy)
    {
        SkillData = data;
        _enemy = enemy;
        _playerLayer = LayerMask.GetMask("Player");
    }

    public virtual void Enter()
    {
        _timer = 0f;
        IsFinished = false;
    }

    public virtual void Update()
    {
        _timer += Time.deltaTime;
    }

    public virtual void Exit() { }

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
}
