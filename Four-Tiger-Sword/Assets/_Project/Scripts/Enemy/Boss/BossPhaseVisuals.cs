using System;
using RootMotion.FinalIK;
using UnityEngine;

[Serializable]
public sealed class BossPhaseVisualSet
{
    public GameObject root;
    public Animator animator;
    public Transform weapon;
    public TrailRenderer trail;
    public FullBodyBipedIK ik;
    public HitReaction reaction;
}

/// <summary>Changes the presentation at the revival boundary while keeping one combat actor.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Boss))]
public sealed class BossPhaseVisuals : MonoBehaviour
{
    public Boss owner;
    public BossPhaseVisualSet phaseOne = new BossPhaseVisualSet();
    public BossPhaseVisualSet phaseTwo = new BossPhaseVisualSet();
    public int ActiveVisualPhase { get; private set; } = -1;
    public Animator ActiveAnimator => ActiveVisualPhase == 1 ? phaseTwo.animator : phaseOne.animator;

    private void OnEnable()
    {
        if (owner == null) owner = GetComponent<Boss>();
        owner.OnPhaseReviveStarted += ApplyPhase;
        owner.OnPhaseChanged += PhaseChanged;
    }

    private void Start() => ApplyPhase(Mathf.Max(0, owner.PhaseIndex));
    private void PhaseChanged(int index, IBossPhase phase) => ApplyPhase(index);

    public void ApplyPhase(int phaseIndex)
    {
        int index = phaseIndex > 0 ? 1 : 0;
        var visual = index == 1 ? phaseTwo : phaseOne;
        if (owner == null || visual == null || visual.root == null || visual.animator == null) return;
        if (ActiveVisualPhase == index && owner.Animator == visual.animator) return;

        var additive = GetComponent<BossAdditiveHitReaction>();
        var finalIK = GetComponent<BossFinalIKHitReaction>();
        additive?.ClearReaction();
        finalIK?.ClearReaction();
        ClearTrail(phaseOne);
        ClearTrail(phaseTwo);

        phaseOne.root.SetActive(index == 0);
        phaseTwo.root.SetActive(index == 1);
        owner.BindAnimator(visual.animator);
        additive?.BindAnimator(visual.animator);
        finalIK?.BindRig(visual.ik, visual.reaction);
        var presentation = GetComponent<SwordRobotBossPresentation>();
        if (presentation != null) presentation.bladeTrail = visual.trail;
        GetComponent<EnemyHitFlash>()?.RefreshTargets(visual.root.transform);
        GetComponent<EnemyDeathFade>()?.BindVisualRoot(visual.root.transform);
        ActiveVisualPhase = index;
    }

    private static void ClearTrail(BossPhaseVisualSet visual)
    {
        if (visual?.trail == null) return;
        visual.trail.emitting = false;
        visual.trail.Clear();
    }

    private void OnDisable()
    {
        if (owner != null)
        {
            owner.OnPhaseReviveStarted -= ApplyPhase;
            owner.OnPhaseChanged -= PhaseChanged;
        }
        ClearTrail(phaseOne);
        ClearTrail(phaseTwo);
    }
}
