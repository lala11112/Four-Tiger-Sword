using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using StudioBaekho;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real Start/Update coverage in a temporary runtime scene, with no asset writes.</summary>
public static partial class EnemyBrainRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool _running;
    private static int _checks;
    private static string _status = "idle";

    public static string StartPlayMode()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Start the enemy brain regression in idle Play Mode.");
        if (_running) throw new InvalidOperationException("Enemy brain regression is already running.");
        _running = true;
        _checks = 0;
        _status = "running";
        _ = RunRuntime();
        return PlayModeStatus();
    }

    public static string PlayModeStatus() => JsonUtility.ToJson(new Result
    { status = _status, checks = _checks }, true);

    [Serializable] private sealed class Result { public string status; public int checks; }

    private static async Task RunRuntime()
    {
        RuntimeFixture fixture = null;
        string outcome = "passed";
        try
        {
            fixture = new RuntimeFixture();
            await ExercisePerceptionAndGroupCombat(fixture);
            await fixture.DisposeAsync();
            fixture = null;
            fixture = new RuntimeFixture();
            await ExerciseGroupAwareness(fixture);
            await ExerciseGroupAttackTiming(fixture);
            await ExerciseGroupAttackQueue(fixture);
            await ExerciseGroupCounterTiming(fixture);
            await ExerciseGroupPositioning(fixture);
            await ExerciseAuthoredMixedGroup(fixture);
            await fixture.DisposeAsync();
            fixture = null;
            fixture = new RuntimeFixture();
            await ExerciseCounterattacks(fixture);
            await fixture.DisposeAsync();
            fixture = null;
            fixture = new RuntimeFixture();
            await ExerciseNormalEnemy(fixture);
            await ExerciseNormalRecoveryAndMemory(fixture);
            await ExercisePatrolAndSpacing(fixture);
            await ExerciseAuthoredSpacing(fixture, "Assets/_Project/Prefabs/Drone.prefab", 2f);
            await ExerciseAuthoredSpacing(fixture, "Assets/_Project/Generated/Enemies/Hover_new_V2/Hover_new_V2.prefab", 8f);
            await ExerciseBoss(fixture);
            await ExerciseBossSequence(fixture);
            await ExerciseAuthoredBossCombat(fixture);
            await ExerciseBossPhaseBoundaries(fixture);
            await ExercisePatternCallbacksAndRequests(fixture);
            await ExerciseExistingPrefab(fixture);
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

    private static async Task ExerciseNormalEnemy(RuntimeFixture fixture)
    {
        var skill = fixture.New<EnemyBrainProbeSkill>();
        var skills = fixture.SkillSet(skill);
        var enemy = fixture.Actor<Enemy>("Normal AI", skills, out var sensor);
        enemy.GetComponent<NavMeshAgent>().speed = 0f;
        await Until(() => enemy.Brain != null, "Enemy.Start did not create its default brain.");
        Check(enemy.Brain is NormalEnemyBrain, "An unchanged Enemy must select NormalEnemyBrain.");
        Check(enemy.stateName == nameof(EnemyIdleState), "An undetected enemy must start idle.");

        sensor.Target = fixture.Target;
        fixture.Target.position = enemy.transform.position + Vector3.forward * 6f;
        await Until(() => enemy.stateName == nameof(EnemyTraceState), "Detection did not enter trace.");
        fixture.Target.position = enemy.transform.position + Vector3.forward * 3f;
        await Until(() => enemy.stateName == nameof(EnemyApproachState), "Combat range did not select and approach an attack.");
        fixture.Target.position = enemy.transform.position + Vector3.forward;
        await Until(() => enemy.CurrentAction?.IsActive == true, "Execute range did not enter the selected attack.");
        var attack = (EnemyBrainProbeAction)enemy.CurrentAction;
        Check(attack.Entered == 1 && enemy.stateName == nameof(EnemyAttackState),
            "The normal FSM must enter an attack exactly once.");
        Check(ParryEventBus.IsTelegraphActive(attack), "A selected action must retain its parry telegraph.");

        enemy.ActorClock.Request(enemy, 5f, 0f, HitStopStacking.Replace);
        enemy.StartAttackCooldown();
        int updates = attack.Updated;
        await Frames(3);
        Check(attack.Updated == updates && enemy.CurrentAction == attack,
            "A complete local hit stop must stop action updates and action transitions.");
        await Until(() => enemy.CanAttack, "World-time attack cooldown must expire during a local actor stop.");
        Check(attack.Updated == updates, "World-time cooldown must not advance the stopped action timeline.");
        enemy.ActorClock.ClearStops();
        await Until(() => attack.Updated > updates, "Action updates did not resume after local hit stop.");

        skill.cooldown = 100f;
        enemy.RecordAttackStarted(skill);
        Check(!enemy.IsSkillReady(skill) && enemy.GetAttackSelectionWeight(skill, 1f) == 0f,
            "Per-actor skill cooldown must exclude the previous attack.");
        skill.cooldown = 0f;
        enemy.CombatLabPassive = true;
        Check(!attack.IsActive && !ParryEventBus.IsTelegraphActive(attack),
            "Passive mode must cancel the active action and release its telegraph immediately.");
        Check(enemy.CurrentAction == null && enemy.GetComponent<Collider>().enabled,
            "Passive mode must clear selection while retaining a live body collider.");
        int detections = sensor.Detections;
        await Frames(3);
        Check(sensor.Detections == detections, "Passive mode must suppress autonomous sensing.");

        enemy.HurtDuration = .5f;
        var hit = enemy.TakeDamage(1f, poiseDamage: 0f);
        Check(hit.Applied, "Passive enemies must still accept damage.");
        await Until(() => enemy.stateName == nameof(EnemyHurtState), "Passive mode lost the hit reaction.");
        enemy.CombatLabPassive = false;
        enemy.CombatLabPassive = true;
        Check(enemy.stateName == nameof(EnemyHurtState) && enemy.IsHurt,
            "Passive toggles must preserve an active hit reaction.");
        await Until(() => !enemy.IsHurt, "Passive hit reactions must finish on the actor clock.");

        enemy.enabled = false;
        Check(enemy.CurrentAction == null, "Disabling an actor must clear its selected action.");
        Check(!enemy.TakeDamage(1f, poiseDamage: 0f).Applied,
            "A suspended encounter actor must reject damage.");
        enemy.enabled = true;
        await Frames(2);
        Check(enemy.Brain is NormalEnemyBrain, "Reactivation must retain the initialized normal brain.");
        int deaths = 0;
        enemy.OnDied += () => deaths++;
        Check(enemy.TakeDamage(1000f, poiseDamage: 0f).Killed, "Lethal damage must report a kill.");
        Check(!enemy.TakeDamage(1000f, poiseDamage: 0f).Applied && deaths == 1,
            "Lethal damage must emit one confirmed death and reject subsequent damage.");
        await Until(() => enemy.stateName == nameof(EnemyDieState), "Death must enter shared cleanup despite passive mode.");
        Check(!enemy.GetComponent<Collider>().enabled, "Shared death cleanup must disable the body collider.");
    }

    private static async Task ExerciseNormalRecoveryAndMemory(RuntimeFixture fixture)
    {
        var skill = fixture.New<EnemyBrainProbeSkill>();
        skill.finishAfterUpdates = 2;
        var skills = fixture.SkillSet(skill);
        var enemy = fixture.Actor<Enemy>("Normal completion and retry", skills, out var sensor);
        sensor.Target = fixture.Target;
        fixture.Target.position = enemy.transform.position + Vector3.forward;
        await Until(() => skill.Actions.Count > 0 && skill.Actions[0].IsFinished,
            "A normal action did not finish through its real Update callback.");
        var first = skill.Actions[0];
        await Until(() => !first.IsActive && enemy.CurrentAction != first,
            "A naturally completed normal attack did not leave AttackState.");
        Check(!enemy.CanAttack, "The normal attack exit must start the common attack cooldown.");
        await Until(() => skill.Actions.Count > 1 && skill.Actions[1].Entered == 1,
            "Normal AI did not resume selecting attacks after its completion cooldown.");
        enemy.CombatLabPassive = true;
        skill.finishAfterUpdates = -1;
        enemy.ApproachStuckTimeout = .1f;
        enemy.FailedAttackRetryDelay = 1f;
        fixture.Target.position = enemy.transform.position + Vector3.forward * 3f;
        int selected = skill.Actions.Count;
        enemy.CombatLabPassive = false;
        await Until(() => skill.Actions.Count > selected && enemy.stateName == nameof(EnemyApproachState),
            "Normal AI did not start an out-of-execute-range approach.");
        await Until(() => enemy.CurrentAction == null && !enemy.IsSkillReady(skill),
            "A stalled normal approach must clear selection and impose a retry delay.");
        Check(skill.Actions[selected].Entered == 0, "A failed approach must never enter its attack action.");

        Set(enemy, "_targetMemoryDuration", .25f);
        fixture.Target.position = enemy.transform.position + Vector3.forward * (enemy.CombatExitRange + 1f);
        await Until(() => enemy.stateName == nameof(EnemyTraceState), "Normal AI did not trace a distant detected target.");
        await Frames(1);
        Vector3 remembered = fixture.Target.position;
        sensor.Target = null;
        Check(enemy.HasTargetMemory && (enemy.LastKnownTargetPosition - remembered).sqrMagnitude < .001f,
            "Losing sight must retain the last sensed position for the configured memory window.");
        await Until(() => enemy.stateName == nameof(EnemyIdleState), "Expired target memory did not return normal AI to idle.");
        enemy.enabled = false;

        var explosiveSet = fixture.SkillSet();
        var explosive = fixture.New<MonsterSelfDestructSO>();
        explosive.hpRatioThreshold = .5f; explosive.explosionDelay = 10f;
        explosiveSet.selfDestruct = explosive;
        var bomber = fixture.Actor<Enemy>("Normal self destruct", explosiveSet, out _);
        await Until(() => bomber.Brain != null, "Self-destruct enemy did not initialize.");
        var damage = bomber.TakeDamage(60f, poiseDamage: 0f);
        Check(damage.Applied && bomber.IsSelfDestructing && bomber.CurrentAction is EnemySelfDestruct,
            "Crossing the normal self-destruct threshold must select the existing self-destruct action immediately.");
        var cancelled = bomber.CurrentAction;
        bomber.enabled = false;
        Check(!cancelled.IsActive && bomber.CurrentAction == null && !ParryEventBus.IsTelegraphActive(cancelled),
            "Suspension must clean an active self-destruct action and its telegraph.");
        bomber.enabled = true;
        await Until(() => bomber.CurrentAction is EnemySelfDestruct && bomber.CurrentAction.IsActive
            && bomber.CurrentAction != cancelled, "Reactivation must safely reconstruct the pending self-destruct action.");
        Check(bomber.TakeDamage(1000f, poiseDamage: 0f).Killed, "A self-destructing ordinary enemy must remain damageable.");
        await Until(() => bomber.stateName == nameof(EnemyDieState), "Death must override an active self-destruct state.");
    }

    private static async Task ExerciseBoss(RuntimeFixture fixture)
    {
        var first = fixture.New<EnemyBrainProbeSkill>();
        var second = fixture.New<EnemyBrainProbeSkill>();
        var firstSet = fixture.SkillSet(first);
        var secondSet = fixture.SkillSet(second);
        var firstPattern = fixture.Sequence(first);
        var secondPattern = fixture.Sequence(second);
        var p1 = fixture.Phase(50f, firstSet, firstPattern);
        var p2 = fixture.Phase(50f, secondSet, secondPattern);
        var boss = fixture.Actor<ConfiguredBoss>("Two phase boss", firstSet, out var sensor);
        Set(boss, "PhaseDatas", new List<BossPhaseData> { p1, p2 });
        boss.StaggerResistLevel = StaggerResistLevel.SUPER_ARMOR;
        sensor.Target = fixture.Target;
        fixture.Target.position = boss.transform.position + Vector3.forward;
        await Until(() => boss.CurrentAction?.IsActive == true, "The boss did not execute its authored first pattern.");
        Check(!(boss.Brain is NormalEnemyBrain) && boss.PhaseIndex == 0 && boss.ActiveSkillSet == firstSet,
            "A boss must select its own brain and first phase skill set.");
        var firstAction = boss.CurrentAction;
        var shield = boss.GetComponent<BossShield>();
        shield.Grant(2);
        float before = boss.EnemyStat.CurrentHp;
        var shielded = boss.TakeDamage(.5f, poiseDamage: 0f);
        Check(shielded.Applied && Mathf.Approximately(shielded.ShieldDamage, .5f)
            && Mathf.Approximately(shielded.HealthDamage, 0f) && Mathf.Approximately(boss.EnemyStat.CurrentHp, before),
            "Shield absorption must retain fractional damage and leave health unchanged.");
        shield.Clear();
        var result = boss.TakeDamage(60f, poiseDamage: 1000f);
        Check(result.Applied && !result.Killed && boss.PhaseIndex == 0 && boss.IsTransitioning
            && Mathf.Approximately(result.HealthDamage, 50f) && boss.EnemyStat.CurrentHp == 0f,
            "Depleting phase HP must begin a nonlethal revival and discard excess damage.");
        Check(!boss.TakeDamage(1000f, poiseDamage: 0f).Applied && boss.PhaseGeneration == 1,
            "Same-frame follow-up hits must not deplete another phase or increment its generation again.");
        await Until(() => boss.PhaseIndex == 1 && boss.CurrentAction?.SkillData == second
            && boss.CurrentAction.IsActive, "The second phase did not revive and execute its own pattern.");
        Check(boss.ActiveSkillSet == secondSet && boss.EnemyStat.MonsterSkillData == firstSet,
            "Boss phase skill selection must not mutate the shared EnemyData skill set.");
        Check(!ParryEventBus.IsTelegraphActive(firstAction), "Phase change must release the old action telegraph.");
        Check(!firstAction.IsActive && !boss.PendingGroggy && !boss.IsHurt && !boss.IsGroggy
            && Mathf.Approximately(boss.EnemyStat.CurrentHp, 50f) && Mathf.Approximately(boss.EnemyStat.MaxHp, 50f),
            "Revival must restore the next phase's own HP pool and clear prior combat reactions.");

        var current = boss.CurrentAction;
        boss.enabled = false;
        Check(!current.IsActive && !ParryEventBus.IsTelegraphActive(current),
            "Suspending a boss must cancel its pattern action and telegraph.");
        int starts = second.Actions.Count;
        await Frames(3);
        Check(second.Actions.Count == starts, "A disabled boss must not continue its separate pattern controller.");
        boss.enabled = true;
        await Until(() => boss.CurrentAction?.IsActive == true, "The boss pattern did not resume after encounter activation.");
        Check(boss.PhaseIndex == 1, "Reactivation must retain the committed boss phase.");
        int deaths = 0; boss.OnDied += () => deaths++;
        var active = boss.CurrentAction;
        Check(boss.TakeDamage(1000f, poiseDamage: 0f).Killed && deaths == 1,
            "Boss lethal damage must preserve the Enemy confirmed-death contract.");
        Check(!active.IsActive && !ParryEventBus.IsTelegraphActive(active),
            "Boss death must immediately cancel the live action and telegraph.");
        await Until(() => boss.stateName == nameof(EnemyDieState), "Boss death must use shared actor cleanup.");
    }

    private static async Task ExerciseBossSequence(RuntimeFixture fixture)
    {
        var first = fixture.New<EnemyBrainProbeSkill>();
        first.weight = 0f; first.engageRange = .1f; first.finishAfterUpdates = 2;
        var second = fixture.New<EnemyBrainProbeSkill>(); second.finishAfterUpdates = 2;
        var skills = fixture.SkillSet(first, second);
        var pattern = fixture.Sequence(first);
        pattern.steps.Add(new BossSequenceStep { kind = BossPatternStepKind.Wait, waitSeconds = .15f });
        pattern.steps.Add(new BossSequenceStep { kind = BossPatternStepKind.Attack, skill = second });
        var phase = fixture.Phase(100f, skills, pattern);
        var boss = fixture.Actor<ConfiguredBoss>("Authored sequence timing", skills, out var sensor);
        Set(boss, "PhaseDatas", new List<BossPhaseData> { phase });
        boss.EnemyStat.AddModifier(EnemyStatType.AttackCooldown, .7f, 0f);
        sensor.Target = fixture.Target;
        fixture.Target.position = boss.transform.position + Vector3.forward * 3f;
        await Until(() => boss.CurrentAction?.SkillData == first && first.Actions.Count == 1,
            "An authored weight-zero attack beyond engageRange must still be selected for approach.");
        Check(first.Actions[0].Entered == 0,
            "An authored attack must wait until executeRange before entering its action.");
        fixture.Target.position = boss.transform.position + Vector3.forward;
        await Until(() => second.Actions.Count > 0 && second.Actions[0].Entered == 1,
            "The sequence did not continue to its second authored attack.");
        float between = second.Actions[0].EnteredAt - first.Actions[0].CompletedAt;
        Check(between >= .12f && between < .7f,
            "The authored wait must separate sequence attacks without imposing the common attack cooldown between them.");
        await Until(() => first.Actions.Count > 1 && first.Actions[1].Entered == 1,
            "A looping sequence did not restart after completion.");
        Check(first.Actions[1].EnteredAt - second.Actions[0].CompletedAt >= .7f,
            "The common attack cooldown must apply when the whole sequence completes.");
        boss.enabled = false;

        var sharedSkill = fixture.New<EnemyBrainProbeSkill>();
        var sharedSkills = fixture.SkillSet(sharedSkill);
        var sharedPattern = fixture.New<EnemyBrainProbePattern>(); sharedPattern.skill = sharedSkill;
        var sharedPhase = fixture.Phase(100f, sharedSkills, sharedPattern);
        var a = fixture.Actor<ConfiguredBoss>("Shared pattern A", sharedSkills, out var sensorA);
        var b = fixture.Actor<ConfiguredBoss>("Shared pattern B", sharedSkills, out var sensorB);
        Set(a, "PhaseDatas", new List<BossPhaseData> { sharedPhase });
        Set(b, "PhaseDatas", new List<BossPhaseData> { sharedPhase });
        sensorA.Target = sensorB.Target = fixture.Target;
        fixture.Target.position = fixture.Origin + Vector3.forward;
        await Until(() => a.CurrentAction?.IsActive == true && b.CurrentAction?.IsActive == true,
            "Two actors sharing one custom pattern asset must both execute their own runtime.");
        Check(sharedPattern.Runtimes.Count == 2 && !ReferenceEquals(sharedPattern.Runtimes[0], sharedPattern.Runtimes[1])
            && sharedPattern.Runtimes.TrueForAll(runtime => runtime.Entered == 1 && runtime.NextCalls == 1),
            "A pattern ScriptableObject must create an independent cursor and call Enter once per boss.");
        var aRuntime = sharedPattern.Runtimes.Find(runtime => runtime.Owner == a);
        var bRuntime = sharedPattern.Runtimes.Find(runtime => runtime.Owner == b);
        var bAction = b.CurrentAction;
        a.enabled = false;
        Check(aRuntime.Exited == 1 && bRuntime.Exited == 0 && b.CurrentAction == bAction && bAction.IsActive,
            "Suspending one boss must exit only its own custom pattern runtime exactly once.");
        a.enabled = false;
        Check(aRuntime.Exited == 1, "Repeated suspension must not exit a disposed custom pattern twice.");
        b.TakeDamage(1000f, poiseDamage: 0f);
        await Until(() => bRuntime.Exited == 1, "Death must exit a custom pattern runtime.");
    }

    private static async Task ExerciseAuthoredBossCombat(RuntimeFixture fixture)
    {
        var skill = fixture.New<EnemyBrainProbeSkill>();
        var skills = fixture.SkillSet(skill);
        var data = fixture.New<BossData>();
        data.maxHp = 100; data.baseAtk = 20f; data.baseDef = 10f;
        data.attackCooldown = .1f; data.combatRange = 4f; data.combatExitRange = 12f;
        data.initialShield = 30f; data.monsterSkillData = skills;
        data.statusImmunityList.Add("Silence"); data.statusImmunityList.Add("DefenseReduction");
        var first = fixture.Phase(50f, skills, fixture.Sequence(skill));
        first.atkMultiplier = 1.5f; first.defMultiplier = 2f;
        var second = fixture.Phase(50f, skills, fixture.Sequence(skill));
        second.atkMultiplier = 2f; second.defMultiplier = 3f;
        second.overrideShield = true; second.shieldAmount = 10f;
        var boss = fixture.Actor<ConfiguredBoss>("Authored boss combat", skills, out var sensor, data);
        Set(boss, "PhaseDatas", new List<BossPhaseData> { first, second });
        boss.StaggerResistLevel = StaggerResistLevel.SUPER_ARMOR;
        sensor.Target = fixture.Target;
        fixture.Target.position = boss.transform.position + Vector3.forward;
        await Until(() => boss.CurrentAction?.IsActive == true, "The authored boss must initialize its first pattern.");
        Check(ReferenceEquals(boss.Data, data) && ReferenceEquals(boss.EnemyStat.Data, data),
            "Real Enemy.Start must preserve the boss's single shared combat asset.");
        boss.EnemyStat.AddModifier(EnemyStatType.ATK, 10f, .2f);
        Check(Mathf.Abs(boss.EnemyStat.GetStat(EnemyStatType.ATK) - 54f) < .001f,
            "The first phase ATK multiplier must compose with actor buffs.");
        var shield = boss.GetComponent<BossShield>();
        Check(Mathf.Approximately(shield.CurrentAbsorption, 30f), "Initial shield must be granted at real brain initialization.");
        var handler = boss.gameObject.AddComponent<StatusEffectHandler>();
        var current = boss.CurrentAction;
        Check(!handler.TryApply(new SilenceEffect(5f)) && boss.CurrentAction == current && current.IsActive,
            "Silence immunity must prevent cancellation of a live authored boss action.");
        var forms = boss.gameObject.AddComponent<FormTargetEffects>(); forms.ApplyArmorShred(true);
        Check(forms.ArmorReductionRatio == 0f, "Actual armor shred must honor boss immunity in Play Mode.");
        boss.TakeDamage(80f, poiseDamage: 0f);
        Check(boss.IsTransitioning && boss.PhaseIndex == 0 && !boss.IsDead,
            "Authored phase HP depletion must begin revival while retaining its encounter actor.");
        await Until(() => boss.PhaseIndex == 1 && boss.CurrentAction?.IsActive == true,
            "Authored boss did not enter its second phase after revival.");
        Check(Mathf.Abs(boss.EnemyStat.GetStat(EnemyStatType.ATK) - 72f) < .001f,
            "The next phase must replace ATK scaling without losing buffs.");
        Check(Mathf.Approximately(shield.CurrentAbsorption, 10f), "The next phase shield must replace the old pool.");
        shield.Absorb(3f);
        boss.enabled = false; boss.enabled = true;
        await Until(() => boss.CurrentAction?.IsActive == true, "The authored boss did not resume its pattern.");
        Check(Mathf.Approximately(shield.CurrentAbsorption, 7f) && boss.PhaseIndex == 1,
            "Reactivation must preserve the current authored phase and remaining shield.");
        Check(Mathf.Approximately(data.baseAtk, 20f) && Mathf.Approximately(first.atkMultiplier, 1.5f),
            "Actor buffs and phase changes must not mutate shared authoring assets.");
        boss.enabled = false;
    }

    private static async Task ExerciseBossPhaseBoundaries(RuntimeFixture fixture)
    {
        var first = fixture.New<EnemyBrainProbeSkill>();
        var second = fixture.New<EnemyBrainProbeSkill>();
        var third = fixture.New<EnemyBrainProbeSkill>();
        var skills = fixture.SkillSet(first);
        var phases = new List<BossPhaseData>
        {
            fixture.Phase(30f, skills, fixture.Sequence(first)),
            fixture.Phase(30f, fixture.SkillSet(second), fixture.Sequence(second)),
            fixture.Phase(40f, fixture.SkillSet(third), fixture.Sequence(third))
        };
        var boss = fixture.Actor<ConfiguredBoss>("Approach phase boundary", skills, out var sensor);
        Set(boss, "PhaseDatas", phases);
        boss.StaggerResistLevel = StaggerResistLevel.SUPER_ARMOR;
        sensor.Target = fixture.Target;
        fixture.Target.position = boss.transform.position + Vector3.forward * 3f;
        var entered = new List<int>(); boss.OnPhaseChanged += (index, _) => entered.Add(index);
        int depleted = 0, deaths = 0;
        boss.OnPhaseDepleted += _ => depleted++;
        boss.OnDied += () => deaths++;
        await Until(() => boss.CurrentAction?.SkillData == first && first.Actions.Count == 1,
            "The first phase did not start its approach.");
        var approaching = (EnemyBrainProbeAction)boss.CurrentAction;
        Check(approaching.Entered == 0, "The approach fixture unexpectedly entered an attack.");
        var depletion = boss.TakeDamage(1000f, poiseDamage: 0f);
        Check(depletion.Applied && !depletion.Killed && Mathf.Approximately(depletion.HealthDamage, 30f)
            && boss.EnemyStat.CurrentHp == 0f && boss.IsTransitioning && boss.PhaseIndex == 0
            && boss.PhaseGeneration == 1 && depleted == 1 && deaths == 0,
            "Overflow damage must deplete exactly the current phase and emit no final death.");
        Check(!boss.TakeDamage(1000f, poiseDamage: 0f).Applied && depleted == 1,
            "A second hit in the depletion frame must not skip the next phase.");
        float timeScale = Time.timeScale;
        try
        {
            Time.timeScale = 0f;
            await Frames(5);
            Check(boss.IsTransitioning && boss.ReviveProgress == 0f && boss.PhaseIndex == 0,
                "Global pause must stop the down/hold/revival timer.");
        }
        finally { Time.timeScale = timeScale; }
        boss.enabled = false;
        float suspendedProgress = boss.ReviveProgress;
        await Frames(5);
        Check(boss.IsTransitioning && boss.PhaseIndex == 0 && boss.ReviveProgress == suspendedProgress,
            "Disabling the encounter actor must suspend its pending revival.");
        boss.enabled = true;
        await Until(() => boss.PhaseIndex == 1 && !boss.IsTransitioning,
            "An interrupted approach did not revive into exactly its next phase.");
        Check(approaching.Entered == 0 && boss.CurrentAction != approaching
            && entered.Count == 2 && entered[0] == 0 && entered[1] == 1
            && Mathf.Approximately(boss.EnemyStat.CurrentHp, 30f),
            "Revival must enter phases in authored order and never enter the stale approach action.");
        Check(!boss.TakeDamage(1000f, poiseDamage: 0f).Killed && boss.PhaseGeneration == 2,
            "The next independent HP pool must produce a second nonfinal depletion.");
        await Until(() => boss.PhaseIndex == 2 && !boss.IsTransitioning,
            "The second depletion did not revive the final phase.");
        Check(Mathf.Approximately(boss.EnemyStat.CurrentHp, 40f) && Mathf.Approximately(boss.EnemyStat.MaxHp, 40f)
            && entered.Count == 3 && depleted == 2 && deaths == 0,
            "The final phase must restore its full authored HP without paying a kill.");
        Check(boss.TakeDamage(1000f, poiseDamage: 0f).Killed && deaths == 1,
            "Only final-phase depletion must confirm a boss kill.");
        await Frames(3);
        Check(boss.IsDead && !boss.IsTransitioning && boss.PhaseIndex == 2 && entered.Count == 3
            && depleted == 2 && !boss.TakeDamage(1000f, poiseDamage: 0f).Applied && deaths == 1,
            "Final death must never revive, repeat phase hooks, or emit a duplicate kill.");
    }

    private static async Task ExerciseExistingPrefab(RuntimeFixture fixture)
    {
        foreach (string path in new[] { "Assets/_Project/Prefabs/Drone.prefab", "Assets/_Project/Prefabs/Chang.prefab" })
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Check(source != null, "Existing ordinary enemy prefab is missing: " + path);
            var clone = Object.Instantiate(source, fixture.Origin + Vector3.right * 12f, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(clone, fixture.Scene);
            var enemy = clone.GetComponent<Enemy>();
            Check(enemy != null, "Existing ordinary enemy prefab lost its Enemy component.");
            enemy.CombatLabPassive = true;
            await Until(() => enemy.Brain != null, "Existing prefab failed to initialize without migration: " + path);
            Check(enemy.Brain is NormalEnemyBrain && enemy.EnemyStat.CurrentHp > 0f
                && enemy.Animator != null && clone.GetComponent<NavMeshAgent>().isOnNavMesh,
                "Existing prefab must retain its default brain, health, animator and navigation: " + path);
            Object.Destroy(clone);
            await Frames(2);
        }
    }

    private static async Task ExercisePatternCallbacksAndRequests(RuntimeFixture fixture)
    {
        var errors = new List<string>();
        Application.LogCallback record = (message, stack, kind) =>
        {
            if ((kind == LogType.Exception || kind == LogType.Error || kind == LogType.Assert)
                && (stack.Contains(nameof(BossBrain)) || stack.Contains(nameof(EnemyBrainProbePatternRuntime))))
                errors.Add(message);
        };
        Application.logMessageReceived += record;
        try
        {
            for (int which = 0; which < 4; which++)
            {
                bool duringEnter = which < 2;
                bool kill = which % 2 == 1;
                var skill = fixture.New<EnemyBrainProbeSkill>();
                var skills = fixture.SkillSet(skill);
                var pattern = fixture.New<EnemyBrainProbePattern>(); pattern.skill = skill;
                Action<Boss> interrupt = owner =>
                {
                    if (kill) owner.TakeDamage(1000f, poiseDamage: 0f);
                    else owner.enabled = false;
                };
                if (duringEnter) pattern.onEnter = interrupt; else pattern.onNext = interrupt;
                var boss = fixture.Actor<ConfiguredBoss>("Pattern callback interruption " + which, skills, out var sensor);
                Set(boss, "PhaseDatas", new List<BossPhaseData> { fixture.Phase(100f, skills, pattern) });
                sensor.Target = fixture.Target;
                fixture.Target.position = boss.transform.position + Vector3.forward;
                await Until(() => kill ? boss.IsDead : !boss.enabled,
                    "A custom pattern callback did not interrupt its owner.");
                await Frames(2);
                Check(skill.Actions.Count == 0 && boss.CurrentAction == null,
                    "A pattern callback that interrupts its owner must not create or enter the returned attack.");
                Check(pattern.Runtimes.Count == 1 && pattern.Runtimes[0].Exited == 1,
                    "A callback interruption must dispose its runtime exactly once.");
            }
            Check(errors.Count == 0, "Custom pattern callback interruption threw: " + string.Join("; ", errors));
        }
        finally { Application.logMessageReceived -= record; }

        var original = fixture.New<EnemyBrainProbeSkill>();
        var requested = fixture.New<EnemyBrainProbeSkill>(); requested.cooldown = 100f; requested.executeRange = .1f;
        var skillsForRequest = fixture.SkillSet(original, requested);
        var phases = new List<BossPhaseData>
        {
            fixture.Phase(50f, skillsForRequest, fixture.Sequence(original)),
            fixture.Phase(50f, skillsForRequest, fixture.Sequence(original))
        };
        var requester = fixture.Actor<ConfiguredBoss>("Safe immediate request", skillsForRequest, out var requestSensor);
        Set(requester, "PhaseDatas", phases);
        requester.StaggerResistLevel = StaggerResistLevel.SUPER_ARMOR;
        requestSensor.Target = fixture.Target;
        fixture.Target.position = requester.transform.position + Vector3.forward;
        await Until(() => requester.CurrentAction?.IsActive == true, "The immediate request fixture did not begin its original action.");
        var old = requester.CurrentAction;
        requestSensor.Target = null;
        requester.StartAttackCooldown();
        requester.RecordAttackStarted(requested);
        Check(!requester.CanAttack && !requester.IsSkillReady(requested), "The request fixture must have both cooldowns active.");
        requester.RequestImmediateSkill(requested);
        Check(requester.CurrentAction == old, "Immediate requests must wait for a safe tick instead of replacing a live callback stack.");
        await Until(() => requester.CurrentAction?.SkillData == requested && requester.CurrentAction.IsActive,
            "An explicit immediate request must bypass cooldowns and execution range without requiring a sensed target.");
        Check(!old.IsActive && requested.Actions.Count == 1 && requested.Actions[0].Entered == 1,
            "A committed immediate request must clean the previous action and enter the new one once.");
        requester.Data.phaseHoldSeconds = 5f;
        requester.TakeDamage(60f, poiseDamage: 0f);
        Check(requester.IsTransitioning, "The immediate request fixture must be reviving after phase depletion.");
        int originalCount = original.Actions.Count;
        requester.RequestImmediateSkill(original);
        await Frames(3);
        Check(original.Actions.Count == originalCount && requester.CurrentAction == null,
            "A boss revival must cancel its prior action and reject immediate skill requests.");
        requester.enabled = false;
    }

    private static async Task ExercisePatrolAndSpacing(RuntimeFixture fixture)
    {
        var room = new GameObject("Patrol room");
        SceneManager.MoveGameObjectToScene(room, fixture.Scene);
        room.transform.position = fixture.Origin;
        var module = room.AddComponent<BaekhoMapModule>();
        module.localBounds = new Bounds(Vector3.up * 2f, new Vector3(8f, 6f, 8f));
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Owned patrol floor";
        floor.transform.SetParent(room.transform, false);
        floor.transform.localPosition = Vector3.down * .25f;
        floor.transform.localScale = new Vector3(8f, .5f, 8f);
        module.groundObjects = new[] { floor };

        var skill = fixture.New<EnemyBrainProbeSkill>();
        var patrol = fixture.Actor<Enemy>("Pre-entry patrol", fixture.SkillSet(skill), out var sensor,
            navigationSpeed: 4f);
        patrol.transform.SetParent(room.transform, true);
        patrol.SetEncounterPatrolOnly(true);
        sensor.Target = fixture.Target;
        fixture.Target.position = fixture.Origin + Vector3.forward;
        await Until(() => patrol.Brain != null, "Patrol fixture did not initialize.");
        Vector3 home = patrol.transform.position;
        int detections = sensor.Detections;
        Check(!patrol.TakeDamage(1f, poiseDamage: 0f).Applied && !patrol.CanAttack
            && patrol.DetectedTarget == null, "Pre-entry patrol must reject damage and combat.");
        await Until(() => Vector3.ProjectOnPlane(patrol.transform.position - home, Vector3.up).sqrMagnitude > .04f,
            "An undetected first-wave enemy did not patrol after its observation pause.");
        Check(sensor.Detections == detections && skill.Actions.Count == 0,
            "Pre-entry patrol must neither sense the player nor create attacks.");
        Check(!TryNavigationMove(patrol, home + Vector3.right * 5f, false),
            "Ordinary movement must reject a destination outside its own room floor.");
        Check(!TryNavigationMove(patrol, home + Vector3.right * 3.5f, true),
            "Patrol must reject destinations beyond three metres of its original home.");
        Vector3 entryPosition = patrol.transform.position;
        patrol.SetEncounterPatrolOnly(false);
        Check((patrol.transform.position - entryPosition).sqrMagnitude < .0001f,
            "First-wave activation must preserve the current patrol position.");
        fixture.Target.position = patrol.transform.position + Vector3.forward;
        await Until(() => skill.Actions.Count > 0 && skill.Actions[0].Entered == 1,
            "Normal sensing and attack selection did not resume on room entry.");
        patrol.enabled = false;
        room.SetActive(false);

        var ranged = fixture.New<EnemyBrainProbeSkill>();
        ranged.preferredRange = 6f; ranged.executeRange = 6f; ranged.engageRange = 10f;
        var melee = fixture.New<EnemyBrainProbeSkill>();
        melee.preferredRange = 2f; melee.executeRange = 2f; melee.engageRange = 6f;
        var spacingData = fixture.New<EnemyData>();
        spacingData.maxHp = 100; spacingData.combatRange = 12f; spacingData.combatExitRange = 20f;
        spacingData.attackCooldown = 100f; spacingData.monsterSkillData = fixture.SkillSet(melee, ranged);
        var spacing = fixture.Actor<Enemy>("Combat spacing", spacingData.monsterSkillData, out var spacingSensor,
            spacingData, 4f);
        spacing.GetComponent<NavMeshAgent>().Warp(fixture.Origin + Vector3.right * 12f);
        Vector3 spacingHome = spacing.transform.position;
        spacing.SetEncounterPatrolOnly(true);
        await Until(() => spacing.Brain != null, "Combat-spacing fixture did not initialize.");
        spacing.StartAttackCooldown();
        spacingSensor.Target = fixture.Target;
        fixture.Target.position = spacing.transform.position + Vector3.forward;
        spacing.SetEncounterPatrolOnly(false);
        await Until(() => Vector3.Distance(spacing.transform.position, fixture.Target.position) >= 1.5f,
            "A close melee enemy did not retreat into its preferred distance band.");
        await Until(() => spacing.stateName == nameof(EnemyCombatIdleState)
            && !spacing.GetComponent<NavMeshAgent>().hasPath,
            "Combat spacing did not stop inside the preferred distance band.");
        Vector3 stopped = spacing.transform.position;
        await Frames(4);
        Check((spacing.transform.position - stopped).sqrMagnitude < .01f,
            "Combat spacing must hold its position instead of orbiting inside the distance band.");
        Vector3 oppositeSide = fixture.Target.position * 2f - spacing.transform.position;
        Check(!TryNavigationMove(spacing, oppositeSide, false, fixture.Target),
            "Combat spacing must reject a path crossing to the other side of the player.");
        Check(spacing.TakeDamage(1f, power: Vector3.right, poiseDamage: 0f).Applied,
            "An entered combat-spacing enemy must accept ordinary hits.");
        await Until(() => spacing.stateName == nameof(EnemyHurtState),
            "A hit did not interrupt combat positioning.");
        Check(spacing.GetComponent<NavMeshAgent>().isStopped,
            "Hit reactions must lock autonomous combat movement.");
        await Until(() => !spacing.IsHurt && !spacing.IsKnockbacking,
            "Combat movement did not release its hit reaction.");
        fixture.Target.position = spacing.transform.position + Vector3.forward * 9f;
        await Until(() => Vector3.Distance(spacing.transform.position, fixture.Target.position) <= 6.5f,
            "A ranged enemy did not approach its nearest preferred skill distance.");
        Check(Vector3.Dot(spacing.transform.forward, (fixture.Target.position - spacing.transform.position).normalized) > .8f,
            "Combat movement must continue facing the target.");
        spacingSensor.Target = null;
        Set(spacing, "_targetMemoryExpiresAt", float.NegativeInfinity);
        var spacingAgent = spacing.GetComponent<NavMeshAgent>();
        spacingAgent.ResetPath();
        spacingAgent.Warp(spacingHome + Vector3.right * 5f);
        await Until(() => spacing.stateName == nameof(EnemyIdleState)
            && Vector3.ProjectOnPlane(spacing.transform.position - spacingHome, Vector3.up).magnitude < 3f,
            "An enemy that lost its target outside its patrol radius did not walk home.");
        spacing.enabled = false;

        var earlyListener = fixture.Actor<Enemy>("Reported cue before Start", fixture.SkillSet(melee), out _);
        Vector3 cue = fixture.Origin + Vector3.forward * 7f;
        earlyListener.RememberEncounterPosition(cue);
        await Until(() => earlyListener.Brain != null, "The early alert listener did not initialize.");
        Check(earlyListener.HasTargetMemory && earlyListener.LastKnownTargetPosition == cue
            && earlyListener.DetectedTarget == null && earlyListener.CurrentAction == null,
            "A reported cue recorded before Start must survive initialization without granting vision or an attack.");
        earlyListener.enabled = false;
    }

    private static async Task ExerciseAuthoredSpacing(RuntimeFixture fixture, string path, float preferredDistance)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Check(source != null, "Authored combat-spacing prefab is missing: " + path);
        Vector3 spawn = fixture.Origin + Vector3.left * 12f;
        var clone = Object.Instantiate(source, spawn, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(clone, fixture.Scene);
        try
        {
            var enemy = clone.GetComponent<Enemy>();
            Check(enemy != null, "Authored combat-spacing prefab has no Enemy: " + path);
            enemy.CombatLabPassive = true;
            await Until(() => enemy.Brain != null, "Authored combat-spacing prefab did not initialize: " + path);
            var authoredSkills = source.GetComponent<EnemyStat>().MonsterSkillData;
            Check(ReferenceEquals(enemy.ActiveSkillSet, authoredSkills),
                "Combat-spacing verification must use the prefab's original skill data: " + path);
            bool hasExpectedDistance = false;
            foreach (var skill in authoredSkills.skillData)
                if (skill != null && !(skill is MonsterSelfDestructSO) && skill.weight > 0f)
                {
                    float preferred = skill.preferredRange > 0f ? skill.preferredRange : skill.executeRange;
                    hasExpectedDistance |= Mathf.Approximately(preferred, preferredDistance);
                }
            Check(hasExpectedDistance, "The authored prefab lost its expected preferred distance: " + path);
            var sensor = clone.AddComponent<EnemyBrainSensorProbe>();
            sensor.Target = fixture.Target;
            Set(enemy, "_sensor", sensor);
            enemy.EnemyStat.AddModifier(EnemyStatType.AttackCooldown, 100f, 0f);
            enemy.StartAttackCooldown();
            fixture.Target.position = spawn + Vector3.forward * (preferredDistance + 2f);
            enemy.CombatLabPassive = false;
            var nav = clone.GetComponent<NavMeshAgent>();
            // Positioning uses the horizontal distance. The baked NavMesh sits above the target's pivot.
            float GroundDistance() => Vector3.ProjectOnPlane(enemy.transform.position - fixture.Target.position, Vector3.up).magnitude;
            await Until(() => enemy.stateName == nameof(EnemyCombatIdleState) && !nav.hasPath
                && Mathf.Abs(GroundDistance() - preferredDistance) <= .5f,
                "The actual prefab did not approach and stop at its authored preferred distance: " + path);
            Check(!enemy.CanAttack && enemy.CurrentAction == null,
                "Authored combat positioning must run during cooldown without selecting an attack: " + path);
            Vector3 heldPosition = enemy.transform.position;
            await Frames(12);
            Check((enemy.transform.position - heldPosition).sqrMagnitude < .01f && !nav.hasPath,
                "The actual prefab did not hold its authored distance during cooldown: " + path);

            fixture.Target.position = enemy.transform.position + Vector3.forward * Mathf.Max(.75f, preferredDistance - 2f);
            await Until(() => !nav.hasPath
                && Mathf.Abs(GroundDistance() - preferredDistance) <= .5f,
                "The actual prefab did not retreat and stop at its authored preferred distance: " + path);
            heldPosition = enemy.transform.position;
            await Frames(12);
            Check((enemy.transform.position - heldPosition).sqrMagnitude < .01f && !enemy.CanAttack
                && enemy.CurrentAction == null && !nav.hasPath,
                "The actual prefab must remain stationary at its authored distance after retreating: " + path);
        }
        finally
        {
            Object.Destroy(clone);
            await Frames(2);
        }
    }

    private static bool TryNavigationMove(Enemy enemy, Vector3 destination, bool patrol, Transform target = null)
    {
        Type navigationType = typeof(Enemy).Assembly.GetType("EnemyNavigation", true);
        object navigation = Activator.CreateInstance(navigationType, new object[] { enemy });
        var move = navigationType.GetMethod("TryMove");
        return (bool)move.Invoke(navigation, new object[] { destination, patrol, target });
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
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

    private static async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            int frame = Time.frameCount;
            do { await Task.Yield(); } while (Application.isPlaying && Time.frameCount == frame);
            if (!Application.isPlaying) throw new InvalidOperationException("Play Mode ended during enemy brain regression.");
        }
    }

    private static void Set(object target, string field, object value)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            var info = type.GetField(field, Hidden | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (info == null) continue;
            info.SetValue(target, value);
            return;
        }
        throw new MissingFieldException(target.GetType().Name, field);
    }

    private sealed class RuntimeFixture
    {
        public readonly Scene Scene;
        public readonly Vector3 Origin = new Vector3(10000f, 0f, 10000f);
        public readonly Transform Target;
        private readonly List<Object> _assets = new List<Object>();
        private NavMeshDataInstance _navigation;

        public RuntimeFixture()
        {
            Scene = SceneManager.CreateScene("EnemyBrainRegression-" + Guid.NewGuid().ToString("N"));
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(Origin + Vector3.down * .25f, Quaternion.identity, Vector3.one),
                    size = new Vector3(80f, .5f, 80f), area = 0 }
            };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(Origin, new Vector3(80f, 10f, 80f)), Vector3.zero, Quaternion.identity);
            if (data == null) throw new InvalidOperationException("Could not build isolated regression navigation.");
            _assets.Add(data);
            _navigation = NavMesh.AddNavMeshData(data);
            var target = new GameObject("Brain target");
            SceneManager.MoveGameObjectToScene(target, Scene);
            Target = target.transform;
            Target.position = Origin + Vector3.forward * 6f;
        }

        public T New<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            _assets.Add(asset);
            return asset;
        }

        public MonsterSkillDataSO SkillSet(params MonsterSkillData[] skills)
        {
            var set = New<MonsterSkillDataSO>(); set.skillData = new List<MonsterSkillData>(skills); return set;
        }

        public BossSequencePatternSO Sequence(MonsterSkillData skill)
        {
            var pattern = New<BossSequencePatternSO>(); pattern.loop = true;
            pattern.steps = new List<BossSequenceStep>
            { new BossSequenceStep { kind = BossPatternStepKind.Attack, skill = skill } };
            return pattern;
        }

        public BossPhaseData Phase(float hp, MonsterSkillDataSO skills, BossPatternSO pattern)
        {
            var phase = New<BossPhaseData>();
            phase.phaseMaxHp = hp; phase.skillSet = skills; phase.pattern = pattern;
            return phase;
        }

        public T Actor<T>(string name, MonsterSkillDataSO skills, out EnemyBrainSensorProbe sensor,
            EnemyData dataOverride = null, float navigationSpeed = 0f) where T : Enemy
        {
            var actor = new GameObject(name); actor.SetActive(false);
            SceneManager.MoveGameObjectToScene(actor, Scene);
            actor.transform.position = Origin;
            var nav = actor.AddComponent<NavMeshAgent>(); nav.speed = navigationSpeed; nav.stoppingDistance = .5f;
            if (navigationSpeed > 0f) nav.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            actor.AddComponent<CapsuleCollider>();
            var model = new GameObject("Model"); model.transform.SetParent(actor.transform, false);
            var animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/_Project/Animations/Drone/Drone.controller");
            var enemy = actor.AddComponent<T>();
            var stats = actor.GetComponent<EnemyStat>();
            enemy.EnemyStat = stats;
            var data = dataOverride ?? (typeof(Boss).IsAssignableFrom(typeof(T)) ? (EnemyData)New<BossData>() : New<EnemyData>());
            if (dataOverride == null)
            {
                data.maxHp = 100; data.baseAtk = 1f; data.combatRange = 4f; data.combatExitRange = 12f;
                data.attackCooldown = .1f; data.monsterSkillData = skills;
            }
            if (enemy is Boss && data is BossData) Set(enemy, "BossData", data);
            if (data is BossData bossData)
            {
                bossData.phaseDownSeconds = .05f;
                bossData.phaseHoldSeconds = .05f;
                bossData.phaseReviveSeconds = .1f;
            }
            Set(stats, "baseData", data);
            sensor = actor.AddComponent<EnemyBrainSensorProbe>();
            actor.SetActive(true);
            return enemy;
        }

        public async Task DisposeAsync()
        {
            if (Scene.IsValid() && Scene.isLoaded)
            {
                foreach (var root in Scene.GetRootGameObjects())
                    foreach (var enemy in root.GetComponentsInChildren<Enemy>(true)) enemy.CancelCurrentAction();
                var operation = SceneManager.UnloadSceneAsync(Scene);
                while (operation != null && !operation.isDone) await Task.Yield();
            }
            if (_navigation.valid) _navigation.Remove();
            foreach (var asset in _assets) if (asset != null) Object.Destroy(asset);
        }
    }
}

