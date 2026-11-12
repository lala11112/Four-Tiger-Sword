using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Production damage, reaction and recovery transitions in an isolated preview scene.</summary>
public static class PlayerHitReactionRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class NeutralState : IPlayerState
    {
        public void Enter() { }
        public void Update() { }
        public void Exit() { }
    }

    private sealed class ProbeForm : BaseForm
    {
        public bool Block;
        public int SkillExits { get; private set; }
        public override ElementType Element { get; }
        public ProbeForm(WeaponActionDataSO data, ElementType element = ElementType.ELEMENT_NONE) : base(data)
            => Element = element;
        public override void EndSkill() { SkillExits++; base.EndSkill(); }
        public override void EndSwitchAttack() { SkillExits++; base.EndSwitchAttack(); }
        public override void EndUltimate() { SkillExits++; base.EndUltimate(); }
        public override void BeginAirAttack() { }
        public override void EndAirAttack() { }
        public override void BeginParry() { }
        public override void EndParry() { }
        public override bool BlocksIncomingDamage(float damage, Vector3 incomingPower, object source = null) => Block;
    }

    private sealed class ProbeAttack : EnemyAction
    {
        public ProbeAttack(MonsterSkillData data) : base(data, null) { }
    }

    [MenuItem("Tools/Studio Baekho/Combat/Run Player Hit Reaction Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Player hit reaction regression requires Edit Mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        var objects = new List<Object>();
        var telemetry = new List<CombatFeedbackTelemetryEvent>();
        var oldServices = new HashSet<CombatHitStopService>(Resources.FindObjectsOfTypeAll<CombatHitStopService>());
        float worldScale = Time.timeScale;
        GameObject host = null;
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Player hit reaction: " + message);
            checks++;
        }
        void Near(float actual, float expected, string message, float epsilon = .0001f)
            => Check(Mathf.Abs(actual - expected) <= epsilon, message + $" (actual {actual}, expected {expected})");
        CombatFeedbackTelemetry.Recorded += telemetry.Add;
        try
        {
            var statsData = ScriptableObject.CreateInstance<PlayerStatData>();
            var formData = ScriptableObject.CreateInstance<FireFormActionDataSO>();
            var waterData = ScriptableObject.CreateInstance<WaterFormActionDataSO>();
            var normalData = ScriptableObject.CreateInstance<MonsterDefaultAttackSO>();
            var downData = ScriptableObject.CreateInstance<MonsterDefaultAttackSO>();
            var comboData = ScriptableObject.CreateInstance<MonsterComboAttackSO>();
            objects.AddRange(new Object[] { statsData, formData, waterData, normalData, downData, comboData });
            statsData.baseHP = 10000f;
            formData.ComboSteps = new List<WeaponActionData> { ActionStep() };
            formData.SkillSteps = new List<WeaponActionData> { ActionStep() };
            formData.UltimateSteps = new List<WeaponActionData> { ActionStep() };
            formData.SwitchAttackSteps.Add(ActionStep());
            waterData.HeavyAttackStep = ActionStep("Heavy");
            waterData.UltimateSteps = new List<WeaponActionData> { ActionStep("WaterUltimate") };
            downData.playerHitReaction = EnemyPlayerHitReaction.Knockdown;
            var normal = new ProbeAttack(normalData);
            var strong = new ProbeAttack(downData);
            Check(!normal.TryGetPlayerKnockdown(out _, out _), "New attacks default to an ordinary reaction.");
            Check(strong.TryGetPlayerKnockdown(out float downSeconds, out float getUpSeconds),
                "Common attack metadata reaches an EnemyAction without a combo wrapper.");
            Near(downSeconds, .3f, "Default down interval.");
            Near(getUpSeconds, .5f, "Default get-up interval.");

            var combo = new EnemyComboAttack(comboData, null);
            comboData.playerHitReaction = EnemyPlayerHitReaction.Knockdown;
            Field(combo, "_executingHit", new EnemyHitEvent { overrideReaction = true, knockdown = false });
            Check(!combo.TryGetPlayerKnockdown(out _, out _), "An explicit ordinary combo hit overrides common down.");
            Field(combo, "_executingHit", new EnemyHitEvent { knockdown = true });
            Check(combo.TryGetPlayerKnockdown(out downSeconds, out getUpSeconds), "Legacy per-hit down remains effective.");
            Near(downSeconds + getUpSeconds, .8f, "Per-hit default recovery totals 0.8 seconds.");
            Field(combo, "_executingHit", null);

            foreach (string path in new[]
            {
                "Assets/_Project/Generated/Bosses/SwordRobotBoss/Charge_Phase1.asset",
                "Assets/_Project/Generated/Bosses/SwordRobotBoss/Charge_Phase2.asset",
                "Assets/_Project/Scripts/Enemy/EnemyActions/SO/JumpSlam.asset",
                "Assets/_Project/Scripts/Enemy/EnemyActions/SO/Drone/Drone_SelfDestruct.asset"
            })
            {
                var data = AssetDatabase.LoadAssetAtPath<MonsterSkillData>(path);
                Check(data != null && data.playerHitReaction == EnemyPlayerHitReaction.Knockdown,
                    "The designated heavy attack is authored as down: " + path);
                Near(data.playerDownSeconds + data.playerGetUpSeconds, .8f, "Authored heavy recovery: " + path);
            }
            foreach (string phase in new[] { "1", "2" })
            {
                var data = AssetDatabase.LoadAssetAtPath<MonsterComboAttackSO>(
                    "Assets/_Project/Generated/Bosses/SwordRobotBoss/Combo_Phase" + phase + ".asset");
                Check(data != null && data.hits.Count == 3 && !data.hits[0].knockdown
                    && !data.hits[1].knockdown && data.hits[2].knockdown,
                    "Only the robot combo's final swing downs in phase " + phase);
                Near(data.hits[2].downSeconds + data.hits[2].getUpSeconds, .8f, "Robot combo down recovery.");
            }

            host = new GameObject("__PlayerHitReactionRegression");
            host.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
            host.transform.position = new Vector3(100f, 500f, 100f);
            new GameObject("Pose").transform.SetParent(host.transform, false);
            var player = host.AddComponent<PlayerController>();
            var input = host.GetComponent<PlayerInputHandler>();
            var stats = host.GetComponent<PlayerStatManager>();
            Property(player, "Controller", host.GetComponent<CharacterController>());
            Property(player, "Input", input);
            Property(player, "StatManager", stats);
            Property(player, "Movement", host.GetComponent<PlayerMovement>());
            player.Movement.Initialize(player);
            player.Controller.minMoveDistance = 0f;
            player.Gravity = player.VerticalVelocity = 0f;
            Field(player, "_attackTrails", Array.Empty<TrailVfxController>());
            Field(player, "_attackTrailsInitialized", true);
            Field(player, "_hitStopDuration", 0f);
            var animator = host.AddComponent<Animator>();
            var controller = new AnimatorController { name = "Temporary player hit regression controller" };
            objects.Add(controller);
            controller.AddLayer("Base Layer");
            var animationStates = controller.layers[0].stateMachine;
            objects.Add(animationStates);
            foreach (string name in new[] { "Idle", "Run", "Hit", "Knockdown", "GetUp", "Dash", "Backstep", "Die", "Heavy", "WaterUltimate" })
            {
                var clip = new AnimationClip { name = name };
                float length = name == "WaterUltimate" ? 4f : 1f;
                clip.SetCurve("Pose", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, length, 0f));
                var state = animationStates.AddState(name);
                state.motion = clip;
                objects.Add(clip); objects.Add(state);
            }
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Property(player, "Animator", animator);
            var forms = new FormManager(player);
            var form = new ProbeForm(formData);
            var chainForm = new ProbeForm(formData, ElementType.ELEMENT_FIRE);
            var water = new WaterForm(waterData);
            forms.AddTransition(chainForm, () => false);
            Property(player, "FormManager", forms);
            forms.ChangeForm(form);
            var impulse = host.AddComponent<CinemachineImpulseSource>();
            impulse.DefaultVelocity = Vector3.down;
            impulse.ImpulseDefinition.ImpulseChannel = 1 << 30;
            player.ImpulseSource = impulse;
            stats.Initialize(statsData);
            host.SetActive(true);
            animator.Rebind(); animator.Update(0f);
            var machine = new PlayerStateMachineSetup(player).Build();
            Property(player, "StateMachine", machine);
            var neutral = new NeutralState();

            int Shakes() => telemetry.Count(e => e.kind == "player-hit-shake" && e.ownerId == host.GetInstanceID());
            void ClearInput()
            {
                input.AttackBuffer.Consume(); input.SkillBuffer.Consume(); input.UltimateBuffer.Consume();
                input.JumpBuffer.Consume(); input.DashBuffer.Consume(); input.ChainSkillBuffer.Consume();
                input.ConsumeFormInput();
                Property(input, "MoveInput", Vector2.zero);
            }
            void Ready()
            {
                machine.ChangeState(neutral);
                player.ConsumeHitReaction(); player.StopParryKnockback(); player.CancelHitStops();
                forms.ChangeForm(form);
                stats.Initialize(statsData);
                player.Controller.enabled = input.enabled = player.Movement.enabled = true;
                Field(player, "_dashCooldownTimer", 0f);
                player.HitStaggerResistance = StaggerResistLevel.NONE;
                player.Gravity = player.VerticalVelocity = 0f;
                form.Block = false;
                ClearInput();
                machine.ChangeState(new PlayerIdleState(player));
            }
            DamageResult Damage(EnemyAction source, Vector3 power = default, float amount = 1f,
                StaggerResistLevel level = StaggerResistLevel.LOW, float poise = 20f)
                => player.TakeDamage(amount, power: power, poiseDamage: poise, staggerResistLevel: level, source: source);
            PlayerHitState EnterHit(EnemyAction source, Vector3 power = default)
            {
                Check(Damage(source, power).Applied, "The fixture attack applies actual damage.");
                machine.Update(false, false);
                Check(machine.CurrentState is PlayerHitState, "The production any-transition enters a reaction.");
                return (PlayerHitState)machine.CurrentState;
            }

            Near(player.HitDuration, .2f, "Ordinary control lock defaults to 0.2 seconds.");
            Near(player.KnockbackDuration, .12f, "Push still ends after 0.12 seconds.");
            Near(player.HitRecoveryInputBufferWindow, .05f, "Late-input window defaults to 0.05 seconds.");
            foreach (float poise in new[] { 0f, 20f })
            {
                Ready();
                player.TakeDamage(1f, poiseDamage: poise, staggerResistLevel: StaggerResistLevel.HIGH, source: normal);
                Check(player.PendingHitReaction == (poise > 0f ? PlayerHitReaction.Stagger : PlayerHitReaction.Hit),
                    "A high stagger grade remains ordinary without an authored down flag.");
                machine.Update(false, false);
                var hit = (PlayerHitState)machine.CurrentState;
                Tick(hit, .2f);
                Check(hit.IsComplete, "Both unpushed hit and stagger release at 0.2 seconds.");
            }
            foreach (int fps in new[] { 30, 60, 120 })
            {
                foreach (bool down in new[] { false, true })
                {
                    Ready();
                    float dt = 1f / fps;
                    float duration = down ? .8f : .2f;
                    var hit = EnterHit(down ? strong : normal, Vector3.right * 8f);
                    Check(down ? hit is PlayerKnockdownState : hit is PlayerKnockbackState,
                        "Explicit strength selects the expected reaction at " + fps + " FPS.");
                    Vector3 start = host.transform.position;
                    float elapsed = 0f;
                    int frames = 0;
                    while (!hit.IsComplete && frames++ < fps * 2)
                    {
                        Tick(hit, dt); elapsed += dt;
                        if (down && elapsed >= .3f && elapsed < .8f)
                            Check(((PlayerKnockdownState)hit).IsGettingUp, "Down advances into get-up at " + fps + " FPS.");
                    }
                    Check(hit.IsComplete && elapsed + .00001f >= duration && elapsed <= duration + dt + .00001f,
                        "Control recovery stays within one frame at " + fps + " FPS.");
                    float pushSeconds = down ? player.KnockbackDuration : Mathf.Min(player.HitDuration, player.KnockbackDuration);
                    float expectedDistance = KnockbackMotion.Velocity(Vector3.right * 8f, 3f).magnitude * pushSeconds * .5f;
                    Near(host.transform.position.x - start.x, expectedDistance,
                        "Integrated push is frame-rate independent at " + fps + " FPS.", .002f);
                    Vector3 end = host.transform.position;
                    Tick(hit, dt);
                    Near(Vector3.Distance(host.transform.position, end), 0f, "No residual push after recovery.", .0001f);
                    machine.Update(true, false);
                    Check(!(machine.CurrentState is PlayerHitState), "Completed recovery leaves on the next transition pass.");
                    Near(animator.speed, 1f, "Recovery exit restores animation speed.");
                }
            }

            Ready();
            int beforeShake = Shakes();
            EnterHit(normal);
            Check(Shakes() == beforeShake + 1, "One applied ordinary hit emits one shake.");
            Near(telemetry.Last(e => e.kind == "player-hit-shake").value, .15f, "Ordinary shake strength.");
            Ready();
            beforeShake = Shakes();
            var downState = (PlayerKnockdownState)EnterHit(strong, Vector3.right * 8f);
            Near(telemetry.Last(e => e.kind == "player-hit-shake").value, .25f, "Down shake is stronger.");
            Tick(downState, .2f);
            float hp = stats.CurrentHp, elapsedBefore = (float)Field(downState, "Elapsed");
            Vector3 pushBefore = (Vector3)Field(downState, "HitVelocity");
            Damage(normal, Vector3.left * 20f);
            Damage(strong, Vector3.left * 30f);
            Near(stats.CurrentHp, hp - 2f, "Down remains vulnerable to follow-up attacks.");
            Near((float)Field(downState, "Elapsed"), elapsedBefore, "Neither follow-up restarts the down timer.");
            Check(player.PendingHitReaction == PlayerHitReaction.None
                && (Vector3)Field(downState, "HitVelocity") == pushBefore, "Follow-ups cannot replace the down or push.");
            Tick(downState, .2f);
            Check(downState.IsGettingUp, "The original get-up interval still starts after follow-ups.");
            Damage(strong);
            Tick(downState, .2f);
            Check(!downState.IsComplete && downState.IsGettingUp, "The original get-up remains committed before 0.8 seconds.");
            Tick(downState, .2f);
            Check(downState.IsComplete && Shakes() == beforeShake + 4,
                "Get-up follow-up preserves the original 0.8-second recovery and still shakes.");

            Ready();
            input.AttackBuffer.Set(); input.SkillBuffer.Set(); input.UltimateBuffer.Set();
            input.JumpBuffer.Set(); input.DashBuffer.Set(); input.ChainSkillBuffer.Set();
            Property(input, "IsForm1Pressed", true);
            var buffered = EnterHit(normal);
            Check(!input.AttackBuffer.IsActive && !input.SkillBuffer.IsActive && !input.UltimateBuffer.IsActive
                && !input.JumpBuffer.IsActive && !input.DashBuffer.IsActive && !input.ChainSkillBuffer.IsActive,
                "Reaction entry discards all pre-hit reservations.");
            Check(!input.IsForm1Pressed, "Reaction entry discards stale form input.");
            input.AttackBuffer.Set(); Tick(buffered, .02f);
            Check(!input.AttackBuffer.IsActive, "An early recovery input is discarded.");
            Tick(buffered, .15f);
            input.AttackBuffer.Set(); Tick(buffered, .03001f);
            Check(buffered.IsComplete && input.AttackBuffer.IsActive, "A fresh input in the final 0.05 seconds survives.");
            machine.Update(true, false);
            Check(machine.CurrentState is PlayerAirAttackState && !input.AttackBuffer.IsActive,
                "Buffered aerial attack executes directly on recovery without an intermediate locomotion frame.");

            Ready();
            player.HitStaggerResistance = StaggerResistLevel.SUPER_ARMOR;
            beforeShake = Shakes();
            Damage(strong, Vector3.right * 8f);
            Check(player.PendingHitReaction == PlayerHitReaction.None && Shakes() == beforeShake + 1,
                "Explicit armor suppresses the reaction while preserving actual-hit feedback.");
            Ready();
            beforeShake = Shakes(); hp = stats.CurrentHp;
            Check(!Damage(strong, amount: 0f).Killed && Shakes() == beforeShake,
                "Zero applied damage has no shake.");
            form.Block = true;
            Check(Damage(strong).Outcome == DamageOutcome.Blocked && Shakes() == beforeShake,
                "Blocked damage has no incoming-hit shake.");
            form.Block = false;
            machine.ChangeState(new PlayerDashState(player));
            Check(!Damage(strong).Applied && Shakes() == beforeShake && stats.CurrentHp == hp,
                "Dodge prevents damage, down and incoming-hit shake.");
            Ready();
            var parry = new PlayerParryState(player);
            machine.ChangeState(parry);
            Property(parry, "ParriedHitCount", 1);
            Field(parry, "_lockedSource", strong);
            beforeShake = Shakes(); hp = stats.CurrentHp;
            Check(Damage(strong).Outcome == DamageOutcome.Parried && stats.CurrentHp == hp
                && Shakes() == beforeShake && !player.IsKnockedDown,
                "Parry retains its own feedback without an incoming-hit shake or down.");
            Ready();
            stats.GrantShield(new object(), 10f, 30f);
            beforeShake = Shakes(); hp = stats.CurrentHp;
            var shielded = Damage(normal);
            Check(shielded.ShieldDamage > 0f && stats.CurrentHp == hp && Shakes() == beforeShake + 1,
                "A shield-absorbed hit produces one incoming-hit shake.");

            void StartSkill()
            {
                Field(form, "_skillCooldownTimer", 0f);
                input.SkillBuffer.Set();
                machine.Update(true, false);
                Check(machine.CurrentState is PlayerSkillState && form.SkillCooldownRemaining == 0f,
                    "E starts an authored skill and defers its cooldown until exit.");
            }

            foreach (string action in new[] { "E", "F", "R" })
            {
                Ready();
                switch (action)
                {
                    case "E": StartSkill(); break;
                    case "F":
                        forms.NotifyFinalComboHit(form.Element);
                        input.ChainSkillBuffer.Set();
                        machine.Update(true, false);
                        Check(machine.CurrentState is PlayerSwitchAttackState chain && !chain.IsComplete
                            && forms.CurrentForm == chainForm && forms.GetChainReadyRemaining(chainForm.Element) == 0f,
                            "F consumes a ready chain and starts its dedicated attack.");
                        break;
                    case "R":
                        stats.AddUltimateGauge(stats.MaxUltimateGauge);
                        input.UltimateBuffer.Set();
                        machine.Update(true, false);
                        Check(machine.CurrentState is PlayerUltimateState && stats.CurrentUltimateGauge == 0f
                            && form.UltimateCooldownRemaining == form.UltimateCooldownDuration,
                            "R starts its authored action and commits the gauge and cooldown.");
                        break;
                }

                var cast = machine.CurrentState;
                var castingForm = (ProbeForm)forms.CurrentForm;
                int exits = castingForm.SkillExits;
                hp = stats.CurrentHp;
                beforeShake = Shakes();
                Check(Damage(normal, poise: 0f).HealthDamage == 1f, action + " still takes ordinary HP damage.");
                Check(Damage(normal, level: StaggerResistLevel.HIGH).HealthDamage == 1f,
                    action + " still takes HP damage from a high stagger hit.");
                stats.GrantShield(new object(), 2f, 30f);
                var pushed = Damage(normal, Vector3.right * 8f, level: StaggerResistLevel.HIGH);
                Check(pushed.ShieldDamage == 1f && stats.CurrentHp == hp - 2f && Shakes() == beforeShake + 3,
                    action + " consumes shield and shows feedback for an ordinary knockback hit.");
                machine.Update(false, false);
                Check(player.PendingHitReaction == PlayerHitReaction.None && machine.CurrentState == cast
                    && castingForm.SkillExits == exits,
                    action + " continues through ordinary hit, stagger, and knockback.");

                Check(Damage(strong, Vector3.right * 8f).ShieldDamage == 1f
                    && player.PendingHitReaction == PlayerHitReaction.Knockdown,
                    action + " can be interrupted by a down attack absorbed by the shield.");
                Damage(normal, Vector3.left * 16f);
                Check(player.PendingHitReaction == PlayerHitReaction.Knockdown && Shakes() == beforeShake + 5,
                    action + " retains the pending down through a same-frame ordinary hit.");
                machine.Update(false, false);
                Check(machine.CurrentState is PlayerKnockdownState && castingForm.SkillExits == exits + 1,
                    action + " enters knockdown and runs its normal exit once.");
                if (action == "E")
                    Near(form.SkillCooldownRemaining, form.SkillCooldownDuration, "Interrupted E starts cooldown.");
                if (action == "F")
                    Check(chainForm.SkillCooldownRemaining == 0f && forms.GetChainReadyRemaining(chainForm.Element) == 0f,
                        "Interrupted F leaves the chain spent without starting an E cooldown.");
                if (action == "R")
                    Check(stats.CurrentUltimateGauge == 0f && form.UltimateCooldownRemaining == form.UltimateCooldownDuration,
                        "Interrupted R leaves the committed gauge and cooldown in place.");
            }

            Ready();
            StartSkill();
            Field(form, "_timer", formData.SkillSteps[0].Duration);
            machine.Update(false, true);
            machine.Update(true, false);
            Check(machine.CurrentState is PlayerIdleState && EnterHit(normal) is PlayerStaggerState,
                "Ordinary hits can interrupt again after E completes.");

            Ready();
            input.AttackBuffer.Set();
            machine.Update(true, false);
            Check(machine.CurrentState is PlayerAttackState attack && !attack.IsHeavyAttack
                && EnterHit(normal, Vector3.right * 8f) is PlayerKnockbackState,
                "An authored normal attack can still be interrupted by ordinary knockback.");

            Ready();
            forms.ChangeForm(water);
            int originalExclusions = player.Controller.excludeLayers;
            ((WaterFlowGauge)Field(water, "_flow")).Charge(water.WaterGaugePerSegment);
            Property(input, "IsAttackHeld", true);
            Property(input, "AttackHeldTime", water.HeavyAttackHoldTime);
            input.AttackBuffer.Set();
            machine.Update(true, false);
            machine.Update(false, true);
            Check(machine.CurrentState is PlayerAttackState heavy && heavy.IsHeavyAttack,
                "The held attack input enters the Water heavy attack.");
            Check(EnterHit(normal, Vector3.right * 8f) is PlayerKnockbackState
                && player.Controller.excludeLayers == originalExclusions,
                "Ordinary knockback still interrupts a heavy attack and restores its collision exclusions.");
            Property(input, "IsAttackHeld", false);
            Property(input, "AttackHeldTime", 0f);

            Ready();
            forms.ChangeForm(water);
            var waterRenderer = host.AddComponent<MeshRenderer>();
            stats.AddUltimateGauge(stats.MaxUltimateGauge);
            input.UltimateBuffer.Set();
            machine.Update(true, false);
            beforeShake = Shakes(); hp = stats.CurrentHp;
            Check(machine.CurrentState is PlayerUltimateState && !waterRenderer.enabled
                && Damage(normal).Outcome == DamageOutcome.Blocked && Damage(strong).Outcome == DamageOutcome.Blocked
                && stats.CurrentHp == hp && player.PendingHitReaction == PlayerHitReaction.None && Shakes() == beforeShake,
                "Water R retains its existing damage block through the ten-hit execution.");
            animator.Update(.02f);
            var advance = new object[] { 2.01f, false };
            typeof(WaterForm).GetMethod("AdvanceUltimate", Hidden).Invoke(water, advance);
            Check(water.IsCurrentActionExecutionComplete && !(bool)advance[1] && waterRenderer.enabled,
                "Water R finishes its execution and restores visibility before the animation ends.");
            Check(Damage(normal).HealthDamage == 1f && player.PendingHitReaction == PlayerHitReaction.None,
                "The Water R animation tail takes ordinary damage without interruption.");
            Check(EnterHit(strong) is PlayerKnockdownState && waterRenderer.enabled && stats.CurrentUltimateGauge == 0f,
                "A down attack interrupts the vulnerable Water R tail and leaves its cleanup and spent gauge intact.");

            Ready();
            StartSkill();
            beforeShake = Shakes();
            var lethal = Damage(strong, amount: 100000f);
            Check(lethal.Killed && Shakes() == beforeShake + 1 && player.PendingHitReaction == PlayerHitReaction.None,
                "A lethal hit shakes once without queuing a living reaction.");
            machine.Update(false, false);
            Check(machine.CurrentState is PlayerDieState, "Death wins over knockdown and skill armor on the transition pass.");
            return $"PASS: player hit reaction ({checks} checks: authored strength, 30/60/120 FPS recovery and movement, down follow-ups, input reservation, shake, skill armor, Water ultimate protection, zero, block, dodge, parry, shields and death).";
        }
        finally
        {
            CombatFeedbackTelemetry.Recorded -= telemetry.Add;
            if (host != null) Object.DestroyImmediate(host);
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var service in Resources.FindObjectsOfTypeAll<CombatHitStopService>())
                if (!oldServices.Contains(service)) Object.DestroyImmediate(service.gameObject);
            Time.timeScale = worldScale;
        }
    }

    private static WeaponActionData ActionStep(string animation = "") => new WeaponActionData
    {
        AnimationName = animation, Duration = 1f, HitStartTime = .2f, HitDuration = .1f, DisableTrail = true
    };

    private static void Tick(PlayerHitState state, float deltaTime)
        => state.GetType().GetMethod("UpdateReaction", Hidden).Invoke(state, new object[] { deltaTime });

    private static FieldInfo FindField(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Hidden | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException(target.GetType().FullName, name);
    }
    private static object Field(object target, string name) => FindField(target, name).GetValue(target);
    private static void Field(object target, string name, object value) => FindField(target, name).SetValue(target, value);
    private static void Property(object target, string name, object value)
        => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);
}
