using System;
using System.Collections.Generic;
using System.Reflection;
using StudioBaekho;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class DungeonRewardChestRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string GeneratorPath = "Assets/_Game/ProceduralDungeon/DungeonGenerator.prefab";
    private const string CratePath = "Assets/LabRoom02V5/Prefabs/R2_09_low_wide_crate.prefab";
    private const string DoorPath = "Assets/LabDoor4x3/Prefabs/35_industrial_sliding_door.prefab";

    [MenuItem("Tools/Studio Baekho/Progression/Dungeon Reward Chest Regression")]
    private static void FromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        var generator = AssetDatabase.LoadAssetAtPath<GameObject>(GeneratorPath).GetComponent<DungeonEncounterDirector>();
        var crate = new SerializedObject(generator).FindProperty("rewardChestPrefab").objectReferenceValue;
        Check(crate == AssetDatabase.LoadAssetAtPath<GameObject>(CratePath), "The production generator is missing its reward crate.");
        var door = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPath);
        var threshold = door.transform.Find("Frame_Threshold");
        Check(threshold != null && threshold.gameObject.layer == LayerMask.NameToLayer("Ground")
            && threshold.GetComponent<BoxCollider>() is { enabled: true, isTrigger: false }, "The door threshold is not solid ground.");

        Scene previous = SceneManager.GetActiveScene();
        Scene fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        bool cursorVisible = Cursor.visible;
        var cursorLock = Cursor.lockState;
        int frameRate = Application.targetFrameRate;
        try
        {
            SceneManager.SetActiveScene(fixture);
            CheckRoom(generator, false, 250f);
            CheckRoom(generator, true, 300f);
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(fixture, true);
            Cursor.visible = cursorVisible;
            Cursor.lockState = cursorLock;
            Application.targetFrameRate = frameRate;
        }
        return "PASS: reward chest spawn, range, one use, choice delay, direct gold, shared F priority and door grounding.";
    }

    private static void CheckRoom(DungeonEncounterDirector generator, bool exhausted, float x)
    {
        var root = new GameObject("Chest regression fixture");
        root.transform.position = new Vector3(x, 0f, 250f);
        var data = ScriptableObject.CreateInstance<PlayerStatData>();
        try
        {
            var generation = Child(root.transform, "Generation");
            var room = Child(generation.transform, "Room").AddComponent<BaekhoMapModule>();
            room.localBounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(16f, 5f, 16f));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(room.transform, false);
            floor.transform.localPosition = Vector3.down * .25f;
            floor.transform.localScale = new Vector3(14f, .5f, 14f);
            floor.layer = LayerMask.NameToLayer("Ground");
            room.groundObjects = new[] { floor };
            var anchors = room.gameObject.AddComponent<StageMapAnchors>();
            anchors.teleportPoint = Child(room.transform, "TeleportPoint").transform;
            anchors.teleportPoint.localPosition = Vector3.up * .06f;
            var identity = room.gameObject.AddComponent<BaekhoGeneratedRoom>();
            identity.role = BaekhoRoomRole.Normal;
            var encounter = room.gameObject.AddComponent<DungeonRoomEncounter>();
            Property(encounter, "State", DungeonEncounterState.Fighting);
            Property(encounter, "WasEntered", true);
            var members = (List<DungeonEncounterMember>)encounter.GetType().GetField("_members", Hidden).GetValue(encounter);
            members.Add(Child(room.transform, "First enemy").AddComponent<DungeonEncounterMember>());
            members.Add(Child(room.transform, "Second enemy").AddComponent<DungeonEncounterMember>());

            var stage = Child(root.transform, "Stage").AddComponent<StageManager>();
            var stageResult = new BaekhoDungeonResult { root = generation, success = true };
            stageResult.rooms.Add(room);
            Property(stage, "Result", stageResult);
            Property(stage, "IsReady", true);
            Field(stage, "encounters", generator);

            var player = Child(root.transform, "Player").AddComponent<PlayerController>();
            player.gameObject.layer = LayerMask.NameToLayer("Player");
            var controller = player.GetComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = .35f;
            controller.center = Vector3.up * .9f;
            Property(player, "Controller", controller);
            Property(player, "Input", player.GetComponent<PlayerInputHandler>());
            Field(player, "_groundLayer", (LayerMask)LayerMask.GetMask("Ground"));
            var stats = player.GetComponent<PlayerStatManager>();
            data.baseHP = 100f;
            stats.Initialize(data);
            Property(player, "StatManager", stats);
            var items = player.GetComponent<PlayerItemRuntime>() ?? player.gameObject.AddComponent<PlayerItemRuntime>();
            items.Catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Generated/BoundaryInventory/ItemCatalog.asset");
            items.Initialize(stats, () => ElementType.ELEMENT_NONE);
            var wallet = player.GetComponent<PlayerWallet>() ?? player.gameObject.AddComponent<PlayerWallet>();
            wallet.Initialize(0);
            if (exhausted)
                foreach (var item in items.Catalog.items) Check(items.TryAddItem(item.itemId), "Failed to populate the exhausted item pool.");

            var choices = new RunItemChoiceService(items, wallet, EngravingSnapshot.Empty, 781);
            var run = Child(root.transform, "Run").AddComponent<RunManager>();
            run.player = player;
            run.stageManager = stage;
            Property(run, "State", RunState.Playing);
            Property(run, "ItemChoices", choices);
            Call(run, "BindUpgradeRooms");

            player.transform.position += Vector3.right * 10f;
            Physics.SyncTransforms();
            Check(DungeonEncounterPlacement.TryPrepareNavigation(room, out var navigation, out var error), error);
            Field(encounter, "_ownedNavigation", navigation);

            Call(run, "OnUpgradeCombatRoomCleared", encounter);
            Check(room.GetComponentInChildren<DungeonRewardChest>() == null, "A fighting room spawned a chest.");
            Property(encounter, "State", DungeonEncounterState.Cleared);
            Field(encounter, "_confirmedDefeats", 1);
            Call(run, "OnUpgradeCombatRoomCleared", encounter);
            Check(room.GetComponentInChildren<DungeonRewardChest>() == null, "A room cleared by despawn spawned a chest.");
            Field(encounter, "_confirmedDefeats", 2);
            Call(run, "OnUpgradeCombatRoomCleared", encounter);
            Call(run, "OnUpgradeCombatRoomCleared", encounter);
            var chests = room.GetComponentsInChildren<DungeonRewardChest>();
            Check(chests.Length == 1 && choices.Current == null && !run.HasPendingItemChoice && wallet.Gold == 0,
                "A clear did not reserve exactly one chest without blocking play or settling rewards.");
            var chest = chests[0];
            Check((chest.transform.position - room.transform.position).sqrMagnitude > 1f, "Chest did not leave the clear teleport landing free.");
            Check(chest.GetComponentInChildren<BoxCollider>() is { enabled: false }, "Reward crate blocks movement.");
            var prompt = chest.GetComponentInChildren<TMP_Text>(true);
            Check(prompt != null && prompt.textBounds.size.y * prompt.transform.lossyScale.y >= .24f,
                "The interaction label is too small in world space.");
            if (!exhausted) CheckPromptSize(chest, prompt, root.transform);
            Check(!(bool)Call(run, "TryOpenRewardChest", chest), "An interaction opened during materialization.");
            Call(chest, "AdvancePresentation", .25f);
            Check(!(bool)Call(run, "TryOpenRewardChest", chest), "An out-of-range interaction opened the chest.");

            player.transform.position = chest.transform.position + chest.transform.forward * 1.4f + Vector3.up * .02f;
            Physics.SyncTransforms();
            player.Input.ChainSkillBuffer.Set();
            Check((bool)Call(run, "TryOpenRewardChest", chest) && !player.Input.ChainSkillBuffer.IsActive,
                "A nearby interaction failed or leaked the shared F press into combat.");
            Call(run, "UpdateItemChoiceSession");
            Check(!run.IsRewardOpen, "A reward modal interrupted the chest animation.");
            Check(!(bool)Call(run, "TryOpenRewardChest", chest), "A second interaction consumed the same chest.");
            Check(exhausted ? wallet.Gold == 10 && choices.Current == null : wallet.Gold == 0 && choices.Current?.Candidates.Count == 3,
                "An opened chest did not settle its correct reward.");

            if (!exhausted)
            {
                var door = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DoorPath), root.scene);
                door.transform.SetParent(root.transform, false);
                door.transform.localPosition = Vector3.right * 20f;
                var threshold = door.transform.Find("Frame_Threshold").GetComponent<BoxCollider>();
                Physics.SyncTransforms();
                player.transform.position = threshold.bounds.center + Vector3.up * (threshold.bounds.extents.y + .025f);
                Physics.SyncTransforms();
                Check(player.IsGround(), "The player did not recognize the door threshold as ground.");
            }
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(data); }
    }

    private static GameObject Child(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static void CheckPromptSize(DungeonRewardChest chest, TMP_Text prompt, Transform parent)
    {
        var camera = Child(parent, "Prompt size camera").AddComponent<Camera>();
        camera.enabled = false;
        float baseScale = prompt.transform.localScale.y;
        var center = prompt.transform.position;
        camera.transform.position = center - Vector3.forward;
        Call(chest, "FacePrompt", camera);
        float glyphHeight = prompt.textBounds.size.y * prompt.transform.lossyScale.y;
        float viewportHeight = camera.WorldToViewportPoint(center + Vector3.up * glyphHeight).y
            - camera.WorldToViewportPoint(center).y;
        Check(viewportHeight is > .024f and < .026f && prompt.transform.localScale.y < baseScale,
            "A nearby camera did not cap the label to 2.5% of viewport height.");
        camera.transform.position = center - Vector3.forward * 25f;
        Call(chest, "FacePrompt", camera);
        Check(Mathf.Approximately(prompt.transform.localScale.y, baseScale), "The distant label did not restore its original size.");
    }

    private static void Property(object owner, string name, object value) => owner.GetType().GetProperty(name).SetValue(owner, value);
    private static void Field(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
    private static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Hidden).Invoke(owner, args);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
