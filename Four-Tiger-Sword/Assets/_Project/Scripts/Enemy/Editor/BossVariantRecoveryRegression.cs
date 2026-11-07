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

/// <summary>Focused live tactical checks for the comparison bosses' extra recovery and hit policy.</summary>
public static class BossVariantRecoveryRegression
{
    private const string Source = "Assets/_Project/Generated/Bosses/SwordRobotBoss/SwordRobotBoss.prefab";
    private const string Variants = "Assets/_Project/Generated/Bosses/HitReactionVariants/SwordRobotBoss_";
    private static readonly string[] Paths = { Source, Variants + "FinalIK.prefab", Variants + "Additive.prefab" };
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly Dictionary<ScriptableObject, string> SharedSnapshots = new Dictionary<ScriptableObject, string>();
    private static bool _running;
    private static string _status = "idle";
    private static int _checks;
    private static readonly List<Sample> Samples = new List<Sample>();

    [Serializable] private sealed class Sample { public string name; public float expected, actual; }
    [Serializable] private sealed class Result { public string status; public int checks; public List<Sample> samples; }

    public static string RunAuthoring()
    {
        var prefabs = Paths.Select(AssetDatabase.LoadAssetAtPath<GameObject>).ToArray();
        Check(prefabs.All(p => p != null), "The source and both comparison prefabs are required.");
        var original = prefabs[0].GetComponent<ConfiguredBoss>();
        Check(original != null && original.AdditionalPatternRecovery == 0f && !original.ResistDamageKnockback
            && Mathf.Approximately(original.PhaseHealthMultiplier, 1f)
            && original.Mobility != null && !original.Mobility.enabled,
            "The original boss must keep zero additional recovery and its existing knockback policy.");
        foreach (var prefab in prefabs.Skip(1))
        {
            var boss = prefab.GetComponent<ConfiguredBoss>();
            Check(boss != null && Mathf.Approximately(boss.AdditionalPatternRecovery, .2f)
                && boss.ResistDamageKnockback && boss.UseOverlayForOrdinaryHits
                && Mathf.Approximately(boss.PhaseHealthMultiplier, .7f)
                && boss.Mobility != null && boss.Mobility.enabled,
                "Only the comparison variants must opt into .2 seconds and ordinary-hit continuity.");
            Check(boss.Data == original.Data && boss.PhaseConfiguration.SequenceEqual(original.PhaseConfiguration),
                "Comparison recovery must not fork or change shared boss phases, skills, or profiles.");
        }
        return "Boss variant recovery authoring checks passed.";
    }

