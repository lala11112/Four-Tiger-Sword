using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Exercises attack stat snapshots, secondary damage and the authored clocks of custom form actions.</summary>
public static class CombatActionStatRegression
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

    [MenuItem("Tools/Studio Baekho/Combat/Run Combat Action Stat Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Combat Action Stat regression requires Edit Mode.");
        int checks = 0;
        void Check(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException("Combat Action Stat: " + message);
            checks++;
        }
        void Near(float actual, float expected, string message, float tolerance = .0001f)
            => Check(Mathf.Abs(actual - expected) <= tolerance,
                message + $" (actual {actual}, expected {expected})");

        Check((int)StatType.ST_HP == 0 && (int)StatType.ST_ATK == 4 && (int)StatType.ST_DEF == 5
            && (int)StatType.ST_ATK_SPD == 20, "Remaining combat stat IDs must match existing serialized assets.");
        Check(!Enum.IsDefined(typeof(StatType), 1) && !Enum.IsDefined(typeof(StatType), 3),
            "Retired resource IDs must not be reassigned to another stat.");

        var assets = new List<Object>();
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject host = null;
        try
        {
            var data = ScriptableObject.CreateInstance<FireFormActionDataSO>();
            assets.Add(data);
            var form = new ProbeForm(data);
            host = new GameObject("__CombatActionStatRegressionPlayer");
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
            var originalHit = form.Hit();
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
            Near(form.Hit().BaseDamage, 15f, "An inactive form retains its attack scaling.");
            Near(delayedHit.BaseDamage, 37f, "Switching forms cannot rewrite already created secondary damage.");
            Near(originalHit.BaseDamage, 15f, "Switching forms cannot rewrite an earlier hit.");

            stats.AddModifier(StatType.ST_ATK_SPD, 0f, -2f);
            Near(form.EffectiveSpeed, .1f, "Attack speed preserves its existing lower bound.");

            // Earth and Water use fixed clocks, while Wood follows normal attack speed.
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
                var earth = new EarthForm(earthData);
                Field(typeof(BaseForm), earth, "_playerController", player);
                Action(earth, action);
                Invoke(typeof(EarthForm), earth, "PrepareSkillAttack");
                Vector3 start = host.transform.position;
                Check(!Advance(typeof(EarthForm), earth, "AdvanceSkill", .5f),
                    $"Earth {action} cannot finish halfway through its dash.");
                Near(Read<float>(typeof(BaseForm), earth, "_timer"), .5f,
                    $"Earth {action} clock preserves its authored speed.");
                Near(player.Animator.speed, 1f,
                    $"Earth {action} animation agrees with its fixed action clock.");
                Near(Vector3.Distance(host.transform.position, start), 3f,
                    $"Earth {action} movement follows its authored progress.", .02f);
                Check(Advance(typeof(EarthForm), earth, "AdvanceSkill", .5f),
                    $"Earth {action} completes at its authored duration.");
                Near(Vector3.Distance(host.transform.position, start), 6f,
                    $"Earth {action} preserves its total authored dash distance.", .02f);
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
            Advance(typeof(WoodForm), wood, "AdvanceSkill", .5f);
            Near(Read<float>(typeof(BaseForm), wood, "_timer"), 1.5f, "Wood skill advances at its buffed speed on the next tick.");
            Near(player.Animator.speed, 1.5f, "Wood skill animation retains its buffed speed on the next tick.");

            var waterData = ScriptableObject.CreateInstance<WaterFormActionDataSO>();
            assets.Add(waterData);
            waterData.UltimateSteps = new List<WeaponActionData> { new WeaponActionData { Duration = 2f } };
            var water = new WaterForm(waterData);
            Field(typeof(BaseForm), water, "_playerController", player);
            Field(typeof(WaterForm), water, "_ultimateCenter", host.transform.position);
            Action(water, "Ultimate");
            string WaterState(bool complete)
                => $"elapsed={Read<float>(typeof(WaterForm), water, "_ultimateElapsed"):R}, "
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
                "Water ultimate uses its fixed clock.");
            Near(player.Animator.speed, 1f, "Water ultimate animation agrees with its fixed clock.");
            bool complete = AdvanceWater(1f);
            // The existing float scheduler can leave the tenth threshold just above 2f
            // on some Unity runtimes. Allow ten microseconds only when completion is due.
            if (!complete) complete = AdvanceWater(.00001f);
            Near(Read<float>(typeof(WaterForm), water, "_ultimateElapsed"), 2f,
                "Water ultimate reaches its expected clock boundary within floating-point tolerance.");
            Check(complete, "Water ultimate completes after two seconds. " + WaterState(complete));
            Check(Read<int>(typeof(WaterForm), water, "_ultimateHits") == 10,
                "Water ultimate preserves its fixed ten hits. " + WaterState(complete));

            return $"PASS: combat action stats ({checks} checks covering attack snapshots, damage, attack speed and custom action timing).";
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
