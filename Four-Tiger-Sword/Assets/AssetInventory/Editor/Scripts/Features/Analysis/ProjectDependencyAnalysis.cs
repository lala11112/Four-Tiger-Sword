using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace AssetInventory
{
    /// <summary>Scans serialized project assets for GUID references and resolves the files that depend on a selected project asset.</summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static partial class ProjectDependencyAnalysis
    {
        private static int _virtualIdCounter;

        /// <summary>Returns GUIDs referenced by the supplied serialized project asset, optionally using a fresh file read instead of cached results.</summary>
        public static List<AssetFile> GetDependencies(string assetPath, bool recursive = true)
        {
            return GetDependencies(assetPath, 0, recursive);
        }

        internal static List<AssetFile> GetDependencies(string assetPath, int preferredAssetId, bool recursive = true)
        {
            _virtualIdCounter = -1;

            string[] allDeps = AssetDatabase.GetDependencies(assetPath, recursive);
            string rootGuid = AssetDatabase.AssetPathToGUID(assetPath);

            // Build parent relationships via BFS for tree structure
            Dictionary<string, HashSet<string>> parentMap = new Dictionary<string, HashSet<string>>();
            if (recursive)
            {
                // Get direct deps of the root
                string[] directDeps = AssetDatabase.GetDependencies(assetPath, false);
                foreach (string dep in directDeps)
                {
                    if (dep == assetPath) continue;
                    if (!parentMap.ContainsKey(dep)) parentMap[dep] = new HashSet<string>();
                    parentMap[dep].Add(rootGuid);
                }

                // BFS: for each dep, find its own direct deps to build the tree
                Queue<string> queue = new Queue<string>(directDeps.Where(d => d != assetPath));
                HashSet<string> visited = new HashSet<string> {assetPath};

                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    if (!visited.Add(current)) continue;

                    string currentGuid = AssetDatabase.AssetPathToGUID(current);
                    string[] childDeps = AssetDatabase.GetDependencies(current, false);
                    foreach (string child in childDeps)
                    {
                        if (child == current) continue;
                        if (!parentMap.ContainsKey(child)) parentMap[child] = new HashSet<string>();
                        parentMap[child].Add(currentGuid);
                        queue.Enqueue(child);
                    }
                }
            }

            List<AssetFile> result = new List<AssetFile>();
            foreach (string dep in allDeps)
            {
                if (dep == assetPath) continue;
                if (!dep.StartsWith("Assets/")) continue;
                if (AssetDatabase.IsValidFolder(dep)) continue;

                string guid = AssetDatabase.AssetPathToGUID(dep);
                string fileName = Path.GetFileName(dep);
                string ext = Path.GetExtension(dep);
                string type = !string.IsNullOrEmpty(ext) ? ext.Substring(1).ToLowerInvariant() : "";

                AssetFile file = new AssetFile();
                file.Id = _virtualIdCounter--;
                file.AssetId = -1;
                file.Guid = guid;
                file.Path = dep;
                file.FileName = fileName;
                file.Type = type;
                file.ProjectPath = dep;
                file.Size = GetProjectFileSize(dep);

                if (parentMap.TryGetValue(dep, out HashSet<string> parents))
                {
                    file.ParentGuids = parents;
                }

                result.Add(file);
            }

            if (preferredAssetId <= 0 || result.Count == 0) return result;

            List<AssetFile> indexedPackageFiles = DBAdapter.DB.Query<AssetFile>(
                "SELECT * FROM AssetFile WHERE AssetId=?", preferredAssetId);
            return ReconcileWithIndexedPackage(result, indexedPackageFiles)
                .OrderBy(file => file.AssetId)
                .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static List<AssetFile> ReconcileWithIndexedPackage(
            IReadOnlyList<AssetFile> projectDependencies,
            IReadOnlyList<AssetFile> indexedPackageFiles)
        {
            if (projectDependencies == null || projectDependencies.Count == 0) return new List<AssetFile>();
            if (indexedPackageFiles == null || indexedPackageFiles.Count == 0) return new List<AssetFile>(projectDependencies);

            Dictionary<string, AssetFile> indexedByGuid = indexedPackageFiles
                .Where(file => file != null && !string.IsNullOrEmpty(file.Guid))
                .GroupBy(file => file.Guid, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(file => file.Id).First(),
                    StringComparer.OrdinalIgnoreCase);

            List<AssetFile> result = new List<AssetFile>(projectDependencies.Count);
            foreach (AssetFile projectDependency in projectDependencies)
            {
                if (projectDependency == null || string.IsNullOrEmpty(projectDependency.Guid) ||
                    !indexedByGuid.TryGetValue(projectDependency.Guid, out AssetFile indexedFile))
                {
                    result.Add(projectDependency);
                    continue;
                }

                indexedFile.ProjectPath = projectDependency.ProjectPath;
                indexedFile.ParentGuids = projectDependency.ParentGuids == null
                    ? null
                    : new HashSet<string>(projectDependency.ParentGuids, StringComparer.OrdinalIgnoreCase);
                result.Add(indexedFile);
            }

            return result;
        }

        private static long GetProjectFileSize(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return 0;

            try
            {
                string fullPath = AssetUtils.AddProjectRoot(assetPath);
                return File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
        }

        /// <summary>Scans serialized project assets and returns files containing any supplied target GUID, reporting progress while it runs.</summary>
        public static async Task<List<string>> FindReferencesAsync(string targetPath, Action<int, int> onProgress = null)
        {
            string targetGuid = AssetDatabase.AssetPathToGUID(targetPath);
            if (string.IsNullOrEmpty(targetGuid)) return new List<string>();

            HashSet<string> targetGuids = new HashSet<string> {targetGuid};
            Dictionary<string, List<string>> usages = await FindReferencesAsync(targetGuids, onProgress);

            if (usages.TryGetValue(targetPath, out List<string> refs))
            {
                return refs;
            }
            return new List<string>();
        }

        /// <summary>Scans serialized project assets and returns files containing any supplied target GUID, reporting progress while it runs.</summary>
        public static async Task<Dictionary<string, List<string>>> FindReferencesAsync(HashSet<string> targetGuids, Action<int, int> onProgress = null)
        {
            Dictionary<string, List<string>> usages = new Dictionary<string, List<string>>();

            string[] allAssetGuids = AssetDatabase.FindAssets("");
            int total = allAssetGuids.Length;

            for (int i = 0; i < total; i++)
            {
                string guid = allAssetGuids[i];
                if (targetGuids.Contains(guid)) continue;

                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;

                string[] deps = AssetDatabase.GetDependencies(path, false);
                foreach (string depPath in deps)
                {
                    string depGuid = AssetDatabase.AssetPathToGUID(depPath);
                    if (targetGuids.Contains(depGuid))
                    {
                        if (!usages.ContainsKey(depPath))
                        {
                            usages[depPath] = new List<string>();
                        }
                        usages[depPath].Add(path);
                    }
                }

                if (i % 50 == 0)
                {
                    onProgress?.Invoke(i + 1, total);
                    await Task.Yield();
                }
            }

            onProgress?.Invoke(total, total);
            return usages;
        }
    }
}
