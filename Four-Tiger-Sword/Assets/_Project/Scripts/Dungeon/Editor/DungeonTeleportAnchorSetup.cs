using System;
using System.Collections.Generic;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Authors only missing fast-travel anchors on the active run's main-room prefabs.</summary>
public static class DungeonTeleportAnchorSetup
{
    private sealed class ProposedAnchor
    {
        public string Path;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
    }

    [MenuItem("Tools/Studio Baekho/Dungeon/Install Teleport Anchors")]
    private static void InstallMenu() => Debug.Log(Install());
    [MenuItem("Tools/Studio Baekho/Dungeon/Validate Teleport Anchors")]
    private static void ValidateMenu() => Debug.Log(Validate());

    public static string Install() => Inspect(true);
    public static string Validate() => Inspect(false);

    /// <summary>Validates an existing anchor or authors a missing one on a disposable room instance. Never saves an asset.</summary>
    public static bool EnsureRoomAnchor(BaekhoMapModule room, CharacterController controller)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Author room teleport anchors in Edit Mode.");
        if (room == null || controller == null) throw new ArgumentException("A room instance and actual player CharacterController are required.");
        if (EditorUtility.IsPersistent(room) || !room.gameObject.scene.IsValid())
            throw new InvalidOperationException("Use a loaded room instance; source prefab assets are read-only.");
        var anchors = room.GetComponent<StageMapAnchors>();
        var point = anchors != null ? anchors.teleportPoint : null;
        if (point == null) point = room.transform.Find("TeleportPoint");
        Physics.SyncTransforms();
        if (point != null)
        {
            if (!DungeonFastTravelLanding.TryResolve(room, point, controller, out _, out _, out string reason))
                throw new InvalidOperationException("Existing TeleportPoint was preserved but is invalid: " + reason);
        }
        else if (!TryFindPoint(room, controller, out point))
        {
            if (point != null) Object.DestroyImmediate(point.gameObject);
            throw new InvalidOperationException("No safe full-player teleport landing was found in " + room.name + ".");
        }
        if (anchors != null && anchors.teleportPoint == point) return false;
        anchors ??= room.gameObject.AddComponent<StageMapAnchors>();
        anchors.teleportPoint = point;
        return true;
    }

    private static string Inspect(bool install)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Author teleport anchors in Edit Mode.");
        var run = ActiveRun();
        var controller = run.player != null ? run.player.GetComponent<CharacterController>() : null;
        if (controller == null) throw new InvalidOperationException("The active run needs its player's CharacterController.");
        var prefabs = MainRooms(run.definition);
        var scene = EditorSceneManager.NewPreviewScene();
        var proposals = new List<ProposedAnchor>();
        var errors = new List<string>();
        try
        {
            foreach (var source in prefabs)
            {
                string path = AssetDatabase.GetAssetPath(source);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                { errors.Add(source.name + ": main rooms must be prefab assets."); continue; }
                GameObject instance = null;
                try
                {
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(source.gameObject, scene);
                    instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var room = instance.GetComponent<BaekhoMapModule>();
                    var anchors = instance.GetComponent<StageMapAnchors>();
                    var point = anchors != null ? anchors.teleportPoint : null;
                    bool bound = point != null;
                    if (point == null) point = instance.transform.Find("TeleportPoint");
                    Physics.SyncTransforms();
                    if (point != null)
                    {
                        if (!DungeonFastTravelLanding.TryResolve(room, point, controller, out _, out _, out string reason))
                            errors.Add(path + ": existing TeleportPoint was preserved but is invalid: " + reason);
                        else if (!bound && install) proposals.Add(Proposal(path, room, point));
                        else if (!bound) errors.Add(path + ": TeleportPoint exists but StageMapAnchors.teleportPoint is unassigned.");
                    }
                    else if (!install) errors.Add(path + ": StageMapAnchors.teleportPoint is missing.");
                    else if (TryFindPoint(room, controller, out point)) proposals.Add(Proposal(path, room, point));
                    else errors.Add(path + ": no safe point was found; author a TeleportPoint manually and run validation.");
                }
                catch (Exception error) { errors.Add(path + ": " + error.Message); }
                finally { if (instance != null) Object.DestroyImmediate(instance); }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        if (errors.Count > 0) throw new InvalidOperationException("Teleport anchor validation failed; no prefabs were saved.\n" + string.Join("\n", errors));
        // Every existing/new pose has passed first, so an invalid room cannot leave a partial installation.
        foreach (var proposal in proposals) Save(proposal);
        return $"Teleport anchors: {prefabs.Count} unique active-run main-room prefabs validated; "
            + (install ? $"{proposals.Count} missing anchors installed. Existing anchors and room art preserved." : "no assets changed.");
    }

    private static RunManager ActiveRun()
    {
        RunManager found = null;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var run in root.GetComponentsInChildren<RunManager>(true))
            {
                if (found != null) throw new InvalidOperationException("The active scene must have exactly one RunManager.");
                found = run;
            }
        if (found == null || found.definition == null) throw new InvalidOperationException("The active scene has no configured RunManager.");
        return found;
    }

    private static List<BaekhoMapModule> MainRooms(RunDefinitionSO definition)
    {
        var unique = new HashSet<BaekhoMapModule>();
        if (definition.chapters == null) throw new InvalidOperationException("Run chapters are missing.");
        foreach (var chapter in definition.chapters)
        {
            if (chapter?.stages == null) throw new InvalidOperationException("Run stages are missing.");
            foreach (var stage in chapter.stages)
            {
                var settings = stage?.dungeonSettings;
                if (settings == null) throw new InvalidOperationException("A stage's dungeon settings are missing.");
                foreach (var room in settings.maps ?? Array.Empty<BaekhoMapModule>())
                {
                    if (room == null) throw new InvalidOperationException(settings.name + " has a missing ordinary room.");
                    unique.Add(room);
                }
                if (settings.startRoom != null) unique.Add(settings.startRoom);
                if (settings.endRoom != null) unique.Add(settings.endRoom);
                if (settings.bossRoom != null) unique.Add(settings.bossRoom);
                if (settings.shopRoom != null) unique.Add(settings.shopRoom);
            }
        }
        var rooms = new List<BaekhoMapModule>(unique);
        rooms.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
        if (rooms.Count == 0) throw new InvalidOperationException("The active run has no main-room prefabs.");
        foreach (var room in rooms) if (room.isCornerRoom) throw new InvalidOperationException(room.name + " is a corner in a main-room slot.");
        return rooms;
    }

    private static bool TryFindPoint(BaekhoMapModule room, CharacterController controller, out Transform point)
    {
        point = new GameObject("TeleportPoint").transform;
        point.SetParent(room.transform, false);
        var ground = DungeonFastTravelLanding.GroundRoots(room);
        var colliders = new HashSet<Collider>();
        foreach (var root in ground)
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                if (collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy) colliders.Add(collider);
        var floors = new List<Vector3>();
        // The floor may be below a roof or on a raised platform: sample authored ground colliders directly.
        foreach (var collider in colliders)
        {
            var bounds = collider.bounds;
            AddFloor(colliders, new Vector3(bounds.center.x, room.transform.TransformPoint(room.localBounds.max).y + 1f,
                bounds.center.z), room.localBounds.size.y + 2f, floors);
        }
        var localBounds = room.localBounds;
        const float grid = 1f;
        for (float x = localBounds.min.x + .5f; x < localBounds.max.x; x += grid)
            for (float z = localBounds.min.z + .5f; z < localBounds.max.z; z += grid)
                AddFloor(colliders, room.transform.TransformPoint(new Vector3(x, localBounds.max.y + 1f, z)),
                    localBounds.size.y + 2f, floors);
        var center = room.transform.TransformPoint(localBounds.center);
        floors.Sort((a, b) =>
        {
            float da = new Vector2(a.x - center.x, a.z - center.z).sqrMagnitude;
            float db = new Vector2(b.x - center.x, b.z - center.z).sqrMagnitude;
            int order = da.CompareTo(db);
            if (order != 0) return order;
            order = a.y.CompareTo(b.y);
            if (order != 0) return order;
            order = a.x.CompareTo(b.x);
            return order != 0 ? order : a.z.CompareTo(b.z);
        });
        foreach (var floor in floors)
        {
            Vector3 facing = room.transform.forward;
            if (room.connections != null && room.connections.Length > 0 && room.connections[0] != null)
                facing = room.connections[0].transform.position - floor;
            facing.y = 0f;
            var rotation = facing.sqrMagnitude > .0001f ? Quaternion.LookRotation(facing.normalized, Vector3.up) : Quaternion.identity;
            point.SetPositionAndRotation(DungeonFastTravelLanding.PlayerRootAtFloor(floor, rotation, controller), rotation);
            if (DungeonFastTravelLanding.TryResolve(room, point, controller, out _, out _, out _)) return true;
        }
        return false;
    }

    private static void AddFloor(HashSet<Collider> colliders, Vector3 origin, float distance, List<Vector3> floors)
    {
        bool found = false;
        var nearest = default(RaycastHit);
        foreach (var collider in colliders)
        {
            var bounds = collider.bounds;
            if (origin.x < bounds.min.x || origin.x > bounds.max.x || origin.z < bounds.min.z || origin.z > bounds.max.z) continue;
            if (collider.Raycast(new Ray(origin, Vector3.down), out var hit, distance) && (!found || hit.distance < nearest.distance))
            { nearest = hit; found = true; }
        }
        if (found) floors.Add(nearest.point);
    }

    private static ProposedAnchor Proposal(string path, BaekhoMapModule room, Transform point)
        => new() { Path = path, LocalPosition = room.transform.InverseTransformPoint(point.position),
            LocalRotation = Quaternion.Inverse(room.transform.rotation) * point.rotation };

    private static void Save(ProposedAnchor proposal)
    {
        var root = PrefabUtility.LoadPrefabContents(proposal.Path);
        try
        {
            var anchors = root.GetComponent<StageMapAnchors>() ?? root.AddComponent<StageMapAnchors>();
            if (anchors.teleportPoint != null) return;
            var point = root.transform.Find("TeleportPoint");
            if (point == null)
            {
                point = new GameObject("TeleportPoint").transform;
                point.SetParent(root.transform, false);
                point.localPosition = proposal.LocalPosition;
                point.localRotation = proposal.LocalRotation;
            }
            anchors.teleportPoint = point;
            if (PrefabUtility.SaveAsPrefabAsset(root, proposal.Path) == null)
                throw new InvalidOperationException("Could not save teleport anchor: " + proposal.Path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
