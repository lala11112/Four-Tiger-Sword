// Unity Pipeline run_script, entry CombatCameraRegression.Run. Isolated Edit Mode scene.
using System;
using System.Reflection;
using UnityEngine;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class CombatCameraRegression
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static string Run()
    {
        if (Application.isPlaying) throw new Exception("Edit Mode required");
        int checks = 0;
        Action<bool, string> check = (ok, text) => { if (!ok) throw new Exception(text); checks++; };
        var scene = EditorSceneManager.NewPreviewScene();
        AttackCameraProfile profile = null;
        WaterFormActionDataSO waterCopy = null;
        try
        {
            var playerGo = new GameObject("Camera regression player");
            SceneManager.MoveGameObjectToScene(playerGo, scene);
            var player = playerGo.AddComponent<PlayerController>();
            var stats = playerGo.GetComponent<PlayerStatManager>();
            typeof(PlayerController).GetProperty("StatManager").SetValue(player, stats);
            typeof(PlayerStatManager).GetField("_currentHp", Hidden).SetValue(stats, 100f);
            var focus = new GameObject("Focus").transform;
            focus.SetParent(playerGo.transform, false);
            focus.localPosition = Vector3.up;
            var view = new GameObject("View").transform;
            SceneManager.MoveGameObjectToScene(view.gameObject, scene);
            view.rotation = Quaternion.Euler(15, 40, 0);
            player.CameraTransform = view;
            var cameraGo = new GameObject("Test camera");
            SceneManager.MoveGameObjectToScene(cameraGo, scene);
            var camera = cameraGo.AddComponent<CinemachineCamera>();
            camera.Follow = focus;
            camera.Target.CustomLookAtTarget = false;
            var director = cameraGo.AddComponent<CombatCameraDirector>();
            typeof(PlayerController).GetField("_combatCamera", Hidden).SetValue(player, director);
            var enemy = new GameObject("Target").transform;
            SceneManager.MoveGameObjectToScene(enemy.gameObject, scene);
            enemy.position = Vector3.right * 4;
            profile = ScriptableObject.CreateInstance<AttackCameraProfile>();
            profile.HitImpulse = 0;
            profile.ReturnDuration = 0.3f;
            profile.CancelDuration = 0.1f;
            profile.FovOffset = AnimationCurve.Constant(0, 1, 5);
            string immutableProfile = JsonUtility.ToJson(profile);
            Action<float> tick = dt => typeof(CombatCameraDirector).GetMethod("Tick", Hidden).Invoke(director, new object[] { dt });
            Func<CameraState> state = () => {
                var s = CameraState.Default;
                s.Lens = camera.Lens;
                director.PrePipelineMutateCameraStateCallback(camera, ref s, 0.02f);
                return s;
            };
            check(!director.BeginAttack(null, player, null, Vector3.forward), "Null profile must be a no-op");
            check(camera.Follow == focus, "Null profile changed targets");
            check(director.BeginAttack(profile, player, enemy, Vector3.forward), "Start failed");
            var proxy = camera.Follow;
            director.SetProgress(player, 0.5f);
            tick(0.2f);
            check(director.Progress == 0.5f && director.IsPlaying, "Explicit attack progress");
            check(camera.Follow != focus && camera.LookAt != focus, "Missing runtime targets");
            check(camera.LookAt.position.x > 0.5f, "Target framing absent");
            float authoredFov = camera.Lens.FieldOfView;
            check(state().Lens.FieldOfView > authoredFov + 4, "FOV offset absent");
            check(camera.Lens.FieldOfView == authoredFov, "Authored FOV mutated");
            check(Mathf.Approximately(state().Lens.FieldOfView, state().Lens.FieldOfView), "FOV accumulates across evaluations");
            var lens = camera.Lens; lens.FieldOfView = 70; camera.Lens = lens;
            check(state().Lens.FieldOfView > 74, "Base FOV changes must compose with attack FOV");
            var beforePause = camera.Follow.position;
            float pausedFov = state().Lens.FieldOfView;
            director.SetProgress(player, 0.7f); tick(0);
            check(camera.Follow.position == beforePause && state().Lens.FieldOfView == pausedFov, "Zero delta moved framing");
            check(director.NotifyHit(player, enemy, Vector3.right), "Hit not handled");
            check(director.NotifyHit(player, null, Vector3.left), "Duplicate hit not suppressed");
            check((Transform)typeof(CombatCameraDirector).GetField("_target", Hidden).GetValue(director) == enemy,
                "Duplicate hit changed focus");
            var originalRotation = proxy.rotation;
            focus.rotation = Quaternion.Euler(0, 180, 0);
            view.rotation = Quaternion.Euler(15, 120, 0);
            director.BeginAttack(profile, player, enemy, Vector3.back, true); tick(0.05f);
            check(camera.Follow == proxy && Quaternion.Angle(proxy.rotation, originalRotation) < 0.01f,
                "Chain recreated targets or flipped view basis");
            check(director.Progress == 0 && !(bool)typeof(CombatCameraDirector).GetField("_hitPlayed", Hidden).GetValue(director),
                "Chain did not reset per-attack state");
            UnityEngine.Object.DestroyImmediate(enemy.gameObject); tick(0.1f);
            check(director.IsPlaying, "Destroyed target broke playback");
            director.EndAttack(player); tick(0.15f);
            check(camera.Follow == proxy && !director.IsPlaying, "Return did not blend");
            tick(0.2f);
            check(camera.Follow == focus && !camera.Target.CustomLookAtTarget, "Original target settings not restored");
            check(state().Lens.FieldOfView == 70, "FOV did not return to current base");
            director.BeginAttack(profile, player, null, Vector3.right); tick(0.1f);
            director.Cancel(player); tick(0.11f);
            check(camera.Follow == focus, "Cancellation did not finish at cancel duration");
            director.BeginAttack(profile, player, null, Vector3.right); tick(0.1f);
            director.Cancel(player, true);
            check(camera.Follow == focus, "Immediate cancellation failed");
            director.BeginAttack(profile, player, null, Vector3.right); tick(0.1f);
            director.enabled = false;
            check(camera.Follow == focus, "Disable leaked targets");
            director.enabled = true;
            director.BeginAttack(profile, player, null, Vector3.right); tick(0.1f);
            camera.Follow = view; tick(0.1f);
            check(camera.Follow == view && camera.LookAt != proxy && !director.IsPlaying, "External ownership overwritten");
            camera.Follow = focus;
            director.BeginAttack(profile, player, null, Vector3.right);
            typeof(PlayerStatManager).GetField("_currentHp", Hidden).SetValue(stats, 0f); tick(0.1f);
            check(camera.Follow == focus && !director.IsPlaying, "Death leaked framing");
            check(JsonUtility.ToJson(profile) == immutableProfile, "Shared profile was mutated by playback");
            var water = AssetDatabase.LoadAssetAtPath<WaterFormActionDataSO>("Assets/_Project/Scripts/FormActions/WaterFromAction.asset");
            check(water.HeavyAttackStep.CameraProfile != null, "Water profile not wired");
            // Exercise the real WaterForm path, including progress and same-state follow-ups.
            typeof(PlayerStatManager).GetField("_currentHp", Hidden).SetValue(stats, 100f);
            var values = (System.Collections.Generic.Dictionary<StatType, float>)
                typeof(PlayerStatManager).GetField("finalValues", Hidden).GetValue(stats);
            values[StatType.ST_ATK_SPD] = 2f;
            playerGo.transform.position = Vector3.one * 50000;
            view.position = playerGo.transform.position;
            var animator = playerGo.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/_Project/Animations/AnimController/BDJ.controller");
            animator.Rebind(); animator.Update(0);
            typeof(PlayerController).GetProperty("Animator").SetValue(player, animator);
            typeof(PlayerController).GetProperty("Input").SetValue(player, playerGo.GetComponent<PlayerInputHandler>());
            typeof(PlayerController).GetProperty("Controller").SetValue(player, playerGo.GetComponent<CharacterController>());
            waterCopy = UnityEngine.Object.Instantiate(water);
            waterCopy.HeavyAttackStep.SlashVFX = null;
            waterCopy.HeavyAttackStep.SwingSound = null;
            var waterForm = new WaterForm(waterCopy);
            typeof(BaseForm).GetField("_playerController", Hidden).SetValue(waterForm, player);
            ((WaterFlowGauge)typeof(WaterForm).GetField("_flow", Hidden).GetValue(waterForm)).Charge(300);
            check(waterForm.TryBeginHeavyAttack() && director.IsPlaying, "Water start did not begin camera");
            tick(0.1f);
            proxy = camera.Follow;
            typeof(BaseForm).GetField("_timer", Hidden).SetValue(waterForm, 0.1f);
            waterForm.UpdateHeavyAttack(out _);
            float attackTime = (float)typeof(BaseForm).GetField("_timer", Hidden).GetValue(waterForm);
            check(Mathf.Approximately(director.Progress, Mathf.Clamp01(attackTime / waterCopy.HeavyAttackStep.Duration)),
                "Water camera clock diverged from attack-speed-scaled clock");
            typeof(WaterForm).GetField("_heavyAttackFinished", Hidden).SetValue(waterForm, true);
            check(waterForm.TryContinueHeavyAttack() && camera.Follow == proxy && director.Progress == 0,
                "Water follow-up did not retain camera session");
            waterForm.EndHeavyAttack(); tick(0.2f);
            check(camera.Follow == focus && !director.IsPlaying, "Water interruption leaked camera");
            return checks + " camera checks passed: FOV composition, hit-stop, hit deduplication, chain continuity, target loss, return, interruption, disable, external ownership, death, immutable profile, asset wiring and real WaterForm lifecycle.";
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
            if (waterCopy != null) UnityEngine.Object.DestroyImmediate(waterCopy);
        }
    }
}
