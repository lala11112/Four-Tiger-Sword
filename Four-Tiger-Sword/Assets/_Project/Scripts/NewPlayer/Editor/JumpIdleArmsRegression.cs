using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Verifies when the shipped jump mask applies and which bones it changes on the player Avatar.</summary>
public static class JumpIdleArmsRegression
{
    private const string ControllerPath = "Assets/_Project/Animations/AnimController/BDJ.controller";
    private const string JumpAssets = "Assets/_Project/Animations/PlayerAnim/Jump/";
    private const string LayerName = "Jump Idle Arms";
    private const string OverlayState = "Idle Arms";
    private const float Step = .01f;
    private static readonly string[] JumpStates = { "Jump", "Jump_Air", "Jump_Land" };
    private static readonly HumanBodyBones[] RightArmBones =
    {
        HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
        HumanBodyBones.RightThumbProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightMiddleDistal
    };
    private static readonly HumanBodyBones[] UnmaskedBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head,
        HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
        HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftMiddleDistal,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
    };

    [MenuItem("Tools/Studio Baekho/Combat/Run Jump Idle Arms Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Jump Idle Arms regression requires Edit Mode.");

        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Jump Idle Arms: " + message);
            checks++;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Check(controller != null, "The player controller is available.");
        var baseStates = controller.layers[0].stateMachine.states.Select(child => child.state).ToArray();
        var overlay = controller.layers.SingleOrDefault(layer => layer.name == LayerName);
        Check(overlay != null && overlay.blendingMode == AnimatorLayerBlendingMode.Override
            && overlay.defaultWeight == 0f && !overlay.iKPass && overlay.syncedLayerIndex == -1,
            "The right arm uses an independent, inactive-by-default override without another IK pass.");
        var mask = overlay.avatarMask;
        Check(mask != null && AssetDatabase.GetAssetPath(mask) == JumpAssets + "Jump_IdleArms.mask"
            && mask.transformCount == 0, "The layer uses the dedicated Humanoid mask without transform overrides.");
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
        {
            var part = (AvatarMaskBodyPart)i;
            bool armOrFinger = part == AvatarMaskBodyPart.RightArm || part == AvatarMaskBodyPart.RightFingers;
            Check(mask.GetHumanoidBodyPartActive(part) == armOrFinger, "Unexpected mask setting for " + part + ".");
        }

        var idle = baseStates.Single(state => state.name == "Idle");
        var overlayState = overlay.stateMachine.defaultState;
        var overlayClip = overlayState?.motion as AnimationClip;
        Check(overlayState != null && overlayState.name == OverlayState && !overlayState.iKOnFeet
            && overlayClip != null && overlayClip.humanMotion && overlayClip != idle.motion
            && AssetDatabase.GetAssetPath(overlayClip) == JumpAssets + "Jump_IdleArms.anim"
            && AnimationUtility.GetAnimationEvents(overlayClip).Length == 0,
            "The default overlay uses the dedicated, event-free Idle clip.");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character.fbx");
        Check(model != null, "The real player rig is available.");

        var preview = EditorSceneManager.NewPreviewScene();
        var reference = new AnimatorController { name = "Jump Idle Arms pose reference" };
        reference.AddLayer("Base Layer");
        var referenceMachine = reference.layers[0].stateMachine;
        try
        {
            foreach (var source in baseStates.Where(state => state.name == "Idle" || JumpStates.Contains(state.name)))
            {
                var state = referenceMachine.AddState(source.name);
                state.motion = source.motion;
                state.speed = source.speed;
                state.cycleOffset = source.cycleOffset;
                state.iKOnFeet = source.iKOnFeet;
                state.writeDefaultValues = source.writeDefaultValues;
            }

            Animator Clone(RuntimeAnimatorController motions)
            {
                var rig = Object.Instantiate(model);
                SceneManager.MoveGameObjectToScene(rig, preview);
                var animator = rig.GetComponentInChildren<Animator>(true);
                Check(animator != null && animator.avatar != null && animator.avatar.isValid && animator.isHuman,
                    "The fixture binds the player's Humanoid Avatar.");
                animator.runtimeAnimatorController = motions;
                animator.applyRootMotion = false;
                animator.fireEvents = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Update(0f);
                return animator;
            }

            var player = Clone(controller);
            var baseline = Clone(reference);
            int armsLayer = player.GetLayerIndex(LayerName);
            Check(armsLayer > 0, "The overlay is present on the runtime Animator.");

            bool Is(AnimatorStateInfo info, string state) => info.shortNameHash == Animator.StringToHash(state);
            AnimatorStateInfo Target() => player.IsInTransition(0)
                ? player.GetNextAnimatorStateInfo(0) : player.GetCurrentAnimatorStateInfo(0);
            void Weight(float expected, string context)
                => Check(Mathf.Abs(player.GetLayerWeight(armsLayer) - expected) < .001f, context);
            void Sample(Animator animator, string state, int layer, float normalizedTime)
            {
                animator.Play(state, layer, normalizedTime);
                animator.Update(0f);
            }
            void Fade(string state, float duration, float weight)
            {
                player.CrossFadeInFixedTime(state, duration, 0, 0f);
                player.Update(Step);
                Check(Is(Target(), state), "The transition targets " + state + ".");
                Weight(weight, "Incorrect arms weight on the first frame of the blend to " + state + ".");
            }
            void Finish(string state, float weight)
            {
                for (int i = 0; i < 7; i++)
                {
                    player.Update(Step);
                    if (Mathf.Abs(player.GetLayerWeight(armsLayer) - weight) >= .001f)
                        throw new InvalidOperationException("Jump Idle Arms: the overlay weight changed during the blend to " + state + ".");
                }
                Check(!player.IsInTransition(0) && Is(player.GetCurrentAnimatorStateInfo(0), state),
                    "The base layer reaches " + state + ".");
                Weight(weight, "Incorrect arms weight after reaching " + state + ".");
            }

            Weight(0f, "Grounded Idle begins with the arms layer disabled.");
            Fade("Jump", .025f, 1f);
            Finish("Jump", 1f);
            for (int i = 0; i < 60 && !Is(Target(), "Jump_Air"); i++)
            {
                player.Update(Step);
                if (player.GetLayerWeight(armsLayer) < .999f)
                    throw new InvalidOperationException("Jump Idle Arms: the natural Jump to Air transition lost the overlay.");
            }
            Check(Is(Target(), "Jump_Air"), "The natural Jump to Air transition keeps the overlay.");
            Finish("Jump_Air", 1f);
            Fade("Jump_Land", .05f, 1f);
            Fade("Jump", .025f, 1f);
            Finish("Jump", 1f);
            Fade("Jump_Air", .05f, 1f);
            Fade("FireAirStart", .05f, 0f);
            Finish("FireAirStart", 0f);

            foreach (string target in new[] { "Run", "Dash", "Fire1", "Hit", "Die" })
            {
                Sample(player, "Jump_Land", 0, .25f);
                Weight(1f, "Entering Land enables the overlay before blending to " + target + ".");
                Fade(target, .05f, 0f);
                Finish(target, 0f);
            }
            Sample(player, "Jump_Land", 0, .9f);
            Weight(1f, "The arms layer remains active during landing.");
            for (int i = 0; i < 20 && !Is(Target(), "Idle"); i++) player.Update(Step);
            Check(Is(Target(), "Idle"), "Landing naturally transitions to Idle.");
            Weight(0f, "The first frame of the natural Land to Idle blend releases the arms layer.");
            Finish("Idle", 0f);

            foreach (string state in JumpStates)
            {
                const float jumpTime = .45f;
                const float idleTime = .31f;
                Sample(player, state, 0, jumpTime);
                Weight(1f, state + " enables the overlay on direct entry.");
                Sample(player, OverlayState, armsLayer, idleTime);
                Sample(baseline, "Idle", 0, idleTime);
                float armToIdle = RightArmBones.Max(bone => RotationDelta(player, baseline, bone));
                Check(armToIdle < 2f, state + " right arm and fingers match Idle. Max difference " + armToIdle + " degrees.");

                Sample(baseline, state, 0, jumpTime);
                float armToJump = RightArmBones.Max(bone => RotationDelta(player, baseline, bone));
                Check(armToJump > 5f, state + " sample actually exercises the right arm override.");
                float bodyToJump = UnmaskedBones.Max(bone => RotationDelta(player, baseline, bone));
                float hipShift = Vector3.Distance(HipPosition(player), HipPosition(baseline));
                Check(bodyToJump < 2f && hipShift < .01f,
                    state + " preserves jump motion for the left arm, body and legs. Max difference " + bodyToJump + " degrees, hips " + hipShift + ".");
            }

            return $"PASS: jump Idle arms ({checks} checks, transition weights and masked player poses).";
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
            foreach (var child in referenceMachine.states) Object.DestroyImmediate(child.state);
            Object.DestroyImmediate(referenceMachine);
            Object.DestroyImmediate(reference);
        }
    }

    private static float RotationDelta(Animator first, Animator second, HumanBodyBones bone)
    {
        var firstBone = first.GetBoneTransform(bone);
        var secondBone = second.GetBoneTransform(bone);
        if (firstBone == null || secondBone == null)
            throw new InvalidOperationException("Jump Idle Arms: the target Avatar is missing " + bone + ".");
        return Quaternion.Angle(firstBone.localRotation, secondBone.localRotation);
    }

    private static Vector3 HipPosition(Animator animator)
        => animator.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position);
}
