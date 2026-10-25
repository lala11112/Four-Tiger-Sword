// Explicit live verification helper. Enter Play Mode first; this bypasses the runner's saved-scene requirement.
// It never saves a scene or edits a source SO. Exit Play Mode after reading Status() to discard runtime state.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StudioBaekho.CombatLab;
using StudioBaekho.CombatLab.FourTigerSword;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class FourTigerFeedbackPlaybackRegression
{
    private const string DefaultSource = "4b73531aafb753149bfae2eb4ee665f6";
    private const string EarlyRequestKey = "CombatLab.FeedbackPlayback.EarlyRequest";
    [Serializable] private sealed class State
    {
        public string status = "idle", directory, error, sourceGuid, actionId;
        public int hits, confirmedImpacts, capturedFrames;
        public float damage, elapsedSeconds;
        public string precisionStatus;
        public string validationStatus;
        public int localFrozenFrames;
        public bool localResumeObserved;
        public List<string> cleanupErrors = new List<string>();
    }
    private static State _state = new State();
    private static ICombatLabSession _session;
    private static CombatLabRecording _recording;
    private static ExperimentRequest _request;
    private static CombatCandidate _candidate;
    private static ExperimentReport _report;
    private static WeaponActionDataSO _source;
    private static string _sourceBefore;
    private static readonly Dictionary<Enemy, bool> PassiveBefore = new Dictionary<Enemy, bool>();
    private static double _lastPoll;
    private static int _lastFrame = -1, _observedEvents;
    private static float _videoOffset;
    private sealed class Pending
    {
        public string source, action, json, exportedPath;
        public bool control;
    }
    [Serializable] private sealed class SuiteState
    {
        public string status = "idle", directory, error;
        public int total, finished, passed, incomplete, failed;
        public State current;
        public List<State> cases = new List<State>();
    }
    private static Pending _pending;
    private static double _waitStarted;
    private static bool _suiteRunning, _suiteInitialized;
    private static SuiteState _suite = new SuiteState();
    private static readonly List<Pending> SuiteQueue = new List<Pending>();
    private static readonly Dictionary<Enemy, bool> SuitePassiveBefore = new Dictionary<Enemy, bool>();
    private static Vector3 _suitePlayerPosition;
    private static Quaternion _suitePlayerRotation;
    private static ExperimentRequest _readinessRequest;
    private static bool _scenarioPrepared;
    private static string _earlyPreparationError, _scenarioPreparationNote;
    private static int _runtimeSeed = 1701;

    static FourTigerFeedbackPlaybackRegression()
    {
        EditorApplication.update += Poll;
        SceneManager.sceneLoaded += (scene, mode) => {
            _scenarioPrepared = false;
            string json = SessionState.GetString(EarlyRequestKey, "");
            if (!Application.isPlaying || string.IsNullOrEmpty(json)) return;
            SessionState.EraseString(EarlyRequestKey);
            try {
                var request = JsonUtility.FromJson<ExperimentRequest>(json); _runtimeSeed = request.seed;
                new FourTigerCombatAdapter().PrepareBeforeSceneStart(request);
            }
            catch (Exception error) { _earlyPreparationError = error.ToString(); Debug.LogException(error); }
        };
        EditorApplication.playModeStateChanged += change => {
            if (change == PlayModeStateChange.ExitingPlayMode && (_session != null || _pending != null || _suiteRunning))
                Abort("Play Mode ended before verification completed.");
        };
        AssemblyReloadEvents.beforeAssemblyReload += () => {
            if (_session != null || _pending != null || _suiteRunning) Abort("Assembly reload interrupted live verification.");
        };
    }

    public static string PreparePlay(int seed = 1701)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Call PreparePlay in Edit Mode before entering Play Mode.");
        SessionState.SetString(EarlyRequestKey, JsonUtility.ToJson(ReadinessRequest(DefaultSource, "normal-1", seed)));
        _earlyPreparationError = null;
        return "Armed runtime-only sceneLoaded preparation for seed " + seed + ". No scene or source asset was saved.";
    }

    private static ExperimentRequest ReadinessRequest(string source, string action, int? seed = null)
        => new ExperimentRequest { schemaVersion = 2, adapterId = FourTigerCombatAdapter.AdapterId,
            sourceGuid = source, actionId = action, scenarioMode = "fixed", seed = seed ?? _runtimeSeed };

    public static string Start(string sourceGuid = DefaultSource, string actionId = "normal-1",
        string candidateJson = null, string exportedAssetPath = null, bool sourceControl = false)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode, then call Start. No scene will be saved.");
        if (_session != null || _pending != null) throw new InvalidOperationException("A playback verification is already running.");
        _source = null; _sourceBefore = null; _report = null; _request = null; _candidate = null;
        _readinessRequest = ReadinessRequest(sourceGuid, actionId);
        _state = new State { status = "preparing", sourceGuid = sourceGuid, actionId = actionId };
        if (!RuntimeReady(out string readyReason))
        {
            _pending = new Pending { source = sourceGuid, action = actionId, json = candidateJson,
                exportedPath = exportedAssetPath, control = sourceControl };
            _waitStarted = EditorApplication.timeSinceStartup;
            _state.status = "waiting-ready"; _state.error = readyReason;
            return Status();
        }
        try
        {
            if (!string.IsNullOrEmpty(exportedAssetPath))
            {
                sourceGuid = AssetDatabase.AssetPathToGUID(exportedAssetPath);
                if (string.IsNullOrEmpty(sourceGuid)) throw new ArgumentException("The exported asset path has no Unity GUID.");
                _state.sourceGuid = sourceGuid;
            }
            _source = FourTigerCombatAdapter.ResolveSource(sourceGuid);
            _sourceBefore = EditorJsonUtility.ToJson(_source);
            var adapter = new FourTigerCombatAdapter();
            var specs = adapter.DescribeFeedback(sourceGuid, actionId);
            _candidate = string.IsNullOrWhiteSpace(candidateJson) ? DefaultCandidate(specs) : JsonUtility.FromJson<CombatCandidate>(candidateJson);
            if (_candidate == null) throw new ArgumentException("Candidate JSON is invalid.");
            if (sourceControl)
                _candidate = new CombatCandidate { id = "source-control", label = "원본 그대로", isSourceControl = true };
            _request = new ExperimentRequest { schemaVersion = 2, adapterId = FourTigerCombatAdapter.AdapterId,
                sourceGuid = sourceGuid, actionId = actionId, durationSeconds = 5f, recordVideo = true,
                scenarioMode = "fixed", precisionCapture = true, seed = _runtimeSeed,
                candidates = new List<CombatCandidate> { sourceControl
                    ? new CombatCandidate { id = "control-request", label = "Internal source-control request" } : _candidate } };
            CombatLab.ValidateData(_request);
            string id = "feedback-playback-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _state.directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "CombatLabResults", id));
            Directory.CreateDirectory(_state.directory);
            File.WriteAllText(Path.Combine(_state.directory, "request.json"), JsonUtility.ToJson(_request, true));
            _report = new ExperimentReport { schemaVersion = 2, runId = id, adapterId = _request.adapterId, sourceGuid = sourceGuid,
                actionId = actionId, sourceDependencyHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(_source)).ToString(),
                unityVersion = Application.unityVersion, scenePath = SceneManager.GetActiveScene().path,
                startedUtc = DateTime.UtcNow.ToString("O"), seed = _request.seed, request = _request };
            _report.notes.Add("Explicit diagnostic: direct adapter session in the existing Play Mode. The editor scene was not saved. Runtime enemies are made passive before preflight, and the player action is cancelled to idle. Exit Play Mode to restore the editor snapshot.");
            _report.notes.Add("Production scenario preparation: " + _scenarioPreparationNote);
            foreach (var enemy in Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            {
                PassiveBefore[enemy] = enemy.CombatLabPassive;
                enemy.CombatLabPassive = true;
            }
            var player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null || player.StatManager == null || player.StatManager.CurrentHp <= 0)
                throw new InvalidOperationException("The diagnostic requires a live initialized player.");
            player.CancelHitStops();
            player.StateMachine.ChangeState(new PlayerIdleState(player));
            player.SetAnimationSpeed(1);
            CombatHitStopService.SetExternalWorldTimeScale(1);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            _session = adapter.Begin(_request, _candidate);
            _state.status = "waiting-arm"; _waitStarted = EditorApplication.timeSinceStartup;
            return Status();
        }
        catch (Exception error)
        {
            Finish(error.ToString());
            return Status();
        }
    }

    public static string Status() => JsonUtility.ToJson(_state, true);
    public static string Cancel()
    {
        Abort("Cancelled by caller.", true);
        return Status();
    }

    public static string StartSuite()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode, then invoke StartSuite.");
        if (_session != null || _pending != null || _suiteRunning) throw new InvalidOperationException("Verification is already running.");
        _suiteRunning = true; _suiteInitialized = false;
        _readinessRequest = ReadinessRequest(DefaultSource, "normal-1");
        _state = new State { status = "waiting-ready" };
        SuiteQueue.Clear(); SuitePassiveBefore.Clear();
        _suite = new SuiteState { status = "waiting-ready", directory = Path.GetFullPath(Path.Combine(Application.dataPath,
            "..", "CombatLabResults", "feedback-suite-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8))) };
        _waitStarted = EditorApplication.timeSinceStartup;
        return SuiteStatus();
    }
    public static string SuiteStatus()
    {
        _suite.current = _state;
        return JsonUtility.ToJson(_suite, true);
    }

    private static bool RuntimeReady(out string reason)
    {
        if (!string.IsNullOrEmpty(_earlyPreparationError)) { reason = _earlyPreparationError; return false; }
        var preparation = new FourTigerCombatAdapter();
        try
        {
            if (!_scenarioPrepared)
            {
                if (!preparation.PrepareScenario(_readinessRequest ?? ReadinessRequest(DefaultSource, "normal-1"), out reason)) return false;
                _scenarioPrepared = true; _scenarioPreparationNote = reason;
            }
            if (!preparation.IsReady(_readinessRequest ?? ReadinessRequest(DefaultSource, "normal-1"), out reason)) return false;
        }
        catch (Exception error) { reason = error.ToString(); return false; }
        var players = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None).Where(p => p.isActiveAndEnabled).ToArray();
        if (players.Length != 1) { reason = "Waiting for exactly one active player."; return false; }
        var player = players[0];
        if (player.StatManager == null || player.FormManager == null || player.StateMachine == null
            || player.Animator == null || !player.Animator.enabled || player.Controller == null || !player.Controller.enabled
            || player.Input == null || !player.Input.enabled || player.StatManager.CurrentHp <= 0 || !player.IsGround())
        { reason = "Waiting for initialized, grounded player controls and animation."; return false; }
        bool target = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None).Any(e =>
            e.isActiveAndEnabled && e.EnemyStat != null && !e.EnemyStat.IsDead
            && e.TryGetComponent<NavMeshAgent>(out var nav) && nav.enabled && nav.isOnNavMesh
            && e.GetComponentsInChildren<Collider>().Any(c => c.enabled && c.gameObject.activeInHierarchy
                && c.gameObject.layer == LayerMask.NameToLayer("Enemy")));
        reason = target ? null : "Waiting for a live configured target on the NavMesh.";
        return target;
    }

    private static void InitializeSuite()
    {
        var player = Object.FindFirstObjectByType<PlayerController>();
        _suitePlayerPosition = player.transform.position; _suitePlayerRotation = player.transform.rotation;
        var adapter = new FourTigerCombatAdapter();
        var discovered = adapter.DiscoverSources();
        string Source(Type type, string field)
        {
            var selected = new SerializedObject(player).FindProperty(field)?.objectReferenceValue as WeaponActionDataSO;
            string guid = selected != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selected)) : null;
            if (!string.IsNullOrEmpty(guid) && discovered.Any(s => s.guid == guid)) return guid;
            var fallback = discovered.FirstOrDefault(s => FourTigerCombatAdapter.ResolveSource(s.guid).GetType() == type);
            if (fallback == null) throw new InvalidOperationException("No source for representative form " + type.Name);
            return fallback.guid;
        }
        var forms = new[] {
            Source(typeof(WaterFormActionDataSO), "_WaterFormActionData"),
            Source(typeof(FireFormActionDataSO), "_FireFormActionData"),
            Source(typeof(EarthFormActionDataSO), "_EarthFormActionData"),
            Source(typeof(WoodFormActionDataSO), "_WoodFormActionData"),
            Source(typeof(IronFormActionDataSO), "_IronFormActionData")
        };
        foreach (string guid in forms) SuiteQueue.Add(new Pending { source = guid, action = "normal-1" });
        SuiteQueue.Add(new Pending { source = forms[0], action = "heavy" });
        foreach (string guid in forms) SuiteQueue.Add(new Pending { source = guid, action = "skill" });
        SuiteQueue.Add(new Pending { source = forms[0], action = "ultimate" });
        SuiteQueue.Add(new Pending { source = forms[4], action = "ultimate" });
        for (int i = 1; i <= 3; i++) SuiteQueue.Add(new Pending { source = forms[i], action = "ultimate" });
        SuiteQueue.Add(new Pending { source = forms[0], action = "normal-combo" });
        foreach (var enemy in Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
        {
            SuitePassiveBefore[enemy] = enemy.CombatLabPassive;
            enemy.CombatLabPassive = true;
        }
        _suite.total = SuiteQueue.Count; _suite.status = "running"; _suiteInitialized = true;
        Directory.CreateDirectory(_suite.directory);
        File.WriteAllText(Path.Combine(_suite.directory, "notes.txt"),
            "17 representative five-second fixed-target diagnostics in ONE Play Mode. No fresh scene/world reset between cases. Player position is restored before each case; sessions restore their cloned form/input/resources and target fixture. This verifies supported execution/capture paths, not deterministic or quality comparisons. No source SO or editor scene is saved.");
    }

    private static void BeginNextSuiteCase()
    {
        if (!_suiteRunning || _session != null || _pending != null) return;
        if (!_suiteInitialized)
        {
            if (!RuntimeReady(out string reason))
            {
                _suite.error = reason;
                if (EditorApplication.timeSinceStartup - _waitStarted > 30) EndSuite("Runtime initialization exceeded 30 seconds: " + reason);
                return;
            }
            try { InitializeSuite(); }
            catch (Exception error) { EndSuite(error.ToString()); return; }
        }
        if (_suite.finished >= SuiteQueue.Count) { EndSuite(null); return; }
        var player = Object.FindFirstObjectByType<PlayerController>();
        if (player == null) { EndSuite("Player disappeared between cases."); return; }
        player.CancelHitStops();
        if (player.StateMachine != null) player.StateMachine.ChangeState(new PlayerIdleState(player));
        bool controllerEnabled = player.Controller != null && player.Controller.enabled;
        if (player.Controller != null) player.Controller.enabled = false;
        player.transform.SetPositionAndRotation(_suitePlayerPosition, _suitePlayerRotation);
        player.VerticalVelocity = 0;
        if (player.Controller != null) player.Controller.enabled = controllerEnabled;
        Physics.SyncTransforms();
        var next = SuiteQueue[_suite.finished];
        Start(next.source, next.action, next.json, next.exportedPath, next.control);
    }

    private static void EndSuite(string error)
    {
        _suiteRunning = false;
        foreach (var item in SuitePassiveBefore)
            if (item.Key != null)
                try { item.Key.CombatLabPassive = item.Value; }
                catch (Exception exception) { error = (error ?? "") + "\nSuite passive restore " + item.Key.name + ": " + exception; }
        SuitePassiveBefore.Clear();
        _suite.error = error; _suite.status = error != null || _suite.failed > 0 ? "failed"
            : _suite.incomplete > 0 ? "incomplete-hits" : "passed";
        if (!string.IsNullOrEmpty(_suite.directory))
        {
            Directory.CreateDirectory(_suite.directory);
            File.WriteAllText(Path.Combine(_suite.directory, "suite.json"), SuiteStatus());
        }
    }

    private static void Abort(string reason, bool cancelled = false)
    {
        bool suite = _suiteRunning;
        _suiteRunning = false;
        _pending = null;
        if (_session != null) Finish(reason, cancelled ? "cancelled" : null);
        _state.status = cancelled ? "cancelled" : "failed"; _state.error = reason;
        if (!string.IsNullOrEmpty(_state.directory)) File.WriteAllText(Path.Combine(_state.directory, "verification.json"), Status());
        if (suite)
        {
            EndSuite(reason);
            if (cancelled)
            {
                _suite.status = "cancelled";
                if (!string.IsNullOrEmpty(_suite.directory)) File.WriteAllText(Path.Combine(_suite.directory, "suite.json"), SuiteStatus());
            }
        }
    }

    private static CombatCandidate DefaultCandidate(List<ParameterSpec> specs)
    {
        var values = new List<ParameterValue>();
        void Add(string id, float value) { if (specs.Any(s => s.id == id)) values.Add(new ParameterValue(id, value)); }
        string stop = specs.FirstOrDefault(s => s.id.StartsWith("step.0.hit.", StringComparison.Ordinal)
            && s.id.EndsWith(".hitstop.duration", StringComparison.Ordinal))?.id;
        stop = stop == null ? "step.0.hitstop" : stop.Substring(0, stop.Length - ".duration".Length);
        Add(stop + ".duration", .045f); Add(stop + ".timeScale", 0); Add(stop + ".scope", (float)HitStopScope.Both);
        Add("step.0.camera.shake.translation.x", .06f); Add("step.0.camera.shake.rotation.z", .8f);
        Add("step.0.camera.shake.duration", .12f); Add("step.0.camera.shake.frequency", 25);
        if (values.Count == 0)
        {
            var buff = specs.FirstOrDefault(s => s.id.StartsWith("form.", StringComparison.Ordinal) && s.kind == "number");
            if (buff != null) values.Add(new ParameterValue(buff.id, Mathf.Clamp(
                buff.useSuggestedRange ? (buff.suggestedMinimum + buff.suggestedMaximum) * .5f : buff.baseline,
                buff.minimum, buff.maximum)));
        }
        return new CombatCandidate { id = "runtime-v2", label = "Local stop and absolute shake", origin = "manual", values = values };
    }

    private static void Poll()
    {
        if (!Application.isPlaying) return;
        if (_pending != null)
        {
            if (RuntimeReady(out string reason))
            {
                var next = _pending; _pending = null;
                Start(next.source, next.action, next.json, next.exportedPath, next.control);
            }
            else if (EditorApplication.timeSinceStartup - _waitStarted > 30)
            { _pending = null; Finish("Runtime initialization exceeded 30 seconds: " + reason); }
            else { _state.error = reason; return; }
        }
        if (_suiteRunning && _session == null) BeginNextSuiteCase();
        if (_session != null && _state.status == "waiting-arm")
        {
            try
            {
                if (_session is ICombatLabReadySession ready && !ready.IsReadyToArm(out string reason))
                {
                    _state.error = reason;
                    if (EditorApplication.timeSinceStartup - _waitStarted > 30) Finish("Fixed fixture did not settle: " + reason);
                    return;
                }
                _recording = new CombatLabRecording();
                _recording.Start(Path.Combine(_state.directory, _candidate.isSourceControl ? "source-control" : "candidate"), true);
                if (!_recording.IsReady) throw new InvalidOperationException("Recorder did not become ready before arming input.");
                (_session as ICombatLabArmedSession)?.Arm();
                _videoOffset = _recording.TimelineSeconds; _observedEvents = 0; _lastFrame = -1;
                _lastPoll = EditorApplication.timeSinceStartup; _state.status = "running"; _state.error = null;
            }
            catch (Exception error) { Finish(error.ToString()); return; }
        }
        if (_session == null || !Application.isPlaying || _lastFrame == Time.frameCount) return;
        _lastFrame = Time.frameCount;
        try
        {
            double now = EditorApplication.timeSinceStartup;
            _session.Tick((float)Math.Max(0, now - _lastPoll));
            _lastPoll = now;
            var metrics = _session.Metrics;
            for (; _observedEvents < metrics.events.Count; _observedEvents++)
            {
                var item = metrics.events[_observedEvents];
                if (item.kind == "confirmed-impact") _recording.MarkImpact(item);
            }
            _state.hits = metrics.hits; _state.damage = metrics.damageDealt; _state.elapsedSeconds = metrics.elapsedSeconds;
            _state.confirmedImpacts = metrics.events.Count(e => e.kind == "confirmed-impact");
            _state.capturedFrames = _recording.CapturedFrameCount;
            ObserveLocalClock();
            if (_session.Complete) Finish(null);
        }
        catch (Exception error) { Finish(error.ToString()); }
    }

    private static void ObserveLocalClock()
    {
        var player = Object.FindFirstObjectByType<PlayerController>();
        if (player == null) return;
        if (player.ActionTimeScale <= 0 && Time.timeScale > 0)
        {
            if (player.ActionDeltaTime != 0 || player.Animator != null && player.Animator.speed != 0)
                throw new InvalidOperationException("A complete local stop did not freeze action delta and animation while world time continued.");
            if (_state.localFrozenFrames++ == 0)
                CombatFeedbackTelemetry.Emit("local-clock-observation", player.gameObject, detail:
                    "state=frozen;worldScale=" + Time.timeScale + ";worldDelta=" + Time.deltaTime
                    + ";actorScale=" + player.ActionTimeScale + ";actorDelta=" + player.ActionDeltaTime
                    + ";animationSpeed=" + (player.Animator != null ? player.Animator.speed : -1));
        }
        else if (_state.localFrozenFrames > 0 && !_state.localResumeObserved && player.ActionTimeScale >= 1
            && (player.Animator == null || player.ActorClock == null || Mathf.Approximately(player.Animator.speed, player.ActorClock.AnimationSpeed)))
        {
            _state.localResumeObserved = true;
            CombatFeedbackTelemetry.Emit("local-clock-observation", player.gameObject, detail:
                "state=resumed;worldScale=" + Time.timeScale + ";actorScale=" + player.ActionTimeScale
                + ";animationSpeed=" + (player.Animator != null ? player.Animator.speed : -1));
        }
    }

    private static void Finish(string error, string forcedStatus = null)
    {
        var session = _session; var recording = _recording;
        _session = null; _recording = null;
        var metrics = session?.Metrics ?? new CombatMetrics();
        try
        {
            recording?.Stop();
            if (error == null && !string.IsNullOrEmpty(recording?.Error)) error = recording.Error;
            if (error == null && (metrics.attackRequests <= 0 || !metrics.events.Any(e => e.kind == "action-executed")))
                error = "The selected action was not requested and executed during this verification.";
            if (error == null && (metrics.hits <= 0 || !metrics.events.Any(e => e.kind == "confirmed-impact")))
                error = "No actual confirmed impact was observed.";
            if (error == null && (recording == null || recording.CapturedFrameCount <= 0 || !File.Exists(recording.OutputPath)))
                error = "No captured render samples/video were produced.";
            if (error == null && _source != null && EditorJsonUtility.ToJson(_source) != _sourceBefore)
                error = "A source asset changed during playback.";
            bool expectsLocalFreeze = metrics.events.Any(e => e.kind == "hitstop-begin" && e.detail != null
                && (e.detail.Contains("scope=Attacker") || e.detail.Contains("scope=Both")) && e.detail.Contains("requested=0;"));
            if (error == null && expectsLocalFreeze && (_state.localFrozenFrames == 0 || !_state.localResumeObserved))
                error = "An actual local zero-scale stop began, but frozen and resumed player frames were not both observed.";
            _state.validationStatus = metrics.validationStatus;
            _state.status = forcedStatus ?? (error != null ? "failed" : metrics.validationStatus == "incomplete-hits" ? "incomplete-hits" : "passed");
            _state.error = error;
            _state.hits = metrics.hits; _state.damage = metrics.damageDealt; _state.elapsedSeconds = metrics.elapsedSeconds;
            _state.confirmedImpacts = metrics.events.Count(e => e.kind == "confirmed-impact");
            _state.capturedFrames = recording?.CapturedFrameCount ?? 0;
            _state.precisionStatus = recording?.Capture?.precisionStatus;
            if (_report != null && !string.IsNullOrEmpty(_state.directory))
            {
                var result = new CandidateResult { candidate = _candidate, metrics = metrics,
                    status = _state.status == "cancelled" ? "cancelled" : error != null ? "failed" : _state.status == "incomplete-hits" ? "incomplete-hits" : "completed", error = error, videoPath = recording?.OutputPath,
                    videoTimeOffsetSeconds = _videoOffset, capture = recording?.Capture };
                if (_candidate?.isSourceControl == true) _report.controlResult = result;
                else _report.results.Add(result);
                if (recording != null) _report.notes.AddRange(recording.Warnings);
                CombatLabReport.Write(_state.directory, _report);
                File.WriteAllText(Path.Combine(_state.directory, "verification.json"), Status());
            }
        }
        catch (Exception failure)
        {
            _state.status = "failed"; _state.error = (error ?? "") + "\n" + failure;
        }
        finally
        {
            try { recording?.Dispose(); } catch (Exception failure) { _state.cleanupErrors.Add("Recording dispose: " + failure); Debug.LogException(failure); }
            try { session?.Dispose(); } catch (Exception failure) { _state.cleanupErrors.Add("Session dispose: " + failure); Debug.LogException(failure); }
            foreach (var item in PassiveBefore)
                if (item.Key != null)
                    try { item.Key.CombatLabPassive = item.Value; }
                    catch (Exception failure) { _state.cleanupErrors.Add("Passive restore " + item.Key.name + ": " + failure); }
            PassiveBefore.Clear();
            if (_state.cleanupErrors.Count > 0) _state.status = "failed";
            if (!string.IsNullOrEmpty(_state.directory)) File.WriteAllText(Path.Combine(_state.directory, "verification.json"), Status());
            if (_suiteRunning)
            {
                _suite.cases.Add(JsonUtility.FromJson<State>(JsonUtility.ToJson(_state)));
                _suite.finished++;
                if (_state.status == "passed") _suite.passed++;
                else if (_state.status == "incomplete-hits") _suite.incomplete++;
                else _suite.failed++;
                File.WriteAllText(Path.Combine(_suite.directory, "suite.json"), SuiteStatus());
            }
        }
    }
}
