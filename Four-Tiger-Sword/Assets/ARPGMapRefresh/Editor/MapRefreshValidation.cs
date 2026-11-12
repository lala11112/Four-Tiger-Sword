#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StudioBaekho;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Validates derived room art while protecting the original gameplay geometry and assets.</summary>
public static class MapRefreshValidation
{
    public const string ManifestPath = "Assets/ARPGMapRefresh/Manifests/MapRefresh.json";
    public static string VerificationRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../MapRefreshVerification"));
    [Serializable] public sealed class Manifest
    {
        public Entry[] entries;
        public string[] settingsPaths;
        public string stylePath, sourceStylePath;
    }
    [Serializable] public sealed class Entry { public string sourcePath, prefabPath, role; }
    [Serializable] public sealed class Report
    {
        public bool passed;
        public int checks, entries, structuralColliders, markers, sockets, protectedAssets;
        public int mainRooms, ordinaryPool, generatedPlans, refreshedSelections, v3Selections;
        public int nativeRooms, enemyPlacements, teleports;
        public int serializationOnlyChanges;
        public string fingerprint;
        public List<string> errors = new List<string>();
        public List<string> details = new List<string>();
    }
    [Serializable] public sealed class AssetRecord
    {
        public string path, category, sha256, semanticSha256, startRoom, endRoom, bossRoom, shopRoom, cornerRoom, style;
        public int colliders, markers, sockets;
        public int objects, components, appearanceGroups, furnitureParts, wallSkins, navTriangles, navVertices;
    }
    [Serializable] public sealed class AssetSnapshot { public AssetRecord[] assets; }
    [Serializable] sealed class ProtectedRecord { public string path, hash; }
    [Serializable] sealed class ProtectedList { public ProtectedRecord[] entries; }
    static readonly string[] DefaultSettings =
    {
        "Assets/_Game/RunProgression/Settings/NormalStage.asset",
        "Assets/_Game/RunProgression/Settings/BossStage.asset",
        "Assets/_Game/ProceduralDungeon/DungeonSettings.asset",
        "Assets/_Project/Rendering/LabStyle/LabDungeonSettings.asset"
    };

    public static Manifest ReadManifest()
    {
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
        if (manifest == null || manifest.entries == null) throw new InvalidDataException("Map refresh manifest has no entries.");
        return manifest;
    }

    public static string Run(string beforeJson = null, bool native = true)
    {
        var report = new Report();
        if (!Check(report, !EditorApplication.isPlayingOrWillChangePlaymode, "Run map refresh validation in Edit Mode."))
            return JsonUtility.ToJson(report, true);
        var manifest = ReadManifest();
        Check(report, manifest.entries.Length == 23 && manifest.entries.Count(e => e.role == "normal") == 17,
            "Expected 17 ordinary replacements and six dedicated/demo/corner replacements.");
        foreach (var entry in manifest.entries)
        {
            try { CompareRoom(report, entry); }
            catch (Exception exception) { Check(report, false, entry.prefabPath + ": " + exception.Message); }
        }
        CheckProtectedAssets(report);
        CheckSettings(report, manifest, beforeJson);
        if (native)
        {
            try { CheckNativeGeometry(report, manifest); }
            catch (Exception exception) { Check(report, false, "Native geometry checks: " + exception.Message); }
        }
        var snapshot = Snapshot();
        var assetRows = JsonUtility.FromJson<AssetSnapshot>(snapshot).assets;
        report.fingerprint = Hash(Encoding.UTF8.GetBytes(string.Join("\n", assetRows.Select(row => row.path + "="
            + (string.IsNullOrEmpty(row.semanticSha256) ? row.sha256 : row.semanticSha256)))));
        if (!string.IsNullOrEmpty(beforeJson)) CompareProtectedSnapshots(report, beforeJson, snapshot);
        report.passed = report.errors.Count == 0;
        return JsonUtility.ToJson(report, true);
    }

