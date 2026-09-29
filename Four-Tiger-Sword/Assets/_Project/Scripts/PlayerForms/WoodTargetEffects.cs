using UnityEngine;
using UnityEngine.AI;

// 목속성의 대상별 스택과 모으기. 설정은 공격자의 전용 SO에서 받습니다.
public sealed class WoodTargetEffects : MonoBehaviour
{
    public int Stacks { get; private set; }
    public float StackRemaining { get; private set; }
    private float _pullRemaining, _pullSpeed, _stopDistance;
    private Vector3 _pullCenter;
    private Enemy _enemy;
    private bool _movementLocked;
    private void OnEnable()
    {
        _enemy = GetComponent<Enemy>();
        if (_enemy != null) _enemy.OnDied += ResetEffects;
    }
    public void AddStacks(WoodFormActionDataSO data, bool maximum = false)
    {
        if (!isActiveAndEnabled || GetComponent<EnemyStat>()?.IsDead == true) return;
        int limit = Mathf.Max(1, data.MaximumStacks);
        Stacks = maximum ? limit : Mathf.Min(limit, Stacks + Mathf.Max(1, data.StacksPerHit));
        StackRemaining = Mathf.Max(0.1f, data.StackLifetime);
        if (Stacks < limit) return;
        var handler = GetComponent<StatusEffectHandler>() ?? gameObject.AddComponent<StatusEffectHandler>();
        handler.Apply(new VulnerabilityEffect(data.VulnerabilityDuration, data.VulnerabilityDamageBonusPercent));
        StackRemaining = Mathf.Max(StackRemaining, data.VulnerabilityDuration);
    }
    public void Pull(Vector3 center, float duration, float speed, float stopDistance)
    {
        if (duration <= 0f || speed <= 0f) return;
        _pullCenter = center;
        _pullRemaining = duration;
        _pullSpeed = speed;
        _stopDistance = Mathf.Max(0f, stopDistance);
        if (!_movementLocked && _enemy != null) { _enemy.LockMovement(); _movementLocked = true; }
    }
    private void Update() => Tick(Time.deltaTime);
    private void Tick(float dt)
    {
        dt = Mathf.Max(0f, dt);
        if (GetComponent<EnemyStat>()?.IsDead == true) { ResetEffects(); return; }
        StackRemaining = Mathf.Max(0f, StackRemaining - dt);
        if (StackRemaining <= 0f) Stacks = 0;
        if (_pullRemaining <= 0f) return;
        float interval = Mathf.Min(dt, _pullRemaining);
        _pullRemaining = Mathf.Max(0f, _pullRemaining - dt);
        Vector3 direction = Vector3.ProjectOnPlane(_pullCenter - transform.position, Vector3.up);
        Vector3 motion = direction.normalized * Mathf.Min(_pullSpeed * interval, Mathf.Max(0f, direction.magnitude - _stopDistance));
        var nav = GetComponent<NavMeshAgent>();
        if (nav != null && nav.enabled && nav.isOnNavMesh)
        {
            Vector3 destination = transform.position + motion;
            if (nav.Raycast(destination, out var hit)) destination = hit.position;
            nav.Move(destination - transform.position);
        }
        else if (TryGetComponent<CharacterController>(out var controller) && controller.enabled) controller.Move(motion);
        else
        {
            var bodyCollider = GetComponent<Collider>();
            Vector3 origin = bodyCollider != null ? bodyCollider.bounds.center : transform.position + Vector3.up;
            float radius = bodyCollider != null ? Mathf.Min(bodyCollider.bounds.extents.x, bodyCollider.bounds.extents.z) : 0.1f;
            int walls = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Enemy", "Player");
            if (motion.sqrMagnitude > 0f && Physics.SphereCast(origin, Mathf.Max(0.05f, radius), motion.normalized,
                out var wall, motion.magnitude, walls, QueryTriggerInteraction.Ignore))
                motion = motion.normalized * Mathf.Max(0f, wall.distance - 0.01f);
            if (TryGetComponent<Rigidbody>(out var body)) body.MovePosition(body.position + motion);
            else transform.position += motion;
        }
        if (_pullRemaining <= 0f) ReleaseMovement();
    }
    private void ReleaseMovement()
    {
        if (_movementLocked && _enemy != null) _enemy.UnlockMovement();
        _movementLocked = false;
    }
    private void ResetEffects()
    {
        Stacks = 0; StackRemaining = _pullRemaining = 0f;
        ReleaseMovement();
    }
    private void OnDisable()
    {
        if (_enemy != null) _enemy.OnDied -= ResetEffects;
        ResetEffects();
    }
}
