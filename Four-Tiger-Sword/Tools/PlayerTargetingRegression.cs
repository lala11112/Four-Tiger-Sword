// Unity Pipeline run_script: PlayerTargetingRegression.Run (Edit Mode).
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public class TargetingRegressionReceiver : MonoBehaviour, IDamageable
{
    public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE,
        bool isCritical = false, Vector3 power = default, float poiseDamage = 20f,
        StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
        => new DamageResult(DamageOutcome.Applied);
}

public static class PlayerTargetingRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Probe : BaseForm
    {
        public Probe(WeaponActionDataSO data) : base(data) { }
        public Transform Target => _softTarget;
        public float HitAngle = -1f;
        public void Select(bool keep = false) => FindSoftTarget(keep);
        public void Rotate(float time) => RotateTowardSoftTarget(time);
        public void Prepare(WeaponActionData step)
            => typeof(BaseForm).GetMethod("ResetTargetApproach", Hidden).Invoke(this, new object[] { step, false });
        public void MoveAt(WeaponActionData step, float time)
        {
            _timer = time;
            MoveForward(step);
        }
        public void AttackAt(float time) { _timer = time; UpdateAttack(out _); }
        protected override void ExecuteHit(WeaponActionData step, float damage, float poise, HashSet<IDamageable> hits)
            => HitAngle = Vector3.Angle(_playerController.transform.forward,
                Target.position - _playerController.transform.position);
    }

    public static string Run()
    {
        if (Application.isPlaying) throw new Exception("Run in Edit Mode.");
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var objects = new List<GameObject>();
        WeaponActionDataSO data = null;
        int checks = 0;
        Action<bool, string> check = (ok, message) => { if (!ok) throw new Exception(message); checks++; };
        Func<string, Vector3, GameObject> make = (name, pos) =>
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = pos;
            objects.Add(go);
            return go;
        };
        Vector3 origin = new Vector3(20000, 20000, 20000);
        try
        {
            var playerGo = make("Player", origin);
            var player = playerGo.AddComponent<PlayerController>();
            var controller = playerGo.GetComponent<CharacterController>();
            typeof(PlayerController).GetProperty("Controller").SetValue(player, controller);
            typeof(PlayerController).GetProperty("StatManager").SetValue(player, playerGo.GetComponent<PlayerStatManager>());
            data = ScriptableObject.CreateInstance<EarthFormActionDataSO>();
            var step = new WeaponActionData { Duration = 1f, UseTargetApproach = true,
                RotationEndTime = 0.1f, ApproachEndTime = 0.1f, HitStartTime = 0.1f, HitDuration = 0.2f };
            data.ComboSteps = new List<WeaponActionData> { step };
            var probe = new Probe(data);
            probe.Equip(player);
            Func<string, Vector3, GameObject> enemy = (name, offset) =>
            {
                var go = make(name, origin + offset);
                go.layer = LayerMask.NameToLayer("Enemy");
                go.AddComponent<TargetingRegressionReceiver>();
                go.AddComponent<EnemyStat>().CurrentHp = 100f;
                go.AddComponent<BoxCollider>();
                return go;
            };
            var front = enemy("Front", Vector3.forward * 3f);
            var rear = enemy("Rear", Vector3.back);
            Physics.SyncTransforms(); probe.Select();
            check(probe.Target == rear.transform, "Closest rear target was excluded.");
            probe.Prepare(step);
            check(probe.Target == rear.transform, "Approach attack excluded the rear target.");
            rear.SetActive(false); probe.Select();
            var side = enemy("Side", new Vector3(1f, 0f, 1f));
            Physics.SyncTransforms(); probe.Select(true);
            check(probe.Target == front.transform, "Combo changed to a newly closer enemy.");
            front.GetComponent<EnemyStat>().CurrentHp = 0f;
            probe.Select(true);
            check(probe.Target == side.transform, "Dead target was retained.");
            side.SetActive(false); probe.Rotate(0.1f);
            check(probe.Target == null, "Inactive target survived rotation validation.");
            side.SetActive(true); front.GetComponent<EnemyStat>().CurrentHp = 100;
            side.transform.position = origin + new Vector3(0, 3, 1);
            Physics.SyncTransforms(); probe.Select();
            check(probe.Target == front.transform, "Target on another floor was selected.");
            var wall = make("Wall", origin + Vector3.forward * 1.5f);
            wall.AddComponent<BoxCollider>().size = new Vector3(4, 4, 0.2f);
            Physics.SyncTransforms(); probe.Select();
            check(probe.Target == null, "Target behind a wall was selected.");
            wall.SetActive(false); probe.Select();
            front.GetComponent<BoxCollider>().enabled = false; probe.Rotate(0.1f);
            check(probe.Target == null, "Disabled collider survived validation.");
            front.GetComponent<BoxCollider>().enabled = true;
            front.transform.position = origin + Quaternion.Euler(0, 70, 0) * Vector3.forward * 3;
            Physics.SyncTransforms(); probe.Prepare(step);
            check(probe.Target == front.transform, "Rotation budget incorrectly restricted target selection.");
            front.transform.position = origin + Quaternion.Euler(0, 50, 0) * Vector3.forward * 3;
            Physics.SyncTransforms(); probe.Prepare(step); probe.MoveAt(step, 0.12f);
            check(Vector3.Angle(playerGo.transform.forward, front.transform.position - origin) < 0.1f,
                "Frame crossing rotation cutoff lost remaining rotation.");
            playerGo.transform.rotation = Quaternion.identity;
            probe.Prepare(step); probe.AttackAt(0.12f);
            check(probe.HitAngle >= 0f && probe.HitAngle < 0.1f, "Hit query preceded rotation.");
            front.GetComponent<EnemyStat>().CurrentHp = 0f; probe.Rotate(0.1f);
            check(probe.Target == null, "Dead target survived rotation validation.");
            front.GetComponent<EnemyStat>().CurrentHp = 100f; probe.Select();
            front.transform.position = origin + Vector3.forward * 12;
            Physics.SyncTransforms(); probe.Rotate(0.1f);
            check(probe.Target == null, "Out-of-range target survived rotation validation.");
            // Exercise the real CharacterController path with different frame partitions and speeds.
            front.SetActive(false); rear.SetActive(false); side.SetActive(false);
            controller.minMoveDistance = 0f;
            step.UseDistanceMovement = true;
            step.ForwardDistance = 2f;
            step.MovementStartTime = 0.08f;
            step.MovementEndTime = 0.3f;
            step.Duration = 0.5f;
            step.RotationEndTime = 0.08f;
            step.MaxApproachDistance = 2f;
            step.MovementProgress = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            foreach (int fps in new[] { 15, 30, 60, 144 })
            foreach (float speed in new[] { 0.5f, 1f, 2f })
            {
                playerGo.transform.SetPositionAndRotation(origin, Quaternion.identity);
                Physics.SyncTransforms(); probe.Prepare(step);
                float time = 0f;
                while (time < step.Duration) { time += speed / fps; probe.MoveAt(step, time); }
                check(Mathf.Abs(playerGo.transform.position.z - origin.z - 2f) < 0.04f,
                    "Distance changed at " + fps + " FPS / speed " + speed);
                Vector3 end = playerGo.transform.position;
                probe.MoveAt(step, time + 1f);
                check(Vector3.Distance(end, playerGo.transform.position) < 0.001f, "Movement continued after end.");
            }
            step.MaxApproachDistance = 1f;
            playerGo.transform.SetPositionAndRotation(origin, Quaternion.identity);
            Physics.SyncTransforms(); probe.Prepare(step); probe.MoveAt(step, 0.5f);
            check(Mathf.Abs(playerGo.transform.position.z - origin.z - 1f) < 0.02f, "Empty swing exceeded maximum distance.");
            step.MaxApproachDistance = 2f;
            step.UntargetedDistanceMultiplier = 0f;
            playerGo.transform.position = origin;
            Physics.SyncTransforms(); probe.Prepare(step); probe.MoveAt(step, 0.5f);
            check(Vector3.Distance(playerGo.transform.position, origin) < 0.001f, "Zero empty-swing multiplier moved.");
            step.UntargetedDistanceMultiplier = 1f;

            front.SetActive(true);
            front.transform.position = origin + Vector3.back * 5f;
            playerGo.transform.SetPositionAndRotation(origin, Quaternion.identity);
            Physics.SyncTransforms(); probe.Prepare(step);
            probe.MoveAt(step, 0.04f);
            check(Vector3.Distance(playerGo.transform.position, origin) < 0.001f, "Moved during facing phase.");
            probe.MoveAt(step, 0.08f);
            check(Vector3.Angle(playerGo.transform.forward, Vector3.back) < 0.1f, "180-degree turn missed movement start.");
            probe.MoveAt(step, 0.5f);
            check(Mathf.Abs(playerGo.transform.position.z - origin.z + 2f) < 0.03f, "Rear approach failed.");

            front.transform.position = origin + Vector3.forward * 2.5f;
            controller.radius = 0.5f;
            playerGo.transform.SetPositionAndRotation(origin, Quaternion.identity);
            Physics.SyncTransforms(); probe.Prepare(step); probe.MoveAt(step, 0.5f);
            check(Mathf.Abs(playerGo.transform.position.z - origin.z - 1.25f) < 0.04f, "Target stop gap not respected.");

            playerGo.transform.position = origin;
            Physics.SyncTransforms(); probe.Prepare(step); probe.MoveAt(step, 0.12f);
            Vector3 deathPosition = playerGo.transform.position;
            front.GetComponent<EnemyStat>().CurrentHp = 0f;
            probe.MoveAt(step, 0.3f);
            check(Vector3.Distance(deathPosition, playerGo.transform.position) < 0.001f, "Lost target caused forward lunge.");
            front.SetActive(false);
            wall.SetActive(true);
            wall.transform.position = origin + Vector3.forward;
            playerGo.transform.position = origin;
            Physics.SyncTransforms(); probe.Prepare(step); probe.MoveAt(step, 0.5f);
            check(playerGo.transform.position.z - origin.z < 0.6f, "Controller crossed wall.");
            Vector3 blockedPosition = playerGo.transform.position;
            wall.SetActive(false); Physics.SyncTransforms(); probe.MoveAt(step, 0.8f);
            check(Vector3.Distance(blockedPosition, playerGo.transform.position) < 0.001f, "Blocked movement accumulated catch-up motion.");

            var profile = new AttackMotionProfile();
            step.UseDistanceMovement = false;
            step.ThrustCurve = AnimationCurve.Constant(0f, 1f, 1f);
            step.ThrustMultiplier = 10f;
            step.ApproachEndTime = 0.2f;
            profile.Reset(step, true);
            check(Mathf.Abs(profile.TotalDistance - 2f) < 0.001f, "Legacy speed curve integration changed distance.");
            check(Mathf.Abs(profile.Delta(0f, 100f) - 2f) < 0.001f, "Large frame overshot legacy movement.");
            step.Duration = 0f; profile.Reset(step, true);
            check(profile.Delta(0f, 1f) == 0f, "Zero duration produced motion.");
            return checks + " targeting / attack movement checks passed.";
        }
        finally
        {
            foreach (var go in objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            if (data != null) UnityEngine.Object.DestroyImmediate(data);
            SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
