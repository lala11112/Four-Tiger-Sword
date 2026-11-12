using System;
using System.Collections.Generic;
using System.Linq;
using EasternLab.GateAddon;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Builds the standalone stage portal from the Hub's authored gate, then connects the run scene.</summary>
public static class StageExitPortalSetup
{
    public const string PrefabPath = "Assets/_Game/RunProgression/Prefabs/StageExitPortal.prefab";
    private const string ModelPath = "Assets/EasternLab_ModelOnly/EasternLab_Model.prefab";
    private const string AddonPath = "Assets/EasternLab_GateAddon/WarpGate_Addon.prefab";
    private const string GameScenePath = "Assets/_Project/Scenes/Game Scene.unity";
    private static readonly Vector3 ExitExclusionSize = new(6.4f, 4.6f, 4f);

    [MenuItem("Tools/Studio Baekho/Dungeon/Set Up Stage Exit Portal")]
    private static void SetupMenu() => Debug.Log(Setup());

    public static string Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Set up the stage portal in Edit Mode.");
        var portal = BuildPrefab();
        var gameScene = SceneManager.GetSceneByPath(GameScenePath);
        bool openedScene = !gameScene.isLoaded;
        if (openedScene) gameScene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);
        try
        {
            var stage = Components<StageManager>(gameScene).Single();
            var run = Components<RunManager>(gameScene).Single();
            var player = Components<PlayerController>(gameScene).Single().GetComponent<CharacterController>();
            if (run.definition == null || player == null)
                throw new InvalidOperationException("Game Scene needs a run definition and player capsule.");
            var rooms = run.definition.chapters.SelectMany(chapter => chapter.stages)
                .Select(definition => definition.dungeonSettings.endRoom).Distinct().ToArray();
            foreach (var room in rooms) PrepareEndRoom(AssetDatabase.GetAssetPath(room), portal, player);
            Undo.RecordObject(stage, "Connect Stage Exit Portal");
            stage.ConfigureExitPrefab(portal);
            EditorUtility.SetDirty(stage);
            PrefabUtility.RecordPrefabInstancePropertyModifications(stage);
            EditorSceneManager.MarkSceneDirty(gameScene);
            if (!EditorSceneManager.SaveScene(gameScene))
                throw new InvalidOperationException("Could not save the stage portal connection.");
            AssetDatabase.SaveAssets();
            return "PASS: standalone Hub gate portal connected; " + rooms.Length
                + " configured end-room prefab(s) retain their exit pose with expanded exclusion and a validated safe landing.";
        }
        finally
        {
            if (openedScene && gameScene.isLoaded) EditorSceneManager.CloseScene(gameScene, true);
        }
    }

    public static StageExit BuildPrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build the stage portal in Edit Mode.");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var addonAsset = AssetDatabase.LoadAssetAtPath<GameObject>(AddonPath);
        if (model == null || addonAsset == null) throw new InvalidOperationException("The Hub gate source assets are missing.");
        var sourceGate = addonAsset.GetComponentInChildren<GateTransit>(true).transform;
        var rotation = Quaternion.Inverse(sourceGate.rotation);
        var floor = new Vector3(sourceGate.position.x, 0f, sourceGate.position.z);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("StageExitPortal");
            SceneManager.MoveGameObjectToScene(root, scene);
            var stageExit = root.AddComponent<StageExit>();
            var frame = CloneRebased(model.transform.Find("Architecture/03_DataGate"), root.transform, floor, rotation);
            var throat = CloneRebased(model.transform.Find("GateHardware/InnerFrame"), root.transform, floor, rotation);
            var addon = CloneRebased(addonAsset.transform, root.transform, floor, rotation);
            var settings = addon.GetComponent<GateAddonSettings>();
            if (settings != null) Object.DestroyImmediate(settings);
            foreach (var panel in addon.GetComponentsInChildren<GateAccessPanel>(true))
                Object.DestroyImmediate(panel.gameObject);
            var transit = addon.GetComponentInChildren<GateTransit>(true);
            var activation = addon.GetComponentInChildren<GateActivation>(true);
            var view = addon.GetComponentInChildren<SpatialPortalView>(true);
            transit.targetScene = string.Empty;
            transit.demoExit = null;
            transit.portalExitFrame = null;
            transit.eventOnlyTraversal = true;
            transit.externalSceneLoading = false;
            transit.playConversionEffect = false;
            transit.playerRoot = null;
            transit.playerCamera = null;
            transit.destinationLabel = "다음 스테이지";
            view.playerCamera = null;
            var frameRenderer = frame.GetComponent<MeshRenderer>();
            activation.statusRenderer = frameRenderer;
            activation.statusMaterialIndex = Array.FindIndex(frameRenderer.sharedMaterials,
                material => material != null && material.name == "V9_UI_gate_status");
            if (activation.statusMaterialIndex < 0)
                throw new InvalidOperationException("The Hub gate status material slot is missing.");
            AddMeshCollider(frame.GetComponent<MeshFilter>());
            AddMeshCollider(throat.Find("Bore").GetComponent<MeshFilter>());
            stageExit.ConfigurePortal(activation, transit, view);
            EnsureFolder("Assets/_Game/RunProgression/Prefabs");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Could not save the stage portal prefab.");
            return prefab.GetComponent<StageExit>();
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static Transform CloneRebased(Transform source, Transform parent, Vector3 floor, Quaternion rotation)
    {
        if (source == null) throw new InvalidOperationException("The Hub gate's frame hierarchy is incomplete.");
        var clone = Object.Instantiate(source.gameObject, parent).transform;
        clone.name = source.name;
        clone.localPosition = rotation * (source.position - floor);
        clone.localRotation = rotation * source.rotation;
        clone.localScale = source.lossyScale;
        return clone;
    }

    private static void AddMeshCollider(MeshFilter filter)
    {
        if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException("Gate collision mesh is missing.");
        var collider = filter.GetComponent<MeshCollider>();
        if (collider == null) collider = filter.gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = filter.sharedMesh;
        collider.convex = false;
    }

    private static void PrepareEndRoom(string path, StageExit portal, CharacterController sourceCapsule)
    {
        if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("A configured end-room prefab is missing.");
        var root = PrefabUtility.LoadPrefabContents(path);
        GameObject portalPreview = null, capsuleObject = null;
        try
        {
            var room = root.GetComponent<BaekhoMapModule>();
            var anchors = root.GetComponent<StageMapAnchors>();
            if (room == null || anchors == null || anchors.exitPoint == null)
                throw new InvalidOperationException("End room needs an exit anchor: " + path);
            if (anchors.teleportPoint == null)
            {
                var landing = root.transform.Find("TeleportPoint");
                if (landing == null)
                {
                    landing = new GameObject("TeleportPoint").transform;
                    landing.SetParent(root.transform, false);
                }
                anchors.teleportPoint = landing;
            }
            anchors.exitSize = ExitExclusionSize;
            portalPreview = Object.Instantiate(portal.gameObject);
            SceneManager.MoveGameObjectToScene(portalPreview, root.scene);
            portalPreview.transform.SetPositionAndRotation(anchors.exitPoint.position, anchors.exitPoint.rotation);
            capsuleObject = new GameObject("Stage portal landing capsule");
            SceneManager.MoveGameObjectToScene(capsuleObject, root.scene);
            capsuleObject.transform.localScale = sourceCapsule.transform.lossyScale;
            capsuleObject.transform.position = new Vector3(10000f, 10000f, 10000f);
            var capsule = capsuleObject.AddComponent<CharacterController>();
            capsule.height = sourceCapsule.height;
            capsule.radius = sourceCapsule.radius;
            capsule.center = sourceCapsule.center;
            capsule.slopeLimit = sourceCapsule.slopeLimit;
            capsule.skinWidth = sourceCapsule.skinWidth;
            capsule.stepOffset = sourceCapsule.stepOffset;
            Physics.SyncTransforms();
            if (!DungeonFastTravelLanding.TryResolve(room, anchors.teleportPoint, capsule, out _, out _, out _))
                FindLanding(room, anchors, capsule);
            Object.DestroyImmediate(portalPreview);
            portalPreview = null;
            Object.DestroyImmediate(capsuleObject);
            capsuleObject = null;
            if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                throw new InvalidOperationException("Could not save portal-safe end room: " + path);
        }
        finally
        {
            if (portalPreview != null) Object.DestroyImmediate(portalPreview);
            if (capsuleObject != null) Object.DestroyImmediate(capsuleObject);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void FindLanding(BaekhoMapModule room, StageMapAnchors anchors, CharacterController capsule)
    {
        var desired = anchors.exitPoint.position + anchors.exitPoint.forward * 4f;
        var bounds = room.localBounds;
        var candidates = new List<Vector3>();
        for (float x = Mathf.Ceil(bounds.min.x * 2f) * .5f; x <= bounds.max.x; x += .5f)
            for (float z = Mathf.Ceil(bounds.min.z * 2f) * .5f; z <= bounds.max.z; z += .5f)
                candidates.Add(room.transform.TransformPoint(new Vector3(x, bounds.max.y + 2f, z)));
        var grounds = DungeonFastTravelLanding.GroundRoots(room)
            .SelectMany(ground => ground.GetComponentsInChildren<Collider>(true)).Where(value => value.enabled && !value.isTrigger).ToArray();
        string reason = null;
        foreach (var candidate in candidates.OrderBy(value => new Vector2(value.x - desired.x, value.z - desired.z).sqrMagnitude))
        {
            var ray = new Ray(candidate, Vector3.down);
            bool found = false;
            RaycastHit floor = default;
            foreach (var ground in grounds)
                if (ground.Raycast(ray, out var hit, bounds.size.y + 8f) && (!found || hit.distance < floor.distance))
                { found = true; floor = hit; }
            if (!found) continue;
            var direction = Vector3.ProjectOnPlane(anchors.exitPoint.position - floor.point, Vector3.up);
            if (direction.sqrMagnitude < .01f) continue;
            var facing = Quaternion.LookRotation(direction, Vector3.up);
            anchors.teleportPoint.SetPositionAndRotation(DungeonFastTravelLanding.PlayerRootAtFloor(floor.point, facing, capsule), facing);
            Physics.SyncTransforms();
            if (DungeonFastTravelLanding.TryResolve(room, anchors.teleportPoint, capsule, out _, out _, out reason)) return;
        }
        throw new InvalidOperationException("No safe landing outside the stage portal was found: " + reason);
    }

    private static IEnumerable<T> Components<T>(Scene scene) where T : Component
        => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

    private static void EnsureFolder(string path)
    {
        string parent = "Assets";
        foreach (string folder in path.Split('/').Skip(1))
        {
            string next = parent + "/" + folder;
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, folder);
            parent = next;
        }
    }
}
