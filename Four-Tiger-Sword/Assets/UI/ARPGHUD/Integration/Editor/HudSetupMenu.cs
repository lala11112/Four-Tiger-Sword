using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArpgHud.Editor
{
    /// <summary>Explicit scene setup only; never changes project settings or saves scenes.</summary>
    public static class HudSetupMenu
    {
        const string PrefabPath = "Assets/UI/ARPGHUD/Integration/Prefabs/PENTA_InGameHUD.prefab";
        const string AddMenu = "Tools/PENTA HUD/Add HUD to Current Scene";
        const string ValidateMenu = "Tools/PENTA HUD/Validate Selected HUD";
        static bool preparing;
        static double prepareDeadline;
        static readonly List<System.Action<bool>> prepareCallbacks = new List<System.Action<bool>>();

        [MenuItem("Tools/PENTA HUD/Prepare Text Resources")]
        static void PrepareTextResourcesMenu() { PrepareTextResources(null); }

        /// <summary>Import canonical TMP essentials only when no TMP settings exist. Never exits the Editor.</summary>
        public static void PrepareTextResources(System.Action<bool> onReady)
        {
            if (TMP_Settings.LoadDefaultSettings() != null) { onReady?.Invoke(true); return; }
            if (onReady != null) prepareCallbacks.Add(onReady);
            if (preparing) return;
            preparing = true;
            prepareDeadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += PollTextResources;
            try { TMP_PackageResourceImporter.ImportResources(true, false, false); }
            catch (System.Exception exception)
            { Debug.LogException(exception); CompleteTextResources(false); }
        }

        static void PollTextResources()
        {
            try
            {
                if (TMP_Settings.LoadDefaultSettings() != null) { CompleteTextResources(true); return; }
                if (EditorApplication.timeSinceStartup > prepareDeadline)
                { Debug.LogError("PENTA HUD: TMP Essential Resources import timed out."); CompleteTextResources(false); }
            }
            catch (System.Exception exception)
            { Debug.LogException(exception); CompleteTextResources(false); }
        }

        static void CompleteTextResources(bool success)
        {
            EditorApplication.update -= PollTextResources;
            preparing = false;
            System.Action<bool>[] callbacks = prepareCallbacks.ToArray();
            prepareCallbacks.Clear();
            if (success) TMPro_EventManager.ON_RESOURCES_LOADED();
            foreach (System.Action<bool> callback in callbacks)
                try { callback(success); }
                catch (System.Exception exception) { Debug.LogException(exception); }
        }

        [MenuItem(AddMenu)]
        public static void AddToCurrentScene()
        {
            if (!CanAdd()) return;
            Scene scene = SceneManager.GetActiveScene();
            if (TMP_Settings.LoadDefaultSettings() == null)
            {
                int originalSceneHandle = scene.handle;
                PrepareTextResources(success => {
                    if (success && CanAdd() && SceneManager.GetActiveScene().handle == originalSceneHandle)
                        AddToCurrentScene();
                });
                return;
            }
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                HudController existing = root.GetComponentInChildren<HudController>(true);
                if (existing == null) continue;
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("PENTA HUD already exists in the active scene; selected it.", existing);
                return;
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Debug.LogError("PENTA HUD prefab is missing: " + PrefabPath); return; }
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Add PENTA HUD");
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        [MenuItem(AddMenu, true)]
        static bool CanAdd()
        {
            Scene scene = SceneManager.GetActiveScene();
            return !EditorApplication.isPlayingOrWillChangePlaymode && scene.IsValid() && scene.isLoaded;
        }

        static HudController SelectedController()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null) return null;
            HudController controller = selected.GetComponentInParent<HudController>(true);
            return controller != null ? controller : selected.GetComponentInChildren<HudController>(true);
        }

        [MenuItem(ValidateMenu)]
        static void ValidateSelected()
        {
            HudController controller = SelectedController();
            string[] errors = Validate(controller != null ? controller.gameObject : null);
            string message = errors.Length == 0 ? "HUD validation passed." : string.Join("\n", errors);
            if (errors.Length == 0) Debug.Log(message, controller);
            else Debug.LogError(message, controller);
            EditorUtility.DisplayDialog("PENTA HUD Validation", message, "OK");
        }

        [MenuItem(ValidateMenu, true)]
        static bool CanValidate() { return !EditorApplication.isPlayingOrWillChangePlaymode && SelectedController() != null; }

        /// <summary>Read-only asset/instance validation. Returns errors without dialogs or scene changes.</summary>
        public static string[] Validate(GameObject target)
        {
            var errors = new List<string>();
            if (TMP_Settings.LoadDefaultSettings() == null)
                errors.Add("TMP Essential Resources are missing. Use Tools/PENTA HUD/Prepare Text Resources.");
            if (target == null) return new[] { "Select a HUD object." };
            HudController controller = target.GetComponent<HudController>();
            if (controller == null) controller = target.GetComponentInChildren<HudController>(true);
            if (controller == null) return new[] { "HudController is missing." };
            GameObject root = controller.gameObject;
            var controllerData = new SerializedObject(controller);
            HudView view = controllerData.FindProperty("view").objectReferenceValue as HudView;
            if (view == null) errors.Add("HudController view binding is missing.");
            Canvas canvas = root.GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                errors.Add("HUD requires a Screen Space Overlay Canvas.");
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) > 0)
                    errors.Add(child.name + ": missing script.");
            foreach (UnityEngine.UI.Graphic graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                if (graphic.raycastTarget) errors.Add(graphic.name + ": raycast target should be disabled.");
            foreach (UnityEngine.UI.Image image in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                if (image.sprite == null) errors.Add(image.name + ": sprite binding is missing.");
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                TMP_FontAsset font = text.font;
                Material material = text.fontSharedMaterial;
                if (font == null || font.atlasTextures == null || font.atlasTextures.Length == 0 || font.atlasTextures[0] == null)
                    errors.Add(text.name + ": font or font atlas is missing.");
                if (material == null || material.shader == null || !material.HasProperty("_MainTex") || material.GetTexture("_MainTex") == null)
                    errors.Add(text.name + ": font material or material atlas is missing.");
                else if (font != null && font.atlasTextures != null && font.atlasTextures.Length > 0
                    && material.GetTexture("_MainTex") != font.atlasTextures[0])
                    errors.Add(text.name + ": material atlas does not match the assigned font.");
            }
            if (view != null)
            {
                var viewData = new SerializedObject(view);
                HudElementTheme theme = viewData.FindProperty("elementTheme").objectReferenceValue as HudElementTheme;
                if (theme == null) errors.Add("Element theme binding is missing.");
                else for (int i = 1; i <= 5; i++)
                    if (!theme.TryGet(i, out var element)) errors.Add("Element " + i + ": E/R icon pair is missing.");
            }
            return errors.ToArray();
        }
    }
}
