using System;
using System.Collections.Generic;
using StudioBaekho;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public struct DungeonEnemyPlacement
{
    public GameObject prefab;
    public Vector3 position;
    public Quaternion rotation;
}

/// <summary>Prepares room navigation and plans every enemy before any enemy is instantiated.</summary>
public static class DungeonEncounterPlacement
{
    private const int AttemptsPerMarker = 64;
    private const float SampleDistance = 0.6f;
    private const float MaximumHeightDifference = 0.5f;
    private const float EnemyClearance = 0.2f;

    /// <summary>Call before adding encounter gates or enemies. The owner must destroy ownedData on cleanup.</summary>
    public static bool TryPrepareNavigation(BaekhoMapModule room, out NavMeshData ownedData, out string error)
    {
        ownedData = null;
        error = null;
        if (room == null)
        {
            error = "The encounter room is missing.";
            return false;
        }

        var surfaces = room.GetComponents<NavMeshSurface>();
        foreach (var existing in surfaces)
        {
            if (existing.navMeshData == null) continue;
            if (existing.agentTypeID != 0)
            {
                error = "Room navigation must support the dungeon enemy agent type (0): " + room.name;
                return false;
            }
            existing.enabled = true;
            // Modules are positioned after Instantiate invokes OnEnable. Register at the final room pose now.
            existing.RemoveData();
            existing.AddData();
            return true;
        }

        if (NavMesh.GetSettingsByID(0).agentTypeID != 0)
        {
            error = "The project has no navigation settings for dungeon enemy agent type 0.";
            return false;
        }

        NavMeshSurface surface = surfaces.Length > 0 ? surfaces[0] : room.gameObject.AddComponent<NavMeshSurface>();
        try
        {
            // Imported map modules use one surface for their assembled static geometry.
            foreach (var child in room.GetComponentsInChildren<NavMeshSurface>(true))
                if (child != surface) child.enabled = false;
            surface.enabled = true;
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.layerMask = ~0;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;
            surface.BuildNavMesh();
            ownedData = surface.navMeshData;
            if (ownedData != null) return true;
            error = "Room navigation could not be built: " + room.name;
            return false;
        }
        catch (Exception exception)
        {
            ownedData = surface.navMeshData;
            error = "Room navigation could not be built for " + room.name + ": " + exception.Message;
            return false;
        }
    }

    /// <summary>position is the enemy pivot, including its existing agent baseOffset; rotation follows the marker.</summary>
    public static bool TryPlan(BaekhoMapModule room, IReadOnlyList<GameObject> prefabs, int seed, float radius,
        out List<DungeonEnemyPlacement> placements, out string error)
    {
        placements = new List<DungeonEnemyPlacement>();
        error = null;
        if (room == null || prefabs == null || !IsFinite(radius) || radius < 0f)
        {
            error = "A room, enemy prefab list and a nonnegative placement radius are required.";
            return false;
        }
        if (prefabs.Count == 0) return true;

        var markers = new List<BaekhoGameplayMarker>();
        foreach (var marker in room.GetComponentsInChildren<BaekhoGameplayMarker>(true))
            if (marker.gameObject.activeInHierarchy && string.Equals(marker.kind, "enemy", StringComparison.Ordinal))
                markers.Add(marker);
        // GetComponentsInChildren provides hierarchy order. Insertion sort keeps that order for duplicate IDs.
        for (int i = 1; i < markers.Count; i++)
        {
            var value = markers[i];
            int j = i - 1;
            while (j >= 0 && string.CompareOrdinal(markers[j].id, value.id) > 0)
            {
                markers[j + 1] = markers[j];
                j--;
            }
            markers[j + 1] = value;
        }
        if (markers.Count == 0)
        {
            error = "Room has no active enemy markers: " + room.name;
            return false;
        }

        var random = new System.Random(seed);
        for (int i = markers.Count - 1; i > 0; i--)
        {
            int other = random.Next(i + 1);
            var swap = markers[i];
            markers[i] = markers[other];
            markers[other] = swap;
        }

        var groundRoots = new HashSet<Transform>();
        foreach (var ground in room.groundObjects ?? Array.Empty<GameObject>())
            if (ground != null) groundRoots.Add(ground.transform);
        if (groundRoots.Count == 0)
        {
            error = "Room has no authored walkable ground: " + room.name;
            return false;
        }

        Physics.SyncTransforms();
        var occupied = new List<OccupiedSpace>();
        for (int enemyIndex = 0; enemyIndex < prefabs.Count; enemyIndex++)
        {
            GameObject prefab = prefabs[enemyIndex];
            var agent = prefab != null ? prefab.GetComponent<NavMeshAgent>() : null;
            if (prefab == null || prefab.GetComponent<Enemy>() == null || agent == null || !agent.enabled)
            {
                placements.Clear();
                error = "Every encounter prefab must have an Enemy and an enabled NavMeshAgent on its root.";
                return false;
            }

            Footprint footprint = GetFootprint(prefab, agent);
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            bool found = false;
            for (int markerOffset = 0; markerOffset < markers.Count && !found; markerOffset++)
            {
                var marker = markers[(enemyIndex + markerOffset) % markers.Count];
                Vector3 anchor = marker.transform.position;
                if (!TrySampleFloor(room, groundRoots, anchor, filter, out Vector3 markerFloor)) continue;

                for (int attempt = 0; attempt < AttemptsPerMarker && !found; attempt++)
                {
                    Vector3 candidate = anchor;
                    if (attempt > 0)
                    {
                        float angle = (float)(random.NextDouble() * Math.PI * 2d);
                        float distance = radius * Mathf.Sqrt((float)random.NextDouble());
                        candidate += new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                    }
                    if (!TrySampleFloor(room, groundRoots, candidate, filter, out Vector3 floor)) continue;
                    Vector2 displacement = new Vector2(floor.x - anchor.x, floor.z - anchor.z);
                    if (displacement.sqrMagnitude > (radius + 0.1f) * (radius + 0.1f)) continue;
                    if (NavMesh.Raycast(markerFloor, floor, out _, filter)) continue;
                    if (!HasClearance(room, groundRoots, floor, footprint, occupied)) continue;

                    placements.Add(new DungeonEnemyPlacement
                    {
                        prefab = prefab,
                        position = floor + Vector3.up * footprint.baseOffset,
                        rotation = marker.transform.rotation
                    });
                    occupied.Add(new OccupiedSpace { floor = floor, radius = footprint.radius });
                    found = true;
                }
            }
            if (!found)
            {
                placements.Clear();
                error = "No safe enemy position was found for " + prefab.name + " (" + (enemyIndex + 1) +
                    "/" + prefabs.Count + ") in " + room.name + ". Check marker clearance and room navigation.";
                return false;
            }
        }
        return true;
    }

