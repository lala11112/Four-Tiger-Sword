using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Checks dodge animation selection through the production state transitions in an isolated scene.</summary>
public static class DashAnimationRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class ProbeForm : BaseForm
    {
        public int Ends;
        public ProbeForm(WeaponActionDataSO data) : base(data) { }
        public override void BeginAttack() { }
        public override void EndAttack() => Ends++;
    }

    private sealed class NeutralState : IPlayerState
    {
        public void Enter() { }
        public void Update() { }
        public void Exit() { }
    }

    [MenuItem("Tools/Studio Baekho/Combat/Run Dash Animation Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Dash animation regression requires Edit Mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        var objects = new List<Object>();
        GameObject host = null;
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Dash animation: " + message);
            checks++;
        }
        void Near(float actual, float expected, string message)
            => Check(Mathf.Abs(actual - expected) < .0001f, message + $" (actual {actual}, expected {expected})");
        try
        {
            var statsData = ScriptableObject.CreateInstance<PlayerStatData>();
            var formData = ScriptableObject.CreateInstance<FireFormActionDataSO>();
            objects.Add(statsData); objects.Add(formData);
            host = new GameObject("__DashAnimationRegression");
            host.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
            host.transform.position = new Vector3(12000f, 1000f, 12000f);
            new GameObject("Pose").transform.SetParent(host.transform, false);
            var player = host.AddComponent<PlayerController>();
            var input = host.GetComponent<PlayerInputHandler>();
            var stats = host.GetComponent<PlayerStatManager>();
            Property(player, "Controller", host.GetComponent<CharacterController>());
            Property(player, "Input", input);
            Property(player, "StatManager", stats);
            Property(player, "Movement", host.GetComponent<PlayerMovement>());
            player.Movement.Initialize(player);
            player.Gravity = player.VerticalVelocity = 0f;
            var animator = host.AddComponent<Animator>();
            var controller = new AnimatorController { name = "Temporary dash regression controller" };
            objects.Add(controller);
            controller.AddLayer("Base Layer");
            var animationStates = controller.layers[0].stateMachine;
            objects.Add(animationStates);
            foreach (string name in new[] { "Idle", "Run", "Dash", "Backstep", "Attack" })
            {
                var clip = new AnimationClip { name = name };
                clip.SetCurve("Pose", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 0f));
                var state = animationStates.AddState(name);
                state.motion = clip;
                objects.Add(clip); objects.Add(state);
            }
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Property(player, "Animator", animator);
            var forms = new FormManager(player);
            var form = new ProbeForm(formData);
            form.Equip(player);
            Property(forms, "CurrentForm", form);
            Property(player, "FormManager", forms);
            var machine = new StateMachine();
            Property(player, "StateMachine", machine);
            var neutral = new NeutralState();
            var idle = new PlayerIdleState(player);
            var move = new PlayerRunState(player);
            var dash = new PlayerDashState(player);
            var attack = new PlayerAttackState(player);
            var setup = new PlayerStateMachineSetup(player);
            typeof(PlayerStateMachineSetup).GetMethod("DashTransitions", Hidden).Invoke(setup,
                new object[] { machine, idle, move, dash });
            typeof(PlayerStateMachineSetup).GetMethod("AttackTransitions", Hidden).Invoke(setup,
                new object[] { machine, idle, move, new PlayerJumpState(player), attack, dash,
                    new PlayerSkillState(player), new PlayerUltimateState(player) });
            host.SetActive(true);
            animator.Rebind();
            animator.Update(0f);

            AnimatorStateInfo Playing()
            {
                animator.Update(0f);
                return animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
            }
            void ExpectAnimation(string name)
                => Check(Playing().shortNameHash == Animator.StringToHash(name), $"Expected {name} animation.");
            void Ready(Vector2 direction)
            {
                machine.ChangeState(neutral);
                Field(player, "_dashCooldownTimer", 0f);
                stats.Initialize(statsData);
                input.AttackBuffer.Consume(); input.JumpBuffer.Consume(); input.DashBuffer.Consume();
                input.SkillBuffer.Consume(); input.UltimateBuffer.Consume();
                Property(input, "MoveInput", direction);
                player.DashAnimationName = "Dash";
                player.BackstepAnimationName = "Backstep";
                host.transform.rotation = Quaternion.identity;
                machine.ChangeState(idle);
                animator.Play("Idle", 0, .5f);
                animator.Update(0f);
            }
            void Start()
            {
                input.DashBuffer.Set();
                machine.Update(true, false);
                Check(ReferenceEquals(machine.CurrentState, dash), "Production transition enters dodge.");
                Check(!input.DashBuffer.IsActive, "Successful dodge consumes its input buffer.");
            }
            void Finish(Vector2 direction)
            {
                Field(dash, "_dashTimer", .2f);
                dash.Update();
                Check(dash.IsDashComplete, "Dodge completes at the existing 0.2-second duration.");
                Property(input, "MoveInput", direction);
                machine.Update(true, false);
                Check(direction.sqrMagnitude > .01f ? ReferenceEquals(machine.CurrentState, move)
                    : ReferenceEquals(machine.CurrentState, idle), "Dodge returns through the production Idle/Run transition.");
                Near((float)Field(player, "_dashCooldownTimer"), player.DashCooldown, "Exit starts the existing cooldown.");
            }

            Ready(Vector2.zero);
            animator.speed = 2f;
            animator.applyRootMotion = true;
            Start();
            ExpectAnimation("Backstep");
            Near(Playing().normalizedTime, 0f, "Backstep starts at its first frame.");
            Check(!animator.applyRootMotion, "Dodge disables root motion.");
            Near(animator.speed, 1f, "Dodge resets inherited animation speed.");
            Check((Vector3)Field(dash, "_dashDirection") == Vector3.back, "No input retreats opposite the original facing.");
            Property(input, "MoveInput", Vector2.right);
            Field(dash, "_dashTimer", -player.ActionDeltaTime);
            dash.Update();
            ExpectAnimation("Backstep");
            Check((Vector3)Field(dash, "_dashDirection") == Vector3.back, "New input cannot redirect an active backstep.");
            Finish(Vector2.right);
            ExpectAnimation("Run");

            foreach (Vector2 direction in new[] { Vector2.up, Vector2.right, Vector2.down })
            {
                Ready(direction);
                Start();
                ExpectAnimation("Dash");
                Check(Vector3.Dot((Vector3)Field(dash, "_dashDirection"), new Vector3(direction.x, 0f, direction.y)) > .999f,
                    "Directional dodge keeps its camera-relative movement, including backward input.");
                Property(input, "MoveInput", Vector2.zero);
                Field(dash, "_dashTimer", -player.ActionDeltaTime);
                dash.Update();
                ExpectAnimation("Dash");
                Finish(Vector2.zero);
                ExpectAnimation("Idle");
            }

            Ready(new Vector2(.099f, 0f)); Start(); ExpectAnimation("Backstep");
            Ready(new Vector2(.101f, 0f)); Start(); ExpectAnimation("Dash");
            Ready(Vector2.up);
            animator.Play("Dash", 0, .75f); animator.Update(0f);
            Start();
            ExpectAnimation("Dash");
            Near(Playing().normalizedTime, 0f, "A later dodge restarts a previously playing animation.");
            Ready(Vector2.up); player.DashAnimationName = "MissingDash"; Start(); ExpectAnimation("Idle");
            Ready(Vector2.zero); player.BackstepAnimationName = " "; Start(); ExpectAnimation("Idle");

            Ready(Vector2.up);
            Field(player, "_dashCooldownTimer", .5f);
            input.DashBuffer.Set(); machine.Update(true, false);
            Check(ReferenceEquals(machine.CurrentState, idle), "Dodge on cooldown cannot leave Idle.");
            ExpectAnimation("Idle");
            Near((float)Field(player, "_dashCooldownTimer"), .5f, "Rejected dodge does not restart cooldown.");

            Ready(Vector2.down);
            machine.ChangeState(attack);
            animator.Play("Attack", 0, .5f); animator.Update(0f);
            int ends = form.Ends;
            Start();
            ExpectAnimation("Dash");
            Check(form.Ends == ends + 1, "Attack-cancel dodge ends the interrupted attack exactly once.");
            return $"PASS: dash animation ({checks} checks: input selection and threshold, snapshots, restart, fallback, root motion and speed, cooldown gate, attack cancellation and Idle/Run recovery).";
        }
        finally
        {
            if (host != null) Object.DestroyImmediate(host);
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static object Field(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    private static void Property(object target, string name, object value)
        => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);
}
