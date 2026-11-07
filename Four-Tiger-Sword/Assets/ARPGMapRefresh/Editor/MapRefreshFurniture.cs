#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StudioBaekho;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Places explicit per-map V3 equipment bays on the retained gameplay shell.</summary>
public static class MapRefreshFurniture
{
    public const string LayoutPath = "Assets/ARPGMapRefresh/SourceData/FurnitureLayouts.json";
    [Serializable] public sealed class LayoutSet { public int version; public Layout[] layouts; }
    [Serializable] public sealed class Layout
    {
        public string sourcePath, role, purpose;
        public int minimumParts = 6;
        public Bay[] bays;
    }
    [Serializable] public sealed class Bay
    {
        public string id, kind;
        public Vector3 position;
        public float yaw;
        public float scale = 1;
    }
    [Serializable] public sealed class PartResult
    {
        public string id, assembly, asset, reason;
        public bool accepted;
        public Vector3 requestedPosition, position, boundsCenter, boundsSize;
    }
    [Serializable] public sealed class Report
    {
        public string sourcePath, role, purpose;
        public int requestedParts, placedParts, rejectedParts, placedAssemblies;
        public float occupiedFloorArea, serviceDeckArea;
        public List<PartResult> parts = new List<PartResult>();
    }
    private sealed class Item
    {
        public string asset;
        public Vector3 offset, fit;
        public float yaw;
        public Item(string asset, Vector3 offset, Vector3 fit, float yaw = 0) { this.asset = asset; this.offset = offset; this.fit = fit; this.yaw = yaw; }
    }
    private sealed class TransitionClearance
    {
        public Matrix4x4 roomToTransition;
        public Bounds run, lowerLanding, upperLanding;
    }
    private sealed class Context
    {
        public GameObject room;
        public Transform furniture;
        public BaekhoMapModule module;
        public Collider[] ground, shell;
        public HashSet<Collider> groundSet;
        public List<Bounds> occupied = new List<Bounds>();
        public List<KeyValuePair<Vector3, float>> protectedPoints = new List<KeyValuePair<Vector3, float>>();
        public List<TransitionClearance> transitions = new List<TransitionClearance>();
        public Report report;
    }
    private static readonly Vector3[] Adjustments =
    {
        Vector3.zero, new Vector3(.5f,0,0), new Vector3(-.5f,0,0), new Vector3(0,0,.5f), new Vector3(0,0,-.5f),
        new Vector3(1,0,0), new Vector3(-1,0,0), new Vector3(0,0,1), new Vector3(0,0,-1),
        new Vector3(.75f,0,.75f), new Vector3(-.75f,0,.75f), new Vector3(.75f,0,-.75f), new Vector3(-.75f,0,-.75f)
    };

    public static Report Apply(GameObject room, string sourcePath, string role, Transform appearanceRoot)
    {
        if (!room || !appearanceRoot || !appearanceRoot.IsChildOf(room.transform)) throw new ArgumentException("Furniture requires a cloned room and its appearance root.");
        if (Application.isPlaying) throw new InvalidOperationException("Author refreshed furniture in Edit Mode.");
        var report = new Report { sourcePath = Normalize(sourcePath), role = role };
        if (role == "corner") { report.purpose = "Unobstructed turning passage"; return report; }
        var set = JsonUtility.FromJson<LayoutSet>(File.ReadAllText(LayoutPath));
        var layout = set?.layouts?.SingleOrDefault(item => string.Equals(Normalize(item.sourcePath), report.sourcePath, StringComparison.OrdinalIgnoreCase));
        if (layout == null) throw new InvalidDataException("No authored furniture layout for " + sourcePath);
        if (layout.role != role) throw new InvalidDataException("Furniture layout role mismatch for " + sourcePath);
        report.purpose = layout.purpose;
        var previous = appearanceRoot.Find("FurnitureV3");
        if (previous) Object.DestroyImmediate(previous.gameObject);
        var furniture = new GameObject("FurnitureV3").transform;
        furniture.SetParent(appearanceRoot, false);
        var module = room.GetComponent<BaekhoMapModule>();
        if (!module) throw new InvalidDataException("The retained room has no dungeon metadata: " + sourcePath);
        var ground = (module.groundObjects ?? Array.Empty<GameObject>()).Where(item => item)
            .SelectMany(item => item.GetComponentsInChildren<Collider>(true)).Where(ActiveSolid).Distinct().ToArray();
        if (ground.Length == 0) throw new InvalidDataException("The retained room has no physical ground: " + sourcePath);
        var context = new Context
        {
            room = room, furniture = furniture, module = module, ground = ground, groundSet = new HashSet<Collider>(ground),
            shell = room.GetComponentsInChildren<Collider>(true).Where(ActiveSolid).ToArray(), report = report
        };
        CollectReservations(context);
        Physics.SyncTransforms();
        foreach (var bay in layout.bays ?? Array.Empty<Bay>()) BuildBay(context, bay);
        if (report.placedParts < layout.minimumParts)
            throw new InvalidDataException($"Furniture layout {sourcePath}: placed {report.placedParts}/{report.requestedParts}; authored minimum is {layout.minimumParts}. Rejected: "
                + string.Join("; ", report.parts.Where(item => !item.accepted).Select(item => item.id + " " + item.reason)));
        return report;
    }

