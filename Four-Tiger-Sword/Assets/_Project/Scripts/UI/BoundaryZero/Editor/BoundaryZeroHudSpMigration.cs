using System;
using System.Linq;
using ArpgHud;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Removes the retired SP display from the existing HUD assets.</summary>
public static class BoundaryZeroHudSpMigration
{
    private const string BaseHud = "Assets/UI/ARPGHUD/Integration/Prefabs/PENTA_InGameHUD.prefab";
    private const string PlayerHud = "Assets/_Project/Prefabs/UI/PlayerHUD.prefab";
    private const string GameScene = "Assets/_Project/Scenes/Game Scene.unity";
    private static readonly string[] ScenePaths =
    {
        GameScene,
        "Assets/_Project/Scenes/Lab Visual Baseline.unity",
        "Assets/_Project/Scenes/Lab Visual Pilot.unity"
    };

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Apply the HUD migration in Edit Mode.");
        string prefabStage = PrefabStageUtility.GetCurrentPrefabStage()?.assetPath;
        if (prefabStage == BaseHud || prefabStage == PlayerHud)
            throw new InvalidOperationException("Close the HUD Prefab Stage before applying the migration.");
        foreach (string path in ScenePaths)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                throw new InvalidOperationException("Missing scene: " + path);
            var scene = SceneManager.GetSceneByPath(path);
            if (scene.IsValid() && scene.isDirty)
                throw new InvalidOperationException("HUD migration requires the target scene to be saved: " + path);
        }

        EditPrefab(BaseHud, root => RemoveChild(root.transform, "HudRoot/VitalsGroup/AuxiliaryBar"));
        EditPrefab(PlayerHud, root =>
        {
            bool changed = false;
            var modifications = PrefabUtility.GetPropertyModifications(root);
            if (modifications != null)
            {
                var kept = modifications.Where(mod => mod.target is not HudView
                    || (mod.propertyPath != "auxiliaryFill" && mod.propertyPath != "auxiliaryText")).ToArray();
                if (kept.Length != modifications.Length)
                {
                    PrefabUtility.SetPropertyModifications(root, kept);
                    changed = true;
                }
            }

            var vitals = root.transform.Find("BoundaryZeroHUD/Vitals")
                ?? throw new InvalidOperationException("PlayerHUD has no Vitals group.");
            changed |= RemoveChild(vitals, "SpTrack");
            changed |= RemoveChild(vitals, "SpFill");
            changed |= RemoveChild(vitals, "SpValue");
            return changed;
        });
        foreach (string path in ScenePaths) EditScene(path);
        return "Removed SP displays and references from the HUD prefabs and legacy scene canvases.";
    }

    private static void EditPrefab(string path, Func<GameObject, bool> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (edit(root) && PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                throw new InvalidOperationException("Could not save prefab: " + path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EditScene(string path)
    {
        var active = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(path);
        bool wasPresent = scene.IsValid();
        bool wasLoaded = wasPresent && scene.isLoaded;
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            bool changed = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (path == GameScene) changed |= RemoveRetiredLabelOverride(root);
                foreach (var manager in root.GetComponentsInChildren<PlayerUIManager>(true))
                {
                    var hp = new SerializedObject(manager).FindProperty("hpBarImage").objectReferenceValue
                        as UnityEngine.UI.Image;
                    var canvas = hp != null ? hp.GetComponentInParent<Canvas>(true) : null;
                    if (canvas == null) continue;
                    changed |= RemoveChild(canvas.transform, "SpBar");
                    changed |= RemoveChild(canvas.transform, "Sp");
                }
            }
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Could not save scene: " + path);
            }
        }
        finally
        {
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, !wasPresent);
            if (active.IsValid() && active.isLoaded && SceneManager.GetActiveScene() != active)
                SceneManager.SetActiveScene(active);
        }
    }

    private static bool RemoveRetiredLabelOverride(GameObject root)
    {
        if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) != PlayerHud)
            return false;
        var modifications = PrefabUtility.GetPropertyModifications(root);
        if (modifications == null) return false;
        // Game Scene kept this color override after the SpValue text was removed from PlayerHUD.
        var kept = modifications.Where(mod => mod.target != null
            || mod.propertyPath != "m_fontColor32.rgba" || mod.value != "4294306784").ToArray();
        if (kept.Length == modifications.Length) return false;
        PrefabUtility.SetPropertyModifications(root, kept);
        return true;
    }

    private static bool RemoveChild(Transform parent, string path)
    {
        var child = parent.Find(path);
        if (child == null) return false;
        Object.DestroyImmediate(child.gameObject);
        return true;
    }
}
