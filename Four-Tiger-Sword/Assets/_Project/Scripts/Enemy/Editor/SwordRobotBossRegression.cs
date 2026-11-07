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

/// <summary>Checks the authored sword robot and runs its real actions in an isolated temporary arena.</summary>
public static class SwordRobotBossRegression
{
    private const string PrefabPath = "Assets/_Project/Generated/Bosses/SwordRobotBoss/SwordRobotBoss.prefab";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static bool _running;
    private static int _checks;
    private static string _status = "idle";
    private static readonly List<LocomotionResult> _locomotion = new List<LocomotionResult>();
    private static readonly List<ComboMotionResult> _comboMotion = new List<ComboMotionResult>();

    [MenuItem("Tools/Studio Baekho/Enemy/Run Sword Robot Boss Authoring Regression")]
    private static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run authoring checks in Edit Mode.");
        _checks = 0;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Check(prefab != null, "SwordRobotBoss prefab has not been generated.");
        var boss = prefab.GetComponent<ConfiguredBoss>();
        Check(boss != null && prefab.GetComponent<NavMeshAgent>() != null,
            "The run boss needs ConfiguredBoss and NavMeshAgent on its root.");
        Check(prefab.GetComponent<IEnemySensor>() == null, "The sword robot must engage without exploration sensors.");
        Check(boss.PhaseConfiguration.Count == 2, "One revival requires exactly two phase HP pools.");
        Check(boss.Data != null && ReferenceEquals(boss.Data, prefab.GetComponent<EnemyStat>().Data),
            "Boss and EnemyStat must reference the same authored BossData.");
        Check(boss.Data.rewardGroup != null && boss.Data.rewardGroup.gold > 0,
            "The final boss death needs an explicit reward group.");
        var phase1 = boss.PhaseConfiguration[0];
        var phase2 = boss.PhaseConfiguration[1];
        Check(phase1.phaseMaxHp > 0f && phase2.phaseMaxHp > 0f
            && phase2.atkMultiplier > phase1.atkMultiplier, "Revival must restore a valid HP pool and increase attack strength.");
        foreach (var phase in boss.PhaseConfiguration)
        {
            Check(phase.combatProfile != null && !phase.combatProfile.Validate().Any(),
                "A phase tactical profile is missing or invalid: " + phase.phaseId);
            Check(phase.combatProfile.repositionDuration > 0f,
                "The sword robot must reposition after recovery even when another attack is ready.");
            Check(phase.skillSet != null && phase.skillSet.skillData != null,
                "Each phase needs its own explicit available skill set.");
        }
        Check(phase2.combatProfile.repositionDuration < phase1.combatProfile.repositionDuration,
            "Phase 2 must retain a faster attack tempo with a shorter reposition interval.");
        var first = Attacks(phase1).ToArray();
        var second = Attacks(phase2).ToArray();
        Check(first.Length == 3 && first.OfType<MonsterDefaultAttackSO>().Count() == 1
            && first.OfType<MonsterComboAttackSO>().Count() == 1 && first.OfType<MonsterBossChargeAttackSO>().Count() == 1,
            "Phase 1 must expose basic, three-hit combo and charged attack only.");
        Check(second.Length == 4 && second.OfType<MonsterComboAttackSO>().Count() == 1
            && second.OfType<MonsterBossChargeAttackSO>().Count() == 1 && second.OfType<MonsterDashAttackSO>().Count() == 1
            && second.OfType<MonsterDefaultAttackSO>().Count() == 1,
            "Phase 2 must expose combo, charge, dash and radial sweep only.");
        var basic = first.OfType<MonsterDefaultAttackSO>().Single();
        var sweep = second.OfType<MonsterDefaultAttackSO>().Single();
        Check(basic != sweep && sweep.hitBoxShape == HitBoxShape.Sphere && sweep.hitBoxRadius > basic.hitBoxRadius,
            "The phase-2 sweep needs a separate wider radial hitbox.");
        foreach (var combo in first.Concat(second).OfType<MonsterComboAttackSO>().Distinct())
        {
            Check(combo.hits.Count == 3 && combo.hits.All(hit => hit.overrideReaction),
                "The combo must author exactly three independent hit reactions.");
            Check(!combo.hits[0].knockdown && !combo.hits[1].knockdown && combo.hits[2].knockdown,
                "Only the combo finisher should knock the player down.");
            Check(combo.hits[0].knockbackForce < basic.knockbackForce
                && combo.hits[1].knockbackForce < basic.knockbackForce,
                "The first two combo swings must push less than the basic swing.");
            Check(combo.hits.All(hit => !string.IsNullOrWhiteSpace(hit.animationState)),
                "Every combo swing needs its own animation state.");
            Check(combo.stepStopDistance > 0f && combo.hits.All(hit => hit.stepDistance > 0f
                && hit.stepDuration > 0f && hit.stepStartTime >= hit.animationStartTime
                && hit.stepStartTime + hit.stepDuration <= hit.hitStartTime + hit.hitDuration),
                "Every sword robot combo swing needs a bounded forward step within its attack window.");
        }
        foreach (var charge in first.Concat(second).OfType<MonsterBossChargeAttackSO>().Distinct())
            Check(charge.chargeSeconds > 0f && charge.interruptOnKnockback && charge.recoveryTime > 0f,
                "Charge must have a visible hold, knockback weakness and punishable recovery.");
        var animator = prefab.GetComponentInChildren<Animator>(true);
        Check(animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman
            && animator.runtimeAnimatorController != null, "The existing robot needs a valid humanoid avatar and authored controller.");
        Check(animator.runtimeAnimatorController.animationClips.Length >= 8,
            "The boss controller is missing attack, locomotion or revival animation clips.");
        var playerController = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
            "Assets/_Project/Animations/AnimController/BDJ.controller");
        Check(playerController != null && playerController.layers[0].stateMachine.states
            .Any(state => state.state.name == "Knockdown" && state.state.motion is AnimationClip down && down.humanMotion)
            && playerController.layers[0].stateMachine.states
            .Any(state => state.state.name == "GetUp" && state.state.motion is AnimationClip up && up.humanMotion),
            "The real player controller needs humanoid down and get-up states for the combo finisher.");
        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Check(hand != null && hand.GetComponentsInChildren<MeshFilter>(true).Any(),
            "The existing greatsword must be attached below the robot's right hand.");
        var run = AssetDatabase.LoadAssetAtPath<RunDefinitionSO>("Assets/_Game/RunProgression/DefaultRun.asset");
        Check(run != null && run.chapters?.Length > 0 && run.chapters[0].stages?.Length > 0
            && run.chapters[0].stages[0].bossPrefab != null
            && run.chapters[0].stages[0].bossPrefab.GetComponent<Boss>() is Boss currentBoss
            && currentBoss.Data == boss.Data
            && currentBoss.PhaseConfiguration.SequenceEqual(boss.PhaseConfiguration),
            "DefaultRun must preserve its selected sword robot presentation and shared phase configuration.");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Generated/Bosses/Test_Boss/Test_Boss.prefab") != null,
            "The old Test_Boss regression fixture must remain available.");
        return $"Sword robot authoring: {_checks} checks passed.";
    }

    private static IEnumerable<MonsterSkillData> Attacks(BossPhaseData phase)
        => phase.combatProfile.patterns.Where(option => option?.pattern is BossTacticalPatternSO)
            .SelectMany(option => ((BossTacticalPatternSO)option.pattern).steps)
            .Where(step => step != null && step.kind == BossTacticalStepKind.Attack && step.skill != null)
            .Select(step => step.skill).Distinct();

    public static string StartPlayMode()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling || _running)
            throw new InvalidOperationException("Start sword robot runtime checks in idle Play Mode.");
        _running = true; _checks = 0; _status = "running"; _locomotion.Clear(); _comboMotion.Clear();
        _ = RunRuntime();
        return PlayModeStatus();
    }

    public static string PlayModeStatus() => JsonUtility.ToJson(new Result
    { status = _status, checks = _checks, locomotion = _locomotion.ToArray(), comboMotion = _comboMotion.ToArray() });
    [Serializable] private sealed class Result
    { public string status; public int checks; public LocomotionResult[] locomotion; public ComboMotionResult[] comboMotion; }
    [Serializable] private sealed class LocomotionResult
    {
        public int phase;
        public Vector3 start, end;
        public float displacement, repositionSeconds, authoredSeconds;
        public bool directionalWalk;
    }
    [Serializable] private sealed class ComboMotionResult
    {
        public int phase;
        public float attackSpeed, displacement;
        public float[] steps;
        public bool committedStep, stationaryRecovery;
    }

    private static async Task RunRuntime()
    {
        Fixture f = null;
        string result = "passed";
        try
        {
            f = new Fixture();
            await VerifyCombatLocomotion(f);
            await VerifyComboSteps(f);
            await VerifyComboStepCancellation(f);
            await VerifyComboSpacing(f);
            await VerifyChargeAndInstanceIsolation(f);
            await VerifyComboDispatch(f);
            await VerifyCommitmentAndCleanup(f);
            await VerifySingleRevival(f);
        }
        catch (Exception exception)
        {
            result = "failed: " + exception.GetBaseException().Message;
            Debug.LogException(exception);
        }
        finally
        {
            try { if (f != null) await f.Dispose(); }
            catch (Exception exception) { result = "cleanup failed: " + exception.GetBaseException().Message; Debug.LogException(exception); }
            _status = result; _running = false;
        }
    }

    private static async Task VerifyComboSteps(Fixture f)
    {
        for (int phase = 0; phase < 2; phase++)
        {
            var boss = f.Actor();
            try
            {
                await Until(() => boss.Brain != null, "The combo step fixture failed to initialize.");
                // The target has retreated from melee range; each committed step has room to complete.
                // Keep the real authored skills and their distinct 1 / 1.12 timeline speeds.
                f.Target.position = boss.transform.position + Vector3.forward * 8f;
                boss.transform.rotation = Quaternion.identity;
                var combo = Attacks(boss.PhaseConfiguration[phase]).OfType<MonsterComboAttackSO>().Single();
                var action = Start(boss, combo);
                Vector3 origin = boss.transform.position;
                var sample = new ComboMotionResult
                { phase = phase + 1, attackSpeed = combo.attackSpeed, steps = new float[combo.hits.Count] };
                for (int swing = 0; swing < combo.hits.Count; swing++)
                {
                    var hit = combo.hits[swing];
                    Vector3 stepOrigin = boss.transform.position;
                    if (swing == 1)
                    {
                        await Pump(action, () => action.AttackTimeline >= hit.stepStartTime + hit.stepDuration * .35f,
                            "The second combo step never began.");
                        await Task.Yield(); // Let the agent publish the most recent manual Move to its transform.
                        Vector3 committedPosition = boss.transform.position;
                        Quaternion committedRotation = boss.transform.rotation;
                        f.Target.position = boss.transform.position + Vector3.right * 8f;
                        await Pump(action, () => action.AttackTimeline >= hit.stepStartTime + hit.stepDuration,
                            "The second combo step never completed.");
                        await Task.Yield();
                        Vector3 committedMotion = boss.transform.position - committedPosition;
                        sample.committedStep = Quaternion.Angle(committedRotation, boss.transform.rotation) < .2f
                            && Mathf.Abs(Vector3.Dot(committedRotation * Vector3.right, committedMotion)) < .04f;
                        Check(sample.committedStep,
                            "A target sidestep bent the direction or path of an already committed combo step.");
                        f.Target.position = origin + Vector3.forward * 8f;
                    }
                    else
                    {
                        await Pump(action, () => action.AttackTimeline >= hit.stepStartTime + hit.stepDuration,
                            $"The authored combo never completed swing {swing + 1}'s movement window.");
                        await Task.Yield();
                    }
                    sample.steps[swing] = Vector3.Distance(stepOrigin, boss.transform.position);
                    Check(sample.steps[swing] > .15f && sample.steps[swing] <= hit.stepDistance + .08f,
                        $"Phase {phase + 1}, swing {swing + 1} did not visibly step within its distance budget: "
                        + sample.steps[swing] + " / " + hit.stepDistance);
                }
                await Pump(action, () => action.BossStage == BossAttackStage.Recovery && !action.IsFinished,
                    "The moving combo did not reach its punishable final recovery.");
                Vector3 recoveryPosition = boss.transform.position;
                await Pump(action, () => action.IsFinished, "The moving combo did not finish its recovery.");
                sample.stationaryRecovery = Vector3.Distance(recoveryPosition, boss.transform.position) < .04f;
                sample.displacement = Vector3.Distance(origin, boss.transform.position);
                _comboMotion.Add(sample);
                Check(sample.stationaryRecovery,
                    "The combo continued stepping through its final punishable recovery.");
                Check(sample.displacement <= combo.hits.Sum(hit => hit.stepDistance) + .1f,
                    "The accelerated combo exceeded its authored total step distance.");
            }
            finally
            {
                boss.CancelCurrentAction();
                boss.gameObject.SetActive(false);
            }
        }
        Check(Mathf.Abs(_comboMotion[0].displacement - _comboMotion[1].displacement) < .15f,
            "Increasing combo attackSpeed changed its travel distance instead of only its timing.");
    }

    private static async Task VerifyComboStepCancellation(Fixture f)
    {
        for (int parry = 0; parry < 2; parry++)
        {
            var boss = f.Actor();
            try
            {
                await Until(() => boss.Brain != null, "The combo interruption fixture failed to initialize.");
                f.Target.position = boss.transform.position + Vector3.forward * 8f;
                boss.transform.rotation = Quaternion.identity;
                var combo = Attacks(boss.PhaseConfiguration[0]).OfType<MonsterComboAttackSO>().Single();
                var action = Start(boss, combo);
                var first = combo.hits[0];
                Vector3 origin = boss.transform.position;
                await Pump(action, () => action.AttackTimeline >= first.stepStartTime + first.stepDuration * .4f,
                    "The interrupted combo never entered its first moving swing.");
                await Task.Yield();
                Check(Vector3.Distance(origin, boss.transform.position) > .1f,
                    "The cancellation check must interrupt an actively moving combo.");
                if (parry == 0) boss.CancelCurrentAction();
                else action.OnParried();
                await Task.Yield(); // A Move submitted before interruption may publish on the following frame.
                Vector3 stopped = boss.transform.position;
                float until = Time.time + .4f;
                await Pump(action, () => Time.time >= until, "The interrupted combo movement check timed out.");
                Check(Vector3.Distance(stopped, boss.transform.position) < .04f
                    && (parry == 0 ? !action.IsActive : action.WasParried),
                    parry == 0 ? "Cancelling a combo left its forward step running."
                        : "Parrying a combo left its forward step running.");
            }
            finally
            {
                boss.CancelCurrentAction();
                boss.gameObject.SetActive(false);
            }
        }
    }

    private static async Task VerifyComboSpacing(Fixture f)
    {
        var boss = f.Actor();
        try
        {
            await Until(() => boss.Brain != null, "The close-target combo fixture failed to initialize.");
            f.Target.position = boss.transform.position + Vector3.forward * 3f;
            boss.transform.rotation = Quaternion.identity;
            var combo = Attacks(boss.PhaseConfiguration[1]).OfType<MonsterComboAttackSO>().Single();
            var action = Start(boss, combo);
            float closest = Vector3.Distance(boss.transform.position, f.Target.position);
            await Pump(action, () =>
            {
                closest = Mathf.Min(closest, Vector3.Distance(boss.transform.position, f.Target.position));
                return action.IsFinished;
            }, "The close-target combo did not complete.");
            Check(closest >= combo.stepStopDistance - .04f
                && Vector3.Distance(boss.transform.position, f.Target.position) < combo.stepStopDistance + .15f,
                "The accelerated combo must close distance and stop at its authored target spacing: " + closest);
        }
        finally
        {
            boss.CancelCurrentAction();
            boss.gameObject.SetActive(false);
        }
    }

    private static async Task VerifyCombatLocomotion(Fixture f)
    {
        for (int phase = 0; phase < 2; phase++)
        {
            // Use the authored prefab/profile without synthetic attacks or manual action ticks.
            // Retire each actor before the next phase probe so avoidance cannot alter its motion.
            var boss = f.Actor();
            try
            {
                await Until(() => boss.Brain != null, "The locomotion fixture failed to initialize.");
                if (phase == 1)
                {
                    boss.SetCombatTarget(null);
                    boss.CombatLabPassive = false;
                    boss.TakeDamage(10000000f, poiseDamage: 0f);
                    await Until(() => boss.PhaseIndex == 1 && !boss.IsTransitioning,
                        "The locomotion fixture failed to revive into its real second-phase profile.");
                }
                f.Target.position = boss.transform.position + Vector3.forward * 3f;
                boss.transform.rotation = Quaternion.identity;
                boss.SetCombatTarget(f.Target);
                boss.CombatLabPassive = false;
                var tactical = ((BossBrain)boss.Brain).Tactical;
                var profile = boss.ActiveCombatProfile;
                Check(profile == boss.PhaseConfiguration[phase].combatProfile && profile.repositionDuration > 0f,
                    "Locomotion must exercise the actual phase combat profile.");
                if (boss.PhaseConfiguration[phase].entryPattern != null)
                    await VerifyAndAwaitPhaseEntry(boss);
                await Until(() => boss.CurrentAction != null && boss.CurrentAction.IsActive,
                    "The real profile never selected its first attack before locomotion.");
                var action = boss.CurrentAction;
                var option = Field<BossPatternOption>(tactical, "_option");
                Check(option != null && profile.patterns.Contains(option),
                    "The locomotion cycle must begin with an authored pattern selection.");
                await Until(() => action.BossStage == BossAttackStage.Recovery && !action.IsFinished,
                    "The selected attack did not retain an observable stationary recovery.");
                Vector3 recoveryPosition = boss.transform.position;
                bool stationary = true;
                await Until(() =>
                {
                    stationary &= Vector3.Distance(recoveryPosition, boss.transform.position) < .04f;
                    return tactical.DebugState == "Recovering" && boss.CurrentAction == null;
                }, "The selected action did not finish into the profile's recovery window.");
                Check(stationary && action.IsFinished && !action.IsActive,
                    "Combat locomotion began before the real attack's recovery finished.");
                Check(Mathf.Abs(Field<float>(tactical, "_remaining") - option.recovery) < .04f,
                    "The normal pattern recovery was shortened before repositioning.");
                float recoveryStarted = Time.time, maxDelta = Time.deltaTime;
                await Until(() =>
                {
                    maxDelta = Mathf.Max(maxDelta, Time.deltaTime);
                    stationary &= Vector3.Distance(recoveryPosition, boss.transform.position) < .04f;
                    return tactical.DebugState == "Repositioning";
                }, "A ready melee attack bypassed the required reposition interval.");
                Check(stationary && Time.time - recoveryStarted + maxDelta + .02f >= option.recovery,
                    "Repositioning must follow the complete stationary punish window.");
                var sample = new LocomotionResult
                {
                    phase = phase + 1, start = boss.transform.position,
                    authoredSeconds = profile.repositionDuration
                };
                float moveStarted = Time.time;
                bool attackedDuringMove = false;
                maxDelta = Time.deltaTime;
                await Until(() =>
                {
                    maxDelta = Mathf.Max(maxDelta, Time.deltaTime);
                    attackedDuringMove |= boss.CurrentAction != null;
                    var animator = boss.Animator;
                    bool moveState = animator.GetCurrentAnimatorStateInfo(0).IsName("Move")
                        || (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName("Move"));
                    sample.directionalWalk |= moveState
                        && new Vector2(animator.GetFloat("DirX"), animator.GetFloat("DirY")).sqrMagnitude > .04f;
                    return tactical.DebugState != "Repositioning";
                }, "The real boss failed to finish its reposition interval.");
                sample.end = boss.transform.position;
                sample.displacement = Vector3.Distance(sample.start, sample.end);
                sample.repositionSeconds = Time.time - moveStarted;
                _locomotion.Add(sample);
                Check(!attackedDuringMove && sample.repositionSeconds + maxDelta + .02f >= sample.authoredSeconds,
                    "An available attack interrupted the authored reposition interval.");
                Check(sample.displacement > .25f && sample.directionalWalk,
                    $"Phase {phase + 1} failed to visibly move with its directional walk blend: "
                    + JsonUtility.ToJson(sample) + "; " + tactical.DebugReason);
                Check(string.IsNullOrEmpty(tactical.LastFailure),
                    "The prefab's locomotion encountered a movement failure: " + tactical.LastFailure);
                await Until(() => boss.CurrentAction != null && boss.CurrentAction != action && boss.CurrentAction.IsActive,
                    "The real profile failed to resume attacks after repositioning.");
                Check(boss.PhaseIndex == phase && boss.ActiveCombatProfile == profile,
                    "A movement cycle unexpectedly changed its active phase/profile.");
            }
            finally
            {
                boss.CancelCurrentAction();
                boss.gameObject.SetActive(false);
            }
        }
    }

    private static async Task VerifyAndAwaitPhaseEntry(Boss boss)
    {
        var configuration = boss.PhaseConfiguration[boss.PhaseIndex];
        var tactical = ((BossBrain)boss.Brain).Tactical;
        await Until(() => boss.CurrentAction is BossPhaseBurstAction && boss.CurrentAction.IsActive,
            "The authored phase opener did not run before regular combat.");
        var action = (BossPhaseBurstAction)boss.CurrentAction;
        var skill = (BossPhaseBurstSkillSO)action.SkillData;
        var option = Field<BossPatternOption>(tactical, "_option");
        Check(option != null && option.pattern == configuration.entryPattern
            && !configuration.combatProfile.patterns.Contains(option),
            "The phase opener must execute from the entry slot, outside repeatable combat options.");
        await Until(() => !action.IsActive && boss.CurrentAction != action,
            "The authored phase opener failed to finish before the locomotion cycle.");
        Check(action.IsFinished && !action.WasParried
            && action.AttackTimeline + .02f >= skill.chargeDuration + skill.activeDuration + skill.recoveryTime,
            "The phase opener was cancelled or lost its full warning, impact or recovery timing.");
        Check(!tactical.IsEntryPending && string.IsNullOrEmpty(tactical.LastFailure)
            && boss.GetComponent<SwordRobotBossPresentation>().BurstEffectCount == 1,
            "The one-time phase opener failed, remained queued or repeated before normal combat.");
    }

    private static async Task VerifyChargeAndInstanceIsolation(Fixture f)
    {
        var first = f.Actor(); var second = f.Actor(10f);
        await Until(() => first.Brain != null && second.Brain != null, "The real boss prefab failed to initialize.");
        var charge = f.Copy(Attacks(first.PhaseConfiguration[0]).OfType<MonsterBossChargeAttackSO>().Single());
        charge.cooldown = 20f;
        var action = Start(first, charge);
        Check(!first.IsSkillReady(charge) && second.IsSkillReady(charge),
            "Starting a shared charge must set cooldown on its owner only.");
        await Pump(action, () => ((EnemyBossChargeAttack)action).IsCharging && first.ActorClock.AnimationSpeed == 0f,
            "The authored charge never entered its vulnerable charge period.");
        float hp = first.EnemyStat.CurrentHp;
        float poise = first.GetComponent<PoiseHandler>().PoiseRatio;
        first.TakeDamage(1f, poiseDamage: 1f, staggerResistLevel: StaggerResistLevel.HIGH);
        Check(first.EnemyStat.CurrentHp < hp && first.GetComponent<PoiseHandler>().PoiseRatio < poise,
            "Charge armor must retain both HP and poise damage.");
        Check(action.IsActive && !first.IsHurt && !first.IsGroggy,
            "An ordinary hit without knockback must not cancel the protected charge.");
        first.TakeDamage(1f, power: Vector3.back * 8f, poiseDamage: 0f,
            staggerResistLevel: StaggerResistLevel.HIGH);
        await Until(() => first.IsGroggy, "Eligible knockback during charge did not produce groggy.");
        Check(!action.IsActive && !action.HasBossArmor && !ParryEventBus.IsTelegraphActive(action),
            "An interrupted charge left hit execution, armor or telegraph active.");
        Check(first.ActorClock.AnimationSpeed == 1f, "Charge interruption left the animation frozen.");
        Check(!second.IsGroggy && second.CurrentAction == null && second.PhaseIndex == 0,
            "Interrupting one robot contaminated the other robot's runtime state.");
        first.gameObject.SetActive(false); second.gameObject.SetActive(false);
    }

    private static async Task VerifyComboDispatch(Fixture f)
    {
        var boss = f.Actor();
        await Until(() => boss.Brain != null, "The combo fixture failed to initialize.");
        var combo = f.Copy(Attacks(boss.PhaseConfiguration[0]).OfType<MonsterComboAttackSO>().Single());
        var player = f.Player();
        // This overlap target spans the authored swing offsets and the combo's spacing-limited advance.
        var probe = f.Probe(boss.transform.position + Vector3.up);
        probe.player = player;
        var action = Start(boss, combo);
        await Pump(action, () => probe.hits.Count == 3, "The authored combo failed to deliver its final swing.",
            () => $"hits={probe.hits.Count}, timeline={Field<float>(action, "_timer")}, active={action.IsActive}, finished={action.IsFinished}, current={boss.CurrentAction == action}, hurt={boss.IsHurt}, groggy={boss.IsGroggy}, root={boss.IsRoot}, knockback={boss.IsKnockbacking}, table={CombatSettings.Active != null}");
        await Until(() => player.StateMachine.CurrentState is PlayerKnockdownState,
            "The real player damage/reaction routing did not enter knockdown on the third combo hit.");
        Check(player.IsKnockedDown && !((PlayerKnockdownState)player.StateMachine.CurrentState).IsGettingUp,
            "Combo knockdown must begin with a committed down interval.");
        var recovery = VerifyPlayerRecovery(player);
        await Pump(action, () => action.IsFinished, "The authored combo failed to finish.");
        await recovery;
        Check(probe.hits.Count == 3, "The combo must deliver exactly one hit per swing to a persistent target.");
        Check(probe.hits.All(hit => ReferenceEquals(hit.source, action)),
            "All combo swings must retain EnemyComboAttack as damage/parry source.");
        Check(!probe.hits[0].down && !probe.hits[1].down && probe.hits[2].down
            && probe.hits[2].downSeconds > 0f && probe.hits[2].getUpSeconds > 0f,
            "The actual third damage dispatch must carry its authored down and get-up reaction.");
        Check(probe.hits[0].power.magnitude < probe.hits[2].power.magnitude
            && probe.hits[1].power.magnitude < probe.hits[2].power.magnitude,
            "The combo runtime ignored its independent weak/strong push values.");
        Check(!action.TryGetPlayerKnockdown(out _, out _), "Combo knockdown metadata leaked beyond the active hit callback.");
        VerifyDodgeAndParry(player, boss, combo);
        boss.CancelCurrentAction(); boss.gameObject.SetActive(false); probe.gameObject.SetActive(false);
        player.gameObject.SetActive(false);
    }

    private static async Task VerifyPlayerRecovery(PlayerController player)
    {
        await Until(() => player.StateMachine.CurrentState is PlayerKnockdownState down && down.IsGettingUp,
            "Player knockdown did not advance into its get-up interval.");
        Check(player.IsKnockedDown, "The get-up interval released player actions before recovery ended.");
        await Until(() => !player.IsKnockedDown && !(player.StateMachine.CurrentState is PlayerKnockdownState),
            "Player input remained locked after its down/get-up duration.");
        Check(player.PendingHitReaction == PlayerHitReaction.None && player.ActorClock.AnimationSpeed == 1f,
            "Player knockdown cleanup left a pending reaction or modified animation speed.");
    }

    private static void VerifyDodgeAndParry(PlayerController player, Boss boss, MonsterSkillData skill)
    {
        var source = new SwordRobotBossDownAction(skill, boss);
        float hp = player.StatManager.CurrentHp;
        player.StateMachine.ChangeState(new PlayerDashState(player));
        var dodged = player.TakeDamage(1f, power: Vector3.forward * 8f,
            staggerResistLevel: StaggerResistLevel.HIGH, source: source);
        Check(!dodged.Applied && player.StatManager.CurrentHp == hp && !player.IsKnockedDown,
            "A dodged third-hit source must not damage or knock the player down.");
        var parry = new PlayerParryState(player);
        player.StateMachine.ChangeState(parry);
        // This isolates damage during an established parry window from form-selection input.
        Set(parry, "<ParriedHitCount>k__BackingField", 1);
        Set(parry, "_lockedSource", source);
        var parried = player.TakeDamage(1f, power: Vector3.forward * 8f,
            staggerResistLevel: StaggerResistLevel.HIGH, source: source);
        Check(parried.Outcome == DamageOutcome.Parried && player.StatManager.CurrentHp == hp
            && !player.IsKnockedDown && source.WasParried,
            "A parried third-hit source must retain parry outcome and never request knockdown.");
        player.CancelHitStops(); player.StopParryKnockback();
        player.StateMachine.ChangeState(new PlayerIdleState(player));
    }

    private static async Task VerifyCommitmentAndCleanup(Fixture f)
    {
        var boss = f.Actor();
        await Until(() => boss.Brain != null, "The aim fixture failed to initialize.");
        var basic = Attacks(boss.PhaseConfiguration[0]).OfType<MonsterDefaultAttackSO>().Single();
        var action = Start(boss, basic);
        await Pump(action, () => action.BossAimLocked, "The basic swing never committed its direction.");
        var rotation = boss.transform.rotation;
        f.Target.position = boss.transform.position + Vector3.left * 5f;
        await Pump(action, () => action.BossStage == BossAttackStage.Recovery || action.IsFinished,
            "The basic swing did not reach recovery.");
        Check(Quaternion.Angle(rotation, boss.transform.rotation) < .1f,
            "A player dodging after direction lock bent the committed swing.");
        Check(action.BossStage == BossAttackStage.Recovery && !action.IsFinished,
            "The basic swing must retain an observable recovery after its damage window.");
        boss.gameObject.SetActive(false);
        Check(!action.IsActive && !action.HasBossArmor && !ParryEventBus.IsTelegraphActive(action)
            && Field<int>(boss, "_moveLockCount") == 0, "Disable left an active attack, telegraph, armor or movement lock.");
    }

    private static async Task VerifySingleRevival(Fixture f)
    {
        var boss = f.Actor();
        await Until(() => boss.Brain != null, "The revival fixture failed to initialize.");
        int depletions = 0, deaths = 0;
        boss.OnPhaseDepleted += _ => depletions++;
        boss.OnDied += () => deaths++;
        var charge = Attacks(boss.PhaseConfiguration[0]).OfType<MonsterBossChargeAttackSO>().Single();
        var action = Start(boss, charge);
        boss.CombatLabPassive = false;
        boss.SetCombatTarget(null);
        var hit = boss.TakeDamage(10000000f, poiseDamage: 0f);
        Check(hit.Applied && !hit.Killed && boss.IsTransitioning && !boss.IsDead && deaths == 0 && depletions == 1,
            "The first HP pool must start revival without death or reward eligibility.");
        Check(!boss.TakeDamage(10000000f, poiseDamage: 0f).Applied,
            "Revival accepted a same-frame follow-up hit.");
        await Until(() => boss.PhaseIndex == 1 && !boss.IsTransitioning,
            "The real configured robot did not revive into phase 2.");
        Check(Mathf.Approximately(boss.EnemyStat.CurrentHp, boss.PhaseConfiguration[1].phaseMaxHp)
            && !action.IsActive && !ParryEventBus.IsTelegraphActive(action),
            "Revival failed to fill the next HP pool or clean the first phase's charge.");
        Check(boss.EnemyStat.GetStat(EnemyStatType.ATK) > boss.Data.baseAtk * boss.PhaseConfiguration[0].atkMultiplier,
            "The revived robot did not become stronger.");
        if (boss.PhaseConfiguration[1].entryPattern != null)
        {
            Check(boss.CombatTarget == null && !boss.IsInvulnerable
                && ((BossBrain)boss.Brain).Tactical.IsEntryPending,
                "A targetless phase opener must remain queued without granting combat invulnerability.");
            f.Target.position = boss.transform.position + Vector3.forward * 3f;
            boss.SetCombatTarget(f.Target);
            await VerifyAndAwaitPhaseEntry(boss);
            boss.SetCombatTarget(null);
        }
        var final = boss.TakeDamage(10000000f, poiseDamage: 0f);
        await Until(() => boss.IsDead && boss.CurrentAction == null, "The last HP pool did not confirm final death.");
        Check(final.Killed && deaths == 1 && depletions == 1 && !boss.IsTransitioning,
            "A two-phase robot must revive exactly once and die exactly once.");
        Check(!boss.TakeDamage(1f, poiseDamage: 0f).Applied && deaths == 1,
            "A dead robot accepted damage or emitted a duplicate reward-eligible death.");
    }

    private static EnemyAction Start(Boss boss, MonsterSkillData skill)
    {
        boss.CancelCurrentAction();
        var action = skill.CreateAction(boss);
        action.ConfigureBossPolicy(new BossAttackPolicy
        { enabled = true, turnSpeed = 180f, lockFraction = .6f, armorDuringStartup = true, armorDuringActive = true });
        boss.CurrentAction = action; action.Enter();
        return action;
    }

    private static async Task Pump(EnemyAction action, Func<bool> done, string message, Func<string> diagnostics = null)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 20d;
        while (!done())
        {
            if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > deadline)
                throw new InvalidOperationException(message + (diagnostics != null ? " " + diagnostics() : ""));
            await Task.Yield();
            action.Update();
        }
        _checks++;
    }

    private static async Task Until(Func<bool> done, string message)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 20d;
        while (!done())
        {
            if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > deadline)
                throw new InvalidOperationException(message);
            await Task.Yield();
        }
        _checks++;
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); _checks++; }

    private static FieldInfo FindField(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Fields | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }
    private static void Set(object target, string name, object value) => FindField(target, name).SetValue(target, value);
    private static T Field<T>(object target, string name) => (T)FindField(target, name).GetValue(target);

    private sealed class Fixture
    {
        public readonly Vector3 Origin = new Vector3(16000f, 0f, 16000f);
        public readonly Transform Target;
        private readonly Scene _scene;
        private readonly List<Object> _objects = new List<Object>();
        private readonly NavMeshDataInstance _nav;
        private readonly float _timeScale = Time.timeScale;
        private readonly UnityEngine.Random.State _random = UnityEngine.Random.state;
        private readonly CombatSettings _previousSettings = CombatSettings.Active;

        public Fixture()
        {
            Time.timeScale = 1f;
            _scene = SceneManager.CreateScene("SwordRobotBossRegression-" + Guid.NewGuid().ToString("N"));
            var source = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.TRS(Origin + Vector3.down * .25f, Quaternion.identity, Vector3.one),
                size = new Vector3(60f, .5f, 60f), area = 0 };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource> { source },
                new Bounds(Origin, new Vector3(60f, 10f, 60f)), Vector3.zero, Quaternion.identity);
            if (data == null) throw new InvalidOperationException("Could not build the temporary sword robot arena.");
            _objects.Add(data); _nav = NavMesh.AddNavMeshData(data);
            var target = new GameObject("Sword robot assigned target");
            SceneManager.MoveGameObjectToScene(target, _scene); Target = target.transform;
            Target.position = Origin + Vector3.forward * 3f;
            var table = ScriptableObject.CreateInstance<ElementEffectiveManager>();
            _objects.Add(table);
            foreach (ElementType attack in Enum.GetValues(typeof(ElementType)))
                foreach (ElementType defense in Enum.GetValues(typeof(ElementType)))
                    table.elementEffectiveList.Add(new ElementEffective { AttackType = attack, DefenseType = defense, multiplier = 1f });
            table.Initialize();
            var settingsHost = new GameObject("Inactive sword robot neutral combat table");
            settingsHost.SetActive(false); SceneManager.MoveGameObjectToScene(settingsHost, _scene);
            var settings = settingsHost.AddComponent<CombatSettings>();
            Set(settings, "_elementTable", table);
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, settings);
        }

        public T Copy<T>(T original) where T : Object
        { var copy = Object.Instantiate(original); _objects.Add(copy); return copy; }

        public ConfiguredBoss Actor(float x = 0f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Generate SwordRobotBoss before runtime checks.");
            var host = Object.Instantiate(prefab, Origin + Vector3.right * x, Quaternion.identity);
            host.SetActive(false); SceneManager.MoveGameObjectToScene(host, _scene);
            var boss = host.GetComponent<ConfiguredBoss>();
            var data = Copy(boss.Data); data.rewardGroup = null;
            data.phaseDownSeconds = .05f; data.phaseHoldSeconds = .05f; data.phaseReviveSeconds = .1f;
            Set(boss, "BossData", data); Set(boss.GetComponent<EnemyStat>(), "baseData", data);
            // Instantiating an active prefab may have initialized stats before the isolated data was installed.
            typeof(EnemyStat).GetMethod("InitializeFromData", Fields).Invoke(boss.GetComponent<EnemyStat>(), null);
            boss.CombatLabPassive = true;
            boss.SetCombatTarget(Target);
            foreach (var animator in host.GetComponentsInChildren<Animator>(true)) animator.fireEvents = false;
            host.SetActive(true);
            return boss;
        }

        public SwordRobotBossHitProbe Probe(Vector3 position)
        {
            var host = new GameObject("Sword robot persistent player overlap");
            SceneManager.MoveGameObjectToScene(host, _scene); host.transform.position = position;
            host.layer = LayerMask.NameToLayer("Player");
            // Match the production CharacterController's solid collider even when global trigger queries are disabled.
            var collider = host.AddComponent<SphereCollider>(); collider.radius = 3f; collider.isTrigger = false;
            Physics.SyncTransforms();
            return host.AddComponent<SwordRobotBossHitProbe>();
        }

        public SwordRobotBossPlayerProbe Player()
        {
            var host = new GameObject("Isolated sword robot player reaction fixture");
            host.SetActive(false); SceneManager.MoveGameObjectToScene(host, _scene);
            host.transform.position = Origin + Vector3.forward * 3f;
            var animator = host.AddComponent<Animator>();
            animator.fireEvents = false;
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)
                .GetComponentInChildren<Animator>(true).runtimeAnimatorController;
            host.AddComponent<CharacterController>();
            host.AddComponent<PlayerInputHandler>();
            host.AddComponent<PlayerMovement>();
            host.AddComponent<PlayerStatManager>();
            var player = host.AddComponent<SwordRobotBossPlayerProbe>();
            var stats = ScriptableObject.CreateInstance<PlayerStatData>();
            stats.baseHP = 100000f; stats.baseDEF = 0f; _objects.Add(stats);
            player.GetComponent<PlayerStatManager>().Initialize(stats);
            // The fixture is driven by production reaction states, without real keyboard/device input.
            player.GetComponent<PlayerInputHandler>().enabled = false;
            host.SetActive(true);
            return player;
        }

        public async Task Dispose()
        {
            try
            {
                if (_scene.IsValid() && _scene.isLoaded)
                {
                    foreach (var host in _scene.GetRootGameObjects())
                        foreach (var enemy in host.GetComponentsInChildren<Enemy>(true)) enemy.CancelCurrentAction();
                    var operation = SceneManager.UnloadSceneAsync(_scene);
                    while (operation != null && !operation.isDone) await Task.Yield();
                }
                if (_nav.valid) _nav.Remove();
                foreach (var item in _objects) if (item != null) Object.Destroy(item);
            }
            finally
            {
                typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, _previousSettings);
                Time.timeScale = _timeScale; UnityEngine.Random.state = _random;
            }
        }
    }
}

