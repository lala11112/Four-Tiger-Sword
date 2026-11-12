using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Exercises the shipped jump clips and the production player transitions in isolation.</summary>
public static class JumpAnimationRegression
{
    private const string ControllerPath = "Assets/_Project/Animations/AnimController/BDJ.controller";
    private const string ClipFolder = "Assets/_Project/Animations/PlayerAnim/Jump/";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class ProbeForm : BaseForm
    {
        public ProbeForm(WeaponActionDataSO data) : base(data) { }
        public override void BeginAttack() => Play("Fire1");
        public override void EndAttack() { }
        public override void BeginSkill() => Play("WaterSkill");
        public override void EndSkill() { }
        public override void BeginAirAttack() => Play("FireAirStart");
        public override void UpdateAirAttackStart(out bool isComplete) => isComplete = true;
        public override void BeginAirAttackLoop() => Play("FireAirIdle");
        public override void BeginAirAttackFinish() => Play("FireAirEnd");
        public override void UpdateAirAttack(out bool isComplete) => isComplete = true;
        public override void EndAirAttack() { }
        private void Play(string state) => _playerController.Animator.CrossFadeInFixedTime(state, .05f, 0, 0f);
    }

    [MenuItem("Tools/Studio Baekho/Combat/Run Jump Animation Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Jump animation regression requires Edit Mode.");

        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Jump animation: " + message);
            checks++;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Check(controller != null, "The player Animator controller is available.");
        var states = controller.layers[0].stateMachine.states;
        AnimationClip Clip(string state, string name)
        {
            string path = ClipFolder + name + ".fbx";
            var clip = states.Single(entry => entry.state.name == state).state.motion as AnimationClip;
            Check(clip != null && AssetDatabase.GetAssetPath(clip) == path
                && clip.humanMotion && !clip.isLooping && clip.length > 0f,
                state + " uses its imported, non-looping Humanoid clip.");
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            Check(importer != null && importer.motionNodeName == "root", name + " extracts motion from the source root bone.");
            var avatar = importer.sourceAvatar ?? AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().SingleOrDefault();
            Check(avatar != null && avatar.isValid && avatar.isHuman, name + " has a valid source Avatar.");
            return clip;
        }
        Clip("Jump", "Jump_Start");
        var air = Clip("Jump_Air", "Jump_Air");
        var land = Clip("Jump_Land", "Jump_Land");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character.fbx");
        Check(model != null, "The actual player model is available.");

        // PlayerController.IsGround queries the default physics scene, which excludes preview scenes.
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var data = ScriptableObject.CreateInstance<FireFormActionDataSO>();
        try
        {
            var floor = new GameObject("Jump regression ground");
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.layer = LayerMask.NameToLayer("Ground");
            Vector3 origin = new Vector3(512f, 256f, 512f);
            floor.transform.position = origin - Vector3.up * .5f;
            floor.AddComponent<BoxCollider>().size = new Vector3(8f, 1f, 8f);

            var host = new GameObject("Jump regression player");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.SetActive(false);
            var rig = Object.Instantiate(model, host.transform);
            var animator = rig.GetComponentInChildren<Animator>(true);
            Check(animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman,
                "The player's target Avatar is valid.");
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.fireEvents = false;
            var player = host.AddComponent<PlayerController>();
            var input = host.GetComponent<PlayerInputHandler>();
            var capsule = host.GetComponent<CharacterController>();
            var stats = host.GetComponent<PlayerStatManager>();
            capsule.center = Vector3.up;
            capsule.height = 2f;
            capsule.radius = .35f;
            SetProperty(player, nameof(player.Controller), capsule);
            SetProperty(player, nameof(player.Animator), animator);
            SetProperty(player, nameof(player.Input), input);
            SetProperty(player, nameof(player.StatManager), stats);
            SetProperty(player, nameof(player.Movement), host.GetComponent<PlayerMovement>());
            player.Movement.Initialize(player);
            SetField(player, "_groundLayer", (LayerMask)LayerMask.GetMask("Ground"));

            data.SkillSteps = new List<WeaponActionData> { new WeaponActionData() };
            var forms = new FormManager(player);
            var form = new ProbeForm(data);
            var alternate = new ProbeForm(data);
            forms.ChangeForm(form);
            forms.AddTransition(alternate, () => false);
            SetProperty(player, nameof(player.FormManager), forms);
            host.SetActive(true);
            animator.Rebind();
            animator.Update(0f);

            void Place(float height)
            {
                host.transform.position = origin + Vector3.up * height;
                Physics.SyncTransforms();
            }
            AnimatorStateInfo Playing()
            {
                animator.Update(0f);
                return animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
            }
            void Expect(string state)
                => Check(Playing().shortNameHash == Animator.StringToHash(state), "Expected " + state + " animation.");
            void Advance(float seconds)
            {
                while (seconds > .01f)
                {
                    animator.Update(.01f);
                    seconds -= .01f;
                }
                animator.Update(seconds);
            }
            void Sample(string state, float time)
            {
                animator.Play(state, 0, time);
                animator.Update(0f);
            }
            void Tick() => player.StateMachine.Update(true, false);
            void Walk(Vector2 direction) => SetProperty(input, nameof(input.MoveInput), direction);

            Place(.05f);
            Check(player.IsGround(), "The isolated capsule detects the test floor.");
            var machine = new PlayerStateMachineSetup(player).Build();
            SetProperty(player, nameof(player.StateMachine), machine);
            animator.Update(.12f);
            var normalIdle = machine.CurrentState;

            foreach (float gravity in new[] { -15f, -25f })
            {
                player.Gravity = gravity;
                animator.applyRootMotion = gravity == -15f;
                // Check the blend from Idle and a restart with an old Jump pose still active.
                Sample(gravity == -15f ? "Idle" : "Jump", .75f);
                animator.speed = 2f;
                input.JumpBuffer.Set();
                Tick();
                Expect("Jump");
                Check(Playing().normalizedTime < .05f && !animator.applyRootMotion && Mathf.Approximately(animator.speed, 1f),
                    "A new jump restarts the clip and restores physics-driven playback.");
                float launchVelocity = Mathf.Sqrt(player.JumpForce * -2f * gravity);
                Check(!input.JumpBuffer.IsActive && Mathf.Approximately(player.VerticalVelocity, launchVelocity),
                    "Jump input is consumed and the impulse matches the scene gravity.");
                const float blendBudget = .03f;
                animator.Update(blendBudget);
                Check(!animator.IsInTransition(0)
                    && animator.GetCurrentAnimatorStateInfo(0).shortNameHash == Animator.StringToHash("Jump"),
                    "Jump fully replaces the previous pose within 0.03 seconds of input.");
                Place(3f);
                float ascentTime = launchVelocity / -gravity;
                Advance(ascentTime - blendBudget);
                Check(Playing().shortNameHash == Animator.StringToHash("Jump_Air"),
                    $"The start clip hands off to Air within the {ascentTime:F3}-second ascent at gravity {gravity}.");
                float beforeFall = Playing().normalizedTime;
                player.VerticalVelocity = -1f;
                Tick();
                Check(machine.CurrentState is PlayerFallState && Playing().normalizedTime >= beforeFall - .001f,
                    "Apex enters Fall without restarting the Air clip.");
                Advance(air.length + .1f);
                Expect("Jump_Air");

                Place(.05f);
                Tick();
                Check(machine.CurrentState is PlayerIdleState, "Stationary landing is immediately in gameplay Idle.");
                Expect("Jump_Land");
                Check(Playing().normalizedTime < .05f, "Landing starts at its first frame.");
                Advance(land.length + .12f);
                Expect("Idle");
                machine.ChangeState(new PlayerFallState(player));
                Walk(Vector2.up);
                Tick();
                Expect("Run");
                Check(machine.CurrentState is PlayerRunState, "Moving landing enters Run directly.");
                Walk(Vector2.zero);
                Tick();
                Check(ReferenceEquals(machine.CurrentState, normalIdle), "Run exits through the ordinary Idle instance.");
            }

            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var lowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            Check(hips != null && lowerLeg != null, "The retargeted rig exposes its hips and lower leg.");
            Sample("Idle", 0f);
            Quaternion groundedLeg = lowerLeg.localRotation;
            Sample("Jump_Air", 0f);
            float hipStart = rig.transform.InverseTransformPoint(hips.position).y;
            Sample("Jump_Air", .53f);
            float hipMid = rig.transform.InverseTransformPoint(hips.position).y;
            Check(Mathf.Abs(hipMid - hipStart) < .35f * Mathf.Max(.5f, animator.humanScale)
                && Quaternion.Angle(groundedLeg, lowerLeg.localRotation) > 1f,
                $"Air retargets the leg pose without adding the source root arc to the hips (shift {hipMid - hipStart:F3}).");

            Sample("Idle", 0f);
            Place(3f);
            player.VerticalVelocity = -1f;
            Tick();
            Check(machine.CurrentState is PlayerFallState, "Stepping off a ledge enters Fall without a jump input.");
            Expect("Jump_Air");

            void Land()
            {
                Place(3f);
                player.VerticalVelocity = -1f;
                machine.ChangeState(new PlayerFallState(player));
                Advance(.08f);
                Place(.05f);
                Tick();
                Expect("Jump_Land");
            }

            Land();
            Walk(Vector2.right);
            Tick();
            Expect("Run");
            Walk(Vector2.zero);
            Land();
            input.AttackBuffer.Set();
            Tick();
            Expect("Fire1");
            Land();
            input.DashBuffer.Set();
            Tick();
            Expect("Backstep");
            Land();
            input.SkillBuffer.Set();
            Tick();
            Expect("WaterSkill");
            Land();
            input.JumpBuffer.Set();
            Tick();
            Expect("Jump");
            Land();
            Check(forms.TrySwitchForm(alternate), "Landing allows the ordinary form change.");

            Place(3f);
            machine.ChangeState(new PlayerFallState(player));
            SetField(player, "_coyoteTimer", .05f);
            input.JumpBuffer.Set();
            Tick();
            Check(machine.CurrentState is PlayerJumpState && !player.CanJump(), "The coyote jump works and consumes the window.");
            Expect("Jump");
            machine.ChangeState(new PlayerFallState(player));
            Place(.05f);
            input.JumpBuffer.Set();
            Tick();
            Expect("Jump");
            Check(machine.CurrentState is PlayerJumpState, "A buffered re-jump on contact takes priority over landing.");

            Place(3f);
            machine.ChangeState(new PlayerFallState(player));
            input.AttackBuffer.Set();
            Tick();
            Expect("FireAirStart");
            Place(.05f);
            machine.CurrentState.Update();
            Expect("FireAirEnd");
            machine.CurrentState.Update();
            Tick();
            Expect("Idle");

            Land();
            SetProperty(player, nameof(player.PendingHitReaction), PlayerHitReaction.Hit);
            machine.Update(false, false);
            Expect("Hit");
            Land();
            SetField(stats, "_currentHp", 0f);
            machine.Update(false, false);
            Expect("Die");

            return $"PASS: jump animation ({checks} checks, shipped clips and Avatar, player transitions and landing interrupts).";
        }
        finally
        {
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            EditorSceneManager.CloseScene(scene, true);
            Object.DestroyImmediate(data);
        }
    }

    private static void SetProperty(object target, string name, object value)
        => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);

    private static void SetField(object target, string name, object value)
        => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
}
