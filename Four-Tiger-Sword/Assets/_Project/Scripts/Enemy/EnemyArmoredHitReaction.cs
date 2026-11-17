using UnityEngine;

/// <summary>Recoil layered over the attack pose without changing the Animator's states.</summary>
[DefaultExecutionOrder(-150)]
[DisallowMultipleComponent]
public sealed class EnemyArmoredHitReaction : MonoBehaviour
{
    private const float Duration = .28f;
    private const float MinimumRetriggerInterval = .045f;
    private const float RecoilDegrees = 20f;
    // A sharp impact and pronounced rebound settle over the continuing attack pose.
    private static readonly AnimationCurve Recoil = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(.028f, 1f), new Keyframe(.09f, -.5f),
        new Keyframe(.16f, .22f), new Keyframe(.225f, -.07f), new Keyframe(Duration, 0f));

    private Enemy _owner;
    private Animator _animator;
    private Avatar _avatar;
    private Transform _recoilBone;
    private Vector3 _worldTiltAxis;
    private Quaternion _animatedRotation, _appliedRotation;
    private float _elapsed, _strength;
    private bool _poseApplied;

    public bool IsReacting { get; private set; }

    internal static void Play(Enemy owner, Vector3 worldDirection, bool critical)
    {
        if (owner.Animator == null || owner.CurrentAction?.HasNormalAttackArmor != true) return;
        var reaction = owner.GetComponent<EnemyArmoredHitReaction>();
        if (reaction == null) reaction = owner.gameObject.AddComponent<EnemyArmoredHitReaction>();
        reaction.Trigger(owner, worldDirection, critical ? 1.2f : 1f);
    }

    private void Trigger(Enemy owner, Vector3 worldDirection, float strength)
    {
        _owner = owner;
        if (_animator != owner.Animator || _avatar != owner.Animator.avatar || _recoilBone == null)
        {
            ClearReaction();
            _animator = owner.Animator;
            _avatar = _animator.avatar;
            _recoilBone = FindVisualBone(_animator, owner.transform);
        }
        if (!CanReact) return;

        if (IsReacting && _elapsed < MinimumRetriggerInterval)
        {
            _strength = Mathf.Max(_strength, strength);
            return;
        }

        var up = owner.transform.up;
        Vector3 planarDirection = Vector3.ProjectOnPlane(worldDirection, up);
        if (!KnockbackMotion.IsFinite(planarDirection.sqrMagnitude) || planarDirection.sqrMagnitude < .0001f)
            planarDirection = -owner.transform.forward;
        _worldTiltAxis = Vector3.Cross(up, planarDirection.normalized);
        _elapsed = 0f;
        _strength = strength;
        IsReacting = true;
    }

    private bool CanReact => isActiveAndEnabled && _owner != null && _owner.isActiveAndEnabled
        && !_owner.IsDead && !_owner.IsGroggy && !_owner.PendingGroggy && _owner.EnemyStat?.CurrentHp > 0f
        && _owner.CurrentAction?.HasNormalAttackArmor == true
        && _animator != null && _animator == _owner.Animator && _animator.avatar == _avatar
        && _animator.isActiveAndEnabled && _recoilBone != null && _recoilBone.gameObject.activeInHierarchy;

    private void Update()
    {
        // Restore before the Animator and enemy actions read the rig again.
        RestorePose();
        if (!IsReacting) return;
        if (!CanReact)
        {
            ClearReaction();
            return;
        }
        _elapsed += Mathf.Max(0f, _owner.ActionDeltaTime);
        if (_elapsed >= Duration) ClearReaction();
    }

    private void LateUpdate()
    {
        if (!IsReacting) return;
        if (!CanReact)
        {
            ClearReaction();
            return;
        }

        var parent = _recoilBone.parent;
        Vector3 localAxis = parent.InverseTransformDirection(_worldTiltAxis);
        _animatedRotation = _recoilBone.localRotation;
        var tilt = Quaternion.AngleAxis(Recoil.Evaluate(_elapsed) * RecoilDegrees * _strength, localAxis);
        _recoilBone.localRotation = tilt * _animatedRotation;
        _appliedRotation = _recoilBone.localRotation;
        _poseApplied = true;
    }

    internal void ClearReaction()
    {
        RestorePose();
        IsReacting = false;
    }

    private void OnDisable() => ClearReaction();

    private void RestorePose()
    {
        if (!_poseApplied) return;
        // An animation or retargeting component may already have written a newer pose.
        if (_recoilBone != null && _recoilBone.localRotation == _appliedRotation)
            _recoilBone.localRotation = _animatedRotation;
        _poseApplied = false;
    }

    private static Transform FindVisualBone(Animator animator, Transform actor)
    {
        if (animator.avatar != null && animator.avatar.isValid && animator.isHuman)
        {
            Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (IsVisualOnly(chest, actor)) return chest;
            Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            if (IsVisualOnly(spine, actor)) return spine;
        }

        // Imported generic rigs and drones often have no humanoid chest mapping.
        if (IsVisualOnly(animator.transform, actor)) return animator.transform;
        foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (IsVisualOnly(skin.rootBone, actor)) return skin.rootBone;
        foreach (var mesh in animator.GetComponentsInChildren<MeshRenderer>(true))
            if (IsVisualOnly(mesh.transform, actor)) return mesh.transform;
        return null;
    }

    private static bool IsVisualOnly(Transform candidate, Transform actor) => candidate != null
        && candidate != actor && candidate.IsChildOf(actor)
        && candidate.GetComponentInChildren<Collider>(true) == null
        && candidate.GetComponentInChildren<Rigidbody>(true) == null;
}
