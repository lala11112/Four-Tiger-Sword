using System;
using System.Linq;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Creates the authored tutorial layout and wires it to the shared game scene components.</summary>
public static class TutorialSetup
{
    public const string ScenePath = "Assets/_Project/Scenes/Tutorial Scene.unity";
    public const string DefinitionPath = "Assets/_Project/Resources/TutorialDefinition.asset";
    private const string GameScenePath = "Assets/_Project/Scenes/Game Scene.unity";
    private const string AssetFolder = "Assets/_Game/Tutorial";
    private const string RoomFolder = "Assets/ARPGMapRefresh/Prefabs/Rooms/";
    private const string ShopPath = AssetFolder + "/TutorialShop.prefab";
    private const int Seed = 12345;

    [MenuItem("Tools/Studio Baekho/Tutorial/Set Up Tutorial Scene")]
    private static void SetupMenu() => Debug.Log(Setup());

    public static string Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Set up the tutorial in Edit Mode.");
        if (!AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.CreateFolder("Assets/_Game", "Tutorial");

        var start = Room("start_7fc33406e6cede048a8e209145ed92c4");
        var training = Room("normal_a4352e64921d28748866d9168736c64e");
        var shop = GetOrCreateShop();
        var combat = Room("normal_4bd69833bd327fe40b3a8540e25bcb0f");
        var end = Room("end_fe6ecafa7f50c8a48922a2d0dac5e47e");
        var style = Load<BaekhoCorridorStyle>("Assets/ARPGMapRefresh/Profiles/V3CorridorStyle.asset");
        var enemy = Load<GameObject>("Assets/_Project/Prefabs/Chang.prefab");

        var settings = GetOrCreate<BaekhoDungeonSettings>(AssetFolder + "/TutorialDungeonSettings.asset");
        settings.startRoom = start;
        settings.shopRoom = shop;
        settings.endRoom = end;
        settings.style = style;
        settings.maps = new[] { training, combat };
        settings.mapCount = 5;
        settings.randomizeSeed = false;
        settings.seed = Seed;
        settings.corridorWidth = 4f;
        EditorUtility.SetDirty(settings);

        var layout = GetOrCreate<BaekhoFixedDungeonLayout>(AssetFolder + "/TutorialLayout.asset");
        layout.rooms = new[]
        {
            Place(start, BaekhoRoomRole.Start, 0, 0, null, "A", 2),
            Place(training, BaekhoRoomRole.Normal, -3, 30, "S1", "N1"),
            Place(shop, BaekhoRoomRole.Shop, -12, 60, "A", "B"),
            Place(combat, BaekhoRoomRole.Normal, -2, 90, "S1", "N1"),
            Place(end, BaekhoRoomRole.End, -12, 120, "A", null)
        };
        EditorUtility.SetDirty(layout);

        var run = GetOrCreate<RunDefinitionSO>(AssetFolder + "/TutorialRun.asset");
        run.mode = RunMode.Tutorial;
        run.chapters = new[] { new ChapterDefinition
        {
            displayName = "훈련장",
            stages = new[] { new StageDefinition
            {
                displayName = "훈련장",
                dungeonSettings = settings,
                fixedLayout = layout,
                mapCountVariation = 0,
                enemyCatalog = Load<DungeonEnemyCatalog>("Assets/_Game/ProceduralDungeon/DungeonEnemyCatalog.asset"),
                roomRosters = new[]
                {
                    new DungeonRoomRoster { roomId = 1, enemies = new[] { enemy } },
                    new DungeonRoomRoster { roomId = 3, enemies = new[] { enemy, enemy, enemy } }
                }
            } }
        } };
        EditorUtility.SetDirty(run);
        if (!run.Validate(out string error)) throw new InvalidOperationException(error);

        var tutorial = GetOrCreate<TutorialDefinitionSO>(DefinitionPath);
        tutorial.runDefinition = run;
        var checkpoint = combat.GetComponent<StageMapAnchors>().teleportPoint;
        tutorial.retrySpawnLocal = combat.transform.InverseTransformPoint(checkpoint.position);
        EditorUtility.SetDirty(tutorial);

        PrepareScene(tutorial, settings);
        EnableBuildScene();
        AssetDatabase.SaveAssets();
        return "Tutorial Scene prepared with five fixed rooms, dedicated encounters and shared game UI.";
    }