    private static void BuildBay(Context c, Bay bay)
    {
        var group = new GameObject(bay.id).transform;
        group.SetParent(c.furniture, false);
        var root = c.room.transform;
        group.SetPositionAndRotation(root.TransformPoint(bay.position), root.rotation * Quaternion.Euler(0, bay.yaw, 0));
        var items = Items(bay.kind);
        bool any = false;
        Bounds combined = default;
        for (int index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var result = new PartResult { id = bay.id + "_" + (index + 1).ToString("D2"), assembly = bay.id, asset = item.asset };
            c.report.parts.Add(result);
            c.report.requestedParts++;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(item.asset);
            if (!source) throw new FileNotFoundException("V3 furniture source is missing.", item.asset);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source, c.room.scene);
            if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = result.id + "_" + source.name;
            go.transform.SetParent(group, false);
            go.transform.localScale = Vector3.one;
            go.transform.localRotation = Quaternion.Euler(0, item.yaw, 0);
            go.SetActive(true);
            foreach (var component in go.GetComponentsInChildren<Component>(true))
                if (component && !(component is Transform) && !(component is MeshFilter) && !(component is MeshRenderer) && !(component is Collider)) Object.DestroyImmediate(component);
            RemapMaterials(go);
            var native = BaekhoDungeonSetup.MeasureGeometryBounds(go, true);
            float scale = Mathf.Min(item.fit.x / Mathf.Max(native.size.x, .01f), item.fit.y / Mathf.Max(native.size.y, .01f), item.fit.z / Mathf.Max(native.size.z, .01f));
            go.transform.localScale = Vector3.one * scale * Mathf.Max(.1f, bay.scale);
            RoomCollisionV3.EnsurePartCollision(go);
            int groundLayer = LayerMask.NameToLayer("Ground");
            foreach (var child in go.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.isStatic = true;
                if (groundLayer >= 0) child.gameObject.layer = groundLayer;
            }
            Vector3 authored = item.offset * Mathf.Max(.1f, bay.scale);
            result.requestedPosition = bay.position + Quaternion.Euler(0, bay.yaw, 0) * authored;
            string reason = "No bounded authored position accepted";
            foreach (var adjustment in Adjustments)
            {
                go.transform.localPosition = authored + adjustment;
                var bounds = BoundsIn(go, root);
                float delta = bay.position.y - bounds.min.y;
                go.transform.position += root.up * delta;
                bounds = BoundsIn(go, root);
                if (!Check(c, bounds, bay.position.y, out float floor, out reason)) continue;
                go.transform.position += root.up * (floor - bay.position.y + .012f);
                bounds = BoundsIn(go, root);
                result.accepted = true;
                result.position = root.InverseTransformPoint(go.transform.position);
                result.boundsCenter = bounds.center;
                result.boundsSize = bounds.size;
                result.reason = adjustment.sqrMagnitude < .001f ? "Authored bay position" : "Bounded bay clearance adjustment";
                c.occupied.Add(bounds);
                c.report.placedParts++;
                c.report.occupiedFloorArea += bounds.size.x * bounds.size.z;
                if (!any) { combined = bounds; any = true; } else combined.Encapsulate(bounds);
                break;
            }
            if (!result.accepted)
            {
                result.reason = reason;
                c.report.rejectedParts++;
                Object.DestroyImmediate(go);
            }
        }
        if (!any) { Object.DestroyImmediate(group.gameObject); return; }
        c.report.placedAssemblies++;
        AddServiceDeck(c, group, combined, bay.position.y);
    }

    private static bool Check(Context c, Bounds bounds, float expectedFloor, out float floor, out string reason)
    {
        floor = expectedFloor;
        reason = null;
        float total = 0;
        for (int x = 0; x < 3; x++) for (int z = 0; z < 3; z++)
        {
            var point = new Vector3(Mathf.Lerp(bounds.min.x + .06f, bounds.max.x - .06f, x * .5f), expectedFloor,
                Mathf.Lerp(bounds.min.z + .06f, bounds.max.z - .06f, z * .5f));
            if (!TryFloor(c, point, out float height)) { reason = "Unsupported footprint or floor-height transition"; return false; }
            total += height;
        }
        floor = total / 9f;
        foreach (var point in c.protectedPoints)
        {
            if (Mathf.Abs(point.Key.y - floor) > 3f) continue;
            var expanded = bounds;
            expanded.Expand(new Vector3(point.Value * 2, 0, point.Value * 2));
            if (point.Key.x >= expanded.min.x && point.Key.x <= expanded.max.x && point.Key.z >= expanded.min.z && point.Key.z <= expanded.max.z)
            { reason = "Gameplay anchor or enemy/reward clearance"; return false; }
        }
        foreach (var socket in c.module.connections ?? Array.Empty<BaekhoMapConnection>())
        {
            if (!socket) continue;
            Bounds local = TransformBounds(bounds, socket.transform.worldToLocalMatrix * c.room.transform.localToWorldMatrix);
            if (local.max.y < -.1f || local.min.y > socket.height + .5f) continue;
            if (local.min.x < socket.width * .5f + .85f && local.max.x > -socket.width * .5f - .85f && local.min.z < 1.5f && local.max.z > -4f)
            { reason = "Door approach reserved"; return false; }
        }
        foreach (var transition in c.transitions)
        {
            var local = TransformBounds(bounds, transition.roomToTransition);
            if (VolumeOverlap(local, transition.run, .02f)
                || VolumeOverlap(local, transition.lowerLanding, .02f)
                || VolumeOverlap(local, transition.upperLanding, .02f))
            {
                reason = "Stair or slope approach reserved";
                return false;
            }
        }
        foreach (var occupied in c.occupied)
        {
            var expanded = occupied;
            expanded.Expand(new Vector3(.15f, 0, .15f));
            if (VolumeOverlap(bounds, expanded, .02f)) { reason = "Another authored furniture part"; return false; }
        }
        foreach (var collider in c.shell)
        {
            if (!ActiveSolid(collider)) continue;
            var other = ColliderBounds(collider, c.room.transform);
            if (c.groundSet.Contains(collider) && other.max.y <= floor + .2f) continue;
            if (VolumeOverlap(bounds, other, .035f)) { reason = "Retained shell or interactive object collision"; return false; }
        }
        return true;
    }

    private static bool TryFloor(Context c, Vector3 point, out float height)
    {
        height = point.y;
        bool found = false;
        float closest = float.PositiveInfinity;
        var ray = new Ray(c.room.transform.TransformPoint(point + Vector3.up * .65f), -c.room.transform.up);
        foreach (var collider in c.ground)
        {
            if (!ActiveSolid(collider) || !collider.Raycast(ray, out var hit, 1.3f) || hit.distance >= closest) continue;
            float y = c.room.transform.InverseTransformPoint(hit.point).y;
            if (Vector3.Dot(hit.normal, c.room.transform.up) < .9f || Mathf.Abs(y - point.y) > .22f) continue;
            found = true; closest = hit.distance; height = y;
        }
        return found;
    }

    private static void CollectReservations(Context c)
    {
        foreach (var marker in c.room.GetComponentsInChildren<BaekhoGameplayMarker>(true))
            c.protectedPoints.Add(new KeyValuePair<Vector3, float>(c.room.transform.InverseTransformPoint(marker.transform.position), marker.kind == "enemy" ? 1.25f : 1.5f));
        var anchors = c.room.GetComponent<StageMapAnchors>();
        if (anchors)
        {
            var points = new[] { anchors.playerSpawn, anchors.exitPoint, anchors.bossSpawn, anchors.shopPoint, anchors.teleportPoint };
            for (int i = 0; i < points.Length; i++) if (points[i])
                c.protectedPoints.Add(new KeyValuePair<Vector3, float>(c.room.transform.InverseTransformPoint(points[i].position), i == 2 ? 4f : i == 3 ? 2.6f : 1.8f));
        }
        foreach (var child in c.room.GetComponentsInChildren<Transform>(true))
        {
            if (!child.gameObject.activeInHierarchy) continue;
            if (child.name.StartsWith("stair_", StringComparison.OrdinalIgnoreCase)
                || child.name.StartsWith("slope_", StringComparison.OrdinalIgnoreCase))
                c.transitions.Add(ReserveTransition(c.room.transform, child));
        }
    }

    private static TransitionClearance ReserveTransition(Transform room, Transform transition)
    {
        const float landingDepth = 1.5f;
        const float landingSideClearance = .6f;
        const float belowFloor = .08f;
        const float headroom = 2.5f;
        var restored = transition.Find("V3RestoredStructure");
        var frame = restored ? restored : transition;
        // Strip scale from the frame so approach distances stay in metres on scaled stair modules.
        var worldToTransition = Matrix4x4.TRS(frame.position, frame.rotation, Vector3.one).inverse;
        var geometryToTransition = worldToTransition * frame.localToWorldMatrix;
        Bounds run;
        if (frame.GetComponentsInChildren<Renderer>(true).Length > 0 || frame.GetComponentsInChildren<Collider>(true).Length > 0)
            run = TransformBounds(BaekhoDungeonSetup.MeasureGeometryBounds(frame.gameObject, true), geometryToTransition);
        else
            run = TransformBounds(new Bounds(Vector3.zero, Vector3.one), geometryToTransition);

        float lowerFloor = run.min.y;
        float upperFloor = run.max.y;
        if (restored)
        {
            // The restored module's walking surface spans local y=0 to y=1. Handrails can extend higher.
            lowerFloor = geometryToTransition.MultiplyPoint3x4(Vector3.zero).y;
            upperFloor = geometryToTransition.MultiplyPoint3x4(Vector3.up).y;
        }
        run.Expand(new Vector3(landingSideClearance * 2f, .1f, 0f));
        var landingSize = new Vector3(run.size.x, headroom + belowFloor, landingDepth);
        float landingCenterOffset = (headroom - belowFloor) * .5f;
        return new TransitionClearance
        {
            roomToTransition = worldToTransition * room.localToWorldMatrix,
            run = run,
            lowerLanding = new Bounds(new Vector3(run.center.x, lowerFloor + landingCenterOffset, run.min.z - landingDepth * .5f), landingSize),
            upperLanding = new Bounds(new Vector3(run.center.x, upperFloor + landingCenterOffset, run.max.z + landingDepth * .5f), landingSize)
        };
    }

    private static void AddServiceDeck(Context c, Transform parent, Bounds bounds, float y)
    {
        var size = new Vector3(Mathf.Min(6f, bounds.size.x + .45f), .012f, Mathf.Min(5f, bounds.size.z + .45f));
        var center = new Vector3(bounds.center.x, y + .006f, bounds.center.z);
        foreach (float x in new[] { -.5f, .5f }) foreach (float z in new[] { -.5f, .5f })
            if (!TryFloor(c, center + new Vector3(x * size.x, 0, z * size.z), out _)) return;
        var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
        deck.name = "ServiceDeck";
        Object.DestroyImmediate(deck.GetComponent<Collider>());
        deck.transform.SetParent(parent, false);
        deck.transform.SetPositionAndRotation(c.room.transform.TransformPoint(center), c.room.transform.rotation);
        deck.transform.localScale = size;
        deck.GetComponent<MeshRenderer>().sharedMaterial = Material("Deck_Charcoal");
        deck.isStatic = true;
        c.report.serviceDeckArea += size.x * size.z;
        var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stripe.name = "SafetyStripe";
        Object.DestroyImmediate(stripe.GetComponent<Collider>());
        stripe.transform.SetParent(parent, false);
        stripe.transform.SetPositionAndRotation(c.room.transform.TransformPoint(center + new Vector3(0, .009f, -size.z * .5f + .09f)), c.room.transform.rotation);
        stripe.transform.localScale = new Vector3(Mathf.Min(1.8f, size.x * .6f), .008f, .065f);
        stripe.GetComponent<MeshRenderer>().sharedMaterial = Material("Safety_Amber");
        stripe.isStatic = true;
    }

    private static Item[] Items(string kind)
    {
        const string models = "Assets/ARPGRoomSetV3/Models/Assemblies/";
        const string equipment = "Assets/ARPGRoomSet/Models/NewEquipment/";
        const string props = "Assets/LabProps29/Prefabs/";
        string main;
        Vector3 fit;
        switch (kind)
        {
            case "command": main = "Assets/ARPGEnvironmentArt/Models/LAB_CommandDesk.fbx"; fit = new Vector3(3.3f, 2.2f, 1.5f); break;
            case "analysis": main = models + "LAB_AnalysisBedGantry.fbx"; fit = new Vector3(3.6f, 3.5f, 2.6f); break;
            case "cryo": main = equipment + "LAB_HorizontalStasisPod.fbx"; fit = new Vector3(3.4f, 2.4f, 1.7f); break;
            case "cooling": main = models + "LAB_WallCoolingManifold.fbx"; fit = new Vector3(3.5f, 3.4f, 1.6f); break;
            case "turbine": main = models + "LAB_TurbineServiceUnit.fbx"; fit = new Vector3(3.8f, 3.4f, 2.6f); break;
            case "cargo": main = models + "LAB_CargoLoadingStation.fbx"; fit = new Vector3(3.6f, 3.2f, 2.4f); break;
            case "archive": main = models + "LAB_LogisticsShelving.fbx"; fit = new Vector3(3.4f, 3.1f, 1.5f); break;
            case "power": main = equipment + "LAB_StackedBatteryBank.fbx"; fit = new Vector3(2.9f, 2.7f, 1.5f); break;
            case "specimen": main = equipment + "LAB_SpecimenCarousel.fbx"; fit = new Vector3(2.5f, 2.8f, 2.2f); break;
            case "compact": return new[]
                {
                    new Item(props + "06_standing_terminal.prefab", Vector3.zero, new Vector3(.9f, 1.7f, .85f)),
                    new Item(props + "10_blue_storage_case.prefab", new Vector3(1.2f,0,.1f), new Vector3(.8f,.8f,.8f)),
                    new Item(props + "13_brown_gas_cylinder.prefab", new Vector3(-1,0,.1f), new Vector3(.55f,1.5f,.55f))
                };
            default: throw new InvalidDataException("Unknown authored equipment bay: " + kind);
        }
        string companion = kind == "cargo" || kind == "archive" ? "10_blue_storage_case.prefab" : kind == "cryo" || kind == "specimen" ? "12_green_gas_cylinder.prefab" : "06_standing_terminal.prefab";
        return new[]
        {
            new Item(main, Vector3.zero, fit),
            new Item(props + companion, new Vector3(2.25f,0,.25f), new Vector3(.85f,1.8f,.85f), -15),
            new Item(props + "11_toolbox.prefab", new Vector3(-2.1f,0,.4f), new Vector3(.8f,.55f,.7f), 15)
        };
    }

    private static void RemapMaterials(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
            {
                if (source && AssetDatabase.GetAssetPath(source).StartsWith("Assets/ARPGEnvironmentArt/Materials/", StringComparison.Ordinal)) return source;
                string name = source ? source.name.ToLowerInvariant() : "";
                return Material(name.Contains("screen") || name.Contains("display") || name.Contains("glass") ? "Display_Blue"
                    : name.Contains("amber") || name.Contains("warning") || name.Contains("yellow") ? "Safety_Amber"
                    : name.Contains("dark") || name.Contains("black") ? "Graphite_Recess"
                    : name.Contains("white") || name.Contains("ceramic") ? "Armor_WarmCeramic"
                    : name.Contains("blue") || name.Contains("navy") || name.Contains("paint") ? "Equipment_Navy" : "Brushed_Metal");
            }).ToArray();
    }
    private static Material Material(string name)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/ARPGEnvironmentArt/Materials/" + name + ".mat");
        if (!material) throw new FileNotFoundException("V3 shared material is missing: " + name);
        return material;
    }
    private static Bounds BoundsIn(GameObject root, Transform relative) => TransformBounds(BaekhoDungeonSetup.MeasureGeometryBounds(root, true), relative.worldToLocalMatrix * root.transform.localToWorldMatrix);
    private static Bounds ColliderBounds(Collider collider, Transform relative)
    {
        Bounds local;
        if (collider is BoxCollider box) local = new Bounds(box.center, box.size);
        else if (collider is MeshCollider mesh && mesh.sharedMesh) local = mesh.sharedMesh.bounds;
        else return TransformBounds(collider.bounds, relative.worldToLocalMatrix);
        return TransformBounds(local, relative.worldToLocalMatrix * collider.transform.localToWorldMatrix);
    }
    private static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
    {
        var result = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++) result.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return result;
    }
    private static bool ActiveSolid(Collider collider) => collider && collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy;
    private static bool VolumeOverlap(Bounds a, Bounds b, float tolerance) => Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x) > tolerance
        && Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y) > tolerance && Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z) > tolerance;
    private static string Normalize(string path) => (path ?? "").Replace('\\', '/');
}
#endif
