// Run with Unity Pipeline run_script, entry WaterCombatRegression.Run (Edit Mode).
// Uses an isolated preview scene and restores the existing UI singleton in finally.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class WaterCombatRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Set(object target, string property, object value)
        => target.GetType().GetProperty(property).SetValue(target, value);
    private static void Timer(BaseForm form, float time)
        => typeof(BaseForm).GetField("_timer", Hidden).SetValue(form, time);
    private static WaterFlowGauge Gauge(WaterForm form)
        => (WaterFlowGauge)typeof(WaterForm).GetField("_flow", Hidden).GetValue(form);
    private static int Combo(BaseForm form)
        => (int)typeof(BaseForm).GetField("_comboStep", Hidden).GetValue(form);
    private static void Press(PlayerInputHandler input, uint id, float duration, bool held = true)
    {
        Set(input, "AttackPressId", id);
        Set(input, "AttackHeldTime", duration);
        Set(input, "IsAttackHeld", held);
        input.AttackBuffer.Set(0.5f);
    }

    public static string Run()
    {
        Check(!Application.isPlaying, "Run regression in Edit Mode.");
        var passed = new List<string>();
        var gauge = new WaterFlowGauge();
        gauge.Charge(99f);
        Check(!gauge.TryConsumeCharge() && gauge.CurrentGauge == 99f, "Partial segment spent.");
        gauge.Charge(51f);
        Check(gauge.TryConsumeCharge() && gauge.CurrentGauge == 50f, "Remainder lost.");
        gauge.Charge(10000f);
        Check(gauge.AvailableCharges == 3 && gauge.CurrentGauge == 300f, "Gauge cap.");
        for (int i = 0; i < 3; i++) Check(gauge.TryConsumeCharge(), "Missing charge.");
        Check(!gauge.TryConsumeCharge(), "Fourth charge accepted.");
        gauge.Charge(float.NaN); gauge.Charge(-100f);
        Check(gauge.CurrentGauge == 0f, "Invalid gauge input.");
        passed.Add("gauge cap / partial remainder / exactly three spends / invalid gains");

        var previousUI = PlayerUIManager.Instance;
        var previousCombatSettings = CombatSettings.Active;
        var scene = EditorSceneManager.NewPreviewScene();
        WeaponActionDataSO data = null;
        try
        {
            PlayerUIManager.Instance = null;
            data = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(
                "Assets/_Project/Scripts/FormActions/WaterFromAction.asset"));
            data.HitVFX = null; data.HitSound = null;
            data.HeavyAttackStep.SlashVFX = null; data.HeavyAttackStep.SwingSound = null;
            var go = new GameObject("WaterCombatRegressionPlayer");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = new Vector3(50000f, 50000f, 50000f);
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/_Project/Animations/AnimController/BDJ.controller");
            animator.Rebind(); animator.Update(0f);
            Check(animator.HasState(0, Animator.StringToHash(data.HeavyAttackStep.AnimationName)), "Heavy animation missing.");
            var player = go.AddComponent<PlayerController>();
            typeof(PlayerController).GetField("_hitStopDuration", Hidden).SetValue(player, 0f);
            var input = go.GetComponent<PlayerInputHandler>();
            var movement = go.GetComponent<PlayerMovement>();
            var stats = go.GetComponent<PlayerStatManager>();
            var values = (Dictionary<StatType, float>)typeof(PlayerStatManager).GetField("finalValues", Hidden).GetValue(stats);
            values[StatType.ST_ATK_SPD] = 1f;
            values[StatType.ST_ATK] = 100f;
            ((Dictionary<StatType, float>)typeof(PlayerStatManager).GetField("baseValues", Hidden).GetValue(stats))[StatType.ST_ATK] = 100f;
            ((Dictionary<StatType, float>)typeof(PlayerStatManager).GetField("addValues", Hidden).GetValue(stats))[StatType.ST_ATK] = 0f;
            ((Dictionary<StatType, float>)typeof(PlayerStatManager).GetField("multValues", Hidden).GetValue(stats))[StatType.ST_ATK] = 0f;
            typeof(PlayerStatManager).GetField("_currentHp", Hidden).SetValue(stats, 100f);
            Set(player, "Animator", animator); Set(player, "Input", input);
            Set(player, "Controller", go.GetComponent<CharacterController>());
            Set(player, "Movement", movement); Set(player, "StatManager", stats);
            movement.Initialize(player);
            var forms = new FormManager(player);
            Set(player, "FormManager", forms); Set(player, "StateMachine", new StateMachine());

            var water = new WaterForm(data); forms.ChangeForm(water);
            water.BeginSkill(); water.EndSkill();
            Check(water.WaterGauge == 100f, "Skill use must reward once even without a hit.");
            Check(water.IsSkillBuffActive && Mathf.Approximately(stats.GetStat(StatType.ST_ATK), 120f)
                && Mathf.Approximately(water.HeavyAttackDamageMultiplier, 1.3f), "Skill buffs not applied.");
            water.BeginAttack();
            typeof(WaterForm).GetMethod("OnHitEnemy", Hidden).Invoke(water, new object[] { go });
            water.EndAttack();
            Check(water.WaterGauge == 112f, "Normal hit gain.");
            passed.Add("skill use +100 / normal hit +12 / configured animation exists");

            Press(input, 1, 0f);
            var attack = new PlayerAttackState(player); attack.Enter();
            Check(attack.IsCharging && water.WaterGauge == 112f, "Direct charge starts without spending.");
            Set(input, "AttackHeldTime", 0.4f); attack.Update();
            Check(attack.IsHeavyAttack && water.WaterGauge == 12f, "Direct heavy spends once.");
            typeof(WaterForm).GetMethod("OnHitEnemy", Hidden).Invoke(water, new object[] { go });
            Check(water.WaterGauge == 12f, "Heavy hit must not regenerate its cost.");
            attack.Update(); attack.Update();
            Check(water.WaterGauge == 12f, "Held input spent repeatedly."); attack.Exit();
            passed.Add("direct heavy / one spend per hold / no heavy self-recharge");

            Gauge(water).Charge(100f);
            Press(input, 2, 0f); attack = new PlayerAttackState(player); attack.Enter();
            Set(input, "AttackHeldTime", 0.1f); Set(input, "IsAttackHeld", false); attack.Update();
            Check(!attack.IsCharging && !attack.IsHeavyAttack && water.WaterGauge == 112f, "Tap must start normal.");
            Press(input, 3, 0.1f); Timer(water, -10f); attack.Update();
            Set(input, "AttackHeldTime", 0.4f); attack.Update();
            Check(!attack.IsHeavyAttack && water.WaterGauge == 112f, "Heavy branched before combo window.");
            Set(input, "IsAttackHeld", false);
            Timer(water, 1.01f); attack.Update();
            Check(attack.IsHeavyAttack && water.WaterGauge == 12f, "Held branch lost after release."); attack.Exit();
            passed.Add("tap normal / heavy waits for combo window / release preserves accepted hold");

            Gauge(water).Charge(100f);
            Press(input, 4, 0.1f, false); attack = new PlayerAttackState(player); attack.Enter(); attack.Update();
            Press(input, 5, 0.1f, false); Timer(water, 1.01f); attack.Update();
            Check(Combo(water) == 1 && !attack.IsHeavyAttack && water.WaterGauge == 112f, "Tap combo regression.");
            attack.Exit();
            Press(input, 6, 0f); attack = new PlayerAttackState(player); attack.Enter(); attack.Exit();
            Check(water.WaterGauge == 112f && !input.AttackBuffer.IsActive, "Canceled charge spent/leaked input.");
            passed.Add("normal combo with available gauge / charge cancellation preserves gauge");

            var fire = new FireForm(data); forms.ChangeForm(fire);
            Press(input, 7, 0f); attack = new PlayerAttackState(player); attack.Enter();
            Check(!attack.IsCharging && Combo(fire) == 0, "Other form attack delayed.");
            Press(input, 8, 0f); Timer(fire, 1.01f); attack.Update();
            Check(Combo(fire) == 1, "Other form combo regression."); attack.Exit();
            forms.ChangeForm(water);
            Check(water.WaterGauge == 112f, "Form switch lost gauge.");
            Gauge(water).TryConsumeCharge();
            Press(input, 9, 2f); attack = new PlayerAttackState(player); attack.Enter();
            Check(!attack.IsCharging && !attack.IsHeavyAttack, "Insufficient gauge should attack normally."); attack.Exit();
            passed.Add("other forms immediate attack + combo / switch preserves gauge / empty gauge fallback");

            var tick = typeof(WaterForm).GetMethod("OnTick", Hidden);
            tick.Invoke(water, new object[] { 2f });
            Check(Mathf.Approximately(water.SkillBuffRemaining, 4f), "Buff timer not ticking.");
            water.BeginSkill(); water.EndSkill();
            Check(Mathf.Approximately(water.SkillBuffRemaining, 6f)
                && Mathf.Approximately(stats.GetStat(StatType.ST_ATK), 120f), "Refresh stacked buff.");
            forms.ChangeForm(fire);
            Check(water.IsSkillBuffActive && Mathf.Approximately(stats.GetStat(StatType.ST_ATK), 120f), "Switch removed player buff.");
            tick.Invoke(water, new object[] { 7f });
            Check(!water.IsSkillBuffActive && Mathf.Approximately(stats.GetStat(StatType.ST_ATK), 100f)
                && water.HeavyAttackDamageMultiplier == 1f, "Unequipped buff did not expire.");
            passed.Add("attack +20% / heavy +30% / refresh without stacking / expiry while unequipped");

            forms.ChangeForm(water);
            stats.AddModifier(StatType.ST_ATK, 0f, 0.1f);
            water.BeginSkill(); water.EndSkill();
            Check(Mathf.Approximately(stats.GetStat(StatType.ST_ATK), 130f), "Other attack modifier overwritten.");
            data.WaterSkillAttackBonusPercent = 50f;
            water.BeginSkill(); water.EndSkill();
            Check(Mathf.Approximately(stats.GetStat(StatType.ST_ATK), 160f), "Inspector edit left residual modifier.");
            forms.ChangeForm(fire);
            forms.CleanupTransientEffects(); forms.CleanupTransientEffects();
            Check(!water.IsSkillBuffActive && Mathf.Approximately(stats.GetStat(StatType.ST_ATK), 110f), "Cleanup removed another buff or left water buff.");
            forms.ChangeForm(water);
            data.WaterSkillBuffDuration = 0f;
            water.BeginSkill(); water.EndSkill();
            Check(!water.IsSkillBuffActive && Mathf.Approximately(stats.GetStat(StatType.ST_ATK), 110f), "Zero duration applied permanent buff.");
            passed.Add("other modifiers preserved / Inspector edits / inactive-form cleanup / zero duration");

            Gauge(water).Charge(10000f);
            int savedLayers = player.Controller.excludeLayers;
            int enemyMask = LayerMask.GetMask("Enemy");
            Check(enemyMask != 0, "Enemy layer missing.");
            Press(input, 100, 0.4f);
            attack = new PlayerAttackState(player); attack.Enter(); attack.Update();
            Check(attack.IsHeavyAttack && water.HeavyAttackCharges == 2
                && (player.Controller.excludeLayers & enemyMask) != 0, "Dash did not ignore enemies.");
            Press(input, 101, 0f, false);
            Timer(water, data.HeavyAttackStep.Duration); attack.Update();
            Check(attack.IsHeavyAttack && !attack.IsAttackComplete && water.HeavyAttackCharges == 1
                && !input.AttackBuffer.IsActive, "Queued tap did not chain instantly.");
            Press(input, 102, 0f, false);
            Timer(water, data.HeavyAttackStep.Duration); attack.Update();
            Check(!attack.IsAttackComplete && water.HeavyAttackCharges == 0, "Third dash failed.");
            Timer(water, data.HeavyAttackStep.Duration); attack.Update();
            Check(attack.IsAttackComplete, "Exhausted gauge caused another dash.");
            attack.Exit();
            Check(player.Controller.excludeLayers == savedLayers, "Dash exit left enemy collision disabled.");
            Gauge(water).Charge(100f);
            Press(input, 103, 0.4f);
            attack = new PlayerAttackState(player); attack.Enter(); attack.Update(); attack.Exit();
            Check(player.Controller.excludeLayers == savedLayers, "Dash cancellation left enemy collision disabled.");
            Gauge(water).Charge(100f);
            water.TryBeginHeavyAttack(); forms.CleanupTransientEffects();
            Check(player.Controller.excludeLayers == savedLayers, "Dash cleanup failed.");
            passed.Add("enemy collision exclusion / immediate queued chain / three dash limit / exit-cancel-cleanup restoration");
            player.VerticalVelocity = 0f;
            Set(input, "MoveInput", Vector2.zero);
            Vector3 dashStart = go.transform.position;
            Gauge(water).Charge(100f); water.TryBeginHeavyAttack();
            bool dashDone = false;
            for (int i = 0; i < 120 && !dashDone; i++) water.UpdateHeavyAttack(out dashDone);
            Check(dashDone && Mathf.Abs(Vector3.Distance(dashStart, go.transform.position) - data.WaterHeavyDashDistance) < 0.05f,
                "Dash did not travel the configured distance.");
            water.EndHeavyAttack();

            var enemyObstacle = new GameObject("DashEnemyCollider", typeof(BoxCollider));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(enemyObstacle, scene);
            enemyObstacle.layer = LayerMask.NameToLayer("Enemy");
            enemyObstacle.transform.position = go.transform.position + go.transform.forward * 2f;
            var wallObstacle = new GameObject("DashWallCollider", typeof(BoxCollider));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wallObstacle, scene);
            wallObstacle.transform.position = go.transform.position + go.transform.forward * 5f;
            wallObstacle.transform.localScale = new Vector3(5f, 5f, 1f);
            Physics.SyncTransforms();
            dashStart = go.transform.position;
            Gauge(water).Charge(100f); water.TryBeginHeavyAttack(); dashDone = false;
            for (int i = 0; i < 120 && !dashDone; i++) water.UpdateHeavyAttack(out dashDone);
            float traveled = Vector3.Distance(dashStart, go.transform.position);
            Check(dashDone && traveled > 2.5f && traveled < 5f, "Dash must cross the enemy and stop at the wall: " + traveled);
            water.EndHeavyAttack();
            passed.Add("configured dash distance / physically crosses enemy / physically blocked by wall");
            Gauge(water).Charge(200f);
            Press(input, 200, 0.4f); attack = new PlayerAttackState(player); attack.Enter(); attack.Update();
            Timer(water, data.HeavyAttackStep.Duration); attack.Update();
            Check(attack.IsAttackComplete, "Unqueued completed attack did not finish.");
            Press(input, 201, 0f, false);
            Check(!attack.IsAttackComplete, "Completion-frame input would exit to Idle.");
            attack.Update();
            Check(attack.IsHeavyAttack && !attack.IsAttackComplete && water.HeavyAttackCharges == 0,
                "Completion-frame input did not chain.");
            attack.Exit();
            passed.Add("completion-frame input chains without Idle or another hold");

            // Use a real damage receiver to verify target memory and a return dash.
            var settings = UnityEngine.Object.FindFirstObjectByType<CombatSettings>();
            Check(settings != null && settings.ElementTable != null, "Combat table required for damage regression.");
            settings.ElementTable.Initialize();
            typeof(CombatSettings).GetProperty("Active").SetValue(null, settings);
            wallObstacle.SetActive(false);
            player.VerticalVelocity = 0f;
            go.transform.rotation = Quaternion.identity;
            enemyObstacle.transform.position = go.transform.position + Vector3.forward * 2f + Vector3.up * 0.8f;
            var receiver = enemyObstacle.AddComponent<WaterDashRegressionTarget>();
            Physics.SyncTransforms();
            Gauge(water).Charge(300f);
            water.TryBeginHeavyAttack();
            Check(!water.TryContinueHeavyAttack() && water.HeavyAttackCharges == 2, "Follow-up started during first dash.");
            dashDone = false;
            for (int i = 0; i < 120 && !dashDone; i++) water.UpdateHeavyAttack(out dashDone);
            Check(dashDone && receiver.Hits == 1 && water.HeavyAttackTarget == receiver.transform,
                "First dash did not remember its damaged target.");
            Vector3 beforeReturn = go.transform.position;
            Check(water.TryContinueHeavyAttack(), "Completed dash could not continue.");
            Check(Vector3.Dot(go.transform.forward, Vector3.back) > 0.9f, "Return dash did not face the remembered target.");
            Check(receiver.Hits == 1, "Follow-up dealt damage before physically reaching target.");
            dashDone = false;
            for (int i = 0; i < 120 && !dashDone; i++) water.UpdateHeavyAttack(out dashDone);
            Check(dashDone && receiver.Hits == 2 && go.transform.position.z < beforeReturn.z,
                "Remembered target was not hit once again on return dash.");
            water.EndHeavyAttack();
            Check(water.HeavyAttackTarget == null, "Target leaked outside the attack chain.");
            passed.Add("no mid-dash continuation / remember actual damaged target / reverse dash hits same target again / memory clears");

            Gauge(water).Charge(300f);
            Press(input, 300, 0.4f); attack = new PlayerAttackState(player); attack.Enter(); attack.Update();
            Press(input, 301, 0f, false);
            Timer(water, data.WaterHeavyDashDuration);
            attack.Update();
            Check(water.HeavyAttackCharges == 2 && !attack.IsAttackComplete,
                "Queue consumed before full attack duration ended.");
            Timer(water, data.HeavyAttackStep.Duration); attack.Update();
            Check(water.HeavyAttackCharges == 1, "Queue failed after full attack completion.");
            attack.Exit();
            passed.Add("queued input waits through recovery until the full motion completes");
            return "PASS: " + string.Join("; ", passed);
        }
        finally
        {
            PlayerUIManager.Instance = null;
            EditorSceneManager.ClosePreviewScene(scene);
            if (data != null) UnityEngine.Object.DestroyImmediate(data);
            PlayerUIManager.Instance = previousUI;
            typeof(CombatSettings).GetProperty("Active").SetValue(null, previousCombatSettings);
        }
    }
}

public class WaterDashRegressionTarget : MonoBehaviour, IDamageable
{
    public int Hits;
    public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE,
        bool isCritical = false, Vector3 power = default, float poiseDamage = 20f,
        StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
    {
        Hits++;
        return new DamageResult(DamageOutcome.Applied, damage);
    }
}
