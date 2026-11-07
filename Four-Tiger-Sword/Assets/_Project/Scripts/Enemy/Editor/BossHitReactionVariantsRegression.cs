using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using RootMotion.FinalIK;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks both hit overlays against an undamaged copy running the same real attack.</summary>
public static class BossHitReactionVariantsRegression
{
    public const string Folder = "Assets/_Project/Generated/Bosses/HitReactionVariants";
    private const string Source = "Assets/_Project/Generated/Bosses/SwordRobotBoss/SwordRobotBoss.prefab";
    private static readonly string[] Paths = { Folder + "/SwordRobotBoss_FinalIK.prefab", Folder + "/SwordRobotBoss_Additive.prefab" };
    private static bool _running;
    private static Report _report = new Report { status = "idle" };

    [Serializable] private sealed class Report
    {
        public string status, error, directory, reportPath, contactSheet;
        public int checks;
        public List<VariantResult> variants = new List<VariantResult>();
        public List<string> screenshots = new List<string>();
    }

    [Serializable] private sealed class VariantResult
    {
        public string variant;
        public int sampledFrames;
        public float maxTorsoAngle, maxTorsoDisplacement, maxFootDeviation, maxRootDisplacement;
        public float maxGripError, baselineGripError, recoveredTorsoAngle, baseAnimationAdvance;
        public float heldTorsoAngle, heldTorsoDisplacement, heldBaseAnimationAdvance;
        public float nonArmoredTorsoAngle, nonArmoredTorsoDisplacement;
        public float initialMaxHp, initialHp, pooledHp, revivedMaxHp, revivedHp;
        public bool damageTriggered, attackPreserved, recovered, pooledReset, lethalIgnored;
        public bool nonArmoredAttackPreserved, postureBreakPreserved, heldChargePreserved, phaseTransitionPreserved;
    }

    [MenuItem("Tools/Studio Baekho/Enemy/Verify Boss Hit Reaction Variants")]
    private static void RunMenu() => Debug.Log(RunAuthoring());

