using System;
using System.Collections.Generic;
using RootMotion.FinalIK;
using UnityEngine;

/// <summary>Visual recoil layered after animation; never requests stagger or changes boss actions.</summary>
[DefaultExecutionOrder(-150)]
[DisallowMultipleComponent]
public sealed class BossFinalIKHitReaction : MonoBehaviour
{
    public Boss owner;
    public FullBodyBipedIK ik;
    public HitReaction reaction;
    public Collider hitCollider;
    [Min(0f)] public float recoilDistance = .12f;
    [Min(.05f)] public float duration = .30f;
    [Min(1f)] public float criticalMultiplier = 1.25f;

    public bool IsReacting => _active;
    public int ReactionCount { get; private set; }

    private Boss _subscribedOwner;
    private Transform[] _bones = Array.Empty<Transform>();
    private Vector3[] _positions = Array.Empty<Vector3>();
    private Quaternion[] _rotations = Array.Empty<Quaternion>();
    private bool _poseStored, _active, _pendingHit;
    private float _finishTime, _pendingStrength, _lastStrength;
    private Vector3 _pendingDirection, _lastDirection;

    private void Awake()
    {
        if (owner == null) owner = GetComponent<Boss>();
        if (ik == null) ik = GetComponentInChildren<FullBodyBipedIK>(true);
        if (reaction == null && ik != null) reaction = ik.GetComponent<HitReaction>();
        if (hitCollider == null) hitCollider = GetComponent<Collider>();
        CacheBones();
    }

    private void OnEnable()
    {
        if (owner == null) owner = GetComponent<Boss>();
        _subscribedOwner = owner;
        if (_subscribedOwner != null) _subscribedOwner.OnDamaged += OnDamaged;
        if (ik != null) ik.solver.OnPreRead += PinAnimatedPose;
        ClearReaction();
    }

    private void OnDisable()
    {
        if (_subscribedOwner != null) _subscribedOwner.OnDamaged -= OnDamaged;
        _subscribedOwner = null;
        if (ik != null) ik.solver.OnPreRead -= PinAnimatedPose;
        ClearReaction();
    }

    /// <summary>Replaces both the solver and its pose cache when the boss changes visual rigs.</summary>
    public void BindRig(FullBodyBipedIK targetIK, HitReaction targetReaction)
    {
        ClearReaction();
        if (isActiveAndEnabled && ik != null) ik.solver.OnPreRead -= PinAnimatedPose;
        ik = targetIK;
        reaction = targetReaction;
        _bones = Array.Empty<Transform>();
        _positions = Array.Empty<Vector3>();
        _rotations = Array.Empty<Quaternion>();
        _poseStored = false;
        CacheBones();
        if (isActiveAndEnabled && ik != null) ik.solver.OnPreRead += PinAnimatedPose;
        ClearReaction();
    }

    private bool CanReact => owner != null && owner.isActiveAndEnabled && !owner.IsDead
        && owner.EnemyStat != null && owner.EnemyStat.CurrentHp > 0f
        && !owner.IsTransitioning && !owner.IsGroggy && !owner.PendingGroggy
        && ik != null && ik.enabled && reaction != null && hitCollider != null;

    private void OnDamaged(float damage, ElementType element, bool critical)
    {
        if (damage <= 0f || !CanReact) return;
        Vector3 direction = owner.CombatTarget != null
            ? transform.position - owner.CombatTarget.position : -transform.forward;
        Trigger(direction, critical ? criticalMultiplier : 1f);
    }

    /// <summary>Queues one bounded pulse. Direction is the world direction away from the impact.</summary>
    public void Trigger(Vector3 worldDirection, float strength = 1f)
    {
        if (!isActiveAndEnabled || !CanReact || !IsFinite(worldDirection)
            || float.IsNaN(strength) || float.IsInfinity(strength) || strength <= 0f) return;
        worldDirection = Vector3.ProjectOnPlane(worldDirection, transform.up);
        if (worldDirection.sqrMagnitude < .0001f) worldDirection = -transform.forward;
        _pendingDirection = worldDirection.normalized;
        _pendingStrength = Mathf.Clamp(strength, .1f, 2f);
        _pendingHit = true;
    }

    [ContextMenu("Preview Hit (Play Mode)")]
    public void PreviewHit() => Trigger(-transform.forward);

    private void Update()
    {
        // Undo only our last solve before Animator samples again. This also prevents accumulating
        // offsets when the actor's Animator is locally stopped while world time keeps advancing.
        RestoreAnimatedPose();
        if (!CanReact) { ClearReaction(); return; }
        if (owner.ActionDeltaTime <= 0f)
        {
            // FinalIK's stock HitReaction uses private Time.time timers. Restart its short visual
            // pulse after a local hitstop rather than letting the entire reaction expire unseen.
            if (_active)
            {
                if (!_pendingHit)
                {
                    _pendingDirection = _lastDirection;
                    _pendingStrength = _lastStrength;
                    _pendingHit = true;
                }
                StopPulse();
            }
            return;
        }
        if (_pendingHit)
        {
            Vector3 direction = _pendingDirection;
            float strength = _pendingStrength;
            _pendingHit = false;
            StartPulse(direction, strength);
        }
        if (_active && Time.time >= _finishTime) StopPulse();
    }

