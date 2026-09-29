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
        WaterFormActionDataSO data = null;
        FireFormActionDataSO fireData = null;
        EarthFormActionDataSO earthData = null;
        GameObject physicsPlayer = null, physicsEnemy = null;
        try
        {
            PlayerUIManager.Instance = null;
            data = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<WaterFormActionDataSO>(
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
            ((Dictionary<StatType, float>)typeof(PlayerStatManager).GetField("baseValues", Hidden).GetValue(stats))[StatType.ST_ATK_SPD] = 1f;
            ((Dictionary<StatType, float>)typeof(PlayerStatManager).GetField("addValues", Hidden).GetValue(stats))[StatType.ST_ATK_SPD] = 0f;
            ((Dictionary<StatType, float>)typeof(PlayerStatManager).GetField("multValues", Hidden).GetValue(stats))[StatType.ST_ATK_SPD] = 0f;
            values[StatType.ST_HP] = 1000f;
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

            fireData = ScriptableObject.CreateInstance<FireFormActionDataSO>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(data), fireData);
            var fire = new FireForm(fireData); forms.ChangeForm(fire);
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
            for (int i = 0; i < 4096 && !dashDone; i++) water.UpdateHeavyAttack(out dashDone);
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
            for (int i = 0; i < 4096 && !dashDone; i++) water.UpdateHeavyAttack(out dashDone);
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
            // Physics.OverlapCapsule queries the default physics scene, unlike Controller.Move.
            physicsPlayer = go; physicsEnemy = enemyObstacle;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(enemyObstacle, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            player.VerticalVelocity = 0f;
            go.transform.rotation = Quaternion.identity;
            enemyObstacle.transform.position = go.transform.position + Vector3.forward * 2f + Vector3.up * 0.8f;
            var receiver = enemyObstacle.AddComponent<WaterDashRegressionTarget>();
            Physics.SyncTransforms();
            Gauge(water).Charge(300f);
            water.TryBeginHeavyAttack();
            Check(!water.TryContinueHeavyAttack() && water.HeavyAttackCharges == 2, "Follow-up started during first dash.");
            dashDone = false;
            for (int i = 0; i < 4096 && !dashDone; i++) water.UpdateHeavyAttack(out dashDone);
            Check(dashDone && receiver.Hits == 1 && water.HeavyAttackTarget == receiver.transform,
                "First dash target: done=" + dashDone + ", hits=" + receiver.Hits + ", target=" + water.HeavyAttackTarget);
            Vector3 beforeReturn = go.transform.position;
            Check(water.TryContinueHeavyAttack(), "Completed dash could not continue.");
            Check(Vector3.Dot(go.transform.forward, Vector3.back) > 0.9f, "Return dash did not face the remembered target.");
            Check(receiver.Hits == 1, "Follow-up dealt damage before physically reaching target.");
            dashDone = false;
            for (int i = 0; i < 4096 && !dashDone; i++) water.UpdateHeavyAttack(out dashDone);
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

            UnityEngine.Object.DestroyImmediate(fireData);
            fireData = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<FireFormActionDataSO>(
                "Assets/_Project/Scripts/FormActions/FireFormAction.asset"));
            // Fix test inputs on the clone; Inspector balancing on the real asset is independent.
            fireData.FireUltimateNormalLifeStealPercent = 10f;
            fireData.FireUltimateSkillLifeStealPercent = 25f;
            fireData.HitVFX = null; fireData.HitSound = null;
            foreach (var step in fireData.ComboSteps) { step.SwingSound = null; step.SlashVFX = null; }
            foreach (var step in fireData.SkillSteps) { step.SwingSound = null; step.SlashVFX = null; }
            fire = new FireForm(fireData); forms.ChangeForm(fire);
            stats.RemoveModifier(StatType.ST_ATK, 0f, 0.1f);
            typeof(PlayerStatManager).GetField("_currentHp", Hidden).SetValue(stats, 500f);
            fire.BeginSkill(); fire.EndSkill();
            Check(fire.IsSkillBuffActive && Mathf.Approximately(stats.GetStat(StatType.ST_ATK), 120f), "Fire skill attack buff.");
            fire.BeginSkill(); fire.EndSkill();
            Check(Mathf.Approximately(stats.GetStat(StatType.ST_ATK), 120f), "Fire skill buff stacked.");
            enemyObstacle.transform.position = go.transform.position + Vector3.right * 4f;
            Physics.SyncTransforms();
            var query = typeof(FireForm).GetMethod("QueryOverlap", Hidden);
            var hitStep = new WeaponActionData { Damage = 1f, HitBoxRadius = 1f, HitBoxOffset = Vector3.zero };
            Check((int)query.Invoke(fire, new object[] { hitStep, go.transform.position }) == 0, "Normal skill radius too large.");
            stats.AddUltimateGauge(stats.MaxUltimateGauge);
            fireData.UltimateSteps.Clear(); // Toggle stance has no separate ultimate attack animation/data requirement.
            Check(fire.CanUltimate, "Fire ultimate cannot activate.");
            fire.BeginUltimate(); fire.UpdateUltimate(out bool activationDone); fire.EndUltimate();
            Check(activationDone && fire.IsUltimateActive && stats.CurrentUltimateGauge == 0f
                && !fire.CanUltimate && Mathf.Approximately(stats.GetStat(StatType.ST_ATK_SPD), 1.3f), "Fire stance activation/cost/speed.");
            fire.BeginSkill(); fire.EndSkill();
            Check((int)query.Invoke(fire, new object[] { hitStep, go.transform.position }) >= 1, "Enhanced skill radius did not expand.");
            float damageBefore = receiver.TotalDamage;
            DamageManager.Apply(new HitInfo(100f, ElementType.ELEMENT_FIRE, 0f, 1f, source: player, trueDamage: true), receiver, enemyObstacle);
            Check(Mathf.Approximately(receiver.TotalDamage - damageBefore, 150f), "Fire outgoing multiplier.");
            float hpBefore = stats.CurrentHp;
            player.TakeDamage(10f, poiseDamage: 0f);
            Check(Mathf.Approximately(hpBefore - stats.CurrentHp, 13f), "Fire incoming vulnerability.");
            player.ConsumeHitReaction();

            var fireTick = typeof(FireForm).GetMethod("OnTick", Hidden);
            int drainDamageEvents = 0, drainHpEvents = 0;
            System.Action<float, ElementType, bool> damageListener = (a, b, c) => drainDamageEvents++;
            System.Action<float, float> hpListener = (a, b) => drainHpEvents++;
            stats.OnDamageTaken += damageListener; stats.OnHpChanged += hpListener;
            stats.GrantShield(100f, 10f);
            hpBefore = stats.CurrentHp;
            fireData.FireUltimateHealthDrainPercentPerSecond = 2f;
            fireTick.Invoke(fire, new object[] { 0.25f });
            fireTick.Invoke(fire, new object[] { 0.75f });
            Check(Mathf.Abs(hpBefore - stats.CurrentHp - stats.MaxHp * 0.02f) < 0.01f
                && stats.Shield == 100f && drainDamageEvents == 0 && drainHpEvents == 2,
                "Fire drain must consume max HP percent directly and notify HP only.");
            stats.OnDamageTaken -= damageListener; stats.OnHpChanged -= hpListener;
            fireData.FireUltimateHealthDrainPercentPerSecond = 0f;
            hpBefore = stats.CurrentHp;
            fireTick.Invoke(fire, new object[] { 1f });
            Check(stats.CurrentHp == hpBefore, "Zero drain setting ignored.");
            fireData.FireUltimateHealthDrainPercentPerSecond = 2f;

            enemyObstacle.transform.position = go.transform.position;
            Physics.SyncTransforms();
            var execute = typeof(BaseForm).GetMethod("ExecuteHit", Hidden);
            fire.BeginAttack(); hpBefore = stats.CurrentHp; damageBefore = receiver.TotalDamage;
            execute.Invoke(fire, new object[] { hitStep, 1f, 0f, new HashSet<IDamageable>() });
            float normalHeal = stats.CurrentHp - hpBefore;
            Check(normalHeal > 0f && Mathf.Abs(normalHeal - (receiver.TotalDamage - damageBefore) * 0.1f) < 0.01f, $"Fire normal lifesteal: heal={normalHeal}, damage={receiver.TotalDamage - damageBefore}, hp={hpBefore}, rate={fireData.FireUltimateNormalLifeStealPercent}.");
            fire.EndAttack();
            fire.BeginSkill(); hpBefore = stats.CurrentHp; damageBefore = receiver.TotalDamage;
            execute.Invoke(fire, new object[] { hitStep, 1f, 0f, new HashSet<IDamageable>() });
            Check(stats.CurrentHp - hpBefore > normalHeal
                && Mathf.Abs(stats.CurrentHp - hpBefore - (receiver.TotalDamage - damageBefore) * 0.25f) < 0.01f, "Fire skill lifesteal.");
            fire.EndSkill();
            input.UltimateBuffer.Set();
            typeof(PlayerController).GetMethod("TryDeactivateUltimate", Hidden).Invoke(player, null);
            Check(!fire.IsUltimateActive && !input.UltimateBuffer.IsActive
                && Mathf.Approximately(stats.GetStat(StatType.ST_ATK_SPD), 1f)
                && fire.IncomingDamageMultiplier == 1f && fire.OutgoingDamageMultiplier == 1f, "Manual ultimate deactivation.");
            fire.BeginAttack(); hpBefore = stats.CurrentHp;
            execute.Invoke(fire, new object[] { hitStep, 1f, 0f, new HashSet<IDamageable>() });
            Check(stats.CurrentHp == hpBefore, "Lifesteal persisted after deactivation.");
            fire.EndAttack();
            fire.BeginUltimate(); forms.ChangeForm(water);
            Check(!fire.IsUltimateActive && Mathf.Approximately(stats.GetStat(StatType.ST_ATK_SPD), 1f), "Switch did not end fire ultimate.");
            typeof(FireForm).GetMethod("OnTick", Hidden).Invoke(fire, new object[] { 7f });
            Check(!fire.IsSkillBuffActive && Mathf.Approximately(stats.GetStat(StatType.ST_ATK), 100f), "Fire skill buff did not expire.");
            hpBefore = stats.CurrentHp;
            fireTick.Invoke(fire, new object[] { 1f });
            Check(stats.CurrentHp == hpBefore, "Drain continued after form switch.");
            forms.ChangeForm(fire); fire.BeginUltimate();
            typeof(PlayerStatManager).GetField("_currentHp", Hidden).SetValue(stats, 2f);
            fireTick.Invoke(fire, new object[] { 100f });
            Check(stats.CurrentHp == 1f && !fire.IsUltimateActive
                && fire.IncomingDamageMultiplier == 1f && fire.OutgoingDamageMultiplier == 1f
                && Mathf.Approximately(stats.GetStat(StatType.ST_ATK_SPD), 1f), "Low HP must end stance without killing player.");
            fireTick.Invoke(fire, new object[] { 1f });
            Check(stats.CurrentHp == 1f, "Drain continued after automatic deactivation.");
            passed.Add("FIRE drain: frame-independent max HP cost / shield bypass / HP event / no hit event / zero setting / switch stop / low HP automatic off");
            passed.Add("FIRE: AoE radii / skill buff refresh-expiry / toggle cost / dealt-taken damage / speed / normal-skill lifesteal / manual off / switch cleanup");
            earthData = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<EarthFormActionDataSO>(
                "Assets/_Project/Scripts/FormActions/EarthFormAction.asset"));
            earthData.HitVFX = null; earthData.HitSound = null;
            foreach (var step in earthData.SkillSteps) { step.SwingSound = null; step.SlashVFX = null; }
            var earth = new EarthForm(earthData); forms.ChangeForm(earth);
            typeof(PlayerStatManager).GetField("_currentHp", Hidden).SetValue(stats, 1000f);
            var shieldTick = typeof(PlayerStatManager).GetMethod("UpdateShields", Hidden);
            shieldTick.Invoke(stats, new object[] { 100f });
            earth.BeginSkill(); earth.EndSkill();
            Check(earth.SkillShield == 300f && earth.UltimateShield == 0f && earth.OutgoingDamageMultiplier == 1f, "Skill shield alone must not activate ultimate buff.");
            stats.AddUltimateGauge(stats.MaxUltimateGauge);
            earthData.UltimateSteps.Clear();
            Check(earth.CanUltimate, "Earth shield ultimate requires unused attack steps.");
            earth.BeginUltimate(); earth.UpdateUltimate(out bool earthDone); earth.EndUltimate();
            Check(earthDone && stats.CurrentUltimateGauge == 0f && stats.Shield == 900f
                && Mathf.Approximately(earth.OutgoingDamageMultiplier, 1.3f), "Independent earth shields/ultimate activation.");
            var shieldHit = stats.TakeDamage(350f, ElementType.ELEMENT_NONE, false);
            Check(earth.SkillShield == 0f && earth.UltimateShield == 550f && shieldHit.HealthDamage == 0f
                && shieldHit.ShieldDamage == 350f && earth.IsUltimateDamageBuffActive, "Shield consumption ordering.");
            earth.BeginSkill(); earth.EndSkill();
            Check(earth.SkillShield == 300f && earth.UltimateShield == 550f, "Skill refresh changed ultimate shield.");
            shieldTick.Invoke(stats, new object[] { 5f });
            Check(earth.SkillShield == 0f && earth.UltimateShield == 550f && earth.IsUltimateDamageBuffActive, "Shield lifetimes not independent.");
            stats.TakeDamage(550f, ElementType.ELEMENT_NONE, false);
            Check(!earth.IsUltimateDamageBuffActive && earth.OutgoingDamageMultiplier == 1f, "Buff survived both shield breaks.");
            earth.BeginSkill(); earth.EndSkill();
            Check(earth.IsUltimateDamageBuffActive, "Skill shield failed to restore buff within ultimate duration.");
            damageBefore = receiver.TotalDamage;
            DamageManager.Apply(new HitInfo(100f, ElementType.ELEMENT_EARTH, 0f, 1f, source: player, trueDamage: true), receiver, enemyObstacle);
            Check(Mathf.Approximately(receiver.TotalDamage - damageBefore, 130f), "Earth outgoing damage not applied.");
            typeof(EarthForm).GetMethod("OnTick", Hidden).Invoke(earth, new object[] { 11f });
            Check(earth.SkillShield > 0f && !earth.IsUltimateDamageBuffActive, "Expired ultimate still buffed skill shield.");
            earth.CleanupTransientEffects();
            Check(stats.Shield == 0f, "Earth shield cleanup.");

            enemyObstacle.transform.position = go.transform.position + Vector3.right * 50f;
            go.transform.rotation = Quaternion.identity;
            Physics.SyncTransforms();
            var advanceEarth = typeof(EarthForm).GetMethod("AdvanceSkill", Hidden);
            Vector3 earthStart = go.transform.position;
            earth.BeginSkill();
            advanceEarth.Invoke(earth, new object[] { 0.2f, false });
            advanceEarth.Invoke(earth, new object[] { 0.2f, false });
            Check(Mathf.Abs(Vector3.Distance(earthStart, go.transform.position) - earthData.SkillDashDistance) < 0.05f, "Earth charge distance.");
            earth.EndSkill();
            earthStart = go.transform.position;
            advanceEarth.Invoke(earth, new object[] { 1f, false });
            Check(go.transform.position == earthStart, "Cancelled earth skill continued moving.");
            enemyObstacle.transform.position = go.transform.position + Vector3.up;
            Physics.SyncTransforms();
            earthData.SkillDashDistance = 0f;
            int earthHitsBefore = receiver.Hits;
            earth.BeginSkill();
            advanceEarth.Invoke(earth, new object[] { 0.1f, false });
            advanceEarth.Invoke(earth, new object[] { 0.1f, false });
            Check(receiver.Hits == earthHitsBefore + 1, "Earth charge must hit each target once.");
            earth.EndSkill();
            earth.BeginUltimate(); forms.ChangeForm(water);
            Check(earth.UltimateBuffRemaining == 0f && earth.OutgoingDamageMultiplier == 1f, "Earth bonus leaked across form switch.");
            earth.CleanupTransientEffects();
            passed.Add("EARTH: separate shield pools / refresh / expiry / damage absorption / shield-gated ultimate damage / buff expiry / charge distance / one hit per target / cancellation / switch cleanup");
            return "PASS: " + string.Join("; ", passed);
        }
        finally
        {
            PlayerUIManager.Instance = null;
            if (physicsPlayer != null) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(physicsPlayer, scene);
            if (physicsEnemy != null) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(physicsEnemy, scene);
            EditorSceneManager.ClosePreviewScene(scene);
            if (data != null) UnityEngine.Object.DestroyImmediate(data);
            if (fireData != null) UnityEngine.Object.DestroyImmediate(fireData);
            if (earthData != null) UnityEngine.Object.DestroyImmediate(earthData);
            PlayerUIManager.Instance = previousUI;
            typeof(CombatSettings).GetProperty("Active").SetValue(null, previousCombatSettings);
        }
    }
}

public class WaterDashRegressionTarget : MonoBehaviour, IDamageable
{
    public int Hits;
    public float TotalDamage;
    public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE,
        bool isCritical = false, Vector3 power = default, float poiseDamage = 20f,
        StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
    {
        Hits++;
        TotalDamage += damage;
        return new DamageResult(DamageOutcome.Applied, damage);
    }
}