    /// <summary>The authored boss anchor is a floor position; only a nearby valid room floor is accepted.</summary>
    public static bool TryPlanBoss(BaekhoMapModule room, GameObject prefab, Transform anchor,
        out DungeonEnemyPlacement placement, out string error)
    {
        placement = default;
        error = null;
        if (room == null || prefab == null || anchor == null || !anchor.IsChildOf(room.transform))
        {
            error = "A boss prefab and a boss spawn anchor inside its room are required.";
            return false;
        }
        var enemy = prefab.GetComponent<Enemy>();
        var agent = prefab.GetComponent<NavMeshAgent>();
        if (prefab.scene.IsValid() || !prefab.activeSelf || enemy == null || !enemy.enabled ||
            prefab.GetComponent<EnemyStat>() == null || agent == null || !agent.enabled || agent.agentTypeID != 0)
        {
            error = "The boss must be an active prefab asset with Enemy, EnemyStat and an enabled NavMeshAgent of type 0 on its root.";
            return false;
        }
        var groundRoots = new HashSet<Transform>();
        foreach (var ground in room.groundObjects ?? Array.Empty<GameObject>())
            if (ground != null) groundRoots.Add(ground.transform);
        if (groundRoots.Count == 0)
        {
            error = "Boss room has no authored walkable ground: " + room.name;
            return false;
        }
        Physics.SyncTransforms();
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        var footprint = GetFootprint(prefab, agent);
        if (!TrySampleFloor(room, groundRoots, anchor.position, filter, out var floor) ||
            !HasClearance(room, groundRoots, floor, footprint, new List<OccupiedSpace>()))
        {
            error = "The boss spawn anchor has no safe position on its room NavMesh: " + room.name;
            return false;
        }
        placement = new DungeonEnemyPlacement
        {
            prefab = prefab,
            position = floor + Vector3.up * footprint.baseOffset,
            rotation = anchor.rotation
        };
        return true;
    }

