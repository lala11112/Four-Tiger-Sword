// Invoke in the open Game Scene: unity command run_script --file Tools/RunProgressionSetup.cs --entry RunProgressionSetup.Install
using System;
using System.Linq;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using TMPro;

public static class RunProgressionSetup
{
    public const string Folder = "Assets/_Game/RunProgression";
    public const string DefinitionPath = Folder + "/DefaultRun.asset";

    public static string Install()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before installing.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/_Project/Scenes/Game Scene.unity") throw new InvalidOperationException("Open Game Scene first.");
        EnsureFolder(Folder);
        EnsureFolder(Folder + "/Rooms");
        EnsureFolder(Folder + "/Settings");
        EnsureFolder(Folder + "/Prefabs");
        var source = AssetDatabase.LoadAssetAtPath<BaekhoDungeonSettings>("Assets/_Game/ProceduralDungeon/DungeonSettings.asset");
        var catalog = AssetDatabase.LoadAssetAtPath<DungeonEnemyCatalog>("Assets/_Game/ProceduralDungeon/DungeonEnemyCatalog.asset");
        if (source == null || source.startRoom == null || source.maps.Length == 0 || catalog == null) throw new InvalidOperationException("Existing dungeon content is missing.");
        var start = CopyRoom(source.startRoom, "RunStart", 0);
        var end = CopyRoom(source.endRoom, "RunExit", 1);
        var boss = CopyRoom(source.maps[0], "ChapterBossRoom", 2);
        var bossPrefab = GetBossPrefab();
        var normalSettings = CopySettings(source, "NormalStage", start, end, null);
        var bossSettings = CopySettings(source, "BossStage", start, end, boss);
        var definition = AssetDatabase.LoadAssetAtPath<RunDefinitionSO>(DefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<RunDefinitionSO>();
            definition.chapters = Enumerable.Range(0, 2).Select(c => new ChapterDefinition
            {
                displayName = "Chapter " + (c + 1),
                stages = Enumerable.Range(0, 3).Select(s => new StageDefinition
                {
                    displayName = s == 2 ? "Boss" : "Exploration " + (s + 1),
                    dungeonSettings = s == 2 ? bossSettings : normalSettings,
                    enemyCatalog = catalog,
                    mapCountVariation = 1,
                    bossPrefab = s == 2 ? bossPrefab : null
                }).ToArray()
            }).ToArray();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }
        if (!definition.Validate(out string error)) throw new InvalidOperationException(error);
        var generator = UnityEngine.Object.FindFirstObjectByType<BaekhoDungeonGenerator>();
        if (generator == null) throw new InvalidOperationException("Scene has no dungeon generator.");
        var director = generator.GetComponent<DungeonEncounterDirector>();
        var stage = UnityEngine.Object.FindFirstObjectByType<StageManager>();
        if (stage == null) stage = new GameObject("StageManager").AddComponent<StageManager>();
        Undo.RecordObject(generator, "Connect run progression");
        generator.generateOnStart = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(generator);
        Undo.RecordObject(stage, "Connect stage generator");
        stage.Configure(generator, director);
        var ui = stage.GetComponent<StageResultUI>() ?? Undo.AddComponent<StageResultUI>(stage.gameObject);
        var uiSerialized = new SerializedObject(ui);
        uiSerialized.FindProperty("fontAsset").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Font/NotoSansKR SDF.asset");
        uiSerialized.ApplyModifiedPropertiesWithoutUndo();
        var run = stage.GetComponent<RunManager>() ?? Undo.AddComponent<RunManager>(stage.gameObject);
        Undo.RecordObject(run, "Configure run");
        run.definition = definition;
        run.stageManager = stage;
        run.player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        run.resultUI = ui;
        run.randomizeSeed = true;
        // Authored test enemies are retained but are not part of generated stage encounters.
        foreach (var enemy in UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!enemy.gameObject.activeSelf || enemy.GetComponentInParent<BaekhoDungeonGenerator>() != null) continue;
            Undo.RecordObject(enemy.gameObject, "Disable standalone test enemy for run");
            enemy.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(enemy.gameObject);
        }
        EditorUtility.SetDirty(stage);
        EditorUtility.SetDirty(run);
        EditorUtility.SetDirty(ui);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"Installed {definition.chapters.Length} chapters / {definition.TotalStages} stages. Edit {DefinitionPath}. Boss uses existing RealBoss scene content.";
    }

    private static BaekhoMapModule CopyRoom(BaekhoMapModule source, string name, int role)
    {
        string path = Folder + "/Rooms/" + name + ".prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing.GetComponent<BaekhoMapModule>();
        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path)) throw new Exception("Could not copy room: " + path);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            root.name = name;
            RemoveMissingNestedPrefabs(root);
            var module = root.GetComponent<BaekhoMapModule>();
            module.encounterBudget = 0;
            var anchors = root.GetComponent<StageMapAnchors>() ?? root.AddComponent<StageMapAnchors>();
            var point = new GameObject(role == 0 ? "PlayerSpawn" : role == 1 ? "ExitPoint" : "BossSpawn").transform;
            point.SetParent(root.transform, false);
            if (role == 2)
            {
                var marker = root.GetComponentsInChildren<BaekhoGameplayMarker>(true).First(m => m.kind == "enemy");
                point.SetPositionAndRotation(marker.transform.position, marker.transform.rotation);
                anchors.bossSpawn = point;
            }
            else
            {
                point.localPosition = new Vector3(0, role == 0 ? 1.05f : 0.1f, 0);
                // Face the doorway when arriving in the otherwise empty start room.
                var forward = module.connections[0].transform.position - point.position;
                forward.y = 0;
                point.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                if (role == 0) anchors.playerSpawn = point; else anchors.exitPoint = point;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<BaekhoMapModule>();
    }

    private static BaekhoDungeonSettings CopySettings(BaekhoDungeonSettings source, string name, BaekhoMapModule start, BaekhoMapModule end, BaekhoMapModule boss)
    {
        string path = Folder + "/Settings/" + name + ".asset";
        var settings = AssetDatabase.LoadAssetAtPath<BaekhoDungeonSettings>(path);
        if (settings != null) return settings;
        settings = UnityEngine.Object.Instantiate(source);
        settings.name = name;
        settings.startRoom = start;
        settings.endRoom = end;
        settings.bossRoom = boss;
        AssetDatabase.CreateAsset(settings, path);
        return settings;
    }

    private static GameObject GetBossPrefab()
    {
        string path = Folder + "/Prefabs/ChapterBoss.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null) return prefab;
        var source = UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(e => e.name == "RealBoss (7)");
        if (source == null) throw new InvalidOperationException("Existing RealBoss (7) is missing; assign an authored boss.");
        var clone = UnityEngine.Object.Instantiate(source.gameObject);
        try
        {
            clone.name = "ChapterBoss";
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clone.SetActive(true);
            clone.GetComponent<Enemy>().enabled = true;
            clone.GetComponent<NavMeshAgent>().enabled = true;
            return PrefabUtility.SaveAsPrefabAsset(clone, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
    }

    public static string RepairMissingRoomPrefabs()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        int removed = 0;
        foreach (string name in new[] { "RunStart", "RunExit", "ChapterBossRoom" })
        {
            string path = Folder + "/Rooms/" + name + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                removed += RemoveMissingNestedPrefabs(root);
                // Repair the new anchor component if a first-import GUID changed during parallel authoring.
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
                var anchors = root.GetComponent<StageMapAnchors>() ?? root.AddComponent<StageMapAnchors>();
                anchors.playerSpawn = root.transform.Find("PlayerSpawn");
                anchors.exitPoint = root.transform.Find("ExitPoint");
                anchors.bossSpawn = root.transform.Find("BossSpawn");
                if (anchors.playerSpawn != null) anchors.playerSpawn.localPosition = new Vector3(0, 1.05f, 0);
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new Exception("Failed to save " + path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        return $"Removed {removed} already-missing nested prefab placeholders from new run copies only.";
    }

    private static int RemoveMissingNestedPrefabs(GameObject root)
    {
        int removed = 0;
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child == null || child == root.transform) continue;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject) ||
                PrefabUtility.GetPrefabAssetType(child.gameObject) != PrefabAssetType.MissingAsset) continue;
            UnityEngine.Object.DestroyImmediate(child.gameObject);
            removed++;
        }
        return removed;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/');
        string parent = path.Substring(0, split);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(split + 1));
    }
}
