using System;
using System.Collections.Generic;
using System.Linq;
using AllIn13DShader;
using RootMotion.FinalIK;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Installs an independent second humanoid rig into an already configured boss prefab.</summary>
public static class BossPhaseTwoVisualSetup
{
    public const string ModelPath = "Assets/BossPhase2HipArmorV5/BossValidated.prefab";
    public const string ModelFbxPath = "Assets/BossPhase2HipArmorV5/Boss_Phase2_Humanoid.fbx";
    public const string WeaponPath = "Assets/BossPhase2Weapon250cm/Boss_Phase2_Weapon_250cm.prefab";
    private const string NoisePath = "Assets/Plugins/AllIn13DShader/DefaultTextures/NoiseTextureDefault.png";

    public static void Configure(GameObject root, string folder)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        var owner = root.GetComponent<Boss>();
        var presentation = root.GetComponent<SwordRobotBossPresentation>();
        if (owner == null || presentation == null || presentation.visualPivot == null)
            throw new InvalidOperationException("The existing boss and VisualPivot are required before phase-two setup.");
        var visuals = root.GetComponent<BossPhaseVisuals>() ?? root.AddComponent<BossPhaseVisuals>();
        var first = visuals.phaseOne?.animator;
        if (first == null) first = presentation.visualPivot.GetComponentInChildren<Animator>(true);
        var controller = first != null ? first.runtimeAnimatorController as AnimatorController : null;
        if (first == null || controller == null)
            throw new InvalidOperationException("The first-phase rig must have its configured AnimatorController.");

