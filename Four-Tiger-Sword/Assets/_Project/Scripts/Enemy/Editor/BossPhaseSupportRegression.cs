using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Isolated checks for nonfinal depletion, phase cleanup and detached pending water hits.</summary>
public static class BossPhaseSupportRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    [MenuItem("Tools/Studio Baekho/Enemy/Run Boss Phase Support Regression")]
    private static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run boss phase support checks in Edit Mode.");
        checks = 0;
        using (var fixture = new Fixture())
        {
            VerifyTimingAndFinalDeath(fixture);
            VerifyEffectsResetAndRejection(fixture);
            VerifyDetachedWaterHits(fixture);
            VerifyRemovedBindVfx(fixture);
            VerifyEncounterMembership(fixture);
        }
        return $"Boss phase support: {checks} checks passed.";
    }

    private static void VerifyTimingAndFinalDeath(Fixture f)
    {
        var boss = f.Boss();
        int deaths = 0, depletions = 0;
        boss.OnDied += () => deaths++;
        boss.OnPhaseDepleted += _ => depletions++;
        var result = boss.TakeDamage(1000f, poiseDamage: 0f);
        Check(result.Applied && !result.Killed && !boss.IsDead && boss.IsTransitioning
            && depletions == 1 && deaths == 0 && boss.PhaseGeneration == 1,
            "First depletion must be nonfinal, latched once and visible to synchronous callers.");
        Near(result.HealthDamage, 60f, "Overflow must stop at the depleted phase HP.");
        Check(!boss.TakeDamage(1000f, poiseDamage: 0f).Applied && depletions == 1,
            "Repeated same-frame hits must be rejected during revival.");
        Tick(boss, .5f);
        Near(boss.ReviveProgress, 0f, "The falling segment must leave the bar empty.");
        Tick(boss, 1f);
        Near(boss.ReviveProgress, 0f, "The hold segment must leave the bar empty.");
        Tick(boss, .75f);
        Near(boss.ReviveProgress, .5f, "The rising segment must expose normalized revival progress.");
        Near(boss.EnemyStat.CurrentHp, 0f, "Display progress must not make the actor damageable before revival finishes.");
        Tick(boss, 0f);
        Near(boss.ReviveProgress, .5f, "A zero-delta tick must not advance revival.");
        Tick(boss, .75f);
        Check(!boss.IsTransitioning && boss.PhaseIndex == 1 && deaths == 0 && depletions == 1,
            "Completing revival must enter exactly the next phase without confirming a death.");
        Near(boss.EnemyStat.CurrentHp, 40f, "Revival must fill the new independent HP pool.");
        Near(boss.EnemyStat.MaxHp, 40f, "The current maximum must follow the phase HP pool.");
        Check(boss.TakeDamage(1000f, poiseDamage: 0f).Killed && boss.IsDead && deaths == 1 && depletions == 1,
            "Only final-phase depletion must emit confirmed death.");
    }

    private static void VerifyEffectsResetAndRejection(Fixture f)
    {
        var boss = f.Boss();
        var handler = boss.gameObject.AddComponent<StatusEffectHandler>();
        var forms = boss.gameObject.AddComponent<FormTargetEffects>();
        var wood = boss.gameObject.AddComponent<WoodTargetEffects>();
        if (Field<Enemy>(forms, "_enemy") == null) typeof(FormTargetEffects).GetMethod("OnEnable", Hidden).Invoke(forms, null);
        if (Field<Enemy>(wood, "_enemy") == null) typeof(WoodTargetEffects).GetMethod("OnEnable", Hidden).Invoke(wood, null);
        var woodData = f.Asset<WoodFormActionDataSO>();
        woodData.MaximumStacks = 5; woodData.StacksPerHit = 1;
        var probe = new EffectProbe();
        Check(handler.TryApply(probe) && handler.TryApply(new BindEffect(20f))
            && handler.TryApply(new DefenseReductionEffect(10f, 20f)), "Phase cleanup fixture could not apply initial effects.");
        float defense = boss.Data.baseDef;
        Check(boss.IsRoot && boss.EnemyStat.GetStat(EnemyStatType.DEF) < defense,
            "Initial status effects must have changed actual actor state.");
        for (int i = 0; i < 5; i++) forms.AddFireStack(default, 1f);
        forms.ApplyArmorShred(false); forms.ApplyArmorShred(true);
        wood.AddStacks(woodData);
        wood.Pull(boss.transform.position + Vector3.forward * 5f, 3f, 2f, .5f);
        Check(forms.FireStacks == 5 && forms.ArmorReductionRatio > 0f && wood.Stacks == 1
            && Field<bool>(wood, "_movementLocked"), "Persistent form stacks, debuffs and pull were not established.");
        boss.TakeDamage(1000f, poiseDamage: 0f);
        Tick(boss, 0f); // Cleanup is committed at the next safe transition boundary.
        Check(!handler.Has<EffectProbe>() && !handler.Has<BindEffect>() && !handler.Has<DefenseReductionEffect>()
            && probe.Removed == 1 && !boss.IsRoot, "Safe transition start must remove statuses and run each removal callback once.");
        Near(boss.EnemyStat.GetStat(EnemyStatType.DEF), defense, "Phase cleanup must remove status defense modifiers.");
        Check(forms.FireStacks == 0 && forms.BasicArmorTime == 0f && forms.SkillArmorTime == 0f
            && wood.Stacks == 0 && wood.StackRemaining == 0f && Field<float>(wood, "_pullRemaining") == 0f
            && !Field<bool>(wood, "_movementLocked"), "Phase cleanup must reset persistent form effects and release pull locks.");
        handler.ClearAll(); forms.ResetForNewPhase(); wood.ResetForNewPhase();
        Check(probe.Removed == 1, "Repeated explicit cleanup must remain idempotent.");

        // Positive HP here deliberately isolates transition guards from the pre-existing HP0 guards.
        boss.EnemyStat.CurrentHp = 1f;
        var rejected = new EffectProbe();
        Check(!handler.TryApply(rejected) && rejected.Applied == 0,
            "A transitioning boss must reject status application before callbacks.");
        forms.AddFireStack(default, 1f); forms.ApplyArmorShred(false); forms.ApplyArmorShred(true);
        wood.AddStacks(woodData, true); wood.Pull(Vector3.zero, 3f, 2f, 0f);
        Check(forms.FireStacks == 0 && forms.ArmorReductionRatio == 0f && wood.Stacks == 0
            && Field<float>(wood, "_pullRemaining") == 0f && !Field<bool>(wood, "_movementLocked"),
            "Transition guards must block new stacks, armor shred and pull independently of the HP value.");
        boss.EnemyStat.CurrentHp = 0f;
        Tick(boss, 3f);
        Check(handler.TryApply(rejected) && rejected.Applied == 1,
            "Revived actors must accept normal effects again.");
        forms.AddFireStack(default, 1f); forms.ApplyArmorShred(false); wood.AddStacks(woodData);
        Check(forms.FireStacks == 1 && forms.BasicArmorTime > 0f && wood.Stacks == 1,
            "Revived actors must accept persistent form effects again.");
    }

    private static void VerifyDetachedWaterHits(Fixture f)
    {
        var boss = f.Boss();
        var natural = new WaterDelayedDamageEffect(3f, 0f, ElementType.ELEMENT_WATER, 0f, 1f);
        natural.OnApply(boss.gameObject);
        var scheduled = PendingWater(natural);
        Check(scheduled.MoveNext(), "A scheduled water hit must first yield its authored delay.");
        natural.OnRemove(boss.gameObject); // Natural expiry removes the scheduling status, not its independent burst.
        float before = boss.EnemyStat.CurrentHp;
        Check(scheduled.MoveNext() && boss.EnemyStat.CurrentHp < before,
            "Natural removal of the scheduling effect must not cancel its pending water burst.");

        var vfx = f.Make("PhaseSupportWaterVfx");
        var stale = new WaterDelayedDamageEffect(3f, 0f, ElementType.ELEMENT_WATER, 0f, 1f, vfx);
        stale.OnApply(boss.gameObject);
        var detached = PendingWater(stale);
        Check(detached.MoveNext(), "The stale water burst must be waiting when phase depletion occurs.");
        int generation = boss.PhaseGeneration;
        boss.TakeDamage(1000f, poiseDamage: 0f);
        Check(boss.PhaseGeneration == generation + 1, "Depletion must invalidate pending hits synchronously.");
        Tick(boss, 3f);
        before = boss.EnemyStat.CurrentHp;
        int clones = CountClones(vfx.name);
        Check(!detached.MoveNext(), "A previous-phase detached burst must end instead of reaching the revived actor.");
        Near(boss.EnemyStat.CurrentHp, before, "A previous-phase pending hit must not damage revived HP.");
        Check(CountClones(vfx.name) == clones, "A previous-phase pending hit must not create impact VFX.");
    }

    private static void VerifyEncounterMembership(Fixture f)
    {
        var boss = f.Boss();
        var member = boss.gameObject.AddComponent<DungeonEncounterMember>();
        member.Initialize(null);
        boss.enabled = true;
        boss.TakeDamage(1000f, poiseDamage: 0f);
        Check(boss.EnemyStat.IsDead && !boss.IsDead && !member.IsDead && !member.HasDied,
            "An HP0 downed boss must remain an undefeated encounter member.");
        Tick(boss, 3f);
        Check(!member.IsDead && !member.HasDied, "Revival must retain the same undefeated encounter member.");
        boss.TakeDamage(1000f, poiseDamage: 0f);
        Check(member.IsDead && member.HasDied, "Final boss death must still confirm the encounter member's defeat.");
    }

    private static void VerifyRemovedBindVfx(Fixture f)
    {
        var boss = f.Boss();
        var vfx = f.Make("PhaseSupportBindVfx");
        var bind = new BindEffect(10f);
        bind.OnApply(boss.gameObject);
        Set(bind, typeof(BindEffect), "_vfxPrefab", vfx);
        var pending = (IEnumerator)typeof(BindEffect).GetMethod("SpawnVFX", Hidden).Invoke(bind,
            new object[] { boss.gameObject, Field<int>(bind, "_applicationVersion") });
        Check(pending.MoveNext(), "Bind VFX must initially wait for its delay.");
        bind.OnRemove(boss.gameObject);
        int clones = CountClones(vfx.name);
        Check(!pending.MoveNext() && CountClones(vfx.name) == clones && !boss.IsRoot,
            "A removed bind must not respawn its delayed VFX after phase cleanup.");
    }

    private static IEnumerator PendingWater(WaterDelayedDamageEffect effect)
        => (IEnumerator)typeof(WaterDelayedDamageEffect).GetMethod("DamageApply", Hidden).Invoke(effect, null);
    private static int CountClones(string name)
        => Resources.FindObjectsOfTypeAll<GameObject>().Count(value => value.name == name + "(Clone)");
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
    private static void Tick(Boss boss, float deltaTime)
    {
        typeof(Boss).GetMethod("BeginPhaseTransition", Hidden).Invoke(boss, null);
        typeof(Boss).GetMethod("TickPhaseTransition", Hidden).Invoke(boss, new object[] { deltaTime });
    }
    private static void Set(object target, Type type, string name, object value)
        => type.GetField(name, Hidden).SetValue(target, value);
    private static void Near(float actual, float expected, string message)
        => Check(Mathf.Abs(actual - expected) <= .001f, message + $" Actual: {actual}, expected: {expected}.");
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("Boss phase support regression: " + message);
    }

    private sealed class EffectProbe : IStatusEffect
    {
        public int Applied, Removed;
        private EnemyStat stats;
        public bool IsExpired => false;
        public void OnApply(GameObject target)
        {
            Applied++;
            stats = target.GetComponent<EnemyStat>();
            stats.AddModifier(EnemyStatType.DEF, -5f, 0f);
        }
        public void OnUpdate(float deltaTime) { }
        public void OnRemove(GameObject target)
        {
            Removed++;
            stats.RemoveModifier(EnemyStatType.DEF, -5f, 0f);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Scene scene = EditorSceneManager.NewPreviewScene();
        private readonly List<Object> assets = new List<Object>();
        private readonly CombatSettings previous = CombatSettings.Active;
        private readonly UnityEngine.Random.State random = UnityEngine.Random.state;

        public Fixture()
        {
            var table = Asset<ElementEffectiveManager>();
            foreach (ElementType attack in Enum.GetValues(typeof(ElementType)))
                foreach (ElementType defense in Enum.GetValues(typeof(ElementType)))
                    table.elementEffectiveList.Add(new ElementEffective { AttackType = attack, DefenseType = defense, multiplier = 1f });
            table.Initialize();
            var settings = Make("Phase support combat settings").AddComponent<CombatSettings>();
            Set(settings, typeof(CombatSettings), "_elementTable", table);
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, settings);
        }

        public T Asset<T>() where T : ScriptableObject
        { var value = ScriptableObject.CreateInstance<T>(); assets.Add(value); return value; }

        public GameObject Make(string name)
        {
            var actor = new GameObject(name);
            actor.SetActive(false);
            SceneManager.MoveGameObjectToScene(actor, scene);
            actor.transform.position = new Vector3(18300f, 900f, 18300f);
            return actor;
        }

        public ConfiguredBoss Boss()
        {
            var data = Asset<BossData>(); data.maxHp = 100; data.baseDef = 20f;
            data.phaseDownSeconds = 1f; data.phaseHoldSeconds = .5f; data.phaseReviveSeconds = 1.5f;
            var first = Asset<BossPhaseData>(); first.phaseMaxHp = 60f;
            var second = Asset<BossPhaseData>(); second.phaseMaxHp = 40f;
            var actor = Make("Phase support boss");
            actor.AddComponent<NavMeshAgent>().enabled = false;
            var boss = actor.AddComponent<ConfiguredBoss>();
            boss.EnemyStat = actor.GetComponent<EnemyStat>();
            Set(boss, typeof(Boss), "BossData", data);
            Set(boss, typeof(Boss), "PhaseDatas", new List<BossPhaseData> { first, second });
            Set(boss.EnemyStat, typeof(EnemyStat), "baseData", data);
            typeof(EnemyStat).GetMethod("InitializeFromData", Hidden).Invoke(boss.EnemyStat, null);
            boss.StaggerResistLevel = StaggerResistLevel.SUPER_ARMOR;
            actor.SetActive(true);
            typeof(Boss).GetMethod("CreateBrain", Hidden).Invoke(boss, null);
            return boss;
        }

        public void Dispose()
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            foreach (var asset in assets) if (asset != null) Object.DestroyImmediate(asset);
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, previous);
            UnityEngine.Random.state = random;
        }
    }
}
