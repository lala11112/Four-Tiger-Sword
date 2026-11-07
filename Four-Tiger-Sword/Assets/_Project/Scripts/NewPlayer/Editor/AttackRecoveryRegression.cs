using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Exercises recovery gates and the production attack transition ordering in an isolated fixture.</summary>
public static class AttackRecoveryRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class ProbeForm : BaseForm
    {
        private readonly Dictionary<WeaponActionData, int> _hitCalls = new();
        public int Starts, Ends;
        public float Elapsed => _timer;
        public float Speed => AttackSpeed;
        public ProbeForm(WeaponActionDataSO data) : base(data) { }
        public int HitCalls(WeaponActionData step) => _hitCalls.TryGetValue(step, out int count) ? count : 0;
        public void At(int stage, float elapsed)
        {
            _currentAction = ActionType.Attack;
            _comboStep = stage;
            _timer = elapsed;
            typeof(BaseForm).GetField("_normalAttackActive", Hidden).SetValue(this, true);
        }
        public override void BeginAttack() { Starts++; base.BeginAttack(); }
        public override void EndAttack() { Ends++; base.EndAttack(); }
        protected override void ExecuteHit(WeaponActionData step, float baseDamage, float poiseDamage,
            HashSet<IDamageable> hitTargets, HitEvent hitEvent = null)
        {
            _hitCalls[step] = HitCalls(step) + 1;
            base.ExecuteHit(step, baseDamage, poiseDamage, hitTargets, hitEvent);
        }
    }

    private sealed class NeutralState : IPlayerState
    {
        public void Enter() { }
        public void Update() { }
        public void Exit() { }
    }

    [MenuItem("Tools/Studio Baekho/Combat/Run Attack Recovery Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Attack recovery regression requires Edit Mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        var objects = new List<Object>();
        GameObject host = null;
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Attack recovery: " + message);
            checks++;
        }
        void Near(float actual, float expected, string message)
            => Check(Mathf.Abs(actual - expected) < .0001f, message + $" (actual {actual}, expected {expected})");
        try
        {
            var authored = AssetDatabase.LoadAssetAtPath<FireFormActionDataSO>(
                "Assets/_Project/Scripts/FormActions/FireFormAction.asset");
            Check(authored != null && authored.ComboSteps.Count == 3, "Authored fire combo has three stages.");
            Near(authored.ComboSteps[0].ComboTransitionTime, .5f, "Fire first chain opens at 0.50 seconds.");
            Near(authored.ComboSteps[1].ComboTransitionTime, .35f, "Fire second chain opens at 0.35 seconds.");
            float[] cancelTimes = { .7f, .5f, .65f };
            float[] durations = { 1.4f, 1.4f, 65f / 30f };
            for (int i = 0; i < 3; i++)
            {
                Near(authored.ComboSteps[i].RecoveryCancelTime, cancelTimes[i], $"Fire stage {i + 1} recovery gate.");
                Near(authored.ComboSteps[i].ActionCancelTime, cancelTimes[i], $"Fire stage {i + 1} initializes the independent action gate.");
                Near(authored.ComboSteps[i].Duration, durations[i], $"Fire stage {i + 1} keeps its full no-input duration.");
            }
            Check(new WeaponActionData().RecoveryCancelTime < 0f, "New and legacy attacks default to disabled recovery cancellation.");
            Check(new WeaponActionData().ActionCancelTime < 0f, "Unconfigured attacks default to disabled early action cancellation.");

            var data = ScriptableObject.CreateInstance<FireFormActionDataSO>();
            var statsData = ScriptableObject.CreateInstance<PlayerStatData>();
            objects.Add(data); objects.Add(statsData);
            data.ComboSteps = new List<WeaponActionData>
            {
                Step(1.4f, .5f, .7f, .35f, .06666666f),
                Step(1.4f, .35f, .6f, .15f, .08333333f),
                Step(65f / 30f, 0f, .9f, .43333334f, .06666666f)
            };
            host = new GameObject("__AttackRecoveryRegression");
            host.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
            host.transform.position = new Vector3(12345f, 1000f, 12345f);
            var player = host.AddComponent<PlayerController>();
            var input = host.GetComponent<PlayerInputHandler>();
            var stats = host.GetComponent<PlayerStatManager>();
            stats.Initialize(statsData);
            Property(player, "Controller", host.GetComponent<CharacterController>());
            Property(player, "Input", input);
            Property(player, "StatManager", stats);
            Property(player, "Movement", host.GetComponent<PlayerMovement>());
            player.Movement.Initialize(player);
            player.Gravity = player.VerticalVelocity = 0f;
            Field(player, "_attackTrails", Array.Empty<TrailVfxController>());
            Field(player, "_attackTrailsInitialized", true);
            var animator = host.AddComponent<Animator>();
            var controller = new AnimatorController { name = "Temporary recovery regression controller" };
            objects.Add(controller);
            controller.AddLayer("Base Layer");
            var stateMachine = controller.layers[0].stateMachine;
            objects.Add(stateMachine);
            foreach (string name in new[] { "Idle", "Run" }) objects.Add(stateMachine.AddState(name));
            animator.runtimeAnimatorController = controller;
            Property(player, "Animator", animator);
            var forms = new FormManager(player);
            Property(player, "FormManager", forms);
            var form = new ProbeForm(data);
            forms.ChangeForm(form);
            var machine = new StateMachine();
            Property(player, "StateMachine", machine);
            var neutral = new NeutralState();
            var idle = new PlayerIdleState(player);
            var move = new PlayerRunState(player);
            var attack = new PlayerAttackState(player);
            // Use the production registration, including its order relative to other attack exits.
            typeof(PlayerStateMachineSetup).GetMethod("AttackTransitions", Hidden).Invoke(
                new PlayerStateMachineSetup(player), new object[]
                {
                    machine, idle, move, new PlayerJumpState(player), attack,
                    new PlayerDashState(player), new PlayerSkillState(player), new PlayerUltimateState(player)
                });
            host.SetActive(true);
            animator.Rebind();
            animator.Update(0f);

            void ClearInput()
            {
                input.AttackBuffer.Consume(); input.JumpBuffer.Consume(); input.DashBuffer.Consume();
                input.SkillBuffer.Consume(); input.UltimateBuffer.Consume();
                Property(input, "MoveInput", Vector2.zero);
            }
            void Ready()
            {
                Field(player, "_finisherAttack", null);
                machine.ChangeState(neutral);
                ClearInput();
                stats.Initialize(statsData);
                machine.ChangeState(attack);
            }
            void TickAt(int stage, float elapsed)
            {
                form.At(stage, elapsed - player.ActionDeltaTime * form.Speed);
                attack.Update();
            }

            Ready();
            data.ComboSteps[0].RecoveryCancelTime = -1f;
            form.At(0, 1f);
            Check(!form.CanCancelAttackRecovery && !attack.CanCancelToMove,
                "Disabled recovery gate retains the legacy attack lock.");
            data.ComboSteps[0].RecoveryCancelTime = .7f;
            form.At(0, .699f);
            Check(!form.CanCancelAttackRecovery, "Movement stays locked before the recovery boundary.");
            form.At(0, .7f);
            Check(form.CanCancelAttackRecovery && !form.CanRestartNormalCombo,
                "Movement opens at the boundary without restarting an unfinished combo.");
            data.ComboSteps[0].RecoveryCancelTime = .1f;
            form.At(0, .4f);
            Check(!form.CanCancelAttackRecovery, "An early configured gate cannot cut off a live single hit.");
            form.At(0, .35f + .06666666f);
            Check(form.CanCancelAttackRecovery, "Single-hit recovery can open at its exact final hit end.");
            data.ComboSteps[0].HitEvents = new List<HitEvent>
            {
                new HitEvent { StartTime = .8f, Duration = .1f },
                new HitEvent { StartTime = .2f, Duration = .1f }
            };
            form.At(0, .899f);
            Check(!form.CanCancelAttackRecovery, "Unsorted multi-hit recovery waits for the latest hit end.");
            form.At(0, .8f + .1f);
            Check(form.CanCancelAttackRecovery, "Multi-hit recovery opens once its final hit is over.");
            data.ComboSteps[0].HitEvents.Clear();
            data.ComboSteps[0].RecoveryCancelTime = .7f;

            Ready();
            int firstHitCalls = form.HitCalls(data.ComboSteps[0]);
            int secondHitCalls = form.HitCalls(data.ComboSteps[1]);
            input.AttackBuffer.Set(.5f);
            input.AttackBuffer.Update(.2f);
            TickAt(0, .49f);
            Check(form.CurrentComboStage == 0 && input.AttackBuffer.IsActive,
                "A valid early click waits in the existing input buffer before the chain window.");
            TickAt(0, .501f);
            Check(form.CurrentComboStage == 1 && !input.AttackBuffer.IsActive,
                "The buffered click advances exactly once when the chain window opens.");
            Check(form.HitCalls(data.ComboSteps[0]) == firstHitCalls + 1,
                "Buffered first-to-second chaining dispatches the first hit exactly once.");
            TickAt(1, .7f);
            Check(form.CurrentComboStage == 1, "A consumed click does not automatically advance another stage.");
            input.AttackBuffer.Set(.5f);
            TickAt(1, .701f);
            Check(form.CurrentComboStage == 2 && !input.AttackBuffer.IsActive,
                "A click after the second window opens advances immediately and is consumed.");
            Check(form.HitCalls(data.ComboSteps[1]) == secondHitCalls + 1,
                "Late second-to-third chaining dispatches the second hit exactly once.");

            Ready();
            input.AttackBuffer.Set(.5f);
            input.AttackBuffer.Update(.501f);
            TickAt(0, .8f);
            Check(form.CurrentComboStage == 0, "An expired click cannot advance a combo.");
            Property(input, "MoveInput", Vector2.up);
            form.At(0, .699f);
            machine.Update(true, false);
            Check(ReferenceEquals(machine.CurrentState, attack), "Held movement cannot leave before the recovery gate.");
            form.At(0, .7f);
            machine.Update(true, false);
            Check(machine.CurrentState is PlayerRunState && form.CurrentComboStage == -1,
                "The same held movement enters Run at the gate and cleans up the old attack.");

            Ready();
            Property(input, "MoveInput", Vector2.up);
            form.At(0, .8f);
            input.AttackBuffer.Set(.5f);
            Check(!attack.CanCancelToMove, "Executable buffered attack has priority over held movement.");
            machine.Update(true, false);
            Check(ReferenceEquals(machine.CurrentState, attack), "Production movement transition preserves the simultaneous attack.");
            attack.Update();
            Check(form.CurrentComboStage == 1 && !input.AttackBuffer.IsActive,
                "Simultaneous attack and movement executes only the next attack.");

            Ready();
            form.At(2, .9f);
            Invoke(attack, "BeginFinisherSelection", Vector3.forward);
            Field(player, "_finisherAttack", attack);
            Check(!attack.CanCancelToMove, "An open finisher selection blocks recovery movement.");
            Field(player, "_finisherAttack", null);
            int starts = form.Starts, ends = form.Ends;
            input.AttackBuffer.Set(.5f);
            Property(input, "MoveInput", Vector2.up);
            Check(!attack.CanCancelToMove, "Third-stage restart has priority over movement.");
            attack.Update();
            Check(form.CurrentComboStage == 0 && form.Starts == starts + 1 && form.Ends == ends + 1,
                "Third-stage recovery ends the old action and starts one fresh normal combo.");
            Check(!input.AttackBuffer.IsActive && !attack.IsAttackComplete,
                "Restart consumes its reservation without marking the fresh attack complete.");
            form.At(1, .6f);
            Check(attack.CanSelectFinisher, "A fresh combo resets the previous finisher-selection-open flag.");
            Property(input, "MoveInput", Vector2.zero);
            TickAt(2, 1f);
            Check(form.CurrentComboStage == 2, "Third-stage recovery never restarts without another click.");

            Ready();
            form.At(1, .65f);
            Check((bool)Invoke(attack, "CommitFinisher", form, Vector3.forward), "Normal terminal finisher can begin.");
            form.At(2, 1f);
            Check(!form.CanCancelAttackRecovery && !form.CanRestartNormalCombo && !attack.CanCancelToMove,
                "Committed finisher execution excludes the new normal-attack recovery rules.");

            Ready();
            form.At(0, 1f);
            TickAt(0, 1f);
            machine.Update(true, false);
            Check(!attack.IsAttackComplete && ReferenceEquals(machine.CurrentState, attack),
                "An open recovery gate does not finish an attack when no input is present.");
            TickAt(0, 1.401f);
            machine.Update(true, false);
            Check(machine.CurrentState is PlayerIdleState, "No-input playback still returns to Idle at its original duration.");

            Ready();
            form.At(0, .8f);
            Field(attack, "_pending", true);
            Check(!attack.CanCancelToMove, "Pending tap/hold decisions cannot be movement-canceled.");
            Field(attack, "_pending", false);
            Field(attack, "_heavyStarted", true);
            Check(!attack.CanCancelToMove, "Heavy attack execution cannot use normal recovery movement.");
            Field(attack, "_heavyStarted", false);
            stats.AddModifier(StatType.ST_ATK_SPD, 0f, 1f);
            form.At(0, .1f);
            float expectedElapsed = form.Elapsed + player.ActionDeltaTime * form.Speed;
            attack.Update();
            Near(form.Elapsed, expectedElapsed, "Recovery shares the existing attack-speed-scaled action clock.");
            var clock = CombatActorClock.GetOrCreate(player);
            Property(player, "ActorClock", clock);
            clock.Request(host, 60f, 0f, HitStopStacking.Replace);
            Check(player.ActionDeltaTime == 0f, "Fixture actor hit-stop freezes its action delta.");
            form.At(0, .699f);
            input.AttackBuffer.Set(.5f);
            Property(input, "MoveInput", Vector2.up);
            machine.Update(player.ActionDeltaTime > 0f, player.ActionDeltaTime > 0f);
            Near(form.Elapsed, .699f, "Stopped actor cannot advance into its recovery window.");
            Check(ReferenceEquals(machine.CurrentState, attack) && input.AttackBuffer.IsActive,
                "Hit-stop preserves the buffered click without running a recovery transition.");
            clock.ClearStops("regression-complete");

            return $"PASS: attack recovery ({checks} checks: authored timing, opt-in compatibility, single/multi-hit boundaries, buffered and late clicks, movement priority, terminal restart and cleanup, no-input playback, finisher isolation and reset, attack speed and actor hit-stop).";
        }
        finally
        {
            if (host != null) Object.DestroyImmediate(host);
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static WeaponActionData Step(float duration, float chain, float recovery, float hitStart, float hitDuration)
        => new WeaponActionData
        {
            AnimationName = "", Duration = duration, ComboTransitionTime = chain, RecoveryCancelTime = recovery,
            ActionCancelTime = recovery,
            HitStartTime = hitStart, HitDuration = hitDuration, UseTargetApproach = false,
            ForwardDistance = 0f, ThrustMultiplier = 0f, DisableTrail = true
        };
    private static void Field(object target, string name, object value)
        => target.GetType().GetField(name, Hidden).SetValue(target, value);
    private static void Property(object target, string name, object value)
        => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] arguments)
        => target.GetType().GetMethod(name, Hidden).Invoke(target, arguments);
}
