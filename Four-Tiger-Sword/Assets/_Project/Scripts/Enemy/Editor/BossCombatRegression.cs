using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Isolated authoring/combat checks. Does not write assets or change the open scene.</summary>
public static class BossCombatRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run boss combat checks in Edit Mode.");
        _checks = 0;
        using (var fixture = new Fixture())
        {
            VerifyCombat(fixture);
            VerifyPhasesAndShields(fixture);
            VerifyImmunity(fixture);
        }
        return "PASS: " + _checks + " boss combat, phase, shield and immunity checks.";
    }

    private static void VerifyCombat(Fixture fixture)
    {
        var data = fixture.Data();
        data.baseAtk = 100f; data.baseDef = 100f;
        var phase = fixture.Asset<BossPhaseData>();
        phase.phaseMaxHp = 1000f; phase.atkMultiplier = 1.5f; phase.defMultiplier = 2f;
        var boss = fixture.Boss(data, phase);
        var stats = boss.EnemyStat;
        Check(ReferenceEquals(stats.Data, data) && ReferenceEquals(boss.Data, data), "Boss and EnemyStat must share one BossData.");
        stats.AddModifier(EnemyStatType.ATK, 10f, .2f);
        stats.AddModifier(EnemyStatType.DEF, 20f, .1f);
        Near(stats.GetStat(EnemyStatType.ATK), 198f, "Phase ATK must multiply existing buffs.");
        Near(stats.GetStat(EnemyStatType.DEF), 264f, "Phase DEF must multiply existing buffs.");
        var forms = boss.gameObject.AddComponent<FormTargetEffects>();
        forms.ApplyArmorShred(false);
        Near(forms.EffectiveDefense, 234f, "Armor shred must use phase-adjusted base DEF.");
        stats.SetPhaseMultipliers(2f, 1f);
        Near(stats.GetStat(EnemyStatType.ATK), 264f, "Replacing a phase must not compound the previous multiplier.");

        var receiverData = fixture.Data(); receiverData.baseDef = 0f;
        receiverData.enableWeakness = true; receiverData.weaknessElement = ElementType.ELEMENT_FIRE;
        receiverData.weakDamageMult = 1.5f;
        receiverData.enableResistance = true; receiverData.resistElement = ElementType.ELEMENT_WATER;
        receiverData.resistDamageMult = .5f;
        var receiver = fixture.Boss(receiverData);
        Near(Hit(receiver, ElementType.ELEMENT_FIRE).HealthDamage, 150f, "Weakness must replace global 2x, not multiply it.");
        Near(Hit(receiver, ElementType.ELEMENT_WATER).HealthDamage, 50f, "Resistance must replace global 3x.");
        Near(Hit(receiver, ElementType.ELEMENT_EARTH).HealthDamage, 75f, "Unspecified elements must retain the global matchup.");
        Near(Hit(receiver, ElementType.ELEMENT_FIRE, true).HealthDamage, 100f, "True damage must bypass elemental overrides.");
        receiver.IsGroggy = true;
        Near(Hit(receiver, ElementType.ELEMENT_FIRE).HealthDamage, 225f, "Groggy and weakness must each apply once.");
        Near(Hit(receiver, ElementType.ELEMENT_FIRE, true).HealthDamage, 150f, "True damage retains incoming groggy vulnerability.");
        var effects = receiver.gameObject.AddComponent<StatusEffectHandler>();
        effects.Apply(new VulnerabilityEffect(10f, 20f));
        Near(Hit(receiver, ElementType.ELEMENT_FIRE, true).HealthDamage, 180f, "Existing vulnerability and groggy must remain independent.");
        receiver.EnemyStat.CurrentHp = 1000f;
        Near(receiver.TakeDamage(100f, ElementType.ELEMENT_FIRE, poiseDamage: 0f).HealthDamage, 100f,
            "Direct TakeDamage must retain its already-resolved damage contract.");
    }

    private static DamageResult Hit(ConfiguredBoss boss, ElementType element, bool trueDamage = false)
    {
        boss.EnemyStat.CurrentHp = 1000f;
        return DamageManager.Apply(new HitInfo(100f, element, 0f, 1f, trueDamage: trueDamage), boss, boss.gameObject);
    }

    private static void VerifyPhasesAndShields(Fixture fixture)
    {
        var data = fixture.Data(); data.initialShield = 50f;
        var first = fixture.Asset<BossPhaseData>(); first.phaseMaxHp = 500f;
        var second = fixture.Asset<BossPhaseData>(); second.phaseMaxHp = 500f;
        second.atkMultiplier = 2f; second.defMultiplier = 3f;
        second.overrideShield = true; second.shieldAmount = 20f;
        var boss = fixture.Boss(data, first, second);
        var shield = boss.GetComponent<BossShield>();
        Near(shield.CurrentAbsorption, 50f, "An unspecified first-phase shield must retain initial shield.");
        int broken = 0; shield.OnShieldBroken += () => broken++;
        var absorbed = boss.TakeDamage(.5f, poiseDamage: 0f);
        Near(absorbed.ShieldDamage, .5f, "Shield must preserve fractional absorption.");
        Near(absorbed.HealthDamage, 0f, "A full absorption must not reduce health.");
        var depleted = boss.TakeDamage(10000f, poiseDamage: 0f);
        Check(depleted.Applied && !depleted.Killed && boss.IsTransitioning && !boss.IsDead,
            "Depleting the first HP pool must begin revival without confirming a death.");
        TickTransition(boss, data.phaseDownSeconds + data.phaseHoldSeconds + data.phaseReviveSeconds);
        Check(boss.PhaseIndex == 1 && !boss.IsTransitioning, "The next authored HP pool must revive after the transition duration.");
        Near(boss.EnemyStat.CurrentHp, 500f, "Revival must fill exactly the next phase HP pool.");
        Near(shield.CurrentAbsorption, 20f, "A phase shield must replace the previous balance.");
        Near(boss.EnemyStat.GetStat(EnemyStatType.ATK), data.baseAtk * 2f, "Entering a phase must apply its ATK multiplier.");
        shield.Absorb(7f);
        boss.enabled = false; boss.enabled = true;
        Near(shield.CurrentAbsorption, 13f, "Reactivation must not refill initial or phase shield.");
        shield.SetAbsorption(0f);
        Check(broken == 1, "Only the depleted first-phase shield must emit a damage-driven shield break.");
        shield.SetAbsorption(10f);
        var penetration = boss.TakeDamage(30f, poiseDamage: 0f);
        Near(penetration.ShieldDamage, 10f, "Partial absorption must be reported as shield damage.");
        Near(penetration.HealthDamage, 20f, "Only shield overflow must reach health.");
        Check(broken == 2, "Each shield depleted by damage must emit exactly one break.");
        shield.SetAbsorption(20f);
        Check(boss.TakeDamage(10000f, poiseDamage: 0f).Killed, "Boss lethal damage must retain the death contract.");
        Near(shield.CurrentAbsorption, 0f, "Death must clean the shield.");
        Near(boss.EnemyStat.GetStat(EnemyStatType.ATK), data.baseAtk, "Death must release the phase stat multiplier.");
        first.overrideShield = true; first.shieldAmount = 0f;
        var zero = fixture.Boss(data, first);
        Near(zero.GetComponent<BossShield>().CurrentAbsorption, 0f, "An explicit zero first-phase shield must replace initial shield.");
    }

    private static void VerifyImmunity(Fixture fixture)
    {
        var data = fixture.Data();
        data.statusImmunityList = new List<string> { "Bind", "Slow", "Airborne", "Silence", "Vulnerability",
            "DefenseReduction", "WeakPointExposed", "WaterDelayedDamage" };
        var boss = fixture.Boss(data);
        var handler = boss.gameObject.AddComponent<StatusEffectHandler>();
        int applied = 0; handler.OnEffectApplied += _ => applied++;
        IStatusEffect[] effects = { new BindEffect(), new SlowEffect(.5f, 1f), new AirborneEffect(1f),
            new SilenceEffect(1f), new VulnerabilityEffect(1f, 20f), new DefenseReductionEffect(10f, 1f),
            new WeakPointExposedEffect(1f), new WaterDelayedDamageEffect(1f, 1f, ElementType.ELEMENT_WATER, 0f, 1f) };
        foreach (var effect in effects)
            Check(!handler.TryApply(effect), "Boss immunity failed for " + effect.GetType().Name + ".");
        Check(applied == 0 && !boss.IsRoot, "Rejected effects must not invoke application callbacks.");
        var forms = boss.gameObject.AddComponent<FormTargetEffects>();
        forms.ApplyArmorShred(false); forms.ApplyArmorShred(true);
        Near(forms.ArmorReductionRatio, 0f, "Defense reduction immunity must block actual Iron-form armor shred.");
        data.statusImmunityList.Remove("Silence");
        var silence = new SilenceEffect(2f);
        Check(handler.TryApply(silence), "A nonimmune effect must apply normally.");
        data.statusImmunityList.Add("Silence");
        Check(!handler.CanApply(typeof(SilenceEffect)), "Existing silence refresh must respect immunity.");
        Check(!handler.TryApply(new SilenceEffect(10f)) && ReferenceEquals(handler.Get<SilenceEffect>(), silence),
            "Rejected reapplication must retain the existing effect instance.");
    }

    private static void Near(float actual, float expected, string message)
        => Check(Mathf.Abs(actual - expected) <= .001f, message + " Actual: " + actual + ", expected: " + expected);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static void Set(object target, Type type, string field, object value)
        => type.GetField(field, Hidden).SetValue(target, value);

    private static object Invoke(object target, Type type, string method)
        => type.GetMethod(method, Hidden).Invoke(target, null);

    private static void TickTransition(Boss boss, float seconds)
        => typeof(Boss).GetMethod("TickPhaseTransition", Hidden).Invoke(boss, new object[] { seconds });

    private sealed class Fixture : IDisposable
    {
        private readonly Scene _scene = EditorSceneManager.NewPreviewScene();
        private readonly List<Object> _assets = new List<Object>();
        private readonly CombatSettings _previousSettings = CombatSettings.Active;
        private readonly UnityEngine.Random.State _random = UnityEngine.Random.state;

        public Fixture()
        {
            var table = Asset<ElementEffectiveManager>();
            foreach (ElementType attack in Enum.GetValues(typeof(ElementType)))
                foreach (ElementType defense in Enum.GetValues(typeof(ElementType)))
                    table.elementEffectiveList.Add(new ElementEffective { AttackType = attack, DefenseType = defense,
                        multiplier = attack == ElementType.ELEMENT_FIRE ? 2f : attack == ElementType.ELEMENT_WATER ? 3f
                            : attack == ElementType.ELEMENT_EARTH ? .75f : 1f });
            table.Initialize();
            var settingsObject = Make("Boss regression combat settings"); settingsObject.SetActive(false);
            var settings = settingsObject.AddComponent<CombatSettings>();
            Set(settings, typeof(CombatSettings), "_elementTable", table);
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, settings);
        }

        public T Asset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>(); _assets.Add(asset); return asset;
        }

        public BossData Data()
        {
            var data = Asset<BossData>(); data.maxHp = 1000; data.baseAtk = 10f;
            data.elementType = ElementType.ELEMENT_WOOD; data.attackCooldown = .1f;
            return data;
        }

        public ConfiguredBoss Boss(BossData data, params BossPhaseData[] phases)
        {
            var actor = Make("Boss combat regression actor"); actor.SetActive(false);
            var boss = actor.AddComponent<ConfiguredBoss>();
            boss.EnemyStat = actor.GetComponent<EnemyStat>();
            Set(boss, typeof(Boss), "BossData", data);
            Set(boss, typeof(Boss), "PhaseDatas", new List<BossPhaseData>(phases));
            Set(boss.EnemyStat, typeof(EnemyStat), "baseData", data);
            Invoke(boss.EnemyStat, typeof(EnemyStat), "InitializeFromData");
            boss.StaggerResistLevel = StaggerResistLevel.SUPER_ARMOR;
            actor.SetActive(true);
            Invoke(boss, typeof(Boss), "CreateBrain");
            return boss;
        }

        private GameObject Make(string name)
        {
            var actor = new GameObject(name);
            SceneManager.MoveGameObjectToScene(actor, _scene);
            return actor;
        }

        public void Dispose()
        {
            if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
            for (int i = _assets.Count - 1; i >= 0; i--) if (_assets[i] != null) Object.DestroyImmediate(_assets[i]);
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, _previousSettings);
            UnityEngine.Random.state = _random;
        }
    }
}
