#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StudioBaekho;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Bakes repaired static visuals without modifying the authored meshes or physics.</summary>
public static class BaekhoCoplanarVisualRepair
{
    public const string OutputRoot = "Assets/_Project/Generated/CoplanarVisuals";
    public const string HubPrefab = "Assets/EasternLab_ModelOnly/EasternLab_Model.prefab";

    [Serializable]
    public sealed class Report
    {
        public string scope;
        public int inputTriangles, overlappingTriangles, removedTriangles, changedMeshes, restoredMeshes, passes;
        public double overlapArea;
        public bool physicsPreserved;
        public string[] assets;
    }

    [Serializable] private sealed class Sources { public Source[] meshes = Array.Empty<Source>(); }
    [Serializable] private sealed class Source
    {
        public string binding, originalGuid, repairedGuid;
        public long originalFileId;
    }

    /// <summary>Restore only our previous output bindings before any rebuild or collision preparation.</summary>
    public static int RestoreSources(GameObject root, string scope)
    {
        var manifests = new Dictionary<string, Sources>(StringComparer.Ordinal);
        var resolved = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        int restored = 0;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.sharedMesh) continue;
            string meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
            if (!meshPath.StartsWith(OutputRoot + "/", StringComparison.Ordinal)) continue;
            string path = meshPath.Substring(0, meshPath.LastIndexOf('/')) + "/Sources.json";
            if (!File.Exists(path)) throw new InvalidOperationException("Missing original mesh manifest: " + path);
            if (!manifests.TryGetValue(path, out Sources sources))
                manifests.Add(path, sources = JsonUtility.FromJson<Sources>(File.ReadAllText(path)));
            string repairedGuid = AssetDatabase.AssetPathToGUID(meshPath);
            var source = sources.meshes.FirstOrDefault(s => s.repairedGuid == repairedGuid);
            if (source == null) throw new InvalidOperationException("Missing original visual binding for " + meshPath);
            // Match mesh identity, so renamed, reordered, or copied objects can still restore their source.
            string sourceKey = source.originalGuid + ":" + source.originalFileId;
            if (!resolved.TryGetValue(sourceKey, out Mesh original))
            {
                string originalPath = AssetDatabase.GUIDToAssetPath(source.originalGuid);
                original = AssetDatabase.LoadAllAssetsAtPath(originalPath).OfType<Mesh>().FirstOrDefault(mesh =>
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long fileId)
                    && guid == source.originalGuid && fileId == source.originalFileId);
                if (!original) throw new InvalidOperationException("Missing original visual mesh for " + scope + "/" + source.binding);
                resolved.Add(sourceKey, original);
            }
            filter.sharedMesh = original;
            RecordBinding(filter);
            restored++;
        }
        return restored;
    }

    public static string[] TargetPrefabs()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal) { HubPrefab };
        foreach (var guid in AssetDatabase.FindAssets("t:BaekhoDungeonSettings"))
        {
            var settings = AssetDatabase.LoadAssetAtPath<StudioBaekho.BaekhoDungeonSettings>(AssetDatabase.GUIDToAssetPath(guid));
            if (!settings) continue;
            foreach (var module in (settings.maps ?? Array.Empty<StudioBaekho.BaekhoMapModule>())
                .Concat(new[] { settings.startRoom, settings.endRoom, settings.bossRoom, settings.shopRoom, settings.cornerRoom }))
                if (module) paths.Add(AssetDatabase.GetAssetPath(module));
        }
        paths.Remove("");
        return paths.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Called after physics preparation by the room builders, before prefab save.</summary>
    public static Report Repair(GameObject root, string scope)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Bake static visuals in Edit Mode.");
        if (!root || string.IsNullOrEmpty(scope)) throw new ArgumentException("A root and stable scope are required.");
        string before = PhysicsSignature(root);
        int restored = RestoreSources(root, scope);
        var result = SolveConverged(root, out int passes);
        var assets = new List<string>();
        var sources = new List<Source>();
        var pending = new HashSet<Mesh>(result.replacements.Values);
        try
        {
            string folder = OutputRoot + "/" + Hash128.Compute(scope);
            if (pending.Count > 0) EnsureFolder(folder);
            // Import generated meshes together; importing every small wall mesh separately is costly.
            AssetDatabase.StartAssetEditing();
            try
            {
            foreach (var replacement in result.replacements.OrderBy(p => HierarchyKey(p.Key.transform, root.transform), StringComparer.Ordinal))
            {
                Mesh mesh = replacement.Value;
                string key = HierarchyKey(replacement.Key.transform, root.transform);
                string path = folder + "/" + Hash128.Compute(key) + ".asset";
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(replacement.Key.sharedMesh, out string sourceGuid, out long sourceId)
                    || string.IsNullOrEmpty(sourceGuid))
                    throw new InvalidOperationException("Persist the authored mesh before visual repair: " + scope + "/" + key);
                var stored = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                // A visual-only bake must never alter a mesh referenced by collision.
                if (stored && root.GetComponentsInChildren<MeshCollider>(true).Any(c => c.sharedMesh == stored))
                    throw new InvalidOperationException("A repaired visual is being used by collision: " + path);
                mesh.name = replacement.Key.sharedMesh.name.Replace("_CoplanarRepaired", "") + "_CoplanarRepaired";
                if (stored)
                {
                    EditorUtility.CopySerialized(mesh, stored);
                    EditorUtility.SetDirty(stored);
                    Object.DestroyImmediate(mesh);
                }
                else
                {
                    AssetDatabase.CreateAsset(mesh, path);
                    stored = mesh;
                }
                pending.Remove(mesh);
                replacement.Key.sharedMesh = stored;
                RecordBinding(replacement.Key);
                AssetDatabase.SaveAssetIfDirty(stored);
                assets.Add(path);
                sources.Add(new Source { binding = key, originalGuid = sourceGuid, originalFileId = sourceId });
            }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            for (int i = 0; i < sources.Count; i++) sources[i].repairedGuid = AssetDatabase.AssetPathToGUID(assets[i]);
            bool physicsPreserved = before == PhysicsSignature(root);
            if (!physicsPreserved) throw new InvalidOperationException("Static visual repair changed physics or navigation: " + scope);
            if (sources.Count > 0 || restored > 0)
            {
                string sourcePath = SourcePath(scope);
                File.WriteAllText(sourcePath, JsonUtility.ToJson(new Sources { meshes = sources.ToArray() }, true));
                AssetDatabase.ImportAsset(sourcePath);
            }
            return new Report
            {
                scope = scope, inputTriangles = result.inputTriangles, overlappingTriangles = result.overlappingTriangles,
                removedTriangles = result.removedTriangles, overlapArea = result.overlapArea,
                changedMeshes = assets.Count, restoredMeshes = restored, passes = passes, physicsPreserved = physicsPreserved, assets = assets.ToArray()
            };
        }
        finally
        {
            foreach (var mesh in pending) if (mesh && !EditorUtility.IsPersistent(mesh)) Object.DestroyImmediate(mesh);
        }
    }

    // Splitting a long triangle can expose a smaller patch within the plane tolerance.
    // Resolve those patches in memory and verify convergence before writing any asset.
    private static BaekhoCoplanarMeshRepair.Result SolveConverged(GameObject root, out int passes)
    {
        var total = new BaekhoCoplanarMeshRepair.Result();
        var originals = new Dictionary<MeshFilter, Mesh>();
        var owned = new HashSet<Mesh>();
        bool converged = false;
        passes = 0;
        try
        {
            for (int pass = 0; pass <= 8; pass++)
            {
                var step = BaekhoCoplanarMeshRepair.Solve(root);
                if (pass == 0) total.inputTriangles = step.inputTriangles;
                if (step.replacements.Count == 0) { converged = true; break; }
                if (pass == 8)
                {
                    foreach (var mesh in step.replacements.Values) Object.DestroyImmediate(mesh);
                    break;
                }
                passes++;
                total.overlappingTriangles += step.overlappingTriangles;
                total.removedTriangles += step.removedTriangles;
                total.overlapArea += step.overlapArea;
                foreach (var pair in step.replacements)
                {
                    if (!originals.ContainsKey(pair.Key)) originals.Add(pair.Key, pair.Key.sharedMesh);
                    owned.Add(pair.Value);
                    pair.Key.sharedMesh = pair.Value;
                    if (total.replacements.TryGetValue(pair.Key, out Mesh previous))
                    {
                        owned.Remove(previous);
                        Object.DestroyImmediate(previous);
                    }
                    total.replacements[pair.Key] = pair.Value;
                }
            }
            if (!converged) throw new InvalidOperationException("Coplanar visual repair did not converge after eight passes: " + root.name);
            return total;
        }
        finally
        {
            foreach (var pair in originals) if (pair.Key) pair.Key.sharedMesh = pair.Value;
            if (!converged) foreach (var mesh in owned) if (mesh) Object.DestroyImmediate(mesh);
        }
    }

    public static Report RepairPrefab(string path)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Bake static visuals in Edit Mode.");
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Report report = Repair(root, path);
            if (report.changedMeshes > 0 || report.restoredMeshes > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved) throw new IOException("Could not save repaired visual references: " + path);
            }
            WriteReport(report);
            return report;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static string SourcePath(string scope) => OutputRoot + "/" + Hash128.Compute(scope) + "/Sources.json";

    private static void RecordBinding(MeshFilter filter)
    {
        EditorUtility.SetDirty(filter);
        if (PrefabUtility.IsPartOfPrefabInstance(filter)) PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
    }

    public static Report AuditPrefab(string path)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var result = BaekhoCoplanarMeshRepair.Solve(root);
            var report = new Report { scope = path, inputTriangles = result.inputTriangles,
                overlappingTriangles = result.overlappingTriangles, removedTriangles = result.removedTriangles,
                overlapArea = result.overlapArea, changedMeshes = result.replacements.Count, physicsPreserved = true,
                assets = Array.Empty<string>() };
            foreach (var mesh in result.replacements.Values) Object.DestroyImmediate(mesh);
            return report;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
    }

    private static string HierarchyKey(Transform transform, Transform root)
    {
        var parts = new List<string>();
        for (var at = transform; at; at = at.parent)
        {
            parts.Add((at == root ? 0 : at.GetSiblingIndex()) + ":" + at.name);
            if (at == root) break;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string PhysicsSignature(GameObject root)
    {
        return string.Join("\n", root.GetComponentsInChildren<Component>(true)
            .Where(c => c && (c is Collider || c is Rigidbody || c.GetType().Name.StartsWith("NavMesh", StringComparison.Ordinal)))
            .Select(c => HierarchyKey(c.transform, root.transform) + "|" + c.GetType().FullName + "|"
                + EditorJsonUtility.ToJson(c) + "|" + c.transform.localToWorldMatrix.ToString("F6")));
    }

    private static void WriteReport(Report report)
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/CoplanarRepairReports"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Hash128.Compute(report.scope) + ".json"), JsonUtility.ToJson(report, true));
    }
}
#endif