    private static bool TrySampleFloor(BaekhoMapModule room, HashSet<Transform> groundRoots, Vector3 desired,
        NavMeshQueryFilter filter, out Vector3 floor)
    {
        floor = default;
        if (!NavMesh.SamplePosition(desired, out NavMeshHit sample, SampleDistance, filter) ||
            Mathf.Abs(sample.position.y - desired.y) > MaximumHeightDifference) return false;
        var hits = Physics.RaycastAll(sample.position + Vector3.up * 0.6f, Vector3.down, 1.2f,
            ~0, QueryTriggerInteraction.Ignore);
        float closest = float.PositiveInfinity;
        Collider support = null;
        float groundY = 0f;
        foreach (var hit in hits)
        {
            if (hit.distance >= closest) continue;
            closest = hit.distance;
            support = hit.collider;
            groundY = hit.point.y;
        }
        if (support == null || !IsGround(support.transform, room.transform, groundRoots) ||
            Mathf.Abs(groundY - sample.position.y) > MaximumHeightDifference) return false;
        floor = sample.position;
        return true;
    }

    private static bool HasClearance(BaekhoMapModule room, HashSet<Transform> groundRoots, Vector3 floor,
        Footprint footprint, List<OccupiedSpace> occupied)
    {
        foreach (var other in occupied)
        {
            float minimum = footprint.radius + other.radius + EnemyClearance;
            Vector2 separation = new Vector2(floor.x - other.floor.x, floor.z - other.floor.z);
            if (separation.sqrMagnitude < minimum * minimum) return false;
        }

        float bottom = Mathf.Max(0.08f, footprint.baseOffset + footprint.minimumY);
        float top = Mathf.Max(bottom + 0.1f, footprint.baseOffset + footprint.maximumY);
        float capsuleRadius = Mathf.Min(footprint.radius, (top - bottom) * 0.5f);
        Vector3 lower = floor + Vector3.up * (bottom + capsuleRadius);
        Vector3 upper = floor + Vector3.up * (top - capsuleRadius);
        foreach (var collider in Physics.OverlapCapsule(lower, upper, capsuleRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            // Supporting floor seams are harmless. A higher floor/ceiling still obstructs the enemy body.
            if (IsGround(collider.transform, room.transform, groundRoots) &&
                collider.bounds.max.y <= floor.y + 0.2f) continue;
            return false;
        }
        return true;
    }

    private static bool IsGround(Transform candidate, Transform room, HashSet<Transform> groundRoots)
    {
        while (candidate != null)
        {
            if (groundRoots.Contains(candidate)) return true;
            if (candidate == room) break;
            candidate = candidate.parent;
        }
        return false;
    }

    private static Footprint GetFootprint(GameObject prefab, NavMeshAgent agent)
    {
        Vector3 scale = prefab.transform.lossyScale;
        float horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float verticalScale = Mathf.Abs(scale.y);
        var footprint = new Footprint
        {
            radius = Mathf.Max(0.1f, agent.radius * horizontalScale),
            baseOffset = agent.baseOffset * verticalScale,
            minimumY = 0f,
            maximumY = Mathf.Max(0.2f, agent.height * verticalScale)
        };
        bool hasBounds = false;
        Bounds bounds = default;
        foreach (var collider in prefab.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled || collider.isTrigger || !IsActivePrefabChild(collider.transform, prefab.transform)) continue;
            if (!TryColliderLocalBounds(collider, out Bounds local)) continue;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3(x, y, z));
                Vector3 point = prefab.transform.InverseTransformPoint(collider.transform.TransformPoint(corner));
                point = Vector3.Scale(point, scale);
                if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; }
                else bounds.Encapsulate(point);
            }
        }
        if (hasBounds)
        {
            footprint.radius = Mathf.Max(footprint.radius, Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x),
                Mathf.Abs(bounds.min.z), Mathf.Abs(bounds.max.z));
            footprint.minimumY = bounds.min.y;
            footprint.maximumY = bounds.max.y;
        }
        return footprint;
    }

    private static bool IsActivePrefabChild(Transform child, Transform root)
    {
        for (var current = child; current != null; current = current.parent)
        {
            if (!current.gameObject.activeSelf) return false;
            if (current == root) return true;
        }
        return false;
    }

    private static bool TryColliderLocalBounds(Collider collider, out Bounds bounds)
    {
        if (collider is BoxCollider box) bounds = new Bounds(box.center, box.size);
        else if (collider is SphereCollider sphere) bounds = new Bounds(sphere.center, Vector3.one * sphere.radius * 2f);
        else if (collider is CapsuleCollider capsule)
        {
            Vector3 size = Vector3.one * capsule.radius * 2f;
            size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
            bounds = new Bounds(capsule.center, size);
        }
        else if (collider is MeshCollider mesh && mesh.sharedMesh != null) bounds = mesh.sharedMesh.bounds;
        else { bounds = default; return false; }
        return true;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private struct Footprint
    {
        public float radius;
        public float baseOffset;
        public float minimumY;
        public float maximumY;
    }

    private struct OccupiedSpace
    {
        public Vector3 floor;
        public float radius;
    }
}
