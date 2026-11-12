using System;
using System.Collections.Generic;
using AllIn13DShader;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Prepares serialized AllIn1 reveal variants for the player and their weapons.</summary>
public static class PlayerFallRevealSetup
{
    private const string GameScenePath = "Assets/_Project/Scenes/Game Scene.unity";
    private const string MaterialFolder = "Assets/_Project/Models/Materials/PlayerFallReveal";
    private const string NoisePath = "Assets/Plugins/AllIn13DShader/DefaultTextures/NoiseTextureDefault.png";

    [MenuItem("Tools/Studio Baekho/Player/Prepare Fall Reveal Materials")]
    private static void PrepareMenu() => Debug.Log(PrepareGameScene());

    public static string PrepareGameScene()
    {
        RequireEditMode();
        var previousActive = SceneManager.GetActiveScene();
        var game = SceneManager.GetSceneByPath(GameScenePath);
        bool wasListed = game.IsValid();
        bool openedHere = !game.isLoaded;
        if (!openedHere && game.isDirty)
            throw new InvalidOperationException("Save the existing Game Scene changes before preparing fall reveal materials.");
        if (openedHere) game = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);

        try
        {
            PlayerController player = null;
            foreach (var root in game.GetRootGameObjects())
                foreach (var candidate in root.GetComponentsInChildren<PlayerController>(true))
                {
                    if (player != null) throw new InvalidOperationException("Game Scene contains more than one player.");
                    player = candidate;
                }
            if (player == null) throw new InvalidOperationException("Game Scene does not contain a player.");

            string result = Prepare(player);
            if (game.isDirty && !EditorSceneManager.SaveScene(game))
                throw new InvalidOperationException("Could not save the fall reveal bindings in Game Scene.");
            return result;
        }
        finally
        {
            if (openedHere)
            {
                if (previousActive.IsValid() && previousActive.isLoaded && SceneManager.GetActiveScene() != previousActive)
                    SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(game, !wasListed);
            }
        }
    }

    public static string Prepare(PlayerController player)
    {
        RequireEditMode();
        if (player == null || !player.gameObject.scene.IsValid())
            throw new ArgumentException("Fall reveal setup requires a player instance in a scene.", nameof(player));
        var animator = player.GetComponentInChildren<Animator>(true);
        if (animator == null) throw new InvalidOperationException("Player visual hierarchy is missing its Animator.");
        var roots = new List<Transform> { animator.transform };
        var weapons = player.GetComponent<WeaponManager>();
        if (weapons != null)
        {
            AddWeaponRoot(roots, weapons.FireWeapon);
            AddWeaponRoot(roots, weapons.WaterWeapon);
            AddWeaponRoot(roots, weapons.WaterOffhandWeapon);
        }

        var shader = Shader.Find("AllIn13DShader/AllIn13DShader");
        var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
        if (shader == null || noise == null)
            throw new InvalidOperationException("AllIn1 shader or fall reveal noise texture is missing.");

        var sources = new Dictionary<Material, string>();
        var renderers = new HashSet<Renderer>();
        foreach (var root in roots)
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) || !renderers.Add(renderer)) continue;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) throw new InvalidOperationException($"Missing material on {renderer.name}.");
                    if (!sources.ContainsKey(material)) sources.Add(material, GetVariantPath(material));
                }
            }
        if (sources.Count == 0) throw new InvalidOperationException("Player has no mesh materials to reveal.");

        EnsureFolder(MaterialFolder);
        var materials = new List<Material>(sources.Keys);
        materials.Sort((a, b) => StringComparer.Ordinal.Compare(sources[a], sources[b]));
        var variants = new Material[materials.Count];
        for (int i = 0; i < materials.Count; i++)
            variants[i] = PrepareVariant(materials[i], sources[materials[i]], shader, noise);

        var effect = player.GetComponent<PlayerFallReveal>();
        if (effect == null) effect = Undo.AddComponent<PlayerFallReveal>(player.gameObject);
        var serialized = new SerializedObject(effect);
        var visualRoots = serialized.FindProperty("_visualRoots");
        visualRoots.arraySize = roots.Count;
        for (int i = 0; i < roots.Count; i++) visualRoots.GetArrayElementAtIndex(i).objectReferenceValue = roots[i];
        var bindings = serialized.FindProperty("_materials");
        bindings.arraySize = materials.Count;
        for (int i = 0; i < materials.Count; i++)
        {
            var binding = bindings.GetArrayElementAtIndex(i);
            binding.FindPropertyRelative("Source").objectReferenceValue = materials[i];
            binding.FindPropertyRelative("Reveal").objectReferenceValue = variants[i];
        }
        if (serialized.ApplyModifiedProperties()) EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        return $"Player fall reveal: {renderers.Count} mesh renderers, {materials.Count} material variants.";
    }

    private static void AddWeaponRoot(List<Transform> roots, GameObject weapon)
    {
        if (weapon != null && !roots.Contains(weapon.transform)) roots.Add(weapon.transform);
    }

    private static string GetVariantPath(Material source)
    {
        string shader = source.shader != null ? source.shader.name : "missing shader";
        if (!IsAllIn1(source) && shader != "Universal Render Pipeline/Lit" && shader != "Standard")
            throw new InvalidOperationException($"Unsupported fall reveal material: {source.name} ({shader}).");
        if (AssetDatabase.GetAssetPath(source).StartsWith(MaterialFolder + "/", StringComparison.Ordinal))
            throw new InvalidOperationException($"A fall reveal variant is assigned as a normal player material: {source.name}.");
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId))
            throw new InvalidOperationException($"Save the player material before preparing its fall reveal variant: {source.name}.");
        return $"{MaterialFolder}/FALL_REVEAL_{guid}_{localId}.mat";
    }

    private static bool IsAllIn1(Material material) => material.shader != null && material.HasProperty("_FadeAmount")
        && material.shader.keywordSpace.FindKeyword("_FADE_ON").isValid
        && material.shader.keywordSpace.FindKeyword("_HOLOGRAM_ON").isValid
        && material.shader.keywordSpace.FindKeyword("_GLITCH_ON").isValid;

    private static Material PrepareVariant(Material source, string path, Shader shader, Texture2D noise)
    {
        bool allIn1 = IsAllIn1(source);
        var prepared = allIn1 ? new Material(source) : new Material(shader);
        try
        {
            if (!allIn1) MaterialConverterTool.ApplyConversion(source, prepared);
            prepared.name = source.name + "_FallReveal";
            ConfigureVariant(prepared, noise);
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(prepared, path);
                return prepared;
            }
            if (EditorJsonUtility.ToJson(prepared) != EditorJsonUtility.ToJson(existing))
            {
                EditorUtility.CopySerialized(prepared, existing);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
            }
            return existing;
        }
        finally
        {
            if (!EditorUtility.IsPersistent(prepared)) UnityEngine.Object.DestroyImmediate(prepared);
        }
    }

    private static void ConfigureVariant(Material material, Texture2D noise)
    {
        // Match the enemy appearance's dissolve texture and scan-line/glitch settings.
        material.EnableKeyword("_FADE_ON");
        material.EnableKeyword("_FADEUVSET_UV1");
        material.DisableKeyword("_FADEUVSET_UV2");
        material.DisableKeyword("_FADEUVSET_WORLD_SPACE");
        material.DisableKeyword("_FADE_BURN_ON");
        material.EnableKeyword("_ALPHA_CUTOFF_ON");
        material.EnableKeyword("_GLITCH_ON");
        material.EnableKeyword("_HOLOGRAM_ON");
        material.SetFloat("_FadeOn", 1f);
        material.SetFloat("_FadeAmount", 1f);
        material.SetFloat("_FadePower", 1.15f);
        material.SetFloat("_FadeTransition", .2f);
        material.SetFloat("_FadeUVSet", 0f);
        material.SetFloat("_FadeBurnOn", 0f);
        material.SetTexture("_FadeTex", noise);
        material.SetTextureScale("_FadeTex", Vector2.one);
        material.SetTextureOffset("_FadeTex", Vector2.zero);
        material.SetFloat("_AlphaCutoffOn", 1f);
        material.SetFloat("_AlphaCutoffValue", .25f);
        material.SetFloat("_BlendSrc", (float)BlendMode.One);
        material.SetFloat("_BlendDst", (float)BlendMode.Zero);
        material.SetFloat("_ZWrite", 1f);
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = (int)RenderQueue.AlphaTest;
        material.SetFloat("_Glitch", 1f);
        material.SetFloat("_GlitchAmount", .25f);
        material.SetFloat("_GlitchTiling", 8f);
        material.SetFloat("_GlitchSpeed", 14f);
        material.SetFloat("_GlitchWorldSpace", 1f);
        material.SetVector("_GlitchOffset", new Vector4(-.5f, 0f, 0f, 0f));
        material.SetFloat("_Hologram", 1f);
        material.SetColor("_HologramColor", new Color(.18f, 2.7f, 3.6f, 1f));
        material.SetVector("_HologramLineDirection", new Vector4(0f, 1f, 0f, 0f));
        material.SetFloat("_HologramBaseAlpha", .2f);
        material.SetFloat("_HologramFrequency", 24f);
        material.SetFloat("_HologramScrollSpeed", 4f);
        material.SetFloat("_HologramAlpha", 1f);
        material.SetFloat("_HologramAccentFrequency", 2f);
        material.SetFloat("_HologramAccentSpeed", 1.25f);
        material.SetFloat("_HologramAccentAlpha", .35f);
        material.SetFloat("_HologramLineCenter", .5f);
        material.SetFloat("_HologramLineSpacing", 2f);
        material.SetFloat("_HologramLineSmoothness", 2f);
        material.shaderKeywords = material.shaderKeywords;
    }

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Prepare player fall reveal materials in Edit Mode.");
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int separator = folder.LastIndexOf('/');
        EnsureFolder(folder.Substring(0, separator));
        AssetDatabase.CreateFolder(folder.Substring(0, separator), folder.Substring(separator + 1));
    }
}
