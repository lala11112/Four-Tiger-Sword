using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Asset-free authoring and live combat checks for opt-in boss tactics.</summary>
public static class BossTacticalRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool _running;
    private static int _checks;
    private static string _status = "idle";

    [MenuItem("Tools/Studio Baekho/Enemy/Run Boss Tactical Authoring Regression")]
    private static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run boss tactical authoring checks in Edit Mode.");
        _checks = 0;
        var assets = new List<Object>();
        try
        {
            var skill = Asset<MonsterDefaultAttackSO>(assets);
            var pattern = Asset<BossTacticalPatternSO>(assets);
            pattern.steps.Add(new BossTacticalStep { kind = BossTacticalStepKind.Attack, skill = skill });
            var profile = Asset<BossCombatProfileSO>(assets);
            profile.patterns.Add(new BossPatternOption { pattern = pattern });
            Check(!pattern.Validate().Any() && !profile.Validate().Any(),
                "A basic attack pattern and its combat profile must be valid.");
            var customSkill = Asset<BossTacticalProbeSkill>(assets);
            var customPolicy = new BossAttackPolicy { enabled = true };
            Check(customPolicy.Validate(customSkill).Any(),
                "A custom skill must explicitly opt in before accepting a boss execution policy.");
            customSkill.supportsBossPolicy = true;
            var customAction = customSkill.CreateAction(null);
            Check(!customPolicy.Validate(customSkill).Any() && customAction.SupportsBossPolicy,
                "A custom skill and action implementing the policy hooks must support the same authored boss policy.");
            customAction.ConfigureBossPolicy(customPolicy);

            var combo = Asset<MonsterComboAttackSO>(assets);
            combo.attackDuration = 1f;
            var stride = new EnemyHitEvent
            {
                animationStartTime = .1f, hitStartTime = .5f, hitDuration = .15f,
                stepDistance = .5f, stepStartTime = .3f, stepDuration = .3f
            };
            combo.hits = new List<EnemyHitEvent> { stride };
            Check(!BossAttackPolicy.ValidateSkill(combo).Any(),
                "A finite forward step inside its swing and hit window must be valid.");
            foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
            {
                stride.stepDistance = invalid;
                Check(BossAttackPolicy.ValidateSkill(combo).Any(),
                    "Negative or nonfinite combo step distances must be rejected.");
            }
            stride.stepDistance = .5f;
            stride.stepDuration = 0f;
            Check(BossAttackPolicy.ValidateSkill(combo).Any(),
                "An enabled forward step must have a positive duration.");
            stride.stepDuration = .3f;
            stride.stepStartTime = .5f;
            Check(BossAttackPolicy.ValidateSkill(combo).Any(),
                "A forward step must not extend beyond its strike's hit window.");
            stride.stepStartTime = 0f;
            Check(BossAttackPolicy.ValidateSkill(combo).Any(),
                "A forward step must not begin before its swing animation.");
            stride.stepDistance = 0f; stride.stepDuration = 0f;
            Check(!BossAttackPolicy.ValidateSkill(combo).Any(),
                "A disabled forward step must keep existing stationary combos valid.");

            profile.decisionInterval = float.NaN;
            Check(profile.Validate().Any(), "Nonfinite decision timing must be rejected.");
            profile.decisionInterval = .2f;
            profile.preferredMinDistance = 5f;
            profile.preferredMaxDistance = 2f;
            Check(profile.Validate().Any(), "Reversed preferred distances must be rejected.");
            profile.preferredMinDistance = 2f;
            profile.preferredMaxDistance = 4f;
            Check(profile.repositionDuration == 0f,
                "Existing profiles must keep repositioning disabled unless explicitly authored.");
            foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
            {
                profile.repositionDuration = invalid;
                Check(profile.Validate().Any(), "Negative or nonfinite reposition timing must be rejected.");
            }
            profile.repositionDuration = 0f;
            pattern.steps[0].skill = null;
            Check(pattern.Validate().Any(), "An attack without a skill must be rejected.");
            pattern.steps[0].skill = skill;
            pattern.steps[0].next = 99;
            Check(pattern.Validate().Any(), "A branch or continuation outside the step list must be rejected.");
            pattern.steps[0].next = BossTacticalPatternSO.Next;
            pattern.steps[0].attackPolicy = new BossAttackPolicy { enabled = true, lockFraction = float.PositiveInfinity };
            Check(pattern.Validate().Any(), "A nonfinite aiming policy must be rejected.");

            pattern.steps.Clear();
            pattern.steps.Add(new BossTacticalStep
            {
                kind = BossTacticalStepKind.Branch, nextIfTrue = 0, nextIfFalse = 0
            });
            Check(pattern.Validate().Any(), "A branch-only cycle must be reported before combat.");

            pattern.steps.Clear();
            pattern.steps.Add(new BossTacticalStep { kind = BossTacticalStepKind.Wait, seconds = .1f });
            pattern.steps.Add(new BossTacticalStep { kind = BossTacticalStepKind.Recover, seconds = .2f });
            var first = pattern.CreateRuntime(null);
            var second = pattern.CreateRuntime(null);
            first.Enter(); second.Enter();
            Check(first.TryGetNextStep(out var a) && a.Kind == BossPatternStepKind.Wait,
                "The first runtime must begin with its own initial wait.");
            Check(first.TryGetNextStep(out var b) && b.Kind == BossPatternStepKind.Recover,
                "The first runtime must advance to its own recovery.");
            Check(second.TryGetNextStep(out var c) && c.Kind == BossPatternStepKind.Wait,
                "Advancing one boss must not advance another boss using the same asset.");
            Check(!first.TryGetNextStep(out _), "A non-looping tactical runtime must finish.");
            first.Exit(); second.Exit();

            var target = new GameObject("Boss tactical condition probe");
            assets.Add(target);
            var condition = new BossCombatCondition
            {
                minimumDistance = 2f, maximumDistance = 4f,
                minimumAngle = 120f, maximumAngle = 180f,
                minimumHpPercent = .25f, maximumHpPercent = .75f,
                visibility = BossVisibilityRequirement.Visible
            };
            Check(condition.Matches(new BossCombatContext(null, target.transform, 3f, 160f, true, .5f)),
                "Distance, rear angle, visibility and HP conditions must combine.");
            Check(!condition.Matches(new BossCombatContext(null, target.transform, 3f, 0f, true, .5f))
                && !condition.Matches(new BossCombatContext(null, target.transform, 7f, 160f, true, .5f))
                && !condition.Matches(new BossCombatContext(null, target.transform, 3f, 160f, false, .5f))
                && !condition.Matches(new BossCombatContext(null, target.transform, 3f, 160f, true, 1f)),
                "A failed condition must exclude the pattern.");
            condition.visibility = BossVisibilityRequirement.Occluded;
            Check(condition.Matches(new BossCombatContext(null, target.transform, 3f, 160f, false, .5f)),
                "An occlusion condition must be independent of the boss field of view.");
        }
        finally
        {
            foreach (var asset in assets) if (asset != null) Object.DestroyImmediate(asset);
        }
        return $"Boss tactical authoring: {_checks} checks passed.";
    }

    public static string StartPlayMode()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Start the boss tactical regression in idle Play Mode.");
        if (_running) throw new InvalidOperationException("Boss tactical regression is already running.");
        _running = true; _checks = 0; _status = "running";
        _ = RunRuntime();
        return PlayModeStatus();
    }

    public static string PlayModeStatus() => JsonUtility.ToJson(new Result { status = _status, checks = _checks });
    [Serializable] private sealed class Result { public string status; public int checks; }

    private static async Task RunRuntime()
    {
        Fixture fixture = null;
        string outcome = "passed";
        try
        {
            fixture = new Fixture();
            await VerifyCombatTargetAndCommitment(fixture);
            await VerifyBranchBoundariesAndSharedProfile(fixture);
            await VerifyRangePositioningAndRepeatFallback(fixture);
            await VerifyCooldownAndRecovery(fixture);
            await VerifyRecoveryRepositioning(fixture);
            await VerifyFailedMovement(fixture);
            await VerifyNoPolicyCallbackCancellation(fixture);
            await VerifyTacticalActorClock(fixture);
            await VerifyAimArmorAndParry(fixture);
            await VerifyPhaseAndDisableCleanup(fixture);
        }
        catch (Exception exception)
        {
            outcome = "failed: " + exception.GetBaseException().Message;
            Debug.LogException(exception);
        }
        finally
        {
            try { if (fixture != null) await fixture.DisposeAsync(); }
            catch (Exception exception)
            {
                outcome = "failed during fixture cleanup: " + exception.GetBaseException().Message;
                Debug.LogException(exception);
            }
            finally { _status = outcome; _running = false; }
        }
    }

    private static async Task VerifyCombatTargetAndCommitment(Fixture f)
    {
        var near = f.Probe();
        var far = f.Probe();
        var profile = f.Profile();
        profile.patterns.Add(f.Option(f.Pattern(near), new BossCombatCondition { maximumDistance = 3f }));
        profile.patterns.Add(f.Option(f.Pattern(far), new BossCombatCondition { minimumDistance = 5f }));
        var boss = f.Actor(profile);
        f.Target.position = f.Origin + Vector3.forward * 2f;
        await Until(() => near.Actions.Any(a => a.IsActive), "A boss without a sensor did not engage its assigned target.");
        Check(boss.GetComponent<IEnemySensor>() == null && boss.DetectedTarget == f.Target,
            "Tactical bosses must retain a combat target without a search sensor.");
        var committed = near.Actions.Last();
        f.Target.position = f.Origin + Vector3.back * 8f;
        await Delay(.08f);
        Check(boss.CurrentAction == committed && !far.Actions.Any(a => a.IsActive),
            "Distance or rear position changes must not replace a committed attack.");
        committed.Complete();
        await Until(() => far.Actions.Any(a => a.IsActive), "The boss did not choose its far pattern after the safe attack boundary.");
        Check(boss.DetectedTarget == f.Target, "Moving behind the boss must not return it to target exploration.");
        var lostTargetAction = boss.CurrentAction;
        f.Target.gameObject.SetActive(false);
        await Until(() => boss.CurrentAction == null, "An unavailable combat target must stop the current pattern.");
        Check(!lostTargetAction.IsActive && !ParryEventBus.IsTelegraphActive(lostTargetAction),
            "Losing a combat participant must clear its active attack and telegraph.");
        f.Target.gameObject.SetActive(true);
        boss.gameObject.SetActive(false);

        var rear = f.Probe();
        var rearProfile = f.Profile(); rearProfile.turnSpeed = 0f;
        rearProfile.patterns.Add(f.Option(f.Pattern(rear), new BossCombatCondition { minimumAngle = 150f }));
        f.Target.position = f.Origin + Vector3.forward * 2f;
        var rearBoss = f.Actor(rearProfile);
        await Until(() => rearBoss.Brain != null, "The rear-response fixture did not initialize.");
        await Delay(.06f);
        Check(rear.Actions.Count == 0, "A rear-only pattern must not execute against a frontal target.");
        f.Target.position = f.Origin + Vector3.back * 2f;
        await Until(() => rear.Actions.Any(a => a.IsActive), "A rear-only pattern did not respond to the target behind the boss.");
        rearBoss.gameObject.SetActive(false);
    }

    private static async Task VerifyBranchBoundariesAndSharedProfile(Fixture f)
    {
        var opening = f.Probe(); var near = f.Probe(); var far = f.Probe();
        var pattern = f.Pattern(opening);
        pattern.steps.Add(new BossTacticalStep
        {
            kind = BossTacticalStepKind.Branch,
            condition = new BossCombatCondition { minimumDistance = 5f },
            nextIfTrue = 2, nextIfFalse = 3
        });
        pattern.steps.Add(new BossTacticalStep
        { kind = BossTacticalStepKind.Attack, skill = far, next = BossTacticalPatternSO.End });
        pattern.steps.Add(new BossTacticalStep
        { kind = BossTacticalStepKind.Attack, skill = near, next = BossTacticalPatternSO.End });
        var profile = f.Profile(); profile.patterns.Add(f.Option(pattern));
        f.Target.position = f.Origin + Vector3.forward * 2f;
        var boss = f.Actor(profile);
        await Until(() => opening.Actions.Any(a => a.IsActive), "The conditional chain did not start.");
        var attack = opening.Actions.Last();
        f.Target.position = f.Origin + Vector3.forward * 8f;
        await Delay(.05f);
        Check(far.Actions.Count == 0 && near.Actions.Count == 0 && boss.CurrentAction == attack,
            "A branch must not be evaluated while the preceding attack is active.");
        attack.Complete();
        await Until(() => far.Actions.Any(a => a.IsActive),
            "A branch must use the current combat situation at the completed-step boundary.");
        Check(near.Actions.Count == 0, "A branch must not retain the distance sampled when the chain began.");
        boss.gameObject.SetActive(false);

        var sharedSkill = f.Probe(); sharedSkill.cooldown = 20f;
        var sharedProfile = f.Profile();
        var option = f.Option(f.Pattern(sharedSkill)); option.cooldown = 20f;
        sharedProfile.patterns.Add(option);
        f.Target.position = f.Origin + Vector3.forward * 2f;
        var first = f.Actor(sharedProfile); var second = f.Actor(sharedProfile);
        await Until(() => sharedSkill.Actions.Any(a => a.Owner == first && a.IsActive)
            && sharedSkill.Actions.Any(a => a.Owner == second && a.IsActive),
            "Two bosses sharing one profile must be able to start independent runtimes and cooldowns.");
        var firstAction = sharedSkill.Actions.First(a => a.Owner == first);
        var secondAction = sharedSkill.Actions.First(a => a.Owner == second);
        firstAction.Complete();
        await Until(() => first.CurrentAction == null, "The first shared-profile boss did not finish its action.");
        Check(second.CurrentAction == secondAction && secondAction.IsActive,
            "Finishing one boss must not change the other boss's current step.");
        first.gameObject.SetActive(false); second.gameObject.SetActive(false);
    }

    private static async Task VerifyCooldownAndRecovery(Fixture f)
    {
        var probe = f.Probe(.02f);
        probe.cooldown = .4f;
        var profile = f.Profile();
        var option = f.Option(f.Pattern(probe));
        option.cooldown = 0f; option.recovery = .1f;
        profile.patterns.Add(option);
        f.Target.position = f.Origin + Vector3.forward * 2f;
        var boss = f.Actor(profile, oldAttackCooldown: 10f);
        await Until(() => probe.Actions.Count > 0, "The tactical pattern did not start.");
        var first = probe.Actions[0];
        await Until(() => first.IsFinished, "The probe action did not finish its own recovery.");
        await Delay(.05f);
        Check(probe.Actions.Count == 1, "Skill cooldown and authored recovery must prevent an immediate repeat.");
        await Until(() => probe.Actions.Count >= 2, "A cooled-down first attack left the tactical boss permanently stalled.");
        Check(probe.Actions[1].EnteredAt - first.EnteredAt >= .38f,
            "Tactical patterns must honor the skill cooldown.");
        Check(probe.Actions[1].EnteredAt - first.EnteredAt < 2f,
            "Tactical recovery must not add the legacy ten-second global attack cooldown.");
        boss.gameObject.SetActive(false);

        var firstSkill = f.Probe(.01f);
        var unavailableFollowup = f.Probe(); unavailableFollowup.cooldown = 20f;
        var nextProfile = f.Profile();
        var chain = f.Pattern(firstSkill);
        chain.steps.Add(new BossTacticalStep { kind = BossTacticalStepKind.Attack, skill = unavailableFollowup });
        var chainedOption = f.Option(chain); chainedOption.cooldown = .05f;
        nextProfile.patterns.Add(chainedOption);
        var nextBoss = f.Actor(nextProfile);
        // Mark only the follow-up as unavailable before the brain starts its first chain.
        nextBoss.RecordAttackStarted(unavailableFollowup);
        await Until(() => firstSkill.Actions.Count >= 2,
            "A cooldown-blocked follow-up must end its chain and allow another decision.");
        Check(unavailableFollowup.Actions.Count == 0,
            "An unavailable follow-up must not ignore its skill cooldown.");
        nextBoss.gameObject.SetActive(false);

        var punishProbe = f.Probe(.01f);
        var punishProfile = f.Profile();
        var punishOption = f.Option(f.Pattern(punishProbe)); punishOption.recovery = .3f;
        punishProfile.patterns.Add(punishOption);
        var punishBoss = f.Actor(punishProfile); punishBoss.HurtDuration = .03f;
        await Until(() => punishProbe.Actions.Count > 0, "The recovery-interruption fixture did not attack.");
        await Until(() => punishProbe.Actions[0].IsFinished && punishBoss.CurrentAction == null,
            "The recovery-interruption fixture did not enter its authored punish window.");
        float recoveryStarted = Time.time;
        punishBoss.TakeDamage(1f, poiseDamage: 0f, staggerResistLevel: StaggerResistLevel.HIGH);
        Check(punishBoss.IsHurt, "A hit during the authored punish window must still trigger its ordinary hit reaction.");
        await Delay(.12f);
        Check(punishProbe.Actions.Count == 1 && punishBoss.CurrentAction == null,
            "A short hit reaction must not discard the remaining .3-second punish window and allow an early counterattack.");
        await Until(() => punishProbe.Actions.Count >= 2,
            "The preserved punish window did not finish after its hit reaction.");
        Check(punishProbe.Actions[1].EnteredAt - recoveryStarted >= .29f,
            "Returning from a shared reaction must retain the remaining authored recovery duration.");
        punishBoss.gameObject.SetActive(false);
    }

    private static async Task VerifyRecoveryRepositioning(Fixture f)
    {
        var skill = f.Probe(); skill.cooldown = 0f;
        var profile = f.Profile(); profile.repositionDuration = .4f; profile.strafeDuration = .9f;
        var option = f.Option(f.Pattern(skill)); option.recovery = .3f;
        profile.patterns.Add(option);
        f.Target.position = f.Origin + Vector3.forward * 3f;
        var boss = f.Actor(profile, movementSpeed: 4f);
        var nav = boss.GetComponent<NavMeshAgent>(); nav.acceleration = 12f;
        await Until(() => skill.Actions.Any(a => a.IsActive), "The repositioning fixture did not attack.");
        float stoppingDistance = nav.stoppingDistance;
        bool updateRotation = nav.updateRotation;
        skill.Actions[0].Complete();
        await Until(() => boss.CurrentAction == null, "The repositioning fixture did not enter recovery.");
        Vector3 recoveryPosition = boss.transform.position;
        float recoveredAt = Time.time;
        await Delay(.12f);
        Check(skill.Actions.Count == 1 && Vector3.Distance(recoveryPosition, boss.transform.position) < .02f,
            "Repositioning must leave the authored stationary punish window intact.");
        var tactical = ((BossBrain)boss.Brain).Tactical;
        await Until(() => tactical.DebugState == "Repositioning", "Recovery did not lead to its opted-in reposition interval.");
        float repositionedAt = Time.time;
        Check(repositionedAt - recoveredAt >= .27f && skill.Actions.Count == 1
            && Vector3.Distance(recoveryPosition, boss.transform.position) < .02f,
            "The full authored recovery must finish before repositioning or another attack starts.");
        await Until(() => Mathf.Abs(boss.transform.position.x - recoveryPosition.x) > .12f || skill.Actions.Count > 1,
            "An in-range boss did not make visible lateral progress during repositioning.");
        Check(skill.Actions.Count == 1 && tactical.DebugState == "Repositioning",
            "A ready repeat attack must not preempt its required reposition interval.");
        await Until(() => skill.Actions.Count >= 2, "The boss did not attack again after its bounded reposition interval.");
        Check(skill.Actions[1].EnteredAt - repositionedAt >= .35f
            && skill.Actions[1].EnteredAt - recoveredAt < 1.1f,
            "Repositioning must use its configured duration and finish even when Strafe Duration is longer.");
        Check(!nav.hasPath && Mathf.Approximately(nav.stoppingDistance, stoppingDistance)
            && nav.updateRotation == updateRotation,
            "Starting the next attack must clear navigation and restore its stopping distance and rotation flag.");

        skill.Actions[1].Complete();
        await Until(() => tactical.DebugState == "Repositioning" && nav.hasPath,
            "A subsequent recovery did not restart navigation.");
        ((BossBrain)boss.Brain).CancelPattern();
        Check(!nav.hasPath && nav.velocity.sqrMagnitude < .001f
            && Mathf.Approximately(nav.stoppingDistance, stoppingDistance) && nav.updateRotation == updateRotation,
            "Interrupting repositioning must clear its path and restore all temporary navigation settings.");
        boss.gameObject.SetActive(false);

        // Recovery inside a combo is an authored boundary, not the end of its pattern.
        var opening = f.Probe(); opening.cooldown = 0f;
        var followup = f.Probe();
        var chain = f.Pattern(opening);
        chain.steps.Add(new BossTacticalStep { kind = BossTacticalStepKind.Recover, seconds = .12f });
        chain.steps.Add(new BossTacticalStep { kind = BossTacticalStepKind.Attack, skill = followup });
        var chainProfile = f.Profile(); chainProfile.repositionDuration = .4f;
        chainProfile.patterns.Add(f.Option(chain));
        var chainedBoss = f.Actor(chainProfile, movementSpeed: 4f);
        await Until(() => opening.Actions.Any(a => a.IsActive), "The authored recovery chain did not start.");
        Vector3 chainPosition = chainedBoss.transform.position;
        float continuedAt = Time.time;
        opening.Actions[0].Complete();
        await Until(() => followup.Actions.Any(a => a.IsActive), "Authored recovery did not continue its existing pattern.");
        Check(Time.time - continuedAt < .35f && Vector3.Distance(chainPosition, chainedBoss.transform.position) < .02f,
            "An authored within-pattern recovery must continue its chain without inserting repositioning.");
        chainedBoss.gameObject.SetActive(false);

        await VerifyRepositionDistance(f, .5f, retreat: true);
        await VerifyRepositionDistance(f, 10f, retreat: false);
    }

    private static async Task VerifyRepositionDistance(Fixture f, float targetDistance, bool retreat)
    {
        var skill = f.Probe(); skill.cooldown = 0f;
        var profile = f.Profile(); profile.repositionDuration = .4f;
        var option = f.Option(f.Pattern(skill)); option.recovery = .04f;
        profile.patterns.Add(option);
        f.Target.position = f.Origin + Vector3.forward * 3f;
        var boss = f.Actor(profile, movementSpeed: 4f);
        boss.GetComponent<NavMeshAgent>().acceleration = 12f;
        await Until(() => skill.Actions.Any(a => a.IsActive), "The distance-repositioning fixture did not attack.");
        f.Target.position = f.Origin + Vector3.forward * targetDistance;
        float before = Vector3.Distance(boss.transform.position, f.Target.position);
        skill.Actions[0].Complete();
        await Until(() => skill.Actions.Count >= 2, "Distance repositioning did not return to a ready attack.");
        float after = Vector3.Distance(boss.transform.position, f.Target.position);
        Check(retreat ? after > before + .1f : after < before - .1f,
            retreat ? "A too-close target must provoke an actual retreat between patterns."
                : "A distant target must provoke an actual approach between patterns.");
        boss.gameObject.SetActive(false);
    }

    private static async Task VerifyRangePositioningAndRepeatFallback(Fixture f)
    {
        var skill = f.Probe(.02f); skill.minimumRange = .5f; skill.executeRange = 1.5f;
        var profile = f.Profile();
        var pattern = f.Pattern(skill);
        var option = f.Option(pattern); option.repeatMultiplier = 0f;
        profile.patterns.Add(option);
        f.Target.position = f.Origin + Vector3.forward * 8f;
        var boss = f.Actor(profile, movementSpeed: 12f);
        await Until(() => boss.Brain != null, "The attack-range positioning fixture did not initialize.");
        var selector = new BossPatternSelector();
        Check(selector.Select(boss, profile, out _) == null
            && selector.PositioningSkill == skill && selector.PositioningOption == option,
            "An out-of-range opening attack must request positioning to that attack's execute range.");
        var moveFirst = f.Pattern(skill);
        moveFirst.steps.Insert(0, new BossTacticalStep
        {
            kind = BossTacticalStepKind.Move,
            movement = new BossMovementSettings { mode = BossMovementMode.Approach, minDistance = .5f, maxDistance = 1.5f }
        });
        var moveProfile = f.Profile(); var moveOption = f.Option(moveFirst);
        moveProfile.patterns.Add(moveOption);
        Check(new BossPatternSelector().Select(boss, moveProfile, out _) == moveOption,
            "An explicit move-first pattern must remain selectable before its future attack is in range.");
        await Until(() => skill.Actions.Count >= 2,
            "A narrow-range attack must become executable, and a sole zero-repeat-weight pattern must remain usable.");
        Check(skill.Actions.All(a => a.EnteredAt > 0f),
            "Range positioning must avoid creating doomed out-of-range attack actions.");
        boss.gameObject.SetActive(false);
    }

    private static async Task VerifyNoPolicyCallbackCancellation(Fixture f)
    {
        var previousSettings = CombatSettings.Active;
        var settingsObject = new GameObject("Tactical callback combat settings");
        SceneManager.MoveGameObjectToScene(settingsObject, f.Target.gameObject.scene);
        settingsObject.SetActive(false);
        var settings = settingsObject.AddComponent<CombatSettings>();
        var table = f.New<ElementEffectiveManager>();
        foreach (ElementType attack in Enum.GetValues(typeof(ElementType)))
            foreach (ElementType defense in Enum.GetValues(typeof(ElementType)))
                table.elementEffectiveList.Add(new ElementEffective
                { AttackType = attack, DefenseType = defense, multiplier = 1f });
        table.Initialize(); Set(settings, "_elementTable", table);
        typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, settings);
        var receiver = f.Target.gameObject.AddComponent<BossTacticalDamageProbe>();
        var collider = f.Target.gameObject.AddComponent<SphereCollider>(); collider.radius = .3f;
        int previousLayer = f.Target.gameObject.layer;
        f.Target.gameObject.layer = LayerMask.NameToLayer("Player");
        ConfiguredBoss boss = null;
        try
        {
            var skill = f.New<MonsterComboAttackSO>(); skill.animName = "Punching";
            skill.executeRange = skill.engageRange = 5f; skill.attackDuration = .2f;
            // Cross all three valid, nonoverlapping windows in the same Update.
            skill.attackSpeed = 10000f;
            skill.hits = new List<EnemyHitEvent>
            {
                new EnemyHitEvent { hitStartTime = .01f, hitDuration = .01f },
                new EnemyHitEvent { hitStartTime = .03f, hitDuration = .01f },
                new EnemyHitEvent { hitStartTime = .05f, hitDuration = .01f }
            };
            var profile = f.Profile(); var option = f.Option(f.Pattern(skill));
            option.cooldown = 20f; profile.patterns.Add(option);
            f.Target.position = f.Origin + Vector3.forward;
            Physics.SyncTransforms();
            boss = f.Actor(profile);
            await Until(() => receiver.Hits > 0,
                "The callback-cancellation fixture did not receive the real combo hit.");
            await Delay(.06f);
            Check(receiver.Hits == 1 && boss.CurrentAction == null,
                "Cancelling a tactical action from its first damage callback must prevent further same-frame hits, even without an attack policy.");
        }
        finally
        {
            if (boss != null) boss.gameObject.SetActive(false);
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, previousSettings);
            f.Target.gameObject.layer = previousLayer;
            Object.Destroy(collider); Object.Destroy(receiver); Object.Destroy(settingsObject);
        }
    }

    private static async Task VerifyFailedMovement(Fixture f)
    {
        var probe = f.Probe();
        var pattern = f.Pattern(probe);
        pattern.steps.Insert(0, new BossTacticalStep
        {
            kind = BossTacticalStepKind.Move,
            movement = new BossMovementSettings
            {
                mode = BossMovementMode.Approach, minDistance = 0f, maxDistance = 1f,
                timeout = .12f, duration = .1f
            }
        });
        var profile = f.Profile(); profile.stuckTimeout = .08f;
        profile.patterns.Add(f.Option(pattern));
        f.Target.position = f.Origin + Vector3.forward * 10f;
        var boss = f.Actor(profile);
        await Until(() => boss.Brain != null, "The blocked-movement boss did not initialize.");
        await Delay(.3f);
        Check(probe.Actions.Count == 0, "A failed approach must not execute a distant follow-up attack.");
        // A new reachable destination must be usable after failure, instead of a latched Move state.
        f.Target.position = f.Origin + Vector3.forward * .5f;
        await Until(() => probe.Actions.Any(a => a.IsActive),
            "A failed or stuck move left the boss unable to retry a reachable pattern.");
        boss.gameObject.SetActive(false);
    }

    private static async Task VerifyAimArmorAndParry(Fixture f)
    {
        var skill = f.BasicAttack();
        var policy = new BossAttackPolicy
        {
            enabled = true,
            turnSpeed = 180f, lockFraction = .2f,
            armorDuringStartup = true, armorDuringActive = true
        };
        var profile = f.Profile(); profile.parryRecovery = .22f;
        profile.patterns.Add(f.Option(f.Pattern(skill, policy)));
        f.Target.position = f.Origin + Vector3.forward * 2f;
        var boss = f.Actor(profile);
        await Until(() => boss.CurrentAction?.IsActive == true, "The real basic attack did not start.");
        var action = boss.CurrentAction;
        Check(action.SupportsBossPolicy && action.HasBossArmor,
            "The real attack must expose its opted-in startup armor.");
        float hp = boss.EnemyStat.CurrentHp;
        boss.TakeDamage(1f, power: Vector3.right, poiseDamage: 0f,
            staggerResistLevel: StaggerResistLevel.HIGH);
        Check(boss.EnemyStat.CurrentHp < hp && !boss.IsHurt && !boss.IsKnockbacking,
            "Core-skill armor must retain HP damage while resisting ordinary stagger and knockback.");
        await Until(() => action.BossAimLocked, "The attack did not reach its authored aim-lock point.");
        Quaternion locked = boss.transform.rotation;
        f.Target.position = f.Origin + Vector3.right * 2f;
        await Delay(.09f);
        Check(Quaternion.Angle(locked, boss.transform.rotation) < .1f,
            "Moving after the aim lock must not bend the committed basic attack.");
        float parriedAt = Time.time;
        action.OnParried();
        await Until(() => boss.CurrentAction != action, "A successful parry did not cancel the current chain.");
        Check(!action.HasBossArmor && !ParryEventBus.IsTelegraphActive(action),
            "Parry cancellation must release armor and the telegraph.");
        await Delay(.09f);
        Check(boss.CurrentAction == null, "The authored parry recovery must provide a real retaliation window.");
        await Until(() => boss.CurrentAction?.IsActive == true, "The boss did not recover from a parry.");
        Check(Time.time - parriedAt >= .2f, "Parry recovery must use the configured duration.");

        var armored = boss.CurrentAction;
        boss.GroggyDuration = .15f;
        boss.TakeDamage(1f, poiseDamage: 10000f, staggerResistLevel: StaggerResistLevel.HIGH);
        await Until(() => boss.IsGroggy, "Core-skill armor must not prevent a real poise break.");
        Check(boss.CurrentAction != armored && !armored.HasBossArmor,
            "Poise break must cancel the armored action and clear its temporary resistance.");
        boss.gameObject.SetActive(false);

        await VerifyDashLock(f, policy);
        await VerifyJumpLock(f, policy);
    }

    private static async Task VerifyTacticalActorClock(Fixture f)
    {
        var skill = f.BasicAttack();
        var policy = new BossAttackPolicy { enabled = true, turnSpeed = 180f, lockFraction = .8f };
        var profile = f.Profile(); var option = f.Option(f.Pattern(skill, policy));
        option.recovery = .2f; profile.patterns.Add(option);
        f.Target.position = f.Origin + Vector3.forward * 2f;
        var boss = f.Actor(profile);
        await Until(() => boss.CurrentAction?.IsActive == true, "The tactical actor-clock fixture did not attack.");
        var action = boss.CurrentAction;
        float timer = Field<float>(action, "_timer");
        Quaternion facing = boss.transform.rotation;
        f.Target.position = f.Origin + Vector3.right * 2f;
        boss.ActorClock.Request(boss, 2f, 0f, HitStopStacking.Replace);
        await Delay(.08f);
        Check(boss.CurrentAction == action && Mathf.Approximately(timer, Field<float>(action, "_timer"))
            && Quaternion.Angle(facing, boss.transform.rotation) < .1f,
            "Local hitstop must freeze a tactical attack's timeline and preparation turning while world time advances.");
        boss.ActorClock.ClearStops();
        await Until(() => action.IsFinished && boss.CurrentAction == null,
            "The tactical attack did not resume after its local hitstop ended.");
        boss.ActorClock.Request(boss, 2f, 0f, HitStopStacking.Replace);
        await Delay(.28f);
        Check(boss.CurrentAction == null,
            "An authored tactical recovery must remain frozen by actor hitstop, even after its duration passes in world time.");
        boss.ActorClock.ClearStops();
        await Until(() => boss.CurrentAction?.IsActive == true,
            "Authored recovery did not resume on the actor clock after hitstop cleared.");
        boss.gameObject.SetActive(false);
    }

    private static async Task VerifyDashLock(Fixture f, BossAttackPolicy policy)
    {
        var skill = f.New<MonsterDashAttackSO>();
        skill.animName = "Punching"; skill.startupTime = .35f; skill.dashDuration = .08f;
        skill.recoveryTime = .1f; skill.dashSpeed = 2f; skill.executeRange = 100f;
        skill.engageRange = 100f; skill.telegraphDuration = .35f;
        var profile = f.Profile(); profile.patterns.Add(f.Option(f.Pattern(skill, policy)));
        f.Target.position = f.Origin + Vector3.forward * 2f;
        var boss = f.Actor(profile);
        await Until(() => boss.CurrentAction?.IsActive == true, "The dash attack did not start.");
        var action = boss.CurrentAction;
        await Until(() => action.BossAimLocked, "The dash did not lock its launch direction during startup.");
        Vector3 lockedForward = boss.transform.forward;
        f.Target.position = f.Origin + Vector3.right * 8f;
        await Until(() => action.BossStage == BossAttackStage.Active, "The dash did not launch.");
        Check(Vector3.Angle(lockedForward, boss.transform.forward) < .1f,
            "The dash must launch along the locked direction, rather than re-aim at startup completion.");
        boss.gameObject.SetActive(false);
        Check(!action.HasBossArmor && !ParryEventBus.IsTelegraphActive(action)
            && Field<int>(boss, "_moveLockCount") == 0 && boss.GetComponent<Collider>().enabled,
            "Disabling a dash must restore the collider and release movement, armor and telegraph state.");
    }

    private static async Task VerifyJumpLock(Fixture f, BossAttackPolicy policy)
    {
        var skill = f.New<MonsterJumpSlamSO>();
        skill.animName = "Punching"; skill.startupTime = .35f; skill.jumpDuration = .12f;
        skill.recoveryTime = .1f; skill.jumpHeight = .2f; skill.executeRange = 100f;
        skill.engageRange = 100f; skill.telegraphDuration = .35f;
        var profile = f.Profile(); profile.patterns.Add(f.Option(f.Pattern(skill, policy)));
        f.Target.position = f.Origin + Vector3.forward * 2f;
        var boss = f.Actor(profile);
        await Until(() => boss.CurrentAction?.IsActive == true, "The jump slam did not start.");
        var action = boss.CurrentAction;
        await Until(() => action.BossAimLocked, "The jump slam did not lock its landing target.");
        Vector3 lockedTarget = f.Target.position;
        f.Target.position = f.Origin + Vector3.right * 8f;
        await Until(() => action.BossStage == BossAttackStage.Recovery || action.IsFinished,
            "The jump slam did not land.");
        Vector3 landingOffset = boss.transform.position - lockedTarget; landingOffset.y = 0f;
        Check(landingOffset.magnitude < .4f,
            "The jump landing must retain the pre-lock target position after the player moves.");
        boss.gameObject.SetActive(false);
        Check(Field<int>(boss, "_moveLockCount") == 0 && !action.HasBossArmor,
            "Disabling a jump must release temporary movement and armor state.");
    }

    private static async Task VerifyPhaseAndDisableCleanup(Fixture f)
    {
        var skill = f.BasicAttack();
        var policy = new BossAttackPolicy { enabled = true, armorDuringStartup = true, armorDuringActive = true };
        var profile = f.Profile(); profile.patterns.Add(f.Option(f.Pattern(skill, policy)));
        f.Target.position = f.Origin + Vector3.forward * 2f;
        var boss = f.Actor(profile, phaseCount: 2);
        int deaths = 0; boss.OnDied += () => deaths++;
        await Until(() => boss.CurrentAction?.IsActive == true, "The phase cleanup fixture did not attack.");
        var action = boss.CurrentAction;
        boss.TakeDamage(10000f, poiseDamage: 0f);
        Check(boss.IsTransitioning && !boss.IsDead && deaths == 0,
            "Exhausting the first phase must not confirm the boss death.");
        await Until(() => boss.PhaseIndex == 1 && !boss.IsTransitioning,
            "Tactical mode did not preserve boss phase revival.");
        Check(!action.IsActive && !action.HasBossArmor && !ParryEventBus.IsTelegraphActive(action),
            "Phase transition must release the previous action, armor and telegraph.");
        await Until(() => boss.CurrentAction?.IsActive == true, "The revived phase did not resume tactical combat.");
        var finalAction = boss.CurrentAction;
        boss.TakeDamage(10000f, poiseDamage: 0f);
        await Until(() => boss.IsDead && boss.CurrentAction == null, "The final phase did not terminate tactical combat.");
        Check(deaths == 1 && !finalAction.HasBossArmor && !ParryEventBus.IsTelegraphActive(finalAction),
            "Final death must happen once and release all temporary attack state.");
        boss.gameObject.SetActive(false);
    }

    private static T Asset<T>(List<Object> assets) where T : ScriptableObject
    {
        var asset = ScriptableObject.CreateInstance<T>(); assets.Add(asset); return asset;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static async Task Until(Func<bool> condition, string message)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 5d;
        while (!condition())
        {
            if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > deadline)
                throw new InvalidOperationException(message);
            await Task.Yield();
        }
        _checks++;
    }

    private static async Task Delay(float seconds)
    {
        float until = Time.time + seconds;
        await Until(() => Time.time >= until, "The boss tactical fixture stopped advancing world time.");
    }

    private static void Set(object target, string name, object value)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Hidden | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (field == null) continue;
            field.SetValue(target, value); return;
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static T Field<T>(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Hidden | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (field != null) return (T)field.GetValue(target);
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private sealed class Fixture
    {
        public readonly Vector3 Origin = new Vector3(12000f, 0f, 12000f);
        public readonly Transform Target;
        private readonly Scene _scene;
        private readonly List<Object> _assets = new List<Object>();
        private readonly NavMeshDataInstance _navigation;
        private readonly float _previousTimeScale;

        public Fixture()
        {
            _previousTimeScale = Time.timeScale; Time.timeScale = 1f;
            _scene = SceneManager.CreateScene("BossTacticalRegression-" + Guid.NewGuid().ToString("N"));
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(Origin + Vector3.down * .25f, Quaternion.identity, Vector3.one),
                    size = new Vector3(60f, .5f, 60f), area = 0
                }
            };
            var navigation = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(Origin, new Vector3(60f, 10f, 60f)), Vector3.zero, Quaternion.identity);
            if (navigation == null) throw new InvalidOperationException("Could not build the tactical regression navigation.");
            _assets.Add(navigation); _navigation = NavMesh.AddNavMeshData(navigation);
            var target = new GameObject("Assigned boss combat target");
            SceneManager.MoveGameObjectToScene(target, _scene); Target = target.transform;
        }

        public T New<T>() where T : ScriptableObject => Asset<T>(_assets);

        public BossTacticalProbeSkill Probe(float duration = -1f)
        {
            var skill = New<BossTacticalProbeSkill>(); skill.duration = duration;
            skill.engageRange = skill.executeRange = 100f; skill.telegraphDuration = 2f;
            return skill;
        }

        public MonsterDefaultAttackSO BasicAttack()
        {
            var skill = New<MonsterDefaultAttackSO>(); skill.animName = "Punching";
            skill.hitStartTime = .45f; skill.hitDuration = .08f; skill.attackDuration = .7f;
            skill.executeRange = skill.engageRange = 100f; skill.telegraphDuration = .45f;
            return skill;
        }

        public BossCombatProfileSO Profile()
        {
            var profile = New<BossCombatProfileSO>(); profile.decisionInterval = .02f;
            profile.turnSpeed = 0f; profile.failureRecovery = .03f;
            return profile;
        }

        public BossTacticalPatternSO Pattern(MonsterSkillData skill, BossAttackPolicy policy = null)
        {
            var pattern = New<BossTacticalPatternSO>();
            pattern.steps.Add(new BossTacticalStep
            { kind = BossTacticalStepKind.Attack, skill = skill, attackPolicy = policy });
            return pattern;
        }

        public BossPatternOption Option(BossPatternSO pattern, BossCombatCondition condition = null)
            => new BossPatternOption
            { pattern = pattern, condition = condition ?? new BossCombatCondition(), cooldown = 0f, recovery = .03f };

        public ConfiguredBoss Actor(BossCombatProfileSO profile, float oldAttackCooldown = .05f,
            int phaseCount = 1, float movementSpeed = 0f)
        {
            var actor = new GameObject("Tactical test boss"); actor.SetActive(false);
            SceneManager.MoveGameObjectToScene(actor, _scene); actor.transform.position = Origin;
            var nav = actor.AddComponent<NavMeshAgent>(); nav.speed = movementSpeed; nav.stoppingDistance = .25f;
            actor.AddComponent<CapsuleCollider>();
            var model = new GameObject("Model"); model.transform.SetParent(actor.transform, false);
            var animator = model.AddComponent<Animator>();
            // These checks drive action timers; production socket animation receivers are not part of the fixture.
            animator.fireEvents = false;
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/_Project/Generated/Bosses/Test_Boss/Test_Boss.controller");
            var boss = actor.AddComponent<ConfiguredBoss>();
            var data = New<BossData>(); data.maxHp = 100; data.baseAtk = 1f;
            data.combatRange = 4f; data.combatExitRange = 20f; data.attackCooldown = oldAttackCooldown;
            data.phaseDownSeconds = .025f; data.phaseHoldSeconds = .025f; data.phaseReviveSeconds = .05f;
            var skills = New<MonsterSkillDataSO>(); skills.skillData = new List<MonsterSkillData>();
            data.monsterSkillData = skills;
            Set(boss, "BossData", data); Set(boss.GetComponent<EnemyStat>(), "baseData", data);
            var phases = new List<BossPhaseData>();
            for (int i = 0; i < phaseCount; i++)
            {
                var phase = New<BossPhaseData>(); phase.phaseId = "tactical_" + i;
                phase.phaseMaxHp = 100f; phase.skillSet = skills; phase.combatProfile = profile;
                phases.Add(phase);
            }
            Set(boss, "PhaseDatas", phases);
            boss.SetCombatTarget(Target);
            actor.SetActive(true);
            return boss;
        }

        public async Task DisposeAsync()
        {
            try
            {
                if (_scene.IsValid() && _scene.isLoaded)
                {
                    foreach (var root in _scene.GetRootGameObjects())
                        foreach (var enemy in root.GetComponentsInChildren<Enemy>(true)) enemy.CancelCurrentAction();
                    var operation = SceneManager.UnloadSceneAsync(_scene);
                    while (operation != null && !operation.isDone) await Task.Yield();
                }
                if (_navigation.valid) _navigation.Remove();
                foreach (var asset in _assets) if (asset != null) Object.Destroy(asset);
            }
            finally { Time.timeScale = _previousTimeScale; }
        }
    }
}

