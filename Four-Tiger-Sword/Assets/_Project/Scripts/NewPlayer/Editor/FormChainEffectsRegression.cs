using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Isolated checks for authored chain actions, persistent buffs and confirmed-hit effects.</summary>
public static class FormChainEffectsRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    private sealed class ProbeForm : BaseForm
    {
        private readonly ElementType _element;
        public ProbeForm(WeaponActionDataSO data, ElementType element) : base(data) => _element = element;
        public override ElementType Element => _element;
        protected override void OnHitEnemy(GameObject target, WeaponActionData step = null, HitEvent hitEvent = null) { }
        public DamageResult Apply(FormChainEffectsDamageProbe target)
        {
            var result = DamageManager.Apply(EffectHit(1f, trueDamage: true), target, target.gameObject);
            if (result.Applied) OnDamageDealt(target.gameObject, result, CurrentNormalAttackStep);
            return result;
        }
        public HitInfo SnapshotHit() => EffectHit(1f, trueDamage: true);
        public HitInfo DirectHit(bool secondary = false)
            => EffectHit(1f, trueDamage: true, secondary: secondary).WithDirectPlayerHit();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Scene _scene = EditorSceneManager.NewPreviewScene();
        private readonly List<Object> _assets = new();
        private readonly PlayerUIManager _previousUi = PlayerUIManager.Instance;
        public readonly PlayerController Player;
        public readonly PlayerStatManager Stats;
        public readonly FormManager Forms;
        public Fixture()
        {
            PlayerUIManager.Instance = null;
            var host = Make("Chain effects player", false);
            Player = host.AddComponent<PlayerController>();
            Stats = host.GetComponent<PlayerStatManager>();
            var stats = Asset<PlayerStatData>();
            stats.baseHP = 1000f; stats.baseATK = 100f; stats.baseSP = 100f;
            stats.baseAtkSpd = 1f; stats.baseCRT = stats.baseElmAtk = 0f;
            Stats.Initialize(stats);
            Set(Player, nameof(PlayerController.StatManager), Stats);
            Set(Player, nameof(PlayerController.Controller), host.GetComponent<CharacterController>());
            Set(Player, nameof(PlayerController.Input), host.GetComponent<PlayerInputHandler>());
            Set(Player, nameof(PlayerController.Animator), host.AddComponent<Animator>());
            Set(Player, nameof(PlayerController.StateMachine), new StateMachine());
            Forms = new FormManager(Player);
            Set(Player, nameof(PlayerController.FormManager), Forms);
            host.SetActive(true);
        }

        public T Asset<T>() where T : ScriptableObject
        { var asset = ScriptableObject.CreateInstance<T>(); _assets.Add(asset); return asset; }

        public GameObject Make(string name, bool active = true)
        {
            var target = new GameObject(name);
            target.SetActive(false);
            SceneManager.MoveGameObjectToScene(target, _scene);
            target.transform.position = new Vector3(17400f, 900f, 17400f);
            target.SetActive(active);
            return target;
        }

        public void Register(BaseForm form)
        {
            Forms.AddTransition(form, () => false);
            form.Equip(Player);
        }

        public FormChainEffectsDamageProbe Target(string name)
        {
            var target = Make(name);
            target.AddComponent<EnemyStat>().CurrentHp = 1000f;
            return target.AddComponent<FormChainEffectsDamageProbe>();
        }

        public void Dispose()
        {
            Forms.CleanupTransientEffects();
            if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
            foreach (var asset in _assets) if (asset != null) Object.DestroyImmediate(asset);
            PlayerUIManager.Instance = _previousUi;
        }
    }

    [MenuItem("Tools/Studio Baekho/Combat/Run Form Chain Effects Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Chain effects regression requires Edit Mode.");
        _checks = 0;
        using (var fixture = new Fixture())
        {
            VerifyBuffs(fixture);
            VerifyConfirmedTargets(fixture);
            VerifyFinalComboHit(fixture);
            VerifyHitClassification(fixture);
        }
        return "PASS: " + _checks + " chain action, 20-second buff, refresh, cleanup, confirmed-target vulnerability, immunity, final-hit and direct/delayed classification checks.";
    }

    private static void VerifyHitClassification(Fixture f)
    {
        var form = new ProbeForm(f.Asset<FireFormActionDataSO>(), ElementType.ELEMENT_FIRE);
        f.Register(form);
        Check(!form.SnapshotHit().IsDirectPlayerHit && form.DirectHit().IsDirectPlayerHit
            && !form.DirectHit(true).IsDirectPlayerHit,
            "Only explicit direct contacts qualify; secondary and fallback effect hits remain indirect.");
        var direct = new HitInfo(5f, ElementType.ELEMENT_FIRE, .3f, 1.8f,
            poiseDamageMultiplier: 1.2f, armorPierce: true, poiseDamage: 0f,
            source: f.Player, trueDamage: true, category: PlayerAttackCategory.Normal,
            itemDamageMultiplier: 1.5f, sourceDamageMultiplier: 2f, hasSourceDamageSnapshot: true,
            isDirectPlayerHit: true);
        var indirect = direct.WithoutDirectPlayerHit();
        foreach (var field in typeof(HitInfo).GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (field.Name == nameof(HitInfo.IsDirectPlayerHit)) continue;
            Check(Equals(field.GetValue(direct), field.GetValue(indirect)),
                "Deferred conversion changed damage payload field " + field.Name + ".");
        }
        Check(!indirect.IsDirectPlayerHit && !new DamageResult(DamageOutcome.Applied).CausedOrdinaryStagger,
            "Deferred hits and legacy damage receivers cannot create direct stagger credit by default.");

        var actor = f.Make("Delayed classification enemy", false);
        var enemy = actor.AddComponent<Enemy>();
        enemy.EnemyStat = actor.GetComponent<EnemyStat>();
        enemy.EnemyStat.CurrentHp = 1000f;
        actor.GetComponent<PoiseHandler>().ResetPoise();
        var brain = new NormalEnemyBrain(enemy);
        Set(enemy, nameof(Enemy.Brain), brain);
        actor.SetActive(true);
        var first = DamageManager.Apply(direct, enemy, actor);
        var count = typeof(NormalEnemyBrain).GetField("_directStaggerCount", Hidden);
        Check(first.CausedOrdinaryStagger && (int)count.GetValue(brain) == 1,
            "The direct contact did not establish one ordinary stagger credit.");
        var runner = f.Player.GetComponent<FormEffectRunner>() ?? f.Player.gameObject.AddComponent<FormEffectRunner>();
        typeof(FormEffectRunner).GetField("_owner", Hidden).SetValue(runner, f.Player);
        float hp = enemy.EnemyStat.CurrentHp;
        var delayed = runner.DelayedHits(enemy, direct, null);
        while (delayed.MoveNext()) { }
        Near(hp - enemy.EnemyStat.CurrentHp, first.HealthDamage * 5f,
            "Deferred conversion changed the five delayed damage ticks.");
        Check((int)count.GetValue(brain) == 1,
            "Delayed hits reused their player's direct-contact flag and advanced the stagger chain.");
        DamageManager.Apply(new HitInfo(1f, ElementType.ELEMENT_NONE, 0f, 1f, source: f.Player,
            trueDamage: true, category: PlayerAttackCategory.Normal), enemy, actor);
        Check((int)count.GetValue(brain) == 1,
            "A retained player source or attack category inferred direct-contact credit.");
    }

    private static void VerifyBuffs(Fixture f)
    {
        var fireData = f.Asset<FireFormActionDataSO>();
        var waterData = f.Asset<WaterFormActionDataSO>();
        var woodData = f.Asset<WoodFormActionDataSO>();
        var fire = new FireForm(fireData);
        var water = new WaterForm(waterData);
        f.Register(fire); f.Register(water);
        Check(fire.ChainSettings.Enabled && fire.ChainSettings.Trigger == FormChainTrigger.OtherFormFinalHit,
            "Fire receives its conditional chain defaults.");
        Check(water.ChainSettings.Enabled && water.ChainSettings.Trigger == FormChainTrigger.SuccessfulDodge,
            "Water receives its conditional chain defaults.");
        Check(!woodData.ChainSettings.Enabled && woodData.ChainSettings.Trigger == FormChainTrigger.Disabled,
            "Deferred forms keep disabled placeholders.");
        fireData.SkillSteps = new List<WeaponActionData> { Step() };
        Check(!fire.CanSwitchAttack && !water.CanSwitchAttack, "Empty switch slots never fall back to E.");
        fire.BeginSwitchAttack();
        Near(f.Stats.GetStat(StatType.ST_ATK), 100f, "Empty switch does not grant a buff.");
        fireData.SwitchAttackSteps.Add(Step()); waterData.SwitchAttackSteps.Add(Step());
        f.Forms.ChangeForm(fire);
        fire.BeginSwitchAttack();
        Near(f.Stats.GetStat(StatType.ST_ATK), 130f, "Fire chain grants attack +30%.");
        Near(fire.ChainBuffRemaining, 20f, "Fire chain begins at 20 seconds.");
        Near(Hit(fire).BaseDamage, 130f, "Chain attack snapshots its own newly granted buff.");
        Check(!fire.IsSkillBuffActive && fire.SkillCooldownRemaining == 0f, "Chain leaves E buff and cooldown independent.");
        fire.EndSwitchAttack();
        Tick(fire, 7f);
        fire.BeginSwitchAttack(); fire.EndSwitchAttack();
        Near(fire.ChainBuffRemaining, 20f, "Retrigger refreshes the full duration.");
        Near(f.Stats.GetStat(StatType.ST_ATK), 130f, "Retrigger never stacks attack bonuses.");
        f.Forms.ChangeForm(water);
        Near(f.Stats.GetStat(StatType.ST_ATK), 130f, "Fire attack bonus persists in another form.");
        water.BeginSwitchAttack(); water.EndSwitchAttack();
        Near(f.Stats.GetStat(StatType.ST_ATK_SPD), 1.25f, "Water chain grants attack speed +25%.");
        Near(water.ChainBuffRemaining, 20f, "Water chain begins at 20 seconds.");
        Check(!water.IsSkillBuffActive, "Water chain does not grant its E buff.");
        Tick(fire, 19.99f);
        Near(f.Stats.GetStat(StatType.ST_ATK), 130f, "Inactive-form bonus persists until expiry.");
        Tick(fire, .02f);
        Near(f.Stats.GetStat(StatType.ST_ATK), 100f, "Inactive-form bonus expires at 20 seconds.");
        water.ChainSettings.AttackSpeedBonusPercent = 90f;
        water.CleanupTransientEffects();
        Near(f.Stats.GetStat(StatType.ST_ATK_SPD), 1f, "Cleanup removes the originally applied value after data edits.");
        water.ChainSettings.AttackSpeedBonusPercent = 25f;
        fire.BeginSwitchAttack();
        f.Stats.TakeDamage(f.Stats.CurrentHp, ElementType.ELEMENT_NONE, false);
        Tick(fire, 0f);
        Near(f.Stats.GetStat(StatType.ST_ATK), 100f, "Death clears the chain modifier immediately.");
        typeof(PlayerStatManager).GetField("_currentHp", Hidden).SetValue(f.Stats, 1000f);
        fire.BeginSwitchAttack();
        f.Player.enabled = false;
        Tick(fire, 0f);
        Near(f.Stats.GetStat(StatType.ST_ATK), 100f, "Disable clears the chain modifier.");
        f.Player.enabled = true;
        fire.EndSwitchAttack();
    }

    private static void VerifyConfirmedTargets(Fixture f)
    {
        var data = f.Asset<WaterFormActionDataSO>(); data.SwitchAttackSteps.Add(Step());
        var water = new ProbeForm(data, ElementType.ELEMENT_WATER);
        f.Register(water); f.Forms.ChangeForm(water);
        var target = f.Target("Confirmed chain target");
        var rejected = f.Target("Rejected chain target"); rejected.Reject = true;
        var untouched = f.Target("Untouched chain target");
        water.BeginSwitchAttack();
        Check(water.Apply(target).Applied, "The test uses an applied production damage result.");
        water.Apply(rejected);
        var effects = target.GetComponent<StatusEffectHandler>();
        var vulnerability = effects?.Get<WaterChainVulnerabilityEffect>();
        Check(vulnerability != null, "Actual water chain target receives vulnerability.");
        Near(vulnerability.Remaining, 20f, "Vulnerability lasts 20 seconds from the hit.");
        Check(rejected.GetComponent<StatusEffectHandler>() == null && untouched.GetComponent<StatusEffectHandler>() == null,
            "Rejected and untouched targets receive no vulnerability.");
        vulnerability.OnUpdate(4f);
        water.Apply(target);
        Near(effects.Get<WaterChainVulnerabilityEffect>().Remaining, 20f, "Repeated hits refresh one water debuff.");
        effects.Apply(new VulnerabilityEffect(6f, 20f));
        Check(effects.Has<VulnerabilityEffect>() && effects.Has<WaterChainVulnerabilityEffect>(),
            "Wood and Water vulnerabilities coexist.");
        Near(HitTarget(target), 138f, "Wood and Water apply independent damage multipliers exactly once.");
        effects.Get<WaterChainVulnerabilityEffect>().OnUpdate(20f);
        Near(HitTarget(target), 120f, "Expired Water vulnerability no longer increases damage.");
        water.EndSwitchAttack(); water.CleanupTransientEffects();
        var bossData = f.Asset<BossData>();
        bossData.statusImmunityList.Add("Vulnerability");
        Check(bossData.IsImmuneToStatus(typeof(WaterChainVulnerabilityEffect)),
            "Water vulnerability honors the existing boss Vulnerability immunity.");
        var bossObject = f.Make("Vulnerability immune boss", false);
        var boss = bossObject.AddComponent<ConfiguredBoss>();
        typeof(Boss).GetField("BossData", Hidden).SetValue(boss, bossData);
        bossObject.SetActive(true);
        Check(!bossObject.AddComponent<StatusEffectHandler>().TryApply(new WaterChainVulnerabilityEffect(20f, 15f)),
            "StatusEffectHandler rejects Water vulnerability on an immune boss.");
    }

    private static void VerifyFinalComboHit(Fixture f)
    {
        var sourceData = f.Asset<WaterFormActionDataSO>();
        sourceData.ComboSteps = new List<WeaponActionData> { Step(), Step(), Step() };
        var source = new ProbeForm(sourceData, ElementType.ELEMENT_WATER);
        var fireData = f.Asset<FireFormActionDataSO>(); fireData.SwitchAttackSteps.Add(Step());
        var fire = new FireForm(fireData);
        f.Register(source); f.Register(fire); f.Forms.ChangeForm(source);
        var rejected = f.Target("Rejected combo target"); rejected.Reject = true;
        var target = f.Target("Confirmed combo target");
        source.BeginAttack(); source.Apply(target);
        Near(f.Forms.GetChainReadyRemaining(ElementType.ELEMENT_FIRE), 0f, "Non-final stages do not prepare Fire.");
        Field(source, "_comboStep", 2);
        source.Apply(rejected);
        Near(f.Forms.GetChainReadyRemaining(ElementType.ELEMENT_FIRE), 0f, "Rejected final hit does not prepare Fire.");
        source.Apply(target);
        Near(f.Forms.GetChainReadyRemaining(ElementType.ELEMENT_FIRE), 5f, "Confirmed final stage prepares Fire.");
        f.Forms.UpdateChainReadiness(1f);
        source.Apply(target);
        Near(f.Forms.GetChainReadyRemaining(ElementType.ELEMENT_FIRE), 4f, "Repeated targets/hits in one stage do not reprepare Fire.");
        Check(source.BeginFinisher(Vector3.forward, false), "A normal terminal finisher begins.");
        source.Apply(target);
        Near(f.Forms.GetChainReadyRemaining(ElementType.ELEMENT_FIRE), 5f, "Confirmed finisher hit prepares Fire.");
        source.EndAttack();
    }

    private static WeaponActionData Step() => new WeaponActionData { Duration = 1f, Damage = 1f };
    private static void Tick(BaseForm form, float seconds)
        => typeof(BaseForm).GetMethod("UpdateChainBuff", Hidden).Invoke(form, new object[] { seconds });
    private static HitInfo Hit(BaseForm form)
        => (HitInfo)typeof(BaseForm).GetMethod("EffectHit", Hidden).Invoke(form,
            new object[] { 1f, 0f, true, null, null, false, Vector3.zero });
    private static float HitTarget(FormChainEffectsDamageProbe target)
        => DamageManager.Apply(new HitInfo(100f, ElementType.ELEMENT_NONE, 0f, 1f, trueDamage: true),
            target, target.gameObject).HealthDamage;
    private static void Set(object target, string property, object value)
        => target.GetType().GetProperty(property).SetValue(target, value);
    private static void Field(BaseForm target, string field, object value)
        => typeof(BaseForm).GetField(field, Hidden).SetValue(target, value);
    private static void Check(bool valid, string message)
    { if (!valid) throw new InvalidOperationException("Form chain effects: " + message); _checks++; }
    private static void Near(float actual, float expected, string message)
        => Check(Mathf.Abs(actual - expected) < .001f, message + $" Expected {expected}, got {actual}.");
}

public sealed class FormChainEffectsDamageProbe : MonoBehaviour, IDamageable
{
    public bool Reject;
    public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE,
        bool isCritical = false, Vector3 power = default, float poiseDamage = 20f,
        StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
    {
        if (Reject) return default;
        var stats = GetComponent<EnemyStat>();
        float applied = Mathf.Min(stats.CurrentHp, damage);
        stats.CurrentHp -= applied;
        return new DamageResult(DamageOutcome.Applied, applied, killed: stats.IsDead);
    }
}
