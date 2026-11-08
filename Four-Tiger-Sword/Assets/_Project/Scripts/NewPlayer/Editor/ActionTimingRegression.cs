using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Exercises independent action exits and real Animator playback in an isolated fixture.</summary>
public static class ActionTimingRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class ProbeForm : BaseForm
    {
        public int AttackStarts, AttackEnds, SkillStarts, SkillEnds, UltimateStarts, UltimateEnds, HitCalls;
        public override float SkillSpCost => 10f;
        public override float SkillCooldown => 3f;
        public float Speed => AttackSpeed;
        public float Elapsed => _timer;
        public ProbeForm(WeaponActionDataSO data) : base(data) { }
        public void AttackAt(int stage, float time) { _comboStep = stage; _timer = time; }
        public void SkillAt(int stage, float time) { _skillStep = stage; _timer = time; }
        public void UltimateAt(int stage, float time) { _ultimateStep = stage; _timer = time; }
        public override void BeginAttack() { AttackStarts++; base.BeginAttack(); }
        public override void EndAttack() { AttackEnds++; base.EndAttack(); }
        public override void BeginSkill() { SkillStarts++; base.BeginSkill(); }
        public override void EndSkill() { SkillEnds++; base.EndSkill(); }
        public override void BeginUltimate() { UltimateStarts++; base.BeginUltimate(); }
        public override void EndUltimate() { UltimateEnds++; base.EndUltimate(); }
        protected override void ExecuteHit(WeaponActionData step, float damage, float poiseDamage,
            HashSet<IDamageable> targets, HitEvent hitEvent = null) => HitCalls++;
    }

    private sealed class NeutralState : IPlayerState
    {
        public void Enter() { }
        public void Update() { }
        public void Exit() { }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly UnityEngine.SceneManagement.Scene _scene = EditorSceneManager.NewPreviewScene();
        private UnityEngine.SceneManagement.Scene _physicsScene;
        private readonly List<Object> _objects = new();
        private readonly NeutralState _neutral = new();
        public readonly GameObject Host;
        public readonly PlayerController Player;
        public readonly PlayerInputHandler Input;
        public readonly PlayerStatManager Stats;
        public readonly PlayerStatData StatsData;
        public readonly FireFormActionDataSO Data;
        public readonly ProbeForm Form;
        public readonly Animator Animator;
        public readonly FormManager Forms;
        public readonly StateMachine Machine;
        public readonly PlayerAttackState Attack;
        public readonly PlayerSkillState Skill;
        public readonly PlayerUltimateState Ultimate;

        public Fixture()
        {
            StatsData = Track(ScriptableObject.CreateInstance<PlayerStatData>());
            Data = Track(ScriptableObject.CreateInstance<FireFormActionDataSO>());
            StandardData();
            Host = new GameObject("__ActionTimingRegression");
            Host.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(Host, _scene);
            Host.transform.position = new Vector3(12500f, 1100f, 12500f);
            new GameObject("Pose").transform.SetParent(Host.transform, false);
            Player = Host.AddComponent<PlayerController>();
            Input = Host.GetComponent<PlayerInputHandler>();
            Stats = Host.GetComponent<PlayerStatManager>();
            Stats.Initialize(StatsData);
            Property(Player, "Controller", Host.GetComponent<CharacterController>());
            Property(Player, "Input", Input);
            SetField(Input, "_player", Player);
            Property(Player, "StatManager", Stats);
            Property(Player, "Movement", Host.GetComponent<PlayerMovement>());
            Player.Movement.Initialize(Player);
            Player.Gravity = Player.VerticalVelocity = 0f;
            SetField(Player, "_attackTrails", Array.Empty<TrailVfxController>());
            SetField(Player, "_attackTrailsInitialized", true);
            Animator = Host.AddComponent<Animator>();
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var controller = Track(new AnimatorController { name = "Temporary action timing controller" });
            controller.AddLayer("Base Layer");
            var states = Track(controller.layers[0].stateMachine);
            foreach (string name in new[] { "Idle", "Run", "LongAction", "ShortAction", "WaterAction" })
            {
                var clip = Track(new AnimationClip { name = name });
                float length = name == "ShortAction" ? .2f : 1.2f;
                clip.SetCurve("Pose", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, length, 0f));
                Track(states.AddState(name)).motion = clip;
            }
            Track(states.AddState("EmptyMotion"));
            Animator.runtimeAnimatorController = controller;
            Property(Player, "Animator", Animator);
            Forms = new FormManager(Player);
            Property(Player, "FormManager", Forms);
            Form = new ProbeForm(Data);
            Forms.ChangeForm(Form);
            // Build enters Idle immediately, which plays the Animator. Bind an active rig first.
            Host.SetActive(true);
            Animator.Rebind();
            Animator.Update(0f);
            Machine = new PlayerStateMachineSetup(Player).Build();
            Property(Player, "StateMachine", Machine);
            Attack = State<PlayerAttackState>();
            Skill = State<PlayerSkillState>();
            Ultimate = State<PlayerUltimateState>();
            Animator.Update(0f);
        }

        public T Track<T>(T item) where T : Object { _objects.Add(item); return item; }
        public GameObject PhysicsTarget(string name)
        {
            if (!_physicsScene.IsValid())
            {
                var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                _physicsScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                if (active.IsValid() && active.isLoaded)
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(active);
            }
            var target = Track(new GameObject(name));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(target, _physicsScene);
            return target;
        }
        public void StandardData()
        {
            Data.ComboSteps = new List<WeaponActionData> { Step(), Step(), Step() };
            Data.SkillSteps = new List<WeaponActionData> { Step() };
            Data.UltimateSteps = new List<WeaponActionData> { Step() };
        }
        public void Ready(IPlayerState state)
        {
            Machine.ChangeState(_neutral);
            if (!ReferenceEquals(Forms.CurrentForm, Form)) Forms.ChangeForm(Form);
            Input.AttackBuffer.Consume(); Input.SkillBuffer.Consume(); Input.UltimateBuffer.Consume();
            Input.JumpBuffer.Consume(); Input.DashBuffer.Consume();
            Property(Input, "MoveInput", Vector2.zero);
            SetField(Player, "_finisherAttack", null);
            Stats.Initialize(StatsData);
            Stats.AddUltimateGauge(StatsData.maxUltimateGauge);
            SetField(Form, "_skillCooldownTimer", 0f);
            SetField(Form, "_ultimateCooldownTimer", 0f);
            Animator.Play("Idle", 0, 0f);
            Animator.Update(0f);
            Machine.ChangeState(state);
        }
        public bool AttackAt(float elapsed)
        {
            Form.AttackAt(0, elapsed - Player.ActionDeltaTime * Form.Speed);
            Form.UpdateAttack(out bool complete);
            return complete;
        }
        public T State<T>() where T : IPlayerState
        {
            if (Machine.CurrentState is T current) return current;
            var transitions = (Dictionary<Type, List<Transition>>)GetField(Machine, "_transitions");
            foreach (var list in transitions.Values)
                foreach (var transition in list)
                    if (transition.To is T match) return match;
            throw new InvalidOperationException("Production state not registered: " + typeof(T).Name);
        }
        public void Dispose()
        {
            if (Host != null) Object.DestroyImmediate(Host);
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            if (_physicsScene.IsValid() && _physicsScene.isLoaded) EditorSceneManager.CloseScene(_physicsScene, true);
            EditorSceneManager.ClosePreviewScene(_scene);
        }
    }

    [MenuItem("Tools/Studio Baekho/Combat/Run Action Timing Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Action timing regression requires Edit Mode.");
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Action timing: " + message);
            checks++;
        }
        void Near(float actual, float expected, string message)
            => Check(Mathf.Abs(actual - expected) < .0001f, message + $" (actual {actual}, expected {expected})");

        using (var f = new Fixture())
        {
            Check(new WeaponActionData().ActionCancelTime < 0f, "Legacy data does not opt into early action exits.");
            f.Ready(f.Attack);
            f.Form.AttackAt(0, .45f);
            Check(f.Form.CanChainAttack && !f.Form.CanCancelCurrentActionToMove
                && !f.Form.CanCancelCurrentActionToAction, "Combo gate opens independently of both exit gates.");
            f.Form.AttackAt(0, .65f);
            Check(f.Form.CanCancelCurrentActionToMove && !f.Form.CanCancelCurrentActionToAction,
                "Movement gate opens before a separately configured combat gate.");
            f.Form.AttackAt(0, .95f);
            Check(f.Form.CanCancelCurrentActionToAction, "Combat gate opens at its own time.");

            var normal = f.Data.ComboSteps[0];
            normal.ComboTransitionTime = normal.RecoveryCancelTime = normal.ActionCancelTime = .1f;
            normal.HitEvents = new List<HitEvent>
            {
                new HitEvent { StartTime = .8f, Duration = .1f },
                new HitEvent { StartTime = .2f, Duration = .1f }
            };
            f.Form.AttackAt(0, .899f);
            Check(!f.Form.CanChainAttack && !f.Form.CanCancelCurrentActionToMove
                && !f.Form.CanCancelCurrentActionToAction, "Every normal exit preserves the latest unsorted hit.");
            f.Form.AttackAt(0, .8f + .1f);
            Check(f.Form.CanChainAttack && f.Form.CanCancelCurrentActionToMove
                && f.Form.CanCancelCurrentActionToAction, "All normal gates can open exactly at the last hit end.");

            f.StandardData();
            f.Data.ComboSteps[2].RecoveryCancelTime = .4f;
            f.Data.ComboSteps[2].ActionCancelTime = .8f;
            f.Ready(f.Attack);
            f.Form.AttackAt(2, .6f - f.Player.ActionDeltaTime * f.Form.Speed);
            f.Input.AttackBuffer.Set(.5f);
            int starts = f.Form.AttackStarts, ends = f.Form.AttackEnds;
            f.Attack.Update();
            Check(f.Form.CurrentComboStage == 2 && f.Form.AttackStarts == starts,
                "Terminal combo cannot restart merely because its movement gate opened.");
            f.Form.AttackAt(2, .801f - f.Player.ActionDeltaTime * f.Form.Speed);
            f.Attack.Update();
            Check(f.Form.CurrentComboStage == 0 && f.Form.AttackStarts == starts + 1
                && f.Form.AttackEnds == ends + 1 && !f.Input.AttackBuffer.IsActive,
                "Terminal combo restarts once at the action gate and consumes the reservation.");

            f.StandardData();
            foreach (bool toUltimate in new[] { false, true })
            {
                f.Ready(f.Attack);
                if (toUltimate) f.Input.UltimateBuffer.Set(); else f.Input.SkillBuffer.Set();
                f.Form.AttackAt(0, .899f);
                f.Machine.Update(true, false);
                Check(ReferenceEquals(f.Machine.CurrentState, f.Attack), "Normal combat exit waits before its action gate.");
                f.Form.AttackAt(0, .9f);
                int oldEnds = f.Form.AttackEnds;
                f.Machine.Update(true, false);
                Check(toUltimate ? f.Machine.CurrentState is PlayerUltimateState : f.Machine.CurrentState is PlayerSkillState,
                    "Normal attack directly enters the requested combat action at the gate.");
                Check(f.Form.AttackEnds == oldEnds + 1, "Direct combat exit ends its source action exactly once.");
                Near(f.Stats.CurrentSp, f.StatsData.baseSP - (toUltimate ? 0f : 10f), "Only the destination skill spends SP.");
                Near(f.Stats.CurrentUltimateGauge, toUltimate ? 0f : f.StatsData.maxUltimateGauge,
                    "Only the destination ultimate consumes its gauge.");
            }

            foreach (bool ultimate in new[] { false, true })
            {
                f.Ready(f.Attack);
                if (ultimate) { f.Input.UltimateBuffer.Set(.15f); f.Input.UltimateBuffer.Update(.151f); }
                else { f.Input.SkillBuffer.Set(.15f); f.Input.SkillBuffer.Update(.151f); }
                f.Form.AttackAt(0, .95f);
                f.Machine.Update(true, false);
                Check(ReferenceEquals(f.Machine.CurrentState, f.Attack),
                    "Expired skill or ultimate reservations cannot execute when the gate opens later.");
            }
            f.Ready(f.Attack);
            f.Form.AttackAt(0, .95f);
            f.Input.SkillBuffer.Set(); f.Input.UltimateBuffer.Set(); f.Input.AttackBuffer.Set(.5f);
            Property(f.Input, "MoveInput", Vector2.up);
            f.Machine.Update(true, false);
            Check(f.Machine.CurrentState is PlayerSkillState,
                "Simultaneous executable skill, ultimate, combo and movement follow the production combat priority.");

            foreach (bool toUltimate in new[] { false, true })
            {
                f.Ready(f.Skill);
                int skillEnds = f.Form.SkillEnds;
                Near(f.Stats.CurrentSp, f.StatsData.baseSP - 10f, "Skill entry spends its SP once.");
                if (toUltimate) f.Input.UltimateBuffer.Set(); else f.Input.AttackBuffer.Set(.5f);
                f.Form.SkillAt(0, .899f); f.Machine.Update(true, false);
                Check(ReferenceEquals(f.Machine.CurrentState, f.Skill), "Skill combat exit remains locked before its action gate.");
                f.Form.SkillAt(0, .9f); f.Machine.Update(true, false);
                Check(toUltimate ? f.Machine.CurrentState is PlayerUltimateState : f.Machine.CurrentState is PlayerAttackState,
                    "Skill directly enters a different requested combat action.");
                Check(f.Form.SkillEnds == skillEnds + 1, "Canceled skill runs its exit exactly once.");
                Near(f.Form.SkillCooldownRemaining, f.Form.SkillCooldown, "Skill cancellation starts its normal cooldown.");
                Near(f.Stats.CurrentSp, f.StatsData.baseSP - 10f, "Skill cancellation does not spend or refund SP again.");
            }

            foreach (bool toSkill in new[] { false, true })
            {
                f.Ready(f.Ultimate);
                int ultimateEnds = f.Form.UltimateEnds;
                if (toSkill) f.Input.SkillBuffer.Set(); else f.Input.AttackBuffer.Set(.5f);
                f.Form.UltimateAt(0, .899f); f.Machine.Update(true, false);
                Check(ReferenceEquals(f.Machine.CurrentState, f.Ultimate), "Ultimate combat exit waits for its action gate.");
                f.Form.UltimateAt(0, .9f); f.Machine.Update(true, false);
                Check(toSkill ? f.Machine.CurrentState is PlayerSkillState : f.Machine.CurrentState is PlayerAttackState,
                    "Ultimate directly enters a different requested combat action.");
                Check(f.Form.UltimateEnds == ultimateEnds + 1, "Canceled ultimate runs its exit exactly once.");
                Near(f.Stats.CurrentUltimateGauge, 0f, "Ultimate cancellation never restores consumed gauge.");
                Near(f.Form.UltimateCooldownRemaining, f.Form.UltimateCooldownDuration,
                    "Ultimate cooldown remains the single cooldown begun on entry.");
            }

            f.Ready(f.Skill);
            f.Form.SkillAt(0, .65f);
            f.Input.AttackBuffer.Set(.5f);
            Property(f.Input, "MoveInput", Vector2.up);
            f.Machine.Update(true, false);
            Check(f.Machine.CurrentState is PlayerRunState,
                "An unavailable combat reservation cannot block an independently open movement exit.");

            f.Data.SkillSteps.Add(Step());
            f.Ready(f.Skill);
            f.Form.SkillAt(0, 1.9f);
            Check(!f.Form.CanCancelCurrentActionToMove && !f.Form.CanCancelCurrentActionToAction,
                "A nonterminal skill stage cannot skip the remaining mandatory stages.");
            f.Form.SkillAt(1, .95f);
            Check(f.Form.CanCancelCurrentActionToMove && f.Form.CanCancelCurrentActionToAction,
                "Only a final skill stage exposes recovery exits.");
            f.StandardData();
            foreach (bool ultimate in new[] { false, true })
            {
                var step = ultimate ? f.Data.UltimateSteps[0] : f.Data.SkillSteps[0];
                step.RecoveryCancelTime = step.ActionCancelTime = .1f;
                step.HitEvents = new List<HitEvent> { new HitEvent { StartTime = 1f, Duration = .2f } };
                f.Ready(ultimate ? (IPlayerState)f.Ultimate : f.Skill);
                if (ultimate) f.Form.UltimateAt(0, 1.199f); else f.Form.SkillAt(0, 1.199f);
                Check(!f.Form.CanCancelCurrentActionToMove && !f.Form.CanCancelCurrentActionToAction,
                    "A final skill or ultimate still protects its last authored hit.");
                if (ultimate) f.Form.UltimateAt(0, 1f + .2f); else f.Form.SkillAt(0, 1f + .2f);
                Check(f.Form.CanCancelCurrentActionToMove && f.Form.CanCancelCurrentActionToAction,
                    "Final-stage recovery opens at the exact last-hit endpoint.");
            }

            f.StandardData();
            normal = f.Data.ComboSteps[0];
            normal.AnimationName = "LongAction"; normal.Duration = .4f;
            normal.UseDistanceMovement = true; normal.ForwardDistance = .5f;
            normal.MovementStartTime = 0f; normal.MovementEndTime = .4f;
            f.Ready(f.Attack);
            f.Animator.Update(0f); f.Animator.Update(.5f);
            Vector3 beforeExecution = f.Host.transform.position;
            Check(!f.AttackAt(.41f) && f.Form.IsCurrentActionExecutionComplete,
                "No input keeps a real longer clip playing after mechanical execution ends.");
            int hits = f.Form.HitCalls;
            Vector3 position = f.Host.transform.position;
            Check(Vector3.Distance(beforeExecution, position) > .1f,
                "The playback fixture executes real configured attack movement before its tail.");
            Check(!f.AttackAt(.7f), "The presentation tail does not complete before the actual clip.");
            Check(f.Form.HitCalls == hits && f.Host.transform.position == position,
                "The presentation tail cannot add hit queries or attack movement.");
            f.Player.VerticalVelocity = -2f;
            f.Form.AttackAt(0, .7f - f.Player.ActionDeltaTime * f.Form.Speed);
            Vector3 beforeGravity = f.Host.transform.position;
            float expectedFall = -2f * f.Player.ActionDeltaTime;
            f.Attack.Update();
            Vector3 afterGravity = f.Host.transform.position;
            Check(Mathf.Abs(afterGravity.y - beforeGravity.y - expectedFall) < .001f
                    && afterGravity.x == beforeGravity.x && afterGravity.z == beforeGravity.z
                    && f.Form.HitCalls == hits,
                "The production attack state preserves vertical gravity without horizontal motion or hits in its presentation tail.");
            f.Player.VerticalVelocity = 0f;
            f.Host.transform.position = beforeGravity;
            f.Animator.Update(.8f);
            Check(f.AttackAt(1.3f), "No-input action completes after both mechanics and actual clip playback finish.");

            f.Ready(f.Attack);
            f.Animator.Play("LongAction", 0, 1.1f); f.Animator.Update(0f);
            f.Form.BeginAttack();
            Check(!f.AttackAt(.5f), "A completed previous same-name state cannot finish a restarted attack.");
            f.Animator.Update(0f); f.Animator.Update(.1f);
            Check(!f.AttackAt(.6f), "Restarted animation remains active after its newly evaluated opening frames.");

            foreach (string absent in new[] { "", "MissingAction", "EmptyMotion" })
            {
                normal.AnimationName = absent;
                f.Ready(f.Attack);
                f.Animator.Update(0f);
                Check(f.AttackAt(.41f), "An absent playable motion falls back to mechanical duration: " + absent);
            }
            normal.AnimationName = "ShortAction"; normal.Duration = .8f;
            normal.HitStartTime = .6f; normal.HitDuration = .1f;
            f.Ready(f.Attack);
            f.Animator.Update(0f); f.Animator.Update(.3f);
            Check(!f.AttackAt(.4f), "A short animation cannot truncate mandatory mechanical execution.");
            Check(f.AttackAt(.81f), "A finished short clip releases after the required mechanical duration.");

            // Water's additional capsule query used to continue even after base.UpdateSkill completed.
            f.Ready(f.Attack);
            f.Machine.ChangeState(new NeutralState());
            var waterData = f.Track(ScriptableObject.CreateInstance<WaterFormActionDataSO>());
            waterData.ComboSteps = new List<WeaponActionData> { Step(), Step(), Step() };
            waterData.SkillSteps = new List<WeaponActionData> { Step() };
            waterData.UltimateSteps = new List<WeaponActionData> { Step() };
            var waterStep = waterData.SkillSteps[0];
            waterStep.AnimationName = "WaterAction"; waterStep.Duration = .9f;
            waterStep.HitStartTime = .15f; waterStep.HitDuration = 1.7f;
            waterStep.RecoveryCancelTime = waterStep.ActionCancelTime = .9f;
            var water = new WaterForm(waterData);
            f.Forms.ChangeForm(water);
            f.Stats.Initialize(f.StatsData);
            int originalExclusions = f.Player.Controller.excludeLayers;
            water.BeginSkill();
            f.Animator.Update(0f); f.Animator.Update(.95f);
            SetField(water, "_timer", .901f - f.Player.ActionDeltaTime);
            water.UpdateSkill(out bool waterComplete);
            Check(!waterComplete && water.IsCurrentActionExecutionComplete,
                "Water skill holds its animation after its original 0.9-second execution window.");
            Check(f.Player.Controller.excludeLayers == originalExclusions,
                "Water restores collision exclusions when mechanics finish, before the presentation tail ends.");
            var target = f.PhysicsTarget("__LateWaterTimingTarget");
            target.layer = LayerMask.NameToLayer("Enemy");
            target.transform.position = f.Player.Controller.bounds.center;
            var targetCollider = target.AddComponent<SphereCollider>(); targetCollider.radius = .2f;
            var damage = target.AddComponent<ActionTimingRegressionTarget>();
            Physics.SyncTransforms();
            Check(Array.IndexOf(Physics.OverlapSphere(f.Player.Controller.bounds.center, 1f,
                    LayerMask.GetMask("Enemy")), targetCollider) >= 0,
                "The late Water target is physically queryable, so the no-hit assertion exercises the execution guard.");
            Vector3 waterPosition = f.Host.transform.position;
            SetField(water, "_timer", 1f - f.Player.ActionDeltaTime);
            water.UpdateSkill(out waterComplete);
            Check(damage.Calls == 0 && f.Host.transform.position == waterPosition,
                "An enemy entering during Water's presentation tail receives no capsule or normal hit.");
            f.Animator.Update(.4f);
            SetField(water, "_timer", 1.3f - f.Player.ActionDeltaTime);
            water.UpdateSkill(out waterComplete);
            Check(waterComplete, "Water skill releases after the remaining animation finishes.");
            water.EndSkill();
            Near(f.Stats.CurrentSp, f.StatsData.baseSP - water.SkillSpCost, "Water's tail never spends the skill cost again.");
            Near(water.SkillCooldownRemaining, water.SkillCooldown, "Water cooldown begins once at the real state exit.");

            target.SetActive(false);
            Physics.SyncTransforms();
            var visibleObject = new GameObject("UltimateVisibleRenderer");
            visibleObject.transform.SetParent(f.Host.transform, false);
            var visibleRenderer = visibleObject.AddComponent<MeshRenderer>();
            var hiddenObject = new GameObject("UltimateOriginallyHiddenRenderer");
            hiddenObject.transform.SetParent(f.Host.transform, false);
            var hiddenRenderer = hiddenObject.AddComponent<MeshRenderer>(); hiddenRenderer.enabled = false;
            waterData.UltimateSteps[0].AnimationName = "";
            waterData.UltimateSteps[0].RecoveryCancelTime = waterData.UltimateSteps[0].ActionCancelTime = 0f;
            f.Stats.AddUltimateGauge(f.StatsData.maxUltimateGauge);
            water.BeginUltimate();
            Check(!visibleRenderer.enabled && !hiddenRenderer.enabled
                    && water.BlocksIncomingDamage(10f, Vector3.zero),
                "Water ultimate hides renderers and protects the actor during its mandatory ten-hit execution.");
            Check(!Advance(water, "AdvanceUltimate", .9f) && (int)GetField(water, "_ultimateHits") == 4
                    && !water.CanCancelCurrentActionToMove && !water.CanCancelCurrentActionToAction,
                "Zero configured recovery times cannot skip Water ultimate's remaining fixed hits.");
            Check(Advance(water, "AdvanceUltimate", 1.11f) && water.IsCurrentActionExecutionComplete
                    && (int)GetField(water, "_ultimateHits") == 10,
                "A no-motion Water ultimate completes only after all ten scheduled hits execute.");
            Check(water.CanCancelCurrentActionToMove && water.CanCancelCurrentActionToAction,
                "Both Water ultimate recovery gates open after the tenth hit.");
            Check(visibleRenderer.enabled && !hiddenRenderer.enabled
                    && !water.BlocksIncomingDamage(10f, Vector3.zero),
                "Water ultimate restores each renderer's original state and removes invulnerability at mechanical completion.");
            Check((bool)GetField(water, "_ultimateExecutionRestored")
                    && Advance(water, "AdvanceUltimate", .5f) && (int)GetField(water, "_ultimateHits") == 10,
                "Further completion updates neither repeat restoration nor produce extra ultimate ticks.");
            Vector3 ultimateExitPosition = f.Host.transform.position;
            water.EndUltimate();
            water.EndUltimate();
            Check(visibleRenderer.enabled && !hiddenRenderer.enabled
                    && !water.BlocksIncomingDamage(10f, Vector3.zero)
                    && f.Host.transform.position == ultimateExitPosition,
                "Water ultimate final cleanup is idempotent after execution already restored its temporary effects.");
            Near(f.Stats.CurrentUltimateGauge, 0f, "Water ultimate spends its gauge once despite repeated completion cleanup.");

            // The injected action delta must not depend on the Editor's current Time.deltaTime.
            // Actual local hit stop and world pause still own the timeline independently.
            var waterClock = CombatActorClock.GetOrCreate(f.Player);
            Property(f.Player, "ActorClock", waterClock);
            SetField(waterClock, "_animator", f.Animator);
            f.Stats.AddUltimateGauge(f.StatsData.maxUltimateGauge);
            water.BeginUltimate();
            waterClock.Request(f.Player, 60f, 0f, HitStopStacking.Maximum);
            Check(!Advance(water, "AdvanceUltimate", .9f) && (int)GetField(water, "_ultimateHits") == 0,
                "A stopped actor cannot advance Water ultimate even when a catch-up delta is supplied.");
            Near((float)GetField(water, "_ultimateElapsed"), 0f, "Actor hit stop freezes Water's mandatory hit timeline.");
            waterClock.ClearStops();
            Check(!Advance(water, "AdvanceUltimate", .9f) && (int)GetField(water, "_ultimateHits") == 4,
                "Clearing local hit stop resumes all four scheduled catch-up hits without relying on Editor delta.");
            float waterWorldScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                Check(!Advance(water, "AdvanceUltimate", 1.11f) && (int)GetField(water, "_ultimateHits") == 4,
                    "World pause cannot advance Water ultimate's remaining scheduled hits.");
                Near((float)GetField(water, "_ultimateElapsed"), .9f, "World pause preserves Water's elapsed action time.");
            }
            finally { Time.timeScale = waterWorldScale; }
            Check(Advance(water, "AdvanceUltimate", 1.11f) && (int)GetField(water, "_ultimateHits") == 10,
                "Water ultimate resumes its six remaining hits after world pause.");
            water.EndUltimate();
            Property(f.Player, "ActorClock", null);

            var woodData = f.Track(ScriptableObject.CreateInstance<WoodFormActionDataSO>());
            woodData.SkillSteps = new List<WeaponActionData> { Step() };
            woodData.SkillSteps[0].HitStartTime = .3f;
            woodData.SkillSteps[0].RecoveryCancelTime = woodData.SkillSteps[0].ActionCancelTime = 0f;
            var wood = new WoodForm(woodData);
            f.Forms.ChangeForm(wood);
            f.Stats.Initialize(f.StatsData);
            wood.BeginSkill();
            Advance(wood, "AdvanceSkill", .29f);
            Check(!wood.CanCancelCurrentActionToMove && !wood.CanCancelCurrentActionToAction,
                "Wood recovery cannot cancel its mandatory gather effect before it is applied.");
            Advance(wood, "AdvanceSkill", .02f);
            Check(wood.CanCancelCurrentActionToMove && wood.CanCancelCurrentActionToAction,
                "Wood recovery uses its actual one-shot effect rather than waiting for the generic hit-window end.");
            wood.EndSkill();

            var earthData = f.Track(ScriptableObject.CreateInstance<EarthFormActionDataSO>());
            earthData.SkillSteps = new List<WeaponActionData> { Step() };
            earthData.SkillSteps[0].RecoveryCancelTime = earthData.SkillSteps[0].ActionCancelTime = 0f;
            earthData.SkillDashDuration = .4f; earthData.SkillDashDistance = 0f;
            var earth = new EarthForm(earthData);
            f.Forms.ChangeForm(earth);
            f.Stats.Initialize(f.StatsData);
            earth.BeginSkill();
            Advance(earth, "AdvanceSkill", .39f);
            Check(!earth.CanCancelCurrentActionToMove && !earth.CanCancelCurrentActionToAction,
                "Earth recovery protects its entire actual dash even after the generic hit data ends.");
            Advance(earth, "AdvanceSkill", .02f);
            Check(earth.CanCancelCurrentActionToMove && earth.CanCancelCurrentActionToAction,
                "Earth recovery opens after its fixed dash execution finishes.");
            earth.EndSkill();
            f.Forms.ChangeForm(f.Form);
        }
        checks += RunSkillReservations();
        return $"PASS: action timing ({checks} checks: independent gates, per-hit skill reservations and cancellation, production combat exits, mandatory hits and stages, resources, real Animator completion, same-state restart, missing-motion fallback, and Water execution-tail isolation).";
    }

    private static int RunSkillReservations()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Skill reservation: " + message);
            checks++;
        }
        void Near(float actual, float expected, string message)
            => Check(Mathf.Abs(actual - expected) < .0001f, message + $" (actual {actual}, expected {expected})");

        using (var f = new Fixture())
        {
            // Editor regressions live in a separate assembly from the internal runtime hooks.
            void Prepare() => Invoke(f.Attack, "PrepareSkillReservation");
            bool Queued() => (bool)f.Attack.GetType().GetProperty("HasQueuedSkill", Hidden).GetValue(f.Attack);
            void Reserve()
            {
                f.Input.SkillBuffer.Set();
                Prepare();
            }
            void Transitions(bool allowActions = true)
            {
                Prepare();
                f.Machine.Update(allowActions, false);
            }
            void Ready(int stage = 0, float elapsed = .1f)
            {
                f.StandardData();
                f.Ready(f.Attack);
                f.Form.AttackAt(stage, elapsed);
            }
            void MakeUnavailable(string reason)
            {
                if (reason == "SP") f.Stats.TryConsumeSp(f.Stats.CurrentSp);
                else if (reason == "cooldown") SetField(f.Form, "_skillCooldownTimer", 1f);
                else f.Data.SkillSteps.Clear();
            }
            void RestoreSkill()
            {
                f.Stats.AddSp(f.StatsData.baseSP);
                SetField(f.Form, "_skillCooldownTimer", 0f);
                if (f.Data.SkillSteps.Count == 0) f.Data.SkillSteps.Add(Step());
            }

            Ready();
            int singleSkillStarts = f.Form.SkillStarts;
            Reserve();
            f.Input.SkillBuffer.Update(.6f);
            f.Form.AttackAt(0, .65f);
            Property(f.Input, "MoveInput", Vector2.up);
            Transitions();
            Check(Queued() && ReferenceEquals(f.Machine.CurrentState, f.Attack),
                "One early E survives the ordinary input timeout and an earlier movement gate without another press.");
            f.Form.AttackAt(0, .9f);
            Transitions();
            Check(f.Machine.CurrentState is PlayerSkillState && f.Form.SkillStarts == singleSkillStarts + 1 && !Queued(),
                "A single early E executes after the same hit without being refreshed by another E.");

            foreach (int stage in new[] { 0, 2 })
            {
                Ready(stage);
                int attackStarts = f.Form.AttackStarts, skillStarts = f.Form.SkillStarts;
                float sp = f.Stats.CurrentSp;
                f.Input.AttackBuffer.Set(.5f);
                Reserve();
                Check(Queued() && !f.Input.SkillBuffer.IsActive && !f.Input.AttackBuffer.IsActive,
                    "Early E becomes one current-hit reservation and replaces an already buffered normal attack.");
                Near(f.Stats.CurrentSp, sp, "Accepting an E reservation does not prepay SP.");
                f.Input.SkillBuffer.Update(.6f);
                f.Form.AttackAt(stage, .65f);
                Prepare();
                Check(Queued() && !f.Input.SkillBuffer.IsActive,
                    "An early E remains reserved after the ordinary 0.15-second input buffer would have expired.");
                Property(f.Input, "MoveInput", Vector2.up);
                for (int i = 0; i < 3; i++)
                {
                    f.Form.AttackAt(stage, .65f - f.Player.ActionDeltaTime * f.Form.Speed);
                    f.Input.AttackBuffer.Set(.5f);
                    Reserve();
                    f.Machine.Update(true, true);
                    Check(ReferenceEquals(f.Machine.CurrentState, f.Attack) && Queued()
                        && f.Form.CurrentComboStage == stage && f.Form.AttackStarts == attackStarts,
                        "Repeated E, normal-attack spam and held movement cannot advance or restart the reserved hit.");
                }
                f.Form.AttackAt(stage, .9f);
                Transitions();
                Check(f.Machine.CurrentState is PlayerSkillState && f.Form.SkillStarts == skillStarts + 1
                    && !Queued() && !f.Input.SkillBuffer.IsActive && !f.Input.AttackBuffer.IsActive,
                    "The queued skill replaces the same hit at its action gate and consumes all duplicate reservations.");
                Near(f.Stats.CurrentSp, sp - f.Form.SkillSpCost, "Repeated E starts one skill and spends SP once.");
                f.Machine.Update(true, false);
                Check(f.Form.SkillStarts == skillStarts + 1, "A consumed E reservation cannot replay on the following frame.");
            }

            Ready();
            SetField(f.Attack, "_pending", true);
            Reserve();
            Check(Queued() && !f.Attack.CanCancelToAction && !(bool)GetField(f.Attack, "_pending"),
                "E replaces a follow-up tap/hold decision without bypassing the current normal hit's action gate.");
            f.Form.AttackAt(0, .899f);
            Transitions();
            Check(ReferenceEquals(f.Machine.CurrentState, f.Attack), "A reserved E still waits just before the action boundary.");
            f.Form.AttackAt(0, .9f);
            Transitions();
            Check(f.Machine.CurrentState is PlayerSkillState, "The reserved E becomes executable at the exact action boundary.");

            foreach (string reason in new[] { "SP", "cooldown", "missing steps" })
            {
                Ready();
                MakeUnavailable(reason);
                Reserve();
                Check(!Queued() && !f.Input.SkillBuffer.IsActive,
                    "An unavailable skill is rejected without retaining a future request: " + reason);
                RestoreSkill();
                f.Form.AttackAt(0, .95f);
                Transitions();
                Check(ReferenceEquals(f.Machine.CurrentState, f.Attack),
                    "Restoring eligibility cannot resurrect an E rejected at input time: " + reason);

                Ready();
                Reserve();
                MakeUnavailable(reason);
                Prepare();
                Check(!Queued() && !f.Input.SkillBuffer.IsActive,
                    "Losing eligibility cancels an accepted reservation before execution: " + reason);
                RestoreSkill();
                f.Form.AttackAt(0, .95f);
                Transitions();
                Check(ReferenceEquals(f.Machine.CurrentState, f.Attack),
                    "A canceled E does not reappear when eligibility recovers: " + reason);
                Property(f.Input, "MoveInput", Vector2.up);
                Transitions();
                Check(f.Machine.CurrentState is PlayerRunState,
                    "Canceling an unavailable reservation releases normal movement: " + reason);
            }

            Ready();
            Reserve();
            f.Form.AttackAt(1, .1f);
            Prepare();
            Check(!Queued(), "A reservation cannot migrate to a different normal-combo stage.");

            Ready();
            Reserve();
            var otherData = f.Track(ScriptableObject.CreateInstance<FireFormActionDataSO>());
            otherData.ComboSteps = new List<WeaponActionData> { Step() };
            otherData.SkillSteps = new List<WeaponActionData> { Step() };
            var otherForm = new ProbeForm(otherData);
            f.Forms.ChangeForm(otherForm);
            Prepare();
            Check(!Queued(), "Changing the equipped form invalidates the source form's E reservation.");

            foreach (string reset in new[] { "input clear", "shop", "map", "finisher input", "input disabled", "player disabled" })
            {
                Ready();
                Reserve();
                if (reset == "input clear") Invoke(f.Input, "ClearGameplayInput");
                else if (reset == "shop") { Invoke(f.Player, "SetShopPaused", true); Invoke(f.Player, "SetShopPaused", false); }
                else if (reset == "map") { Invoke(f.Player, "SetMapPaused", true); Invoke(f.Player, "SetMapPaused", false); }
                else if (reset == "finisher input")
                {
                    Invoke(f.Input, "SetFinisherInputBlocked", true);
                    Invoke(f.Input, "SetFinisherInputBlocked", false);
                }
                // Edit Mode does not guarantee MonoBehaviour lifecycle messages for a preview fixture.
                else if (reset == "input disabled") Invoke(f.Input, "OnDisable");
                else Invoke(f.Player, "OnDisable");
                Check(!Queued(), "Input reset paths discard the local reservation while the attack state remains active: " + reset);
                f.Form.AttackAt(0, .95f);
                Transitions();
                Check(ReferenceEquals(f.Machine.CurrentState, f.Attack),
                    "Resuming gameplay cannot execute an E from before an input reset: " + reset);
            }

            Ready();
            Reserve();
            Invoke(f.Attack, "BeginFinisherSelection", Vector3.forward);
            Check(!Queued(), "Opening the finisher selection explicitly discards an earlier normal-hit E request.");
            foreach (IPlayerState destination in new IPlayerState[] { new NeutralState(), new PlayerHitState(f.Player), f.State<PlayerDashState>() })
            {
                Ready();
                Reserve();
                f.Machine.ChangeState(destination);
                Check(!Queued(), "Leaving the source attack discards its reservation: " + destination.GetType().Name);
                f.Ready(f.Attack);
                f.Form.AttackAt(0, .95f);
                Transitions();
                Check(ReferenceEquals(f.Machine.CurrentState, f.Attack), "A later attack cannot inherit the previous action's E.");
            }

            Ready();
            Reserve();
            SetField(f.Player, "_dashCooldownTimer", 0f);
            f.Form.AttackAt(0, .95f);
            int beforeDodgeSkills = f.Form.SkillStarts;
            f.Input.DashBuffer.Set();
            Transitions();
            Check(f.Machine.CurrentState is PlayerDashState && !Queued()
                && f.Form.SkillStarts == beforeDodgeSkills,
                "A real dash input wins even when the reserved skill gate is open and cancels E.");

            Ready();
            var normal = f.Data.ComboSteps[0];
            normal.AnimationName = "LongAction";
            normal.Duration = .4f;
            normal.ActionCancelTime = -1f;
            f.Ready(f.Attack);
            f.Form.AttackAt(0, .1f);
            Reserve();
            f.Animator.Update(0f);
            f.Animator.Update(.5f);
            f.Form.AttackAt(0, .41f);
            f.Attack.Update();
            Transitions();
            Check(Queued() && ReferenceEquals(f.Machine.CurrentState, f.Attack) && !f.Attack.CanCancelToAction,
                "A disabled early action gate keeps the E reservation through the visible animation tail.");
            f.Animator.Update(.8f);
            f.Form.AttackAt(0, 1.31f);
            f.Attack.Update();
            Transitions();
            Check(f.Machine.CurrentState is PlayerSkillState && !Queued(),
                "An E reserved behind a -1 action gate executes once after the full attack and animation complete.");

            Ready();
            f.Form.AttackAt(0, 2.1f);
            f.Attack.Update();
            Check(f.Attack.IsAttackComplete, "The no-motion fixture reaches normal completion before the final-frame reservation case.");
            Reserve();
            Check(Queued() && !f.Attack.IsAttackComplete, "An E received on the completion frame prevents an Idle detour.");
            Transitions();
            Check(f.Machine.CurrentState is PlayerSkillState, "A completion-frame E directly enters the skill.");

            Ready();
            var clock = CombatActorClock.GetOrCreate(f.Player);
            Property(f.Player, "ActorClock", clock);
            SetField(clock, "_animator", f.Animator);
            clock.Request(f.Player, 60f, 0f, HitStopStacking.Maximum);
            float stoppedAt = f.Form.Elapsed, stoppedSp = f.Stats.CurrentSp;
            Reserve();
            f.Input.SkillBuffer.Update(f.Player.ActionDeltaTime);
            f.Machine.Update(false, false);
            Check(Queued() && clock.IsStopped && ReferenceEquals(f.Machine.CurrentState, f.Attack),
                "An E pressed during actor hit stop can be reserved without starting a new action.");
            Near(f.Form.Elapsed, stoppedAt, "Hit stop does not advance the reserved attack's clock.");
            Near(f.Stats.CurrentSp, stoppedSp, "Hit stop does not spend a reserved skill's SP.");
            clock.ClearStops();
            float originalSpeed = f.Form.Speed;
            f.Stats.AddModifier(StatType.ST_ATK_SPD, 0f, 1f);
            Check(f.Form.Speed > originalSpeed, "The reservation speed case applies a real attack-speed modifier.");
            f.Form.AttackAt(0, .5f);
            float expected = f.Form.Elapsed + f.Player.ActionDeltaTime * f.Form.Speed;
            f.Attack.Update();
            Near(f.Form.Elapsed, expected, "The reserved hit keeps using the actor clock and current attack speed.");
            Check(Queued() && f.Form.CurrentComboStage == 0, "Resuming at a different speed preserves the source hit's reservation.");
            f.Form.AttackAt(0, .9f);
            Transitions();
            Check(f.Machine.CurrentState is PlayerSkillState && !Queued(), "The reserved E executes after hit stop at the normal action gate.");
            Near(f.Stats.CurrentSp, stoppedSp - f.Form.SkillSpCost, "Hit stop and speed changes never duplicate the skill cost.");
        }
        return checks;
    }

    private static WeaponActionData Step() => new WeaponActionData
    {
        AnimationName = "", Duration = 2f, ComboTransitionTime = .4f,
        RecoveryCancelTime = .6f, ActionCancelTime = .9f,
        HitStartTime = .2f, HitDuration = .1f,
        UseTargetApproach = false, ForwardDistance = 0f, ThrustMultiplier = 0f, DisableTrail = true
    };
    private static FieldInfo Field(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Hidden);
            if (field != null) return field;
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }
    private static object GetField(object target, string name) => Field(target, name).GetValue(target);
    private static void SetField(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] arguments)
        => target.GetType().GetMethod(name, Hidden).Invoke(target, arguments);
    private static bool Advance(object form, string method, float deltaTime)
    {
        object[] parameters = { deltaTime, false };
        form.GetType().GetMethod(method, Hidden).Invoke(form, parameters);
        return (bool)parameters[1];
    }
    private static void Property(object target, string name, object value)
        => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);
}

public sealed class ActionTimingRegressionTarget : MonoBehaviour, IDamageable
{
    public int Calls;
    public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE,
        bool isCritical = false, Vector3 power = default, float poiseDamage = 20f,
        StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
    {
        Calls++;
        return new DamageResult(DamageOutcome.Rejected);
    }
}
