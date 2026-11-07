using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Read-only destination planning and collider sweeps shared by boss mobility actions.</summary>
public static class BossMobilityNavigation
{
    private const float Skin = .04f;
    private const float MaximumFloorDifference = .45f;

    public sealed class Plan
    {
        public Vector3 StartPosition { get; internal set; }
        public Vector3 Destination { get; internal set; }
        public Vector3[] Corners { get; internal set; }
        internal Context Query;
        internal BossMobilitySkillSO Skill;
    }

    internal sealed class Context
    {
        public readonly Boss Owner;
        public readonly NavMeshAgent Agent;
        public readonly BaekhoMapModule Room;
        public readonly NavMeshQueryFilter Filter;
        public readonly HashSet<Transform> Ground = new HashSet<Transform>();
        public readonly float Radius, Height, CenterY, BaseOffset;

        public Context(Boss owner, NavMeshAgent agent)
        {
            Owner = owner; Agent = agent;
            Room = owner.GetComponentInParent<BaekhoMapModule>();
            if (Room != null && Room.groundObjects != null)
                foreach (var ground in Room.groundObjects) if (ground != null) Ground.Add(ground.transform);
            Filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            Vector3 scale = owner.transform.lossyScale;
            float horizontal = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float vertical = Mathf.Abs(scale.y);
            var capsule = owner.GetComponent<CapsuleCollider>();
            Radius = Mathf.Max(.1f, agent.radius * horizontal,
                capsule != null ? capsule.radius * horizontal : 0f);
            Height = Mathf.Max(Radius * 2f, agent.height * vertical,
                capsule != null ? capsule.height * vertical : 0f);
            CenterY = capsule != null ? capsule.center.y * vertical : Height * .5f;
            BaseOffset = agent.baseOffset * vertical;
        }

        public bool Available => Owner != null && Agent != null && Agent.enabled && Agent.isOnNavMesh;
        public bool Own(Collider collider) => collider == null || collider.transform == Owner.transform
            || collider.transform.IsChildOf(Owner.transform);

        private bool IsGround(Transform candidate)
        {
            while (candidate != null)
            {
                if (Ground.Contains(candidate)) return true;
                if (Room != null && candidate == Room.transform) break;
                candidate = candidate.parent;
            }
            return false;
        }

        public bool Floor(Vector3 desired, out Vector3 floor)
        {
            floor = default;
            if (!Finite(desired) || !NavMesh.SamplePosition(desired, out var hit, .4f, Filter)
                || Mathf.Abs(hit.position.y - desired.y) > MaximumFloorDifference) return false;
            floor = hit.position;
            if (Room == null) return true; // Standalone test/preview navigation has no room ownership.
            if (!Room.localBounds.Contains(Room.transform.InverseTransformPoint(floor)) || Ground.Count == 0)
                return false;
            float nearest = float.PositiveInfinity;
            Collider support = null;
            foreach (var supportHit in Physics.RaycastAll(floor + Vector3.up * .35f, Vector3.down, .8f,
                ~0, QueryTriggerInteraction.Ignore))
            {
                if (Own(supportHit.collider) || supportHit.collider.GetComponentInParent<Enemy>() != null
                    || supportHit.distance >= nearest) continue;
                nearest = supportHit.distance; support = supportHit.collider;
            }
            return support != null && IsGround(support.transform);
        }

        private bool Blocks(Collider collider, float floorY)
        {
            if (Own(collider) || !collider.enabled || collider.isTrigger) return false;
            // The intended supporting floor is not a body obstacle. Other floors/ceilings remain so.
            if (collider.bounds.max.y <= floorY + .12f
                && (Room == null || IsGround(collider.transform))) return false;
            return true;
        }

        private void Capsule(Vector3 root, out Vector3 lower, out Vector3 upper, out float radius)
        {
            radius = Mathf.Max(.05f, Radius - Skin);
            float bottom = CenterY - Height * .5f + .07f;
            float top = CenterY + Height * .5f - .02f;
            lower = root + Vector3.up * (bottom + radius);
            upper = root + Vector3.up * Mathf.Max(bottom + radius, top - radius);
        }

        public bool Clear(Vector3 root, float floorY)
        {
            Capsule(root, out var lower, out var upper, out float radius);
            foreach (var collider in Physics.OverlapCapsule(lower, upper, radius, ~0, QueryTriggerInteraction.Ignore))
                if (Blocks(collider, floorY)) return false;
            return true;
        }

        public bool Sweep(Vector3 from, Vector3 to, float floorY)
        {
            if (!Finite(from) || !Finite(to) || !Clear(to, floorY)) return false;
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < .0000001f) return true;
            Capsule(from, out var lower, out var upper, out float radius);
            foreach (var hit in Physics.CapsuleCastAll(lower, upper, radius, delta.normalized,
                delta.magnitude + .005f, ~0, QueryTriggerInteraction.Ignore))
                if (Blocks(hit.collider, floorY)) return false;
            return true;
        }

