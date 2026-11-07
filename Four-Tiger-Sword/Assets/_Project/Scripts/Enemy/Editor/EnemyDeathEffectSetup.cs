using System;
using System.Collections.Generic;
using AllIn13DShader;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Serializes death-only Fade, Glitch and Hologram variants for enemy prefabs and player builds.</summary>
public static class EnemyDeathEffectSetup
{
    public const string MaterialFolder = "Assets/_Project/Models/Materials/EnemyDeathFade";
    private const string NoisePath = "Assets/Plugins/AllIn13DShader/DefaultTextures/NoiseTextureDefault.png";
    private static readonly string[] SearchFolders = { "Assets/_Project", "Assets/_Game" };

    [MenuItem("Tools/Studio Baekho/Combat/Prepare Enemy Death Effects")]
    private static void PrepareMenu() => Debug.Log(PrepareAll());

    public static string PrepareAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Prepare enemy death effects in Edit Mode.");

        Shader shader = Shader.Find("AllIn13DShader/AllIn13DShader");
        Texture2D noise = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
        if (shader == null || noise == null)
            throw new InvalidOperationException("AllIn1 shader or death dissolve noise texture is missing.");

        string[] guids = AssetDatabase.FindAssets("t:Prefab", SearchFolders);
        Array.Sort(guids, StringComparer.Ordinal);
        var prefabPaths = new List<string>();
        var livingMaterials = new HashSet<Material>();
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            // A death variant may be shared by many fade targets, but never by a living renderer.
            foreach (Renderer renderer in asset.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null) livingMaterials.Add(material);
            if (asset.GetComponentsInChildren<Enemy>(true).Length > 0) prefabPaths.Add(path);
        }
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (Material material in renderer.sharedMaterials)
                if (material != null) livingMaterials.Add(material);

        EnsureFolder(MaterialFolder);
        var prepared = new Dictionary<Material, Material>();
        int changedPrefabs = 0, addedComponents = 0, renderers = 0, createdMaterials = 0;
        foreach (string path in prefabPaths)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                foreach (Enemy enemy in root.GetComponentsInChildren<Enemy>(true))
                {
                    EnemyDeathFade fade = enemy.GetComponent<EnemyDeathFade>();
                    if (fade == null)
                    {
                        fade = enemy.gameObject.AddComponent<EnemyDeathFade>();
                        var defaults = new SerializedObject(fade);
                        defaults.FindProperty("_delay").floatValue = 0.35f;
                        defaults.FindProperty("_duration").floatValue = 2f;
                        defaults.ApplyModifiedPropertiesWithoutUndo();
                        addedComponents++;
                        changed = true;
                    }
                    var serialized = new SerializedObject(fade);
                    SerializedProperty targets = serialized.FindProperty("_targets");
                    var previous = ReadTargets(targets);
                    var owned = new List<Renderer>();
                    foreach (Renderer renderer in enemy.GetComponentsInChildren<Renderer>(true))
                        if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                            && renderer.GetComponentInParent<Enemy>(true) == enemy && renderer.sharedMaterials.Length > 0)
                            owned.Add(renderer);
                    if (owned.Count == 0)
                        throw new InvalidOperationException($"No enemy mesh renderers found: {path} / {enemy.name}");

                    targets.arraySize = owned.Count;
                    for (int i = 0; i < owned.Count; i++)
                    {
                        Renderer renderer = owned[i];
                        Material[] sourceSlots = renderer.sharedMaterials;
                        previous.TryGetValue(renderer, out Material[] previousSlots);
                        SerializedProperty target = targets.GetArrayElementAtIndex(i);
                        target.FindPropertyRelative("Renderer").objectReferenceValue = renderer;
                        SerializedProperty slots = target.FindPropertyRelative("Materials");
                        slots.arraySize = sourceSlots.Length;
                        for (int slot = 0; slot < sourceSlots.Length; slot++)
                        {
                            Material old = previousSlots != null && previousSlots.Length == sourceSlots.Length ? previousSlots[slot] : null;
                            bool reuse = old != null && old.IsKeywordEnabled("_FADE_ON") && !livingMaterials.Contains(old);
                            Material source = reuse ? old : sourceSlots[slot];
                            if (source == null)
                                throw new InvalidOperationException($"Missing material: {path} / {renderer.name} / slot {slot}");
                            slots.GetArrayElementAtIndex(slot).objectReferenceValue = PrepareMaterial(source, reuse,
                                shader, noise, livingMaterials, prepared, ref createdMaterials);
                        }
                        renderers++;
                    }
                    changed |= serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                if (!changed) continue;
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                    throw new InvalidOperationException("Could not save enemy death effects: " + path);
                changedPrefabs++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (Material material in new HashSet<Material>(prepared.Values))
            AssetDatabase.SaveAssetIfDirty(material);
        return $"Enemy death effects: inspected {prefabPaths.Count} prefabs; updated {changedPrefabs}; "
            + $"added {addedComponents} fade components; prepared {renderers} renderers; created {createdMaterials} death materials.";
    }

    private static Dictionary<Renderer, Material[]> ReadTargets(SerializedProperty targets)
    {
        var previous = new Dictionary<Renderer, Material[]>();
        for (int i = 0; i < targets.arraySize; i++)
        {
            SerializedProperty target = targets.GetArrayElementAtIndex(i);
            var renderer = target.FindPropertyRelative("Renderer").objectReferenceValue as Renderer;
            if (renderer == null) continue;
            SerializedProperty slots = target.FindPropertyRelative("Materials");
            var materials = new Material[slots.arraySize];
            for (int slot = 0; slot < materials.Length; slot++)
                materials[slot] = slots.GetArrayElementAtIndex(slot).objectReferenceValue as Material;
            previous[renderer] = materials;
        }
        return previous;
    }

    private static Material PrepareMaterial(Material source, bool reuse, Shader shader, Texture2D noise,
        HashSet<Material> living, Dictionary<Material, Material> prepared, ref int created)
    {
        if (prepared.TryGetValue(source, out Material cached)) return cached;
        bool allIn1 = source.shader != null && source.shader.keywordSpace.FindKeyword("_HOLOGRAM_ON").isValid
            && source.shader.keywordSpace.FindKeyword("_GLITCH_ON").isValid && source.HasProperty("_FadeAmount");
        string shaderName = source.shader != null ? source.shader.name : "missing shader";
        if (!allIn1 && shaderName != "Universal Render Pipeline/Lit" && shaderName != "Standard")
            throw new InvalidOperationException($"Unsupported enemy death material: {AssetDatabase.GetAssetPath(source)} ({shaderName})");

        Material material = source;
        string destination = null;
        bool createAsset = false;
        if (!reuse)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId))
                throw new InvalidOperationException("Enemy material must be saved before preparing death effects: " + source.name);
            destination = $"{MaterialFolder}/DEATH_{guid}_{localId}.mat";
            material = AssetDatabase.LoadAssetAtPath<Material>(destination);
            if (material == null)
            {
                material = allIn1 ? new Material(source) : new Material(shader);
                material.name = source.name + "_Death";
                if (!allIn1) MaterialConverterTool.ApplyConversion(source, material);
                createAsset = true;
            }
        }
        if (living.Contains(material))
            throw new InvalidOperationException("A death variant is also assigned to a living renderer: " + AssetDatabase.GetAssetPath(material));
        Configure(material, noise);
        // CreateAsset writes immediately, so the first serialized copy must already contain every effect keyword.
        if (createAsset)
        {
            AssetDatabase.CreateAsset(material, destination);
            created++;
        }
        prepared[source] = material;
        return material;
    }

    private static void Configure(Material material, Texture2D noise)
    {
        string before = EditorJsonUtility.ToJson(material);
        material.EnableKeyword("_FADE_ON");
        material.EnableKeyword("_FADEUVSET_UV1");
        material.DisableKeyword("_FADEUVSET_UV2");
        material.DisableKeyword("_FADEUVSET_WORLD_SPACE");
        material.DisableKeyword("_FADE_BURN_ON");
        material.EnableKeyword("_ALPHA_CUTOFF_ON");
        material.EnableKeyword("_GLITCH_ON");
        material.EnableKeyword("_HOLOGRAM_ON");
        material.SetFloat("_FadeOn", 1f);
        material.SetFloat("_FadeAmount", 0f);
        material.SetFloat("_FadePower", 1.15f);
        material.SetFloat("_FadeTransition", 0.2f);
        material.SetFloat("_FadeUVSet", 0f);
        material.SetFloat("_FadeBurnOn", 0f);
        material.SetTexture("_FadeTex", noise);
        material.SetTextureScale("_FadeTex", Vector2.one);
        material.SetTextureOffset("_FadeTex", Vector2.zero);
        material.SetFloat("_AlphaCutoffOn", 1f);
        material.SetFloat("_AlphaCutoffValue", 0.25f);
        material.SetFloat("_BlendSrc", (float)BlendMode.One);
        material.SetFloat("_BlendDst", (float)BlendMode.Zero);
        material.SetFloat("_ZWrite", 1f);
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = (int)RenderQueue.AlphaTest;
        material.SetFloat("_Glitch", 1f);
        material.SetFloat("_GlitchAmount", 0.25f);
        material.SetFloat("_GlitchTiling", 8f);
        material.SetFloat("_GlitchSpeed", 14f);
        material.SetFloat("_GlitchWorldSpace", 1f);
        material.SetVector("_GlitchOffset", new Vector4(-0.5f, 0f, 0f, 0f));
        material.SetFloat("_Hologram", 1f);
        material.SetColor("_HologramColor", new Color(1.15f, 2.2f, 3.2f, 1f));
        material.SetVector("_HologramLineDirection", new Vector4(0f, 1f, 0f, 0f));
        material.SetFloat("_HologramBaseAlpha", 0.2f);
        material.SetFloat("_HologramFrequency", 24f);
        material.SetFloat("_HologramScrollSpeed", 4f);
        material.SetFloat("_HologramAlpha", 1f);
        material.SetFloat("_HologramAccentFrequency", 2f);
        material.SetFloat("_HologramAccentSpeed", 1.25f);
        material.SetFloat("_HologramAccentAlpha", 0.35f);
        material.SetFloat("_HologramLineCenter", 0.5f);
        material.SetFloat("_HologramLineSpacing", 2f);
        material.SetFloat("_HologramLineSmoothness", 2f);
        // Snapshot the complete keyword list before serialization, including newly enabled local variants.
        material.shaderKeywords = material.shaderKeywords;
        if (before != EditorJsonUtility.ToJson(material)) EditorUtility.SetDirty(material);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int separator = folder.LastIndexOf('/');
        EnsureFolder(folder.Substring(0, separator));
        AssetDatabase.CreateFolder(folder.Substring(0, separator), folder.Substring(separator + 1));
    }
}
