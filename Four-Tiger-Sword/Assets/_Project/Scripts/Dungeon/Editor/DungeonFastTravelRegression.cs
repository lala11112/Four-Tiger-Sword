using System;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Landing checks in a separate physics scene, with no production assets changed.</summary>
public static class DungeonFastTravelRegression
{
    private static int _checks;

    [MenuItem("Tools/Studio Baekho/Dungeon/Run Fast Travel Landing Regression")]
    private static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run fast-travel landing checks in Edit Mode.");
        _checks = 0;
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var player = InScene("Capsule geometry", scene).AddComponent<CharacterController>();
            player.height = 1.56f;
            player.radius = .23f;
            player.center = new Vector3(-.007f, -.215f, 0f);
            player.transform.position = new Vector3(1000f, 1000f, 1000f);
            var room = InScene("Grounded room", scene).AddComponent<BaekhoMapModule>();
            room.localBounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(20f, 8f, 20f));
            room.transform.SetPositionAndRotation(new Vector3(25f, 4f, -30f), Quaternion.Euler(0f, 90f, 0f));
            var floor = Box("Own floor", room.transform, new Vector3(0f, -.25f, 0f), new Vector3(18f, .5f, 18f));
            room.groundObjects = new[] { floor.gameObject };
            var anchors = room.gameObject.AddComponent<StageMapAnchors>();
            var point = new GameObject("TeleportPoint").transform;
            point.SetParent(room.transform, false);
            anchors.teleportPoint = point;
            Vector3 floorPosition = room.transform.position;
            point.SetPositionAndRotation(DungeonFastTravelLanding.PlayerRootAtFloor(floorPosition, room.transform.rotation, player), room.transform.rotation);
            Physics.SyncTransforms();
            Check(DungeonFastTravelLanding.TryResolve(room, point, player, out var root, out var rotation, out _),
                "A clear authored point over its own floor was rejected.");
            Check(Vector3.Distance(root, point.position) < .0001f && Quaternion.Angle(rotation, point.rotation) < .001f,
                "Runtime changed the authored player-root pose.");
            Check(Mathf.Abs(root.y - (floorPosition.y + .78f + .215f + DungeonFastTravelLanding.LandingLift)) < .001f,
                "CharacterController center offset was not included in authoring height.");
            Check(!DungeonFastTravelLanding.TryResolve(room, null, player, out _, out _, out _), "A missing anchor was accepted.");

            var wall = Box("Blocking wall", room.transform, new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 1f));
            Physics.SyncTransforms();
            Check(!DungeonFastTravelLanding.TryResolve(room, point, player, out _, out _, out _), "A blocked capsule was accepted.");
            wall.enabled = false;
            var ceiling = Box("Low ceiling", room.transform, new Vector3(0f, 1.3f, 0f), new Vector3(2f, .2f, 2f));
            Physics.SyncTransforms();
            Check(!DungeonFastTravelLanding.TryResolve(room, point, player, out _, out _, out _), "A capsule overlapping a low ceiling was accepted.");
            ceiling.enabled = false;
            room.groundObjects = Array.Empty<GameObject>();
            Check(!DungeonFastTravelLanding.TryResolve(room, point, player, out _, out _, out _), "Unowned ground was accepted.");
            room.groundObjects = new[] { floor.gameObject };
            floor.enabled = false;
            Physics.SyncTransforms();
            Check(!DungeonFastTravelLanding.TryResolve(room, point, player, out _, out _, out _), "A point over a void was accepted.");
            floor.enabled = true;
            floor.transform.localRotation = Quaternion.Euler(60f, 0f, 0f);
            Physics.SyncTransforms();
            Check(!DungeonFastTravelLanding.TryResolve(room, point, player, out _, out _, out _), "A steep support surface was accepted.");
            floor.transform.localRotation = Quaternion.identity;

            var socket = new GameObject("Door exclusion").AddComponent<BaekhoMapConnection>();
            socket.transform.SetParent(room.transform, false);
            socket.width = 4f;
            socket.height = 3f;
            room.connections = new[] { socket };
            Physics.SyncTransforms();
            Check(!DungeonFastTravelLanding.TryResolve(room, point, player, out _, out _, out _), "A point inside a doorway was accepted.");
            room.connections = Array.Empty<BaekhoMapConnection>();
            anchors.exitPoint = socket.transform;
            Check(!DungeonFastTravelLanding.TryResolve(room, point, player, out _, out _, out _), "A point inside the automatic exit area was accepted.");
            anchors.exitPoint = null;
            anchors.shopPoint = socket.transform;
            Check(!DungeonFastTravelLanding.TryResolve(room, point, player, out _, out _, out _), "A point inside the future shop counter was accepted.");
            anchors.shopPoint = null;
            point.rotation = Quaternion.Euler(25f, 0f, 0f);
            Check(!DungeonFastTravelLanding.ValidateAnchor(room, point, out _), "A tilted player-root pose was accepted.");
            point.rotation = room.transform.rotation;
            point.position += Vector3.up * 2f;
            Check(!DungeonFastTravelLanding.TryResolve(room, point, player, out _, out _, out _), "An airborne point was accepted.");
            point.position += Vector3.right * 100f;
            Check(!DungeonFastTravelLanding.ValidateAnchor(room, point, out _), "An anchor outside its room was accepted.");

            var model = new DungeonMinimapModel();
            var service = new DungeonFastTravelService(null, model);
            service.Bind(null);
            Check(!service.CanTravel(1, out string reason) && !string.IsNullOrEmpty(reason), "A missing run/session enabled travel.");
            Check(!service.TryTravel(1, new BaekhoDungeonResult(), out reason) && !string.IsNullOrEmpty(reason),
                "A stale map generation was accepted.");
            return $"Fast-travel landing: {_checks} checks passed (exact root pose, capsule center, transformed room, "
                + "missing/unowned ground, void, obstacles/ceiling, door/exit/shop exclusion, invalid anchors and stale context).";
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static GameObject InScene(string name, Scene scene)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        return go;
    }

    private static BoxCollider Box(string name, Transform parent, Vector3 center, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        var box = go.AddComponent<BoxCollider>();
        box.size = size;
        return box;
    }

    private static void Check(bool condition, string message)
    {
        ++_checks;
        if (!condition) throw new InvalidOperationException("Fast-travel landing: " + message);
    }
}
