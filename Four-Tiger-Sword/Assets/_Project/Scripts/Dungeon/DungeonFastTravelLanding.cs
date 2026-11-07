using System;
using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Checks an authored player-root pose against live room geometry; NavMesh is not required.</summary>
public static class DungeonFastTravelLanding
{
    public const float LandingLift = .06f;
    private const float GroundTolerance = .18f;
    private const float DoorDepth = 2.5f;

    public static bool ValidateAnchor(BaekhoMapModule room, Transform rootPoint, out string reason)
    {
        reason = null;
        if (room == null || rootPoint == null || rootPoint == room.transform || !rootPoint.IsChildOf(room.transform))
            return Fail("이 방에 지정된 안전지점이 없습니다.", out reason);
        var local = room.transform.InverseTransformPoint(rootPoint.position);
        var bounds = room.localBounds;
        if (!Finite(local) || !Finite(rootPoint.rotation) || !Finite(bounds.center) || !Finite(bounds.size)
            || !bounds.Contains(local) || Vector3.Dot(rootPoint.up, Vector3.up) < .999f)
            return Fail("안전지점의 위치 또는 방향이 올바르지 않습니다.", out reason);
        return true;
    }

    public static bool TryResolve(BaekhoMapModule room, Transform rootPoint, CharacterController controller,
        out Vector3 position, out Quaternion rotation, out string reason)
    {
        position = default;
        rotation = Quaternion.identity;
        if (!ValidateAnchor(room, rootPoint, out reason)) return false;
        if (!TryCapsule(controller, out float radius, out float halfHeight, out var scaledCenter))
            return Fail("플레이어의 충돌 크기를 확인할 수 없습니다.", out reason);
        position = rootPoint.position;
        rotation = rootPoint.rotation.normalized;
        var center = position + rotation * scaledCenter;
        var bottom = center - Vector3.up * halfHeight;
        var groundRoots = GroundRoots(room);
        if (groundRoots.Count == 0) return Fail("이 방의 바닥 정보를 확인할 수 없습니다.", out reason);
        var physics = room.gameObject.scene.GetPhysicsScene();
        if (!physics.IsValid()) return Fail("이 방의 충돌 공간을 확인할 수 없습니다.", out reason);
        float supportRadius = radius * .8f;
        Vector3[] offsets = { Vector3.zero, Vector3.right * supportRadius, Vector3.left * supportRadius,
            Vector3.forward * supportRadius, Vector3.back * supportRadius };
        foreach (var offset in offsets)
        {
            if (!physics.Raycast(bottom + offset + Vector3.up * .3f, Vector3.down, out var hit, .65f,
                ~0, QueryTriggerInteraction.Ignore) || !OwnsGround(hit.collider.transform, room.transform, groundRoots)
                || Vector3.Angle(hit.normal, Vector3.up) > Mathf.Min(controller.slopeLimit, 45f)
                || Mathf.Abs(hit.point.y - bottom.y) > GroundTolerance)
                return Fail("안전지점 아래에 충분한 바닥이 없습니다.", out reason);
        }
        if (NearDoorOrExit(room, bottom, radius, out reason)) return false;
        var overlaps = new Collider[128];
        int count = physics.OverlapCapsule(center - Vector3.up * (halfHeight - radius),
            center + Vector3.up * (halfHeight - radius), radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
        if (count >= overlaps.Length) return Fail("도착 공간을 확인할 수 없습니다.", out reason);
        for (int i = 0; i < count; ++i)
        {
            var collider = overlaps[i];
            if (collider == null || collider.transform == controller.transform || collider.transform.IsChildOf(controller.transform)) continue;
            return Fail("도착 공간이 장애물로 막혀 있습니다.", out reason);
        }
        return true;
    }

    /// <summary>Used only by the authoring tool; runtime keeps the exact authored root pose.</summary>
    public static Vector3 PlayerRootAtFloor(Vector3 floor, Quaternion rotation, CharacterController controller)
    {
        if (!TryCapsule(controller, out _, out float halfHeight, out var scaledCenter))
            throw new ArgumentException("A valid upright CharacterController is required.");
        return floor + Vector3.up * (halfHeight + LandingLift) - rotation * scaledCenter;
    }

    public static HashSet<Transform> GroundRoots(BaekhoMapModule room)
    {
        var roots = new HashSet<Transform>();
        if (room != null) foreach (var ground in room.groundObjects ?? Array.Empty<GameObject>())
            if (ground != null && ground.transform.IsChildOf(room.transform)) roots.Add(ground.transform);
        return roots;
    }

    public static bool OwnsGround(Transform candidate, Transform room, HashSet<Transform> roots)
    {
        while (candidate != null)
        {
            if (roots.Contains(candidate)) return true;
            if (candidate == room) break;
            candidate = candidate.parent;
        }
        return false;
    }

    private static bool NearDoorOrExit(BaekhoMapModule room, Vector3 feet, float radius, out string reason)
    {
        reason = null;
        foreach (var socket in room.connections ?? Array.Empty<BaekhoMapConnection>())
        {
            if (socket == null) continue;
            var local = socket.transform.InverseTransformPoint(feet);
            if (Mathf.Abs(local.x) <= socket.width * .5f + radius + .25f
                && Mathf.Abs(local.z) <= DoorDepth + radius && Mathf.Abs(local.y) <= Mathf.Max(3f, socket.height))
            { reason = "안전지점이 출입문에 너무 가깝습니다."; return true; }
        }
        var anchors = room.GetComponent<StageMapAnchors>();
        if (anchors != null && anchors.exitPoint != null)
        {
            var local = anchors.exitPoint.InverseTransformPoint(feet);
            if (Mathf.Abs(local.x) <= anchors.exitSize.x * .5f + radius + .5f
                && Mathf.Abs(local.z) <= anchors.exitSize.z * .5f + radius + .5f
                && Mathf.Abs(local.y) <= anchors.exitSize.y + .5f)
            { reason = "안전지점이 스테이지 출구에 너무 가깝습니다."; return true; }
        }
        if (anchors != null && anchors.shopPoint != null)
        {
            var delta = feet - anchors.shopPoint.position;
            if (new Vector2(delta.x, delta.z).sqrMagnitude < 4f && Mathf.Abs(delta.y) < 3f)
            { reason = "안전지점이 상점 카운터에 너무 가깝습니다."; return true; }
        }
        return false;
    }

    private static bool TryCapsule(CharacterController controller, out float radius, out float halfHeight, out Vector3 center)
    {
        radius = halfHeight = 0f;
        center = default;
        if (controller == null) return false;
        var scale = controller.transform.lossyScale;
        if (!Finite(scale) || scale.x <= 0f || scale.y <= 0f || scale.z <= 0f
            || Vector3.Dot(controller.transform.up, Vector3.up) < .999f) return false;
        radius = controller.radius * Mathf.Max(scale.x, scale.z);
        halfHeight = Mathf.Max(controller.height * scale.y * .5f, radius);
        center = Vector3.Scale(controller.center, scale);
        return Finite(radius) && Finite(halfHeight) && Finite(center) && radius > .001f;
    }

    private static bool Fail(string value, out string reason) { reason = value; return false; }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z)
        && Finite(value.w) && Quaternion.Dot(value, value) > .0001f;
}
