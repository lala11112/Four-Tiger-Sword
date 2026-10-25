using System;
using System.Collections;
using System.Collections.Generic;
using MoreMountains.Feedbacks;
using MoreMountains.FeedbacksForThirdParty;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks saved authoring and runs bounded, isolated native Feel playback checks.</summary>
public static class WaterHeavyFeelRegression
{
    private const string WaterPath = "Assets/_Project/Scripts/FormActions/WaterFormLab/WaterFormActionSO_V2.asset";
    private const string FormFolder = "Assets/_Project/Scripts/FormActions";
    private static IEnumerator _runtimeChecks;
    private static double _deadline;

    public static bool IsRunning => _runtimeChecks != null;
    public static string LastResult { get; private set; } = "Not run.";

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Water heavy Feel: " + message);
    }

    public static string Run()
    {
        Check(!Application.isPlaying, "Run() requires Edit Mode; use RunPlayMode() while playing.");
        VerifyAssets();
        LastResult = "PASS: only the assigned Water V2 heavy attack opts in; native Feel zoom, impulse and flash targets are connected.";
        return LastResult;
    }

    private static CombatFeelFeedback VerifyAssets()
    {
        var water = AssetDatabase.LoadAssetAtPath<WaterFormActionDataSO>(WaterPath);
        Check(water != null && water.HeavyAttackStep != null, "The assigned Water V2 heavy attack is missing.");
        var profile = water.HeavyAttackStep.CameraProfile;
        Check(profile != null && profile.FeelFeedbackPrefab != null, "The Water V2 heavy camera has no Feel prefab.");
        int optedIn = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:WeaponActionDataSO", new[] { FormFolder }))
        {
            var form = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (WeaponActionData step in Steps(form))
            {
                if (step?.CameraProfile?.FeelFeedbackPrefab == null) continue;
                optedIn++;
                Check(form == water && ReferenceEquals(step, water.HeavyAttackStep),
                    "An action other than Water V2 HeavyAttackStep acquired a Feel preset: " + form.name);
            }
        }
        Check(optedIn == 1, "Expected exactly one opted-in action, found " + optedIn + ".");
        CombatFeelFeedback prefab = profile.FeelFeedbackPrefab;
        Check(PrefabUtility.IsPartOfPrefabAsset(prefab) && prefab.IsConfigured, "The Feel reference must be a configured prefab asset.");
        var zoom = prefab.ZoomFeedbacks.GetFeedbackOfType<MMF_Property>();
        var impulse = prefab.HitFeedbacks.GetFeedbackOfType<MMF_CinemachineImpulseSource>();
        var flash = prefab.HitFeedbacks.GetFeedbackOfType<MMF_CanvasGroup>();
        Check(zoom != null && zoom.Active && zoom.Target != null && zoom.Target.TargetComponent == prefab
            && zoom.Target.TargetPropertyName == nameof(CombatFeelFeedback.ZoomOffset),
            "Native property feedback must animate this prefab's additive ZoomOffset.");
        Check(impulse != null && impulse.Active, "The native Cinemachine impulse feedback is missing or disabled.");
        Check(prefab.OverrideImpulseDirection && prefab.CameraSpaceImpulseDirection.sqrMagnitude > 0.0001f
            && Mathf.Abs(prefab.CameraSpaceImpulseDirection.z) < 0.0001f,
            "The Water heavy preset must direct its impulse along the screen plane.");
        Check(!impulse.ClearImpulseOnStop, "Stopping the preset must preserve unrelated active impulses.");
        Check(flash != null && flash.Active && flash.TargetCanvasGroup == prefab.FlashGroup,
            "Native CanvasGroup feedback must target this prefab's flash.");
        Check(!prefab.ZoomFeedbacks.AutoPlayOnEnable && !prefab.ZoomFeedbacks.AutoPlayOnStart
            && !prefab.HitFeedbacks.AutoPlayOnEnable && !prefab.HitFeedbacks.AutoPlayOnStart,
            "Feedbacks must wait for the attack and actual-hit triggers.");
        Check(Mathf.Abs(prefab.ZoomOffset) < 0.0001f && prefab.FlashGroup.alpha == 0f,
            "The saved prefab must start with a neutral zoom and an invisible flash.");
        return prefab;
    }

    private static IEnumerable<WeaponActionData> Steps(WeaponActionDataSO form)
    {
        if (form == null) yield break;
        if (form.ComboSteps != null) foreach (var step in form.ComboSteps) yield return step;
        if (form.SkillSteps != null) foreach (var step in form.SkillSteps) yield return step;
        if (form.UltimateSteps != null) foreach (var step in form.UltimateSteps) yield return step;
        yield return form.AirAttackStep;
        yield return form.CounterStep;
        if (form is WaterFormActionDataSO water) yield return water.HeavyAttackStep;
    }

    // This entry returns immediately. Poll IsRunning/LastResult or read the final Console message.
    public static string RunPlayMode()
    {
        Check(Application.isPlaying && !EditorApplication.isPaused && Mathf.Approximately(Time.timeScale, 1f),
            "RunPlayMode() requires an unpaused Play Mode simulation at its normal time scale.");
        Check(!IsRunning, "A runtime check is already running.");
        CombatFeelFeedback prefab = VerifyAssets();
        LastResult = "RUNNING: isolated Water heavy Feel playback checks.";
        _deadline = EditorApplication.timeSinceStartup + 12d;
        _runtimeChecks = VerifyRuntime(prefab);
        EditorApplication.update += Advance;
        return LastResult;
    }

    private static void Advance()
    {
        try
        {
            Check(Application.isPlaying && !EditorApplication.isPaused, "Play Mode ended or paused during the check.");
            Check(EditorApplication.timeSinceStartup < _deadline, "Runtime playback exceeded its 12-second deadline.");
            if (_runtimeChecks.MoveNext()) return;
            Finish();
            Debug.Log(LastResult);
        }
        catch (Exception exception)
        {
            LastResult = "FAIL: " + exception.Message;
            Finish();
            Debug.LogError(LastResult + "\n" + exception.StackTrace);
        }
    }

    private static void Finish()
    {
        EditorApplication.update -= Advance;
        IEnumerator routine = _runtimeChecks;
        _runtimeChecks = null;
        (routine as IDisposable)?.Dispose();
    }

    private static IEnumerator VerifyRuntime(CombatFeelFeedback prefab)
    {
        Scene scene = SceneManager.CreateScene("Water heavy Feel regression");
        var created = new List<Object>();
        float timeScale = Time.timeScale;
        CombatFeelFeedback bridge = null;
        try
        {
            bridge = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(bridge.gameObject, scene);
            bridge.gameObject.hideFlags = HideFlags.HideAndDontSave;
            created.Add(bridge.gameObject);
            // Exercise the real alpha animation without drawing a flash over the user's game.
            foreach (Canvas canvas in bridge.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
            // Let first-use scene/shader work settle before measuring a short effect.
            int readyFrame = Time.frameCount + 3;
            while (Time.frameCount < readyFrame) yield return null;
            int hits = 0;
            ObservePlays(bridge.HitFeedbacks, () => hits++);
            bridge.Begin(0.4f, 1f);
            bool zoomed = false;
            float until = Time.time + 0.3f;
            while (Time.time < until)
            {
                zoomed |= Mathf.Abs(bridge.ZoomOffset) > 0.01f;
                Check(hits == 0 && bridge.FlashGroup.alpha == 0f, "A missed attack played its impact flash.");
                yield return null;
            }
            Check(zoomed, "Native Feel property playback never produced a zoom offset.");
            bridge.Stop();
            CheckNeutral(bridge);
            bridge.PlayHit(null, Vector3.forward);
            Check(hits == 0, "A canceled missed attack accepted a late hit callback.");
            yield return null;
            CheckNeutral(bridge);

            bridge.Begin(0.4f, 1f);
            bridge.SetPlaybackSpeed(2f);
            Check(Mathf.Abs(bridge.ZoomFeedbacks.GetFeedbackOfType<MMF_Property>().FeedbackDuration - 0.4f) < 0.0001f,
                "Attack speed was applied both to the property duration and its playback clock.");
            bridge.PlayHit(null, Vector3.forward);
            bridge.PlayHit(null, Vector3.right);
            Check(hits == 1, "Two contacts in one attack played the impact sequence more than once.");
            bool flashed = false;
            until = Time.time + 0.5f;
            while (Time.time < until)
            {
                flashed |= bridge.FlashGroup.alpha > 0.001f;
                yield return null;
            }
            Check(flashed, "The actual-hit trigger never animated the native flash.");
            bridge.PlayHit(null, Vector3.back);
            Check(hits == 1, "A later contact replayed the impact after the sequence completed.");
            Check(Mathf.Abs(bridge.ZoomOffset) < 0.01f, "The sped-up native zoom did not return to neutral.");
            bridge.Begin(0.4f, 1f);
            bridge.PlayHit(null, Vector3.forward);
            Check(hits == 2, "The next chained attack did not re-arm its actual-hit feedback.");
            bridge.Stop();
            CheckNeutral(bridge);
            bridge.Begin(0.4f, 1f);
            bridge.PlayHit(null, Vector3.forward);
            bridge.gameObject.SetActive(false);
            CheckNeutral(bridge);
            Check(Time.timeScale == timeScale, "Feel playback changed the existing hitstop's time scale.");

            VerifyDirector(prefab, scene, created);
            IEnumerator impulses = VerifyImpulsePlayback(prefab, scene, created);
            while (impulses.MoveNext()) yield return impulses.Current;
            LastResult = "PASS: native zoom and flash / actual Feel impulse emission and visible listener correction for forward and side attacks in both listener spaces / whiff and zero-strength silence / duplicate suppression / direction opt-out and legacy fallback / chain, speed and cleanup / unchanged time scale, authored lens and source settings.";
        }
        finally
        {
            if (bridge != null) bridge.Stop();
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }
    }

    private static void VerifyDirector(CombatFeelFeedback prefab, Scene scene, List<Object> created)
    {
        GameObject ownerObject = Make("Inactive Feel camera owner", scene, created, false);
        var owner = ownerObject.AddComponent<PlayerController>();
        GameObject follow = Make("Feel regression follow", scene, created, true);
        GameObject look = Make("Feel regression look", scene, created, true);
        GameObject cameraObject = Make("Feel regression camera", scene, created, false);
        var camera = cameraObject.AddComponent<CinemachineCamera>();
        camera.Priority = -1000000;
        camera.Follow = follow.transform;
        camera.LookAt = look.transform;
        var lens = camera.Lens;
        lens.FieldOfView = 53f;
        camera.Lens = lens;
        var director = cameraObject.AddComponent<CombatCameraDirector>();
        cameraObject.SetActive(true);
        var profile = ScriptableObject.CreateInstance<AttackCameraProfile>();
        created.Add(profile);
        profile.FeelFeedbackPrefab = prefab;
        Check(director.BeginAttack(profile, owner, null, Vector3.forward, false, 0.4f, 1f),
            "The director rejected an otherwise valid isolated camera.");
        CombatFeelFeedback runtime = director.GetComponentInChildren<CombatFeelFeedback>();
        Check(runtime != null && runtime != prefab, "The director did not create its own runtime preset.");
        foreach (Canvas canvas in runtime.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
        int hits = 0;
        ObservePlays(runtime.HitFeedbacks, () => hits++);
        Check(director.NotifyHit(owner, null, Vector3.forward) && director.NotifyHit(owner, null, Vector3.right)
            && hits == 1, "The director did not suppress duplicate hit feedback.");
        director.Cancel(owner, true);
        Check(!director.IsPlaying && camera.Follow == follow.transform && camera.LookAt == look.transform,
            "Immediate cancellation did not restore the original camera targets.");
        CheckNeutral(runtime);
        Check(Mathf.Abs(camera.Lens.FieldOfView - 53f) < 0.0001f,
            "Feel playback overwrote the authored Cinemachine lens.");
        profile.FeelFeedbackPrefab = null;
        Check(director.BeginAttack(profile, owner, null, Vector3.forward),
            "An ordinary camera profile no longer works without Feel.");
        director.Cancel(owner, true);
        Check(camera.Follow == follow.transform && camera.LookAt == look.transform,
            "The legacy fallback did not restore camera targets.");
    }

    private sealed class ImpulseSample
    {
        public float RawPeak, WorldPeak, ScreenPeak;
        public Vector3 StrongestCorrection;
    }

    private static IEnumerator VerifyImpulsePlayback(CombatFeelFeedback prefab, Scene scene, List<Object> created)
    {
        var ownerObject = Make("Inactive real impulse owner", scene, created, false);
        var owner = ownerObject.AddComponent<PlayerController>();
        var source = Make("Isolated real Feel impulse source", scene, created, true)
            .AddComponent<CinemachineImpulseSource>();
        // Match the current player source's effective settings. Legacy noise/gain fields
        // are intentionally irrelevant to a Uniform impulse. Test channels never reach live listeners.
        source.ImpulseDefinition = new CinemachineImpulseDefinition
        {
            ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform,
            ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Explosion,
            ImpulseDuration = 0.3f,
            DissipationDistance = 100f
        };
        source.DefaultVelocity = Vector3.down;
        owner.ImpulseSource = source;
        var follow = Make("Isolated impulse follow", scene, created, true);
        var look = Make("Isolated impulse look", scene, created, true);
        var cameraObject = Make("Isolated impulse listener", scene, created, false);
        var camera = cameraObject.AddComponent<CinemachineCamera>();
        camera.Priority = -1000000;
        camera.Follow = follow.transform;
        camera.LookAt = look.transform;
        var listener = cameraObject.AddComponent<CinemachineImpulseListener>();
        listener.Gain = 1f;
        listener.ApplyAfter = CinemachineCore.Stage.Noise;
        var director = cameraObject.AddComponent<CombatCameraDirector>();
        cameraObject.SetActive(true);
        owner.CameraTransform = cameraObject.transform;
        var profile = ScriptableObject.CreateInstance<AttackCameraProfile>();
        created.Add(profile);
        profile.FeelFeedbackPrefab = prefab;
        const float strength = 0.336f;
        CombatFeelFeedback runtime = null;
        int sequences = 0;

        // Separate channel bits prevent one case's still-decaying pulse from passing the next case.
        for (int test = 0; test < 8; test++)
        {
            int channel = 1 << (test < 7 ? 24 + test : 23);
            source.ImpulseDefinition.ImpulseChannel = listener.ChannelMask = channel;
            listener.UseCameraSpace = test >= 4 && test < 6;
            cameraObject.transform.rotation = listener.UseCameraSpace
                ? Quaternion.Euler(35f, 125f, 0f) : Quaternion.Euler(15f, 20f, 0f);
            camera.InternalUpdateCameraState(Vector3.up, 1f / 60f);
            Quaternion view = camera.State.GetFinalOrientation();
            Vector3 attack = (test == 3 || test == 5) ? view * Vector3.right
                : Vector3.ProjectOnPlane(view * Vector3.forward, Vector3.up).normalized;
            profile.HitImpulse = test == 1 ? 0f : strength;
            profile.FeelFeedbackPrefab = test == 7 ? null : prefab;
            Check(director.BeginAttack(profile, owner, null, attack, false, 0.4f, 1f),
                "The isolated impulse camera could not begin case " + test + ".");
            if (runtime == null)
            {
                runtime = director.GetComponentInChildren<CombatFeelFeedback>();
                Check(runtime != null, "No runtime Feel preset was created for the real impulse test.");
                foreach (Canvas canvas in runtime.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
                ObservePlays(runtime.HitFeedbacks, () => sequences++);
            }
            runtime.OverrideImpulseDirection = test != 6;
            var native = runtime.HitFeedbacks.GetFeedbackOfType<MMF_CinemachineImpulseSource>();
            int playsBefore = native.Timing.PlayCount;
            int sequencesBefore = sequences;
            if (test != 0)
            {
                Check(director.NotifyHit(owner, null, attack), "The real impulse hit was not handled.");
                Check(director.NotifyHit(owner, null, -attack), "A duplicate real impulse hit was not suppressed.");
                if (test != 7)
                    Check(sequences == sequencesBefore + 1, "One hit generated multiple native sequences.");
            }
            if (test >= 2 && test <= 6)
            {
                Check(native.Active && native.ImpulseSource == source && native.Timing.PlayCount == playsBefore + 1,
                    "The actual hit did not execute the native Feel impulse feedback.");
                Vector3 worldVelocity = listener.UseCameraSpace ? view * native.Velocity : native.Velocity;
                Vector3 expected = test == 6 ? attack : view * prefab.CameraSpaceImpulseDirection.normalized;
                Check(Mathf.Abs(worldVelocity.magnitude - strength) < 0.0001f
                    && Vector3.Dot(worldVelocity.normalized, expected.normalized) > 0.999f,
                    "Impulse direction was transformed twice, lost its strength, or changed the opted-out legacy direction.");
            }
            // The inactive owner is intentionally never awakened. Cancel synchronously before
            // LateUpdate, then exercise the real listener: Stop must not erase its emitted pulse.
            director.Cancel(owner, true);
            var sample = new ImpulseSample();
            IEnumerator sampling = SampleImpulse(camera, channel, sample, test < 2 ? 0.05f : 0.34f);
            while (sampling.MoveNext()) yield return sampling.Current;
            if (test < 2)
            {
                Check(sample.RawPeak < 0.0001f && sample.WorldPeak < 0.0001f,
                    "A missed or zero-strength attack emitted a camera impulse.");
                if (test == 0) Check(sequences == sequencesBefore, "A whiff played its native hit sequence.");
            }
            else
            {
                Check(sample.RawPeak > 0.02f,
                    "Native playback produced no Cinemachine impulse signal (case " + test + ").");
                Check(sample.WorldPeak > 0.02f,
                    "The emitted impulse never reached the camera listener (case " + test + ").");
                if (test <= 5)
                    Check(sample.ScreenPeak > 0.02f && sample.ScreenPeak >= sample.WorldPeak * 0.85f,
                        "The hit pulse moved mainly in camera depth instead of visibly across the screen (case " + test + ").");
                else
                {
                    Check(Mathf.Abs(Vector3.Dot(sample.StrongestCorrection.normalized, attack.normalized)) > 0.999f,
                        "Opting out of Feel direction or using the legacy profile changed its attack-direction impulse.");
                    if (test == 6)
                        Check(sample.ScreenPeak < sample.WorldPeak * 0.5f,
                            "The opted-out comparison no longer reproduces the original camera-depth pulse.");
                }
            }
            Check(source.DefaultVelocity == Vector3.down && source.ImpulseDefinition.ImpulseType
                == CinemachineImpulseDefinition.ImpulseTypes.Uniform && source.ImpulseDefinition.ImpulseDuration == 0.3f,
                "Per-hit playback changed the shared source's authored settings.");
        }
    }

    private static IEnumerator SampleImpulse(CinemachineCamera camera, int channel, ImpulseSample sample, float duration)
    {
        float until = Time.time + duration;
        int previousFrame = Time.frameCount;
        do
        {
            if (Time.frameCount != previousFrame)
            {
                previousFrame = Time.frameCount;
                // Force the pipeline instead of relying on whether a live Brain updates this low-priority camera.
                camera.InternalUpdateCameraState(Vector3.up, Time.deltaTime);
                CinemachineImpulseManager.Instance.GetImpulseAt(camera.State.GetFinalPosition(), false, channel,
                    out Vector3 raw, out Quaternion ignoredRotation);
                sample.RawPeak = Mathf.Max(sample.RawPeak, raw.magnitude);
                Vector3 correction = camera.State.PositionCorrection;
                if (correction.magnitude > sample.WorldPeak)
                {
                    sample.WorldPeak = correction.magnitude;
                    sample.StrongestCorrection = correction;
                }
                Quaternion view = camera.State.GetFinalOrientation();
                float screen = new Vector2(Vector3.Dot(correction, view * Vector3.right),
                    Vector3.Dot(correction, view * Vector3.up)).magnitude;
                sample.ScreenPeak = Mathf.Max(sample.ScreenPeak, screen);
            }
            yield return null;
        }
        while (Time.time < until);
    }

    private static GameObject Make(string name, Scene scene, List<Object> created, bool active)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        go.SetActive(false);
        SceneManager.MoveGameObjectToScene(go, scene);
        created.Add(go);
        go.SetActive(active);
        return go;
    }

    private static void ObservePlays(MMF_Player player, UnityAction action)
    {
        if (player.Events.OnPlay == null) player.Events.OnPlay = new UnityEvent();
        player.Events.OnPlay.AddListener(action);
        player.Events.TriggerUnityEvents = true;
        player.Events.Initialization();
    }

    private static void CheckNeutral(CombatFeelFeedback bridge)
        => Check(Mathf.Abs(bridge.ZoomOffset) < 0.0001f && bridge.FlashGroup.alpha == 0f
            && !bridge.ZoomFeedbacks.IsPlaying && !bridge.HitFeedbacks.IsPlaying,
            "Stop or disable left a zoom, flash or native feedback coroutine active.");
}
