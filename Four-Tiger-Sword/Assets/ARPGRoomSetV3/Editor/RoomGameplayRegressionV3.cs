#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabDoor;
using StudioBaekho;
using Unity.AI.Navigation;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Checks the repaired assets and the room pools used by actual run stages.</summary>
public static class RoomGameplayRegressionV3
{
    [Serializable] public sealed class Report
    {
        public bool passed;
        public int checks, sourcePrefabs, restoredSourceColliders, restoredSourceMetadata;
        public int rooms, equipmentColliders, collisionMeshesRaycast;
        public string scope = "Source collision recovery, V3 room gameplay metadata and native collision shapes, authored navigation assets, and actual stage layout planning. Does not simulate player combat.";
        public List<string> errors = new List<string>();
        public List<RoomResult> roomResults = new List<RoomResult>();
        public List<SettingsResult> settingsResults = new List<SettingsResult>();
    }

    [Serializable] public sealed class RoomResult
    {
        public string name, prefab;
        public int equipmentColliders, enemyMarkers, groundRoots, connections;
        public bool navigation;
    }

    [Serializable] public sealed class SettingsResult
    {
        public string asset;
        public int ordinaryPool, configuredV3Rooms, successfulSeeds, v3Instances;
        public List<string> selectedV3Rooms = new List<string>();
    }

    static readonly string[] RestoredProps =
    {
        "01_power_cabinet", "02_terminal_kiosk", "03_server_rack", "04_control_server_rack",
        "05_green_specimen_tank", "06_standing_terminal", "10_blue_storage_case", "11_toolbox",
        "12_green_gas_cylinder", "13_brown_gas_cylinder", "15_computer_desk", "18_lab_chair",
        "19_slanted_control_console", "20_heavy_control_console", "21_gray_hazard_drum",
        "22_red_hazard_drum", "23_blue_hazard_drum"
    };

    static readonly string[] StageSettings =
    {
        "Assets/_Game/RunProgression/Settings/NormalStage.asset",
        "Assets/_Game/RunProgression/Settings/BossStage.asset"
    };

    public static string Run()
    {
        var report = new Report();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Check(report, false, "Run the asset regression in Edit Mode.");
            return JsonUtility.ToJson(report, true);
        }

        CheckSources(report);
        var manifest = JsonUtility.FromJson<RoomSetV3.SetReport>(File.ReadAllText(
            RoomSetV3.Root + "/Manifests/RoomSet_manifest.json"));
        Check(report, manifest != null && manifest.rooms != null && manifest.rooms.Length == 22,
            "Expected the complete 22-room V3 manifest.");
        if (manifest == null || manifest.rooms == null) return JsonUtility.ToJson(report, true);

        var modules = new HashSet<BaekhoMapModule>();
        var testedMeshes = new HashSet<Mesh>();
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (var record in manifest.rooms)
            {
                try { CheckRoom(report, record, modules, testedMeshes, scene); }
                catch (Exception exception) { Check(report, false, record.prefab + ": " + exception.Message); }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        Check(report, report.collisionMeshesRaycast > 0, "No V3 equipment mesh was verified against a native collider.");

        foreach (var path in StageSettings)
        {
            try { CheckSettings(report, path, modules); }
            catch (Exception exception) { Check(report, false, path + ": " + exception.Message); }
        }
        report.passed = report.errors.Count == 0;
        return JsonUtility.ToJson(report, true);
    }

