using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Copies the authored gameplay character and camera into the existing Hub world.</summary>
public static class HubWorldSetup
{
    private const string HubPath = "Assets/_Project/Scenes/Hub Scene.unity";
    private const string GamePath = "Assets/_Project/Scenes/Game Scene.unity";
    private const string HubModelPath = "Assets/EasternLab_ModelOnly/Models/EasternLab.fbx";
    private const string UndoName = "Set Up Hub World Player";

    [MenuItem("Tools/Studio Baekho/Progression/Set Up Hub World Player")]
    private static void SetupMenu() => Debug.Log(Setup());

    public static string Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Set up the Hub world in Edit Mode.");

        var hub = SceneManager.GetSceneByPath(HubPath);
        if (!hub.isLoaded) hub = EditorSceneManager.OpenScene(HubPath, OpenSceneMode.Additive);
        var game = SceneManager.GetSceneByPath(GamePath);
        bool openedGame = !game.isLoaded;
        if (openedGame) game = EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Additive);

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        try
        {
            var sourcePlayer = Components<PlayerController>(game).Single();
            var sourceVirtualCamera = Components<CinemachineCamera>(game)
                .Single(camera => camera.gameObject.name == "PlayerCam");
            var sourceCamera = Components<Camera>(game).Single(camera => camera.CompareTag("MainCamera"));
            var sourceBrain = sourceCamera.GetComponent<CinemachineBrain>();
            if (sourceBrain == null) throw new InvalidOperationException("The gameplay MainCamera has no CinemachineBrain.");
            var mainCamera = Components<Camera>(hub).Single(camera => camera.CompareTag("MainCamera"));
            var map = hub.GetRootGameObjects().Single(root => root.name == "EasternLab_Model");
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer < 0) throw new InvalidOperationException("The Ground layer is missing.");

            var world = hub.GetRootGameObjects().FirstOrDefault(root => root.name == "HubWorld");
            if (world == null)
            {
                var previousActive = SceneManager.GetActiveScene();
                try
                {
                    SceneManager.SetActiveScene(hub);
                    world = new GameObject("HubWorld");
                }
                finally
                {
                    if (previousActive.IsValid() && previousActive.isLoaded)
                        SceneManager.SetActiveScene(previousActive);
                }
                Undo.RegisterCreatedObjectUndo(world, UndoName);
            }
            if (world.GetComponent<HubWorldController>() == null) Undo.AddComponent<HubWorldController>(world);
            if (!world.activeSelf)
            {
                Undo.RecordObject(world, UndoName);
                world.SetActive(true);
            }

            var player = Components<PlayerController>(hub).SingleOrDefault();
            if (player == null)
                player = CloneInto(sourcePlayer.gameObject, hub, world.transform).GetComponent<PlayerController>();
            var virtualCamera = Components<CinemachineCamera>(hub).SingleOrDefault();
            if (virtualCamera == null)
                virtualCamera = CloneInto(sourceVirtualCamera.gameObject, hub, world.transform).GetComponent<CinemachineCamera>();

            var references = new Dictionary<Object, Object>();
            MapHierarchy(sourcePlayer.transform, player.transform, references);
            MapHierarchy(sourceVirtualCamera.transform, virtualCamera.transform, references);
            references[sourceCamera.gameObject] = mainCamera.gameObject;
            references[sourceCamera.transform] = mainCamera.transform;
            references[sourceCamera] = mainCamera;

            Undo.RecordObject(mainCamera, UndoName);
            EditorUtility.CopySerialized(sourceCamera, mainCamera);
            mainCamera.enabled = true;
            var brain = mainCamera.GetComponent<CinemachineBrain>();
            if (brain == null) brain = Undo.AddComponent<CinemachineBrain>(mainCamera.gameObject);
            CopyComponent(sourceBrain, brain, references);
            foreach (var extra in mainCamera.GetComponentsInChildren<CinemachineBrain>(true))
                if (extra != brain) Undo.DestroyObjectImmediate(extra);
            CopyOptionalCameraComponent<CameraMove>(sourceCamera, mainCamera, references);
            CopyOptionalCameraComponent<CinemachineImpulseListener>(sourceCamera, mainCamera, references);

            RestoreMappedReferences(references);
            RemapReferences(player.gameObject, hub, references);
            RemapReferences(virtualCamera.gameObject, hub, references);
            RemapReferences(mainCamera.gameObject, hub, references);
            Undo.RecordObject(player, UndoName);
            player.CameraTransform = mainCamera.transform;
            player.VerticalVelocity = 0f;
            EditorUtility.SetDirty(player);
            RecordPrefabChange(player);
            var input = new SerializedObject(player.GetComponent<PlayerInputHandler>());
            input.FindProperty("_inputMode").enumValueIndex = (int)PlayerInputHandler.InputMode.LocomotionOnly;
            input.ApplyModifiedProperties();
            RecordPrefabChange(input.targetObject);

            int colliderCount = AddWorldColliders(map, groundLayer);
            PlaceOnFloor(player, map, groundLayer);
            // Start the virtual and output cameras in the same relative position as the gameplay rig.
            Vector3 cameraOffset = sourceVirtualCamera.transform.position - sourcePlayer.transform.position;
            Undo.RecordObject(virtualCamera.transform, UndoName);
            virtualCamera.transform.SetPositionAndRotation(player.transform.position + cameraOffset,
                sourceVirtualCamera.transform.rotation);
            Undo.RecordObject(mainCamera.transform, UndoName);
            mainCamera.transform.SetPositionAndRotation(virtualCamera.transform.position, virtualCamera.transform.rotation);
            EditorUtility.SetDirty(mainCamera);
            EditorUtility.SetDirty(mainCamera.transform);
            EditorUtility.SetDirty(virtualCamera.transform);

            string validation = Validate();
            EditorSceneManager.MarkSceneDirty(hub);
            if (!EditorSceneManager.SaveScene(hub)) throw new InvalidOperationException("Could not save the Hub world.");
            Undo.CollapseUndoOperations(undoGroup);
            return $"Hub world configured with {colliderCount} static mesh colliders. {validation}";
        }
        catch
        {
            Undo.RevertAllDownToGroup(undoGroup);
            throw;
        }
        finally
        {
            // The source scene is never saved or explicitly modified by this setup.
            if (openedGame && game.isLoaded) EditorSceneManager.CloseScene(game, true);
        }
    }

    public static string Validate()
    {
        var hub = SceneManager.GetSceneByPath(HubPath);
        if (!hub.isLoaded) throw new InvalidOperationException("Load the Hub Scene before validating it.");
        var player = Components<PlayerController>(hub).Single();
        var camera = Components<CinemachineCamera>(hub).Single();
        var mainCamera = Components<Camera>(hub).Single(value => value.CompareTag("MainCamera"));
        var brains = Components<CinemachineBrain>(hub).ToArray();
        if (brains.Length != 1 || brains[0].gameObject != mainCamera.gameObject)
            throw new InvalidOperationException("The Hub must have one CinemachineBrain on its Main Camera.");
        if (!player.gameObject.activeInHierarchy || !camera.gameObject.activeInHierarchy
            || !player.enabled || !camera.enabled || !mainCamera.enabled)
            throw new InvalidOperationException("The Hub player and cameras must be active.");
        if (player.CameraTransform != mainCamera.transform || camera.Follow == null
            || !camera.Follow.IsChildOf(player.transform))
            throw new InvalidOperationException("The Hub camera is not following its own player.");
        if (player.GetComponent<PlayerInputHandler>().Mode != PlayerInputHandler.InputMode.LocomotionOnly)
            throw new InvalidOperationException("The Hub player must use locomotion-only input.");
        if (Components<PlayerHudPresenter>(hub).Any() || Components<PlayerUIManager>(hub).Any())
            throw new InvalidOperationException("The Hub must not contain the gameplay player's HUD.");
        foreach (var root in hub.GetRootGameObjects()) ValidateReferences(root, hub);
        return "PASS: one local player, camera and brain; locomotion only; no player HUD or cross-scene references.";
    }

    private static IEnumerable<T> Components<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

    private static GameObject CloneInto(GameObject source, Scene scene, Transform parent)
    {
        // Native Editor copy/paste retains nested prefab instances and their authored overrides.
        // Object.Instantiate would unpack those links when cloning a scene-authored root.
        var previousSelection = Selection.objects;
        var previousActive = SceneManager.GetActiveScene();
        var existing = new HashSet<int>(Components<Transform>(scene).Select(value => value.GetInstanceID()));
        try
        {
            Selection.objects = new Object[] { source };
            Unsupported.CopyGameObjectsToPasteboard();
            SceneManager.SetActiveScene(scene);
            Selection.objects = Array.Empty<Object>();
            Unsupported.PasteGameObjectsFromPasteboard();
            var copiedRoots = Components<Transform>(scene)
                .Where(value => !existing.Contains(value.GetInstanceID())
                    && (value.parent == null || existing.Contains(value.parent.GetInstanceID())))
                .ToArray();
            if (copiedRoots.Length != 1)
                throw new InvalidOperationException($"Pasting {source.name} did not produce exactly one Hub object.");
            var clone = copiedRoots[0].gameObject;
            Undo.RecordObject(clone, UndoName);
            clone.name = source.name;
            Undo.SetTransformParent(clone.transform, parent, UndoName);
            return clone;
        }
        finally
        {
            Selection.objects = previousSelection.Where(value => value != null).ToArray();
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
        }
    }

    private static void MapHierarchy(Transform source, Transform destination, Dictionary<Object, Object> references)
    {
        var sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(source);
        if (sourcePrefab != null && PrefabUtility.GetCorrespondingObjectFromSource(destination) != sourcePrefab)
            throw new InvalidOperationException($"Copying {source.name} did not preserve its nested prefab connection.");
        references[source.gameObject] = destination.gameObject;
        references[source] = destination;
        foreach (var sourceComponent in source.GetComponents<Component>())
        {
            if (sourceComponent == null) continue;
            var type = sourceComponent.GetType();
            var peers = source.GetComponents(type);
            int index = Array.IndexOf(peers, sourceComponent);
            var targets = destination.GetComponents(type);
            if (index >= targets.Length)
                throw new InvalidOperationException($"Hub {destination.name} is missing the copied {type.Name} component.");
            references[sourceComponent] = targets[index];
        }
        if (destination.childCount < source.childCount)
            throw new InvalidOperationException($"Hub {destination.name} has an incomplete player or camera hierarchy.");
        for (int i = 0; i < source.childCount; i++)
        {
            var child = destination.GetChild(i);
            if (child.name != source.GetChild(i).name)
                throw new InvalidOperationException($"Hub {destination.name} has a different copied hierarchy at child {i}.");
            MapHierarchy(source.GetChild(i), child, references);
        }
    }

    private static void CopyComponent(Component source, Component destination, Dictionary<Object, Object> references)
    {
        Undo.RecordObject(destination, UndoName);
        EditorUtility.CopySerialized(source, destination);
        references[source] = destination;
        EditorUtility.SetDirty(destination);
        RecordPrefabChange(destination);
    }

    private static void CopyOptionalCameraComponent<T>(Camera source, Camera destination,
        Dictionary<Object, Object> references) where T : Component
    {
        var sourceComponent = source.GetComponent<T>();
        if (sourceComponent == null) return;
        var target = destination.GetComponent<T>();
        if (target == null) target = Undo.AddComponent<T>(destination.gameObject);
        CopyComponent(sourceComponent, target, references);
    }

    private static void RemapReferences(GameObject root, Scene scene, Dictionary<Object, Object> references)
    {
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null) throw new InvalidOperationException($"Missing script in copied {root.name} hierarchy.");
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                var value = property.objectReferenceValue;
                if (value != null && references.TryGetValue(value, out var replacement))
                    property.objectReferenceValue = replacement;
            }
            if (serialized.ApplyModifiedProperties()) RecordPrefabChange(component);
        }
        ValidateReferences(root, scene);
    }

    private static void RestoreMappedReferences(Dictionary<Object, Object> references)
    {
        // Pasting separately copied roots into another scene can clear references between them.
        // Rebuild those references from the source after both hierarchies have been mapped.
        foreach (var pair in references)
        {
            if (!(pair.Key is Component source) || !(pair.Value is Component destination)) continue;
            var sourceData = new SerializedObject(source);
            var targetData = new SerializedObject(destination);
            var property = sourceData.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference
                    || property.propertyPath == "m_GameObject" || property.propertyPath == "m_Father"
                    || property.propertyPath.StartsWith("m_Children", StringComparison.Ordinal)) continue;
                var value = property.objectReferenceValue;
                if (value == null || EditorUtility.IsPersistent(value)) continue;
                var targetProperty = targetData.FindProperty(property.propertyPath);
                if (references.TryGetValue(value, out var replacement))
                {
                    if (targetProperty != null) targetProperty.objectReferenceValue = replacement;
                }
                else if (value is GameObject || value is Component)
                    throw new InvalidOperationException($"The copied {source.name}.{property.propertyPath} needs an unmapped scene object.");
            }
            if (targetData.ApplyModifiedProperties()) RecordPrefabChange(destination);
        }
    }

    private static void ValidateReferences(GameObject root, Scene scene)
    {
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null) throw new InvalidOperationException($"Missing script on {root.name}.");
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                var value = property.objectReferenceValue;
                if (value == null || EditorUtility.IsPersistent(value)) continue;
                GameObject referenced = value as GameObject;
                if (value is Component target) referenced = target.gameObject;
                if (referenced != null && referenced.scene.IsValid() && referenced.scene != scene)
                    throw new InvalidOperationException($"{component.name}.{property.propertyPath} still references scene {referenced.scene.name}.");
            }
        }
    }

    private static int AddWorldColliders(GameObject map, int groundLayer)
    {
        var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(HubModelPath);
        var sourceMeshes = sourceModel?.GetComponentsInChildren<MeshFilter>(true)
            .Where(filter => filter.sharedMesh != null)
            .ToDictionary(filter => filter.name, filter => filter.sharedMesh, StringComparer.Ordinal);

        int count = 0;
        foreach (var filter in map.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.name == "RecessedGuides"
                || filter.GetComponentInParent<EasternLab.GateAddon.GateAddonSettings>(true) != null) continue;
            var collider = filter.GetComponent<MeshCollider>();
            if (collider == null) collider = Undo.AddComponent<MeshCollider>(filter.gameObject);
            Undo.RecordObject(collider, UndoName);
            // Architecture can use repaired render meshes while keeping its original collision surfaces.
            if (sourceMeshes != null && sourceMeshes.TryGetValue(filter.name, out var sourceMesh))
            {
                if (collider.sharedMesh == null) collider.sharedMesh = sourceMesh;
            }
            else collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
            collider.isTrigger = false;
            collider.enabled = true;
            Undo.RecordObject(filter.gameObject, UndoName);
            filter.gameObject.layer = groundLayer;
            EditorUtility.SetDirty(collider);
            EditorUtility.SetDirty(filter.gameObject);
            RecordPrefabChange(collider);
            RecordPrefabChange(filter.gameObject);
            count++;
        }
        return count;
    }

    private static void PlaceOnFloor(PlayerController player, GameObject map, int groundLayer)
    {
        Physics.SyncTransforms();
        var floor = map.GetComponentsInChildren<MeshCollider>(true).Single(collider => collider.name == "00_Floor");
        Vector3 center = floor.bounds.center;
        var ray = new Ray(new Vector3(center.x, floor.bounds.max.y + 5f, center.z), Vector3.down);
        if (!floor.Raycast(ray, out var hit, 10f))
            throw new InvalidOperationException("The center of the Hub floor is not a valid spawn surface.");
        var controller = player.GetComponent<CharacterController>();
        Undo.RecordObject(player.transform, UndoName);
        Undo.RecordObject(controller, UndoName);
        bool wasEnabled = controller.enabled;
        controller.enabled = false;
        player.transform.rotation = Quaternion.identity;
        float halfHeight = controller.height * Mathf.Abs(player.transform.lossyScale.y) * .5f;
        float centerOffset = player.transform.TransformVector(controller.center).y;
        float clearance = Mathf.Clamp(controller.skinWidth * .25f, .005f, .04f);
        player.transform.position = hit.point + Vector3.up * (halfHeight - centerOffset + clearance);
        controller.enabled = wasEnabled;
        Physics.SyncTransforms();
        Vector3 capsuleCenter = player.transform.TransformPoint(controller.center);
        float radius = controller.radius * Mathf.Max(Mathf.Abs(player.transform.lossyScale.x), Mathf.Abs(player.transform.lossyScale.z));
        float segment = Mathf.Max(0f, halfHeight - radius);
        var overlaps = Physics.OverlapCapsule(capsuleCenter + Vector3.up * segment,
            capsuleCenter - Vector3.up * segment, radius * .95f, 1 << groundLayer, QueryTriggerInteraction.Ignore);
        if (overlaps.Any(collider => collider.gameObject.scene == map.scene))
            throw new InvalidOperationException("The Hub player spawn overlaps the world geometry.");
        EditorUtility.SetDirty(player.transform);
        RecordPrefabChange(player.transform);
    }

    private static void RecordPrefabChange(Object value)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
    }
}