public sealed class EnemyBrainSensorProbe : MonoBehaviour, IEnemySensor
{
    public Transform Target;
    public int Detections;
    public Transform DetectedTarget => Target;
    public bool DetectTarget() { Detections++; return Target != null; }
}

public sealed class EnemyBrainProbeSkill : MonsterSkillData
{
    public int finishAfterUpdates = -1;
    public readonly List<EnemyBrainProbeAction> Actions = new List<EnemyBrainProbeAction>();
    public override EnemyAction CreateAction(Enemy enemy)
    {
        var action = new EnemyBrainProbeAction(this, enemy); Actions.Add(action); return action;
    }
}

public sealed class EnemyBrainProbeAction : EnemyAction
{
    public int Entered, Updated;
    public float EnteredAt, CompletedAt;
    public EnemyBrainProbeAction(MonsterSkillData skill, Enemy enemy) : base(skill, enemy) { }
    public override void Enter() { Entered++; EnteredAt = Time.time; base.Enter(); }
    public override void Update()
    {
        Updated++; base.Update();
        int limit = ((EnemyBrainProbeSkill)SkillData).finishAfterUpdates;
        if (limit >= 0 && Updated >= limit) Complete();
    }
    public void Complete() { IsFinished = true; CompletedAt = Time.time; }
}