    public static string StartPlayMode()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling || _running)
            throw new InvalidOperationException("Start this regression once in idle Play Mode.");
        _running = true; _status = "running"; _checks = 0; Samples.Clear();
        _ = RunRuntime();
        return PlayModeStatus();
    }

    public static string PlayModeStatus() => JsonUtility.ToJson(new Result
        { status = _status, checks = _checks, samples = Samples });

    private static async Task RunRuntime()
    {
        Fixture fixture = null;
        string outcome = "passed";
        try
        {
            RunAuthoring();
            SnapshotSharedAssets();
            fixture = new Fixture();
            await VerifyCompletionAndClock(fixture);
            await VerifyRequestedAction(fixture);
            await VerifyAuthoredRecovery(fixture);
            await VerifyFailureAndParry(fixture);
            await VerifyMovementContinuity(fixture);
            await VerifyStatusExceptions(fixture);
            foreach (var snapshot in SharedSnapshots)
                Check(snapshot.Key != null && EditorJsonUtility.ToJson(snapshot.Key) == snapshot.Value,
                    "Runtime comparison checks changed shared asset: " + snapshot.Key.name);
        }
        catch (Exception exception)
        {
            outcome = "failed: " + exception.GetBaseException().Message;
            Debug.LogException(exception);
        }
        finally
        {
            try { if (fixture != null) await fixture.Dispose(); }
            catch (Exception exception) { outcome = "cleanup failed: " + exception.GetBaseException().Message; Debug.LogException(exception); }
            SharedSnapshots.Clear();
            _status = outcome; _running = false;
        }
    }

    private static async Task VerifyCompletionAndClock(Fixture f)
    {
        foreach (float extra in new[] { 0f, .4f })
        {
            var skill = f.Probe();
            var boss = f.Actor(f.Profile(f.Pattern(skill)), extra);
            var tactic = await Started(boss);
            ((BossTacticalProbeAction)boss.CurrentAction).Complete();
            tactic.Update();
            Check(tactic.DebugState == "Recovering" && boss.CurrentAction == null,
                "A completed real tactical pattern must enter its final recovery.");
            Near("completed pattern, extra=" + extra, Remaining(tactic), .25f + extra);
            if (extra > 0f)
            {
                HighPowerHit(boss, tactic, "Recovering");
                float beforeResume = Remaining(tactic);
                boss.IsGroggy = true;
                tactic.Exit();
                boss.IsGroggy = false;
                tactic.Enter();
                Near("recovery resume must not add again", Remaining(tactic), beforeResume);

                boss.ActorClock.Request(boss, 5f, 0f, HitStopStacking.Maximum);
                float frozen = Remaining(tactic);
                await Frames(4);
                Near("added recovery respects actor hitstop", Remaining(tactic), frozen);
                boss.ActorClock.ClearStops();
                await Until(() => Remaining(tactic) < frozen - .001f,
                    "Extra recovery did not resume after actor-local hitstop.");
                Check(tactic.DebugState == "Recovering", "Actor hitstop must preserve the recovery state.");
            }
            boss.gameObject.SetActive(false);
        }
    }

    private static async Task VerifyRequestedAction(Fixture f)
    {
        var first = f.Probe(); var requested = f.Probe();
        var profile = f.Profile(f.Pattern(first)); profile.failureRecovery = .12f;
        var boss = f.Actor(profile, .4f);
        var tactic = await Started(boss);
        boss.RequestImmediateSkill(requested);
        tactic.Update();
        Check(boss.CurrentAction?.SkillData == requested && boss.CurrentAction.IsActive,
            "An explicit requested action must enter through the real tactical executor.");
        ((BossTacticalProbeAction)boss.CurrentAction).Complete();
        tactic.Update();
        Near("explicit requested action", Remaining(tactic), .52f);
        Check(tactic.DebugState == "Recovering", "A completed requested action must retain the added interval.");
        boss.gameObject.SetActive(false);
    }

    private static async Task VerifyAuthoredRecovery(Fixture f)
    {
        var skill = f.Probe();
        var pattern = f.Pattern(skill);
        pattern.steps.Add(new BossTacticalStep { kind = BossTacticalStepKind.Recover, seconds = .6f });
        pattern.steps.Add(new BossTacticalStep { kind = BossTacticalStepKind.Wait, seconds = .5f });
        var boss = f.Actor(f.Profile(pattern), .4f);
        var tactic = await Started(boss);
        ((BossTacticalProbeAction)boss.CurrentAction).Complete();
        tactic.Update();
        Check(Field<bool>(tactic, "_recoverWithinPattern"), "The authored recovery fixture did not enter its inner step.");
        Near("authored inner recovery unchanged", Remaining(tactic), .6f);
        HighPowerHit(boss, tactic, "Recovering");
        await Until(() => tactic.DebugState == "Waiting", "Authored recovery did not advance to its following wait.");
        Check(Remaining(tactic) <= .5f && Remaining(tactic) > .4f,
            "An authored Wait must keep its own duration without extra final recovery.");
        boss.gameObject.SetActive(false);
    }

    private static async Task VerifyFailureAndParry(Fixture f)
    {
        var skill = f.Probe(); var pattern = f.Pattern(skill); pattern.steps[0].timeout = .06f;
        var failed = f.Actor(f.Profile(pattern), .4f);
        var tactic = await Started(failed);
        await Until(() => tactic.DebugState == "Recovering", "The bounded action timeout did not enter failure recovery.");
        Check(!string.IsNullOrEmpty(tactic.LastFailure), "The timeout fixture did not record a real failure.");
        Check(Remaining(tactic) <= .25f && Remaining(tactic) > .12f,
            "Failure recovery must keep max(option recovery, failure recovery) without adding .4 seconds.");
        failed.gameObject.SetActive(false);

        var parrySkill = f.Probe();
        var profile = f.Profile(f.Pattern(parrySkill)); profile.parryRecovery = .17f;
        var parried = f.Actor(profile, .4f);
        tactic = await Started(parried);
        var action = parried.CurrentAction;
        action.OnParried();
        tactic.Update();
        Check(!action.IsActive && parried.CurrentAction == null && tactic.DebugState == "Recovering",
            "A real tactical parry must still cancel the protected action.");
        Near("parry recovery unchanged", Remaining(tactic), .17f);
        parried.gameObject.SetActive(false);
    }

    private static async Task VerifyMovementContinuity(Fixture f)
    {
        var skill = f.Probe(); var pattern = f.Pattern(skill);
        pattern.steps.Insert(0, new BossTacticalStep
        {
            kind = BossTacticalStepKind.Move,
            movement = new BossMovementSettings { mode = BossMovementMode.StrafeLeft, duration = 1f, timeout = 3f }
        });
        var boss = f.Actor(f.Profile(pattern), .4f);
        await Until(() => Tactical(boss)?.DebugState == "Moving", "The real tactical movement step did not start.");
        var tactic = Tactical(boss);
        HighPowerHit(boss, tactic, "Moving");
        int step = tactic.DebugStep;
        await Frames(2);
        Check(tactic.DebugState == "Moving" && tactic.DebugStep == step && boss.CurrentAction == null
            && !boss.IsHurt && !boss.IsKnockbacking,
            "Ordinary damage must preserve the active tactical movement command on following frames.");
        boss.gameObject.SetActive(false);

        var repositionSkill = f.Probe(); var profile = f.Profile(f.Pattern(repositionSkill));
        profile.repositionDuration = .8f;
        boss = f.Actor(profile, .4f);
        tactic = await Started(boss);
        ((BossTacticalProbeAction)boss.CurrentAction).Complete(); tactic.Update();
        await Until(() => tactic.DebugState == "Repositioning", "Post-pattern repositioning did not start.");
        HighPowerHit(boss, tactic, "Repositioning");
        await Frames(2);
        Check(tactic.DebugState == "Repositioning", "An ordinary hit must preserve the current reposition interval.");
        boss.gameObject.SetActive(false);
    }

    private static async Task VerifyStatusExceptions(Fixture f)
    {
        foreach (bool silence in new[] { false, true })
        {
            var skill = f.Probe(); var boss = f.Actor(f.Profile(f.Pattern(skill)), .4f);
            await Started(boss);
            var action = boss.CurrentAction;
            var handler = boss.gameObject.AddComponent<StatusEffectHandler>();
            Check(handler.TryApply(silence ? (IStatusEffect)new SilenceEffect(1f) : new BindEffect(1f)),
                "The opted-in boss must retain its Bind/Silence application policy.");
            await Until(() => !action.IsActive && boss.CurrentAction == null,
                "An actual " + (silence ? "Silence" : "Bind") + " must still interrupt the protected action.");
            Check(silence ? boss.IsSilenced : boss.IsRoot,
                "The explicit control effect must remain active after it cancels the attack.");
            boss.gameObject.SetActive(false);
        }
    }

    private static void HighPowerHit(ConfiguredBoss boss, BossTacticalController tactic, string state)
    {
        float before = boss.EnemyStat.CurrentHp;
        int step = tactic.DebugStep;
        var action = boss.CurrentAction;
        var hit = boss.TakeDamage(1f, power: Vector3.right * 5f, poiseDamage: 0f,
            staggerResistLevel: StaggerResistLevel.HIGH);
        Check(hit.Applied && boss.EnemyStat.CurrentHp < before && !boss.IsHurt && !boss.IsKnockbacking
            && tactic.DebugState == state && tactic.DebugStep == step && boss.CurrentAction == action,
            "High-stagger physical damage must preserve tactical state " + state + ".");
    }

    private static async Task<BossTacticalController> Started(ConfiguredBoss boss)
    {
        await Until(() => boss.CurrentAction?.IsActive == true, "The tactical fixture failed to start a probe action.");
        return Tactical(boss);
    }

    private static BossTacticalController Tactical(ConfiguredBoss boss) => (boss.Brain as BossBrain)?.Tactical;
    private static float Remaining(BossTacticalController tactic) => Field<float>(tactic, "_remaining");
    private static void Near(string name, float actual, float expected)
    {
        Samples.Add(new Sample { name = name, expected = expected, actual = actual });
        Check(Mathf.Abs(actual - expected) < .002f, name + ": expected " + expected + ", got " + actual);
    }

    private static void SnapshotSharedAssets()
    {
        SharedSnapshots.Clear();
        foreach (string path in AssetDatabase.GetDependencies(Paths, true))
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
            if (asset is BossData || asset is BossPhaseData || asset is BossCombatProfileSO
                || asset is BossPatternSO || asset is MonsterSkillData || asset is MonsterSkillDataSO)
                SharedSnapshots[asset] = EditorJsonUtility.ToJson(asset);
        }
    }

    private static async Task Frames(int count)
    {
        int end = Time.frameCount + count;
        await Until(() => Time.frameCount >= end, "Fixture frames stopped advancing.");
    }

    private static async Task Until(Func<bool> condition, string message)
    {
        double end = Time.realtimeSinceStartupAsDouble + 8d;
        while (!condition())
        {
            if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > end)
                throw new InvalidOperationException(message);
            await Task.Yield();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Hidden).GetValue(value);
    private static void Set(object value, string name, object data)
    {
        for (Type type = value.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Hidden | BindingFlags.DeclaredOnly);
            if (field == null) continue;
            field.SetValue(value, data); return;
        }
        throw new MissingFieldException(value.GetType().Name, name);
    }

    private sealed class Fixture
    {
        private readonly Scene _scene;
        private readonly List<Object> _assets = new List<Object>();
        private readonly NavMeshDataInstance _navigation;
        private readonly Vector3 _origin = new Vector3(14000f, 0f, 14000f);
        private readonly Transform _target;
        private readonly float _timeScale = Time.timeScale;
        private readonly UnityEngine.Random.State _random = UnityEngine.Random.state;

        public Fixture()
        {
            Time.timeScale = 1f;
            _scene = SceneManager.CreateScene("BossVariantRecovery-" + Guid.NewGuid().ToString("N"));
            var navigation = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0),
                new List<NavMeshBuildSource> { new NavMeshBuildSource {
                    shape = NavMeshBuildSourceShape.Box, size = new Vector3(30f, .5f, 30f),
                    transform = Matrix4x4.TRS(_origin + Vector3.down * .25f, Quaternion.identity, Vector3.one) } },
                new Bounds(_origin, new Vector3(30f, 8f, 30f)), Vector3.zero, Quaternion.identity);
            if (navigation == null) throw new InvalidOperationException("Could not build regression navigation.");
            _assets.Add(navigation); _navigation = NavMesh.AddNavMeshData(navigation);
            var target = new GameObject("Recovery test target"); SceneManager.MoveGameObjectToScene(target, _scene);
            _target = target.transform; _target.position = _origin + Vector3.forward * 3f;
        }

        private T New<T>() where T : ScriptableObject
        { var value = ScriptableObject.CreateInstance<T>(); _assets.Add(value); return value; }

        public BossTacticalProbeSkill Probe()
        {
            var skill = New<BossTacticalProbeSkill>(); skill.duration = -1f;
            skill.cooldown = 0f; skill.engageRange = skill.executeRange = 100f;
            return skill;
        }

        public BossTacticalPatternSO Pattern(MonsterSkillData skill)
        {
            var pattern = New<BossTacticalPatternSO>();
            pattern.steps.Add(new BossTacticalStep { kind = BossTacticalStepKind.Attack, skill = skill });
            return pattern;
        }

        public BossCombatProfileSO Profile(BossPatternSO pattern)
        {
            var profile = New<BossCombatProfileSO>(); profile.decisionInterval = .02f;
            profile.turnSpeed = 0f; profile.failureRecovery = .12f; profile.parryRecovery = .17f;
            profile.patterns.Add(new BossPatternOption { pattern = pattern, cooldown = 0f, recovery = .25f });
            return profile;
        }

        public ConfiguredBoss Actor(BossCombatProfileSO profile, float extra)
        {
            var host = new GameObject("Recovery policy test boss"); host.SetActive(false);
            SceneManager.MoveGameObjectToScene(host, _scene); host.transform.position = _origin;
            var nav = host.AddComponent<NavMeshAgent>(); nav.speed = 2f; nav.stoppingDistance = .25f;
            host.AddComponent<CapsuleCollider>();
            var model = new GameObject("Model"); model.transform.SetParent(host.transform, false);
            var animator = model.AddComponent<Animator>(); animator.fireEvents = false;
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/_Project/Generated/Bosses/Test_Boss/Test_Boss.controller");
            var boss = host.AddComponent<ConfiguredBoss>();
            var data = New<BossData>(); data.maxHp = 100; data.baseAtk = 1f;
            var skills = New<MonsterSkillDataSO>(); skills.skillData = new List<MonsterSkillData>();
            data.monsterSkillData = skills;
            var phase = New<BossPhaseData>(); phase.phaseMaxHp = 100f; phase.skillSet = skills;
            phase.combatProfile = profile;
            Set(boss, "BossData", data); Set(boss.GetComponent<EnemyStat>(), "baseData", data);
            Set(boss, "PhaseDatas", new List<BossPhaseData> { phase });
            boss.AdditionalPatternRecovery = extra;
            boss.UseOverlayForOrdinaryHits = extra > 0f;
            boss.ResistDamageKnockback = extra > 0f;
            boss.SetCombatTarget(_target);
            host.SetActive(true);
            return boss;
        }

        public async Task Dispose()
        {
            if (_scene.IsValid() && _scene.isLoaded)
            {
                foreach (var root in _scene.GetRootGameObjects()) root.SetActive(false);
                var unload = SceneManager.UnloadSceneAsync(_scene);
                if (unload != null) await Until(() => unload.isDone, "Regression scene cleanup timed out.");
            }
            _navigation.Remove();
            foreach (var asset in _assets) if (asset != null) Object.Destroy(asset);
            Time.timeScale = _timeScale; UnityEngine.Random.state = _random;
        }
    }
}
