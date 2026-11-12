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

public static class AirAttackLandingRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string StairRoomPath = "Assets/ARPGMapRefresh/Prefabs/Rooms/normal_0211f42f761304940bb6edfc23620fea.prefab";
    private const string StairRampPath = "32_industrial_staircase_1m/WalkableRamp";

    [Serializable]
    private sealed class Report
    {
        public string status = "PASS";
        public int checks;
        public int[] landingFrameRates = { 30, 60, 120 };
    }

    private sealed class ProbeForm : BaseForm
    {
        public int Starts, Loops, Finishes, Ends;
        public bool FinishComplete;

        public ProbeForm() : base(null) { }
        public override void BeginAirAttack()
        {
            Starts++;
            FinishComplete = false;
        }
        public override void UpdateAirAttackStart(out bool isComplete) => isComplete = true;
        public override void BeginAirAttackLoop() => Loops++;
        public override void BeginAirAttackFinish() => Finishes++;
        public override void UpdateAirAttack(out bool isComplete) => isComplete = FinishComplete;
        public override void EndAirAttack() => Ends++;
    }

    [MenuItem("Tools/Studio Baekho/Combat/Run Air Attack Landing Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Air attack landing regression requires Edit Mode.");

        var report = new Report();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Air attack landing: " + message);
            report.checks++;
        }

        var updateDescent = typeof(PlayerAirAttackState).GetMethod("UpdateDescent", Hidden);
        var updateBlockedDescent = typeof(PlayerAirAttackState).GetMethod("UpdateBlockedDescent", Hidden);
        Check(updateDescent != null && updateBlockedDescent != null, "The descent methods are available.");

        int groundLayer = LayerMask.NameToLayer("Ground");
        int playerLayer = LayerMask.NameToLayer("Player");
        int enemyMask = LayerMask.GetMask("Enemy");
        int originalExclusions = LayerMask.GetMask("Water");
        Check(groundLayer >= 0 && playerLayer >= 0 && enemyMask != 0 && originalExclusions != 0,
            "The project physics layers are available.");

        // The production ground query uses the default physics scene.
        Scene previousScene = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var animatorAssets = new List<Object>();
        try
        {
            var origin = new Vector3(512f, 256f, 512f);
            var flat = origin;
            var step = origin + Vector3.right * 16f;
            var prop = origin + Vector3.right * 32f;
            var trigger = origin + Vector3.right * 48f;
            var openAir = origin + Vector3.right * 64f;

            BoxCollider Box(string name, Vector3 center, Vector3 size, int layer)
            {
                var obj = new GameObject(name) { layer = layer };
                SceneManager.MoveGameObjectToScene(obj, scene);
                obj.transform.position = center;
                var box = obj.AddComponent<BoxCollider>();
                box.size = size;
                return box;
            }

            Box("Floor", flat - Vector3.up * .5f, new Vector3(4f, 1f, 4f), groundLayer);
            Box("Lower stair tread", step - Vector3.up * .5f, new Vector3(4f, 1f, 4f), groundLayer);
            // The capsule center is outside the raised tread, while its foot still overlaps the edge.
            Box("Raised stair tread", step + new Vector3(1.1f, .25f, 0f),
                new Vector3(2f, .5f, 4f), groundLayer);
            Box("Default layer prop", prop + new Vector3(1.1f, .4f, 0f),
                new Vector3(2f, .8f, 4f), 0);
            var triggerBox = Box("Ground layer trigger", trigger - Vector3.up * .5f,
                new Vector3(4f, 1f, 4f), groundLayer);
            triggerBox.isTrigger = true;

            var room = AssetDatabase.LoadAssetAtPath<GameObject>(StairRoomPath);
            var sourceStair = room != null ? room.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "V3RestoredStructure" && t.Find(StairRampPath) != null) : null;
            Check(sourceStair != null, "The authored room stair is available.");
            var authoredStair = Object.Instantiate(sourceStair.gameObject);
            SceneManager.MoveGameObjectToScene(authoredStair, scene);
            authoredStair.transform.SetPositionAndRotation(origin + Vector3.right * 80f, sourceStair.rotation);
            authoredStair.transform.localScale = sourceStair.lossyScale;
            var ramp = authoredStair.transform.Find(StairRampPath).GetComponent<MeshCollider>();
            Check(ramp != null && ramp.sharedMesh != null && ramp.enabled && !ramp.isTrigger
                && ramp.gameObject.layer == groundLayer, "The authored ramp has solid Ground collision.");
            Bounds rampBounds = ramp.sharedMesh.bounds;
            Vector3 rampMiddle = ramp.transform.TransformPoint(rampBounds.center);
            Vector3 rampSide = ramp.transform.TransformPoint(rampBounds.center
                + Vector3.right * (rampBounds.extents.x - .05f));
            Vector3 rampTop = ramp.transform.TransformPoint(new Vector3(rampBounds.center.x,
                rampBounds.max.y, rampBounds.max.z));
            var upperFloor = Box("Upper landing at the authored ramp seam",
                rampTop + ramp.transform.forward - Vector3.up * .5f, new Vector3(2f, 1f, 2f), groundLayer);
            upperFloor.transform.rotation = ramp.transform.rotation;

            var host = new GameObject("Air attack landing player") { layer = playerLayer };
            SceneManager.MoveGameObjectToScene(host, scene);
            host.SetActive(false);
            var player = host.AddComponent<PlayerController>();
            var capsule = host.GetComponent<CharacterController>();
            capsule.height = 1.56f;
            capsule.radius = .23f;
            capsule.center = Vector3.up * .78f;
            capsule.skinWidth = .08f;
            capsule.slopeLimit = 45f;
            capsule.stepOffset = .45f;
            capsule.minMoveDistance = .001f;
            capsule.excludeLayers = originalExclusions;
            var input = host.GetComponent<PlayerInputHandler>();
            var stats = host.GetComponent<PlayerStatManager>();
            SetProperty(player, nameof(player.Controller), capsule);
            SetProperty(player, nameof(player.Input), input);
            SetProperty(player, nameof(player.StatManager), stats);
            typeof(PlayerController).GetField("_groundLayer", Hidden).SetValue(player, (LayerMask)(1 << groundLayer));

            var animator = host.AddComponent<Animator>();
            var animatorController = new AnimatorController { name = "Air attack landing regression animator" };
            animatorAssets.Add(animatorController);
            animatorController.AddLayer("Base Layer");
            var animatorMachine = animatorController.layers[0].stateMachine;
            animatorAssets.Add(animatorMachine);
            foreach (string name in new[] { "Idle", "Run", "Hit", "Die" })
                animatorAssets.Add(animatorMachine.AddState(name));
            animator.runtimeAnimatorController = animatorController;
            SetProperty(player, nameof(player.Animator), animator);

            var forms = new FormManager(player);
            var form = new ProbeForm();
            forms.ChangeForm(form);
            SetProperty(player, nameof(player.FormManager), forms);
            host.SetActive(true);
            animator.Rebind();
            animator.Update(0f);
            var machine = new PlayerStateMachineSetup(player).Build();
            SetProperty(player, nameof(player.StateMachine), machine);

            void Place(Vector3 position)
            {
                host.transform.position = position;
                Physics.SyncTransforms();
            }

            PlayerAirAttackState Begin(Vector3 position)
            {
                Place(position);
                machine.ChangeState(new PlayerFallState(player));
                int starts = form.Starts;
                int loops = form.Loops;
                input.AttackBuffer.Set();
                machine.Update(true, false);
                Check(machine.CurrentState is PlayerAirAttackState && !input.AttackBuffer.IsActive,
                    "Buffered air attack enters through the real fall transition.");
                var attack = (PlayerAirAttackState)machine.CurrentState;
                // Edit Mode can retain an unrelated frame delta. Start the loop without moving;
                // Descend supplies the simulated frame duration for every actual move.
                bool previousPause = player.IsShopPaused;
                SetProperty(player, nameof(player.IsShopPaused), true);
                try { attack.Update(); }
                finally { SetProperty(player, nameof(player.IsShopPaused), previousPause); }
                Check(form.Starts == starts + 1 && form.Loops == loops + 1
                    && !attack.IsFinishing && !attack.IsComplete,
                    $"Expected fresh descent (starts {form.Starts - starts}, loops {form.Loops - loops}, "
                    + $"finishing {attack.IsFinishing}, complete {attack.IsComplete}, "
                    + $"position {host.transform.position}, editor delta {Time.deltaTime:F3}).");
                Check(capsule.excludeLayers.value == (originalExclusions | enemyMask),
                    "Descending preserves the original exclusions and ignores enemies.");
                return attack;
            }

            void Descend(PlayerAirAttackState attack, float deltaTime)
                => updateDescent.Invoke(attack, new object[] { deltaTime });

            void Finish(PlayerAirAttackState attack, int finishes, bool run)
            {
                int ends = form.Ends;
                Check(attack.IsFinishing && player.VerticalVelocity == 0f && form.Finishes == finishes + 1,
                    "Landing begins the finish exactly once and ends downward motion.");
                attack.Update();
                Check(!attack.IsComplete && form.Finishes == finishes + 1,
                    "Finishing does not replay the impact or complete before the form is ready.");
                form.FinishComplete = true;
                attack.Update();
                Check(attack.IsComplete && capsule.excludeLayers.value == originalExclusions,
                    "Completed finish restores the original collision exclusions.");
                SetProperty(input, nameof(input.MoveInput), run ? Vector2.right : Vector2.zero);
                machine.Update(true, false);
                Check((run ? machine.CurrentState is PlayerRunState : machine.CurrentState is PlayerIdleState)
                    && form.Ends == ends + 1, "Normal completion resumes the requested locomotion and cleans up the form once.");
                SetProperty(input, nameof(input.MoveInput), Vector2.zero);
            }

            void Land(string surface, Vector3 at, float minHeight, int rate, bool requiresBelow = false)
            {
                var attack = Begin(at + Vector3.up * 2f);
                int finishes = form.Finishes;
                for (int frame = 0; frame < rate && !attack.IsFinishing; frame++)
                    Descend(attack, 1f / rate);

                Check(attack.IsFinishing && host.transform.position.y - at.y >= minHeight,
                    $"The capsule landed on {surface} at {rate} FPS (offset {host.transform.position.y - at.y:F3}, flags {capsule.collisionFlags}).");
                if (requiresBelow)
                    Check(!player.IsGround() && (capsule.collisionFlags & CollisionFlags.Below) != 0,
                        $"A real below collision finishes the attack even without a Ground hit at {rate} FPS.");
                Finish(attack, finishes, requiresBelow && rate == 60);
            }

            foreach (int rate in report.landingFrameRates)
            {
                Land("flat ground", flat, -.1f, rate);
                Land("the stair tread edge", step, .25f, rate);
                Land("the Default prop edge", prop, .5f, rate, true);
                Land("the authored ramp center", rampMiddle, -.2f, rate);
                Land("the authored ramp side", rampSide, -.2f, rate);
                Land("the authored ramp's upper seam", rampTop, -.2f, rate);
            }

            var airAttack = Begin(trigger + Vector3.up * 2f);
            int previousFinishes = form.Finishes;
            Place(trigger + Vector3.up * (.05f + player.AirAttackDescentSpeed / 120f));
            Descend(airAttack, 1f / 120f);
            Vector3 probeOrigin = host.transform.position + Vector3.up * capsule.radius;
            Check(Physics.SphereCast(probeOrigin, capsule.radius, Vector3.down, out var hit, .1f,
                    1 << groundLayer, QueryTriggerInteraction.Collide) && hit.collider == triggerBox,
                "The trigger overlaps the original landing query distance.");
            Check(!airAttack.IsFinishing && form.Finishes == previousFinishes,
                "The air attack passes through a Ground trigger without impacting.");
            Place(openAir + Vector3.up * 100f);
            float startHeight = host.transform.position.y;
            for (int i = 0; i < 180; i++) Descend(airAttack, 1f / 60f);
            Check(!airAttack.IsFinishing && host.transform.position.y < startHeight - 70f,
                "An unobstructed three-second descent keeps falling without a false landing.");

            bool Blocked(CollisionFlags flags, float requested, float actual, float deltaTime)
                => (bool)updateBlockedDescent.Invoke(airAttack, new object[] { flags, requested, actual, deltaTime });

            for (int i = 0; i < 20; i++)
            {
                if (Blocked(CollisionFlags.Sides, .2f, .1f, .02f))
                    throw new InvalidOperationException("Air attack landing: Sliding down a wall ended the descent.");
            }
            Check(!Blocked(CollisionFlags.Above, .2f, 0f, .16f),
                "A ceiling-only contact does not qualify as a stalled descent.");
            Check(!Blocked(CollisionFlags.Sides, .2f, 0f, .1f),
                "A short stalled contact does not meet the recovery threshold.");
            Vector3 frozenPosition = host.transform.position;
            for (int i = 0; i < 20; i++)
            {
                Descend(airAttack, 0f);
                if (Blocked(CollisionFlags.Sides, .2f, 0f, 0f)
                    || Blocked(CollisionFlags.Sides, .0005f, 0f, .02f))
                    throw new InvalidOperationException("Air attack landing: An ineligible move advanced the stalled descent.");
            }
            Check(host.transform.position == frozenPosition && !airAttack.IsFinishing,
                "A frozen action makes no descent or phase progress.");
            Check(Blocked(CollisionFlags.Sides, .2f, 0f, .06f),
                "The stalled contact resumes from its pre-freeze time.");
            Check(!Blocked(CollisionFlags.None, .2f, 0f, .02f)
                && !Blocked(CollisionFlags.Sides, .2f, 0f, .1f),
                "Losing side contact resets the accumulated stalled time.");

            int previousEnds = form.Ends;
            SetProperty(player, nameof(player.PendingHitReaction), PlayerHitReaction.Hit);
            machine.Update(false, false);
            Check(machine.CurrentState is PlayerHitState && capsule.excludeLayers.value == originalExclusions
                && form.Ends == previousEnds + 1,
                "A hit reaction interrupts descent, restores exclusions, and cleans up the form.");
            Check(ReferenceEquals(airAttack, Begin(openAir + Vector3.up * 2f)),
                "The state machine reuses the attack instance on re-entry.");
            Check(!Blocked(CollisionFlags.Sides, .2f, 0f, .1f),
                "Re-entry clears the previous attack's stalled time.");
            Check(Blocked(CollisionFlags.Sides, .2f, 0f, .06f),
                "A sustained side contact with no downward progress meets the recovery threshold.");
            Check(!Blocked(CollisionFlags.Sides, .2f, .1f, .02f)
                && !Blocked(CollisionFlags.Sides, .2f, 0f, .1f),
                "Resuming downward movement during side contact resets the stalled time.");
            previousEnds = form.Ends;
            stats.ConsumeHealth(stats.CurrentHp, 0f);
            machine.Update(false, false);
            Check(machine.CurrentState is PlayerDieState && capsule.excludeLayers.value == originalExclusions
                && form.Ends == previousEnds + 1 && !capsule.enabled,
                "Death exits the attack and restores exclusions before disabling movement.");
            return JsonUtility.ToJson(report);
        }
        finally
        {
            if (previousScene.IsValid() && previousScene.isLoaded)
                SceneManager.SetActiveScene(previousScene);
            EditorSceneManager.CloseScene(scene, true);
            for (int i = animatorAssets.Count - 1; i >= 0; i--)
                if (animatorAssets[i] != null) Object.DestroyImmediate(animatorAssets[i]);
        }
    }

    private static void SetProperty(object target, string name, object value)
        => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);
}
