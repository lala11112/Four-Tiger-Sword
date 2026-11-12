#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StudioBaekho;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Rebuilds owned V3 appearances in separate legacy-room derivatives.</summary>
public static class MapRefreshBuilder
{
    public const string Root = "Assets/ARPGMapRefresh";
    public const string ManifestPath = Root + "/Manifests/MapRefresh.json";
    public const string ProfilePath = Root + "/Profiles/V3Architecture.asset";
    public const string StylePath = Root + "/Profiles/V3CorridorStyle.asset";
    public const string SourceStylePath = "Assets/_Game/ProceduralDungeon/GreyboxCorridorStyle.asset";
    public const string AppearanceName = "V3RefreshAppearance";
    public static readonly string[] SettingsPaths = {
        "Assets/_Game/RunProgression/Settings/NormalStage.asset",
        "Assets/_Game/RunProgression/Settings/BossStage.asset",
        "Assets/_Game/ProceduralDungeon/DungeonSettings.asset",
        "Assets/_Project/Rendering/LabStyle/LabDungeonSettings.asset"
    };
    [Serializable] public sealed class Entry { public string sourcePath, prefabPath, role, guid; }
    [Serializable] public sealed class Manifest
    {
        public int version = 1;
        public Entry[] entries;
        public string[] settingsPaths = SettingsPaths;
        public string stylePath = StylePath, sourceStylePath = SourceStylePath;
    }
    [Serializable] public sealed class BuildReport
    {
        public string sourcePath, prefabPath;
        public int disabledProps, wallSkins, restoredStairs, colliderCount;
        public MapRefreshFurniture.Report furniture;
    }
    private static Queue<Entry> pending;
    public static string Status { get; private set; } = "Idle";

