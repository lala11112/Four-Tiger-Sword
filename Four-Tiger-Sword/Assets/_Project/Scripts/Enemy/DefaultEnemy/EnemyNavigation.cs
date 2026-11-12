using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Validates short ordinary-enemy moves against their room and complete navigation paths.</summary>
internal sealed class EnemyNavigation
{
    private readonly Enemy _enemy;
    private readonly BaekhoMapModule _room;
    private readonly HashSet<Transform> _ground = new();
    private readonly NavMeshPath _path = new();
    private readonly RaycastHit[] _floorHits = new RaycastHit[32];
    private bool? _movingAnimation;
    public NavMeshAgent Agent { get; }
    public bool Available => Agent != null && Agent.enabled && Agent.isOnNavMesh;
    public float VerticalOffset => Agent.baseOffset * Mathf.Abs(_enemy.transform.lossyScale.y);

    public EnemyNavigation(Enemy enemy)
    {
        _enemy = enemy;
        Agent = enemy.GetComponent<NavMeshAgent>();
        _room = enemy.GetComponentInParent<BaekhoMapModule>();
        if (_room != null && _room.groundObjects != null)
            foreach (var ground in _room.groundObjects)
                if (ground != null) _ground.Add(ground.transform);
    }

    public bool TryMove(Vector3 destination, bool patrol, Transform avoidTarget = null)
        => TryBuildPath(destination, patrol, avoidTarget, _path, out _, out _) && Agent.SetPath(_path);

    public bool TryBuildPath(Vector3 destination, bool patrol, Transform avoidTarget, NavMeshPath path,
        out Vector3 actorDestination, out float length)
    {
        actorDestination = default;
        length = 0f;
        if (!Available) return false;
        destination.y -= VerticalOffset;
        var filter = new NavMeshQueryFilter { agentTypeID = Agent.agentTypeID, areaMask = Agent.areaMask };
        if (!NavMesh.SamplePosition(destination, out var sample, .6f, filter)
            || Mathf.Abs(sample.position.y - destination.y) > .5f
            || !Agent.CalculatePath(sample.position, path) || path.status != NavMeshPathStatus.PathComplete)
            return false;
        Vector3 side = avoidTarget != null ? _enemy.transform.position - avoidTarget.position : Vector3.zero;
        side.y = 0f;
        if (avoidTarget != null && side.sqrMagnitude < .001f)
            side = Vector3.ProjectOnPlane(-_enemy.transform.forward, Vector3.up);
        side.Normalize();
        var corners = path.corners;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 start = i == 0 ? corners[i] : corners[i - 1];
            float segmentLength = Vector3.Distance(start, corners[i]);
            length += segmentLength;
            int steps = Mathf.Max(1, Mathf.CeilToInt(segmentLength / .75f));
            for (int step = 0; step <= steps; step++)
            {
                Vector3 point = Vector3.Lerp(start, corners[i], (float)step / steps);
                if (!OnOwnFloor(point)) return false;
                Vector3 fromHome = point - _enemy.PatrolOrigin;
                fromHome.y = 0f;
                if (patrol && fromHome.sqrMagnitude > 9.01f) return false;
                if (avoidTarget != null && Vector3.Dot(point - avoidTarget.position, side) < -.05f) return false;
            }
        }
        actorDestination = sample.position + Vector3.up * VerticalOffset;
        return corners.Length > 0;
    }

    private bool OnOwnFloor(Vector3 point)
    {
        if (_room == null) return true;
        Vector3 local = _room.transform.InverseTransformPoint(point);
        if (!_room.localBounds.Contains(local) || _ground.Count == 0) return false;
        int count = Physics.RaycastNonAlloc(point + Vector3.up * .35f, Vector3.down,
            _floorHits, .8f, ~0, QueryTriggerInteraction.Ignore);
        if (count == _floorHits.Length) return false;
        Transform support = null;
        float closest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = _floorHits[i];
            if (hit.collider.GetComponentInParent<Enemy>() != null || hit.distance >= closest) continue;
            support = hit.transform;
            closest = hit.distance;
        }
        while (support != null)
        {
            if (_ground.Contains(support)) return true;
            if (support == _room.transform) break;
            support = support.parent;
        }
        return false;
    }

    public void Stop()
    {
        if (Available)
        {
            if (Agent.hasPath || Agent.pathPending) Agent.ResetPath();
            if (Agent.velocity.sqrMagnitude > 0f) Agent.velocity = Vector3.zero;
        }
        Animate(false);
    }

    public void RefreshAnimation() => _movingAnimation = null;

    public void Animate(bool moving)
    {
        if (_movingAnimation == moving) return;
        _movingAnimation = moving;
        var animator = _enemy.Animator;
        string state = moving ? "Move" : "Idle";
        if (animator != null && animator.isActiveAndEnabled && animator.layerCount > 0
            && animator.HasState(0, Animator.StringToHash(state)))
            animator.CrossFade(state, .1f, 0);
    }
}
