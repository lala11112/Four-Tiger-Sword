#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StudioBaekho;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Repairs only missing legacy stair dependencies inside a caller's derived room.</summary>
public static class MapRefreshStructureRepair
{
    const string MissingGuid = "a0b56e7f0a62df34a888a8b7d8030442";
    const string NativeStair = "Assets/LabStaircase1m/Prefabs/32_industrial_staircase_1m.prefab";
    const string ChildName = "V3RestoredStructure";
    const string RampPath = "Assets/ARPGMapRefresh/Meshes/Structure/WalkableRamp.asset";
    [Serializable] sealed class Map { public int schemaVersion; public float cellSize; public Placement[] placements; }
    [Serializable] sealed class Placement
    {
        public string id, role, prefabGuid, surface;
        public Vector3 position, rotation, scale, fitSize;
    }
    [Serializable] public sealed class Report
    {
        public string sourcePath, manifestPath;
        public int restoredStairs, restoredSlopes, alreadyRestored;
        public List<string> details = new List<string>();
    }

    public static Report Repair(GameObject root, string sourcePath)
    {
        if (!root) throw new ArgumentNullException(nameof(root));
        if (EditorUtility.IsPersistent(root))
            throw new InvalidOperationException("Repair a loaded derived copy, never the original prefab asset.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Repair structure in Edit Mode.");
        var report = new Report { sourcePath = sourcePath };
        var missing = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains(MissingGuid)).ToArray();
        if (missing.Length == 0) return report;
        report.manifestPath = ManifestPath(sourcePath);
        var map = JsonUtility.FromJson<Map>(File.ReadAllText(report.manifestPath));
        if (map?.placements == null || map.cellSize <= 0) throw new InvalidDataException("Invalid recovered source map: " + report.manifestPath);
        var native = AssetDatabase.LoadAssetAtPath<GameObject>(NativeStair);
        if (!native) throw new FileNotFoundException("The native 1m stair module is required.", NativeStair);
        var used = new HashSet<Transform>();
        var restoredGround = new List<GameObject>();
        foreach (var item in map.placements.Where(p => (p.role == "stair" || p.role == "slope") && p.prefabGuid == MissingGuid))
        {
            if (!BaekhoDungeonGeometry.Positive(item.scale)) throw new InvalidDataException("Invalid authored stair dimensions: " + item.id);
            var rotation = Quaternion.Euler(item.rotation);
            // The deleted dependency was a centred unit cube. Keep its imported placeholder pose.
            var expected = item.position;
            if (item.surface == "bottom") expected.y += item.scale.y * .5f;
            else if (item.surface == "top") expected.y -= item.scale.y * .5f;
            var parent = missing.Where(t => !used.Contains(t))
                .OrderBy(t => (root.transform.InverseTransformPoint(t.position) - expected).sqrMagnitude).FirstOrDefault();
            if (!parent || Vector3.Distance(root.transform.InverseTransformPoint(parent.position), expected) > .03f
                || Quaternion.Angle(Quaternion.Inverse(root.transform.rotation) * parent.rotation, rotation) > .2f)
                throw new InvalidDataException("Cannot match missing stair placeholder to source placement " + item.id + " in " + sourcePath);
            used.Add(parent);
            restoredGround.Add(parent.gameObject);
            var existing = parent.Find(ChildName);
            if (existing)
            {
                if (!existing.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger))
                    throw new InvalidDataException("The restored stair has lost its collision: " + item.id);
                report.alreadyRestored++;
                continue;
            }
            if (parent.GetComponentsInChildren<Renderer>(true).Length != 0 || parent.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidDataException("Refusing to overwrite existing authored stair geometry: " + item.id);
            // Source neighbour floors resolve the old cube-pivot mismatch without changing any route.
            var forward = rotation * Vector3.forward * map.cellSize;
            var low = Landing(map, item.position - forward, item.position.y, item.id);
            var high = Landing(map, item.position + forward, low + item.scale.y, item.id);
            if (Mathf.Abs(high - low - item.scale.y) > .03f)
                throw new InvalidDataException("Stair rise or direction disagrees with adjacent authored landings: " + item.id);
            var bottom = new Vector3(item.position.x, low, item.position.z);
            var wrapper = new GameObject(ChildName);
            SceneManager.MoveGameObjectToScene(wrapper, root.scene);
            wrapper.transform.SetParent(parent, false);
            try
            {
                SetPose(wrapper.transform, root.transform, bottom, rotation, item.scale);
                int ground = LayerMask.NameToLayer("Ground");
                if (ground < 0) throw new InvalidDataException("The Ground layer is missing.");
                if (item.role == "stair")
                {
                    var art = (GameObject)PrefabUtility.InstantiatePrefab(native, root.scene);
                    art.transform.SetParent(wrapper.transform, false);
                    art.transform.localPosition = Vector3.zero;
                    art.transform.localRotation = Quaternion.identity;
                    art.transform.localScale = Vector3.one;
                    PrefabUtility.UnpackPrefabInstance(art, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    foreach (var metadata in art.GetComponentsInChildren<BaekhoMapAsset>(true)) Object.DestroyImmediate(metadata);
                    Recolor(art);
                    report.restoredStairs++;
                }
                else
                {
                    var mesh = Ramp();
                    wrapper.AddComponent<MeshFilter>().sharedMesh = mesh;
                    wrapper.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/ARPGEnvironmentArt/Materials/Deck_Charcoal.mat");
                    var collider = wrapper.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;
                    collider.convex = true;
                    report.restoredSlopes++;
                }
                foreach (var t in wrapper.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ground;
                report.details.Add(item.id + ": " + item.role + " " + item.scale + ", landings " + low + " -> " + high + ", yaw " + item.rotation.y);
            }
            catch { Object.DestroyImmediate(wrapper); throw; }
        }
        if (used.Count != missing.Length) throw new InvalidDataException("Some missing stair dependencies have no matching source placement: " + sourcePath);
        var module = root.GetComponent<BaekhoMapModule>();
        if (module)
            module.groundObjects = (module.groundObjects ?? Array.Empty<GameObject>()).Where(o => o).Concat(restoredGround).Distinct().ToArray();
        return report;
    }

    static string ManifestPath(string source)
    {
        var sibling = Path.Combine(Path.GetDirectoryName(source), "Map.baekhomap.json").Replace('\\', '/');
        if (File.Exists(sibling)) return sibling;
        foreach (int index in new[] { 5, 6 })
            if (Path.GetFileName(source).StartsWith("BaekhoMap" + index + "_", StringComparison.Ordinal))
                return "Assets/ARPGMapRefresh/SourceData/Structure/BaekhoMap" + index + ".baekhomap.json";
        throw new FileNotFoundException("Missing source map for faithful stair repair.", sibling);
    }

    static float Landing(Map map, Vector3 point, float preferred, string id)
    {
        var floors = map.placements.Where(p => (p.role == "floor" || p.role == "bridge")
            && Vector2.Distance(new Vector2(p.position.x, p.position.z), new Vector2(point.x, point.z)) < .03f)
            .OrderBy(p => Mathf.Abs(p.position.y - preferred)).ToArray();
        if (floors.Length == 0) throw new InvalidDataException("No exact authored floor landing for " + id + " at " + point);
        if (!string.IsNullOrEmpty(floors[0].surface) && floors[0].surface != "top")
            throw new InvalidDataException("Repair requires a source floor with explicit top-surface alignment: " + id);
        return floors[0].position.y;
    }

    static void SetPose(Transform target, Transform root, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var matrix = target.parent.worldToLocalMatrix * root.localToWorldMatrix * Matrix4x4.TRS(position, rotation, scale);
        target.localPosition = matrix.GetColumn(3);
        target.localRotation = matrix.rotation;
        target.localScale = matrix.lossyScale;
    }

    static void Recolor(GameObject art)
    {
        foreach (var renderer in art.GetComponentsInChildren<MeshRenderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                string name = materials[i] ? materials[i].name.ToLowerInvariant() : "";
                string palette = name.Contains("stripes") ? "Safety_Amber" : name.Contains("metal") || name.Contains("silver") || name.Contains("edge") ? "Brushed_Metal" : "Deck_Charcoal";
                materials[i] = AssetDatabase.LoadAssetAtPath<Material>("Assets/ARPGEnvironmentArt/Materials/" + palette + ".mat");
                if (!materials[i]) throw new InvalidDataException("Missing V3 stair material " + palette);
            }
            renderer.sharedMaterials = materials;
        }
    }

    static Mesh Ramp()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(RampPath);
        if (existing) return existing;
        Directory.CreateDirectory(Path.GetDirectoryName(RampPath));
        AssetDatabase.Refresh();
        var mesh = new Mesh { name = "Restored unit walkable ramp" };
        // Match the native stair's 5mm entry: the collision slope stays just below
        // the default 45-degree agent/controller limit, with the same full rise.
        mesh.vertices = new[] { new Vector3(-.5f,0,-.5f), new Vector3(.5f,0,-.5f), new Vector3(-.5f,.005f,-.5f), new Vector3(.5f,.005f,-.5f),
            new Vector3(-.5f,1,.5f), new Vector3(.5f,1,.5f), new Vector3(-.5f,0,.5f), new Vector3(.5f,0,.5f) };
        mesh.triangles = new[] { 2,4,5,2,5,3, 0,1,7,0,7,6, 4,6,7,4,7,5, 0,2,3,0,3,1, 0,6,4,0,4,2, 1,3,5,1,5,7 };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, RampPath);
        return mesh;
    }
}
#endif