    public static Manifest ReadManifest() => JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));

    [MenuItem("Tools/Studio Baekho/Map Refresh/1 Prepare Profile and Manifest")]
    public static void Prepare()
    {
        RequireEditMode();
        foreach (var path in new[] { "Profiles", "Manifests", "Prefabs/Rooms", "Prefabs/WallKit", "Meshes", "Navigation", "Reports" }) EnsureFolder(Root + "/" + path);
        var profile = AssetDatabase.LoadAssetAtPath<BaekhoArchitectureProfile>(ProfilePath);
        if (!profile) { profile = ScriptableObject.CreateInstance<BaekhoArchitectureProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
        profile.wallMaterial = Material("Armor_WarmCeramic"); profile.floorMaterial = Material("Deck_Charcoal");
        profile.jointMaterial = Material("Graphite_Recess"); profile.trimMaterial = Material("Brushed_Metal"); profile.accentMaterial = Material("Display_Blue");
        EditorUtility.SetDirty(profile);
        var style = AssetDatabase.LoadAssetAtPath<BaekhoCorridorStyle>(StylePath);
        if (!style) { style = Object.Instantiate(AssetDatabase.LoadAssetAtPath<BaekhoCorridorStyle>(SourceStylePath)); AssetDatabase.CreateAsset(style, StylePath); }
        style.appearance = profile; EditorUtility.SetDirty(style);
        var source = JArray.Parse(File.ReadAllText(Root + "/SourceData/SourceRooms.json"));
        var entries = source.Select(row => new Entry { sourcePath = (string)row["path"], role = (string)row["role"], guid = (string)row["guid"],
            prefabPath = Root + "/Prefabs/Rooms/" + (string)row["role"] + "_" + (string)row["guid"] + ".prefab" }).ToArray();
        if (entries.Count(e => e.role == "normal") != 17 || entries.Length != 23) throw new InvalidDataException("Expected seventeen ordinary and six supporting source rooms.");
        File.WriteAllText(ManifestPath, JsonUtility.ToJson(new Manifest { entries = entries }, true));
        AssetDatabase.ImportAsset(ManifestPath);
        BuildKit(profile);
        AssetDatabase.SaveAssets();
        Status = "Prepared 23 source records and V3 architecture kit";
    }

    [MenuItem("Tools/Studio Baekho/Map Refresh/2 Rebuild All Derivatives")]
    public static void BeginBuild()
    {
        RequireEditMode();
        if (pending != null) throw new InvalidOperationException("A refresh is already running.");
        Prepare(); pending = new Queue<Entry>(ReadManifest().entries);
        Status = "Queued 23 rooms"; EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            var entry = pending.Dequeue();
            var report = Build(entry.sourcePath);
            Status = $"Built {23 - pending.Count}/23: {entry.role} {Path.GetFileName(entry.sourcePath)}";
            Debug.Log(Status);
            if (pending.Count == 0) { EditorApplication.update -= Tick; pending = null; Status = "All 23 refreshed rooms built; register after validation."; }
        }
        catch (Exception e) { EditorApplication.update -= Tick; pending = null; Status = "Failed: " + e; Debug.LogError(Status); }
    }

    public static BuildReport Build(string sourcePath)
    {
        RequireEditMode();
        var entry = ReadManifest().entries.Single(e => e.sourcePath == sourcePath);
        var profile = AssetDatabase.LoadAssetAtPath<BaekhoArchitectureProfile>(ProfilePath);
        if (!profile) throw new InvalidDataException("Prepare the appearance profile first.");
        // Subsequent builds retain every authored gameplay change in the derivative.
        var input = AssetDatabase.LoadAssetAtPath<GameObject>(entry.prefabPath) ? entry.prefabPath : entry.sourcePath;
        var room = PrefabUtility.LoadPrefabContents(input);
        var report = new BuildReport { sourcePath = sourcePath, prefabPath = entry.prefabPath };
        try
        {
            BaekhoCoplanarVisualRepair.RestoreSources(room, entry.prefabPath);
            room.name = AssetDatabase.LoadAssetAtPath<GameObject>(entry.sourcePath).name + "_V3Refresh";
            // Deleted legacy dependencies leave missing nested-prefab records even after geometry is repaired.
            // Unpack those records on the derivative only, retaining their objects and every local reference.
            foreach (var missing in room.GetComponentsInChildren<Transform>(true).Where(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)
                && PrefabUtility.GetPrefabInstanceStatus(t.gameObject) == PrefabInstanceStatus.MissingAsset).ToArray())
                PrefabUtility.UnpackPrefabInstance(missing.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var module = room.GetComponent<BaekhoMapModule>();
            if (!module) throw new InvalidDataException("Source is not a prepared room: " + sourcePath);
            var old = room.transform.Find(AppearanceName); if (old) Object.DestroyImmediate(old.gameObject);
            foreach (var skin in room.GetComponentsInChildren<BaekhoWallAppearanceInstance>(true).ToArray()) BaekhoWallAppearance.Remove(skin.transform.parent, skin.name);
            var appearance = new GameObject(AppearanceName).transform; appearance.SetParent(room.transform, false);
            foreach (Transform child in room.transform)
            {
                if (child.name.StartsWith("prop_", StringComparison.OrdinalIgnoreCase) || child.name.StartsWith("decoration_", StringComparison.OrdinalIgnoreCase))
                {
                    // Keep components and references; only inert, decorative content may be retired.
                    bool functional = child.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c && !(c is BaekhoMapAsset) && !(c is NavMeshModifier))
                        || child.GetComponentsInChildren<Collider>(true).Any(c => c.isTrigger) || child.GetComponentsInChildren<Rigidbody>(true).Length > 0;
                    if (!functional) { child.gameObject.SetActive(false); report.disabledProps++; }
                }
            }
            RepairStructure(room, sourcePath, report);
            // Import the many small persistent wall meshes as one batch.
            AssetDatabase.StartAssetEditing();
            try
            {
            module.groundObjects = module.groundObjects.Concat(room.GetComponentsInChildren<Transform>(true)
                .Where(t => (t.name.StartsWith("stair_") || t.name.StartsWith("slope_")) && t.GetComponentsInChildren<Collider>(true).Length > 0).Select(t => t.gameObject)).Where(o => o).Distinct().ToArray();
            foreach (Transform child in room.transform)
            {
                if (!child.gameObject.activeSelf || child == appearance) continue;
                string name = child.name.ToLowerInvariant();
                Material material = name.StartsWith("floor") || name.StartsWith("bridge") || name.StartsWith("stair") || name.StartsWith("slope") ? profile.floorMaterial
                    : name.StartsWith("wall") || name.StartsWith("cliff") ? profile.wallMaterial : name.StartsWith("ceiling") ? profile.jointMaterial
                    : name.StartsWith("railing") || name.StartsWith("column") ? profile.trimMaterial : null;
                if (material) Recolor(child.gameObject, material);
                if (name.StartsWith("wall") || name.StartsWith("cliff") || name.StartsWith("ceiling")) Modifier(child.gameObject, false, true);
                if (!name.StartsWith("wall") && !name.StartsWith("cliff")) continue;
                var boxes = child.GetComponentsInChildren<BoxCollider>(true).Where(c => c.enabled && !c.isTrigger).ToArray();
                foreach (var box in boxes)
                {
                    if (!box.gameObject.activeInHierarchy) continue;
                    var skin = BaekhoWallAppearance.Apply(box.transform, new Bounds(box.center, box.size), profile);
                    PersistMesh(skin, entry.guid + "_Wall_" + report.wallSkins.ToString("D4")); report.wallSkins++;
                }
            }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            foreach (var door in room.GetComponentsInChildren<LabDoor.SlidingDoor>(true)) Modifier(door.gameObject, true, false);
            report.furniture = MapRefreshFurniture.Apply(room, sourcePath, entry.role, appearance);
            var furniture = appearance.Find("FurnitureV3"); if (furniture) Modifier(furniture.gameObject, false, true);
            // Cosmetic architecture never participates in the navigation bake.
            foreach (var skin in room.GetComponentsInChildren<BaekhoWallAppearanceInstance>(true)) Modifier(skin.gameObject, true, false);
            Bake(room, entry.guid);
            report.colliderCount = room.GetComponentsInChildren<Collider>(true).Count(c => c.enabled && c.gameObject.activeInHierarchy);
            BaekhoCoplanarVisualRepair.Repair(room, entry.prefabPath);
            PrefabUtility.SaveAsPrefabAsset(room, entry.prefabPath);
            File.WriteAllText(Root + "/Reports/" + entry.guid + ".json", JsonUtility.ToJson(report, true));
            AssetDatabase.SaveAssets();
            return report;
        }
        finally { PrefabUtility.UnloadPrefabContents(room); }
    }

    private static void RepairStructure(GameObject room, string path, BuildReport report)
    {
        if (!room.GetComponentsInChildren<Transform>(true).Any(t => (t.name.StartsWith("stair_") || t.name.StartsWith("slope_")) && t.name.Contains("Missing Prefab"))) return;
        MapRefreshStructureRepair.Repair(room, path);
        report.restoredStairs = room.GetComponentsInChildren<Transform>(true).Count(t => t.name == "V3RestoredStructure");
    }

    private static void Bake(GameObject room, string guid)
    {
        var surface = room.GetComponent<NavMeshSurface>() ?? room.AddComponent<NavMeshSurface>();
        foreach (var other in room.GetComponentsInChildren<NavMeshSurface>(true)) if (other != surface) other.enabled = false;
        surface.RemoveData(); surface.navMeshData = null; surface.enabled = true; surface.agentTypeID = 0;
        surface.collectObjects = CollectObjects.Children; surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        // Preserve the physical landing height instead of interpolating the adjacent stair slope.
        surface.buildHeightMesh = true;
        surface.layerMask = ~0; surface.ignoreNavMeshAgent = true; surface.ignoreNavMeshObstacle = true;
        surface.BuildNavMesh(); var built = surface.navMeshData;
        if (!built) throw new InvalidDataException("Navigation bake failed: " + room.name);
        surface.RemoveData();
        string path = Root + "/Navigation/" + guid + ".asset";
        var asset = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
        if (asset) { EditorUtility.CopySerialized(built, asset); Object.DestroyImmediate(built); EditorUtility.SetDirty(asset); }
        else { AssetDatabase.CreateAsset(built, path); asset = built; }
        surface.navMeshData = asset; EditorUtility.SetDirty(surface);
    }

    [MenuItem("Tools/Studio Baekho/Map Refresh/3 Register Refreshed Rooms")]
    private static void RegisterMenu() => Debug.Log(Register());

    public static string Register()
    {
        RequireEditMode();
        var manifest = ReadManifest();
        var replacements = manifest.entries.ToDictionary(e => e.sourcePath, e => AssetDatabase.LoadAssetAtPath<GameObject>(e.prefabPath)?.GetComponent<BaekhoMapModule>());
        if (replacements.Any(pair => !pair.Value || !pair.Value.GetComponent<NavMeshSurface>()?.navMeshData)) throw new InvalidDataException("Build all rooms before registration.");
        var ordinary = manifest.entries.Where(e => e.role == "normal").Select(e => replacements[e.sourcePath]).ToArray();
        foreach (var path in SettingsPaths)
        {
            var settings = AssetDatabase.LoadAssetAtPath<BaekhoDungeonSettings>(path);
            var pool = settings.maps.Select(m => Replace(m, replacements)).ToList();
            foreach (var room in ordinary) if (!pool.Contains(room)) pool.Add(room);
            pool.RemoveAll(BaekhoDungeonSetup.HasTwoByThreeDoor);
            if (pool.Distinct().Count() != 32) throw new InvalidDataException("Expected 32 unique ordinary rooms after excluding 2 x 3 doorways: " + path);
            settings.maps = pool.ToArray();
            settings.startRoom = Replace(settings.startRoom, replacements); settings.endRoom = Replace(settings.endRoom, replacements);
            settings.shopRoom = Replace(settings.shopRoom, replacements); settings.bossRoom = Replace(settings.bossRoom, replacements);
            settings.cornerRoom = Replace(settings.cornerRoom, replacements); settings.style = AssetDatabase.LoadAssetAtPath<BaekhoCorridorStyle>(StylePath);
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings);
        }
        BuildShopCounter();
        return "Registered 32 eligible ordinary rooms and refreshed dedicated rooms in four settings assets.";
    }
    private static BaekhoMapModule Replace(BaekhoMapModule module, Dictionary<string, BaekhoMapModule> replacements)
        => module && replacements.TryGetValue(AssetDatabase.GetAssetPath(module), out var replacement) ? replacement : module;

    public static void BuildShopCounter()
    {
        const string sourcePath = "Assets/_Game/Shop/ShopCounter.prefab";
        string output = Root + "/Prefabs/ShopCounter_V3Refresh.prefab";
        var root = PrefabUtility.LoadPrefabContents(AssetDatabase.LoadAssetAtPath<GameObject>(output) ? output : sourcePath);
        try
        {
            var existing = root.transform.Find(AppearanceName); if (existing) Object.DestroyImmediate(existing.gameObject);
            // All original colliders, sign text, and interaction references remain on the clone.
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) if (r.GetComponent<MeshFilter>() && !r.GetComponent<TMPro.TMP_Text>() && !r.GetComponent<TextMesh>()) r.enabled = false;
            var visual = new GameObject(AppearanceName).transform; visual.SetParent(root.transform, false);
            var desk = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ARPGEnvironmentArt/Models/LAB_CommandDesk.fbx"), root.scene);
            PrefabUtility.UnpackPrefabInstance(desk, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            desk.transform.SetParent(visual, false);
            var b = BaekhoDungeonSetup.MeasureGeometryBounds(desk, true);
            desk.transform.localScale = new Vector3(2f / b.size.x, 1.08f / b.size.y, 1.05f / b.size.z);
            desk.transform.localPosition = -Vector3.Scale(new Vector3(b.center.x,b.min.y,b.center.z), desk.transform.localScale);
            foreach (var c in desk.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var renderer in desk.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterials = renderer.sharedMaterials.Select(m => Material(m.name.Replace(" (Instance)", "")) ?? m).ToArray();
            PrefabUtility.SaveAsPrefabAsset(root, output);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var active = SceneManager.GetActiveScene();
        const string scenePath = "Assets/_Project/Scenes/Game Scene.unity";
        var scene = SceneManager.GetSceneByPath(scenePath); bool loaded = scene.IsValid() && scene.isLoaded;
        if (!loaded) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            var services = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<StageShopService>(true)).ToArray();
            foreach (var service in services)
            {
                service.counterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(output);
                EditorUtility.SetDirty(service); if (PrefabUtility.IsPartOfPrefabInstance(service)) PrefabUtility.RecordPrefabInstancePropertyModifications(service);
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        finally { if (!loaded) EditorSceneManager.CloseScene(scene, true); if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active); }
    }

    private static void BuildKit(BaekhoArchitectureProfile profile)
    {
        foreach (string kind in new[] { "Straight", "Corner", "Doorway" })
        {
            var go = new GameObject("V3_Wall_" + kind);
            try
            {
                Action<string,Vector3,Vector3> panel = (name, center, size) => {
                    var owner = new GameObject(name); owner.transform.SetParent(go.transform, false);
                    var skin = BaekhoWallAppearance.Apply(owner.transform, new Bounds(center, size), profile);
                    PersistMesh(skin, "Kit_" + kind + "_" + name);
                };
                if (kind == "Straight") panel("Panel", new Vector3(0,1.5f,0), new Vector3(2,3,.25f));
                if (kind == "Corner") { panel("PanelX",new Vector3(0,1.5f,0),new Vector3(2,3,.25f)); panel("PanelZ",new Vector3(.875f,1.5f,1),new Vector3(.25f,3,2)); }
                if (kind == "Doorway") { panel("Left",new Vector3(-1.25f,1.5f,0),new Vector3(.5f,3,.25f)); panel("Right",new Vector3(1.25f,1.5f,0),new Vector3(.5f,3,.25f)); panel("Lintel",new Vector3(0,2.75f,0),new Vector3(2,.5f,.25f)); }
                var path = Root + "/Prefabs/WallKit/" + go.name + ".prefab";
                BaekhoCoplanarVisualRepair.Repair(go, path);
                var asset = PrefabUtility.SaveAsPrefabAsset(go, path);
                var field = typeof(BaekhoArchitectureProfile).GetField(char.ToLowerInvariant(kind[0]) + kind.Substring(1) + "Module");
                field?.SetValue(profile,asset);
            }
            finally { Object.DestroyImmediate(go); }
        }
        EditorUtility.SetDirty(profile);
    }
    private static void PersistMesh(GameObject skin, string key)
    {
        var filter = skin.GetComponent<MeshFilter>(); var copy = Object.Instantiate(filter.sharedMesh);
        copy.name = key;
        string path = Root + "/Meshes/" + key + ".asset";
        var persistent = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (persistent) { EditorUtility.CopySerialized(copy, persistent); Object.DestroyImmediate(copy); EditorUtility.SetDirty(persistent); }
        else { AssetDatabase.CreateAsset(copy,path); persistent = copy; }
        filter.sharedMesh = persistent;
        var owner = skin.GetComponent<BaekhoGeneratedMeshes>(); if (owner) Object.DestroyImmediate(owner);
    }
    private static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/ARPGEnvironmentArt/Materials/" + name + ".mat");
    private static void Recolor(GameObject root, Material mat)
    { foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = r.sharedMaterials.Select(_ => mat).ToArray(); }
    private static void Modifier(GameObject go, bool ignore, bool notWalkable)
    { var m = go.GetComponent<NavMeshModifier>() ?? go.AddComponent<NavMeshModifier>(); m.ignoreFromBuild = ignore; m.overrideArea = notWalkable; m.area = notWalkable ? 1 : 0; m.applyToChildren = true; }
    private static void EnsureFolder(string path)
    { if (AssetDatabase.IsValidFolder(path)) return; EnsureFolder(Path.GetDirectoryName(path).Replace('\\','/')); AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path)); }
    private static void RequireEditMode()
    { if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Refresh room assets in Edit Mode."); }
}
#endif
