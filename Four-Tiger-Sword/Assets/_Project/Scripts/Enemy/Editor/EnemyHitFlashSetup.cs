using System;
using System.Collections.Generic;
using System.Text;
using AllIn13DShader;
using UnityEditor;
using UnityEngine;

/// <summary>Prepares serialized AllIn1 Hit variants so the effect also survives build stripping.</summary>
public static class EnemyHitFlashSetup
{
    public const string MaterialFolder = "Assets/_Project/Models/Materials/EnemyHitFlash";
    private static readonly string[] SearchFolders = { "Assets/_Project", "Assets/_Game" };
    private const string HitKeyword = "_HIT_ON";

    [MenuItem("Tools/Studio Baekho/Combat/Prepare Enemy Hit Flash Materials")]
    private static void PrepareMenu() => Debug.Log(PrepareAll());

    public static string PrepareAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Prepare enemy hit materials in Edit Mode.");

        Shader shader = Shader.Find("AllIn13DShader/AllIn13DShader");
        if (shader == null) throw new InvalidOperationException("The AllIn1 3D shader is unavailable.");
        EnsureFolder(MaterialFolder);

        var prepared = new Dictionary<Material, Material>();
        var unsupported = new SortedSet<string>(StringComparer.Ordinal);
        int prefabs = 0, changedPrefabs = 0, rendererSlots = 0, fadeSlots = 0, createdMaterials = 0;
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", SearchFolders);
        Array.Sort(prefabGuids, StringComparer.Ordinal);
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || asset.GetComponentsInChildren<Enemy>(true).Length == 0) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                prefabs++;
                bool changed = false;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!IsEnemyMesh(renderer)) continue;
                    Material[] materials = renderer.sharedMaterials;
                    bool rendererChanged = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        Material replacement = PrepareMaterial(materials[i], shader, prepared, unsupported, ref createdMaterials);
                        if (replacement == null || replacement == materials[i]) continue;
                        materials[i] = replacement;
                        rendererChanged = true;
                        rendererSlots++;
                    }
                    if (!rendererChanged) continue;
                    renderer.sharedMaterials = materials;
                    changed = true;
                }

                // The delayed death effect swaps materials. Retain Hit in those variants too.
                foreach (EnemyDeathFade fade in root.GetComponentsInChildren<EnemyDeathFade>(true))
                {
                    var serialized = new SerializedObject(fade);
                    SerializedProperty targets = serialized.FindProperty("_targets");
                    if (targets == null || !targets.isArray) continue;
                    bool fadeChanged = false;
                    for (int targetIndex = 0; targetIndex < targets.arraySize; targetIndex++)
                    {
                        SerializedProperty target = targets.GetArrayElementAtIndex(targetIndex);
                        var renderer = target.FindPropertyRelative("Renderer").objectReferenceValue as Renderer;
                        if (!IsEnemyMesh(renderer)) continue;
                        SerializedProperty materials = target.FindPropertyRelative("Materials");
                        for (int i = 0; i < materials.arraySize; i++)
                        {
                            SerializedProperty slot = materials.GetArrayElementAtIndex(i);
                            var source = slot.objectReferenceValue as Material;
                            Material replacement = PrepareMaterial(source, shader, prepared, unsupported, ref createdMaterials);
                            if (replacement == null || replacement == source) continue;
                            slot.objectReferenceValue = replacement;
                            fadeChanged = true;
                            fadeSlots++;
                        }
                    }
                    if (!fadeChanged) continue;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }

                if (!changed) continue;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                changedPrefabs++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        // Save only our variants; unrelated dirty assets may belong to the user's open work.
        foreach (Material material in new HashSet<Material>(prepared.Values))
            if (material != null) AssetDatabase.SaveAssetIfDirty(material);

        var report = new StringBuilder();
        report.Append($"Enemy Hit materials: inspected {prefabs} prefabs; updated {changedPrefabs}; ");
        report.Append($"prepared {rendererSlots} renderer slots and {fadeSlots} death-fade slots; created {createdMaterials} material variants.");
        if (unsupported.Count > 0)
        {
            report.Append("\nUnsupported materials left unchanged:");
            foreach (string source in unsupported) report.Append("\n- ").Append(source);
        }
        return report.ToString();
    }

    private static bool IsEnemyMesh(Renderer renderer)
        => renderer != null && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
            && renderer.GetComponentInParent<Enemy>() != null;

    private static Material PrepareMaterial(Material source, Shader shader,
        Dictionary<Material, Material> prepared, SortedSet<string> unsupported, ref int createdMaterials)
    {
        if (source == null) return null;
        if (prepared.TryGetValue(source, out Material cached)) return cached;
        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (sourcePath.StartsWith(MaterialFolder + "/", StringComparison.Ordinal))
        {
            ConfigureHit(source);
            prepared[source] = source;
            return source;
        }

        bool allIn1 = source.shader != null && source.shader.keywordSpace.FindKeyword(HitKeyword).isValid
            && source.HasProperty("_HitBlend") && source.HasProperty("_HitColor") && source.HasProperty("_HitGlow");
        string shaderName = source.shader != null ? source.shader.name : "missing shader";
        bool convertible = shaderName == "Universal Render Pipeline/Lit" || shaderName == "Standard";
        if ((!allIn1 && !convertible)
            || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId))
        {
            unsupported.Add($"{sourcePath} / {source.name} ({shaderName})");
            prepared[source] = null;
            return null;
        }

        string destination = $"{MaterialFolder}/HIT_{guid}_{localId}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(destination);
        if (material == null)
        {
            material = allIn1 ? new Material(source) : new Material(shader);
            material.name = source.name + "_Hit";
            if (!allIn1) MaterialConverterTool.ApplyConversion(source, material);
            ConfigureHit(material);
            AssetDatabase.CreateAsset(material, destination);
            createdMaterials++;
        }
        else
        {
            ConfigureHit(material);
        }
        prepared[source] = material;
        return material;
    }

    private static void ConfigureHit(Material material)
    {
        bool changed = !material.IsKeywordEnabled(HitKeyword) || material.GetFloat("_Hit") != 1f
            || material.GetFloat("_HitBlend") != 0f || material.GetColor("_HitColor") != Color.white
            || material.GetFloat("_HitGlow") != 2f;
        if (!changed) return;
        material.EnableKeyword(HitKeyword);
        material.SetFloat("_Hit", 1f);
        material.SetFloat("_HitBlend", 0f);
        material.SetColor("_HitColor", Color.white);
        material.SetFloat("_HitGlow", 2f);
        EditorUtility.SetDirty(material);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int separator = folder.LastIndexOf('/');
        string parent = folder.Substring(0, separator);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folder.Substring(separator + 1));
    }
}
