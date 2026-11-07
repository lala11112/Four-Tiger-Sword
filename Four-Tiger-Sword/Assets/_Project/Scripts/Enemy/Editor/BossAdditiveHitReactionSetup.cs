using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Builds owned additive assets for a copied boss prefab.</summary>
public static class BossAdditiveHitReactionSetup
{
    public const string LayerName = "HitReaction";
    public const string TimeParameterName = "HitReactionTime";
    public const float ClipDuration = .24f;
    private const float RecoilAmplitude = 2f;

    public static BossAdditiveHitReaction Configure(GameObject root, string outputFolder)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        var owner = root.GetComponent<Enemy>();
        var animator = root.GetComponentInChildren<Animator>(true);
        if (owner == null || animator == null || animator.avatar == null || !animator.avatar.isHuman)
            throw new InvalidOperationException("The additive boss copy requires an Enemy and a humanoid Animator.");
        var source = animator.runtimeAnimatorController as AnimatorController;
        if (source == null) throw new InvalidOperationException("Expected the sword robot's AnimatorController.");
        EnsureFolder(outputFolder);

        string controllerPath = outputFolder + "/Additive_Boss.controller";
        string sourcePath = AssetDatabase.GetAssetPath(source);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            if (string.IsNullOrEmpty(sourcePath) || !AssetDatabase.CopyAsset(sourcePath, controllerPath))
                throw new InvalidOperationException("Could not copy the boss AnimatorController.");
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        }
        var timeParameter = controller.parameters.FirstOrDefault(p => p.name == TimeParameterName);
        if (timeParameter == null) controller.AddParameter(TimeParameterName, AnimatorControllerParameterType.Float);
        else if (timeParameter.type != AnimatorControllerParameterType.Float)
            throw new InvalidOperationException("The additive sample-time parameter must be a float.");

        var neutral = Clip(outputFolder + "/Additive_Neutral.anim", "Additive_Neutral", false);
        var recoil = Clip(outputFolder + "/Additive_Hit.anim", "Additive_Hit", true);
        AnimationUtility.SetAdditiveReferencePose(recoil, neutral, 0f);
        AnimationUtility.SetAdditiveReferencePose(neutral, neutral, 0f);
        EditorUtility.SetDirty(recoil);
        EditorUtility.SetDirty(neutral);
        var mask = Mask(outputFolder + "/Additive_UpperBody.mask");

        var layers = controller.layers.ToList();
        var layer = layers.FirstOrDefault(l => l.name == LayerName);
        if (layer == null)
        {
            var stateMachine = new AnimatorStateMachine { name = LayerName };
            AssetDatabase.AddObjectToAsset(stateMachine, controller);
            layer = new AnimatorControllerLayer { name = LayerName, stateMachine = stateMachine };
            layers.Add(layer);
        }
        layer.blendingMode = AnimatorLayerBlendingMode.Additive;
        layer.avatarMask = mask;
        layer.defaultWeight = 0f;
        layer.iKPass = false;
        layer.syncedLayerIndex = -1;
        var neutralState = State(layer.stateMachine, "Neutral", neutral, new Vector3(240f, 40f));
        var hitState = State(layer.stateMachine, "Hit", recoil, new Vector3(240f, 120f));
        hitState.speed = 0f;
        hitState.timeParameter = TimeParameterName;
        hitState.timeParameterActive = true;
        layer.stateMachine.defaultState = neutralState;
        controller.layers = layers.ToArray();
        animator.runtimeAnimatorController = controller;

        var reaction = root.GetComponent<BossAdditiveHitReaction>() ?? root.AddComponent<BossAdditiveHitReaction>();
        reaction.owner = owner;
        reaction.animator = animator;
        reaction.layerName = LayerName;
        reaction.stateName = "Hit";
        reaction.timeParameterName = TimeParameterName;
        reaction.duration = ClipDuration;
        reaction.maximumWeight = .8f;
        reaction.blendIn = .018f;
        reaction.blendOut = .045f;
        reaction.minimumRetriggerInterval = .045f;
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(reaction);
        EditorUtility.SetDirty(animator);
        AssetDatabase.SaveAssetIfDirty(controller);
        AssetDatabase.SaveAssetIfDirty(neutral);
        AssetDatabase.SaveAssetIfDirty(recoil);
        AssetDatabase.SaveAssetIfDirty(mask);
        return reaction;
    }

    private static AnimatorState State(AnimatorStateMachine stateMachine, string name, AnimationClip clip, Vector3 position)
    {
        var state = stateMachine.states.FirstOrDefault(s => s.state.name == name).state;
        if (state == null) state = stateMachine.AddState(name, position);
        state.motion = clip;
        state.speed = 1f;
        state.writeDefaultValues = false;
        return state;
    }

    private static AnimationClip Clip(string path, string name, bool recoil)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.name = name;
        clip.frameRate = 60f;
        // Muscle-only curves keep the root, hips, legs, hands and weapon animation intact.
        foreach (var binding in AnimationUtility.GetCurveBindings(clip)) AnimationUtility.SetEditorCurve(clip, binding, null);
        Muscle(clip, "Spine Front-Back", recoil ? -.13f : 0f);
        Muscle(clip, "Spine Twist Left-Right", recoil ? .08f : 0f);
        Muscle(clip, "Chest Front-Back", recoil ? -.22f : 0f);
        Muscle(clip, "Chest Left-Right", recoil ? .055f : 0f);
        Muscle(clip, "Chest Twist Left-Right", recoil ? .095f : 0f);
        Muscle(clip, "Neck Nod Down-Up", recoil ? -.055f : 0f);
        Muscle(clip, "Head Nod Down-Up", recoil ? -.04f : 0f);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        settings.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static void Muscle(AnimationClip clip, string property, float amount)
    {
        if (!HumanTrait.MuscleName.Contains(property))
            throw new InvalidOperationException("Unknown humanoid muscle: " + property);
        amount *= RecoilAmplitude;
        var curve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(.028f, amount),
            new Keyframe(.078f, -amount * .4f), new Keyframe(.135f, amount * .18f),
            new Keyframe(.19f, -amount * .055f), new Keyframe(ClipDuration, 0f));
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), property), curve);
    }

    private static AvatarMask Mask(string path)
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, path);
        }
        mask.name = "Additive_UpperBody";
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
        mask.transformCount = 0;
        EditorUtility.SetDirty(mask);
        return mask;
    }

    private static void EnsureFolder(string folder)
    {
        folder = folder.Replace('\\', '/').TrimEnd('/');
        if (folder != "Assets" && !folder.StartsWith("Assets/", StringComparison.Ordinal))
            throw new ArgumentException("Output folder must be inside Assets.", nameof(folder));
        if (AssetDatabase.IsValidFolder(folder)) return;
        int slash = folder.LastIndexOf('/');
        string parent = folder.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
    }
}