    private void StartPulse(Vector3 direction, float strength)
    {
        if (!_active) RebuildHitPoints();
        _lastDirection = direction;
        _lastStrength = strength;
        reaction.enabled = true;
        reaction.weight = 1f;
        ik.solver.IKPositionWeight = 1f;
        reaction.Hit(hitCollider, direction * recoilDistance * strength, hitCollider.bounds.center);
        _finishTime = Time.time + Mathf.Max(.05f, duration);
        _active = true;
        ReactionCount++;
    }

    public void ClearReaction()
    {
        _pendingHit = false;
        StopPulse();
    }

    private void StopPulse()
    {
        bool hadPulse = _active || reaction != null && reaction.weight > 0f;
        RestoreAnimatedPose();
        _active = false;
        if (reaction != null) reaction.weight = 0f;
        if (ik != null)
        {
            ik.solver.IKPositionWeight = 0f;
            foreach (var effector in ik.solver.effectors) effector.positionOffset = Vector3.zero;
        }
        // HitReaction has no reset API; fresh hit-point instances clear its private blend/timer state.
        if (hadPulse) RebuildHitPoints();
    }

    private void RebuildHitPoints()
    {
        if (reaction == null || ik == null || hitCollider == null) return;
        reaction.effectorHitPoints = new[] { CreateUpperBodyHitPoint(hitCollider, duration) };
        reaction.boneHitPoints = Array.Empty<HitReaction.HitPointBone>();
        // Prime FinalIK's per-point wall-clock timestamp before its first Hit (otherwise a fresh
        // point created late in a session advances by the whole session time on its first Apply).
        reaction.effectorHitPoints[0].Apply(ik.solver, 0f);
    }

    public static HitReaction.HitPointEffector CreateUpperBodyHitPoint(Collider collider, float seconds)
    {
        seconds = Mathf.Max(.05f, seconds);
        return new HitReaction.HitPointEffector
        {
            name = "Upper body recoil",
            collider = collider,
            offsetInForceDirection = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(seconds * .2f, 1f),
                new Keyframe(seconds * .46f, -.32f), new Keyframe(seconds * .72f, .12f),
                new Keyframe(seconds, 0f)),
            offsetInUpDirection = AnimationCurve.Linear(0f, 0f, seconds, 0f),
            effectorLinks = new[]
            {
                Link(FullBodyBipedEffector.LeftShoulder, .65f),
                Link(FullBodyBipedEffector.RightShoulder, .65f),
                Link(FullBodyBipedEffector.LeftHand, 1f),
                Link(FullBodyBipedEffector.RightHand, 1f)
            }
        };
    }

    private static HitReaction.HitPointEffector.EffectorLink Link(FullBodyBipedEffector effector, float weight)
        => new HitReaction.HitPointEffector.EffectorLink { effector = effector, weight = weight };

    private void PinAnimatedPose()
    {
        if (!_active) return;
        if (_bones.Length == 0) CacheBones();
        for (int i = 0; i < _bones.Length; i++)
        {
            _positions[i] = _bones[i].localPosition;
            _rotations[i] = _bones[i].localRotation;
        }
        _poseStored = true;
        // Targets follow the current animation each frame, so attacks and locomotion keep playing.
        // Identical wrist offsets preserve the authored two-hand sword grip as far as limb reach allows.
        foreach (var effector in ik.solver.effectors)
        {
            // FinalIK blends toward effector.position at positionWeight=1; positionOffset alone
            // only changes its animated starting point and would be canceled by a pinned target.
            // Include that same offset in the target so both wrists retain their relative grip.
            effector.PinToBone(1f, effector.rotationWeight);
            effector.position += effector.positionOffset;
        }
    }

    private void CacheBones()
    {
        if (ik == null || ik.references == null || !ik.references.isFilled) return;
        var r = ik.references;
        var bones = new HashSet<Transform>(r.spine)
        {
            r.pelvis, r.head, r.leftUpperArm, r.leftForearm, r.leftHand,
            r.rightUpperArm, r.rightForearm, r.rightHand, r.leftThigh, r.leftCalf,
            r.leftFoot, r.rightThigh, r.rightCalf, r.rightFoot
        };
        foreach (var mapping in ik.solver.limbMappings)
            if (mapping.parentBone != null) bones.Add(mapping.parentBone);
        bones.Remove(null);
        _bones = new Transform[bones.Count];
        bones.CopyTo(_bones);
        _positions = new Vector3[_bones.Length];
        _rotations = new Quaternion[_bones.Length];
    }

    private void RestoreAnimatedPose()
    {
        if (!_poseStored) return;
        for (int i = 0; i < _bones.Length; i++)
        {
            if (_bones[i] == null) continue;
            _bones[i].localPosition = _positions[i];
            _bones[i].localRotation = _rotations[i];
        }
        _poseStored = false;
    }

    private static bool IsFinite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y)
        && !float.IsNaN(value.z) && !float.IsInfinity(value.x) && !float.IsInfinity(value.y)
        && !float.IsInfinity(value.z);
}