public sealed class EnemyBrainProbePattern : BossPatternSO
{
    public MonsterSkillData skill;
    public Action<Boss> onEnter, onNext;
    public readonly List<EnemyBrainProbePatternRuntime> Runtimes = new List<EnemyBrainProbePatternRuntime>();
    public override IBossPattern CreateRuntime(Boss boss)
    {
        var runtime = new EnemyBrainProbePatternRuntime(boss, skill, onEnter, onNext); Runtimes.Add(runtime); return runtime;
    }
}

public sealed class EnemyBrainProbePatternRuntime : IBossPattern
{
    public readonly Boss Owner;
    private readonly MonsterSkillData _skill;
    private readonly Action<Boss> _onEnter, _onNext;
    public int Entered, Exited, NextCalls;
    public bool Loop => true;
    public EnemyBrainProbePatternRuntime(Boss owner, MonsterSkillData skill, Action<Boss> onEnter, Action<Boss> onNext)
    { Owner = owner; _skill = skill; _onEnter = onEnter; _onNext = onNext; }
    public void Enter() { Entered++; _onEnter?.Invoke(Owner); }
    public bool TryGetNextStep(out BossPatternStep step)
    {
        NextCalls++; _onNext?.Invoke(Owner); step = BossPatternStep.Attack(_skill); return NextCalls == 1;
    }
    public void Exit() { Exited++; }
}
