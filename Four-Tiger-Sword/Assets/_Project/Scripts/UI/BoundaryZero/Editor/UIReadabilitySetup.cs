using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>Updates authored title/loading assets without touching unrelated scenes or progression.</summary>
public static class UIReadabilitySetup
{
    public static string ApplyTitleAndLoading()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        const string scenePath = "Assets/_Project/Scenes/TitleScene.unity";
        const string overlayPath = "Assets/_Project/Prefabs/UI/BoundaryZeroRunOverlay.prefab";
        var title = SceneManager.GetSceneByPath(scenePath);
        if (title.IsValid() && title.isLoaded && title.isDirty)
            throw new InvalidOperationException("TitleScene has unsaved edits; preserve them before updating title bindings.");
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == overlayPath && stage.scene.isDirty)
            throw new InvalidOperationException("The loading prefab has unsaved edits.");
        var overlay = PrefabUtility.LoadPrefabContents(overlayPath);
        try
        {
            var view = overlay.GetComponent<BoundaryZeroRunOverlay>();
            view.ShowLoading(); view.Hide();
            PrefabUtility.SaveAsPrefabAsset(overlay, overlayPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(overlay); }
        bool opened = !title.IsValid() || !title.isLoaded;
        var previous = SceneManager.GetActiveScene();
        if (opened) title = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            foreach (var root in title.GetRootGameObjects())
            {
                foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                    if (button.GetComponentInParent<TitleManager>() != null
                        || button.transform.parent.name == "Menu")
                        EnsureFocus(button);
                foreach (var manager in root.GetComponentsInChildren<TitleManager>(true))
                {
                    var serialized = new SerializedObject(manager);
                    foreach (string field in new[] { "startButton", "quitButton" })
                        if (serialized.FindProperty(field).objectReferenceValue is UnityEngine.UI.Button button) EnsureFocus(button);
                }
                foreach (var events in root.GetComponentsInChildren<EventSystem>(true))
                { events.firstSelectedGameObject = null; EditorUtility.SetDirty(events); }
            }
            EditorSceneManager.MarkSceneDirty(title);
            if (!EditorSceneManager.SaveScene(title)) throw new InvalidOperationException("Could not save TitleScene.");
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (opened) EditorSceneManager.CloseScene(title, true);
        }
        return "Title hover bindings and concise loading prefab saved.";
    }

    private static void EnsureFocus(UnityEngine.UI.Button button)
    {
        if (button.GetComponent<TitleButtonFocus>() == null) button.gameObject.AddComponent<TitleButtonFocus>();
        EditorUtility.SetDirty(button.gameObject);
    }
}
