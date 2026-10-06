using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AssetInventory.Automation
{
    internal static class SceneAutomation
    {
        internal sealed class AddToSceneRequest
        {
            public string ProjectPath { get; set; }
            public float? PositionX { get; set; }
            public float? PositionY { get; set; }
            public float? PositionZ { get; set; }
            public string ParentGameObject { get; set; }
            public bool DryRun { get; set; } = true;
            public string ConfirmationToken { get; set; }
        }

        internal static AutomationResponse AddToScene(AddToSceneRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ProjectPath))
            {
                return AutomationResponse.Error("ProjectPath is required.", errorCode: "invalid_input");
            }

            if (!AutomationInputValidator.TryNormalizeAssetsPath(request.ProjectPath, out string projectPath) || string.Equals(projectPath, "Assets", System.StringComparison.Ordinal))
            {
                return AutomationResponse.Error("ProjectPath must be a normalized project-relative asset path under Assets and cannot contain path traversal.", errorCode: "invalid_input");
            }
            if ((request.PositionX.HasValue && (float.IsNaN(request.PositionX.Value) || float.IsInfinity(request.PositionX.Value)))
                || (request.PositionY.HasValue && (float.IsNaN(request.PositionY.Value) || float.IsInfinity(request.PositionY.Value)))
                || (request.PositionZ.HasValue && (float.IsNaN(request.PositionZ.Value) || float.IsInfinity(request.PositionZ.Value))))
            {
                return AutomationResponse.Error("Position values must be finite numbers.", errorCode: "invalid_input");
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(projectPath);
            if (prefab == null)
            {
                return AutomationResponse.Error($"Could not load prefab or model at '{projectPath}'. Ensure the file exists and is a prefab or model.", errorCode: "not_found");
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                return AutomationResponse.Error("There is no valid loaded active scene.", errorCode: "project_state_invalid");
            }
            Transform parentTransform = null;
            string parentPath = null;
            if (!string.IsNullOrWhiteSpace(request.ParentGameObject))
            {
                List<Transform> matches = FindSceneObjectsByName(activeScene, request.ParentGameObject);
                if (matches.Count == 0)
                {
                    return AutomationResponse.Error($"Parent GameObject '{request.ParentGameObject}' was not found in the active scene.", errorCode: "not_found");
                }
                if (matches.Count > 1)
                {
                    return AutomationResponse.Error($"Parent GameObject '{request.ParentGameObject}' is ambiguous in the active scene. Use a unique GameObject name.", new {matches = matches.Select(GetHierarchyPath).ToArray()}, "ambiguous_target");
                }
                parentTransform = matches[0];
                parentPath = GetHierarchyPath(parentTransform);
            }

            bool hasPosition = request.PositionX.HasValue || request.PositionY.HasValue || request.PositionZ.HasValue;
            Vector3 position;
            if (parentTransform != null && !hasPosition)
            {
                position = Vector3.zero;
            }
            else if (hasPosition)
            {
                position = new Vector3(request.PositionX ?? 0f, request.PositionY ?? 0f, request.PositionZ ?? 0f);
            }
            else
            {
                SceneView sceneView = SceneView.lastActiveSceneView;
                position = sceneView != null ? sceneView.pivot : Vector3.zero;
            }

            string sceneIdentity = string.IsNullOrEmpty(activeScene.path) ? activeScene.name : activeScene.path;
            string prefabGuid = AssetDatabase.AssetPathToGUID(projectPath);
            int parentInstanceId = parentTransform != null ? parentTransform.GetStableId() : 0;
            AutomationResponse confirmation = AutomationMutationGuard.RequireConfirmation(
                "add_to_scene",
                request.DryRun,
                request.ConfirmationToken,
                new {projectPath, prefabGuid, prefabName = prefab.name, scene = sceneIdentity, parentPath, position = new {x = position.x, y = position.y, z = position.z}},
                projectPath, prefabGuid, activeScene.handle.ToString(), sceneIdentity, parentPath, parentInstanceId.ToString(), position.x.ToString("R"), position.y.ToString("R"), position.z.ToString("R"));
            if (confirmation != null) return confirmation;

            AssetUtils.AddToScene(projectPath, position, parentTransform);
            GameObject instance = Selection.activeGameObject;
            string instanceName = instance != null ? instance.name : prefab.name;
            return AutomationResponse.Success($"'{instanceName}' added to the active scene.", new {gameObjectName = instanceName});
        }

        private static List<Transform> FindSceneObjectsByName(Scene scene, string objectName)
        {
            List<Transform> matches = new List<Transform>();
            if (!scene.IsValid() || !scene.isLoaded) return matches;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform transform in transforms)
                {
                    if (transform.name == objectName) matches.Add(transform);
                }
            }
            return matches;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return null;
            List<string> names = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
