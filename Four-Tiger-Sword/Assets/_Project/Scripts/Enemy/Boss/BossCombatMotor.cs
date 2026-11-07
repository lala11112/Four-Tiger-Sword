using UnityEngine;
using UnityEngine.AI;

/// <summary>Navigation for engaged bosses. Never patrols or searches for a target.</summary>
public sealed class BossCombatMotor
{
    public enum Result { Running, Completed, Failed }
    private readonly Boss _boss;
    private readonly NavMeshAgent _nav;
    private BossMovementSettings _settings;
    private BossCombatProfileSO _profile;
    private float _elapsed, _stuck, _repath, _oldStoppingDistance;
    private Vector3 _progressPosition;
    private bool _oldUpdateRotation;
    public bool IsMoving { get; private set; }
    public string Reason { get; private set; }

    public BossCombatMotor(Boss boss)
    { _boss = boss; _nav = boss.GetComponent<NavMeshAgent>(); }

    public void Begin(BossMovementSettings settings, BossCombatProfileSO profile)
    {
        Stop();
        _settings = settings;
        _profile = profile;
        _elapsed = _stuck = _repath = 0f;
        _progressPosition = _boss.transform.position;
        Reason = "Moving: " + settings.mode;
        IsMoving = true;
        if (_nav != null)
        {
            _oldStoppingDistance = _nav.stoppingDistance;
            _oldUpdateRotation = _nav.updateRotation;
            _nav.stoppingDistance = .1f;
            _nav.updateRotation = false;
        }
        if (_boss.Animator != null && _boss.Animator.runtimeAnimatorController != null
            && _boss.Animator.HasState(0, Animator.StringToHash("Move")))
            _boss.Animator.CrossFade("Move", .1f);
    }

    public Result Tick(float delta)
    {
        if (!IsMoving) return Result.Completed;
        var target = _boss.CombatTarget;
        if (target == null) return Fail("Combat target unavailable");
        if (_nav == null || !_nav.enabled || !_nav.isOnNavMesh) return Fail("NavMesh unavailable");
        _elapsed += delta;
        Vector3 offset = _boss.transform.position - target.position;
        offset.y = 0f;
        float distance = offset.magnitude;
        bool strafe = _settings.mode == BossMovementMode.StrafeLeft || _settings.mode == BossMovementMode.StrafeRight;
        bool reached = _settings.mode == BossMovementMode.Approach ? distance <= _settings.maxDistance
            : _settings.mode == BossMovementMode.Retreat ? distance >= _settings.minDistance
            : !strafe && distance >= _settings.minDistance && distance <= _settings.maxDistance;
        FaceTarget(delta);
        if (reached || (strafe && _elapsed >= _settings.duration))
        { Reason = "Movement completed"; Stop(); return Result.Completed; }
        if (_elapsed >= _settings.timeout) return Fail("Movement timeout");
        if ((_boss.transform.position - _progressPosition).sqrMagnitude >= .01f)
        { _progressPosition = _boss.transform.position; _stuck = 0f; }
        else _stuck += delta;
        if (_stuck >= _profile.stuckTimeout) return Fail("Movement stuck");
        if (_elapsed > .2f && !_nav.pathPending && _nav.hasPath
            && _nav.pathStatus != NavMeshPathStatus.PathComplete) return Fail("Incomplete path");
        _repath -= delta;
        if (_repath > 0f) return Result.Running;
        _repath = _profile.repathInterval;
        Vector3 away = distance > .001f ? offset / distance : -_boss.transform.forward;
        float radius = (_settings.minDistance + _settings.maxDistance) * .5f;
        Vector3 destination = target.position + away * radius;
        if (strafe)
        {
            float angle = _settings.mode == BossMovementMode.StrafeLeft ? -30f : 30f;
            destination = target.position + Quaternion.Euler(0f, angle, 0f) * away * Mathf.Max(.5f, radius);
        }
        var filter = new NavMeshQueryFilter { agentTypeID = _nav.agentTypeID, areaMask = _nav.areaMask };
        if (!NavMesh.SamplePosition(destination, out var sample, 1.5f, filter))
            return Fail("No reachable NavMesh destination");
        if (!_nav.SetDestination(sample.position)) return Fail("Path request failed");
        return Result.Running;
    }

    public void FaceTarget(float delta)
    {
        if (_boss.CombatTarget == null || _profile == null) return;
        Vector3 direction = _boss.CombatTarget.position - _boss.transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > .001f)
            _boss.transform.rotation = Quaternion.RotateTowards(_boss.transform.rotation,
                Quaternion.LookRotation(direction), _profile.turnSpeed * delta);
    }

    private Result Fail(string reason)
    { Reason = reason; Stop(); return Result.Failed; }

    public void Stop()
    {
        if (!IsMoving) return;
        IsMoving = false;
        if (_nav == null) return;
        if (_nav.enabled && _nav.isOnNavMesh)
        { _nav.ResetPath(); _nav.velocity = Vector3.zero; }
        _nav.stoppingDistance = _oldStoppingDistance;
        _nav.updateRotation = _oldUpdateRotation;
    }
}
