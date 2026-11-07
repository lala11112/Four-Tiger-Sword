using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks fire-only pursuit deadlines using the production movement and collider path.</summary>
public static class FireAttackMotionRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const float Tolerance = .003f;

    [MenuItem("Tools/Studio Baekho/Combat/Run Fire Attack Motion Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Fire attack motion regression requires Edit Mode.");
        var originalScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var objects = new List<Object>();
        GameObject host = null, target = null;
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Fire attack motion: " + message);
            checks++;
        }
        void Near(float actual, float expected, string message)
            => Check(Mathf.Abs(actual - expected) < Tolerance,
                message + $" (actual {actual}, expected {expected})");
        T Data<T>() where T : ScriptableObject
        {
            var data = ScriptableObject.CreateInstance<T>();
            objects.Add(data);
            return data;
        }
        try
        {
            var origin = new Vector3(1000f, 1000f, 1000f);
            host = new GameObject("__FireAttackMotionRegression_Player");
            host.SetActive(false);
            SceneManager.MoveGameObjectToScene(host, scene);
            host.transform.position = origin;
            var player = host.AddComponent<PlayerController>();
            var controller = host.GetComponent<CharacterController>();
            controller.minMoveDistance = 0f;
            typeof(PlayerController).GetProperty("Controller").SetValue(player, controller);
            player.Gravity = player.VerticalVelocity = 0f;
            host.SetActive(true);
            target = new GameObject("__FireAttackMotionRegression_Target");
            SceneManager.MoveGameObjectToScene(target, scene);
            var receiver = target.AddComponent<FireAttackMotionRegressionTarget>();
            var targetCollider = target.AddComponent<BoxCollider>();
            targetCollider.size = Vector3.one;
            var targetStats = target.AddComponent<EnemyStat>();
            var fire = new FireForm(Data<FireFormActionDataSO>());
            var others = new BaseForm[]
            {
                new WaterForm(Data<WaterFormActionDataSO>()),
                new WoodForm(Data<WoodFormActionDataSO>()),
                new IronForm(Data<IronFormActionDataSO>()),
                new EarthForm(Data<EarthFormActionDataSO>())
            };

            var authored = AssetDatabase.LoadAssetAtPath<FireFormActionDataSO>(
                "Assets/_Project/Scripts/FormActions/FireFormAction.asset");
            Check(authored != null && authored.ComboSteps.Count == 3, "The authored fire combo has three stages.");
            var authoredForm = new FireForm(authored);
            Field(authoredForm, "_normalAttackActive", true);
            float[] recovery = { .7f, .5f, .65f };
            for (int stage = 0; stage < recovery.Length; stage++)
            {
                Field(authoredForm, "_comboStep", stage);
                Field(authoredForm, "_timer", recovery[stage] - .001f);
                Check(!authoredForm.CanCancelAttackRecovery,
                    $"Authored fire stage {stage + 1} stays locked just before its new recovery gate.");
                Field(authoredForm, "_timer", recovery[stage]);
                Check(authoredForm.CanCancelAttackRecovery,
                    $"Authored fire stage {stage + 1} permits movement at its new recovery gate.");
            }

            void Prepare(BaseForm form, WeaponActionData step, bool finisher = false)
            {
                controller.enabled = false;
                host.transform.SetPositionAndRotation(origin, Quaternion.identity);
                controller.enabled = true;
                target.SetActive(true);
                target.transform.position = origin + Vector3.forward * 5f;
                targetStats.CurrentHp = 100f;
                Physics.SyncTransforms();
                Field(form, "_playerController", player);
                Field(form, "_softTarget", target.transform);
                Field(form, "_softTargetOwner", receiver);
                Field(form, "_softTargetCollider", targetCollider);
                Field(form, "_timer", 0f);
                Field(form, "_finisherAttackActive", finisher);
                // These fixtures exercise normal actions without animation, VFX or damage dispatch.
                var action = typeof(BaseForm).GetField("_currentAction", Hidden);
                action.SetValue(form, Enum.Parse(action.FieldType, "Attack"));
                Invoke(form, "ResetMotion", step);
            }
            void Move(BaseForm form, WeaponActionData step, float time)
            {
                Field(form, "_timer", time);
                Invoke(form, "MoveForward", step);
            }
            void MoveTarget()
            {
                target.transform.position = origin + new Vector3(1f, 0f, 6f);
                Physics.SyncTransforms();
            }

            foreach (bool distanceMode in new[] { false, true })
            {
                string mode = distanceMode ? "distance" : "legacy";
                var step = Step(distanceMode);
                Prepare(fire, step);
                Move(fire, step, .2f);
                Vector3 stopped = host.transform.position;
                Quaternion facing = host.transform.rotation;
                Near(stopped.z - origin.z, .96f, $"Fire {mode} spends only its 0.04-to-0.20 pursuit window.");
                MoveTarget();
                Move(fire, step, .4f);
                Move(fire, step, .7f);
                Near(Vector3.Distance(host.transform.position, stopped), 0f,
                    $"Fire {mode} translation stops at the authored deadline.");
                Near(Quaternion.Angle(host.transform.rotation, facing), 0f,
                    $"Fire {mode} facing stops following a target after the deadline.");

                Prepare(fire, step);
                Move(fire, step, .4f);
                Near(host.transform.position.z - origin.z, .96f,
                    $"Fire {mode} clips a frame that crosses the pursuit deadline.");

                step.HitStartTime = .1f;
                Prepare(fire, step);
                Move(fire, step, .4f);
                Near(host.transform.position.z - origin.z, .36f,
                    $"Fire {mode} also stops at the earlier last-hit start.");
                step.HitStartTime = .6f;

                foreach (BaseForm other in others)
                {
                    Prepare(other, step);
                    Move(other, step, .2f);
                    Vector3 before = host.transform.position;
                    Quaternion beforeFacing = host.transform.rotation;
                    MoveTarget();
                    Move(other, step, .4f);
                    Check(Vector3.Distance(host.transform.position, before) > .5f,
                        $"{other.GetType().Name} {mode} preserves pursuit beyond the authored movement end.");
                    Check(Quaternion.Angle(host.transform.rotation, beforeFacing) > 1f,
                        $"{other.GetType().Name} {mode} preserves its existing target-follow rotation.");
                    Move(other, step, .6f);
                    Vector3 lastHitPosition = host.transform.position;
                    Quaternion lastHitFacing = host.transform.rotation;
                    target.transform.position = origin + new Vector3(-1f, 0f, 7f);
                    Physics.SyncTransforms();
                    Move(other, step, .9f);
                    Near(Vector3.Distance(host.transform.position, lastHitPosition), 0f,
                        $"{other.GetType().Name} {mode} still stops at the last-hit start.");
                    Near(Quaternion.Angle(host.transform.rotation, lastHitFacing), 0f,
                        $"{other.GetType().Name} {mode} keeps its last-hit facing cutoff.");
                }

                Prepare(fire, step, true);
                Move(fire, step, .4f);
                Check(host.transform.position.z - origin.z > 2f,
                    $"Fire {mode} finisher excludes the normal-attack pursuit override.");

                Prepare(fire, step);
                Move(fire, step, .1f);
                Vector3 beforeDeath = host.transform.position;
                targetStats.CurrentHp = 0f;
                Move(fire, step, .2f);
                Near(Vector3.Distance(host.transform.position, beforeDeath), 0f,
                    $"Fire {mode} stops immediately when its target dies.");
            }
            return $"PASS: fire attack motion ({checks} checks: authored deadlines, translation and facing, crossing frames, last-hit limit, four other forms, finisher isolation and target death).";
        }
        finally
        {
            if (host != null) Object.DestroyImmediate(host);
            if (target != null) Object.DestroyImmediate(target);
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static WeaponActionData Step(bool distanceMode) => new WeaponActionData
    {
        Duration = 1f, HitStartTime = .6f, HitDuration = .05f,
        UseTargetApproach = true, FollowMovingTarget = true,
        UseDistanceMovement = distanceMode, MovementStartTime = .04f, MovementEndTime = .2f,
        ApproachEndTime = .2f, RotationEndTime = .04f,
        ForwardDistance = 0f, ThrustMultiplier = 0f,
        TargetFollowSpeed = 6f, TargetFollowRotationSpeed = 720f,
        ApproachAlignmentAngle = 90f, StopDistance = .25f, MaxApproachDistance = 10f
    };

    private static void Field(BaseForm form, string name, object value)
        => typeof(BaseForm).GetField(name, Hidden).SetValue(form, value);
    private static void Invoke(BaseForm form, string name, params object[] arguments)
        => typeof(BaseForm).GetMethod(name, Hidden).Invoke(form, arguments);
}

public sealed class FireAttackMotionRegressionTarget : MonoBehaviour, IDamageable
{
    public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE,
        bool isCritical = false, Vector3 power = default, float poiseDamage = 20f,
        StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
        => new DamageResult(DamageOutcome.Applied);
}