    static void CheckSources(Report report)
    {
        foreach (var name in RestoredProps)
        {
            string path = "Assets/LabProps29/Prefabs/" + name + ".prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!Check(report, source, "Missing recovered source prefab: " + path)) continue;
            report.sourcePrefabs++;
            var colliders = source.GetComponentsInChildren<Collider>(true).Where(Solid).ToArray();
            report.restoredSourceColliders += colliders.Length;
            Check(report, colliders.Length >= (name == "05_green_specimen_tank" ? 2 : 1),
                "Recovered source collision is incomplete: " + path);
            Check(report, colliders.All(ValidMesh), "Recovered source contains an empty MeshCollider: " + path);
            var metadata = source.GetComponent<BaekhoMapAsset>();
            if (metadata) report.restoredSourceMetadata++;
            Check(report, metadata, "Recovered source has no BaekhoMapAsset registration: " + path);
        }
        Check(report, report.restoredSourceColliders >= 18 && report.restoredSourceMetadata == 17,
            "The 17 overwritten sources must recover at least 18 solid colliders and all 17 registrations.");
    }

    static void CheckRoom(Report report, RoomSetV3.RoomReport record, HashSet<BaekhoMapModule> modules,
        HashSet<Mesh> testedMeshes, Scene preview)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(record.prefab);
        if (!Check(report, prefab, "Missing V3 room: " + record.prefab)) return;
        report.rooms++;
        var row = new RoomResult { name = record.name, prefab = record.prefab };
        report.roomResults.Add(row);
        var equipment = RoomCollisionV3.Equipment(prefab);
        Check(report, RoomCollisionV3.HasValidEquipmentCollision(prefab),
            record.name + ": missing solid equipment collision or MeshCollider mesh.");
        var equipmentColliders = equipment ? equipment.GetComponentsInChildren<Collider>(true).Where(Solid).ToArray()
            : Array.Empty<Collider>();
        row.equipmentColliders = equipmentColliders.Length;
        report.equipmentColliders += equipmentColliders.Length;
        Check(report, prefab.GetComponentsInChildren<Collider>(true).All(ValidMesh),
            record.name + ": a room MeshCollider has no mesh.");
        foreach (var collider in equipmentColliders.OfType<MeshCollider>())
        {
            if (!collider.sharedMesh || !testedMeshes.Add(collider.sharedMesh)) continue;
            Check(report, RaycastMesh(collider.sharedMesh, preview),
                record.name + ": native collision ray did not hit equipment mesh " + collider.sharedMesh.name);
            report.collisionMeshesRaycast++;
            // One independently cooked native triangle shape per room is sufficient here.
            break;
        }

