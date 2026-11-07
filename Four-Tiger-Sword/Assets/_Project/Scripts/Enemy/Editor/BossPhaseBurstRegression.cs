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

/// <summary>Focused phase-opener scheduling, cylindrical damage, invulnerability and cleanup checks.</summary>
public static class BossPhaseBurstRegression
{
    private const string Source = "Assets/_Project/Generated/Bosses/SwordRobotBoss/SwordRobotBoss.prefab";
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static bool _running;
    private static string _status = "idle";
    private static int _checks;
    [Serializable] private sealed class Report { public string status; public int checks; }

    public static string RunAuthoring()
    {
        var boss = AssetDatabase.LoadAssetAtPath<GameObject>(Source)?.GetComponent<Boss>();
        Check(boss != null && boss.PhaseConfiguration.Count == 2, "The authored two-phase boss is missing.");
        Check(boss.PhaseConfiguration[0].entryPattern == null, "Phase one must not gain a phase-entry burst.");
        var opening = boss.PhaseConfiguration[1].entryPattern as BossTacticalPatternSO;
        Check(opening != null && opening.TryGetFirstAttack(null, out var skill) && skill is BossPhaseBurstSkillSO,
            "Phase two must own an explicit burst entry pattern.");
        var burst = (BossPhaseBurstSkillSO)opening.steps.First(step => step.kind == BossTacticalStepKind.Attack).skill;
        Check(!burst.Validate().Any() && !burst.isParryable, "The authored phase burst is invalid or parryable.");
        Check(Mathf.Approximately(burst.radius, 12f) && Mathf.Approximately(burst.chargeDuration, 2.5f)
            && Mathf.Approximately(burst.activeDuration, .15f) && Mathf.Approximately(burst.recoveryTime, 1.2f),
            "The phase burst must retain its authored warning, radius, active time and recovery.");
        Check(boss.PhaseConfiguration[1].combatProfile.patterns.All(option => option.pattern != opening),
            "The one-time opening must not be selectable as a repeatable normal pattern.");
        return "Boss phase burst authoring checks passed.";
    }