    /// <summary>Content hashes for protected assets and stable geometry/reference signatures for generated prefabs and navigation.</summary>
    public static string Snapshot()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Capture map refresh snapshots in Edit Mode.");
        var manifest = ReadManifest();
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in manifest.entries)
        {
            paths[entry.sourcePath] = "source";
            paths[entry.prefabPath] = "derived";
        }
        foreach (var path in Directory.GetFiles("Assets/ARPGRoomSetV3", "*", SearchOption.AllDirectories))
            paths[path.Replace('\\', '/')] = "v3";
        foreach (var path in SettingsPaths(manifest)) paths[path] = "settings";
        if (!string.IsNullOrEmpty(manifest.stylePath)) paths[manifest.stylePath] = "style";
        if (!string.IsNullOrEmpty(manifest.sourceStylePath)) paths[manifest.sourceStylePath] = "source";
        // Include generated meshes/materials/style dependencies so a rerun cannot hide asset churn.
        if (Directory.Exists("Assets/ARPGMapRefresh"))
            foreach (var path in Directory.GetFiles("Assets/ARPGMapRefresh", "*", SearchOption.AllDirectories))
                if (!paths.ContainsKey(path.Replace('\\', '/'))) paths[path.Replace('\\', '/')] = "derived";
        var rows = new List<AssetRecord>();
        var activeNavigation = NavMesh.CalculateTriangulation().vertices ?? Array.Empty<Vector3>();
        float navIsolationFloor = Mathf.Ceil(activeNavigation.Length == 0 ? 0f : activeNavigation.Max(point => point.y)) + 32f;
        foreach (var pair in paths.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var row = new AssetRecord { path = pair.Key, category = pair.Value, sha256 = File.Exists(pair.Key) ? Hash(File.ReadAllBytes(pair.Key)) : "missing" };
            if (pair.Key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(pair.Key);
                if (root)
                {
                    row.colliders = root.GetComponentsInChildren<Collider>(true).Length;
                    row.markers = root.GetComponentsInChildren<BaekhoGameplayMarker>(true).Length;
                    row.sockets = root.GetComponentsInChildren<BaekhoMapConnection>(true).Length;
                    if (pair.Value == "derived" && pair.Key.StartsWith("Assets/ARPGMapRefresh/Prefabs/", StringComparison.Ordinal))
                        row.semanticSha256 = PrefabSignature(root, row);
                }
            }
            if (pair.Value == "derived" && pair.Key.StartsWith("Assets/ARPGMapRefresh/Navigation/", StringComparison.Ordinal)
                && pair.Key.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) && File.Exists(pair.Key))
            {
                var navigation = AssetDatabase.LoadAssetAtPath<NavMeshData>(pair.Key);
                if (!navigation) throw new InvalidDataException("Missing generated navigation data: " + pair.Key);
                row.semanticSha256 = NavigationSignature(navigation, navIsolationFloor, row);
            }
            var settings = pair.Value == "settings" ? AssetDatabase.LoadAssetAtPath<BaekhoDungeonSettings>(pair.Key) : null;
            if (settings)
            {
                row.startRoom = AssetPath(settings.startRoom); row.endRoom = AssetPath(settings.endRoom);
                row.bossRoom = AssetPath(settings.bossRoom); row.shopRoom = AssetPath(settings.shopRoom);
                row.cornerRoom = AssetPath(settings.cornerRoom); row.style = AssetPath(settings.style);
            }
            rows.Add(row);
        }
        return JsonUtility.ToJson(new AssetSnapshot { assets = rows.ToArray() }, true);
    }

    public static string CompareSnapshots(string beforeJson, string afterJson)
    {
        var report = new Report();
        var before = JsonUtility.FromJson<AssetSnapshot>(beforeJson);
        var after = JsonUtility.FromJson<AssetSnapshot>(afterJson);
        var current = after.assets.ToDictionary(r => r.path, StringComparer.Ordinal);
        Check(report, before.assets.Length == after.assets.Length, "A repeated build changed the asset inventory count.");
        foreach (var old in before.assets)
        {
            Check(report, current.TryGetValue(old.path, out var now), "A repeated build removed " + old.path);
            if (now == null) continue;
            bool semantic = !string.IsNullOrEmpty(old.semanticSha256) && !string.IsNullOrEmpty(now.semanticSha256);
            bool contentMatches = semantic ? old.semanticSha256 == now.semanticSha256 : old.sha256 == now.sha256;
            bool countsMatch = old.colliders == now.colliders && old.markers == now.markers && old.sockets == now.sockets
                && old.objects == now.objects && old.components == now.components && old.appearanceGroups == now.appearanceGroups
                && old.furnitureParts == now.furnitureParts && old.wallSkins == now.wallSkins
                && old.navTriangles == now.navTriangles && old.navVertices == now.navVertices;
            Check(report, contentMatches && countsMatch, "A repeated build changed content or component counts: " + old.path);
            if (semantic && contentMatches && countsMatch && old.sha256 != now.sha256)
            {
                report.serializationOnlyChanges++;
                report.details.Add("Generated asset bytes changed while its semantic signature matched: " + old.path);
            }
        }
        report.passed = report.errors.Count == 0;
        return JsonUtility.ToJson(report, true);
    }

    static string PrefabSignature(GameObject root, AssetRecord row)
    {
        var paths = new Dictionary<Transform, string>();
        AddPaths(root.transform, "$", paths);
        var references = new Dictionary<Object, string>();
        var content = new StringBuilder();
        row.objects = paths.Count;
        foreach (var pair in paths.OrderBy(pair => pair.Value, StringComparer.Ordinal))
        {
            var node = pair.Key;
            var gameObject = node.gameObject;
            Field(content, pair.Value);
            Field(content, node.name);
            Field(content, gameObject.activeSelf.ToString());
            Field(content, gameObject.layer.ToString(CultureInfo.InvariantCulture));
            Field(content, gameObject.tag);
            Field(content, ((int)GameObjectUtility.GetStaticEditorFlags(gameObject)).ToString(CultureInfo.InvariantCulture));
            Field(content, node.localPosition.ToString("R", CultureInfo.InvariantCulture));
            Field(content, node.localRotation.ToString("R", CultureInfo.InvariantCulture));
            Field(content, node.localScale.ToString("R", CultureInfo.InvariantCulture));
            if (node.name == MapRefreshBuilder.AppearanceName || node.name == "FurnitureV3") row.appearanceGroups++;
            if (node.parent && node.parent.parent && node.parent.parent.name == "FurnitureV3") row.furnitureParts++;
            var components = gameObject.GetComponents<Component>();
            row.components += components.Length;
            row.wallSkins += components.Count(component => component is BaekhoWallAppearanceInstance);
            // Preserve same-type component order. Ignore native serialization order between unrelated component types.
            foreach (var component in components.Where(component => !(component is Transform))
                         .OrderBy(component => component ? component.GetType().FullName : "<missing>", StringComparer.Ordinal))
            {
                Field(content, component ? component.GetType().FullName : "<missing>");
                if (!component) continue;
                using var serialized = new SerializedObject(component);
                using var property = serialized.GetIterator();
                var managedReferences = new Dictionary<long, int>();
                bool enter = true;
                while (property.Next(enter))
                {
                    enter = true;
                    // Ownership pointers are already represented by hierarchy paths and the component type.
                    if (property.propertyPath == "m_GameObject" || property.propertyPath == "m_PrefabAsset"
                        || property.propertyPath == "m_PrefabInstance" || property.propertyPath == "m_CorrespondingSourceObject")
                    { enter = false; continue; }
                    if (property.propertyType == SerializedPropertyType.Generic) continue;
                    Field(content, property.propertyPath);
                    Field(content, property.propertyType.ToString());
                    if (property.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        var value = property.objectReferenceValue;
                        Field(content, value ? ReferenceKey(value, paths, references)
                            : property.objectReferenceInstanceIDValue == 0 ? "null" : "missing reference");
                    }
                    else if (property.propertyType == SerializedPropertyType.ExposedReference)
                        Field(content, ReferenceKey(property.exposedReferenceValue, paths, references));
                    else if (property.propertyType == SerializedPropertyType.ManagedReference)
                    {
                        Field(content, property.managedReferenceFullTypename);
                        if (managedReferences.TryGetValue(property.managedReferenceId, out int ordinal))
                        { Field(content, "alias:" + ordinal); enter = false; }
                        else managedReferences.Add(property.managedReferenceId, managedReferences.Count);
                    }
                    else Field(content, property.contentHash.ToString());
                }
            }
        }
        return Hash(Encoding.UTF8.GetBytes(content.ToString()));
    }

    static void AddPaths(Transform parent, string path, Dictionary<Transform, string> paths)
    {
        paths.Add(parent, path);
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Transform child in parent)
        {
            names.TryGetValue(child.name, out int ordinal);
            names[child.name] = ordinal + 1;
            AddPaths(child, path + "/" + child.name.Length + ":" + child.name + ":" + ordinal, paths);
        }
    }

    static string ReferenceKey(Object value, Dictionary<Transform, string> paths, Dictionary<Object, string> cache)
    {
        if (!value) return "null";
        if (cache.TryGetValue(value, out var result)) return result;
        var component = value as Component;
        var node = value is GameObject go ? go.transform : component ? component.transform : null;
        string asset = AssetPath(value);
        if (node && paths.TryGetValue(node, out var local)) result = "local:" + local + ComponentSuffix(component);
        else if (node && !string.IsNullOrEmpty(asset))
        {
            var external = new Dictionary<Transform, string>();
            AddPaths(node.root, "$", external);
            result = "external:" + asset + ":" + external[node] + ComponentSuffix(component);
        }
        else if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long fileId))
            result = "asset:" + asset + ":" + guid + ":" + fileId + ":" + value.GetType().FullName;
        else result = "object:" + asset + ":" + value.GetType().FullName + ":" + value.name;
        cache.Add(value, result);
        return result;
    }

    static string ComponentSuffix(Component component) => component
        ? "::" + component.GetType().FullName + ":" + Array.IndexOf(component.GetComponents(component.GetType()), component)
        : "::GameObject";

    static string NavigationSignature(NavMeshData data, float isolationFloor, AssetRecord row)
    {
        // Add only this data high above every pre-existing surface. Unity's global triangulation
        // can then identify its vertices without removing or changing any other registered NavMesh.
        var offset = Vector3.up * (isolationFloor + Mathf.Abs(data.sourceBounds.min.y) + 32f);
        var handle = NavMesh.AddNavMeshData(data, offset, Quaternion.identity);
        if (!handle.valid) throw new InvalidDataException("Could not inspect generated navigation: " + row.path);
        try
        {
            var mesh = NavMesh.CalculateTriangulation();
            var triangles = new List<string>();
            var vertices = new HashSet<string>(StringComparer.Ordinal);
            var indices = mesh.indices ?? Array.Empty<int>();
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                var a = mesh.vertices[indices[i]];
                var b = mesh.vertices[indices[i + 1]];
                var c = mesh.vertices[indices[i + 2]];
                if (a.y < isolationFloor || b.y < isolationFloor || c.y < isolationFloor) continue;
                var corners = new[] { NavigationVertex(a - offset), NavigationVertex(b - offset), NavigationVertex(c - offset) };
                Array.Sort(corners, StringComparer.Ordinal);
                vertices.UnionWith(corners);
                triangles.Add(mesh.areas[i / 3].ToString(CultureInfo.InvariantCulture) + ":" + string.Join(";", corners));
            }
            triangles.Sort(StringComparer.Ordinal);
            row.navTriangles = triangles.Count;
            row.navVertices = vertices.Count;
            string content = NavigationVertex(data.sourceBounds.center) + "\n" + NavigationVertex(data.sourceBounds.size)
                + "\n" + NavigationVertex(data.position) + "\n" + data.rotation.ToString("R", CultureInfo.InvariantCulture)
                + "\n" + string.Join("\n", triangles);
            return Hash(Encoding.UTF8.GetBytes(content));
        }
        finally { handle.Remove(); }
    }

    // Millimetre precision ignores translation roundoff in the temporary isolation placement.
    static string NavigationVertex(Vector3 point) => string.Concat(Mathf.RoundToInt(point.x * 1000f).ToString(CultureInfo.InvariantCulture), ",",
        Mathf.RoundToInt(point.y * 1000f).ToString(CultureInfo.InvariantCulture), ",", Mathf.RoundToInt(point.z * 1000f).ToString(CultureInfo.InvariantCulture));

    static void Field(StringBuilder content, string value)
    {
        value ??= "";
        content.Append(value.Length).Append(':').Append(value).Append('|');
    }

    public static string RenderPreviews() => MapRefreshPreviews.Render();

    [MenuItem("Tools/Studio Baekho/Map Refresh/5 Validate Gameplay and Sources")]
    static void ValidateMenu() => Debug.Log(Run());
    [MenuItem("Tools/Studio Baekho/Map Refresh/6 Render Before and After Previews")]
    static void PreviewsMenu() => Debug.Log(RenderPreviews());

    static void CompareRoom(Report report, Entry entry)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(entry.sourcePath);
        var target = AssetDatabase.LoadAssetAtPath<GameObject>(entry.prefabPath);
        if (!Check(report, source && target && source != target, "Expected separate original and derived prefabs: " + entry.prefabPath)) return;
        report.entries++;
        var sourceModule = source.GetComponent<BaekhoMapModule>();
        var targetModule = target.GetComponent<BaekhoMapModule>();
        if (!Check(report, sourceModule && targetModule, "Missing module metadata: " + entry.prefabPath)) return;
        Check(report, Near(sourceModule.localBounds.center, targetModule.localBounds.center) && Near(sourceModule.localBounds.size, targetModule.localBounds.size),
            entry.prefabPath + ": module planning bounds changed.");
        Check(report, sourceModule.isCornerRoom == targetModule.isCornerRoom && sourceModule.encounterBudget == targetModule.encounterBudget,
            entry.prefabPath + ": corner classification or encounter budget changed.");
        var targetColliders = target.GetComponentsInChildren<Collider>(true).ToDictionary(c => ColliderKey(c, target.transform), StringComparer.Ordinal);
        foreach (var collider in source.GetComponentsInChildren<Collider>(true).Where(c => ProtectedCollider(c, sourceModule)))
        {
            report.structuralColliders++;
            string key = ColliderKey(collider, source.transform);
            if (!Check(report, targetColliders.TryGetValue(key, out var copy), entry.prefabPath + ": missing structural/interactive collider " + key)) continue;
            Check(report, SameCollider(collider, source.transform, copy, target.transform), entry.prefabPath + ": collider changed: " + key);
        }
        var sourceGround = (sourceModule.groundObjects ?? Array.Empty<GameObject>()).Select(g => Relative(g ? g.transform : null, source.transform)).OrderBy(p => p).ToArray();
        var targetGround = (targetModule.groundObjects ?? Array.Empty<GameObject>()).Select(g => Relative(g ? g.transform : null, target.transform)).OrderBy(p => p).ToArray();
        Check(report, sourceGround.All(targetGround.Contains), entry.prefabPath + ": an original authored ground reference was removed.");
        CompareSockets(report, sourceModule, targetModule, entry.prefabPath);
        foreach (var marker in source.GetComponentsInChildren<BaekhoGameplayMarker>(true))
        {
            report.markers++;
            string path = Relative(marker.transform, source.transform);
            var copy = Find(target.transform, path)?.GetComponent<BaekhoGameplayMarker>();
            Check(report, copy && marker.id == copy.id && marker.kind == copy.kind && marker.label == copy.label && marker.contentKey == copy.contentKey
                && Mathf.Abs(marker.clearanceRadius - copy.clearanceRadius) < .001f && Mathf.Abs(marker.clearanceHeight - copy.clearanceHeight) < .001f
                && SamePose(marker.transform, source.transform, copy.transform, target.transform), entry.prefabPath + ": gameplay marker changed: " + path);
        }
        CompareAnchors(report, source, target, entry.prefabPath);
        Check(report, target.GetComponentsInChildren<MeshCollider>(true).All(c => c.sharedMesh), entry.prefabPath + ": empty MeshCollider.");
        Check(report, target.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m && m.shader)), entry.prefabPath + ": missing material/shader.");
        if (entry.role != "corner") report.mainRooms++;
    }

    static void CompareSockets(Report report, BaekhoMapModule source, BaekhoMapModule target, string label)
    {
        var originals = source.connections ?? Array.Empty<BaekhoMapConnection>();
        var copies = target.connections ?? Array.Empty<BaekhoMapConnection>();
        Check(report, originals.Length == copies.Length, label + ": socket count changed.");
        foreach (var socket in originals)
        {
            report.sockets++;
            var copy = socket ? copies.FirstOrDefault(c => c && c.id == socket.id) : null;
            if (!Check(report, socket && copy, label + ": missing socket identity.")) continue;
            Check(report, socket.kind == copy.kind && socket.label == copy.label && Mathf.Abs(socket.width - copy.width) < .001f
                && Mathf.Abs(socket.height - copy.height) < .001f && SamePose(socket.transform, source.transform, copy.transform, target.transform),
                label + ": socket pose/dimensions changed: " + socket.id);
            Check(report, Relative(socket.runtimeDoor ? socket.runtimeDoor.transform : null, source.transform)
                == Relative(copy.runtimeDoor ? copy.runtimeDoor.transform : null, target.transform), label + ": runtime door binding changed: " + socket.id);
        }
    }

    static void CompareAnchors(Report report, GameObject source, GameObject target, string label)
    {
        var a = source.GetComponent<StageMapAnchors>();
        if (!a) return;
        var b = target.GetComponent<StageMapAnchors>();
        if (!Check(report, b, label + ": StageMapAnchors was removed.")) return;
        foreach (var field in typeof(StageMapAnchors).GetFields().Where(f => f.FieldType == typeof(Transform)))
        {
            var point = field.GetValue(a) as Transform;
            if (!point) continue;
            var copy = field.GetValue(b) as Transform;
            Check(report, copy && SamePose(point, source.transform, copy, target.transform)
                && Relative(point, source.transform) == Relative(copy, target.transform), label + ": role/teleport anchor changed: " + field.Name);
        }
        Check(report, Near(a.exitSize, b.exitSize), label + ": exit trigger size changed.");
    }

    static void CheckSettings(Report report, Manifest manifest, string beforeJson)
    {
        var v3 = JsonUtility.FromJson<RoomSetV3.SetReport>(File.ReadAllText(RoomSetV3.Root + "/Manifests/RoomSet_manifest.json"));
        var expected = new HashSet<string>(manifest.entries.Where(e => e.role == "normal").Select(e => e.prefabPath), StringComparer.Ordinal);
        expected.UnionWith(v3.rooms.Select(r => r.prefab));
        expected.RemoveWhere(path => BaekhoDungeonSetup.HasTwoByThreeDoor(
            AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponent<BaekhoMapModule>()));
        var refreshed = new HashSet<string>(manifest.entries.Where(e => e.role == "normal").Select(e => e.prefabPath), StringComparer.Ordinal);
        var v3Paths = new HashSet<string>(v3.rooms.Select(r => r.prefab), StringComparer.Ordinal);
        var replacement = manifest.entries.ToDictionary(e => e.sourcePath, e => e.prefabPath, StringComparer.Ordinal);
        var before = string.IsNullOrEmpty(beforeJson) ? null : JsonUtility.FromJson<AssetSnapshot>(beforeJson);
        Check(report, expected.Count == 32, "Expected 32 unique ordinary modules after excluding 2 x 3 doorways.");
        report.ordinaryPool = expected.Count;
        foreach (var path in SettingsPaths(manifest))
        {
            var settings = AssetDatabase.LoadAssetAtPath<BaekhoDungeonSettings>(path);
            if (!Check(report, settings, "Missing generator settings: " + path)) continue;
            var maps = settings.maps ?? Array.Empty<BaekhoMapModule>();
            var actual = new HashSet<string>(maps.Where(m => m).Select(AssetPath), StringComparer.Ordinal);
            Check(report, maps.All(m => m) && actual.SetEquals(expected), path + ": the ordinary pool is not exactly 32 unique expected modules.");
            Check(report, string.IsNullOrEmpty(manifest.stylePath) || AssetPath(settings.style) == manifest.stylePath, path + ": refreshed corridor style is not connected.");
            Check(report, BaekhoDungeonGenerator.ValidateStyle(settings.style, out var styleError), path + ": " + styleError);
            var roleAssets = new[] { settings.startRoom, settings.endRoom, settings.bossRoom, settings.shopRoom, settings.cornerRoom };
            string[] roleNames = { "start", "end", "boss", "shop", "corner" };
            for (int i = 0; i < roleAssets.Length; i++)
            {
                if (!roleAssets[i]) continue;
                string currentPath = AssetPath(roleAssets[i]);
                bool sharedDemoEnd = i == 1 && settings.startRoom == settings.endRoom
                    && (path == "Assets/_Game/ProceduralDungeon/DungeonSettings.asset"
                        || path == "Assets/_Project/Rendering/LabStyle/LabDungeonSettings.asset");
                Check(report, manifest.entries.Any(e => e.prefabPath == currentPath
                    && (e.role == roleNames[i] || ((i == 0 || sharedDemoEnd) && e.role == "demo-start"))),
                    path + ": dedicated " + roleNames[i] + " does not reference the corresponding derived prefab.");
            }
            var prior = before?.assets.FirstOrDefault(r => r.path == path);
            if (prior != null)
            {
                string[] oldRoles = { prior.startRoom, prior.endRoom, prior.bossRoom, prior.shopRoom, prior.cornerRoom };
                string[] nowRoles = { AssetPath(settings.startRoom), AssetPath(settings.endRoom), AssetPath(settings.bossRoom), AssetPath(settings.shopRoom), AssetPath(settings.cornerRoom) };
                for (int i = 0; i < oldRoles.Length; i++)
                {
                    string required = oldRoles[i] != null && replacement.TryGetValue(oldRoles[i], out var output) ? output : oldRoles[i];
                    Check(report, (required ?? "") == (nowRoles[i] ?? ""), path + ": dedicated role reference changed incorrectly (slot " + i + ").");
                }
            }
            for (int seed = 0; seed < 10; seed++)
            {
                if (!Check(report, BaekhoDungeonPlanner.TryPlan(settings, seed, out var plan, out var error), path + " seed " + seed + ": " + error)) continue;
                report.generatedPlans++;
                Check(report, plan.rooms.Count == settings.mapCount && plan.links.Count == Mathf.Max(0, settings.mapCount - 1), path + ": partial generation plan.");
                Check(report, plan.rooms.All(r => !BaekhoDungeonSetup.HasTwoByThreeDoor(r.prefab)), path + " seed " + seed + ": generated a room with a 2 x 3 doorway.");
                report.refreshedSelections += plan.rooms.Count(r => refreshed.Contains(AssetPath(r.prefab)));
                report.v3Selections += plan.rooms.Count(r => v3Paths.Contains(AssetPath(r.prefab)));
            }
        }
        Check(report, report.refreshedSelections > 0 && report.v3Selections > 0, "Actual generation plans must select both refreshed and unchanged V3 rooms.");
        var style = AssetDatabase.LoadAssetAtPath<BaekhoCorridorStyle>(manifest.stylePath ?? "");
        var sourceStyle = AssetDatabase.LoadAssetAtPath<BaekhoCorridorStyle>(manifest.sourceStylePath ?? "");
        if (style && sourceStyle)
            Check(report, Mathf.Abs(style.wallHeight - sourceStyle.wallHeight) < .001f && Mathf.Abs(style.wallThickness - sourceStyle.wallThickness) < .001f
                && Mathf.Abs(style.floorThickness - sourceStyle.floorThickness) < .001f && Mathf.Abs(style.ceilingThickness - sourceStyle.ceilingThickness) < .001f
                && style.groundLayer == sourceStyle.groundLayer, "Corridor collision dimensions/ground layer changed.");
    }

    static void CheckNativeGeometry(Report report, Manifest manifest)
    {
        using var player = MapRefreshAnchors.OpenPlayerScope();
        var controller = player.Controller;
        var catalog = AssetDatabase.LoadAssetAtPath<DungeonEnemyCatalog>("Assets/_Game/ProceduralDungeon/DungeonEnemyCatalog.asset");
        if (!Check(report, controller && catalog, "Native checks require the configured scene player and dungeon enemy catalog.")) return;
        var costs = catalog.GetValidatedCosts();
        var v3 = JsonUtility.FromJson<RoomSetV3.SetReport>(File.ReadAllText(RoomSetV3.Root + "/Manifests/RoomSet_manifest.json"));
        var rooms = manifest.entries.Where(entry => entry.role != "corner").Concat(v3.rooms.Select(room => new Entry
        {
            prefabPath = room.prefab,
            sourcePath = room.sourcePrefabPath,
            role = "normal"
        })).ToArray();
        Check(report, rooms.Length == 44 && rooms.Count(entry => entry.role == "normal") == 39
            && rooms.Select(entry => entry.prefabPath).Distinct(StringComparer.Ordinal).Count() == 44,
            "Native checks require all 39 ordinary rooms and five dedicated/demo rooms.");
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
        try
        {
            int index = 0;
            foreach (var entry in rooms)
            {
                GameObject instance = null;
                NavMeshData owned = null;
                try
                {
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(entry.prefabPath);
                    if (!Check(report, source, "Missing room for native geometry checks: " + entry.prefabPath)) continue;
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                    foreach (var s in instance.GetComponentsInChildren<NavMeshSurface>(true)) s.RemoveData();
                    instance.transform.SetPositionAndRotation(new Vector3(50000 + index++ * 256f, 0, 50000), Quaternion.Euler(0, (index % 4) * 90, 0));
                    var module = instance.GetComponent<BaekhoMapModule>();
                    if (!Check(report, module, entry.prefabPath + ": no runtime module.")) continue;
                    Physics.SyncTransforms();
                    var anchors = instance.GetComponent<StageMapAnchors>();
                    string landingError = "Missing StageMapAnchors.teleportPoint.";
                    bool landing = anchors && anchors.teleportPoint && DungeonFastTravelLanding.TryResolve(module, anchors.teleportPoint, controller, out _, out _, out landingError);
                    Check(report, landing, entry.prefabPath + ": actual player capsule cannot resolve teleport landing: " + landingError);
                    if (landing) report.teleports++;
                    if (entry.role == "normal" && module.encounterBudget > 0)
                    {
                        if (!Check(report, DungeonEncounterPlacement.TryPrepareNavigation(module, out owned, out var error), entry.prefabPath + ": " + error)) continue;
                        var composition = BaekhoEncounterPlanner.Plan(module.encounterBudget, costs, 74129 + index);
                        var enemies = new List<GameObject>();
                        for (int species = 0; species < composition.Counts.Length; species++)
                            for (int count = 0; count < composition.Counts[species]; count++) enemies.Add(catalog.enemies[species].prefab);
                        bool planned = DungeonEncounterPlacement.TryPlan(module, enemies, 74129 + index, 3f, out var placements, out error);
                        Check(report, planned && placements.Count == enemies.Count, entry.prefabPath + ": native safe encounter placement failed: " + error);
                        if (planned) report.enemyPlacements += placements.Count;
                    }
                    else if (entry.role == "boss")
                    {
                        var boss = ConfiguredBoss(entry.prefabPath);
                        if (Check(report, boss && anchors && anchors.bossSpawn, entry.prefabPath + ": configured boss prefab/spawn is missing.")
                            && Check(report, DungeonEncounterPlacement.TryPrepareNavigation(module, out owned, out var error), entry.prefabPath + ": " + error))
                        {
                            bool planned = DungeonEncounterPlacement.TryPlanBoss(module, boss, anchors.bossSpawn, out _, out error);
                            Check(report, planned, entry.prefabPath + ": actual boss capsule cannot occupy its preserved spawn: " + error);
                            if (planned) report.enemyPlacements++;
                        }
                    }
                    report.nativeRooms++;
                }
                catch (Exception exception) { Check(report, false, entry.prefabPath + ": native check: " + exception.Message); }
                finally
                {
                    if (instance)
                    {
                        foreach (var s in instance.GetComponentsInChildren<NavMeshSurface>(true)) s.RemoveData();
                        Object.DestroyImmediate(instance);
                    }
                    if (owned && !EditorUtility.IsPersistent(owned)) Object.DestroyImmediate(owned);
                }
            }
        }
        finally
        {
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void CheckProtectedAssets(Report report)
    {
        string path = Path.Combine(VerificationRoot, "ProtectedAssetHashes.json");
        if (!Check(report, File.Exists(path), "Protected source/V3 asset baseline is missing.")) return;
        var list = JsonUtility.FromJson<ProtectedList>("{\"entries\":" + File.ReadAllText(path) + "}");
        foreach (var item in list.entries)
        {
            report.protectedAssets++;
            Check(report, File.Exists(item.path) && string.Equals(Hash(File.ReadAllBytes(item.path)), item.hash, StringComparison.OrdinalIgnoreCase),
                "Protected source/V3 asset changed: " + item.path);
        }
    }

    static GameObject ConfiguredBoss(string roomPath)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:RunDefinitionSO"))
        {
            var definition = AssetDatabase.LoadAssetAtPath<RunDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var chapter in definition.chapters ?? Array.Empty<ChapterDefinition>())
                foreach (var stage in chapter.stages ?? Array.Empty<StageDefinition>())
                    if (stage != null && stage.dungeonSettings && stage.bossPrefab && AssetPath(stage.dungeonSettings.bossRoom) == roomPath)
                        return stage.bossPrefab;
        }
        return null;
    }

    static void CompareProtectedSnapshots(Report report, string beforeJson, string currentJson)
    {
        var before = JsonUtility.FromJson<AssetSnapshot>(beforeJson);
        var current = JsonUtility.FromJson<AssetSnapshot>(currentJson).assets.ToDictionary(r => r.path, StringComparer.Ordinal);
        foreach (var old in before.assets.Where(r => r.category == "source" || r.category == "v3"))
            Check(report, current.TryGetValue(old.path, out var now) && now.sha256 == old.sha256,
                "Protected snapshot content changed: " + old.path);
    }

    static bool ProtectedCollider(Collider collider, BaekhoMapModule module)
    {
        if (collider.isTrigger) return true;
        if ((module.groundObjects ?? Array.Empty<GameObject>()).Any(g => g && (collider.gameObject == g || collider.transform.IsChildOf(g.transform)))) return true;
        for (var at = collider.transform; at && at != module.transform; at = at.parent)
        {
            string n = at.name.ToLowerInvariant();
            if (n.StartsWith("prop_") || n == "roomdecoration" || n == "authoredcompositionv3") return false;
            if (new[] { "wall", "floor", "ceiling", "stair", "bridge", "slope", "door", "cliff", "structure" }.Any(n.StartsWith)) return true;
        }
        return false;
    }

    static bool SameCollider(Collider a, Transform ar, Collider b, Transform br)
    {
        if (a.GetType() != b.GetType() || a.enabled != b.enabled || a.isTrigger != b.isTrigger || a.gameObject.layer != b.gameObject.layer
            || a.gameObject.activeSelf != b.gameObject.activeSelf || a.sharedMaterial != b.sharedMaterial || Mathf.Abs(a.contactOffset - b.contactOffset) > .0001f) return false;
        var ma = ar.worldToLocalMatrix * a.transform.localToWorldMatrix;
        var mb = br.worldToLocalMatrix * b.transform.localToWorldMatrix;
        for (int i = 0; i < 16; i++) if (Mathf.Abs(ma[i] - mb[i]) > .001f) return false;
        if (a is BoxCollider box && b is BoxCollider boxB) return Near(box.center, boxB.center) && Near(box.size, boxB.size);
        if (a is SphereCollider sphere && b is SphereCollider sphereB) return Near(sphere.center, sphereB.center) && Mathf.Abs(sphere.radius - sphereB.radius) < .001f;
        if (a is CapsuleCollider capsule && b is CapsuleCollider capsuleB) return Near(capsule.center, capsuleB.center) && capsule.direction == capsuleB.direction
            && Mathf.Abs(capsule.radius - capsuleB.radius) < .001f && Mathf.Abs(capsule.height - capsuleB.height) < .001f;
        if (a is MeshCollider mesh && b is MeshCollider meshB) return mesh.sharedMesh == meshB.sharedMesh && mesh.convex == meshB.convex;
        return true;
    }

    static string ColliderKey(Collider collider, Transform root)
    {
        // A prefab can contain sibling jambs with identical names. Preserve their authored order in the key.
        var segments = new List<string>();
        for (var node = collider.transform; node && node != root; node = node.parent)
            segments.Add(node.name + "[" + node.GetSiblingIndex() + "]");
        segments.Reverse();
        int index = Array.IndexOf(collider.GetComponents(collider.GetType()), collider);
        return string.Join("/", segments) + "::" + collider.GetType().FullName + ":" + index;
    }
    static string Relative(Transform child, Transform root)
    {
        if (!child) return "";
        var parts = new List<string>();
        for (var at = child; at && at != root; at = at.parent) parts.Add(at.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
    static Transform Find(Transform root, string path) => string.IsNullOrEmpty(path) ? root : root.Find(path);
    static bool SamePose(Transform a, Transform ar, Transform b, Transform br) => Near(ar.InverseTransformPoint(a.position), br.InverseTransformPoint(b.position))
        && Quaternion.Angle(Quaternion.Inverse(ar.rotation) * a.rotation, Quaternion.Inverse(br.rotation) * b.rotation) < .1f;
    static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < .000001f;
    static string AssetPath(Object asset) => asset ? AssetDatabase.GetAssetPath(asset) : "";
    static IEnumerable<string> SettingsPaths(Manifest manifest) => manifest.settingsPaths != null && manifest.settingsPaths.Length > 0 ? manifest.settingsPaths : DefaultSettings;
    static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
    static bool Check(Report report, bool passed, string message)
    {
        report.checks++;
        if (!passed && report.errors.Count < 120) report.errors.Add(message);
        return passed;
    }
}
#endif
