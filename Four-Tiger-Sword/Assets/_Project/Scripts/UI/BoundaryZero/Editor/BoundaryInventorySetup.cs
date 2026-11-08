using System;
using BoundaryZero.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Installs a bound inventory without replacing or saving the user's open game scene.</summary>
public static class BoundaryInventorySetup
{
    public const string PrefabPath = "Assets/BoundaryInventory/Prefabs/InventoryCanvas.prefab";
    public const string GameScenePath = "Assets/_Project/Scenes/Game Scene.unity";

    [MenuItem("Tools/Studio Baekho/BoundaryZERO/Install Inventory In Game Scene")]
    private static void Menu() => Debug.Log(Install());

    public static string Install()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Install the inventory in Edit Mode.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != GameScenePath) throw new InvalidOperationException("Open Game Scene before installing the inventory.");
        RunManager run = null;
        InventoryView view = null;
        PlayerInventoryPresenter presenter = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var candidate in root.GetComponentsInChildren<RunManager>(true))
            {
                if (run != null) throw new InvalidOperationException("Game Scene must have exactly one RunManager.");
                run = candidate;
            }
            foreach (var candidate in root.GetComponentsInChildren<InventoryView>(true))
            {
                if (view != null) throw new InvalidOperationException("Game Scene already has multiple inventory views.");
                view = candidate;
            }
            foreach (var candidate in root.GetComponentsInChildren<PlayerInventoryPresenter>(true))
            {
                if (presenter != null) throw new InvalidOperationException("Game Scene already has multiple inventory presenters.");
                presenter = candidate;
            }
        }
        if (run == null || run.player == null) throw new InvalidOperationException("Game Scene requires its bound run player.");
        if (view == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Import the BoundaryInventory presentation package first.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Install inventory");
            view = instance.GetComponent<InventoryView>();
        }
        if (view == null || view.Catalog == null) throw new InvalidOperationException("The inventory prefab has no view or catalog.");
        var canvas = view.GetComponent<Canvas>();
        if (canvas == null) throw new InvalidOperationException("The inventory view has no Canvas.");
        Undo.RecordObject(canvas, "Set inventory layer");
        canvas.sortingOrder = 170;
        PrefabUtility.RecordPrefabInstancePropertyModifications(canvas);
        if (presenter == null) presenter = Undo.AddComponent<PlayerInventoryPresenter>(run.gameObject);
        Undo.RecordObject(presenter, "Bind inventory presenter");
        presenter.Configure(run, view);
        EditorUtility.SetDirty(presenter);
        EditorSceneManager.MarkSceneDirty(scene);
        return "Installed one I-key inventory bound to Game Scene's run player; scene remains open and unsaved.";
    }
}
