using System;
using RootMotion;
using RootMotion.FinalIK;
using UnityEditor;
using UnityEngine;

/// <summary>Configures only the supplied prefab instance; shared controller/avatar assets stay untouched.</summary>
public static class BossFinalIKHitReactionSetup
{
    public static BossFinalIKHitReaction Configure(GameObject root)
        => Configure(root, root != null ? root.GetComponentInChildren<Animator>(true) : null);

    public static BossFinalIKHitReaction Configure(GameObject root, Animator animator)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        var owner = root.GetComponent<Boss>();
        var collider = root.GetComponent<Collider>();
        if (owner == null || collider == null) throw new InvalidOperationException("Boss root and existing root collider are required.");
        if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            throw new InvalidOperationException("FinalIK reaction requires a valid humanoid Animator.");

        var references = new BipedReferences();
        BipedReferences.AutoDetectReferences(ref references, animator.transform,
            new BipedReferences.AutoDetectParams(true, false));
        string message = string.Empty;
        // FinalIK's auto-detection assumes every Humanoid is valid; validate actual hierarchy too.
        if (BipedReferences.SetupError(references, ref message)) throw new InvalidOperationException(message);
        ValidateLimb(references.leftUpperArm, references.leftForearm, references.leftHand);
        ValidateLimb(references.rightUpperArm, references.rightForearm, references.rightHand);
        ValidateLimb(references.leftThigh, references.leftCalf, references.leftFoot);
        ValidateLimb(references.rightThigh, references.rightCalf, references.rightFoot);
        Transform previous = references.pelvis;
        foreach (Transform spine in references.spine)
        {
            ValidateSegment(previous, spine);
            previous = spine;
        }
        if (references.spine.Length == 0) throw new InvalidOperationException("A nonempty spine mapping is required.");
        ValidateSegment(references.leftUpperArm, references.rightUpperArm);
        ValidateSegment(references.leftThigh, references.rightThigh);

        var ik = animator.GetComponent<FullBodyBipedIK>() ?? animator.gameObject.AddComponent<FullBodyBipedIK>();
        ik.SetReferences(references, IKSolverFullBodyBiped.DetectRootNodeBone(references));
        if (ik.ReferencesError(ref message) || !ik.solver.IsValid(ref message))
            throw new InvalidOperationException("Invalid FinalIK boss mapping: " + message);
        ik.fixTransforms = false; // Runtime restores the last pre-IK pose, including during local hitstop.
        ik.solver.IKPositionWeight = 0f;
        ik.solver.iterations = 4;
        ik.solver.pullBodyHorizontal = 0f;
        ik.solver.pullBodyVertical = 0f;
        ik.solver.spineStiffness = .65f;
        foreach (var chain in ik.solver.chain) { chain.pull = 0f; chain.reach = 0f; }
        foreach (var effector in ik.solver.effectors)
        {
            effector.target = null;
            effector.positionWeight = 1f;
            effector.rotationWeight = 1f;
            effector.maintainRelativePositionWeight = 0f;
        }
        // Shoulders target the recoil offset around the pinned lower spine; pelvis/thighs/feet
        // stay on the animation. Runtime includes positionOffset in these pinned targets.
        ik.solver.leftArmMapping.maintainRotationWeight = 1f;
        ik.solver.rightArmMapping.maintainRotationWeight = 1f;
        ik.solver.leftLegMapping.maintainRotationWeight = 1f;
        ik.solver.rightLegMapping.maintainRotationWeight = 1f;

        var hit = animator.GetComponent<HitReaction>() ?? animator.gameObject.AddComponent<HitReaction>();
        hit.ik = ik;
        hit.weight = 0f;
        hit.effectorHitPoints = new[] { BossFinalIKHitReaction.CreateUpperBodyHitPoint(collider, .30f) };
        hit.boneHitPoints = Array.Empty<HitReaction.HitPointBone>();
        var feedback = root.GetComponent<BossFinalIKHitReaction>() ?? root.AddComponent<BossFinalIKHitReaction>();
        feedback.owner = owner;
        feedback.ik = ik;
        feedback.reaction = hit;
        feedback.hitCollider = collider;
        feedback.recoilDistance = .24f;
        feedback.duration = .30f;
        EditorUtility.SetDirty(ik);
        EditorUtility.SetDirty(hit);
        EditorUtility.SetDirty(feedback);
        return feedback;
    }

    private static void ValidateLimb(Transform first, Transform middle, Transform last)
    {
        ValidateSegment(first, middle);
        ValidateSegment(middle, last);
    }

    private static void ValidateSegment(Transform first, Transform second)
    {
        if (first == null || second == null || (first.position - second.position).sqrMagnitude < .00000001f)
            throw new InvalidOperationException("FinalIK requires nonzero bone lengths: "
                + (first != null ? first.name : "null") + " -> " + (second != null ? second.name : "null"));
    }
}
