using UnityEngine;

/// <summary>Keeps the robot's broad humanoid proportions on the authored two-hand sword grip.</summary>
[RequireComponent(typeof(Animator))]
public sealed class SwordRobotBossWeaponGrip : MonoBehaviour
{
    public Boss owner;
    public Transform leftHandGrip;
    [Range(0f, 1f)] public float weight = 1f;
    public Transform[] fingers;
    public Quaternion[] fingerRotations;
    private Animator _animator;
    private Transform _rightHand, _leftHand, _rightUpperArm, _rightLowerArm, _leftUpperArm, _leftLowerArm;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        if (owner == null) owner = GetComponentInParent<Boss>();
        CacheArms();
    }

    private bool CanGrip => owner != null && leftHandGrip != null && owner.isActiveAndEnabled
        && !owner.IsDead && !owner.IsTransitioning && !owner.IsGroggy && !owner.PendingGroggy
        && !owner.IsHurt && !owner.IsKnockbacking && UsesCombatPose();

    private bool UsesCombatPose()
    {
        if (_animator == null) return false;
        var state = _animator.GetCurrentAnimatorStateInfo(0);
        return !state.IsName("PhaseDown") && !state.IsName("PhaseRevive") && !state.IsName("Die")
            && !state.IsName("Groggy") && !state.IsName("Hit");
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (_animator == null) _animator = GetComponent<Animator>();
        if (_rightHand == null) CacheArms();
        float amount = CanGrip ? weight : 0f;
        _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, amount);
        _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, amount);
        _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, amount);
        _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, amount);
        if (amount <= 0f) return;

        // The imported robot has much broader shoulders than the source swordsman. A left-only
        // target can lie beyond that arm's reach. Share the correction between both hands while
        // keeping the sword's animated rotation, then constrain the pair to both arm lengths.
        Vector3 offset = leftHandGrip.position - _rightHand.position;
        Vector3 rightTarget = (_rightHand.position + _leftHand.position - offset) * .5f;
        float rightReach = (Vector3.Distance(_rightUpperArm.position, _rightLowerArm.position)
            + Vector3.Distance(_rightLowerArm.position, _rightHand.position)) * .97f;
        float leftReach = (Vector3.Distance(_leftUpperArm.position, _leftLowerArm.position)
            + Vector3.Distance(_leftLowerArm.position, _leftHand.position)) * .97f;
        for (int i = 0; i < 12; i++)
        {
            rightTarget = _rightUpperArm.position + Vector3.ClampMagnitude(rightTarget - _rightUpperArm.position, rightReach);
            Vector3 leftTarget = _leftUpperArm.position + Vector3.ClampMagnitude(rightTarget + offset - _leftUpperArm.position, leftReach);
            rightTarget = leftTarget - offset;
        }
        _animator.SetIKPosition(AvatarIKGoal.RightHand, rightTarget);
        _animator.SetIKRotation(AvatarIKGoal.RightHand, _rightHand.rotation);
        _animator.SetIKPosition(AvatarIKGoal.LeftHand, rightTarget + offset);
        _animator.SetIKRotation(AvatarIKGoal.LeftHand, leftHandGrip.rotation);
    }

    private void CacheArms()
    {
        _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
        _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
        _rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        _rightLowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        _leftUpperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        _leftLowerArm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
    }

    private void LateUpdate()
    {
        if (!CanGrip || fingers == null || fingerRotations == null) return;
        int count = Mathf.Min(fingers.Length, fingerRotations.Length);
        for (int i = 0; i < count; i++)
            if (fingers[i] != null) fingers[i].localRotation = fingerRotations[i];
    }
}
