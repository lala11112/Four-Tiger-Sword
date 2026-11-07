using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Builds a correctly scaled T-pose avatar without changing the supplied model importer.</summary>
public static class SwordRobotBossRigSetup
{
    public static void Configure(Animator animator, GameObject weapon, string folder, AnimatorController controller,
        string avatarAssetName = "SwordRobotBoss_Avatar")
    {
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            throw new InvalidOperationException("Sword robot requires its supplied humanoid avatar.");
        if (weapon == null) throw new ArgumentNullException(nameof(weapon));
        // The weapon is authored after the avatar. Excluding it prevents Rebind from restoring
        // its temporary pre-grip transform stored in a humanoid skeleton description.
        var transforms = animator.GetComponentsInChildren<Transform>(true)
            .Where(t => t != weapon.transform && !t.IsChildOf(weapon.transform)).ToArray();
        var positions = transforms.Select(t => t.localPosition).ToArray();
        var rotations = transforms.Select(t => t.localRotation).ToArray();
        var scales = transforms.Select(t => t.localScale).ToArray();
        var fingers = new List<Transform>();
        var fingerRotations = new List<Quaternion>();
        Quaternion gripRotation;
        Quaternion leftGripRotation;
        Vector3 gripPosition;
        Avatar avatar = null;
        try
        {
            foreach (bool right in new[] { false, true }) EnforceArmTPose(animator, right);
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Quaternion gripWorld = Quaternion.LookRotation(-animator.transform.up, animator.transform.forward);
            gripRotation = Quaternion.Inverse(hand.rotation) * gripWorld;
            gripPosition = hand.InverseTransformVector(animator.transform.right * .1f + animator.transform.forward * .05f);
            leftGripRotation = Quaternion.Inverse(gripWorld) * left.rotation;
            var description = animator.avatar.humanDescription;
            description.skeleton = transforms.Select(t => new SkeletonBone {
                name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale
            }).ToArray();
            avatar = AvatarBuilder.BuildHumanAvatar(animator.gameObject, description);
            avatar.name = avatarAssetName;
            if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Corrected robot avatar is invalid.");
            CaptureFingerGrip(animator, false, fingers, fingerRotations);
            CaptureFingerGrip(animator, true, fingers, fingerRotations);
        }
        finally
        {
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].localPosition = positions[i];
                transforms[i].localRotation = rotations[i];
                transforms[i].localScale = scales[i];
            }
        }
        string path = folder + "/" + avatarAssetName + ".asset";
        Avatar saved = AssetDatabase.LoadAssetAtPath<Avatar>(path);
        if (saved == null) { AssetDatabase.CreateAsset(avatar, path); saved = avatar; }
        else { EditorUtility.CopySerialized(avatar, saved); UnityEngine.Object.DestroyImmediate(avatar); }
        animator.avatar = saved;
        animator.applyRootMotion = false;
        animator.fireEvents = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var layers = controller.layers;
        if (layers.Length > 0) layers[0].iKPass = true;
        controller.layers = layers;
        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        weapon.transform.SetParent(rightHand, false);
        weapon.transform.localPosition = gripPosition;
        weapon.transform.localRotation = gripRotation;
        Vector3 parentScale = rightHand.lossyScale;
        weapon.transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);
        var leftGrip = weapon.transform.Find("LeftHandGrip");
        if (leftGrip == null)
        {
            leftGrip = new GameObject("LeftHandGrip").transform;
            leftGrip.SetParent(weapon.transform, false);
        }
        leftGrip.localPosition = new Vector3(0f, -.20f, 0f);
        leftGrip.localRotation = leftGripRotation;
        var component = animator.GetComponent<SwordRobotBossWeaponGrip>() ?? animator.gameObject.AddComponent<SwordRobotBossWeaponGrip>();
        component.owner = animator.GetComponentInParent<Boss>();
        component.leftHandGrip = leftGrip;
        component.fingers = fingers.ToArray();
        component.fingerRotations = fingerRotations.ToArray();
        EditorUtility.SetDirty(saved);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(saved);
        AssetDatabase.SaveAssetIfDirty(controller);
    }

    private static void EnforceArmTPose(Animator animator, bool right)
    {
        var upper = animator.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
        var lower = animator.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
        var hand = animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        Vector3 direction = animator.transform.TransformDirection(right ? Vector3.right : Vector3.left);
        upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, direction) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, direction) * lower.rotation;
        var middle = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        var index = animator.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
        var little = animator.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
        Vector3 palm = Vector3.Cross(middle.position - hand.position, index.position - little.position).normalized * (right ? 1f : -1f);
        Quaternion currentBasis = Quaternion.LookRotation(palm, middle.position - hand.position);
        Quaternion desiredBasis = Quaternion.LookRotation(-animator.transform.up, direction);
        hand.rotation = desiredBasis * Quaternion.Inverse(currentBasis) * hand.rotation;
    }

    private static void CaptureFingerGrip(Animator animator, bool right, List<Transform> fingers, List<Quaternion> rotations)
    {
        var hand = animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        var middle = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        Vector3 axis = Vector3.Cross(middle.position - hand.position, -animator.transform.up).normalized;
        HumanBodyBones first = right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal;
        HumanBodyBones last = right ? HumanBodyBones.RightLittleDistal : HumanBodyBones.LeftLittleDistal;
        for (int i = (int)first; i <= (int)last; i++)
        {
            var bone = animator.GetBoneTransform((HumanBodyBones)i);
            if (bone == null) continue;
            bone.rotation = Quaternion.AngleAxis((i - (int)first) % 3 == 0 ? 60f : 70f, axis) * bone.rotation;
            fingers.Add(bone); rotations.Add(bone.localRotation);
        }
    }
}