    public static string StartPlayMode()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling || _running)
            throw new InvalidOperationException("Start phase-burst checks once in idle Play Mode.");
        _running = true; _status = "running"; _checks = 0;
        _ = RunRuntime();
        return PlayModeStatus();
    }

    public static string PlayModeStatus() => JsonUtility.ToJson(new Report { status = _status, checks = _checks });
    public static string Status() => PlayModeStatus();

    private static async Task RunRuntime()
    {
        Fixture fixture = null;
        string outcome = "passed";
        try
        {
            fixture = new Fixture();
            await VerifyEntryScheduling(fixture);
            await VerifyFailedEntry(fixture);
            await VerifyBurst(fixture);
        }
        catch (Exception exception)
        { outcome = "failed: " + exception.GetBaseException().Message; Debug.LogException(exception); }
        finally
        {
            try { if (fixture != null) await fixture.Dispose(); }
            catch (Exception exception) { outcome = "cleanup failed: " + exception.GetBaseException().Message; Debug.LogException(exception); }
            _running = false; _status = outcome;
        }
    }

    private static async Task VerifyEntryScheduling(Fixture f)
    {
        var normal = f.Probe(); var opening = f.Probe();
        var boss = f.Actor(f.Pattern(normal), f.Pattern(opening), true);
        await Until(() => normal.Actions.Count == 1, "Phase one's normal pattern did not start.");
        boss.SetCombatTarget(null);
        boss.TakeDamage(1000f, poiseDamage: 0f);
        await Until(() => boss.PhaseIndex == 1 && !boss.IsTransitioning, "The boss did not enter its second phase.");
        var tactical = ((BossBrain)boss.Brain).Tactical;
        Check(tactical.IsEntryPending && opening.Actions.Count == 0 && !boss.IsInvulnerable,
            "A targetless phase entry must remain pending without giving permanent invulnerability.");
        Check(!tactical.RequestSkill(normal), "An explicit skill request must not replace a pending phase opener.");
        tactical.Exit(); tactical.Enter();
        Check(tactical.IsEntryPending, "State re-entry must not discard an unstarted phase opener.");
        boss.SetCombatTarget(f.Target);
        tactical.Update();
        Check(opening.Actions.Count == 1 && boss.CurrentAction == opening.Actions[0] && normal.Actions.Count == 1,
            "The opener must run before normal attacks, movement or mobility.");
        Check(!tactical.IsEntryPending, "A started opener must be consumed for the current phase generation.");
        Check(!tactical.RequestSkill(normal), "An explicit skill request must not interrupt an executing phase opener.");
        opening.Actions[0].Complete(); tactical.Update();
        Check(tactical.DebugState == "Positioning" && boss.CurrentAction == null,
            "The opener's authored aftermath must not gain option, additional or reposition recovery.");
        tactical.Update();
        Check(normal.Actions.Count == 2, "Normal attacks must become selectable immediately after the opener's own recovery.");
        tactical.Exit(); tactical.Enter(); tactical.Update();
        Check(opening.Actions.Count == 1, "Reaction-state re-entry must never replay a consumed phase opener.");
        boss.SetCombatTarget(null); tactical.Update(); boss.SetCombatTarget(f.Target); tactical.Update();
        Check(opening.Actions.Count == 1, "Target loss and recovery must not replay the phase opener.");
        boss.gameObject.SetActive(false);

        // An opener interrupted after it starts is still a single attempt, rather than an unlimited retry.
        var interrupted = f.Probe();
        boss = f.Actor(f.Pattern(f.Probe()), f.Pattern(interrupted), false);
        await Until(() => interrupted.Actions.Count == 1, "The interruption fixture did not start its opener.");
        tactical = ((BossBrain)boss.Brain).Tactical;
        boss.SetCombatTarget(null); tactical.Update();
        boss.SetCombatTarget(f.Target); tactical.Enter(); tactical.Update();
        Check(interrupted.Actions.Count == 1 && !tactical.IsEntryPending,
            "An opener cancelled after actual start must remain consumed.");
        boss.gameObject.SetActive(false);
    }

    private static async Task VerifyBurst(Fixture f)
    {
        var burst = f.New<BossPhaseBurstSkillSO>();
        burst.isParryable = false; burst.chargeDuration = .3f; burst.activeDuration = .12f; burst.recoveryTime = .15f;
        burst.preparationDuration = .04f; burst.releaseHitDelay = .08f;
        burst.executeRange = burst.engageRange = 100f; burst.playerHitReaction = EnemyPlayerHitReaction.Knockdown;
        var elevated = f.Receiver("Raised-platform receiver", new Vector3(3f, 2f, 0f));
        elevated.gameObject.AddComponent<SphereCollider>().radius = .25f;
        var outside = f.Receiver("Outside-radius receiver", new Vector3(13f, 0f, 0f));
        var normal = f.Probe();
        var boss = f.Actor(f.Pattern(normal), f.Pattern(burst), false);
        await Until(() => boss.CurrentAction is BossPhaseBurstAction, "The real phase burst did not start.");
        var action = (BossPhaseBurstAction)boss.CurrentAction;
        Vector3 center = action.Center;
        Check(action.IsInvulnerable && action.BossStage == BossAttackStage.Startup
            && !ParryEventBus.IsTelegraphActive(action), "The non-parryable startup must be protected without opening a parry telegraph.");
        float hp = boss.EnemyStat.CurrentHp;
        float poise = boss.GetComponent<PoiseHandler>().PoiseRatio;
        boss.TakeDamage(1000f, poiseDamage: 10000f, power: Vector3.right * 40f, staggerResistLevel: StaggerResistLevel.HIGH);
        var statuses = boss.gameObject.AddComponent<StatusEffectHandler>();
        bool bound = statuses.TryApply(new BindEffect(1f));
        Check(Mathf.Approximately(boss.EnemyStat.CurrentHp, hp)
            && Mathf.Approximately(boss.GetComponent<PoiseHandler>().PoiseRatio, poise)
            && !boss.PendingGroggy && !boss.IsGroggy && !boss.IsHurt && !boss.IsKnockbacking && !bound,
            "Burst startup must resist health, posture, reaction, knockback and binding together.");
        action.OnParried();
        Check(!action.WasParried, "The phase burst must not accept direct parry cancellation.");
        boss.ActorClock.Request(boss, 1f, 0f, HitStopStacking.Replace);
        float progress = action.ChargeProgress;
        await Frames(3);
        Check(Mathf.Approximately(progress, action.ChargeProgress) && action.Center == center && elevated.Hits == 0,
            "The warning must freeze on actor hitstop and keep its original center.");
        boss.ActorClock.ClearStops();
        await Until(() => elevated.Hits == 1, "A receiver on the two-metre platform did not receive cylindrical burst damage.");
        Check(elevated.LastSource == action && !elevated.LastParryable && outside.Hits == 0,
            "Burst damage must keep its action source, reject parry, and exclude targets outside the radius.");
        await Until(() => action.BossStage == BossAttackStage.Recovery, "The burst did not enter its own punishable recovery.");
        Check(!action.IsInvulnerable && elevated.Hits == 1 && action.Center == center,
            "Recovery must remove invulnerability and duplicate colliders must not cause repeated damage.");
        boss.TakeDamage(1f, poiseDamage: 0f);
        Check(boss.EnemyStat.CurrentHp < hp, "The burst's aftermath must allow real retaliation damage.");
        float interruptedAt = Time.time, remaining = action.RecoveryRemaining;
        Check(statuses.TryApply(new BindEffect(.04f)), "Burst recovery must allow an ordinary crowd-control effect.");
        await Until(() => normal.Actions.Count > 0, "The real burst did not return to normal attacks after its own recovery.");
        Check(normal.Actions[0].EnteredAt - interruptedAt >= remaining - .025f,
            "Interrupting the burst aftermath must not erase its remaining punish window.");
        Check(!action.IsActive && !action.IsInvulnerable && Field<int>(boss, "_moveLockCount") == 0,
            "A completed phase burst must release its action, invulnerability and movement lock.");
        boss.gameObject.SetActive(false);
    }

    private static async Task VerifyFailedEntry(Fixture f)
    {
        var unavailable = f.New<BossPhaseBurstSkillSO>();
        unavailable.isParryable = false; unavailable.minimumRange = 0f;
        unavailable.executeRange = unavailable.engageRange = .5f;
        var normal = f.Probe();
        var boss = f.Actor(f.Pattern(normal), f.Pattern(unavailable), false);
        await Until(() => boss.Brain != null && !string.IsNullOrEmpty(((BossBrain)boss.Brain).Tactical.LastFailure),
            "An unavailable opening command did not record its bounded failure.");
        var tactical = ((BossBrain)boss.Brain).Tactical;
        Check(!tactical.IsEntryPending && !boss.IsInvulnerable,
            "A definitively failed opening command must not retain pending invulnerability or retry each frame.");
        await Until(() => normal.Actions.Count > 0, "Normal patterns did not resume after an opening-command failure.");
        Check(normal.Actions.Count == 1 && !tactical.IsEntryPending,
            "The failed entry must remain consumed once ordinary combat resumes.");
        boss.gameObject.SetActive(false);
    }

    private static async Task Frames(int count)
    { int frame = Time.frameCount + count; await Until(() => Time.frameCount >= frame, "Regression frames stopped advancing."); }

    private static async Task Until(Func<bool> condition, string message)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 8d;
        while (!condition())
        {
            if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > deadline) throw new InvalidOperationException(message);
            await Task.Yield();
        }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); _checks++; }

    private static T Field<T>(object owner, string name)
    {
        for (Type type = owner.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Hidden | BindingFlags.DeclaredOnly);
            if (field != null) return (T)field.GetValue(owner);
        }
        throw new MissingFieldException(owner.GetType().Name, name);
    }

    private static void Set(object owner, string name, object value)
    {
        for (Type type = owner.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Hidden | BindingFlags.DeclaredOnly);
            if (field == null) continue;
            field.SetValue(owner, value); return;
        }
        throw new MissingFieldException(owner.GetType().Name, name);
    }

    private sealed class Fixture
    {
        private readonly Scene _scene;
        private readonly List<Object> _assets = new List<Object>();
        private readonly NavMeshDataInstance _navigation;
        private readonly Vector3 _origin = new Vector3(16500f, 0f, 16500f);
        private readonly float _oldTimeScale = Time.timeScale;
        private readonly CombatSettings _oldSettings = CombatSettings.Active;
        public Transform Target { get; }

        public Fixture()
        {
            Time.timeScale = 1f;
            _scene = SceneManager.CreateScene("BossPhaseBurst-" + Guid.NewGuid().ToString("N"));
            var navigation = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0),
                new List<NavMeshBuildSource> { new NavMeshBuildSource {
                    shape = NavMeshBuildSourceShape.Box, size = new Vector3(40f, .5f, 40f),
                    transform = Matrix4x4.TRS(_origin + Vector3.down * .25f, Quaternion.identity, Vector3.one) } },
                new Bounds(_origin, new Vector3(40f, 8f, 40f)), Vector3.zero, Quaternion.identity);
            if (navigation == null) throw new InvalidOperationException("Could not build phase-burst regression navigation.");
            _assets.Add(navigation); _navigation = NavMesh.AddNavMeshData(navigation);
            var target = Host("Assigned target"); Target = target.transform; Target.position = _origin + Vector3.forward * 3f;
            var settingsHost = Host("Burst regression combat settings"); settingsHost.SetActive(false);
            var settings = settingsHost.AddComponent<CombatSettings>();
            var table = New<ElementEffectiveManager>();
            foreach (ElementType attack in Enum.GetValues(typeof(ElementType)))
                foreach (ElementType defense in Enum.GetValues(typeof(ElementType)))
                    table.elementEffectiveList.Add(new ElementEffective { AttackType = attack, DefenseType = defense, multiplier = 1f });
            table.Initialize(); Set(settings, "_elementTable", table);
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, settings);
        }

        private GameObject Host(string name)
        { var host = new GameObject(name); SceneManager.MoveGameObjectToScene(host, _scene); return host; }

        public T New<T>() where T : ScriptableObject
        { var asset = ScriptableObject.CreateInstance<T>(); _assets.Add(asset); return asset; }

        public BossTacticalProbeSkill Probe()
        { var skill = New<BossTacticalProbeSkill>(); skill.duration = -1f; skill.executeRange = skill.engageRange = 100f; return skill; }

        public BossTacticalPatternSO Pattern(MonsterSkillData skill)
        {
            var pattern = New<BossTacticalPatternSO>();
            pattern.steps.Add(new BossTacticalStep { kind = BossTacticalStepKind.Attack, skill = skill, timeout = 8f });
            return pattern;
        }

        public BossPhaseBurstDamageProbe Receiver(string name, Vector3 offset)
        {
            var host = Host(name); host.transform.position = _origin + offset;
            host.layer = LayerMask.NameToLayer("Player");
            var collider = host.AddComponent<SphereCollider>(); collider.radius = .3f;
            Physics.SyncTransforms();
            return host.AddComponent<BossPhaseBurstDamageProbe>();
        }

        public ConfiguredBoss Actor(BossPatternSO normal, BossPatternSO opening, bool secondPhaseOnly)
        {
            var host = Host("Phase opening fixture boss"); host.SetActive(false); host.transform.position = _origin;
            var navigation = host.AddComponent<NavMeshAgent>(); navigation.speed = 0f;
            var model = new GameObject("Model"); model.transform.SetParent(host.transform, false);
            var animator = model.AddComponent<Animator>(); animator.fireEvents = false;
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/_Project/Generated/Bosses/Test_Boss/Test_Boss.controller");
            var boss = host.AddComponent<ConfiguredBoss>();
            var data = New<BossData>(); data.maxHp = 100; data.baseAtk = 10f;
            data.phaseDownSeconds = .02f; data.phaseHoldSeconds = .02f; data.phaseReviveSeconds = .02f;
            var skills = New<MonsterSkillDataSO>(); skills.skillData = new List<MonsterSkillData>(); data.monsterSkillData = skills;
            var profile = New<BossCombatProfileSO>(); profile.repositionDuration = 1f;
            profile.patterns.Add(new BossPatternOption { pattern = normal, recovery = 1f, cooldown = 0f });
            var phases = new List<BossPhaseData>();
            for (int i = 0; i < (secondPhaseOnly ? 2 : 1); i++)
            {
                var phase = New<BossPhaseData>(); phase.phaseId = "burst_test_" + i;
                phase.phaseMaxHp = 100f; phase.skillSet = skills; phase.combatProfile = profile;
                if (!secondPhaseOnly || i == 1) phase.entryPattern = opening;
                phases.Add(phase);
            }
            Set(boss, "BossData", data); Set(boss.GetComponent<EnemyStat>(), "baseData", data);
            Set(boss, "PhaseDatas", phases);
            boss.AdditionalPatternRecovery = .7f; boss.UseOverlayForOrdinaryHits = true; boss.ResistDamageKnockback = true;
            boss.SetCombatTarget(Target); host.SetActive(true); return boss;
        }

        public async Task Dispose()
        {
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, _oldSettings);
            if (_scene.IsValid() && _scene.isLoaded)
            {
                foreach (var root in _scene.GetRootGameObjects()) root.SetActive(false);
                var unload = SceneManager.UnloadSceneAsync(_scene);
                if (unload != null) await Until(() => unload.isDone, "Phase-burst regression cleanup timed out.");
            }
            _navigation.Remove();
            foreach (var asset in _assets) if (asset != null) Object.Destroy(asset);
            Time.timeScale = _oldTimeScale;
        }
    }
}

public sealed class BossPhaseBurstDamageProbe : MonoBehaviour, IDamageable
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
