using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Disposable fixtures verifying form holding has no endurance penalty and preserves combat calculations.</summary>
public static class FormSpRegression
{
    private sealed class ProbeForm : BaseForm
    {
        private readonly ElementType _element;
        public ProbeForm(WeaponActionDataSO data, ElementType element = ElementType.ELEMENT_NONE) : base(data)
            => _element = element;
        public override ElementType Element => _element;
        public float EffectiveAttack => LiveAttackStat;
        public float EffectiveSpeed => AttackSpeed;
        public void SnapshotNormal() { _currentAction = ActionType.Attack; SnapshotAttackDamage(); }
        public void UseSkill() => _currentAction = ActionType.Skill;
        public HitInfo Hit(float multiplier = 1f, float extra = 0f, bool secondary = false)
            => EffectHit(multiplier, extra, secondary: secondary);
    }

    [MenuItem("Tools/Studio Baekho/Combat/Run Form SP Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Form SP regression requires Edit Mode.");
        int checks = 0;
        void Check(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException("Form SP: " + message);
            checks++;
        }
        void Near(float actual, float expected, string message, float tolerance = .0001f)
            => Check(Mathf.Abs(actual - expected) <= tolerance,
                message + $" (actual {actual}, expected {expected})");

        var assets = new List<Object>();
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject host = null;
        FireFormActionDataSO Data()
        {
            var data = ScriptableObject.CreateInstance<FireFormActionDataSO>();
            assets.Add(data);
            return data;
        }
        try
        {
            var data = Data();
            var form = new ProbeForm(data);
            Near(form.CurrentFormSp, 100f, "A new form starts full.");
            Near(form.MaxFormSp, 100f, "Default form maximum.");
            Check(!form.IsFormSpExhausted, "A new form cannot start exhausted.");
            int notifications = 0;
            form.OnFormSpChanged += changed =>
            {
                Check(ReferenceEquals(changed, form), "Notification identifies the owning form.");
                Near(changed.CurrentFormSp, changed.MaxFormSp, "Notification observes a full form resource.");
                Check(!changed.IsFormSpExhausted, "Notification observes cleared exhaustion.");
                notifications++;
            };
            form.UpdateFormSp(60f, true, false);
            form.UpdateFormSp(60f, false, true);
            form.UpdateFormSp(0f, true, true);
            form.UpdateFormSp(-1f, true, true);
            form.UpdateFormSp(float.NaN, true, true);
            form.UpdateFormSp(float.PositiveInfinity, false, false);
            Near(form.CurrentFormSp, 100f, "Active, inactive and invalid ticks preserve full SP.");
            Check(notifications == 0, "Unchanged state does not notify.");
            foreach (bool active in new[] { false, true })
                foreach (bool combat in new[] { false, true })
                {
                    form.UpdateFormSp(1000f, active, combat);
                    Near(form.CurrentFormSp, 100f, "Holding any form for a long time never drains SP.");
                    Check(!form.IsFormSpExhausted, "Combat and selection cannot cause exhaustion.");
                }
            Check(notifications == 0, "Long unchanged ticks do not notify.");

            LegacyExhaustion(form);
            form.UpdateFormSp(0f, true, true);
            form.UpdateFormSp(float.NaN, true, true);
            Near(form.CurrentFormSp, 0f, "Invalid ticks do not modify a legacy state.");
            Check(form.IsFormSpExhausted && notifications == 0, "Invalid ticks neither clear state nor notify.");
            form.UpdateFormSp(1f / 60f, true, true);
            Near(form.CurrentFormSp, 100f, "A valid tick immediately restores legacy depleted SP.");
            Check(!form.IsFormSpExhausted, "A valid tick immediately clears legacy exhaustion.");
            Check(notifications == 1, "A legacy state correction emits one notification.");
            form.UpdateFormSp(1000f, true, true);
            Check(notifications == 1, "A restored form does not repeatedly notify.");

            var frameForm = new ProbeForm(data);
            for (int i = 0; i < 3600; i++) frameForm.UpdateFormSp(1f / 60f, true, true);
            Near(frameForm.CurrentFormSp, 100f, "A minute of combat frame ticks preserves full SP.");
            Check(!frameForm.IsFormSpExhausted, "Frame-sized combat ticks cannot exhaust a form.");
            for (int i = 0; i < 240; i++) frameForm.UpdateFormSp(1f / 60f, false, false);
            Near(frameForm.CurrentFormSp, 100f, "Inactive frame ticks also preserve full SP.");
            Check(!frameForm.IsFormSpExhausted, "Inactive ticks do not introduce exhaustion.");

            var customData = Data();
            customData.CommonBalance.FormSp.MaxSp = 200f;
            customData.CommonBalance.FormSp.DepletionSeconds = 10f;
            customData.CommonBalance.FormSp.RecoverySeconds = 8f;
            customData.CommonBalance.FormSp.ExhaustionRecoveryRatio = .5f;
            var custom = new ProbeForm(customData);
            Near(custom.CurrentFormSp, 200f, "Per-form maximum is used on construction.");
            custom.UpdateFormSp(1000f, true, true);
            Near(custom.CurrentFormSp, 200f, "Authored depletion settings cannot drain SP.");
            Check(!custom.IsFormSpExhausted, "Authored exhaustion settings cannot exhaust a form.");
            LegacyExhaustion(custom);
            custom.UpdateFormSp(1f / 60f, false, false);
            Near(custom.CurrentFormSp, 200f, "Legacy SP restores to the authored maximum immediately.");
            Check(!custom.IsFormSpExhausted, "Legacy exhaustion clears independently of authored recovery settings.");
            Near(form.CurrentFormSp, 100f, "Another form's custom state cannot change the first form.");

            host = new GameObject("__FormSpRegressionPlayer");
            host.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
            var player = host.AddComponent<PlayerController>();
            var stats = host.GetComponent<PlayerStatManager>();
            var statData = ScriptableObject.CreateInstance<PlayerStatData>();
            assets.Add(statData);
            statData.baseATK = 10f;
            statData.baseAtkSpd = 1f;
            stats.Initialize(statData);
            Property(player, nameof(PlayerController.StatManager), stats);
            Property(player, nameof(PlayerController.Controller), host.GetComponent<CharacterController>());
            Property(player, nameof(PlayerController.Input), host.GetComponent<PlayerInputHandler>());
            Property(player, nameof(PlayerController.Animator), host.AddComponent<Animator>());
            var manager = new FormManager(player);
            Property(player, nameof(PlayerController.FormManager), manager);

            form.Equip(player);
            stats.AddModifier(StatType.ST_ATK, 0f, .5f);
            stats.AddModifier(StatType.ST_ATK_SPD, 0f, .5f);
            Near(form.EffectiveAttack, 15f, "Form attack includes existing buffs.");
            Near(form.EffectiveSpeed, 1.5f, "Form speed includes existing buffs.");
            form.SnapshotNormal();
            var healthyHit = form.Hit();
            form.UpdateFormSp(1000f, true, true);
            Near(form.EffectiveAttack, 15f, "Prolonged combat preserves buffed attack.");
            Near(form.EffectiveSpeed, 1.5f, "Prolonged combat preserves buffed attack speed.");
            Near(stats.GetStat(StatType.ST_ATK), 15f, "Form ticks do not mutate shared attack stats.");
            Near(stats.GetStat(StatType.ST_ATK_SPD), 1.5f, "Form ticks do not mutate shared speed stats.");
            Near(form.Hit().BaseDamage, 15f, "An ongoing normal attack retains its damage snapshot.");
            stats.AddModifier(StatType.ST_ATK, 0f, .25f);
            float changedAttack = stats.GetStat(StatType.ST_ATK);
            Check(changedAttack > 15f, "The snapshot fixture changes the live attack stat.");
            Near(form.Hit().BaseDamage, 15f, "An ongoing normal attack retains its snapshot after a stat change.");
            form.SnapshotNormal();
            Near(form.Hit().BaseDamage, changedAttack, "A new normal attack snapshots the updated attack.");
            stats.RemoveModifier(StatType.ST_ATK, 0f, .25f);
            form.UseSkill();
            Near(form.Hit().BaseDamage, 15f, "Live actions read the current attack.");
            var delayedHit = form.Hit(2f, 7f, true);
            Near(delayedHit.BaseDamage, 37f, "Secondary damage scales its attack portion and preserves fixed extra damage.");
            Check(delayedHit.HasSourceDamageSnapshot, "Generated effects retain their damage snapshot.");

            var other = new ProbeForm(data, ElementType.ELEMENT_FIRE);
            manager.AddTransition(form, () => false);
            manager.AddTransition(other, () => false);
            manager.ChangeForm(other);
            Near(form.Hit().BaseDamage, 15f, "An inactive form retains normal attack scaling.");
            form.UpdateFormSp(4f, false, false);
            Near(form.EffectiveAttack, 15f, "Inactive ticks preserve attack buffs.");
            Near(form.EffectiveSpeed, 1.5f, "Inactive ticks preserve speed buffs.");
            Near(delayedHit.BaseDamage, 37f, "Later ticks cannot rewrite damage already created.");
            Near(healthyHit.BaseDamage, 15f, "Later resource changes cannot rewrite an earlier hit.");
            form.SnapshotNormal();
            Near(form.Hit().BaseDamage, 15f, "A new attack keeps its full buffed damage.");
            form.UpdateFormSp(30f, true, true);
            form.UpdateFormSp(4f, false, false);
            Near(stats.GetStat(StatType.ST_ATK), 15f, "Repeated active and inactive ticks never accumulate stat changes.");

            custom.Equip(player);
            customData.CommonBalance.FormSp.ExhaustedAttackMultiplier = 0f;
            customData.CommonBalance.FormSp.ExhaustedAttackSpeedMultiplier = 0f;
            LegacyExhaustion(custom);
            Near(custom.EffectiveAttack, 15f, "Legacy exhaustion and authored attack penalties are ignored immediately.");
            Near(custom.EffectiveSpeed, 1.5f, "Legacy exhaustion and authored speed penalties are ignored immediately.");
            custom.UpdateFormSp(1000f, true, true);
            Near(custom.EffectiveAttack, 15f, "Authored attack penalties remain ignored after prolonged combat.");
            Near(custom.EffectiveSpeed, 1.5f, "Authored speed penalties remain ignored after prolonged combat.");
            Check(!custom.IsFormSpExhausted, "Prolonged combat clears the legacy exhaustion state.");
            stats.AddModifier(StatType.ST_ATK_SPD, 0f, -2f);
            Near(form.EffectiveSpeed, .1f, "Attack speed preserves its existing lower bound.");

            float sharedSp = stats.CurrentSp;
            Check(stats.TryConsumeSp(20f), "Shared action SP can still be consumed independently.");
            float ownSp = form.CurrentFormSp;
            stats.SetItemResourceModifiers(new object(), 0f, 50f, 10f);
            Near(form.MaxFormSp, 100f, "Shared SP item modifiers do not change form capacity.");
            Near(form.CurrentFormSp, ownSp, "Shared SP spending and item changes do not alter form SP.");
            form.UpdateFormSp(2f, true, true);
            Near(stats.CurrentSp, sharedSp - 20f, "Form SP ticking does not spend shared SP.");

            // Custom actions use their authored clocks: Earth/Water ignore normal speed buffs,
            // while Wood follows the normal attack clock. Holding a form must not slow either clock.
            stats.RemoveModifier(StatType.ST_ATK_SPD, 0f, -2f);
            host.transform.position = new Vector3(100000f, 10000f, 100000f);
            host.SetActive(true);
            var earthData = ScriptableObject.CreateInstance<EarthFormActionDataSO>();
            assets.Add(earthData);
            earthData.SkillDashDistance = 6f;
            earthData.SkillDashDuration = 1f;
            earthData.SkillSteps = new List<WeaponActionData> { new WeaponActionData { Duration = 1f } };
            foreach (string action in new[] { "Skill", "SwitchAttack" })
            {
                foreach (bool prolongedUse in new[] { false, true })
                {
                    var earth = new EarthForm(earthData);
                    Field(typeof(BaseForm), earth, "_playerController", player);
                    Action(earth, action);
                    if (prolongedUse) earth.UpdateFormSp(1000f, true, true);
                    Invoke(typeof(EarthForm), earth, "PrepareSkillAttack");
                    Vector3 start = host.transform.position;
                    Check(!Advance(typeof(EarthForm), earth, "AdvanceSkill", .5f),
                        $"Earth {action} cannot finish halfway through its dash.");
                    Near(Read<float>(typeof(BaseForm), earth, "_timer"), .5f,
                        $"Earth {action} clock preserves its authored speed after holding the form.");
                    Near(player.Animator.speed, 1f,
                        $"Earth {action} animation agrees with its fixed action clock.");
                    Near(Vector3.Distance(host.transform.position, start), 3f,
                        $"Earth {action} movement follows its authored progress.", .02f);
                    Check(Advance(typeof(EarthForm), earth, "AdvanceSkill", .5f),
                        $"Earth {action} completes at its authored duration.");
                    Near(Vector3.Distance(host.transform.position, start), 6f,
                        $"Earth {action} preserves its total authored dash distance.", .02f);
                }
            }

            var woodData = ScriptableObject.CreateInstance<WoodFormActionDataSO>();
            assets.Add(woodData);
            woodData.SkillSteps = new List<WeaponActionData>
            {
                new WeaponActionData { Duration = 3f, HitStartTime = 99f }
            };
            var wood = new WoodForm(woodData);
            Field(typeof(BaseForm), wood, "_playerController", player);
            Field(typeof(WoodForm), wood, "_skillActive", true);
            Action(wood, "Skill");
            Advance(typeof(WoodForm), wood, "AdvanceSkill", .5f);
            Near(Read<float>(typeof(BaseForm), wood, "_timer"), .75f, "Wood skill follows normal attack speed buffs.");
            Near(player.Animator.speed, 1.5f, "Wood skill refreshes its normal animation speed.");
            wood.UpdateFormSp(1000f, true, true);
            Advance(typeof(WoodForm), wood, "AdvanceSkill", .5f);
            Near(Read<float>(typeof(BaseForm), wood, "_timer"), 1.5f, "Wood skill clock retains its buffed speed after prolonged use.");
            Near(player.Animator.speed, 1.5f, "Wood skill animation retains its buffed speed after prolonged use.");

            var waterData = ScriptableObject.CreateInstance<WaterFormActionDataSO>();
            assets.Add(waterData);
            waterData.UltimateSteps = new List<WeaponActionData> { new WeaponActionData { Duration = 2f } };
            foreach (bool prolongedUse in new[] { false, true })
            {
                var water = new WaterForm(waterData);
                Field(typeof(BaseForm), water, "_playerController", player);
                Field(typeof(WaterForm), water, "_ultimateCenter", host.transform.position);
                Action(water, "Ultimate");
                if (prolongedUse) water.UpdateFormSp(1000f, true, true);
                string WaterState(bool complete)
                    => $"prolongedUse={prolongedUse}, exhaustedState={water.IsFormSpExhausted}, "
                    + $"SP={water.CurrentFormSp:R}, elapsed={Read<float>(typeof(WaterForm), water, "_ultimateElapsed"):R}, "
                    + $"hits={Read<int>(typeof(WaterForm), water, "_ultimateHits")}, complete={complete}, "
                    + $"ActionDeltaTime={player.ActionDeltaTime:R}, animatorSpeed={player.Animator.speed:R}";
                bool AdvanceWater(float dt)
                {
                    bool done = Advance(typeof(WaterForm), water, "AdvanceUltimate", dt);
                    // Edit Mode may have zero ActionDeltaTime. Drain only already-due hits so
                    // the production hit-stop break cannot make this deterministic clock test stall.
                    for (int i = 0; i < 10 && !done; i++)
                    {
                        int hits = Read<int>(typeof(WaterForm), water, "_ultimateHits");
                        float elapsed = Read<float>(typeof(WaterForm), water, "_ultimateElapsed");
                        // Match the scheduler's multiplication comparison exactly; division/floor
                        // can round a nominal boundary to one fewer due hit.
                        if (hits >= 10 || elapsed < (hits + 1) * .2f) break;
                        done = Advance(typeof(WaterForm), water, "AdvanceUltimate", 0f);
                    }
                    return done;
                }
                Check(!AdvanceWater(1f), "Water ultimate cannot finish after one second.");
                Near(Read<float>(typeof(WaterForm), water, "_ultimateElapsed"), 1f,
                    "Water ultimate retains its fixed clock after prolonged use.");
                Near(player.Animator.speed, 1f, "Water ultimate animation agrees with its fixed clock.");
                bool complete = AdvanceWater(1f);
                // The existing float scheduler can leave the tenth threshold just above 2f
                // on some Unity runtimes. Allow ten microseconds only when completion is due.
                if (!complete) complete = AdvanceWater(.00001f);
                Near(Read<float>(typeof(WaterForm), water, "_ultimateElapsed"), 2f,
                    "Water ultimate reaches its expected clock boundary within floating-point tolerance.");
                Check(complete, "Water ultimate completes after two seconds even after prolonged use. " + WaterState(complete));
                Check(Read<int>(typeof(WaterForm), water, "_ultimateHits") == 10,
                    "Water ultimate preserves its fixed ten hits. " + WaterState(complete));
            }

            // The real manager clears legacy state on every registered form, including inactive forms.
            var forms = new List<ProbeForm>();
            var managerData = Data();
            var tickingManager = new FormManager(player);
            Property(player, nameof(PlayerController.FormManager), tickingManager);
            for (int i = 0; i < 5; i++)
            {
                var entry = new ProbeForm(managerData);
                LegacyExhaustion(entry);
                forms.Add(entry);
                tickingManager.AddTransition(entry, () => false);
                tickingManager.AddTransition(entry, () => false);
            }
            tickingManager.ChangeForm(forms[0]);
            host.SetActive(true);
            Check(player.isActiveAndEnabled, "Manager fixture is active.");
            Check(!player.IsInRoomCombat, "A player without an encounter source is outside combat.");
            tickingManager.UpdateFormSp(4f);
            for (int i = 0; i < forms.Count; i++)
            {
                Near(forms[i].CurrentFormSp, 100f, "The manager immediately restores both active and inactive forms.");
                Check(!forms[i].IsFormSpExhausted, "Each registered form independently clears legacy exhaustion.");
            }
            LegacyExhaustion(forms[1]);
            player.enabled = false;
            tickingManager.UpdateFormSp(1f);
            Near(forms[1].CurrentFormSp, 0f, "Disabled players freeze form resources.");
            player.enabled = true;
            Property(player, "IsShopPaused", true);
            tickingManager.UpdateFormSp(1f);
            Near(forms[1].CurrentFormSp, 0f, "Shop pause freezes form resources.");
            Property(player, "IsShopPaused", false);
            Property(player, "IsStageTransitioning", true);
            tickingManager.UpdateFormSp(1f);
            Near(forms[1].CurrentFormSp, 0f, "Stage transitions freeze form resources.");
            Property(player, "IsStageTransitioning", false);
            typeof(PlayerStatManager).GetField("_currentHp", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(stats, 0f);
            tickingManager.UpdateFormSp(1f);
            Near(forms[1].CurrentFormSp, 0f, "Dead players freeze form resources.");
            var fresh = new ProbeForm(managerData);
            Near(fresh.CurrentFormSp, 100f, "A fresh player form does not inherit another form's exhaustion.");
            Check(!fresh.IsFormSpExhausted, "Fresh forms reset the exhaustion latch.");
            return $"PASS: form SP ({checks} checks: no holding penalty, full resources, legacy state cleanup, ignored exhaustion settings, frame ticks, notifications, attack snapshots, fixed extra damage, custom action clocks/motion, buff/SP isolation, and manager pause/death guards).";
        }
        finally
        {
            if (host != null) Object.DestroyImmediate(host);
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var asset in assets) if (asset != null) Object.DestroyImmediate(asset);
        }
    }

    private static void Property(object target, string name, object value)
        => target.GetType().GetProperty(name).SetValue(target, value);

    private static void LegacyExhaustion(BaseForm form)
    {
        Field(typeof(BaseForm), form, "_currentFormSp", 0d);
        typeof(BaseForm).GetProperty(nameof(BaseForm.IsFormSpExhausted)).SetValue(form, true);
    }

    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Field(Type owner, object target, string name, object value)
        => owner.GetField(name, Hidden).SetValue(target, value);
    private static T Read<T>(Type owner, object target, string name)
        => (T)owner.GetField(name, Hidden).GetValue(target);
    private static void Action(BaseForm form, string action)
    {
        var field = typeof(BaseForm).GetField("_currentAction", Hidden);
        field.SetValue(form, Enum.Parse(field.FieldType, action));
    }
    private static void Invoke(Type owner, object target, string name)
        => owner.GetMethod(name, Hidden).Invoke(target, null);
    private static bool Advance(Type owner, object target, string name, float deltaTime)
    {
        object[] args = { deltaTime, false };
        owner.GetMethod(name, Hidden).Invoke(target, args);
        return (bool)args[1];
    }
}
