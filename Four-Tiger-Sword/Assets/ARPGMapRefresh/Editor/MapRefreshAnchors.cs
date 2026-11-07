#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using StudioBaekho;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Installs safe fast-travel anchors only on the owned map-refresh derivatives.</summary>
public static class MapRefreshAnchors
{
    public const string GameScenePath = "Assets/_Project/Scenes/Game Scene.unity";
    [Serializable] public sealed class Report
    {
        public bool passed;
        public int validated, installed;
        public List<string> changed = new List<string>();
        public List<string> errors = new List<string>();
    }
    sealed class Proposal
    {
        public string path, pointPath, sourceHash;
        public Vector3 position;
        public Quaternion rotation;
    }

    /// <summary>Temporarily opens Game Scene if required; never saves it or changes the user's active scene.</summary>
    public sealed class PlayerScope : IDisposable
    {
        readonly Scene original;
        readonly Object[] selection;
        Scene game;
        bool owned, disposed;
        public CharacterController Controller { get; private set; }
        public PlayerScope()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use the player inspection scope in Edit Mode.");
            original = SceneManager.GetActiveScene();
            selection = Selection.objects;
            try
            {
                game = SceneManager.GetSceneByPath(GameScenePath);
                if (!game.IsValid() || !game.isLoaded)
                {
                    game = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);
                    owned = true;
                }
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                var players = game.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayerController>(true)).ToArray();
                var player = players.FirstOrDefault(p => p.GetComponent<CharacterController>());
                Controller = player ? player.GetComponent<CharacterController>() : null;
                if (!Controller) throw new InvalidOperationException("Game Scene has no actual player CharacterController.");
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (owned && game.IsValid() && game.isLoaded) EditorSceneManager.CloseScene(game, true);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            Selection.objects = selection.Where(item => item).ToArray();
        }
    }

    public static PlayerScope OpenPlayerScope() => new PlayerScope();

    [MenuItem("Tools/Studio Baekho/Map Refresh/4 Install Scoped Teleport Anchors")]
    static void InstallMenu() => Debug.Log(Install());
    [MenuItem("Tools/Studio Baekho/Map Refresh/Validate Scoped Teleport Anchors")]
    static void ValidateMenu() => Debug.Log(Validate());
    public static string Install() => Inspect(true);
    public static string Validate() => Inspect(false);

    static string Inspect(bool install)
    {
        var report = new Report();
        var proposals = new List<Proposal>();
        var entries = MapRefreshValidation.ReadManifest().entries.Where(e => e.role != "corner").ToArray();
        if (entries.Length != 22) throw new InvalidDataException("Expected 22 noncorner map-refresh derivatives.");
        using (var player = OpenPlayerScope())
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var entry in entries)
                {
                    GameObject instance = null;
                    try
                    {
                        if (entry.prefabPath == entry.sourcePath || !entry.prefabPath.StartsWith("Assets/ARPGMapRefresh/Prefabs/Rooms/", StringComparison.Ordinal))
                            throw new InvalidOperationException("Refusing to author an anchor outside owned room derivatives: " + entry.prefabPath);
                        var source = AssetDatabase.LoadAssetAtPath<GameObject>(entry.prefabPath);
                        if (!source) throw new FileNotFoundException(entry.prefabPath);
                        string hash = FileHash(entry.prefabPath);
                        instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                        foreach (var surface in instance.GetComponentsInChildren<NavMeshSurface>(true))
                        { surface.RemoveData(); surface.enabled = false; }
                        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        var module = instance.GetComponent<BaekhoMapModule>();
                        bool changed = DungeonTeleportAnchorSetup.EnsureRoomAnchor(module, player.Controller);
                        report.validated++;
                        if (!changed) continue;
                        if (!install) { report.errors.Add(entry.prefabPath + ": missing assigned teleport anchor."); continue; }
                        var point = instance.GetComponent<StageMapAnchors>().teleportPoint;
                        proposals.Add(new Proposal { path = entry.prefabPath, sourceHash = hash,
                            pointPath = Relative(point, instance.transform), position = instance.transform.InverseTransformPoint(point.position),
                            rotation = Quaternion.Inverse(instance.transform.rotation) * point.rotation });
                    }
                    catch (Exception exception) { report.errors.Add(entry.prefabPath + ": " + exception.Message); }
                    finally { if (instance) Object.DestroyImmediate(instance); }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            // Validate every proposal and guard against concurrent edits before touching any prefab file.
            foreach (var proposal in proposals)
                if (FileHash(proposal.path) != proposal.sourceHash) report.errors.Add("Room changed during anchor inspection: " + proposal.path);
            if (report.errors.Count > 0) return JsonUtility.ToJson(report, true);
            foreach (var proposal in proposals)
            {
                var root = PrefabUtility.LoadPrefabContents(proposal.path);
                try
                {
                    var anchors = root.GetComponent<StageMapAnchors>() ?? root.AddComponent<StageMapAnchors>();
                    if (anchors.teleportPoint) throw new InvalidOperationException("Anchor was assigned after inspection: " + proposal.path);
                    var point = root.transform.Find(proposal.pointPath);
                    if (!point)
                    {
                        if (proposal.pointPath != "TeleportPoint") throw new InvalidOperationException("Missing previously inspected point: " + proposal.pointPath);
                        point = new GameObject("TeleportPoint").transform;
                        point.SetParent(root.transform, false);
                        point.localPosition = proposal.position;
                        point.localRotation = proposal.rotation;
                    }
                    anchors.teleportPoint = point;
                    if (!PrefabUtility.SaveAsPrefabAsset(root, proposal.path)) throw new IOException("Could not save anchor: " + proposal.path);
                    report.installed++;
                    report.changed.Add(proposal.path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }
        report.passed = report.errors.Count == 0 && report.validated == entries.Length;
        return JsonUtility.ToJson(report, true);
    }

    static string Relative(Transform child, Transform root)
    {
        var parts = new List<string>();
        for (var at = child; at && at != root; at = at.parent) parts.Add(at.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
    static string FileHash(string path)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
#endif