        var module = prefab.GetComponent<BaekhoMapModule>();
        if (!Check(report, module, record.name + ": missing BaekhoMapModule.")) return;
        modules.Add(module);
        Check(report, !module.isCornerRoom, record.name + ": authored room is misclassified as a corridor corner.");
        var grounds = module.groundObjects ?? Array.Empty<GameObject>();
        row.groundRoots = grounds.Length;
        Check(report, grounds.Length > 0 && grounds.All(g => g && g.transform.IsChildOf(prefab.transform)
            && g.GetComponentsInChildren<Collider>(true).Any(Solid)), record.name + ": invalid physical ground roots.");
        row.enemyMarkers = prefab.GetComponentsInChildren<BaekhoGameplayMarker>(true)
            .Count(m => m.kind == "enemy" && ActiveInPrefab(m.transform, prefab.transform));
        Check(report, module.encounterBudget == 0 || row.enemyMarkers > 0,
            record.name + ": combat room has no active enemy marker.");
        var surfaces = prefab.GetComponents<NavMeshSurface>();
        row.navigation = surfaces.Any(s => s.enabled && s.agentTypeID == 0 && s.navMeshData);
        Check(report, row.navigation, record.name + ": missing enabled, baked type-0 room navigation.");
        var connections = module.connections ?? Array.Empty<BaekhoMapConnection>();
        row.connections = connections.Length;
        var sourceMap = JsonUtility.FromJson<RoomSetV3.Map>(File.ReadAllText(record.sourceAsset));
        var exteriorDoors = (sourceMap.connections ?? Array.Empty<RoomSetV3.Connection>())
            .Where(c => string.IsNullOrEmpty(c.kind) || c.kind == "door").ToArray();
        Check(report, connections.Length == exteriorDoors.Length && connections.Length > 0,
            record.name + ": runtime socket count differs from the source exterior connections.");
        Check(report, prefab.GetComponentsInChildren<SlidingDoor>(true).Length == record.doors,
            record.name + ": physical door count differs from the room manifest, including internal doors.");
        Check(report, exteriorDoors.All(expected => connections.Any(actual => actual && actual.id == expected.id
            && Vector3.Distance(prefab.transform.InverseTransformPoint(actual.transform.position), expected.position) < .02f
            && Quaternion.Angle(Quaternion.Inverse(prefab.transform.rotation) * actual.transform.rotation,
                Quaternion.Euler(expected.rotation)) < .2f)),
            record.name + ": an exterior runtime socket differs from its authored position or rotation.");
        Check(report, connections.All(c => c && c.transform.IsChildOf(prefab.transform)
            && c.runtimeDoor && c.runtimeDoor.transform.IsChildOf(prefab.transform)
            && c.runtimeDoor.GetComponentInChildren<SlidingDoor>(true)),
            record.name + ": a socket has no corresponding physical runtime door.");
        Check(report, prefab.GetComponentsInChildren<SlidingDoor>(true)
            .Where(door => !connections.Any(socket => socket && socket.runtimeDoor
                && (door.gameObject == socket.runtimeDoor || door.transform.IsChildOf(socket.runtimeDoor.transform))))
            .All(door => door.OpenAmount >= .99f),
            record.name + ": an internal door without a socket controller blocks the authored route.");
        Check(report, connections.Where(c => c).Select(c => c.id).Distinct().Count() == connections.Length,
            record.name + ": duplicate socket identities.");
    }

    static void CheckSettings(Report report, string path, HashSet<BaekhoMapModule> v3)
    {
        var settings = AssetDatabase.LoadAssetAtPath<BaekhoDungeonSettings>(path);
        if (!Check(report, settings, "Missing actual stage settings: " + path)) return;
        var row = new SettingsResult { asset = path, ordinaryPool = settings.maps == null ? 0 : settings.maps.Length };
        report.settingsResults.Add(row);
        var configured = new HashSet<BaekhoMapModule>((settings.maps ?? Array.Empty<BaekhoMapModule>())
            .Concat(new[] { settings.startRoom, settings.endRoom, settings.bossRoom, settings.shopRoom }).Where(m => m));
        row.configuredV3Rooms = v3.Count(configured.Contains);
        Check(report, v3.Count == 22 && row.configuredV3Rooms == 22, path + ": not all 22 V3 rooms are configured.");
        var seen = new HashSet<string>();
        for (int seed = 0; seed < 10; seed++)
        {
            if (!Check(report, BaekhoDungeonPlanner.TryPlan(settings, seed, out var plan, out var error),
                path + " seed " + seed + ": " + error)) continue;
            row.successfulSeeds++;
            CheckPlan(report, path + " seed " + seed, settings, plan);
            foreach (var placed in plan.rooms.Where(r => v3.Contains(r.prefab)))
            {
                row.v3Instances++;
                seen.Add(placed.prefab.name);
            }
        }
        row.selectedV3Rooms = seen.OrderBy(n => n, StringComparer.Ordinal).ToList();
        Check(report, row.successfulSeeds == 10 && row.v3Instances > 0,
            path + ": ten successful real-stage layouts must actually select V3 rooms.");
    }

    static void CheckPlan(Report report, string label, BaekhoDungeonSettings settings, BaekhoDungeonPlan plan)
    {
        Check(report, plan.rooms.Count == settings.mapCount && plan.links.Count == Mathf.Max(0, settings.mapCount - 1),
            label + ": incomplete room/link counts.");
        var rooms = plan.rooms.ToDictionary(r => r.id);
        var adjacency = rooms.Keys.ToDictionary(id => id, id => new List<int>());
        var used = new HashSet<string>();
        foreach (var link in plan.links)
        {
            if (!Check(report, rooms.ContainsKey(link.fromMapId) && rooms.ContainsKey(link.toMapId)
                && link.fromMapId != link.toMapId, label + ": invalid graph edge.")) continue;
            adjacency[link.fromMapId].Add(link.toMapId);
            adjacency[link.toMapId].Add(link.fromMapId);
            Check(report, used.Add(link.fromMapId + ":" + link.fromSocketIndex)
                && used.Add(link.toMapId + ":" + link.toSocketIndex), label + ": a doorway is reused.");
            var from = rooms[link.fromMapId];
            var to = rooms[link.toMapId];
            var start = from.SocketPosition(link.fromSocketIndex);
            var end = to.SocketPosition(link.toSocketIndex);
            Check(report, link.segments.Count > 0
                && Vector3.Distance(link.segments[0].start, start) < .01f
                && Vector3.Distance(link.segments[link.segments.Count - 1].end, end) < .01f,
                label + ": corridor endpoints miss their door sockets.");
            Check(report, Mathf.Abs(start.y - end.y) < .01f
                && link.segments.All(s => Mathf.Abs(s.start.y - start.y) < .01f && Mathf.Abs(s.end.y - start.y) < .01f),
                label + ": room doorway and corridor floor heights differ.");
        }
        var visited = new HashSet<int>();
        var pending = new Queue<int>();
        if (plan.rooms.Count > 0) pending.Enqueue(plan.rooms[0].id);
        while (pending.Count > 0)
        {
            int id = pending.Dequeue();
            if (!visited.Add(id)) continue;
            foreach (int next in adjacency[id]) pending.Enqueue(next);
        }
        Check(report, visited.Count == plan.rooms.Count, label + ": generated graph is disconnected.");
    }

    static bool RaycastMesh(Mesh mesh, Scene preview)
    {
        Vector3 a = default, b = default, c = default;
        bool triangle = false;
        using (var snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh))
        {
            var data = snapshot[0];
            using (var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
            {
                data.GetVertices(vertices);
                for (int sub = 0; sub < data.subMeshCount && !triangle; sub++)
                {
                    var descriptor = data.GetSubMesh(sub);
                    if (descriptor.topology != MeshTopology.Triangles) continue;
                    using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                    {
                        data.GetIndices(indices, sub, true);
                        for (int i = 0; i + 2 < indices.Length; i += 3)
                        {
                            a = vertices[indices[i]]; b = vertices[indices[i + 1]]; c = vertices[indices[i + 2]];
                            if (Vector3.Cross(b - a, c - a).sqrMagnitude < .00000001f) continue;
                            triangle = true;
                            break;
                        }
                    }
                }
            }
        }
        if (!triangle) return false;
        var probe = new GameObject("V3 collision regression probe");
        SceneManager.MoveGameObjectToScene(probe, preview);
        try
        {
            var collider = probe.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            var normal = Vector3.Cross(b - a, c - a).normalized;
            var center = (a + b + c) / 3f;
            return collider.Raycast(new Ray(center + normal * .02f, -normal), out var hit, .04f)
                && Vector3.Distance(hit.point, center) < .025f;
        }
        finally { UnityEngine.Object.DestroyImmediate(probe); }
    }

    static bool ActiveInPrefab(Transform child, Transform root)
    {
        for (var current = child; current; current = current.parent)
        {
            if (!current.gameObject.activeSelf) return false;
            if (current == root) return true;
        }
        return false;
    }

    static bool Solid(Collider collider) => collider && collider.enabled && !collider.isTrigger;
    static bool ValidMesh(Collider collider) => !(collider is MeshCollider mesh) || mesh.sharedMesh;
    static bool Check(Report report, bool passed, string error)
    {
        report.checks++;
        if (!passed) report.errors.Add(error);
        return passed;
    }
}
#endif