    private static void PrepareScene(TutorialDefinitionSO definition, BaekhoDungeonSettings settings)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !AssetDatabase.CopyAsset(GameScenePath, ScenePath))
            throw new InvalidOperationException("Could not create the tutorial scene from Game Scene.");
        var active = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var run = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RunManager>(true)).Single();
            run.definition = definition.runDefinition;
            run.randomizeSeed = false;
            run.seed = Seed;
            var generator = run.stageManager.Generator;
            generator.settings = settings;
            generator.generateOnStart = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(generator);
            var director = run.GetComponent<TutorialDirector>() ?? run.gameObject.AddComponent<TutorialDirector>();
            director.definition = definition;
            var upgradeBinding = run.player.GetComponent<PlayerUpgradeBinding>();
            if (upgradeBinding != null) Object.DestroyImmediate(upgradeBinding);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Tutorial Scene.");
        }
        finally
        {
            if (opened && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }

    private static void EnableBuildScene()
    {
        var scenes = EditorBuildSettings.scenes;
        int index = Array.FindIndex(scenes, scene => scene.path == ScenePath);
        if (index >= 0)
        {
            if (scenes[index].enabled) return;
            scenes[index] = new EditorBuildSettingsScene(ScenePath, true);
        }
        else
        {
            Array.Resize(ref scenes, scenes.Length + 1);
            scenes[^1] = new EditorBuildSettingsScene(ScenePath, true);
        }
        EditorBuildSettings.scenes = scenes;
    }

    private static BaekhoMapModule GetOrCreateShop()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ShopPath);
        if (existing == null && !AssetDatabase.CopyAsset(RoomFolder + "shop_7114c457d5e47474aafbf442eca44fd9.prefab", ShopPath))
            throw new InvalidOperationException("Could not copy the tutorial shop.");
        var root = PrefabUtility.LoadPrefabContents(ShopPath);
        try
        {
            var module = root.GetComponent<BaekhoMapModule>();
            bool addedDoor = !module.connections.Any(connection => connection.id == "B");
            if (addedDoor) AddNorthDoor(module);
            bool addedFloor = EnsureNorthDoorFloor(module);
            if ((addedDoor || addedFloor) && PrefabUtility.SaveAsPrefabAsset(root, ShopPath) == null)
                throw new InvalidOperationException("Could not save the two-door tutorial shop.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return Load<GameObject>(ShopPath).GetComponent<BaekhoMapModule>();
    }

    private static void AddNorthDoor(BaekhoMapModule module)
    {
        module.gameObject.name = "TutorialShop";
        var south = module.connections.Single(connection => connection.id == "A");
        var transforms = module.GetComponentsInChildren<Transform>(true);
        // Open two wall bays. Keep the same lintel, opening height and wall depth as the south entrance.
        foreach (string name in new[] { "wall_wall-6-0", "wall_wall-7-0" })
            transforms.Single(child => child.name == name).gameObject.SetActive(false);
        var north = new GameObject("B").AddComponent<BaekhoMapConnection>();
        north.transform.SetParent(south.transform.parent, false);
        north.transform.localPosition = new Vector3(4, 0, 10);
        north.id = "B";
        north.label = "다음 전투방";
        north.kind = south.kind;
        north.width = south.width;
        north.height = south.height;

        var lintels = new GameObject[2];
        for (int i = 0; i < lintels.Length; i++)
        {
            var source = transforms.Single(child => child.name == $"wall_fill-doorTop-A-{i}");
            lintels[i] = CopyOntoNorthSide(module.transform, source, $"wall_fill-doorTop-B-{i}", flip: true);
        }
        var wall = south.closedWall;
        north.closedWall = new BaekhoClosedWall
        {
            template = wall.template, templateLocalBounds = wall.templateLocalBounds,
            localCenter = new Vector3(wall.localCenter.x, wall.localCenter.y, -wall.localCenter.z),
            localRotation = wall.localRotation, size = wall.size, replaceWhenClosed = lintels
        };
        module.connections = new[] { south, north };
    }

    private static bool EnsureNorthDoorFloor(BaekhoMapModule module)
    {
        bool changed = false;
        var transforms = module.GetComponentsInChildren<Transform>(true);
        var ground = new System.Collections.Generic.List<GameObject>(module.groundObjects);
        // The original north wall cells contained no floor. Fill the two-cell doorway recess
        // with the existing south entrance tile, so the room reaches the corridor socket.
        for (int column = 6; column <= 7; column++)
        {
            string name = $"floor_floor-{column}-0";
            var floor = transforms.SingleOrDefault(child => child.name == name)?.gameObject;
            if (floor == null)
            {
                var source = transforms.Single(child => child.name == $"floor_floor-{column}-9");
                floor = CopyOntoNorthSide(module.transform, source, name, flip: false);
                changed = true;
            }
            if (!ground.Contains(floor)) { ground.Add(floor); changed = true; }
        }
        if (changed) module.groundObjects = ground.ToArray();
        return changed;
    }

    private static GameObject CopyOntoNorthSide(Transform room, Transform source, string name, bool flip)
    {
        var copy = Object.Instantiate(source.gameObject, source.parent);
        copy.name = name;
        var local = room.InverseTransformPoint(source.position);
        local.z = -local.z;
        var rotation = flip ? Quaternion.AngleAxis(180f, room.up) * source.rotation : source.rotation;
        copy.transform.SetPositionAndRotation(room.TransformPoint(local), rotation);
        return copy;
    }

    private static BaekhoFixedDungeonLayout.Room Place(BaekhoMapModule module, BaekhoRoomRole role,
        float x, float z, string entrance, string exit, int quarterTurns = 0)
        => new() { module = module, role = role, position = new Vector3(x, 0f, z),
            quarterTurns = quarterTurns, entranceSocket = entrance, exitSocket = exit };

    private static BaekhoMapModule Room(string stem) => Load<GameObject>(RoomFolder + stem + ".prefab").GetComponent<BaekhoMapModule>();

    private static T Load<T>(string path) where T : Object
        => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing asset: " + path);

    private static T GetOrCreate<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }
}
