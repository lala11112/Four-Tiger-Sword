using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Persists adjustable motion on the existing UI assets without rebuilding or opening their views.</summary>
public static class UIPresentationSetup
{
    private const string ShopPath = "Assets/_Game/Shop/UI/TemporaryShop.prefab";
    private const string MapPath = "Assets/_Project/Prefabs/UI/DungeonFullMap.prefab";
    private const string WorkshopPath = "Assets/_Project/Prefabs/UI/HubWorkshop.prefab";
    private const string OverlayPath = "Assets/_Project/Prefabs/UI/BoundaryZeroRunOverlay.prefab";
    private const string TitlePath = "Assets/_Project/Scenes/TitleScene.unity";
    private const string RewardFeedPath = "Assets/BoundaryUI/Resources/BoundaryUI/GoldHUD/GoldRewardFeed.prefab";
    private static readonly string[] PrefabPaths = { ShopPath, MapPath, WorkshopPath, OverlayPath, RewardFeedPath };

    [MenuItem("Tools/Studio Baekho/BoundaryZERO/Install UI Motion")]
    private static void InstallMenu() => Debug.Log(Install());

    public static string Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Install UI motion in Edit Mode.");
        Preflight();
        int changed = 0;
        if (EditPrefab(ShopPath, InstallShop)) changed++;
        if (EditPrefab(MapPath, InstallMap)) changed++;
        if (EditPrefab(WorkshopPath, InstallWorkshop)) changed++;
        if (EditPrefab(OverlayPath, InstallButtons)) changed++;
        if (EditPrefab(RewardFeedPath, InstallRewardRenderers)) changed++;
        if (InstallTitle()) changed++;
        return "UI motion installed on the existing shop, full map, workshop, run-overlay buttons and title menu; reward feed renderers repaired. "
            + changed + " target assets saved; existing timing settings and presentation states preserved.";
    }

    private static void Preflight()
    {
        foreach (string path in PrefabPaths)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                throw new InvalidOperationException("UI motion target is missing: " + path);
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TitlePath) == null)
            throw new InvalidOperationException("UI motion title scene is missing: " + TitlePath);
        var title = SceneManager.GetSceneByPath(TitlePath);
        if (title.IsValid() && title.isLoaded && title.isDirty)
            throw new InvalidOperationException("TitleScene has unsaved changes. Save it before installing UI motion; its current edits were not changed.");
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.scene.isDirty && PrefabPaths.Contains(stage.assetPath))
            throw new InvalidOperationException("The target prefab has unsaved Prefab Mode changes: " + stage.assetPath
                + ". Save it before installing UI motion.");
    }

    private static bool EditPrefab(string path, Func<GameObject, bool> install)
    {
        // Read the asset before loading: ExecuteAlways layout components can refresh in a preview scene.
        var original = HierarchyState.Capture(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            bool changed = install(root);
            original.Restore(root);
            if (changed && PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                throw new InvalidOperationException("Could not save UI motion target: " + path);
            return changed;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static bool InstallShop(GameObject root)
    {
        var view = Require<ShopView>(root);
        var panel = Reference<GameObject>(view, "panel");
        var content = Reference<RectTransform>(view, "boundary.Content");
        var visual = content.parent as RectTransform;
        if (visual == null) throw new InvalidOperationException("The shop motion visual is missing.");
        bool changed = EnsureGroup(panel);
        changed |= EnsureMotion(panel, visual, .24f, .16f, 20f);
        changed |= EnsureMotion(content.gameObject, content, .14f, 0f, 0f);
        changed |= InstallButtons(root);
        return changed;
    }

    private static bool InstallMap(GameObject root)
    {
        var view = Require<DungeonMinimapView>(root);
        var viewport = Reference<RectTransform>(view, "viewport");
        var panel = viewport.parent as RectTransform;
        if (panel == null) throw new InvalidOperationException("The full-map motion panel is missing.");
        bool changed = EnsureMotion(root, panel, .18f, .14f, 0f);
        changed |= InstallButtons(root);
        return changed;
    }

    private static bool InstallWorkshop(GameObject root)
    {
        var view = Require<HubWorkshopView>(root);
        var panel = Reference<GameObject>(view, "references.panel");
        var visual = panel.transform.Find("Window") as RectTransform;
        if (visual == null) throw new InvalidOperationException("The workshop motion window is missing.");
        bool changed = EnsureGroup(panel);
        changed |= EnsureMotion(panel, visual, .24f, .16f, 20f);
        changed |= InstallButtons(root);
        return changed;
    }

    private static bool InstallTitle()
    {
        var scene = SceneManager.GetSceneByPath(TitlePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        var previous = SceneManager.GetActiveScene();
        if (opened) scene = EditorSceneManager.OpenScene(TitlePath, OpenSceneMode.Additive);
        try
        {
            var managers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TitleManager>(true)).ToArray();
            if (managers.Length != 1) throw new InvalidOperationException("TitleScene requires exactly one existing TitleManager.");
            var start = Reference<UnityEngine.UI.Button>(managers[0], "startButton");
            if (start.transform.parent == null) throw new InvalidOperationException("The title menu button parent is missing.");
            var menu = start.transform.parent.gameObject;
            var original = HierarchyState.Capture(menu);
            bool changed = InstallButtons(menu);
            foreach (var button in menu.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                changed |= EnsureMotion(button.gameObject, button.transform as RectTransform, .24f, .16f, 14f);
            original.Restore(menu);
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save TitleScene UI motion.");
            }
            return changed;
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static bool EnsureMotion(GameObject host, RectTransform defaultVisual, float enter, float exit, float rise)
    {
        if (host == null || defaultVisual == null) throw new InvalidOperationException("A UI motion target is unbound.");
        var motion = host.GetComponent<UIModalMotion>();
        bool added = motion == null;
        bool changed = added;
        if (motion == null) motion = host.AddComponent<UIModalMotion>();
        var serialized = new SerializedObject(motion);
        var visual = serialized.FindProperty("visual");
        if (visual.objectReferenceValue == null)
        {
            visual.objectReferenceValue = defaultVisual;
            if (added)
            {
                serialized.FindProperty("enterDuration").floatValue = enter;
                serialized.FindProperty("exitDuration").floatValue = exit;
                serialized.FindProperty("rise").floatValue = rise;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }
        var target = visual.objectReferenceValue as RectTransform;
        if (target == null || (target != host.transform && !target.IsChildOf(host.transform)))
            throw new InvalidOperationException("UI motion visual must remain inside its owner: " + host.name);
        changed |= EnsureGroup(target.gameObject);
        if (changed) EditorUtility.SetDirty(motion);
        return changed;
    }

    private static bool EnsureGroup(GameObject root)
    {
        if (root.GetComponent<CanvasGroup>() != null) return false;
        EditorUtility.SetDirty(root.AddComponent<CanvasGroup>());
        return true;
    }

    private static bool InstallButtons(GameObject root)
    {
        bool changed = false;
        foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
        {
            if (button.GetComponent<UIButtonMotion>() != null) continue;
            EditorUtility.SetDirty(button.gameObject.AddComponent<UIButtonMotion>());
            changed = true;
        }
        return changed;
    }

    private static bool InstallRewardRenderers(GameObject root)
    {
        bool changed = false;
        foreach (var graphic in root.GetComponentsInChildren<BoundaryZeroUI.BoundaryGoldFrameGraphic>(true))
        {
            if (graphic.GetComponent<CanvasRenderer>() != null) continue;
            EditorUtility.SetDirty(graphic.gameObject.AddComponent<CanvasRenderer>());
            changed = true;
        }
        return changed;
    }

    private static T Require<T>(GameObject root) where T : Component
    {
        var value = root.GetComponent<T>();
        if (value == null) throw new InvalidOperationException(root.name + " is missing " + typeof(T).Name + ".");
        return value;
    }

    private static T Reference<T>(UnityEngine.Object owner, string field) where T : UnityEngine.Object
    {
        var property = new SerializedObject(owner).FindProperty(field);
        var value = property != null ? property.objectReferenceValue as T : null;
        if (value == null) throw new InvalidOperationException(owner.name + " is missing its " + field + " binding.");
        return value;
    }

    private sealed class HierarchyState
    {
        private sealed class Node
        {
            public int[] Path;
            public bool Active;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
            public bool IsRect;
            public Vector2 AnchorMin, AnchorMax, Pivot, Size;
            public Vector3 AnchoredPosition;
            public Group[] Groups;
            public UnityEngine.UI.ColorBlock[] ButtonColors;
        }

        private struct Group
        {
            public float Alpha;
            public bool Enabled, Interactable, Blocks, IgnoreParents;
        }

        private readonly List<Node> _nodes = new();

        public static HierarchyState Capture(GameObject root)
        {
            var state = new HierarchyState();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var path = new List<int>();
                for (var current = transform; current != root.transform; current = current.parent) path.Add(current.GetSiblingIndex());
                path.Reverse();
                var node = new Node { Path = path.ToArray(), Active = transform.gameObject.activeSelf,
                    Position = transform.localPosition, Rotation = transform.localRotation, Scale = transform.localScale,
                    Groups = transform.GetComponents<CanvasGroup>().Select(group => new Group { Alpha = group.alpha,
                        Enabled = group.enabled, Interactable = group.interactable, Blocks = group.blocksRaycasts,
                        IgnoreParents = group.ignoreParentGroups }).ToArray(),
                    ButtonColors = transform.GetComponents<UnityEngine.UI.Button>().Select(button => button.colors).ToArray() };
                if (transform is RectTransform rect)
                {
                    node.IsRect = true; node.AnchorMin = rect.anchorMin; node.AnchorMax = rect.anchorMax;
                    node.Pivot = rect.pivot; node.Size = rect.sizeDelta; node.AnchoredPosition = rect.anchoredPosition3D;
                }
                state._nodes.Add(node);
            }
            return state;
        }

        public void Restore(GameObject root)
        {
            foreach (var node in _nodes)
            {
                var transform = root.transform;
                foreach (int index in node.Path) transform = transform.GetChild(index);
                if (transform.gameObject.activeSelf != node.Active) transform.gameObject.SetActive(node.Active);
                if (node.IsRect && transform is RectTransform rect)
                {
                    if (rect.anchorMin != node.AnchorMin) rect.anchorMin = node.AnchorMin;
                    if (rect.anchorMax != node.AnchorMax) rect.anchorMax = node.AnchorMax;
                    if (rect.pivot != node.Pivot) rect.pivot = node.Pivot;
                    if (rect.sizeDelta != node.Size) rect.sizeDelta = node.Size;
                    if (rect.anchoredPosition3D != node.AnchoredPosition) rect.anchoredPosition3D = node.AnchoredPosition;
                }
                else if (transform.localPosition != node.Position) transform.localPosition = node.Position;
                if (transform.localRotation != node.Rotation) transform.localRotation = node.Rotation;
                if (transform.localScale != node.Scale) transform.localScale = node.Scale;
                var groups = transform.GetComponents<CanvasGroup>();
                for (int i = 0; i < node.Groups.Length; i++)
                {
                    var before = node.Groups[i]; var group = groups[i];
                    if (group.alpha != before.Alpha) group.alpha = before.Alpha;
                    if (group.enabled != before.Enabled) group.enabled = before.Enabled;
                    if (group.interactable != before.Interactable) group.interactable = before.Interactable;
                    if (group.blocksRaycasts != before.Blocks) group.blocksRaycasts = before.Blocks;
                    if (group.ignoreParentGroups != before.IgnoreParents) group.ignoreParentGroups = before.IgnoreParents;
                }
                var buttons = transform.GetComponents<UnityEngine.UI.Button>();
                for (int i = 0; i < node.ButtonColors.Length; i++)
                    if (!buttons[i].colors.Equals(node.ButtonColors[i])) buttons[i].colors = node.ButtonColors[i];
            }
        }
    }
}
