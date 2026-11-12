using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks the shipped skill movement through the controller and Water's real swept hit path.</summary>
public static class SkillMotionRegression
{
    private const string ActionFolder = "Assets/_Project/Scripts/FormActions/";
    private const string ControllerPath = "Assets/_Project/Animations/AnimController/BDJ.controller";
    private const float PositionTolerance = .003f;
    private static readonly Vector3 Origin = new(810f, 230f, 0f);
    private static readonly FieldInfo Timer = typeof(BaseForm).GetField("_timer",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private sealed class MotionProbe : BaseForm
    {
        public MotionProbe(WeaponActionDataSO data) : base(data) { }
        protected override void ProcessHit(WeaponActionData step) { }
    }

    [MenuItem("Tools/Studio Baekho/Combat/Run Skill Motion Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Skill motion regression requires Edit Mode.");

        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Skill motion: " + message);
            checks++;
        }

        void Near(float actual, float expected, string message, float tolerance = PositionTolerance)
            => Check(Mathf.Abs(actual - expected) <= tolerance,
                $"{message} (actual {actual:F4}, expected {expected:F4})");

        var controllerAsset = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var fireData = AssetDatabase.LoadAssetAtPath<FireFormActionDataSO>(ActionFolder + "FireFormAction.asset");
        var waterData = AssetDatabase.LoadAssetAtPath<WaterFormActionDataSO>(ActionFolder + "WaterFormV2.asset");
        var labData = AssetDatabase.LoadAssetAtPath<WaterFormActionDataSO>(
            ActionFolder + "WaterFormLab/WaterFormActionSO_V2.asset");
        Check(controllerAsset != null && fireData != null && waterData != null && labData != null,
            "The shipped controller and live action assets are available.");

        foreach (var (data, machineName, duration) in new (WeaponActionDataSO, string, float)[]
                 { (fireData, "Fire", 4f), (waterData, "Water", .9f), (labData, "Water", .9f) })
        {
            var step = data.SkillSteps.Single();
            var machine = controllerAsset.layers[0].stateMachine.stateMachines
                .Single(child => child.stateMachine.name == machineName).stateMachine;
            var state = machine.states.Single(child => child.state.name == step.AnimationName).state;
            Check(state.motion is AnimationClip && state.speed == 1f,
                $"{data.name} plays the sampled clip at the skill clock's speed.");
            Check(step.UseDistanceMovement && step.MovementStartTime == 0f,
                $"{data.name} replays the authored distance curve from the start of its skill.");
            Near(step.Duration, duration, $"{data.name} retains its mechanical duration.");
            Near(step.MovementEndTime, duration, $"{data.name} samples within its mechanical window.");

            var source = SkillMotionSetup.Sample((AnimationClip)state.motion, duration);
            var profile = new AttackMotionProfile();
            profile.Reset(step, false);
            Near(profile.TotalDistance, source.TotalDistance, $"{data.name} matches the Avatar's peak advance.", .005f);
            float largestError = 0f;
            float forwardPeak = 0f;
            for (int i = 0; i < source.Times.Length; i++)
            {
                forwardPeak = Mathf.Max(forwardPeak, source.ForwardPositions[i]);
                largestError = Mathf.Max(largestError, Mathf.Abs(profile.DistanceAt(source.Times[i]) - forwardPeak));
            }
            Check(largestError < .05f, $"{data.name} follows the clip throughout its action (error {largestError:F4} m).");
        }

        foreach (string name in new[] { "WoodFormAction", "IronFormAction", "EarthFormAction" })
        {
            var data = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(ActionFolder + name + ".asset");
            Check(data != null && data.SkillSteps.All(step => string.IsNullOrEmpty(step.AnimationName)
                    && !step.UseDistanceMovement), $"{name} retains its existing movement.");
        }

        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var statsData = ScriptableObject.CreateInstance<PlayerStatData>();
        var isolatedWater = Object.Instantiate(waterData);
        var previousSettings = CombatSettings.Active;
        var activeSettings = typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active));
        ElementEffectiveManager elementTable = null;
        try
        {
            GameObject Create(string name)
            {
                var obj = new GameObject(name);
                SceneManager.MoveGameObjectToScene(obj, scene);
                return obj;
            }

            var host = Create("__SkillMotionPlayer");
            host.SetActive(false);
            var player = host.AddComponent<PlayerController>();
            var capsule = host.GetComponent<CharacterController>();
            capsule.radius = .25f;
            capsule.minMoveDistance = 0f;
            var stats = host.GetComponent<PlayerStatManager>();
            SetProperty(player, nameof(PlayerController.Controller), capsule);
            SetProperty(player, nameof(PlayerController.StatManager), stats);
            player.Gravity = player.VerticalVelocity = 0f;
            host.SetActive(true);

            void StartSkill(BaseForm form, float speed)
            {
                capsule.enabled = false;
                host.transform.SetPositionAndRotation(Origin, Quaternion.identity);
                capsule.enabled = true;
                Physics.SyncTransforms();
                statsData.baseAtkSpd = speed;
                stats.Initialize(statsData);
                form.Equip(player);
                form.BeginSkill();
            }

            bool TickAt(BaseForm form, float elapsed)
            {
                // Edit Mode cannot set Time.deltaTime per simulated frame. Offset the timer so the
                // production update reaches the same action time that the simulated frame would.
                Timer.SetValue(form, elapsed - player.ActionDeltaTime * stats.GetStat(StatType.ST_ATK_SPD));
                form.UpdateSkill(out bool complete);
                return complete;
            }

            float Distance() => host.transform.position.z - Origin.z;

            foreach (var data in new WeaponActionDataSO[] { fireData, waterData })
            {
                var step = data.SkillSteps[0];
                var form = new MotionProbe(data);
                foreach (int fps in new[] { 30, 60, 120 })
                foreach (float speed in new[] { .5f, 1f, 2f })
                {
                    StartSkill(form, speed);
                    int frames = Mathf.CeilToInt(step.Duration * fps / speed);
                    bool complete = false;
                    for (int frame = 1; frame <= frames; frame++)
                        complete = TickAt(form, frame * speed / fps);
                    if (!complete) complete = TickAt(form, step.Duration + .0001f);
                    Check(complete, $"{data.name} completes at {fps} fps and {speed}x speed.");
                    Near(Distance(), step.ForwardDistance,
                        $"{data.name} moves the same distance at {fps} fps and {speed}x speed.");
                    form.EndSkill();
                }
            }

            var waterStep = isolatedWater.SkillSteps[0];
            waterStep.Feedback = new AttackFeedbackSettings();
            var profileWater = new AttackMotionProfile();
            profileWater.Reset(waterStep, false);
            var wall = Create("__SkillMotionWall");
            wall.transform.position = Origin + Vector3.forward * .65f;
            wall.AddComponent<BoxCollider>().size = new Vector3(4f, 4f, .1f);
            var waterProbe = new MotionProbe(isolatedWater);
            StartSkill(waterProbe, 1f);
            TickAt(waterProbe, .30f);
            float blockedDistance = Distance();
            Check(blockedDistance < profileWater.DistanceAt(.30f) - .2f, "The capsule stops at the wall.");
            wall.SetActive(false);
            Physics.SyncTransforms();
            TickAt(waterProbe, .45f);
            Near(Distance() - blockedDistance, profileWater.Delta(.30f, .45f),
                "Removing a wall releases only the next frame's movement.");
            waterProbe.EndSkill();

            var rig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character.fbx"),
                host.transform);
            rig.transform.localScale = Vector3.one * SkillMotionSetup.PlayerModelScale;
            var animator = rig.GetComponentInChildren<Animator>(true);
            animator.applyRootMotion = false;
            animator.fireEvents = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = controllerAsset;
            SetProperty(player, nameof(PlayerController.Animator), animator);
            animator.Rebind();
            animator.Update(0f);
            var clock = CombatActorClock.GetOrCreate(player);
            clock.BindAnimator(animator);
            SetProperty(player, nameof(PlayerController.ActorClock), clock);

            var tableAsset = AssetDatabase.LoadAssetAtPath<ElementEffectiveManager>(
                "Assets/_Project/CombatData/ElementEffectiveManager.asset");
            Check(tableAsset != null, "The real element table is available for Water's damage path.");
            elementTable = Object.Instantiate(tableAsset);
            Check(elementTable.ValidateTable(), "The sweep fixture has a complete element table.");
            var settingsObject = Create("__SkillMotionCombatSettings");
            settingsObject.SetActive(false);
            var settings = settingsObject.AddComponent<CombatSettings>();
            typeof(CombatSettings).GetField("_elementTable", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(settings, elementTable);
            activeSettings.SetValue(null, settings);

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            Check(enemyLayer >= 0, "The sweep fixture can use the Enemy layer.");
            var target = Create("__SkillMotionSweepTarget");
            target.layer = enemyLayer;
            target.AddComponent<SphereCollider>().radius = .08f;
            var receiver = target.AddComponent<ActionTimingRegressionTarget>();
            var water = new WaterForm(isolatedWater);
            int originalExclusions = capsule.excludeLayers;
            StartSkill(water, 1f);
            int enemyMask = 1 << enemyLayer;
            Check(((int)capsule.excludeLayers & enemyMask) != 0, "Water passes through enemies during execution.");
            target.transform.position = capsule.bounds.center + Vector3.forward * .3f;
            Physics.SyncTransforms();
            Check(player.ActionDeltaTime > 0f, "The editor supplied action time for the active sweep.");
            TickAt(water, .1f);
            Check(receiver.Calls == 0, "Crossing an enemy before the first hit does not damage it early.");
            Check(capsule.bounds.center.z > target.transform.position.z,
                "The fixture crosses the target before the first active hit.");
            TickAt(water, .45f);
            Check(receiver.Calls == 1,
                $"The first active sweep hits the enemy crossed before the current position (hits {receiver.Calls}).");
            target.transform.position = capsule.bounds.center;
            Physics.SyncTransforms();
            TickAt(water, .6f);
            Check(receiver.Calls == 1, "Staying in the sweep does not hit an enemy twice in a cast.");

            float beforePause = Distance();
            float timeBeforePause = (float)Timer.GetValue(water);
            clock.Request(player, 60f, 0f, HitStopStacking.Maximum);
            water.UpdateSkill(out _);
            Check(clock.IsStopped && (float)Timer.GetValue(water) == timeBeforePause,
                "Hit stop pauses the skill clock.");
            Near(Distance(), beforePause, "Hit stop pauses skill displacement.");
            clock.ClearStops();

            animator.Update(0f);
            animator.Update(.95f);
            Check(!TickAt(water, waterStep.Duration + .0001f) && water.IsCurrentActionExecutionComplete,
                "Water stops mechanics at 0.9 seconds while the actual animation continues.");
            Check((int)capsule.excludeLayers == originalExclusions,
                "Water restores collision before the animation tail.");
            Near(Distance(), waterStep.ForwardDistance, "Water reaches its authored maximum.");
            var lateTarget = Create("__SkillMotionTailTarget");
            lateTarget.layer = enemyLayer;
            lateTarget.transform.position = capsule.bounds.center;
            lateTarget.AddComponent<SphereCollider>().radius = .08f;
            var lateReceiver = lateTarget.AddComponent<ActionTimingRegressionTarget>();
            Physics.SyncTransforms();
            TickAt(water, 1f);
            Check(lateReceiver.Calls == 0, "An enemy entering during the visual tail takes no swept damage.");
            Near(Distance(), waterStep.ForwardDistance, "The visual tail cannot move the capsule.");
            water.EndSkill();

            StartSkill(water, 1f);
            TickAt(water, .1f);
            water.EndSkill();
            Check((int)capsule.excludeLayers == originalExclusions,
                "An early skill exit restores the original collision exclusions.");
        }
        finally
        {
            activeSettings.SetValue(null, previousSettings);
            if (elementTable != null) Object.DestroyImmediate(elementTable);
            Object.DestroyImmediate(isolatedWater);
            Object.DestroyImmediate(statsData);
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            EditorSceneManager.CloseScene(scene, true);
        }
        return $"PASS: skill motion ({checks} checks).";
    }

    private static void SetProperty(PlayerController player, string name, object value)
        => typeof(PlayerController).GetProperty(name).SetValue(player, value);
}
