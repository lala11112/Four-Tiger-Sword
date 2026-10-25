using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Development grants use the same ownership API as gameplay.</summary>
[CustomEditor(typeof(PlayerItemRuntime))]
public sealed class PlayerItemRuntimeEditor : Editor
{
    private string filter = "";

    public override void OnInspectorGUI()
    {
        // Runtime catalog reassignment must go through Catalog's ownership-reset API.
        // Set the serialized starting catalog in Edit Mode; grants below stay active in Play Mode.
        using (new EditorGUI.DisabledScope(Application.isPlaying)) DrawDefaultInspector();
        var runtime = (PlayerItemRuntime)target;
        ItemCatalog catalog = runtime.Catalog;
        if (catalog == null)
        {
            EditorGUILayout.HelpBox("백호툴 출력 폴더를 반입한 뒤 Item Catalog를 연결하세요.", MessageType.Info);
            return;
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("아이템 시험", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("콘텐츠 상태", catalog.contentStatus + " / revision " + catalog.contentRevision);
        if (catalog.contentStatus == "draft")
            EditorGUILayout.HelpBox("이 카탈로그는 생성 도구의 초안입니다. 보유·효과 시험에 사용할 수 있으며 검토 상태는 원본 그대로 보존됩니다.", MessageType.Info);
        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Play Mode에서 지급·제거하면 실제 보유 효과를 시험할 수 있습니다.", MessageType.Info);

        filter = EditorGUILayout.TextField("이름 / ID 검색", filter);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("전체 지급"))
                    foreach (var item in catalog.items.Where(i => i != null)) runtime.TryAddItem(item.itemId);
                if (GUILayout.Button("전체 제거"))
                    foreach (var item in runtime.OwnedItems.ToArray()) runtime.TryRemoveItem(item.itemId);
            }
            EditorGUILayout.LabelField("현재 보유", runtime.OwnedItems.Count.ToString());
            foreach (ItemDefinition item in catalog.items.Where(i => i != null &&
                (string.IsNullOrWhiteSpace(filter) || i.itemId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (i.displayName ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(item, typeof(ItemDefinition), false);
                    bool owned = runtime.Contains(item.itemId);
                    if (GUILayout.Button(owned ? "제거" : "지급", GUILayout.Width(52)))
                    {
                        if (owned) runtime.TryRemoveItem(item.itemId);
                        else runtime.TryAddItem(item.itemId);
                    }
                }
            }
        }
        if (Application.isPlaying) Repaint();
    }
}

public static class BaekhoItemSetup
{
    [MenuItem("Tools/Studio Baekho/Items/Attach Selected Catalog To Player")]
    private static void AttachSelectedCatalog()
    {
        if (!(Selection.activeObject is ItemCatalog catalog))
            throw new InvalidOperationException("Project 창에서 반입한 ItemCatalog를 선택하세요.");
        Selection.activeGameObject = AttachCatalogToPlayer(catalog).gameObject;
    }

    /// <summary>Assigns a catalog with Undo. Never saves the scene or changes prefab assets.</summary>
    public static PlayerItemRuntime AttachCatalogToPlayer(ItemCatalog catalog, PlayerController player = null)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이어 연결은 편집 모드에서 실행하세요.");
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        if (player == null && Selection.activeGameObject != null)
            player = Selection.activeGameObject.GetComponentInParent<PlayerController>();
        if (player == null)
        {
            Scene active = SceneManager.GetActiveScene();
            var candidates = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(p => p.gameObject.scene == active).ToArray();
            if (candidates.Length != 1)
                throw new InvalidOperationException("활성 씬에 플레이어가 정확히 1개 있어야 합니다. 연결할 PlayerController를 선택하거나 명시적으로 전달하세요.");
            player = candidates[0];
        }
        if (EditorUtility.IsPersistent(player) || !player.gameObject.scene.IsValid() || !player.gameObject.scene.isLoaded)
            throw new InvalidOperationException("열린 씬의 PlayerController만 연결할 수 있습니다.");
        var runtime = player.GetComponent<PlayerItemRuntime>();
        if (runtime == null) runtime = Undo.AddComponent<PlayerItemRuntime>(player.gameObject);
        Undo.RecordObject(runtime, "Connect Baekho Item Catalog");
        runtime.Catalog = catalog;
        PrefabUtility.RecordPrefabInstancePropertyModifications(runtime);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        return runtime;
    }
}
