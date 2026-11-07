using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using StudioBaekho;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Exercises mobility sensing, safe tactical boundaries, geometry and real action cleanup.</summary>
public static class BossMobilityRegression
{
    private const string Folder = "Assets/_Project/Generated/Bosses/HitReactionVariants";
    private const string SourcePath = "Assets/_Project/Generated/Bosses/SwordRobotBoss/SwordRobotBoss.prefab";
    private const string VariantPath = Folder + "/SwordRobotBoss_FinalIK.prefab";
    private static bool _running;
    private static Report _report = new Report { status = "idle" };

    [Serializable] private sealed class Report
    {
        public string status, currentCase, error, reportPath;
        public int checks;
        public int[] approachChoices = new int[2];
        public List<string> completed = new List<string>();
    }

    [MenuItem("Tools/Studio Baekho/Enemy/Verify Boss Mobility")]
    private static void RunMenu() => Debug.Log(RunAuthoring());

    public static string RunAuthoring()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        Check(source != null, "The original sword robot is missing.");
        var original = source.GetComponent<Boss>();
        Check(original.Mobility != null && !original.Mobility.enabled && Mathf.Approximately(original.PhaseHealthMultiplier, 1f),
            "The original boss must keep mobility disabled and unscaled phase health.");
        var originalController = source.GetComponentInChildren<Animator>(true).runtimeAnimatorController as AnimatorController;
        Check(originalController != null && originalController.layers.Length == 1
            && !originalController.layers[0].stateMachine.states.Any(state => state.state.name.StartsWith("Mobility", StringComparison.Ordinal)),
            "Mobility must not modify the original controller.");
        foreach (string suffix in new[] { "FinalIK", "Additive" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/SwordRobotBoss_" + suffix + ".prefab");
            Check(prefab != null, "Missing mobility variant: " + suffix);
            var boss = prefab.GetComponent<Boss>();
            var settings = boss.Mobility;
            Check(settings != null && settings.enabled && Mathf.Approximately(boss.PhaseHealthMultiplier, .7f),
                "Only the mobility variants should use the .7 health policy.");
            Check(Mathf.Approximately(boss.PhaseConfiguration[0].phaseMaxHp * boss.PhaseHealthMultiplier, 5460f)
                && Mathf.Approximately(boss.PhaseConfiguration[1].phaseMaxHp * boss.PhaseHealthMultiplier, 6552f),
                "The comparison variants must have 5460 and 6552 phase HP.");
            Check(settings.hitCount == 5 && Mathf.Approximately(settings.hitWindow, 2f)
                && Mathf.Approximately(settings.cornerDuration, .6f) && Mathf.Approximately(settings.farDistance, 7f)
                && Mathf.Approximately(settings.farDuration, .5f), "Authored mobility pressure thresholds changed.");
            Check(Mathf.Approximately(settings.escapeCooldown, 6f) && Mathf.Approximately(settings.approachCooldown, 4.5f)
                && Mathf.Approximately(settings.sharedCooldown, 2f), "Authored mobility cooldowns changed.");
            var controller = prefab.GetComponentInChildren<Animator>(true).runtimeAnimatorController as AnimatorController;
            Check(controller != null && controller != originalController && controller.layers.Length == (suffix == "FinalIK" ? 1 : 2),
                "Each variant needs its own mobility animation controller.");
            foreach (string name in new[] { "MobilityJumpStart", "MobilityJumpLift", "MobilityJumpAir", "MobilityJumpLand",
                "MobilityDashReady", "MobilityDashSlash" })
                Check(controller.layers[0].stateMachine.states.Any(state => state.state.name == name && state.state.motion != null),
                    "Missing mobility animation state: " + name);
            var skills = Skills(settings);
            Check(skills.Length == 3 && skills.All(skill => skill != null)
                && skills.Select(skill => skill.kind).Distinct().Count() == 3, "All three distinct mobility skills are required.");
            Check(!controller.layers[0].stateMachine.states.Any(state => state.state.name == "MobilitySprint"),
                "The removed quick approach must not remain in either owned controller.");
            Check(skills.Where(skill => !skill.DealsDamage).All(skill => skill.damage == 0f && !skill.isParryable)
                && skills.Where(skill => skill.DealsDamage).All(skill => skill.damage > 0f && skill.isParryable),
                "Movement-only and damaging mobility must retain their distinct attack/parry metadata.");
            Check(boss.Data == original.Data && boss.PhaseConfiguration.SequenceEqual(original.PhaseConfiguration),
                "Mobility setup must preserve shared combat data and phase assets.");
        }
        Check(AssetDatabase.LoadMainAssetAtPath(Folder + "/Mobility/Sprint.asset") == null
            && AssetDatabase.LoadMainAssetAtPath(Folder + "/Mobility/Animations/Mobility_Sprint.anim") == null,
            "Removed quick-approach assets must not remain in the generated variants folder.");
        return "Boss mobility authoring checks passed.";
    }