        public bool CompletePath(Vector3 fromFloor, Vector3 toFloor, out Vector3[] corners)
        {
            var path = new NavMeshPath(); corners = null;
            if (!NavMesh.CalculatePath(fromFloor, toFloor, Filter, path)
                || path.status != NavMeshPathStatus.PathComplete || path.corners.Length == 0) return false;
            corners = path.corners;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 start = i == 0 ? fromFloor : corners[i - 1];
                int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, corners[i]) / .4f));
                for (int n = 0; n <= samples; n++)
                {
                    Vector3 point = Vector3.Lerp(start, corners[i], (float)n / samples);
                    if (!Floor(point, out var floor) || Mathf.Abs(floor.y - fromFloor.y) > MaximumFloorDifference)
                        return false;
                }
            }
            return true;
        }
    }

    public static bool TryPlan(Boss owner, BossMobilitySkillSO skill, out Plan plan)
    {
        plan = null;
        if (owner == null || skill == null || owner.CombatTarget == null) return false;
        var agent = owner.GetComponent<NavMeshAgent>();
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return false;
        var query = new Context(owner, agent);
        Vector3 start = agent.nextPosition;
        Vector3 startFloor = start - Vector3.up * query.BaseOffset;
        if (!query.Floor(startFloor, out startFloor)) return false;
        Vector3 player = owner.CombatTarget.position;
        Vector3 toPlayer = Flat(player - start);
        if (skill.kind != BossMobilityKind.EscapeJump)
        {
            if (toPlayer.magnitude <= skill.stopDistance + .25f) return false;
            float distance = Mathf.Min(skill.maxDistance, toPlayer.magnitude - skill.stopDistance);
            return Candidate(query, skill, startFloor, startFloor + toPlayer.normalized * distance, out plan);
        }

        Vector3 away = toPlayer.sqrMagnitude > .001f ? -toPlayer.normalized : -owner.transform.forward;
        float currentDistance = toPlayer.magnitude;
        var candidates = new List<KeyValuePair<float, Vector3>>();
        foreach (float fraction in new[] { 2f / 3f, 5f / 6f, 1f })
        {
            float distance = skill.maxDistance * fraction;
            for (int index = 0; index < 12; index++)
            {
                Vector3 direction = Quaternion.Euler(0f, index * 30f, 0f) * away;
                if (!query.Floor(startFloor + direction * distance, out var goal)
                    || Mathf.Abs(goal.y - startFloor.y) > MaximumFloorDifference
                    || NavMesh.Raycast(startFloor, goal, out _, query.Filter)) continue;
                float separation = Flat(goal - player).magnitude;
                if (separation < currentDistance + 1f) continue;
                float clearance = NavMesh.FindClosestEdge(goal, out var edge, query.Filter)
                    ? Mathf.Min(edge.distance, 3f) : 0f;
                float score = clearance * 3f + Mathf.Min(separation, 8f)
                    - Mathf.Max(0f, separation - 8f) * .4f + distance * .15f;
                candidates.Add(new KeyValuePair<float, Vector3>(score, goal));
            }
        }
        // Rank cheap floor/edge probes first; full path and arc sweeps are usually needed for just
        // the best candidate rather than every point around the boss.
        candidates.Sort((a, b) => b.Key.CompareTo(a.Key));
        foreach (var candidate in candidates)
            if (Candidate(query, skill, startFloor, candidate.Value, out plan)) return true;
        return false;
    }

    private static bool Candidate(Context query, BossMobilitySkillSO skill, Vector3 startFloor,
        Vector3 desired, out Plan plan)
    {
        plan = null;
        if (!query.Floor(desired, out var goal) || Mathf.Abs(goal.y - startFloor.y) > MaximumFloorDifference
            || Flat(goal - startFloor).magnitude < .3f
            || Flat(goal - startFloor).magnitude > skill.maxDistance + .1f
            || !query.CompletePath(startFloor, goal, out var corners)
            || !query.Clear(goal + Vector3.up * query.BaseOffset, goal.y)) return false;
        float pathLength = 0f;
        Vector3 previousCorner = startFloor;
        foreach (Vector3 corner in corners)
        {
            pathLength += Vector3.Distance(previousCorner, corner);
            previousCorner = corner;
        }
        if (pathLength > skill.maxDistance + .1f) return false;
        if (NavMesh.Raycast(startFloor, goal, out _, query.Filter)) return false;
        if (skill.IsJump)
        {
            int samples = Mathf.Max(12, Mathf.CeilToInt(Vector3.Distance(startFloor, goal) / .2f));
            Vector3 previous = startFloor + Vector3.up * query.BaseOffset;
            for (int i = 1; i <= samples; i++)
            {
                float t = (float)i / samples;
                Vector3 ground = Vector3.Lerp(startFloor, goal, t);
                if (!query.Floor(ground, out var floor)) return false;
                Vector3 point = floor + Vector3.up * (query.BaseOffset + skill.jumpHeight * Mathf.Sin(t * Mathf.PI));
                if (!query.Sweep(previous, point, floor.y)) return false;
                previous = point;
            }
        }
        else
        {
            for (int i = 1; i < corners.Length; i++)
                if (!query.Sweep(corners[i - 1] + Vector3.up * query.BaseOffset,
                    corners[i] + Vector3.up * query.BaseOffset, Mathf.Min(corners[i - 1].y, corners[i].y))) return false;
        }
        plan = new Plan
        {
            StartPosition = startFloor + Vector3.up * query.BaseOffset,
            Destination = goal + Vector3.up * query.BaseOffset,
            Corners = corners, Query = query, Skill = skill
        };
        return true;
    }

    public static bool CanStart(Plan plan)
        => plan != null && plan.Query.Available
            && (plan.Query.Agent.nextPosition - plan.StartPosition).sqrMagnitude < .09f
            && GroundSafe(plan, plan.Query.Agent.nextPosition) && LandingSafe(plan);

    public static bool GroundSafe(Plan plan, Vector3 groundRoot)
    {
        // Static geometry queries also remain useful while an encounter disables its agent.
        if (plan == null || plan.Query.Owner == null || plan.Query.Agent == null || !Finite(groundRoot)) return false;
        var query = plan.Query;
        Vector3 desired = groundRoot - Vector3.up * query.BaseOffset;
        return query.Floor(desired, out var floor) && Vector3.Distance(floor, desired) < .1f
            && query.Clear(groundRoot, floor.y);
    }

    public static bool SafeGroundFallback(Plan plan, Vector3 current, Vector3 lastClear, out Vector3 ground)
    {
        Vector3 origin = Finite(current) ? current : lastClear;
        ground = origin;
        foreach (Vector3 candidate in new[] { origin, lastClear, plan.StartPosition })
        {
            if (!GroundSafe(plan, candidate)) continue;
            Vector3 startFloor = origin - Vector3.up * plan.Query.BaseOffset;
            Vector3 endFloor = candidate - Vector3.up * plan.Query.BaseOffset;
            if (NavMesh.Raycast(startFloor, endFloor, out _, plan.Query.Filter)
                || !plan.Query.CompletePath(startFloor, endFloor, out _)
                || !plan.Query.Sweep(origin, candidate, Mathf.Min(startFloor.y, endFloor.y))) continue;
            ground = candidate;
            return true;
        }
        // If every fallback is dynamically blocked, stay at the current known floor projection.
        // Never teleport to an unchecked launch point across a newly introduced obstacle.
        return false;
    }

    public static bool LandingSafe(Plan plan)
    {
        if (plan == null || !plan.Query.Available) return false;
        var query = plan.Query;
        Vector3 desired = plan.Destination - Vector3.up * query.BaseOffset;
        return query.Floor(desired, out var floor) && Vector3.Distance(floor, desired) < .1f
            && query.Clear(plan.Destination, floor.y);
    }

    /// <summary>Projects a movement step onto its original floor and checks its complete body sweep.</summary>
    public static bool ValidateStep(Plan plan, Vector3 fromRoot, Vector3 desiredRoot, Vector3 desiredGround,
        out Vector3 projectedGround)
    {
        projectedGround = default;
        if (plan == null || !plan.Query.Available) return false;
        var query = plan.Query;
        if (!query.Floor(desiredGround - Vector3.up * query.BaseOffset, out var floor)
            || Mathf.Abs(floor.y - (plan.StartPosition.y - query.BaseOffset)) > MaximumFloorDifference) return false;
        projectedGround = floor + Vector3.up * query.BaseOffset;
        if (Flat(projectedGround - desiredGround).sqrMagnitude > .01f) return false;
        Vector3 currentFloor = query.Agent.nextPosition - Vector3.up * query.BaseOffset;
        return !NavMesh.Raycast(currentFloor, floor, out _, query.Filter)
            && query.Sweep(fromRoot, desiredRoot + projectedGround - desiredGround, floor.y);
    }

    public static bool IsCornered(Boss owner, BossMobilitySettings settings)
    {
        if (owner == null || settings == null || owner.CombatTarget == null
            || Flat(owner.transform.position - owner.CombatTarget.position).magnitude > settings.cornerPlayerDistance)
            return false;
        var agent = owner.GetComponent<NavMeshAgent>();
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return false;
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        if (!NavMesh.FindClosestEdge(agent.nextPosition, out var edge, filter)
            || edge.distance > settings.cornerEdgeDistance) return false;
        Vector3 away = Flat(owner.transform.position - owner.CombatTarget.position).normalized;
        if (away.sqrMagnitude < .001f) away = -owner.transform.forward;
        Vector3 behind = agent.nextPosition + away * settings.blockedRetreatDistance;
        if (agent.Raycast(behind, out _)) return true;
        var query = new Context(owner, agent);
        return !query.Sweep(agent.nextPosition, behind, agent.nextPosition.y - query.BaseOffset);
    }

    public static Vector3 Flat(Vector3 value) { value.y = 0f; return value; }
    public static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y)
        && !float.IsNaN(value.z) && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
}