        string output = folder.TrimEnd('/') + "/Phase2";
        EnsureFolder(output);
        var model = visuals.phaseTwo?.root;
        if (model == null)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null) throw new InvalidOperationException("The selected second-phase model is missing.");
            model = (GameObject)PrefabUtility.InstantiatePrefab(source, presentation.visualPivot);
            model.name = "SwordRobotPhase2Model";
        }
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;
        var second = model.GetComponentInChildren<Animator>(true) ?? model.AddComponent<Animator>();
        var importedAvatar = AssetDatabase.LoadAllAssetsAtPath(ModelFbxPath).OfType<Avatar>()
            .FirstOrDefault(avatar => avatar != null && avatar.isHuman && avatar.isValid);
        if (importedAvatar == null) throw new InvalidOperationException("The selected model has no valid imported humanoid avatar.");
        second.avatar = importedAvatar;
        second.runtimeAnimatorController = controller;

        var weaponSource = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPath);
        if (weaponSource == null) throw new InvalidOperationException("The selected 250 cm phase-two weapon is missing.");
        var weapon = visuals.phaseTwo?.weapon != null ? visuals.phaseTwo.weapon.gameObject : null;
        if (weapon == null)
        {
            weapon = (GameObject)PrefabUtility.InstantiatePrefab(weaponSource, model.transform);
            weapon.name = "BossPhase2Weapon";
        }
        var sourceAlignment = weaponSource.transform.Find("Grip Alignment");
        var alignment = weapon.transform.Find("Grip Alignment");
        if (sourceAlignment == null || alignment == null)
            throw new InvalidOperationException("The phase-two weapon is missing its authored grip alignment.");
        // This imported mesh's blade points along +Z. The shared grip and trail use +Y;
        // derive the correction from the source every time so rebuilding cannot accumulate it.
        alignment.localRotation = Quaternion.Euler(-90f, 0f, 0f) * sourceAlignment.localRotation;
        SwordRobotBossRigSetup.Configure(second, weapon, output, controller, "BossPhase2_Avatar");
        var firstGrip = first.GetComponent<SwordRobotBossWeaponGrip>();
        Transform firstWeapon = firstGrip != null && firstGrip.leftHandGrip != null
            ? firstGrip.leftHandGrip.parent : presentation.bladeTrail != null ? presentation.bladeTrail.transform.parent : null;
        TrailRenderer secondTrail = visuals.phaseTwo?.trail;
        if (secondTrail == null && presentation.bladeTrail != null)
        {
            var trail = Object.Instantiate(presentation.bladeTrail.gameObject, weapon.transform, false);
            trail.name = "BladeTrail";
            trail.transform.localPosition = Vector3.up * 1.25f;
            secondTrail = trail.GetComponent<TrailRenderer>();
            secondTrail.emitting = false;
        }
        foreach (Transform child in model.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = root.layer;

        var feedback = root.GetComponent<BossFinalIKHitReaction>();
        var firstIK = first.GetComponent<FullBodyBipedIK>();
        var firstReaction = first.GetComponent<HitReaction>();
        if (feedback != null)
        {
            BossFinalIKHitReactionSetup.Configure(root, second);
            // The prefab always starts with phase one; runtime switches the root adapter atomically.
            feedback.ik = firstIK;
            feedback.reaction = firstReaction;
            EditorUtility.SetDirty(feedback);
        }
        visuals.owner = owner;
        visuals.phaseOne = new BossPhaseVisualSet {
            root = first.gameObject, animator = first, weapon = firstWeapon,
            trail = presentation.bladeTrail, ik = firstIK, reaction = firstReaction
        };
        visuals.phaseTwo = new BossPhaseVisualSet {
            root = model, animator = second, weapon = weapon.transform, trail = secondTrail,
            ik = second.GetComponent<FullBodyBipedIK>(), reaction = second.GetComponent<HitReaction>()
        };
        visuals.phaseOne.root.SetActive(true);
        visuals.phaseTwo.root.SetActive(false);
        PrepareEffects(root, output + "/Materials");
        AssetDatabase.SaveAssetIfDirty(controller);
        EditorUtility.SetDirty(second);
        EditorUtility.SetDirty(visuals);
        EditorUtility.SetDirty(root);
    }

    private static void PrepareEffects(GameObject root, string folder)
    {
        EnsureFolder(folder);
        var shader = Shader.Find("AllIn13DShader/AllIn13DShader");
        var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
        if (shader == null || noise == null) throw new InvalidOperationException("The existing enemy Hit/Fade shader resources are missing.");
        var fade = root.GetComponent<EnemyDeathFade>() ?? root.AddComponent<EnemyDeathFade>();
        if (root.GetComponent<EnemyHitFlash>() == null) root.AddComponent<EnemyHitFlash>();
        var serialized = new SerializedObject(fade);
        var targets = serialized.FindProperty("_targets");
        var previous = new Dictionary<Renderer, Material[]>();
        for (int i = 0; i < targets.arraySize; i++)
        {
            var target = targets.GetArrayElementAtIndex(i);
            var renderer = target.FindPropertyRelative("Renderer").objectReferenceValue as Renderer;
            if (renderer == null) continue;
            var slots = target.FindPropertyRelative("Materials");
            var materials = new Material[slots.arraySize];
            for (int s = 0; s < slots.arraySize; s++) materials[s] = slots.GetArrayElementAtIndex(s).objectReferenceValue as Material;
            previous[renderer] = materials;
        }
        var owned = root.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                && renderer.GetComponentInParent<Enemy>(true) == root.GetComponent<Enemy>()).ToArray();
        targets.arraySize = owned.Length;
        for (int i = 0; i < owned.Length; i++)
        {
            var renderer = owned[i];
            var living = renderer.sharedMaterials;
            previous.TryGetValue(renderer, out var old);
            var target = targets.GetArrayElementAtIndex(i);
            target.FindPropertyRelative("Renderer").objectReferenceValue = renderer;
            var slots = target.FindPropertyRelative("Materials");
            slots.arraySize = living.Length;
            for (int s = 0; s < living.Length; s++)
            {
                if (living[s] == null) throw new InvalidOperationException("A boss renderer has a missing material: " + renderer.name);
                living[s] = HitMaterial(living[s], folder, shader);
                Material death = old != null && old.Length == living.Length ? old[s] : null;
                if (death == null || !death.IsKeywordEnabled("_FADE_ON")) death = DeathMaterial(living[s], folder, noise);
                slots.GetArrayElementAtIndex(s).objectReferenceValue = death;
            }
            renderer.sharedMaterials = living;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Material HitMaterial(Material source, string folder, Shader shader)
    {
        if (source.HasProperty("_HitBlend") && source.IsKeywordEnabled("_HIT_ON")) return source;
        bool allInOne = source.HasProperty("_HitBlend");
        string shaderName = source.shader != null ? source.shader.name : "";
        if (!allInOne && shaderName != "Universal Render Pipeline/Lit" && shaderName != "Standard")
            throw new InvalidOperationException("Unsupported boss material shader: " + shaderName);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId);
        string path = folder + "/HIT_" + guid + "_" + localId + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create = material == null;
        if (create)
        {
            material = allInOne ? new Material(source) : new Material(shader);
            material.name = source.name + "_BossHit";
            if (!allInOne) MaterialConverterTool.ApplyConversion(source, material);
        }
        material.EnableKeyword("_HIT_ON");
        material.SetFloat("_Hit", 1f);
        material.SetFloat("_HitBlend", 0f);
        material.SetColor("_HitColor", Color.white);
        material.SetFloat("_HitGlow", 2f);
        if (create) AssetDatabase.CreateAsset(material, path);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    private static Material DeathMaterial(Material source, string folder, Texture2D noise)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId);
        string path = folder + "/DEATH_" + guid + "_" + localId + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create = material == null;
        if (create) { material = new Material(source); material.name = source.name + "_Death"; }
        material.EnableKeyword("_FADE_ON");
        material.EnableKeyword("_FADEUVSET_UV1");
        material.EnableKeyword("_ALPHA_CUTOFF_ON");
        material.SetFloat("_FadeOn", 1f);
        material.SetFloat("_FadeAmount", 0f);
        material.SetFloat("_FadePower", 1.15f);
        material.SetFloat("_FadeTransition", .2f);
        material.SetFloat("_FadeUVSet", 0f);
        material.SetTexture("_FadeTex", noise);
        material.SetFloat("_AlphaCutoffOn", 1f);
        material.SetFloat("_AlphaCutoffValue", .25f);
        material.SetFloat("_BlendSrc", (float)BlendMode.One);
        material.SetFloat("_BlendDst", (float)BlendMode.Zero);
        material.SetFloat("_ZWrite", 1f);
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = (int)RenderQueue.AlphaTest;
        if (create) AssetDatabase.CreateAsset(material, path);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (path == "Assets" || AssetDatabase.IsValidFolder(path)) return;
        if (!path.StartsWith("Assets/", StringComparison.Ordinal)) throw new ArgumentException("The output folder must be inside Assets.");
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