public sealed class BossTacticalProbeSkill : MonsterSkillData
{
    public float duration = -1f;
    public bool supportsBossPolicy;
    public override bool SupportsBossExecutionPolicy => supportsBossPolicy;
    public readonly List<BossTacticalProbeAction> Actions = new List<BossTacticalProbeAction>();
    public override EnemyAction CreateAction(Enemy enemy)
    {
        var action = new BossTacticalProbeAction(this, enemy); Actions.Add(action); return action;
    }
}

public sealed class BossTacticalProbeAction : EnemyAction
{
    public float EnteredAt { get; private set; }
    public override bool SupportsBossPolicy => ((BossTacticalProbeSkill)SkillData).supportsBossPolicy;
    public BossTacticalProbeAction(MonsterSkillData skill, Enemy owner) : base(skill, owner) { }
    public override void Enter() { base.Enter(); EnteredAt = Time.time; }
    public override void Update()
    {
        base.Update();
        float duration = ((BossTacticalProbeSkill)SkillData).duration;
        if (duration >= 0f && _timer >= duration) Complete();
    }
    public void Complete() => IsFinished = true;
}

public sealed class BossTacticalDamageProbe : MonoBehaviour, IDamageable
{
    public int Hits { get; private set; }
    public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE,
        bool isCritical = false, Vector3 power = default, float poiseDamage = 20f,
        StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE,
        bool isParryable = true, object source = null)
    {
        Hits++;
        (source as EnemyAction)?.Owner.CancelCurrentAction();
        return new DamageResult(DamageOutcome.Applied, damage);
    }
}
