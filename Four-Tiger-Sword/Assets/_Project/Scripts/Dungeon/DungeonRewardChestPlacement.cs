using System;
using System.Collections.Generic;
using System.Linq;
using StudioBaekho;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

internal static class DungeonRewardChestPlacement
{
    // The crate is 1.8 × .95 × .9 m at authored scale. Reserve a small margin around its footprint.
    private static readonly Vector3 HalfExtents = new(.8f, .36f, .46f);
    private static readonly Vector3[] Directions =
    {
        Vector3.forward, Vector3.right, Vector3.left, Vector3.back,
        new Vector3(1, 0, 1).normalized, new Vector3(-1, 0, 1).normalized,
        new Vector3(1, 0, -1).normalized, new Vector3(-1, 0, -1).normalized
    };
    private static readonly float[] Rings = { 2f, 3f };
    private static readonly Vector3[] FloorSamples =
    {
        new(-.72f, 0, -.38f), new(-.72f, 0, .38f), new(.72f, 0, -.38f), new(.72f, 0, .38f)
    };

    internal static bool TryFind(BaekhoMapModule room, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (room == null) return false;
        var physics = room.gameObject.scene.GetPhysicsScene();
        var ground = DungeonFastTravelLanding.GroundRoots(room);
        if (!physics.IsValid() || ground.Count == 0) return false;

        var anchors = room.GetComponent<StageMapAnchors>();
        var teleport = anchors != null ? anchors.teleportPoint : null;
        Vector3 anchorFloor = default;
        bool hasAnchor = DungeonFastTravelLanding.ValidateAnchor(room, teleport, out _)
            && TryFloor(room, physics, ground, teleport.position, out anchorFloor);
        var overlaps = new Collider[24];
        var path = new NavMeshPath();
        bool hasStart = NavMesh.SamplePosition(anchorFloor, out var start, .65f, NavMesh.AllAreas) && hasAnchor;
        Vector3 reference = hasAnchor ? anchorFloor : room.transform.TransformPoint(room.localBounds.center);

        if (hasAnchor)
        {
            foreach (float radius in Rings)
                foreach (Vector3 direction in Directions)
                {
                    Vector3 desired = anchorFloor + room.transform.TransformDirection(direction) * radius;
                    if (TryCandidate(desired, out position, out rotation)) return true;
                }
        }

        var markers = room.GetComponentsInChildren<BaekhoGameplayMarker>(false)
            .Where(marker => marker.kind == "enemy").OrderBy(marker => marker.id, StringComparer.Ordinal);
        foreach (var marker in markers)
            if (TryCandidate(marker.transform.position, out position, out rotation)) return true;

        // The authored teleport floor is a last resort. The chest has no solid collider,
        // so even this fallback cannot trap a player traveling to the room.
        if (!hasAnchor) return false;
        position = anchorFloor;
        rotation = teleport.rotation;
        return true;

        bool TryCandidate(Vector3 desired, out Vector3 floor, out Quaternion facing)
        {
            facing = Quaternion.identity;
            if (!TryFloor(room, physics, ground, desired, out floor) || NearDoorOrExit(room, anchors, floor)
                || (hasAnchor && new Vector2(floor.x - anchorFloor.x, floor.z - anchorFloor.z).sqrMagnitude < 1.4f * 1.4f))
                return false;
            Vector3 forward = Vector3.ProjectOnPlane(reference - floor, Vector3.up);
            if (forward.sqrMagnitude < .01f) forward = room.transform.forward;
            facing = Quaternion.LookRotation(forward);
            foreach (Vector3 offset in FloorSamples)
                if (!TryFloor(room, physics, ground, floor + facing * offset, out var corner)
                    || Mathf.Abs(corner.y - floor.y) > .15f) return false;

            int count = physics.OverlapBox(floor + Vector3.up * .4f, HalfExtents, overlaps, facing,
                ~0, QueryTriggerInteraction.Ignore);
            if (count >= overlaps.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var collider = overlaps[i];
                if (!DungeonFastTravelLanding.OwnsGround(collider.transform, room.transform, ground)
                    || collider.bounds.max.y > floor.y + .08f) return false;
            }

            Vector3 approach = floor + facing * Vector3.forward * .95f;
            if (!TryFloor(room, physics, ground, approach, out var approachFloor)
                || !NavMesh.SamplePosition(approachFloor, out var end, .55f, NavMesh.AllAreas)) return false;
            return !hasStart || (NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete);
        }
    }

    private static bool TryFloor(BaekhoMapModule room, PhysicsScene physics, HashSet<Transform> ground,
        Vector3 desired, out Vector3 floor)
    {
        floor = default;
        if (!physics.Raycast(desired + Vector3.up * .65f, Vector3.down, out var hit, 2.2f,
            ~LayerMask.GetMask("Player", "Enemy", "PlayerSkill", "EnemySkill"), QueryTriggerInteraction.Ignore)
            || !DungeonFastTravelLanding.OwnsGround(hit.collider.transform, room.transform, ground)
            || hit.normal.y < .9f) return false;
        var bounds = room.localBounds;
        bounds.Expand(.2f);
        if (!bounds.Contains(room.transform.InverseTransformPoint(hit.point))) return false;
        floor = hit.point;
        return true;
    }

    private static bool NearDoorOrExit(BaekhoMapModule room, StageMapAnchors anchors, Vector3 floor)
    {
        foreach (var socket in room.connections ?? Array.Empty<BaekhoMapConnection>())
        {
            if (socket == null) continue;
            Vector3 local = socket.transform.InverseTransformPoint(floor);
            if (Mathf.Abs(local.x) < socket.width * .5f + 1f && Mathf.Abs(local.z) < 2.5f && Mathf.Abs(local.y) < 2f)
                return true;
        }
        if (anchors == null || anchors.exitPoint == null) return false;
        Vector3 exit = anchors.exitPoint.InverseTransformPoint(floor);
        return Mathf.Abs(exit.x) < anchors.exitSize.x * .5f + 1f
            && Mathf.Abs(exit.z) < anchors.exitSize.z * .5f + 1f && Mathf.Abs(exit.y) < anchors.exitSize.y;
    }
}
