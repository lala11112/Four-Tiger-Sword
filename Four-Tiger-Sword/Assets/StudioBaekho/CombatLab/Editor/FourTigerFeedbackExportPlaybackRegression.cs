// Explicit diagnostic: create the fixture in Edit Mode, then compare two captured sessions in one Play Mode.
// No scene is saved. Only this helper's registered temporary asset folder may be deleted by CleanupFixture.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using StudioBaekho.CombatLab;
using StudioBaekho.CombatLab.FourTigerSword;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class FourTigerFeedbackExportPlaybackRegression
{
    private const string FixtureKey = "CombatLab.FeedbackExportPlayback.Fixture";
    private const string Prefix = "Assets/CombatLabExportPlayback_";
    [Serializable] private sealed class Fixture
    {
        public string id, directory, sourceGuid, actionId, candidateJson, exportedAssetPath, sourceHash;
        public string authoredSnapshotHash;
    }
    [Serializable] private sealed class Snapshot
    {
        public List<WeaponActionData> steps = new List<WeaponActionData>();
        public List<ParameterValue> values = new List<ParameterValue>();
    }
    [Serializable] private sealed class PlaybackState
    {
        public string status, directory, error, validationStatus;
        public int hits, confirmedImpacts, capturedFrames, localFrozenFrames;
        public bool localResumeObserved;
        public List<string> cleanupErrors = new List<string>();
    }
    [Serializable] private sealed class Comparison
    {
        public string status = "idle", directory, error, authoredSnapshotHash;
        public string requestSnapshotHash, exportedSnapshotHash;
        public bool sourceUnchanged, runtimeDataEqual;
        public List<string> cleanupErrors = new List<string>();
        public PlaybackState requestPlayback, exportedPlayback;
        public string note = "Two captured fixed-target sessions in the same Play Mode. Exact serialized runtime data and resolved bindings are compared; physics, damage and frame timings are not claimed deterministic. No scene is saved.";
    }
    private static Fixture _fixture;
    private static Comparison _comparison = new Comparison();
    private static string _requestSnapshot, _exportedSnapshot;
    private static int _phase;
    private static bool _running, _started;
    private static Vector3 _playerPosition;
    private static Quaternion _playerRotation;
    private static bool _positionSaved;
    private static readonly Dictionary<Enemy, bool> PassiveBefore = new Dictionary<Enemy, bool>();

    static FourTigerFeedbackExportPlaybackRegression()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += change => {
            if (_running && change == PlayModeStateChange.ExitingPlayMode) Finish("Play Mode ended before comparison completed.");
        };
        AssemblyReloadEvents.beforeAssemblyReload += () => { if (_running) Finish("Assembly reload interrupted comparison."); };
    }

    public static string CreateFixture(string sourceGuid = "4b73531aafb753149bfae2eb4ee665f6",
        string actionId = "normal-1", string candidateJson = null)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Create the export fixture in Edit Mode.");
        if (!string.IsNullOrEmpty(SessionState.GetString(FixtureKey, "")))
            throw new InvalidOperationException("A fixture is already registered. Call CleanupFixture in Edit Mode first.");
        var source = FourTigerCombatAdapter.ResolveSource(sourceGuid);
        string before = EditorJsonUtility.ToJson(source);
        var adapter = new FourTigerCombatAdapter();
        var candidate = string.IsNullOrWhiteSpace(candidateJson)
            ? (CombatCandidate)typeof(FourTigerFeedbackPlaybackRegression).GetMethod("DefaultCandidate", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { adapter.DescribeFeedback(sourceGuid, actionId) })
            : JsonUtility.FromJson<CombatCandidate>(candidateJson);
        if (candidate == null || candidate.isSourceControl) throw new ArgumentException("Use a patched candidate for an export equivalence check.");
        var request = new ExperimentRequest { schemaVersion = 2, adapterId = FourTigerCombatAdapter.AdapterId,
            sourceGuid = sourceGuid, actionId = actionId, durationSeconds = 5, scenarioMode = "fixed", precisionCapture = true,
            candidates = new List<CombatCandidate> { candidate } };
        CombatLab.ValidateData(request);
        var fixture = new Fixture { id = Guid.NewGuid().ToString("N"), sourceGuid = sourceGuid, actionId = actionId,
            candidateJson = JsonUtility.ToJson(candidate), sourceHash = Hash(before) };
        string folder = Prefix + fixture.id;
        fixture.exportedAssetPath = folder + "/ExportedForm.asset";
        fixture.directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "CombatLabResults", "feedback-export-" + fixture.id));
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", folder.Substring(7))))
            throw new InvalidOperationException("Cannot create an isolated export fixture folder.");
        SessionState.SetString(FixtureKey, JsonUtility.ToJson(fixture));
        var owned = new List<Object>();
        try
        {
            var expected = FourTigerCombatAdapter.CloneAndApply(source, candidate, owned, actionId, 2);
            string expectedJson = SnapshotData(expected, actionId, null);
            adapter.ExportCombined(new CombinedExportRequest { adapterId = FourTigerCombatAdapter.AdapterId, sourceGuid = sourceGuid,
                selections = new List<CombatActionSelection> { new CombatActionSelection { request = request, candidateId = candidate.id } } },
                fixture.exportedAssetPath);
            var exported = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(fixture.exportedAssetPath);
            if (exported == null || SnapshotData(exported, actionId, null) != expectedJson)
                throw new InvalidOperationException("Reloaded exported SO differs from CloneAndApply before playback.");
            if (EditorJsonUtility.ToJson(source) != before) throw new InvalidOperationException("Export changed the original source.");
            fixture.authoredSnapshotHash = Hash(expectedJson);
            Directory.CreateDirectory(fixture.directory);
            File.WriteAllText(Path.Combine(fixture.directory, "authored-snapshot.json"), expectedJson);
            File.WriteAllText(Path.Combine(fixture.directory, "fixture.json"), JsonUtility.ToJson(fixture, true));
            SessionState.SetString(FixtureKey, JsonUtility.ToJson(fixture));
            return JsonUtility.ToJson(fixture, true);
        }
        catch
        {
            AssetDatabase.DeleteAsset(folder); SessionState.EraseString(FixtureKey); throw;
        }
        finally { foreach (var item in owned) if (item != null && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item); }
    }

    public static string StartComparison()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before StartComparison. No scene will be saved.");
        if (_running) throw new InvalidOperationException("An export comparison is already running.");
        var playback = JsonUtility.FromJson<PlaybackState>(FourTigerFeedbackPlaybackRegression.Status());
        if (IsPending(playback.status))
            throw new InvalidOperationException("Wait for the existing playback check to finish.");
        var suite = JsonUtility.FromJson<PlaybackState>(FourTigerFeedbackPlaybackRegression.SuiteStatus());
        if (IsPending(suite.status))
            throw new InvalidOperationException("Wait for the representative playback suite to finish.");
        string json = SessionState.GetString(FixtureKey, "");
        if (string.IsNullOrEmpty(json)) throw new InvalidOperationException("CreateFixture must first create and register an Edit Mode export.");
        _fixture = JsonUtility.FromJson<Fixture>(json);
        _comparison = new Comparison { status = "running", directory = _fixture.directory, authoredSnapshotHash = _fixture.authoredSnapshotHash };
        _requestSnapshot = _exportedSnapshot = null; _phase = 0; _started = false; _positionSaved = false;
        PassiveBefore.Clear(); _running = true;
        return Status();
    }
    public static string Status() => JsonUtility.ToJson(_comparison, true);
    public static string Cancel()
    {
        if (_running) Finish("Cancelled by caller.", true);
        return Status();
    }

    public static string CleanupFixture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || _running)
            throw new InvalidOperationException("Exit Play Mode before deleting this helper's temporary export.");
        string json = SessionState.GetString(FixtureKey, "");
        if (string.IsNullOrEmpty(json)) return "No fixture is registered.";
        var fixture = JsonUtility.FromJson<Fixture>(json);
        string folder = Path.GetDirectoryName(fixture.exportedAssetPath)?.Replace('\\', '/');
        if (folder != Prefix + fixture.id || fixture.id == null || fixture.id.Length != 32)
            throw new InvalidOperationException("Registered fixture path is invalid; no asset was deleted.");
        if (AssetDatabase.IsValidFolder(folder) && !AssetDatabase.DeleteAsset(folder))
            throw new InvalidOperationException("Cannot remove the registered temporary asset folder.");
        SessionState.EraseString(FixtureKey);
        return "Removed only " + folder + ". Comparison reports remain in " + fixture.directory;
    }

    private static void Poll()
    {
        if (!_running || !Application.isPlaying) return;
        try
        {
            var runtimePlayer = Object.FindFirstObjectByType<PlayerController>();
            if (!_positionSaved && runtimePlayer != null && runtimePlayer.Controller != null && runtimePlayer.StateMachine != null)
            {
                _playerPosition = runtimePlayer.transform.position; _playerRotation = runtimePlayer.transform.rotation; _positionSaved = true;
                foreach (var enemy in Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                { PassiveBefore[enemy] = enemy.CombatLabPassive; enemy.CombatLabPassive = true; }
            }
            if (!_started)
            {
                var player = runtimePlayer;
                if (_phase == 1 && _positionSaved && player != null)
                {
                    bool enabled = player.Controller != null && player.Controller.enabled;
                    if (player.Controller != null) player.Controller.enabled = false;
                    player.transform.SetPositionAndRotation(_playerPosition, _playerRotation); player.VerticalVelocity = 0;
                    if (player.Controller != null) player.Controller.enabled = enabled;
                    Physics.SyncTransforms();
                }
                string empty = JsonUtility.ToJson(new CombatCandidate { id = "export-direct", label = "Exported SO, empty patch", origin = "manual" });
                _started = true;
                FourTigerFeedbackPlaybackRegression.Start(_fixture.sourceGuid, _fixture.actionId,
                    _phase == 0 ? _fixture.candidateJson : empty, _phase == 0 ? null : _fixture.exportedAssetPath);
            }
            CaptureRuntimeData();
            var state = JsonUtility.FromJson<PlaybackState>(FourTigerFeedbackPlaybackRegression.Status());
            if (IsPending(state.status)) return;
            if (_phase == 0) _comparison.requestPlayback = state;
            else _comparison.exportedPlayback = state;
            if (state.status == "cancelled") { Finish(state.error ?? "Playback cancelled by caller.", true); return; }
            if (state.status != "passed" && state.status != "incomplete-hits")
            { Finish("Playback " + state.status + ": " + (state.error ?? string.Join("\n", state.cleanupErrors))); return; }
            if (_phase == 0)
            {
                _comparison.requestPlayback = state;
                if (_requestSnapshot == null) { Finish("No runtime data snapshot was captured for the request session."); return; }
                _phase = 1; _started = false; return;
            }
            _comparison.exportedPlayback = state;
            _comparison.runtimeDataEqual = _requestSnapshot != null && _requestSnapshot == _exportedSnapshot;
            _comparison.sourceUnchanged = Hash(EditorJsonUtility.ToJson(FourTigerCombatAdapter.ResolveSource(_fixture.sourceGuid))) == _fixture.sourceHash;
            if (!_comparison.runtimeDataEqual) { Finish("Request and exported SO produced different runtime steps or resolved bindings."); return; }
            if (!_comparison.sourceUnchanged) { Finish("Original source changed since fixture creation."); return; }
            Finish(null);
        }
        catch (Exception exception) { Finish(exception.ToString()); }
    }

    private static void CaptureRuntimeData()
    {
        if (_phase == 0 && _requestSnapshot != null || _phase == 1 && _exportedSnapshot != null) return;
        var session = typeof(FourTigerFeedbackPlaybackRegression).GetField("_session", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        if (session == null) return;
        var data = session.GetType().GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(session) as WeaponActionDataSO;
        var steps = session.GetType().GetField("_selectedSteps", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(session) as List<WeaponActionData>;
        if (data == null || steps == null) throw new InvalidOperationException("Runtime session data contract changed; update the diagnostic helper.");
        string action = _fixture.actionId.StartsWith("normal-", StringComparison.Ordinal) ? "normal-combo" : _fixture.actionId;
        string snapshot = SnapshotData(data, action, steps);
        if (_phase == 0) { _requestSnapshot = snapshot; _comparison.requestSnapshotHash = Hash(snapshot); }
        else { _exportedSnapshot = snapshot; _comparison.exportedSnapshotHash = Hash(snapshot); }
        File.WriteAllText(Path.Combine(_fixture.directory, _phase == 0 ? "request-runtime-snapshot.json" : "exported-runtime-snapshot.json"), snapshot);
    }

    private static string SnapshotData(WeaponActionDataSO data, string actionId, List<WeaponActionData> steps)
        => JsonUtility.ToJson(new Snapshot { steps = steps ?? FourTigerCombatAdapter.SelectedSteps(data, actionId) ?? new List<WeaponActionData>(),
            values = FourTigerCombatAdapter.AppliedFeedbackValues(data, actionId).OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ParameterValue(pair.Key, pair.Value)).ToList() }, true);

    private static string Hash(string input)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(input))).Replace("-", "").ToLowerInvariant();
    }

    private static bool IsPending(string status)
        => status == "preparing" || status == "waiting-ready" || status == "waiting-arm" || status == "running";

    private static void Finish(string error, bool cancelled = false)
    {
        _running = false;
        _comparison.error = error;
        _comparison.status = cancelled ? "cancelled" : error != null ? "failed" : _comparison.requestPlayback?.status == "incomplete-hits"
            || _comparison.exportedPlayback?.status == "incomplete-hits" ? "incomplete-hits" : "passed";
        if (error != null && _started)
            try
            {
                var current = JsonUtility.FromJson<PlaybackState>(FourTigerFeedbackPlaybackRegression.Status());
                // Completed failures already contain the first cause. Cancelling them would overwrite it.
                if (IsPending(current.status))
                {
                    FourTigerFeedbackPlaybackRegression.Cancel();
                    current = JsonUtility.FromJson<PlaybackState>(FourTigerFeedbackPlaybackRegression.Status());
                }
                if (_phase == 0 && _comparison.requestPlayback == null) _comparison.requestPlayback = current;
                if (_phase == 1 && _comparison.exportedPlayback == null) _comparison.exportedPlayback = current;
            }
            catch (Exception exception) { _comparison.cleanupErrors.Add("Playback cancel: " + exception); }
        foreach (var item in PassiveBefore)
            if (item.Key != null)
                try { item.Key.CombatLabPassive = item.Value; }
                catch (Exception exception) { _comparison.cleanupErrors.Add("Passive restore " + item.Key.name + ": " + exception); }
        PassiveBefore.Clear();
        if (_comparison.cleanupErrors.Count > 0 && _comparison.status == "passed") _comparison.status = "failed";
        if (!string.IsNullOrEmpty(_comparison.directory))
        {
            try { Directory.CreateDirectory(_comparison.directory); File.WriteAllText(Path.Combine(_comparison.directory, "comparison.json"), Status()); }
            catch (Exception exception) { _comparison.cleanupErrors.Add("Comparison report: " + exception); _comparison.status = "failed"; }
        }
    }
}