    public static string StartPlayMode()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling || _running)
            throw new InvalidOperationException("Start mobility checks once in idle Play Mode.");
        _report = new Report { status = "running" };
        _report.reportPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BossMobilityVerification",
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"), "report.json"));
        _running = true;
        _ = RunRuntime();
        return Status();
    }

    public static string Status() => JsonUtility.ToJson(_report, true);
    public static string PlayModeStatus() => Status();

    private static async Task RunRuntime()
    {
        Fixture fixture = null;
        try
        {
            _report.currentCase = "authoring"; RunAuthoring();
            fixture = new Fixture();
            await Case("pressure thresholds and local clock", () => VerifyPressure(fixture));
            await Case("feasible random selection and geometry", () => VerifySelectionAndGeometry(fixture));
            await Case("whole-pattern and outer-recovery boundaries", () => VerifyTacticalBoundary(fixture));
            await Case("all three actual mobility actions", () => VerifyActions(fixture));
            await Case("geometry changing after jump launch", () => VerifyDynamicObstacles(fixture));
            await Case("airborne interruption cleanup", () => VerifyInterruptions(fixture));
            _report.status = "passed";
        }
        catch (Exception exception)
        {
            _report.status = "failed"; _report.error = exception.GetBaseException().Message;
            Debug.LogException(exception);
        }
        finally
        {
            try { if (fixture != null) await fixture.Dispose(); }
            catch (Exception exception) { _report.status = "failed"; _report.error = "Cleanup: " + exception.GetBaseException().Message; }
            _running = false;
            Directory.CreateDirectory(Path.GetDirectoryName(_report.reportPath));
            File.WriteAllText(_report.reportPath, Status());
        }
    }

    private static async Task Case(string name, Func<Task> run)
    { _report.currentCase = name; await run(); _report.completed.Add(name); }

    private static async Task VerifyPressure(Fixture f)
    {
        var boss = await f.Actor();
        var mobility = Tactical(boss).Mobility;
        f.Target.position = f.Origin + Vector3.forward * 3f;
        mobility.ClearIntent();
        boss.TakeDamage(0f, poiseDamage: 0f);
        Check(mobility.RecentHitCount == 0, "Zero HP damage must not add pressure.");
        boss.TakeDamage(1f, poiseDamage: 0f);
        boss.TakeDamage(1f, poiseDamage: 0f);
        Check(mobility.RecentHitCount == 1 && !mobility.EscapePending, "Multiple damage calls in one frame must count once.");
        for (int i = 0; i < 4; i++) { await Frames(1); boss.TakeDamage(1f, poiseDamage: 0f); }
        Check(mobility.RecentHitCount == 5 && mobility.EscapePending, "Five real damage frames inside two actor seconds must request escape.");
        mobility.ClearIntent();
        f.Target.position = f.Origin + Vector3.forward * 6.99f;
        mobility.Observe(.6f);
        Check(mobility.FarDuration == 0f, "A target inside seven metres must not accumulate far pressure.");
        f.Target.position = f.Origin + Vector3.forward * 7.1f;
        mobility.Observe(.49f);
        Check(!mobility.TrySelect(out _), "Far pressure must last at least .5 actor seconds.");
        mobility.Observe(.02f);
        Check(mobility.TrySelect(out var approach), "Sustained far pressure did not select a safe approach.");
        mobility.Complete(approach);
        Near(mobility.ApproachCooldownRemaining, 4.5f, .03f, "Approach cooldown must start on completion.");
        Near(mobility.SharedCooldownRemaining, 2f, .03f, "Completion must start the shared cooldown.");
        float before = mobility.ApproachCooldownRemaining;
        boss.ActorClock.Request(boss, 3f, 0f, HitStopStacking.Replace);
        await Frames(5);
        Near(mobility.ApproachCooldownRemaining, before, .002f, "Actor-local hitstop must freeze mobility cooldowns.");
        boss.ActorClock.ClearStops();
        await Frames(2);
        Check(mobility.ApproachCooldownRemaining < before, "Mobility cooldown did not resume with the actor clock.");

        // A fresh controller avoids spending time waiting on the preceding intentional cooldown.
        mobility = new BossMobilityController(boss);
        Warp(boss, f.Origin + Vector3.right * 28.7f);
        f.Target.position = boss.transform.position + Vector3.left * 3f;
        Check(BossMobilityNavigation.IsCornered(boss, boss.Mobility), "The edge fixture must block retreat while a player is nearby.");
        mobility.Observe(.59f);
        Check(!mobility.EscapePending, "Corner pressure must last .6 actor seconds.");
        mobility.Observe(.02f);
        Check(mobility.EscapePending, "Sustained corner pressure did not request escape.");
        f.Target.position = boss.transform.position + Vector3.left * 5f;
        mobility.ClearIntent(); mobility.Observe(.7f);
        Check(!mobility.EscapePending, "A distant player must not trigger the corner escape condition.");
        Warp(boss, f.Origin);
        mobility.ClearIntent();
        mobility.RecordHit(1f, 100); await Delay(boss, 2.05f);
        Check(mobility.RecentHitCount == 0, "Pressure hits must expire after the two-second actor window.");
        f.Stop(boss);
    }

    private static async Task VerifySelectionAndGeometry(Fixture f)
    {
        var boss = await f.Actor();
        f.Target.position = f.Origin + Vector3.forward * 10f;
        foreach (var skill in Skills(boss.Mobility))
            Check(skill.TryCreateAction(boss, out _), "The open owned floor must allow " + skill.kind);
        UnityEngine.Random.InitState(71429);
        for (int i = 0; i < 120; i++)
        {
            var mobility = new BossMobilityController(boss); mobility.Observe(.51f);
            Check(mobility.TrySelect(out var selected), "An open-floor approach selection failed.");
            int index = selected.Kind == BossMobilityKind.DashSlash ? 0 : 1;
            _report.approachChoices[index]++;
        }
        Check(_report.approachChoices.All(count => count >= 40 && count <= 80),
            "Equal feasible approach choices must be sampled across dash slash and jump slam without a fixed preference.");

        var ceiling = f.Box("Low jump ceiling", new Vector3(0f, 3.4f, 3f), new Vector3(30f, .2f, 30f));
        Physics.SyncTransforms();
        Check(!boss.Mobility.jumpSlam.TryCreateAction(boss, out _) && !boss.Mobility.escapeJump.TryCreateAction(boss, out _),
            "Jump arcs must reject a low ceiling even when their landing is clear.");
        Check(boss.Mobility.dashSlash.TryCreateAction(boss, out _),
            "A ceiling above standing height must leave the grounded dash slash feasible.");
        for (int i = 0; i < 12; i++)
        {
            var mobility = new BossMobilityController(boss); mobility.Observe(.51f);
            Check(mobility.TrySelect(out var selected) && selected.Kind == BossMobilityKind.DashSlash,
                "Selection must fall back to dash slash when jump slam is infeasible.");
        }
        ceiling.SetActive(false);
        var walls = f.EncloseStart(); Physics.SyncTransforms();
        var blocked = new BossMobilityController(boss); blocked.Observe(.51f);
        Check(!blocked.TrySelect(out _), "Body sweeps must reject a fully obstructed approach.");
        Check(blocked.EscapeCooldownRemaining == 0f && blocked.ApproachCooldownRemaining == 0f
            && blocked.SharedCooldownRemaining == 0f, "An infeasible query must not charge group or shared cooldowns.");
        foreach (var wall in walls) wall.SetActive(false);
        Physics.SyncTransforms();
        var bounds = f.Room.localBounds;
        f.Room.localBounds = new Bounds(new Vector3(0f, 4f, 0f), new Vector3(8f, 8f, 8f));
        Check(!boss.Mobility.dashSlash.TryCreateAction(boss, out _) && !boss.Mobility.jumpSlam.TryCreateAction(boss, out _),
            "A valid global NavMesh must not permit landing outside the boss's own room bounds.");
        f.Room.localBounds = bounds;
        var foreign = f.Box("Foreign support", new Vector3(0f, .02f, 8f), new Vector3(4f, .12f, 5f));
        Physics.SyncTransforms();
        Check(!boss.Mobility.dashSlash.TryCreateAction(boss, out _),
            "Landing on a collider not owned by the room ground set must be rejected.");
        foreign.SetActive(false);
        f.AddUpperFloor();
        f.Target.position = f.Origin + new Vector3(0f, 6f, 10f);
        Physics.SyncTransforms();
        Check(boss.Mobility.dashSlash.TryCreateAction(boss, out var floorAction)
            && Mathf.Abs(floorAction.Destination.y - boss.transform.position.y) < .2f,
            "Stacked NavMeshes must keep the planned destination on the actor's original floor.");
        f.Stop(boss);
    }

    private static async Task VerifyTacticalBoundary(Fixture f)
    {
        var first = f.Probe(); var second = f.Probe();
        var pattern = f.Pattern(first);
        pattern.steps.Add(new BossTacticalStep { kind = BossTacticalStepKind.Recover, seconds = .18f });
        pattern.steps.Add(new BossTacticalStep { kind = BossTacticalStepKind.Attack, skill = second });
        var boss = await f.Actor(true, false, pattern);
        f.Target.position = f.Origin + Vector3.forward * 3f;
        await Until(() => boss.CurrentAction is BossTacticalProbeAction, "The tactical opening did not start.");
        var tactical = Tactical(boss);
        for (int i = 0; i < 5; i++) tactical.Mobility.RecordHit(1f, 1000 + i);
        Check(tactical.Mobility.EscapePending, "The whole-pattern fixture did not accumulate escape pressure.");
        ((BossTacticalProbeAction)boss.CurrentAction).Complete();
        await Until(() => tactical.DebugState == "Recovering", "The authored inner recovery did not start.");
        await Frames(2);
        Check(boss.CurrentAction == null && tactical.DebugState == "Recovering",
            "Mobility must not interrupt an authored recovery inside a pattern.");
        await Until(() => second.Actions.Any(action => action.IsActive), "The next pattern attack did not run before mobility.");
        Check(!(boss.CurrentAction is BossMobilityAction) && tactical.Mobility.EscapePending,
            "A pending escape must wait until the entire pattern is complete.");
        float authoredRecovery = boss.ActiveCombatProfile.patterns.Single(option => option.pattern == pattern).recovery;
        float expectedOuterRecovery = authoredRecovery + boss.AdditionalPatternRecovery;
        // Start before the next brain tick observes completion, avoiding a frame of lost observation time.
        float outerStarted = boss.ActionTime;
        ((BossTacticalProbeAction)boss.CurrentAction).Complete();
        await Until(() => tactical.DebugState == "Recovering", "The final punish window did not start.");
        await Frames(2);
        Check(!(boss.CurrentAction is BossMobilityAction), "A pending escape skipped final pattern recovery.");
        await Until(() => boss.CurrentAction is BossMobilityAction, "The pending escape was not consumed after final recovery.");
        float elapsedOuterRecovery = boss.ActionTime - outerStarted;
        float timingTolerance = Mathf.Clamp(boss.ActionDeltaTime, .01f, .03f);
        Check(elapsedOuterRecovery + timingTolerance >= expectedOuterRecovery,
            "The full authored recovery (" + authoredRecovery + ") plus additional recovery ("
            + boss.AdditionalPatternRecovery + ") must precede mobility; observed " + elapsedOuterRecovery + ".");
        var movement = (BossMobilityAction)boss.CurrentAction;
        Check(movement.Kind == BossMobilityKind.EscapeJump && tactical.Mobility.SharedCooldownRemaining == 0f,
            "Escape must take priority and cooldowns must start after, not during, mobility.");
        await Until(() => boss.CurrentAction != movement, "The tactical escape never completed.");
        Check(tactical.Mobility.EscapeCooldownRemaining > 5.7f && tactical.Mobility.SharedCooldownRemaining > 1.7f,
            "Successful tactical mobility did not start its six-second and shared cooldowns.");
        f.Stop(boss);

        var cancelBoss = await f.Actor(true, false, f.Pattern(f.Probe()));
        f.Target.position = f.Origin + Vector3.forward * 3f;
        await Until(() => cancelBoss.CurrentAction is BossTacticalProbeAction, "The cancellation fixture did not start.");
        var cancelTactic = Tactical(cancelBoss);
        for (int i = 0; i < 5; i++) cancelTactic.Mobility.RecordHit(1f, 2000 + i);
        ((BossTacticalProbeAction)cancelBoss.CurrentAction).Complete();
        await Until(() => cancelBoss.CurrentAction is BossMobilityAction action && action.IsAirborne,
            "The cancellation fixture did not reach airborne mobility.");
        var cancelled = (BossMobilityAction)cancelBoss.CurrentAction;
        cancelBoss.GetComponent<StatusEffectHandler>().TryApply(new SilenceEffect(1f));
        await Frames(2);
        Check(!cancelled.IsActive && cancelTactic.Mobility.EscapeCooldownRemaining > 5.7f
            && cancelTactic.Mobility.SharedCooldownRemaining > 1.7f,
            "Cancelling an entered tactical mobility action must still charge its cooldowns.");
        CheckNavigationRestored(cancelBoss, "tactical Silence cancellation");
        f.Stop(cancelBoss);
    }

    private static async Task VerifyActions(Fixture f)
    {
        foreach (BossMobilityKind kind in Enum.GetValues(typeof(BossMobilityKind)))
        {
            var boss = await f.Actor();
            f.Target.position = f.Origin + Vector3.forward * (kind == BossMobilityKind.EscapeJump ? 3f : 10f);
            var skill = Skills(boss.Mobility).Single(value => value.kind == kind);
            Check(skill.TryCreateAction(boss, out var action), "The actual " + kind + " action had no safe plan.");
            Vector3 destination = action.Destination;
            var probe = f.DamageProbe(action.DealsDamage && kind == BossMobilityKind.DashSlash
                ? Vector3.Lerp(action.StartPosition, destination, .65f) : destination);
            boss.CurrentAction = action;
            action.Enter();
            Check(action.IsActive && !action.Failed, "The actual " + kind + " action failed to enter.");
            Check(action.DealsDamage || !ParryEventBus.IsTelegraphActive(action), "Movement-only mobility opened an attack telegraph.");
            f.Target.position += Vector3.right * 7f;
            bool damaged = false;
            double end = Time.realtimeSinceStartupAsDouble + 7d;
            while (action.IsActive && !action.IsFinished)
            {
                if (Time.realtimeSinceStartupAsDouble > end) throw new InvalidOperationException(kind + " did not finish.");
                await Frames(1); action.Update();
                Check(action.Destination == destination, "Mobility changed its locked destination after the target moved.");
                if (!damaged && action.MovementProgress > .05f && action.MovementProgress < .8f)
                {
                    boss.TakeDamage(1f, power: Vector3.right * 5f, poiseDamage: 0f, staggerResistLevel: StaggerResistLevel.HIGH);
                    Check(boss.CurrentAction == action && action.IsActive && !boss.IsHurt && !boss.IsKnockbacking,
                        "Ordinary damage cancelled or knocked back " + kind);
                    damaged = true;
                }
            }
            Check(damaged && !action.Failed, "The actual " + kind + " movement did not survive its ordinary-hit sample: " + action.FailureReason);
            Check(action.DealsDamage ? probe.Hits == 1 && probe.LastSource == action && probe.LastParryable
                : probe.Hits == 0 && ParryEventBus.CurrentAttackSource != action,
                "Damage/parry metadata or once-per-target filtering was incorrect for " + kind);
            action.Exit(); boss.CurrentAction = null;
            CheckNavigationRestored(boss, kind + " completion");
            Check(Vector3.Distance(BossMobilityNavigation.Flat(boss.transform.position), BossMobilityNavigation.Flat(destination)) < .2f,
                kind + " did not finish at its locked destination.");
            if (kind == BossMobilityKind.EscapeJump)
                Check(Vector3.Distance(action.StartPosition, destination) >= 3.9f
                    && Vector3.Distance(action.StartPosition, destination) <= 6.1f, "Escape jump must travel four to six metres.");
            f.Stop(boss); Object.Destroy(probe.gameObject); await Frames(1);
        }
    }

    private static async Task VerifyInterruptions(Fixture f)
    {
        foreach (string reason in new[] { "Parry", "Bind", "Silence", "Poise", "Phase", "Death", "Disable", "AgentDisabled" })
        {
            var boss = await f.Actor(false, reason == "Phase");
            f.Target.position = f.Origin + Vector3.forward * 10f;
            Check(boss.Mobility.jumpSlam.TryCreateAction(boss, out var action), "The interruption jump could not be planned: " + reason);
            boss.CurrentAction = action; action.Enter();
            await PumpUntil(action, () => action.IsAirborne && action.MovementProgress > .1f,
                "The interruption fixture never became airborne: " + reason);
            var nav = boss.GetComponent<NavMeshAgent>();
            switch (reason)
            {
                case "Parry": action.OnParried(); break;
                case "Bind": boss.GetComponent<StatusEffectHandler>().TryApply(new BindEffect(1f)); break;
                case "Silence": boss.GetComponent<StatusEffectHandler>().TryApply(new SilenceEffect(1f)); break;
                case "Poise": boss.TakeDamage(1f, poiseDamage: 100000f); break;
                case "Phase":
                case "Death": boss.TakeDamage(10000000f, poiseDamage: 0f); break;
                case "Disable": boss.gameObject.SetActive(false); break;
                case "AgentDisabled": nav.enabled = false; break;
            }
            action.Update();
            Check(!action.IsActive, "Airborne mobility did not exit for " + reason);
            Check(nav.updatePosition && nav.updateRotation && Mathf.Abs(boss.transform.position.y - f.Origin.y) < .3f,
                "Airborne mobility retained a detached agent or floating root after " + reason);
            if (reason == "AgentDisabled") Check(!nav.enabled, "Cleanup must not re-enable an intentionally disabled agent.");
            else if (reason != "Disable") Check(nav.enabled && nav.isOnNavMesh, "Cleanup lost the original NavMesh after " + reason);
            Check(!ParryEventBus.IsTelegraphActive(action), "Cancellation leaked a telegraph: " + reason);
            if (reason == "Disable")
            {
                boss.gameObject.SetActive(true); await Frames(1);
                Check(nav.enabled && nav.isOnNavMesh && nav.updatePosition, "Pooled airborne mobility did not restore navigation.");
            }
            f.Stop(boss); await Frames(1);
        }
    }

    private static async Task VerifyDynamicObstacles(Fixture f)
    {
        foreach (bool occupyLanding in new[] { true, false })
        {
            var boss = await f.Actor();
            f.Target.position = f.Origin + Vector3.forward * 10f;
            Check(boss.Mobility.jumpSlam.TryCreateAction(boss, out var action),
                "The dynamic-obstacle jump had no initial safe destination.");
            var probe = f.DamageProbe(action.Destination);
            boss.CurrentAction = action; action.Enter();
            await PumpUntil(action, () => action.IsAirborne && action.MovementProgress > .1f,
                "The dynamic-obstacle jump never left the floor.");
            Vector3 before = boss.transform.position;
            Vector3 blockerCenter = (occupyLanding ? action.Destination
                : Vector3.Lerp(before, action.Destination, .4f)) - f.Origin;
            blockerCenter.y = occupyLanding ? 1.5f : 5f;
            var blocker = f.Box(occupyLanding ? "Newly occupied landing" : "Wall appearing during jump", blockerCenter,
                occupyLanding ? new Vector3(2f, 3f, 2f) : new Vector3(20f, 10f, .3f));
            Physics.SyncTransforms();
            float highestZ = before.z;
            double deadline = Time.realtimeSinceStartupAsDouble + 3d;
            while (action.IsActive && !action.IsFinished)
            {
                if (Time.realtimeSinceStartupAsDouble > deadline)
                    throw new InvalidOperationException("A jump stayed active after its geometry became unsafe.");
                await Frames(1); action.Update();
                highestZ = Mathf.Max(highestZ, boss.transform.position.z);
            }
            Check(action.Failed && !action.IsActive && !string.IsNullOrEmpty(action.FailureReason),
                "A changed " + (occupyLanding ? "landing" : "flight path") + " must safely interrupt the jump.");
            Check(probe.Hits == 0, "An interrupted jump delivered landing damage without a successful landing.");
            CheckNavigationRestored(boss, "dynamic " + (occupyLanding ? "landing occupancy" : "flight wall"));
            Check(boss.GetComponent<CapsuleCollider>().enabled && !ParryEventBus.IsTelegraphActive(action),
                "A geometry interruption leaked collider or telegraph state.");
            if (occupyLanding)
                Check(Vector3.Distance(BossMobilityNavigation.Flat(before), BossMobilityNavigation.Flat(boss.transform.position)) < .3f,
                    "An occupied landing caused a teleport away from the last safe ground position.");
            else
                Check(highestZ < blocker.GetComponent<BoxCollider>().bounds.min.z - .3f,
                    "Jump cancellation teleported or moved the boss across the new solid wall.");
            blocker.SetActive(false);
            f.Stop(boss); Object.Destroy(probe.gameObject); await Frames(1);
        }
    }

    private static void CheckNavigationRestored(Boss boss, string context)
    {
        var nav = boss.GetComponent<NavMeshAgent>();
        Check(nav.enabled && nav.isOnNavMesh && nav.updatePosition && nav.updateRotation
            && Mathf.Abs(boss.transform.position.y - nav.nextPosition.y) < .1f,
            "Navigation/root ownership was not restored after " + context);
    }

    private static BossMobilitySkillSO[] Skills(BossMobilitySettings settings)
        => new[] { settings.escapeJump, settings.dashSlash, settings.jumpSlam };
    private static BossTacticalController Tactical(Boss boss) => (boss.Brain as BossBrain).Tactical;
    private static void Warp(Boss boss, Vector3 position)
    {
        Check(boss.GetComponent<NavMeshAgent>().Warp(position), "The test actor could not warp onto its fixture floor.");
        boss.transform.position = boss.GetComponent<NavMeshAgent>().nextPosition;
        Physics.SyncTransforms();
    }
    private static async Task PumpUntil(BossMobilityAction action, Func<bool> condition, string message)
    {
        double end = Time.realtimeSinceStartupAsDouble + 5d;
        while (!condition())
        {
            if (!action.IsActive || action.Failed || Time.realtimeSinceStartupAsDouble > end)
                throw new InvalidOperationException(message + " " + action.FailureReason);
            await Frames(1); action.Update();
        }
    }
    private static async Task Delay(Boss boss, float seconds)
    { float end = boss.ActionTime + seconds; await Until(() => boss.ActionTime >= end, "Actor time stopped advancing."); }
    private static async Task Frames(int count)
    { int frame = Time.frameCount + count; await Until(() => Time.frameCount >= frame, "Play Mode frames stopped advancing."); await Awaitable.EndOfFrameAsync(); }
    private static async Task Until(Func<bool> condition, string message)
    {
        double end = Time.realtimeSinceStartupAsDouble + 12d;
        while (!condition())
        { if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > end) throw new InvalidOperationException(message); await Task.Yield(); }
    }
    private static void Near(float actual, float expected, float tolerance, string message)
        => Check(Mathf.Abs(actual - expected) <= tolerance, message + " Expected " + expected + ", got " + actual);
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); _report.checks++; }
    private static void Set(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null) { field.SetValue(target, value); return; }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private sealed class Fixture
    {
        public readonly Vector3 Origin = new Vector3(18000f, 0f, 18000f);
        public readonly Transform Target;
        public readonly BaekhoMapModule Room;
        private readonly Scene _scene;
        private readonly List<Object> _assets = new List<Object>();
        private readonly List<NavMeshDataInstance> _navigation = new List<NavMeshDataInstance>();
        private readonly float _timeScale = Time.timeScale;
        private readonly UnityEngine.Random.State _random = UnityEngine.Random.state;
        private readonly CombatSettings _combatSettings = CombatSettings.Active;

        public Fixture()
        {
            Time.timeScale = 1f;
            _scene = SceneManager.CreateScene("BossMobilityRegression-" + Guid.NewGuid().ToString("N"));
            var room = new GameObject("Owned mobility room"); SceneManager.MoveGameObjectToScene(room, _scene);
            room.transform.position = Origin;
            Room = room.AddComponent<BaekhoMapModule>();
            Room.localBounds = new Bounds(new Vector3(0f, 4f, 0f), new Vector3(60f, 8f, 60f));
            var floor = Box("Owned supporting floor", Vector3.down * .25f, new Vector3(60f, .5f, 60f));
            Room.groundObjects = new[] { floor };
            AddNavigation(Origin, new Vector3(60f, .5f, 60f));
            var target = new GameObject("Assigned mobility target"); SceneManager.MoveGameObjectToScene(target, _scene);
            Target = target.transform; Target.position = Origin + Vector3.forward * 3f;
            var table = New<ElementEffectiveManager>();
            foreach (ElementType attack in Enum.GetValues(typeof(ElementType)))
                foreach (ElementType defense in Enum.GetValues(typeof(ElementType)))
                    table.elementEffectiveList.Add(new ElementEffective { AttackType = attack, DefenseType = defense, multiplier = 1f });
            table.Initialize();
            var settingsHost = new GameObject("Inactive neutral mobility damage table"); settingsHost.SetActive(false);
            SceneManager.MoveGameObjectToScene(settingsHost, _scene);
            var settings = settingsHost.AddComponent<CombatSettings>(); Set(settings, "_elementTable", table);
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, settings);
            Physics.SyncTransforms();
        }

        public GameObject Box(string name, Vector3 localCenter, Vector3 size)
        {
            var host = new GameObject(name); SceneManager.MoveGameObjectToScene(host, _scene);
            host.transform.position = Origin + localCenter;
            host.AddComponent<BoxCollider>().size = size;
            return host;
        }
        public GameObject[] EncloseStart() => new[]
        {
            Box("Blocked north", new Vector3(0f, 1.5f, 1.2f), new Vector3(5f, 3f, .3f)),
            Box("Blocked south", new Vector3(0f, 1.5f, -1.2f), new Vector3(5f, 3f, .3f)),
            Box("Blocked east", new Vector3(1.2f, 1.5f, 0f), new Vector3(.3f, 3f, 5f)),
            Box("Blocked west", new Vector3(-1.2f, 1.5f, 0f), new Vector3(.3f, 3f, 5f))
        };
        public void AddUpperFloor()
        { Box("Foreign upper floor", new Vector3(0f, 5.75f, 10f), new Vector3(12f, .5f, 12f)); AddNavigation(Origin + new Vector3(0f, 6f, 10f), new Vector3(12f, .5f, 12f)); }
        private void AddNavigation(Vector3 center, Vector3 size)
        {
            var navigation = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0),
                new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = size,
                    transform = Matrix4x4.TRS(center + Vector3.down * .25f, Quaternion.identity, Vector3.one) } },
                new Bounds(center, new Vector3(size.x, 10f, size.z)), Vector3.zero, Quaternion.identity);
            if (navigation == null) throw new InvalidOperationException("Could not build the synthetic mobility floor.");
            _assets.Add(navigation); _navigation.Add(NavMesh.AddNavMeshData(navigation));
        }
        private T New<T>() where T : ScriptableObject
        { var asset = ScriptableObject.CreateInstance<T>(); _assets.Add(asset); return asset; }
        public BossTacticalProbeSkill Probe()
        { var skill = New<BossTacticalProbeSkill>(); skill.duration = -1f; skill.cooldown = 0f; skill.engageRange = skill.executeRange = 100f; return skill; }
        public BossTacticalPatternSO Pattern(MonsterSkillData skill)
        { var pattern = New<BossTacticalPatternSO>(); pattern.steps.Add(new BossTacticalStep { kind = BossTacticalStepKind.Attack, skill = skill }); return pattern; }

        public async Task<Boss> Actor(bool tactical = false, bool twoPhases = false, BossTacticalPatternSO pattern = null)
        {
            Target.position = Origin + Vector3.forward * 3f;
            var staging = new GameObject("Inactive mobility actor staging"); staging.SetActive(false);
            SceneManager.MoveGameObjectToScene(staging, _scene);
            var host = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath), staging.transform);
            host.SetActive(false); host.transform.SetParent(Room.transform, false);
            host.transform.localPosition = Vector3.zero; host.transform.localRotation = Quaternion.identity;
            Object.Destroy(staging);
            var boss = host.GetComponent<ConfiguredBoss>();
            var data = Object.Instantiate(boss.Data); _assets.Add(data); data.rewardGroup = null; data.initialShield = 0f;
            data.phaseDownSeconds = .03f; data.phaseHoldSeconds = .03f; data.phaseReviveSeconds = .06f;
            data.statusImmunityList = new List<string>();
            var profile = New<BossCombatProfileSO>(); profile.decisionInterval = .02f; profile.turnSpeed = 0f;
            profile.preferredMinDistance = 2f; profile.preferredMaxDistance = 4f; profile.repositionDuration = 0f;
            profile.patterns.Add(new BossPatternOption { pattern = pattern ?? Pattern(Probe()), recovery = .2f, cooldown = 0f });
            var phases = new List<BossPhaseData>();
            for (int i = 0; i < (twoPhases ? 2 : 1); i++)
            {
                var phase = New<BossPhaseData>(); phase.phaseId = "mobility-test-" + i; phase.phaseMaxHp = 10000f;
                phase.combatProfile = profile; phase.skillSet = data.monsterSkillData; phases.Add(phase);
            }
            Set(boss, "BossData", data); Set(host.GetComponent<EnemyStat>(), "baseData", data); Set(boss, "PhaseDatas", phases);
            boss.CombatLabPassive = true; boss.SetCombatTarget(Target);
            foreach (var animator in host.GetComponentsInChildren<Animator>(true)) animator.fireEvents = false;
            host.SetActive(true);
            await Until(() => boss.Brain != null && boss.GetComponent<NavMeshAgent>().isOnNavMesh, "The mobility actor did not initialize on navigation.");
            if (!tactical) boss.Brain.Suspend();
            boss.CombatLabPassive = false;
            if (tactical) boss.Brain.Resume();
            await Frames(1);
            return boss;
        }
        public BossMobilityDamageProbe DamageProbe(Vector3 ground)
        {
            var host = new GameObject("Trigger-only mobility damage receiver"); SceneManager.MoveGameObjectToScene(host, _scene);
            host.layer = LayerMask.NameToLayer("Player"); host.transform.position = ground + Vector3.up;
            var collider = host.AddComponent<SphereCollider>(); collider.radius = .55f; collider.isTrigger = true;
            Physics.SyncTransforms(); return host.AddComponent<BossMobilityDamageProbe>();
        }
        public void Stop(Boss boss)
        { if (boss != null) { boss.CancelCurrentAction(); boss.gameObject.SetActive(false); Object.Destroy(boss.gameObject); } }
        public async Task Dispose()
        {
            try
            {
                if (_scene.IsValid() && _scene.isLoaded)
                {
                    foreach (var host in _scene.GetRootGameObjects()) host.SetActive(false);
                    var unload = SceneManager.UnloadSceneAsync(_scene);
                    if (unload != null) await Until(() => unload.isDone, "Mobility fixture cleanup timed out.");
                }
                foreach (var nav in _navigation) if (nav.valid) nav.Remove();
                foreach (var asset in _assets) if (asset != null) Object.Destroy(asset);
            }
            finally
            {
                typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, _combatSettings);
                Time.timeScale = _timeScale; UnityEngine.Random.state = _random;
            }
        }
    }
}

public sealed class BossMobilityDamageProbe : MonoBehaviour, IDamageable
{
    public int Hits { get; private set; }
    public object LastSource { get; private set; }
    public bool LastParryable { get; private set; }
    public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE,
        bool isCritical = false, Vector3 power = default, float poiseDamage = 20f,
        StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
    {
        Hits++; LastSource = source; LastParryable = isParryable;
        return new DamageResult(DamageOutcome.Applied, damage);
    }
}