    public static string RunAuthoring()
    {
        int before = _report.checks;
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        Check(source != null, "The source SwordRobotBoss prefab is missing.");
        var sourceAnimator = source.GetComponentInChildren<Animator>(true);
        var sourceController = sourceAnimator.runtimeAnimatorController as AnimatorController;
        Check(sourceController != null && sourceController.layers.Length == 1,
            "The source boss controller must retain its original single base layer.");
        Check(source.GetComponentInChildren<BossFinalIKHitReaction>(true) == null
            && source.GetComponentInChildren<BossAdditiveHitReaction>(true) == null,
            "The source boss must not acquire either comparison overlay.");
        Check(!source.GetComponent<ConfiguredBoss>().UseOverlayForOrdinaryHits,
            "The source boss must retain its ordinary hit-reaction policy.");
        Check(!source.GetComponent<ConfiguredBoss>().ResistDamageKnockback
            && Mathf.Approximately(source.GetComponent<ConfiguredBoss>().AdditionalPatternRecovery, 0f),
            "The source boss must retain its authored knockback and pattern recovery policy.");
        Check(Mathf.Approximately(source.GetComponent<ConfiguredBoss>().PhaseHealthMultiplier, 1f),
            "The source boss must retain its authored phase health.");
        Check(source.GetComponent<ConfiguredBoss>().Mobility != null && !source.GetComponent<ConfiguredBoss>().Mobility.enabled,
            "The source boss must keep optional mobility disabled.");
        var run = AssetDatabase.LoadAssetAtPath<RunDefinitionSO>("Assets/_Game/RunProgression/DefaultRun.asset");
        Check(run != null && run.chapters != null && run.chapters.Length > 0
            && run.chapters[0].stages != null && run.chapters[0].stages.Length > 0
            && (run.chapters[0].stages[0].bossPrefab == source
                || Paths.Contains(AssetDatabase.GetAssetPath(run.chapters[0].stages[0].bossPrefab))),
            "The current run must reference the source boss or one of its comparison variants.");

        foreach (string path in Paths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Check(prefab != null, "Missing comparison prefab: " + path);
            Check(prefab.GetComponentsInChildren<MonoBehaviour>(true).All(script => script != null),
                "A comparison prefab has a missing script: " + path);
            var owner = prefab.GetComponent<ConfiguredBoss>();
            var animator = prefab.GetComponentInChildren<Animator>(true);
            Check(owner != null && animator != null && animator.avatar != null && animator.avatar.isHuman
                && animator.avatar.isValid, "A comparison prefab has an invalid boss or humanoid rig: " + path);
            Check(owner.UseOverlayForOrdinaryHits,
                "The comparison boss must keep its current motion on ordinary hits outside attack armor: " + path);
            Check(owner.ResistDamageKnockback && Mathf.Approximately(owner.AdditionalPatternRecovery, .2f),
                "The comparison boss needs damage-knockback resistance and .2 extra pattern recovery: " + path);
            Check(Mathf.Approximately(owner.PhaseHealthMultiplier, .7f),
                "The comparison boss must use .7 of each authored phase's health: " + path);
            Check(owner.Mobility != null && owner.Mobility.enabled,
                "The comparison boss must enable its authored mobility settings: " + path);
            Check(owner.Data == source.GetComponent<ConfiguredBoss>().Data
                && owner.PhaseConfiguration.SequenceEqual(source.GetComponent<ConfiguredBoss>().PhaseConfiguration),
                "A comparison prefab unexpectedly changed combat configuration: " + path);
            Check(animator.GetComponent<SwordRobotBossWeaponGrip>() != null,
                "A comparison prefab lost the existing sword grip: " + path);
            if (path == Paths[0])
            {
                var feedback = prefab.GetComponentInChildren<BossFinalIKHitReaction>(true);
                var ik = prefab.GetComponentInChildren<FullBodyBipedIK>(true);
                string error = string.Empty;
                Check(feedback != null && ik != null && feedback.ik == ik && feedback.reaction != null
                    && feedback.reaction.ik == ik && feedback.hitCollider != null,
                    "The FinalIK comparison is missing its connected solver/reaction/collider.");
                Check(!ik.ReferencesError(ref error), "FinalIK rig references are invalid: " + error);
                Check(ik.references.leftFoot != null && ik.references.rightFoot != null
                    && ik.references.pelvis != null, "FinalIK must reference the pelvis and both feet.");
                var ownedController = animator.runtimeAnimatorController as AnimatorController;
                Check(ownedController != null && ownedController != sourceController && ownedController.layers.Length == 1
                    && sourceController.layers[0].stateMachine.states.All(sourceState => ownedController.layers[0].stateMachine.states
                        .Any(state => state.state.name == sourceState.state.name && SameMotion(state.state.motion, sourceState.state.motion))),
                    "The FinalIK version needs an owned base controller preserving the source animation states.");
                Check(prefab.GetComponentInChildren<BossAdditiveHitReaction>(true) == null,
                    "The FinalIK comparison unexpectedly contains the additive reaction.");
            }
            else
            {
                var feedback = prefab.GetComponentInChildren<BossAdditiveHitReaction>(true);
                var controller = animator.runtimeAnimatorController as AnimatorController;
                Check(feedback != null && feedback.animator == animator && controller != null
                    && controller != sourceController && controller.layers.Length == 2,
                    "The additive comparison needs its own connected controller with base and recoil layers.");
                var layer = controller.layers.SingleOrDefault(candidate => candidate.name == feedback.layerName);
                Check(layer != null && layer.blendingMode == AnimatorLayerBlendingMode.Additive
                    && layer.avatarMask != null && !layer.iKPass,
                    "The additive version requires a masked additive layer with no second hand-IK pass.");
                Check(!layer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root)
                    && !layer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg)
                    && !layer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg)
                    && layer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body),
                    "The additive mask must include the torso while excluding root and legs.");
                var state = layer.stateMachine.states.Select(child => child.state)
                    .FirstOrDefault(candidate => candidate.name == feedback.stateName);
                Check(state != null && state.motion is AnimationClip clip && clip.humanMotion && !clip.isLooping,
                    "The additive layer needs a finite humanoid recoil clip.");
                Check(state.timeParameterActive && state.timeParameter == feedback.timeParameterName
                    && controller.parameters.Any(parameter => parameter.name == feedback.timeParameterName
                        && parameter.type == AnimatorControllerParameterType.Float),
                    "The additive sample must use actor-clock motion time, including held charge poses.");
                Check(prefab.GetComponentInChildren<FullBodyBipedIK>(true) == null
                    && prefab.GetComponentInChildren<BossFinalIKHitReaction>(true) == null,
                    "The additive comparison unexpectedly contains FinalIK reaction components.");
            }
        }
        return "Boss hit reaction authoring: " + (_report.checks - before) + " checks passed.";
    }

    public static string StartPlayMode()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling || _running)
            throw new InvalidOperationException("Start hit reaction verification in idle Play Mode.");
        _running = true;
        _report = new Report { status = "running" };
        _report.directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BossHitReactionVerification",
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")));
        Directory.CreateDirectory(_report.directory);
        _report.reportPath = Path.Combine(_report.directory, "report.json");
        _ = RunRuntime();
        return Status();
    }

    public static string Status() => JsonUtility.ToJson(_report, true);

    private static async Task RunRuntime()
    {
        Fixture fixture = null;
        try
        {
            RunAuthoring();
            fixture = new Fixture();
            foreach (string path in Paths) await VerifyVariant(fixture, path);
            BuildContactSheet();
            _report.status = "passed";
        }
        catch (Exception exception)
        {
            _report.status = "failed";
            _report.error = exception.GetBaseException().Message;
            Debug.LogException(exception);
        }
        finally
        {
            try { if (fixture != null) await fixture.Dispose(); }
            catch (Exception exception) { _report.status = "failed"; _report.error = "Cleanup: " + exception.GetBaseException().Message; }
            _running = false;
            File.WriteAllText(_report.reportPath, Status());
        }
    }

    private static async Task VerifyVariant(Fixture fixture, string path)
    {
        var subject = fixture.Actor(path, 0f);
        var control = fixture.Actor(path, 9f);
        var result = new VariantResult { variant = Path.GetFileNameWithoutExtension(path) };
        _report.variants.Add(result);
        await Until(() => subject.Brain != null && control.Brain != null, "The comparison actors did not initialize.");
        result.initialMaxHp = subject.EnemyStat.MaxHp;
        result.initialHp = subject.EnemyStat.CurrentHp;
        float expectedInitialHp = subject.PhaseConfiguration[0].phaseMaxHp * .7f;
        Check(Mathf.Approximately(result.initialMaxHp, expectedInitialHp)
            && Mathf.Approximately(result.initialHp, expectedInitialHp),
            "The variant's initial phase did not initialize at .7 authored maximum/current HP.");
        await Frames(3);
        var attack = fixture.Attack(subject);
        var controlAttack = fixture.Attack(control);
        await PumpFrames(4, attack, controlAttack);
        Check(attack.HasBossArmor && controlAttack.HasBossArmor, "The verification attack did not acquire boss armor.");
        Check(subject.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash
            == control.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash, "The comparison base animations did not synchronize.");
        result.baselineGripError = GripError(subject);
        Capture(fixture, subject, result.variant + "_before");
        await PumpFrames(3, attack, controlAttack);
        float initialNormalizedTime = subject.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        Vector3 origin = subject.transform.position;
        Quaternion rotation = subject.transform.rotation;

        var hit = subject.TakeDamage(1f, poiseDamage: 0f);
        Check(hit.Applied && hit.HealthDamage > 0f, "The comparison did not receive actual HP damage.");
        await PumpFrames(1, attack, controlAttack);
        result.damageTriggered = IsReacting(subject);
        Check(result.damageTriggered, "Actual damage did not trigger the comparison overlay.");
        Check(!IsReacting(control), "An undamaged comparison actor reacted to another actor's damage.");
        bool peakCaptured = false;
        double deadline = Time.realtimeSinceStartupAsDouble + .65d;
        while (Time.realtimeSinceStartupAsDouble < deadline)
        {
            await PumpFrames(1, attack, controlAttack);
            result.sampledFrames++;
            Check(subject.CurrentAction == attack && attack.IsActive && !subject.IsHurt,
                "A cosmetic hit reaction interrupted the armored attack.");
            var state = subject.Animator.GetCurrentAnimatorStateInfo(0);
            Check(state.fullPathHash == control.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash,
                "A cosmetic hit reaction replaced the base animation state.");
            float angle = TorsoAngle(subject, control);
            float offset = BoneDifference(subject, control, HumanBodyBones.Chest);
            result.maxTorsoAngle = Mathf.Max(result.maxTorsoAngle, angle);
            result.maxTorsoDisplacement = Mathf.Max(result.maxTorsoDisplacement, offset);
            result.maxFootDeviation = Mathf.Max(result.maxFootDeviation,
                BoneDifference(subject, control, HumanBodyBones.LeftFoot), BoneDifference(subject, control, HumanBodyBones.RightFoot));
            result.maxRootDisplacement = Mathf.Max(result.maxRootDisplacement, Vector3.Distance(origin, subject.transform.position));
            result.maxGripError = Mathf.Max(result.maxGripError, GripError(subject));
            Check(Quaternion.Angle(rotation, subject.transform.rotation) < .05f, "Hit feedback rotated the gameplay root.");
            if (!peakCaptured && (angle > .3f || offset > .004f))
            {
                Capture(fixture, subject, result.variant + "_peak");
                peakCaptured = true;
            }
        }
        result.attackPreserved = subject.CurrentAction == attack && attack.IsActive && !subject.IsHurt;
        result.baseAnimationAdvance = subject.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime - initialNormalizedTime;
        result.recoveredTorsoAngle = TorsoAngle(subject, control);
        result.recovered = !IsReacting(subject);
        Check(result.maxTorsoAngle > .1f || result.maxTorsoDisplacement > .002f,
            "The hit event ran but did not visibly change the torso pose.");
        Check(result.baseAnimationAdvance > .01f, "The base attack animation stopped advancing during hit feedback.");
        Check(result.maxRootDisplacement < .005f && result.maxFootDeviation < .04f,
            "Hit feedback displaced the gameplay root or leaked into the feet.");
        Check(result.maxGripError < Mathf.Max(.25f, result.baselineGripError + .1f),
            "Hit feedback pulled the left hand away from the sword grip.");
        Check(result.recovered && result.recoveredTorsoAngle < .5f,
            "The torso failed to return to the current underlying animation after the hit.");
        Capture(fixture, subject, result.variant + "_recovered");

        // Use the real charge action: it normally converts damage knockback into forced groggy.
        // Variant resistance must prevent that callback while retaining the held animation.
        var charge = fixture.Charge(subject);
        var controlCharge = fixture.Charge(control);
        double chargeDeadline = Time.realtimeSinceStartupAsDouble + 3d;
        while (subject.Animator.speed > 0f || control.Animator.speed > 0f)
        {
            if (Time.realtimeSinceStartupAsDouble > chargeDeadline)
                throw new InvalidOperationException("The actual boss charge did not reach its held pose.");
            await PumpFrames(1, charge, controlCharge);
        }
        await PumpFrames(2, charge, controlCharge);
        Check(charge.IsCharging && charge.AllowsKnockbackDuringBossArmor && subject.ActionDeltaTime > 0f,
            "The charge fixture must hold with its normal knockback-interruption callback enabled.");
        float heldNormalizedTime = subject.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        subject.TakeDamage(1f, power: subject.transform.forward * 5f, poiseDamage: 0f,
            staggerResistLevel: StaggerResistLevel.HIGH);
        Check(!subject.IsKnockbacking && !subject.PendingGroggy && !subject.IsGroggy,
            "Ordinary damage invoked the held charge's knockback-to-groggy interruption.");
        double heldDeadline = Time.realtimeSinceStartupAsDouble + .45d;
        while (Time.realtimeSinceStartupAsDouble < heldDeadline)
        {
            await PumpFrames(1, charge, controlCharge);
            result.heldTorsoAngle = Mathf.Max(result.heldTorsoAngle, TorsoAngle(subject, control));
            result.heldTorsoDisplacement = Mathf.Max(result.heldTorsoDisplacement,
                BoneDifference(subject, control, HumanBodyBones.Chest));
            Check(subject.CurrentAction == charge && charge.IsCharging && !subject.IsKnockbacking
                && !subject.IsHurt && !subject.IsGroggy && !subject.PendingGroggy,
                "A normal damaging hit interrupted the real held charge.");
        }
        result.heldBaseAnimationAdvance = Mathf.Abs(subject.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime - heldNormalizedTime);
        result.heldChargePreserved = subject.CurrentAction == charge && charge.IsCharging && !subject.PendingGroggy;
        Check(result.heldTorsoAngle > .1f || result.heldTorsoDisplacement > .002f,
            "Hit feedback did not animate over a held Animator.speed=0 charge pose.");
        Check(result.heldBaseAnimationAdvance < .001f && result.heldChargePreserved && !subject.IsHurt,
            "A hit reaction advanced or interrupted the held base attack pose.");
        Check(!IsReacting(subject), "A hit over a held pose did not finish.");

        // Ordinary hits also retain the current animation outside the boss's temporary armor.
        attack = fixture.Attack(subject, false, true); controlAttack = fixture.Attack(control, false, true);
        double activeDeadline = Time.realtimeSinceStartupAsDouble + 2d;
        while (attack.BossStage != BossAttackStage.Active || controlAttack.BossStage != BossAttackStage.Active)
        {
            if (Time.realtimeSinceStartupAsDouble > activeDeadline)
                throw new InvalidOperationException("The ordinary-hit fixture failed to reach its actual hit window.");
            await PumpFrames(1, attack, controlAttack);
        }
        await PumpFrames(2, attack, controlAttack);
        Check(!attack.HasBossArmor && !controlAttack.HasBossArmor,
            "The ordinary-hit fixture must run without temporary attack armor.");
        Check(attack.BossStage == BossAttackStage.Active && controlAttack.BossStage == BossAttackStage.Active,
            "The powered ordinary hit must be delivered inside an actual active hit window.");
        subject.TakeDamage(1f, power: subject.transform.forward * 5f, poiseDamage: 0f,
            staggerResistLevel: StaggerResistLevel.HIGH);
        await PumpFrames(1, attack, controlAttack);
        Check(IsReacting(subject) && !subject.IsHurt && !subject.IsKnockbacking
            && subject.CurrentAction == attack && attack.IsActive,
            "A high-stagger damaging hit replaced or knocked back the current unarmored attack.");
        double ordinaryDeadline = Time.realtimeSinceStartupAsDouble + .42d;
        while (Time.realtimeSinceStartupAsDouble < ordinaryDeadline)
        {
            await PumpFrames(1, attack, controlAttack);
            result.nonArmoredTorsoAngle = Mathf.Max(result.nonArmoredTorsoAngle, TorsoAngle(subject, control));
            result.nonArmoredTorsoDisplacement = Mathf.Max(result.nonArmoredTorsoDisplacement,
                BoneDifference(subject, control, HumanBodyBones.Chest));
            Check(subject.CurrentAction == attack && attack.IsActive && !subject.IsHurt && !subject.IsKnockbacking
                && Vector3.Distance(origin, subject.transform.position) < .005f
                && subject.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash
                    == control.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash,
                "An ordinary hit interrupted the current unarmored base animation.");
        }
        result.nonArmoredAttackPreserved = subject.CurrentAction == attack && attack.IsActive && !subject.IsHurt;
        Check(result.nonArmoredTorsoAngle > .1f || result.nonArmoredTorsoDisplacement > .002f,
            "Ordinary damage outside attack armor did not animate the torso overlay.");

        // Several impacts must remain bounded and must clear when an encounter actor is pooled.
        subject.TakeDamage(1f, poiseDamage: 0f);
        await PumpFrames(2, attack, controlAttack);
        subject.TakeDamage(1f, poiseDamage: 0f);
        await PumpFrames(1, attack, controlAttack);
        Check(IsReacting(subject), "Repeated damage failed to refresh the reaction.");
        float hpBeforePooling = subject.EnemyStat.CurrentHp;
        float maxHpBeforePooling = subject.EnemyStat.MaxHp;
        subject.gameObject.SetActive(false);
        result.pooledReset = !IsReacting(subject);
        Check(result.pooledReset, "Disabling the actor retained a hit reaction.");
        subject.gameObject.SetActive(true);
        await Frames(2);
        Check(!IsReacting(subject), "Re-enabling the actor replayed a stale hit reaction.");
        result.pooledHp = subject.EnemyStat.CurrentHp;
        Check(Mathf.Approximately(result.pooledHp, hpBeforePooling)
            && Mathf.Approximately(subject.EnemyStat.MaxHp, maxHpBeforePooling),
            "Re-enabling the variant healed it or compounded its health multiplier.");
        attack = fixture.Attack(subject);
        await PumpFrames(2, attack);
        subject.TakeDamage(0f, poiseDamage: 0f);
        Check(!IsReacting(subject), "A zero-damage hit triggered a visual recoil.");
        subject.TakeDamage(1f, poiseDamage: 0f, staggerResistLevel: StaggerResistLevel.HIGH);
        await PumpFrames(1, attack);
        Check(IsReacting(subject), "The posture-break fixture did not begin with an active overlay.");
        subject.TakeDamage(1f, power: subject.transform.forward * 5f, poiseDamage: 1000000f,
            staggerResistLevel: StaggerResistLevel.HIGH);
        await Frames(3);
        result.postureBreakPreserved = subject.IsGroggy && !IsReacting(subject)
            && subject.GetComponent<PoiseHandler>().IsPoiseBreaking
            && (subject.Animator.GetCurrentAnimatorStateInfo(0).IsName("Groggy")
                || subject.Animator.GetNextAnimatorStateInfo(0).IsName("Groggy"));
        Check(result.postureBreakPreserved,
            "The ordinary-hit overlay policy suppressed posture break or left the overlay on the groggy pose.");
        var lethal = subject.TakeDamage(10000000f, poiseDamage: 0f);
        await Frames(2);
        result.lethalIgnored = lethal.Killed && subject.IsDead && !IsReacting(subject);
        Check(result.lethalIgnored, "Final death retained or started a cosmetic hit reaction.");

        var phaseActor = fixture.Actor(path, 18f, true);
        await Until(() => phaseActor.Brain != null, "The two-phase comparison actor did not initialize.");
        var phaseAttack = fixture.Attack(phaseActor, false);
        await PumpFrames(2, phaseAttack);
        phaseActor.TakeDamage(1f, power: Vector3.forward * 5f, poiseDamage: 0f,
            staggerResistLevel: StaggerResistLevel.HIGH);
        await PumpFrames(1, phaseAttack);
        Check(IsReacting(phaseActor), "The phase-transition fixture did not begin with a hit overlay.");
        var depletion = phaseActor.TakeDamage(10000000f, poiseDamage: 0f);
        await Frames(3);
        result.phaseTransitionPreserved = depletion.Applied && !depletion.Killed && !phaseActor.IsDead
            && phaseActor.IsTransitioning && !IsReacting(phaseActor) && !phaseAttack.IsActive;
        Check(result.phaseTransitionPreserved,
            "The overlay policy prevented phase depletion from cancelling attacks and entering revival.");
        phaseActor.SetCombatTarget(null);
        phaseActor.CombatLabPassive = false;
        await Until(() => !phaseActor.IsTransitioning && phaseActor.PhaseIndex == 1,
            "The health-scaled variant did not naturally complete its phase revival.");
        result.revivedMaxHp = phaseActor.EnemyStat.MaxHp;
        result.revivedHp = phaseActor.EnemyStat.CurrentHp;
        float expectedRevivedHp = phaseActor.PhaseConfiguration[1].phaseMaxHp * .7f;
        Check(Mathf.Approximately(result.revivedMaxHp, expectedRevivedHp)
            && Mathf.Approximately(result.revivedHp, expectedRevivedHp),
            "The variant's revived phase did not restore exactly .7 of that phase's authored HP.");
        phaseActor.gameObject.SetActive(false);
        Object.Destroy(phaseActor.gameObject);
        Clear(subject); Clear(control);
        // Normal death cleanup can destroy the first actor while the other actor revives.
        if (subject != null) { subject.CancelCurrentAction(); Object.Destroy(subject.gameObject); }
        if (control != null) { control.CancelCurrentAction(); Object.Destroy(control.gameObject); }
        await Frames(2);
    }

    private static bool IsReacting(Boss owner)
    {
        var finalIK = owner.GetComponentInChildren<BossFinalIKHitReaction>(true);
        return finalIK != null ? finalIK.IsReacting : owner.GetComponentInChildren<BossAdditiveHitReaction>(true).IsReacting;
    }

    private static bool SameMotion(Motion actual, Motion source)
    {
        if (actual == source) return true;
        if (!(actual is BlendTree copied) || !(source is BlendTree original)
            || copied.blendType != original.blendType || copied.blendParameter != original.blendParameter
            || copied.blendParameterY != original.blendParameterY || copied.children.Length != original.children.Length) return false;
        var a = copied.children; var b = original.children;
        for (int i = 0; i < a.Length; i++)
            if (!SameMotion(a[i].motion, b[i].motion) || a[i].threshold != b[i].threshold
                || a[i].position != b[i].position || a[i].timeScale != b[i].timeScale
                || a[i].cycleOffset != b[i].cycleOffset || a[i].mirror != b[i].mirror
                || a[i].directBlendParameter != b[i].directBlendParameter) return false;
        return true;
    }

    private static void Clear(Boss owner)
    {
        if (owner == null) return;
        owner.GetComponentInChildren<BossFinalIKHitReaction>(true)?.ClearReaction();
        owner.GetComponentInChildren<BossAdditiveHitReaction>(true)?.ClearReaction();
    }

    private static float TorsoAngle(Boss subject, Boss control)
    {
        float maximum = 0f;
        foreach (var bone in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
        {
            var a = subject.Animator.GetBoneTransform(bone);
            var b = control.Animator.GetBoneTransform(bone);
            if (a != null && b != null) maximum = Mathf.Max(maximum, Quaternion.Angle(
                Quaternion.Inverse(subject.transform.rotation) * a.rotation,
                Quaternion.Inverse(control.transform.rotation) * b.rotation));
        }
        return maximum;
    }

    private static float BoneDifference(Boss subject, Boss control, HumanBodyBones bone)
    {
        var a = subject.Animator.GetBoneTransform(bone);
        var b = control.Animator.GetBoneTransform(bone);
        if (a == null || b == null) return 0f;
        return Vector3.Distance(subject.transform.InverseTransformPoint(a.position), control.transform.InverseTransformPoint(b.position));
    }

    private static float GripError(Boss owner)
    {
        var grip = owner.Animator.GetComponent<SwordRobotBossWeaponGrip>();
        return grip != null && grip.leftHandGrip != null ? Vector3.Distance(
            owner.Animator.GetBoneTransform(HumanBodyBones.LeftHand).position, grip.leftHandGrip.position) : 0f;
    }

    private static void Capture(Fixture fixture, Boss owner, string label)
    {
        var camera = fixture.Camera;
        camera.transform.position = owner.transform.position + new Vector3(4.2f, 3.3f, 5.8f);
        camera.transform.LookAt(owner.transform.position + Vector3.up * 1.55f);
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(480, 480, 24, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(480, 480, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 480, 480), 0, 0);
            texture.Apply();
            string path = Path.Combine(_report.directory, label + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            _report.screenshots.Add(path);
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.Destroy(texture);
        }
    }

    private static void BuildContactSheet()
    {
        if (_report.screenshots.Count != 6) return;
        var sheet = new Texture2D(1440, 960, TextureFormat.RGB24, false);
        try
        {
            for (int i = 0; i < 6; i++)
            {
                var tile = new Texture2D(2, 2, TextureFormat.RGB24, false);
                try
                {
                    tile.LoadImage(File.ReadAllBytes(_report.screenshots[i]));
                    sheet.SetPixels((i % 3) * 480, (1 - i / 3) * 480, 480, 480, tile.GetPixels());
                }
                finally { Object.Destroy(tile); }
            }
            sheet.Apply();
            _report.contactSheet = Path.Combine(_report.directory, "comparison-contact.png");
            File.WriteAllBytes(_report.contactSheet, sheet.EncodeToPNG());
        }
        finally { Object.Destroy(sheet); }
    }

    private static async Task PumpFrames(int count, params EnemyAction[] actions)
    {
        for (int i = 0; i < count; i++)
        {
            await Frames(1);
            foreach (var action in actions) if (action != null && action.IsActive && !action.IsFinished) action.Update();
        }
    }

    private static async Task Frames(int count)
    {
        int until = Time.frameCount + count;
        double deadline = Time.realtimeSinceStartupAsDouble + 15d;
        while (Time.frameCount < until)
        {
            if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > deadline)
                throw new InvalidOperationException("Play Mode frames stopped advancing during hit reaction verification.");
            await Task.Yield();
        }
        // Task.Yield resumes from UnitySynchronizationContext during Update, before Animator
        // evaluation and FinalIK's LateUpdate. Observe the rendered pose after all IK instead.
        await Awaitable.EndOfFrameAsync();
    }

    private static async Task Until(Func<bool> condition, string message)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 15d;
        while (!condition())
        {
            if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > deadline) throw new InvalidOperationException(message);
            await Task.Yield();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _report.checks++;
    }

    private static void Set(object target, string fieldName, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) { field.SetValue(target, value); return; }
        }
        throw new MissingFieldException(target.GetType().Name, fieldName);
    }

    private sealed class Fixture
    {
        private readonly Scene _scene;
        private readonly List<Object> _assets = new List<Object>();
        private readonly float _timeScale = Time.timeScale;
        private readonly UnityEngine.Random.State _random = UnityEngine.Random.state;
        private readonly Vector3 _origin = new Vector3(12000f, 0f, 12000f);
        private readonly Transform _target;
        public readonly Camera Camera;

        public Fixture()
        {
            Time.timeScale = 1f;
            _scene = SceneManager.CreateScene("BossHitReactionVerification-" + Guid.NewGuid().ToString("N"));
            var target = new GameObject("Hit reaction test target");
            SceneManager.MoveGameObjectToScene(target, _scene);
            target.transform.position = _origin + Vector3.forward * 4f;
            _target = target.transform;
            var cameraHost = new GameObject("Hit reaction verification camera");
            SceneManager.MoveGameObjectToScene(cameraHost, _scene);
            Camera = cameraHost.AddComponent<Camera>();
            Camera.enabled = false;
            Camera.fieldOfView = 37f;
            Camera.nearClipPlane = .05f;
            Camera.farClipPlane = 35f;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(.14f, .16f, .19f, 1f);
            var lightHost = new GameObject("Hit reaction verification light");
            SceneManager.MoveGameObjectToScene(lightHost, _scene);
            var light = lightHost.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
        }

        public Boss Actor(string path, float x, bool keepPhases = false)
        {
            var staging = new GameObject("Inactive hit reaction actor staging");
            staging.SetActive(false);
            SceneManager.MoveGameObjectToScene(staging, _scene);
            var host = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), staging.transform);
            host.SetActive(false);
            host.transform.SetParent(null);
            SceneManager.MoveGameObjectToScene(host, _scene);
            host.transform.SetPositionAndRotation(_origin + Vector3.right * x, Quaternion.identity);
            Object.Destroy(staging);
            var boss = host.GetComponent<ConfiguredBoss>();
            var data = Object.Instantiate(boss.Data);
            data.rewardGroup = null;
            data.initialShield = 0f;
            _assets.Add(data);
            Set(boss, "BossData", data);
            Set(host.GetComponent<EnemyStat>(), "baseData", data);
            // A one-pool fixture makes lethal cleanup observable without running encounter progression.
            if (!keepPhases) Set(boss, "PhaseDatas", new List<BossPhaseData> { boss.PhaseConfiguration[0] });
            boss.CombatLabPassive = true;
            boss.SetCombatTarget(_target);
            host.GetComponent<NavMeshAgent>().enabled = false;
            foreach (var animator in host.GetComponentsInChildren<Animator>(true)) animator.fireEvents = false;
            host.SetActive(true);
            return boss;
        }

        public EnemyAction Attack(Boss boss, bool armor = true, bool activeWindow = false)
        {
            boss.CancelCurrentAction();
            var source = boss.PhaseConfiguration[0].combatProfile.patterns
                .Where(option => option?.pattern is BossTacticalPatternSO)
                .SelectMany(option => ((BossTacticalPatternSO)option.pattern).steps)
                .Select(step => step?.skill).OfType<MonsterDefaultAttackSO>().First();
            var skill = Object.Instantiate(source);
            _assets.Add(skill);
            skill.attackSpeed = 1f;
            skill.isParryable = false;
            skill.damage = 0f;
            skill.hitStartTime = activeWindow ? .04f : 3f;
            skill.hitDuration = activeWindow ? 2f : .1f;
            skill.attackDuration = activeWindow ? 3f : 6f;
            var action = skill.CreateAction(boss);
            action.ConfigureBossPolicy(new BossAttackPolicy
                { enabled = true, turnSpeed = 0f, lockFraction = 0f, armorDuringStartup = armor, armorDuringActive = armor });
            boss.CurrentAction = action;
            action.Enter();
            return action;
        }

        public EnemyBossChargeAttack Charge(Boss boss)
        {
            boss.CancelCurrentAction();
            var source = boss.PhaseConfiguration[0].combatProfile.patterns
                .Where(option => option?.pattern is BossTacticalPatternSO)
                .SelectMany(option => ((BossTacticalPatternSO)option.pattern).steps)
                .Select(step => step?.skill).OfType<MonsterBossChargeAttackSO>().First();
            var skill = Object.Instantiate(source);
            _assets.Add(skill);
            skill.attackSpeed = 1f;
            skill.isParryable = false;
            skill.damage = 0f;
            skill.chargePoseTime = .04f;
            skill.chargeSeconds = 1.5f;
            skill.interruptOnKnockback = true;
            var action = (EnemyBossChargeAttack)skill.CreateAction(boss);
            action.ConfigureBossPolicy(new BossAttackPolicy
                { enabled = true, turnSpeed = 0f, lockFraction = 0f, armorDuringStartup = true, armorDuringActive = true });
            boss.CurrentAction = action;
            action.Enter();
            return action;
        }

        public async Task Dispose()
        {
            try
            {
                if (_scene.IsValid() && _scene.isLoaded)
                {
                    foreach (var root in _scene.GetRootGameObjects())
                        foreach (var boss in root.GetComponentsInChildren<Boss>(true))
                        { Clear(boss); boss.CancelCurrentAction(); }
                    var operation = SceneManager.UnloadSceneAsync(_scene);
                    while (operation != null && !operation.isDone) await Task.Yield();
                }
                foreach (var asset in _assets) if (asset != null) Object.Destroy(asset);
            }
            finally { Time.timeScale = _timeScale; UnityEngine.Random.state = _random; }
        }
    }
}
