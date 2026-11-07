using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Isolated combat checks. Run() is Edit Mode safe; RunPlayMode() adds real overlap queries.</summary>
public static class BaekhoItemCombatRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 Origin = new Vector3(18200f, 500f, 18200f);

    private sealed class ProbeForm : BaseForm
    {
        public ProbeForm(WeaponActionDataSO data) : base(data) { }
        public override ElementType Element => ElementType.ELEMENT_WOOD;
        public float Outgoing = 2f;
        public override float OutgoingDamageMultiplier => Outgoing;
        public void CaptureNormal() { _currentAction = ActionType.Attack; SnapshotAttackDamage(); }
        public HitInfo Hit(bool secondary = false) => EffectHit(1f, secondary: secondary);
    }

    private sealed class DamageProbe : IDamageable
    {
        public float Damage;
        public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE,
            bool isCritical = false, Vector3 power = default, float poiseDamage = 20f,
            StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
        { Damage = damage; return new DamageResult(DamageOutcome.Applied, damage); }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly Scene Scene;
        public readonly bool Playing;
        public readonly List<Object> Objects = new List<Object>();
        public readonly PlayerController Player;
        public readonly PlayerStatManager Stats;
        public readonly PlayerItemRuntime Items;
        private readonly UnityEngine.Random.State _random;
        private readonly CombatSettings _previousSettings;

        public Fixture(bool playing)
        {
            Playing = playing;
            _random = UnityEngine.Random.state;
            _previousSettings = CombatSettings.Active;
            Scene = playing ? SceneManager.CreateScene("Baekho item combat regression") : EditorSceneManager.NewPreviewScene();
            var go = Make("Inactive item combat player", false);
            Player = go.AddComponent<PlayerController>();
            Stats = go.GetComponent<PlayerStatManager>();
            var data = Asset<PlayerStatData>();
            data.baseATK = 10f;
            data.baseCRT = 0f;
            data.baseElmAtk = 0f;
            Stats.Initialize(data);
            Set(Player, nameof(PlayerController.StatManager), Stats);
            Set(Player, nameof(PlayerController.Controller), go.GetComponent<CharacterController>());
            Set(Player, nameof(PlayerController.Input), go.GetComponent<PlayerInputHandler>());
            Set(Player, nameof(PlayerController.Animator), go.AddComponent<Animator>());
            Set(Player, nameof(PlayerController.StateMachine), new StateMachine());
            Set(Player, nameof(PlayerController.FormManager), new FormManager(Player));
            Items = go.AddComponent<PlayerItemRuntime>();
            Items.Initialize(Stats, () => ElementType.ELEMENT_WOOD);
            Set(Player, nameof(PlayerController.ItemRuntime), Items);
            var catalog = Asset<ItemCatalog>();
            catalog.projectId = Guid.NewGuid().ToString();
            var definition = Asset<ItemDefinition>();
            definition.projectId = catalog.projectId;
            definition.candidateKey = Guid.NewGuid().ToString();
            definition.itemId = "COMBAT_REGRESSION";
            definition.effects = new[] {
                new ItemEffect { stat = ItemStat.NormalDamage, amount = .5f },
                new ItemEffect { stat = ItemStat.SwitchDamage, amount = .25f }
            };
            catalog.items = new[] { definition };
            Items.Catalog = catalog;
            Require(Items.TryAddItem(definition.itemId), "Cannot grant isolated damage item.");
        }

        public T Asset<T>() where T : ScriptableObject
        { var asset = ScriptableObject.CreateInstance<T>(); Objects.Add(asset); return asset; }

        public GameObject Make(string name, bool active = true)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            SceneManager.MoveGameObjectToScene(go, Scene);
            go.transform.position = Origin;
            Objects.Add(go);
            go.SetActive(active);
            return go;
        }

        public T Data<T>() where T : WeaponActionDataSO
        {
            var data = Asset<T>();
            data.SkillSteps = new List<WeaponActionData> { Step() };
            data.SwitchAttackSteps = new List<WeaponActionData> { Step() };
            data.ComboSteps = new List<WeaponActionData> { Step() };
            // These checks isolate item/E behavior; dedicated chain-effect checks cover enabled buffs.
            data.ChainSettings.Enabled = false;
            return data;
        }

        public void Bind(BaseForm form)
        {
            typeof(BaseForm).GetField("_playerController", Hidden).SetValue(form, Player);
            typeof(BaseForm).GetField("_enemyLayer", Hidden).SetValue(form, (LayerMask)LayerMask.GetMask("Enemy"));
        }

        public void InstallNeutralTable()
        {
            var table = Asset<ElementEffectiveManager>();
            foreach (ElementType attack in Enum.GetValues(typeof(ElementType)))
                foreach (ElementType defense in Enum.GetValues(typeof(ElementType)))
                    table.elementEffectiveList.Add(new ElementEffective { AttackType = attack, DefenseType = defense, multiplier = 1f });
            table.Initialize();
            var settings = Make("Inactive regression combat settings", false).AddComponent<CombatSettings>();
            typeof(CombatSettings).GetField("_elementTable", Hidden).SetValue(settings, table);
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, settings);
        }

        public void Dispose()
        {
            for (int i = Objects.Count - 1; i >= 0; i--)
                if (Objects[i] != null) Object.DestroyImmediate(Objects[i]);
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, _previousSettings);
            if (Playing) SceneManager.UnloadSceneAsync(Scene);
            else EditorSceneManager.ClosePreviewScene(Scene);
            UnityEngine.Random.state = _random;
        }
    }

    public static string Run()
    {
        Require(!Application.isPlaying, "Run requires Edit Mode; use RunPlayMode while playing.");
        using (var fixture = new Fixture(false)) Verify(fixture);
        return "PASS: damage category exclusions, one item multiplier, origin snapshots, five switch lifecycles, independent E costs/cooldowns/buffs, five R resource paths and Water collision cleanup.";
    }

    public static string RunPlayMode()
    {
        Require(Application.isPlaying, "RunPlayMode requires Play Mode.");
        using (var fixture = new Fixture(true))
        {
            Verify(fixture);
            VerifyGeometry(fixture);
        }
        return "PASS: Edit Mode combat checks plus independent Fire/Iron/Water switch shapes, preserved Earth/Wood E geometry and Water E swept hits.";
    }

    private static void Verify(Fixture fixture)
    {
        var probe = new DamageProbe();
        var target = fixture.Make("Item multiplier target");
        DamageManager.Apply(new HitInfo(10f, ElementType.ELEMENT_WOOD, 0f, 1f, trueDamage: true,
            category: PlayerAttackCategory.Normal, itemDamageMultiplier: 1.5f,
            sourceDamageMultiplier: 2f, hasSourceDamageSnapshot: true), probe, target);
        Near(probe.Damage, 30f, "Normal item multiplier must be applied exactly once.");
        foreach (var category in new[] { PlayerAttackCategory.Skill, PlayerAttackCategory.Ultimate,
            PlayerAttackCategory.Counter, PlayerAttackCategory.Heavy, PlayerAttackCategory.Secondary })
        {
            DamageManager.Apply(new HitInfo(10f, ElementType.ELEMENT_NONE, 0f, 1f, trueDamage: true,
                category: category, itemDamageMultiplier: 5f), probe, target);
            Near(probe.Damage, 10f, category + " incorrectly inherited an item attack multiplier.");
        }
        DamageManager.Apply(new HitInfo(10f, ElementType.ELEMENT_NONE, 0f, 1f, trueDamage: true,
            category: PlayerAttackCategory.SwitchAttack, itemDamageMultiplier: -5f), probe, target);
        Near(probe.Damage, 0f, "Negative bonuses must never heal a damage receiver.");

        var data = fixture.Data<WoodFormActionDataSO>();
        var form = new ProbeForm(data);
        fixture.Bind(form);
        form.CaptureNormal();
        var normal = form.Hit();
        Near(normal.ItemDamageMultiplier, 1.5f, "Normal damage item was not captured.");
        Near(normal.BaseDamage, 10f, "Normal ATK snapshot missing.");
        fixture.Items.TryRemoveItem("COMBAT_REGRESSION");
        fixture.Stats.AddModifier(StatType.ST_ATK, 10f, 0f);
        form.Outgoing = 4f;
        var stillNormal = form.Hit();
        Near(stillNormal.BaseDamage, normal.BaseDamage, "In-flight normal damage changed with stats.");
        Near(stillNormal.ItemDamageMultiplier, normal.ItemDamageMultiplier, "In-flight normal damage changed with ownership.");
        Near(stillNormal.SourceDamageMultiplier, 2f, "Origin outgoing snapshot changed after the attack began.");
        Require(normal.HasSourceDamageSnapshot && normal.Category == PlayerAttackCategory.Normal,
            "Normal hit lost its damage category/source metadata.");
        var secondary = form.Hit(true);
        Require(secondary.Category == PlayerAttackCategory.Secondary, "Secondary damage lost its category.");
        Near(secondary.ItemDamageMultiplier, 1f, "Passive explosion inherited the normal attack item bonus.");
        fixture.Items.TryAddItem("COMBAT_REGRESSION");
        form.BeginSwitchAttack();
        var switched = form.Hit();
        Require(switched.Category == PlayerAttackCategory.SwitchAttack, "Switch attack was categorized as E skill.");
        Near(switched.ItemDamageMultiplier, 1.25f, "Switch damage must use its own item stat.");
        form.EndSwitchAttack();

        var fireData = fixture.Data<FireFormActionDataSO>();
        var waterData = fixture.Data<WaterFormActionDataSO>();
        var earthData = fixture.Data<EarthFormActionDataSO>();
        var ironData = fixture.Data<IronFormActionDataSO>();
        var fire = new FireForm(fireData);
        var water = new WaterForm(waterData);
        var earth = new EarthForm(earthData);
        foreach (BaseForm candidate in new BaseForm[] { fire, water, new WoodForm(data), earth, new IronForm(ironData) })
        {
            fixture.Bind(candidate);
            typeof(BaseForm).GetField("_skillCooldownTimer", Hidden).SetValue(candidate, 2f);
            float sp = fixture.Stats.CurrentSp;
            float attack = fixture.Stats.GetStat(StatType.ST_ATK);
            LayerMask mask = fixture.Player.Controller.excludeLayers;
            Require(candidate.CanSwitchAttack, candidate.GetType().Name + " unexpectedly requires E readiness.");
            candidate.BeginSwitchAttack();
            Near(fixture.Stats.CurrentSp, sp, "Switch attack spent E SP.");
            Near(fixture.Stats.GetStat(StatType.ST_ATK), attack, "Switch attack applied an E attack buff.");
            Near(candidate.SkillCooldownRemaining, 2f, "Switch attack changed the independent E cooldown.");
            Require(!fire.IsSkillBuffActive && !water.IsSkillBuffActive && earth.SkillShield == 0f,
                "Switch attack granted an E-only buff or shield.");
            candidate.EndSwitchAttack();
            Near(candidate.SkillCooldownRemaining, 2f, "Switch attack exit started an E cooldown.");
            Require(fixture.Player.Controller.excludeLayers == mask, "Water switch attack leaked collision exclusion.");
        }
        VerifyPreservedSkillsAndUltimates(fixture, new BaseForm[] {
            fire, water, new WoodForm(data), earth, new IronForm(ironData)
        }, new[] { 20f, 15f, 18f, 15f, 20f });
        data.SkillSteps[0].Duration = float.NaN;
        Require(form.CanSwitchAttack, "Malformed E data disabled the independently authored switch attack.");
        data.SwitchAttackSteps[0].Duration = float.NaN;
        Require(!form.CanSwitchAttack, "Malformed duration was accepted as a switch attack.");
    }

    private static void VerifyPreservedSkillsAndUltimates(Fixture fixture, BaseForm[] forms, float[] expectedCosts)
    {
        for (int i = 0; i < forms.Length; i++)
        {
            BaseForm form = forms[i];
            fixture.Bind(form);
            string name = form.GetType().Name;
            typeof(BaseForm).GetField("_skillCooldownTimer", Hidden).SetValue(form, 0f);
            var data = (WeaponActionDataSO)typeof(BaseForm).GetField("_weaponActionData", Hidden).GetValue(form);
            data.UltimateSteps = new List<WeaponActionData> { Step() };
            fixture.Stats.AddSp(fixture.Stats.MaxSp);
            float beforeSp = fixture.Stats.CurrentSp;
            float beforeAttack = fixture.Stats.GetStat(StatType.ST_ATK);
            float beforeWaterGauge = form is WaterForm beforeWater ? beforeWater.WaterGauge : 0f;
            LayerMask beforeMask = fixture.Player.Controller.excludeLayers;
            Near(form.SkillSpCost, expectedCosts[i], name + " E cost is not on the 100-SP scale.");
            Require(form.CanSkill, name + " cannot use E with full SP and ready cooldown.");
            form.BeginSkill();
            Near(fixture.Stats.CurrentSp, beforeSp - expectedCosts[i], name + " E did not consume exactly its own cost.");
            HitInfo eHit = Hit(form);
            Require(eHit.Category == PlayerAttackCategory.Skill, name + " E was classified as a switch attack.");
            Near(eHit.ItemDamageMultiplier, 1f, name + " E inherited an item normal/switch bonus.");
            if (form is FireForm fire)
                Require(fire.IsSkillBuffActive && fixture.Stats.GetStat(StatType.ST_ATK) > beforeAttack,
                    "Fire E lost its attack buff.");
            if (form is WaterForm water)
                Require(water.IsSkillBuffActive && fixture.Stats.GetStat(StatType.ST_ATK) > beforeAttack
                    && water.WaterGauge > beforeWaterGauge, "Water E lost its buff or skill-use gauge gain.");
            if (form is EarthForm earth)
                Require(earth.SkillShield > 0f, "Earth E lost its shield.");
            form.EndSkill();
            Near(form.SkillCooldownRemaining, form.SkillCooldown, name + " E exit did not start its own cooldown.");
            Require(!form.CanSkill, name + " E ignored its just-started cooldown.");
            Require(fixture.Player.Controller.excludeLayers == beforeMask, name + " E leaked its collision override.");
            form.CleanupTransientEffects();
            Near(fixture.Stats.GetStat(StatType.ST_ATK), beforeAttack, name + " E cleanup left an attack modifier.");

            fixture.Stats.AddUltimateGauge(fixture.Stats.MaxUltimateGauge);
            Require(form.CanUltimate, name + " R did not become available at full ultimate gauge.");
            float spBeforeR = fixture.Stats.CurrentSp;
            form.BeginUltimate();
            Near(fixture.Stats.CurrentUltimateGauge, 0f, name + " R did not consume its ultimate gauge.");
            Near(fixture.Stats.CurrentSp, spBeforeR, name + " R incorrectly spent SP.");
            Near(form.UltimateCooldownRemaining, form.UltimateCooldownDuration, name + " R lost its cooldown.");
            HitInfo rHit = Hit(form);
            Require(rHit.Category == PlayerAttackCategory.Ultimate, name + " R lost its attack category.");
            Near(rHit.ItemDamageMultiplier, 1f, name + " R inherited an item normal/switch bonus.");
            if (form is FireForm ultimateFire)
                Require(ultimateFire.IsUltimateActive, "Fire R lost its toggle stance.");
            if (form is EarthForm ultimateEarth)
                Require(ultimateEarth.UltimateShield > 0f, "Earth R lost its shield.");
            if (form is WoodForm ultimateWood)
                Require(ultimateWood.ElementBuffRemaining > 0f && ultimateWood.UltimateFieldRemaining > 0f,
                    "Wood R lost its elemental buff or field.");
            if (form is WaterForm ultimateWater)
                Require(ultimateWater.BlocksIncomingDamage(1f, Vector3.zero), "Water R lost its action protection.");
            form.EndUltimate();
            form.CleanupTransientEffects();
            Require(!form.CanUltimate, name + " R became immediately ready after resource consumption.");
        }
    }

    private static void VerifyGeometry(Fixture fixture)
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        Require(enemyLayer >= 0, "Enemy layer is required for geometry regressions.");
        fixture.InstallNeutralTable();
        var target = fixture.Make("Geometry regression target");
        target.layer = enemyLayer;
        var collider = target.AddComponent<SphereCollider>(); collider.radius = .1f;
        var receiver = target.AddComponent<BaekhoItemCombatDamageProbe>();
        var fireData = fixture.Data<FireFormActionDataSO>(); fireData.FireSkillRadius = 3f;
        var fire = new FireForm(fireData); fixture.Bind(fire); fire.BeginSwitchAttack();
        target.transform.position = Origin + Vector3.right * 2f;
        Physics.SyncTransforms();
        Require(Query(fire, fireData.SwitchAttackSteps[0], Origin) == 0, "Fire switch inherited E's radius override.");
        fireData.SwitchAttackSteps[0].HitBoxRadius = 2.5f;
        Require(Query(fire, fireData.SwitchAttackSteps[0], Origin) > 0, "Fire switch ignored its own authored radius.");
        fire.EndSwitchAttack();
        fire.BeginSkill();
        Require(Query(fire, fireData.SkillSteps[0], Origin) > 0, "Fire E lost its radius override.");
        fire.EndSkill();

        var ironData = fixture.Data<IronFormActionDataSO>();
        ironData.SwitchAttackSteps[0].HitBoxShape = HitBoxShape.Box;
        ironData.SwitchAttackSteps[0].HitBoxSize = new Vector3(.2f, .5f, 3f);
        var iron = new IronForm(ironData); fixture.Bind(iron); iron.BeginSwitchAttack();
        target.transform.position = Origin + Vector3.forward * 2f; Physics.SyncTransforms();
        Require(Query(iron, ironData.SwitchAttackSteps[0], Origin) > 0, "Iron switch ignored its authored box shape.");
        iron.EndSwitchAttack();

        var earthData = fixture.Data<EarthFormActionDataSO>(); earthData.SkillDashHitRadius = .25f;
        var earth = new EarthForm(earthData); fixture.Bind(earth); earth.BeginSkill();
        typeof(EarthForm).GetField("_sweepStart", Hidden).SetValue(earth, Origin - Vector3.right * 3f);
        typeof(EarthForm).GetField("_sweepEnd", Hidden).SetValue(earth, Origin + Vector3.right * 3f);
        target.transform.position = Origin + Vector3.right; Physics.SyncTransforms();
        Require(Query(earth, earthData.SkillSteps[0], Origin) > 0, "Earth E lost its swept dash hitbox.");
        earth.EndSkill();

        var woodData = fixture.Data<WoodFormActionDataSO>();
        woodData.SkillRadius = .5f; woodData.SkillCenterOffset = Vector3.forward * 2f;
        var wood = new WoodForm(woodData); fixture.Bind(wood); wood.BeginSkill();
        target.transform.position = Origin + woodData.SkillCenterOffset; Physics.SyncTransforms();
        receiver.Calls = 0;
        typeof(WoodForm).GetMethod("ApplyGather", Hidden).Invoke(wood, null);
        Require(receiver.Calls == 1, "Wood E lost its configured area center/radius.");
        var effects = target.GetComponent<WoodTargetEffects>();
        Require(effects != null && effects.Stacks == woodData.MaximumStacks,
            "Wood E lost its immediate maximum-stack effect.");
        Require((float)typeof(WoodTargetEffects).GetField("_pullRemaining", Hidden).GetValue(effects) > 0f,
            "Wood E lost its pull while the switch attack was separated.");
        wood.EndSkill();

        var controllerObject = fixture.Make("Water sweep controller");
        var controller = controllerObject.AddComponent<CharacterController>();
        Set(fixture.Player, nameof(PlayerController.Controller), controller);
        var waterData = fixture.Data<WaterFormActionDataSO>();
        waterData.SkillSteps[0].HitBoxRadius = .2f;
        var water = new WaterForm(waterData); fixture.Bind(water); water.BeginSkill();
        Vector3 center = controller.bounds.center;
        typeof(WaterForm).GetField("_trailStart", Hidden).SetValue(water, center - Vector3.right * 2f);
        target.transform.position = center - Vector3.right; Physics.SyncTransforms();
        receiver.Calls = 0;
        water.UpdateSkill(out _);
        Require(receiver.Calls == 1, "Water E lost its between-frame swept hit.");
        water.EndSkill();
        Require((controller.excludeLayers & LayerMask.GetMask("Enemy")) == 0, "Water sweep collision mask did not restore.");
        water.BeginSwitchAttack();
        Require((controller.excludeLayers & LayerMask.GetMask("Enemy")) == 0,
            "Water switch inherited E's collision exclusion.");
        Require(Query(water, waterData.SwitchAttackSteps[0], Origin) == 0,
            "Water switch inherited the previous E swept path.");
        water.EndSwitchAttack();
    }

    private static WeaponActionData Step() => new WeaponActionData
    {
        AnimationName = "", Duration = 1f, Damage = 1f,
        HitStartTime = 0f, HitDuration = .5f, HitBoxShape = HitBoxShape.Sphere, HitBoxRadius = .1f
    };
    private static int Query(BaseForm form, WeaponActionData step, Vector3 center)
        => (int)form.GetType().GetMethod("QueryOverlap", Hidden).Invoke(form, new object[] { step, center });
    private static HitInfo Hit(BaseForm form)
        => (HitInfo)typeof(BaseForm).GetMethod("EffectHit", Hidden).Invoke(form,
            new object[] { 1f, 0f, false, null, null, false, Vector3.zero });
    private static void Set(object target, string property, object value)
        => target.GetType().GetProperty(property).SetValue(target, value);
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException("Baekho item combat: " + message); }
    private static void Near(float actual, float expected, string message)
        => Require(Mathf.Abs(actual - expected) < .0001f, message + " Expected " + expected + ", got " + actual);
}

public sealed class BaekhoItemCombatDamageProbe : MonoBehaviour, IDamageable
{
    public int Calls;
    public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE,
        bool isCritical = false, Vector3 power = default, float poiseDamage = 20f,
        StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
    { Calls++; return new DamageResult(DamageOutcome.Applied, damage); }
}