public sealed class SwordRobotBossHitProbe : MonoBehaviour, IDamageable
{
    public PlayerController player;
    public struct Hit { public object source; public Vector3 power; public bool down; public float downSeconds, getUpSeconds; }
    public readonly List<Hit> hits = new List<Hit>();
    public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE,
        bool isCritical = false, Vector3 power = default, float poiseDamage = 20f,
        StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
    {
        float downSeconds = 0f, getUpSeconds = 0f;
        bool down = source is EnemyAction action && action.TryGetPlayerKnockdown(out downSeconds, out getUpSeconds);
        hits.Add(new Hit { source = source, power = power, down = down, downSeconds = downSeconds, getUpSeconds = getUpSeconds });
        return player != null ? player.TakeDamage(damage, damageType, isCritical, power, poiseDamage,
            staggerResistLevel, isParryable, source) : new DamageResult(DamageOutcome.Applied, damage);
    }
}

/// <summary>Uses the production damage handler/state graph without registering a second player with global HUDs.</summary>
public sealed class SwordRobotBossPlayerProbe : PlayerController
{
    private void Assign(string name, object value) => typeof(PlayerController).GetProperty(name).SetValue(this, value);
    private void Awake()
    {
        var stats = GetComponent<PlayerStatManager>();
        stats.Heal(100000f);
        Assign(nameof(StatManager), stats);
        Assign(nameof(Animator), GetComponent<Animator>());
        Assign(nameof(Controller), GetComponent<CharacterController>());
        Assign(nameof(Input), GetComponent<PlayerInputHandler>());
        Assign(nameof(Movement), GetComponent<PlayerMovement>());
        Assign(nameof(ActorClock), CombatActorClock.GetOrCreate(this));
        Assign(nameof(FormManager), new FormManager(this));
        Movement.Initialize(this);
        FormManager.ChangeForm(new ProbeForm());
        Gravity = 0f;
        KnockdownAnimationName = "PhaseDown";
        GetUpAnimationName = "PhaseRevive";
        Assign(nameof(StateMachine), new PlayerStateMachineSetup(this).Build());
        typeof(PlayerController).GetField("_hitStopDuration", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(this, 0f);
    }
    private void Start() { }
    private void Update() => StateMachine?.Update(ActionDeltaTime > 0f, ActionDeltaTime > 0f);
    private void OnDisable()
    {
        CancelHitStops(); StopParryKnockback();
        StateMachine?.CurrentState?.Exit();
        ConsumeHitReaction();
    }
    private sealed class ProbeForm : BaseForm
    {
        public ProbeForm() : base(null) { }
        public override void BeginParry() { }
        public override void EndParry() { }
    }
}

public sealed class SwordRobotBossDownAction : EnemyAction
{
    public SwordRobotBossDownAction(MonsterSkillData skill, Enemy enemy) : base(skill, enemy) { }
    public override bool TryGetPlayerKnockdown(out float downSeconds, out float getUpSeconds)
    { downSeconds = .2f; getUpSeconds = .2f; return true; }
}
