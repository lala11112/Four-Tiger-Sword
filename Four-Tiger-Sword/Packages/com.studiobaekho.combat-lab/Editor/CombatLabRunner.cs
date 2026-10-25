using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StudioBaekho.CombatLab
{
    [InitializeOnLoad]
    public static class CombatLabRunner
    {
        private const string StateKey = "Baekho.CombatLab.Run.v1";
        private const string LastKey = "Baekho.CombatLab.LastReport";
        [Serializable] private sealed class RunState
        {
            public ExperimentRequest request;
            public string directory;
            public string phase;
            public int candidateIndex;
            public int repeatIndex;
            public bool cancelled;
            public double phaseStarted;
            public float originalTimeScale;
            public bool originalRunInBackground;
            public int originalCaptureFramerate;
            public float originalCaptureDeltaTime;
            public bool earlyPreparationApplied, scenePrepared;
            public string preparationError, preparationNote;
            public int settledFrames;
        }
        private static RunState state;
        private static ExperimentReport report;
        private static ICombatLabSession session;
        private static CombatLabRecording recording;
        private static double lastTick;
        private static int lastFrame = -1;
        private static float videoTimeOffset;
        private static bool ending;
        private static int eventCursor;
        private static int lastSettlingFrame = -1;
        private static double sessionStarted;
        private static CombatCandidate CurrentCandidate => state.candidateIndex < 0
            ? new CombatCandidate { id = "source-control", label = "원본 (미적용)", isSourceControl = true, origin = "source" }
            : state.request.candidates[state.candidateIndex];
        private static void Advance()
        {
            if (state.candidateIndex < 0) { state.candidateIndex = 0; state.repeatIndex = 0; }
            else if (++state.repeatIndex >= Math.Max(1, state.request.repeatCount)) { state.candidateIndex++; state.repeatIndex = 0; }
        }

        static CombatLabRunner()
        {
            var saved = SessionState.GetString(StateKey, "");
            if (!string.IsNullOrEmpty(saved))
            {
                try
                {
                    state = JsonUtility.FromJson<RunState>(saved);
                    report = JsonUtility.FromJson<ExperimentReport>(File.ReadAllText(Path.Combine(state.directory, "report.json")));
                }
                catch (Exception e) { Debug.LogException(e); state = null; SessionState.EraseString(StateKey); }
            }
            EditorApplication.playModeStateChanged += OnPlayMode;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.quitting += OnQuit;
        }

        public static bool IsRunning => state != null;
        public static string LastDirectory => SessionState.GetString(LastKey, "");
        public static string Status => state == null ? "대기 중" : state.candidateIndex < 0 ? "원본 비교 · " + state.phase
            : $"AI·편집 후보 {state.candidateIndex + 1}/{state.request.candidates.Count} · {state.repeatIndex + 1}/{Math.Max(1, state.request.repeatCount)}회 · {state.phase}";
        public static string StatusJson() => JsonUtility.ToJson(new StatusData {
            running = IsRunning, status = Status, directory = state?.directory ?? LastDirectory
        });
        [Serializable] private sealed class StatusData { public bool running; public string status; public string directory; }

        public static string Start(ExperimentRequest request)
        {
            if (state != null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("편집 모드에서 다른 실행이 끝난 뒤 시작하세요.");
            if (EditorSettings.enterPlayModeOptionsEnabled && EditorSettings.enterPlayModeOptions != EnterPlayModeOptions.None)
                throw new InvalidOperationException("후보마다 상태를 초기화하려면 Enter Play Mode의 Reload Domain과 Reload Scene을 모두 켜세요.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty || string.IsNullOrEmpty(scene.path))
                    throw new InvalidOperationException("열린 씬을 저장한 뒤 실행하세요. 도구는 씬을 자동 저장하지 않습니다.");
            }
            CombatLab.Validate(request);
            // Freeze the request. Editor fields and external callers may keep mutating their copy.
            request = JsonUtility.FromJson<ExperimentRequest>(JsonUtility.ToJson(request));
            string id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "CombatLabResults", id));
            Directory.CreateDirectory(directory);
            report = new ExperimentReport {
                schemaVersion = request.schemaVersion, request = request,
                runId = id, adapterId = request.adapterId, sourceGuid = request.sourceGuid, actionId = request.actionId,
                sourceDependencyHash = SourceHash(request.sourceGuid), unityVersion = Application.unityVersion,
                scenePath = SceneManager.GetActiveScene().path, seed = request.seed, startedUtc = DateTime.UtcNow.ToString("O")
            };
            report.notes.Add("규칙 기반 플레이 정책. 후보별 새 Play Mode. 동일 시드는 물리/프레임/적 AI의 완전한 재현을 보장하지 않습니다.");
            report.notes.Add(request.generation != null && !string.IsNullOrEmpty(request.generation.provider)
                ? "AI가 생성한 후보를 실행합니다. 후보 생성 정보는 request.json에 저장됩니다. 설명은 예상 효과이며 실제 타격감은 영상을 보고 판단하세요."
                : "JSON 입력 후보를 실행합니다. 자동 플레이는 규칙 기반 정책입니다.");
            File.WriteAllText(Path.Combine(directory, "request.json"), JsonUtility.ToJson(request, true));
            File.WriteAllText(Path.Combine(directory, "source-snapshot.json"), EditorJsonUtility.ToJson(
                AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(request.sourceGuid)), true));
            File.WriteAllText(Path.Combine(directory, "scene-dependency-hash.txt"), AssetDatabase.GetAssetDependencyHash(report.scenePath).ToString());
            SnapshotSourceContext(request, report, directory);
            state = new RunState { request = request, directory = directory,
                candidateIndex = request.schemaVersion == 2 && request.includeSourceControl ? -1 : 0,
                originalTimeScale = Time.timeScale, originalRunInBackground = Application.runInBackground,
                originalCaptureFramerate = Time.captureFramerate, originalCaptureDeltaTime = Time.captureDeltaTime };
            SessionState.SetString(LastKey, directory);
            try { SaveReport(); EnterNext(); }
            catch
            {
                RestoreGlobals(); state = null; SessionState.EraseString(StateKey); throw;
            }
            return directory;
        }

        private static string SourceHash(string guid) => AssetDatabase.GetAssetDependencyHash(AssetDatabase.GUIDToAssetPath(guid)).ToString();

        private static void SnapshotSourceContext(ExperimentRequest request, ExperimentReport output, string directory)
        {
            if (!(CombatLab.Adapter(request.adapterId) is ICombatLabFeedbackAdapter feedback)) return;
            string context = feedback.DescribeContext(request.sourceGuid, request.actionId);
            PersistSourceContext(output, directory, context);
        }

        private static void PersistSourceContext(ExperimentReport output, string directory, string context)
        {
            if (string.IsNullOrWhiteSpace(context))
                throw new InvalidOperationException("연결이 원본 동작 컨텍스트를 반환하지 않았습니다.");
            output.sourceContext = context;
            output.sourceContextPath = "source-context.json";
            output.sourceContextCapturedUtc = DateTime.UtcNow.ToString("O");
            using (var hash = SHA256.Create())
                output.sourceContextHash = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(context))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(Path.Combine(directory, output.sourceContextPath), context, new UTF8Encoding(false));
            output.notes.Add("source-context.json은 실행 전 편집 모드에서 연결이 설명한 원본 동작·상속 설정입니다. 실제 Play Mode 적용값과 시작 조건은 각 결과에 별도 기록합니다.");
        }

        /// <summary>Checks observed fixed-fixture conditions, without moving or correcting any scene object.</summary>
        public static void ValidateFixtureConditions(ExperimentReport output, CandidateResult result)
        {
            if (output == null || result == null) throw new ArgumentNullException(output == null ? nameof(output) : nameof(result));
            var validation = new FixtureConditionValidation { executionStatus = result.status };
            result.fixtureValidation = validation;
            if (output.request?.schemaVersion != 2 || output.request.scenarioMode != "fixed") return;

            var required = new[] { "runtime.player.start.x", "runtime.player.start.y", "runtime.player.start.z", "runtime.fixture.playerYaw",
                "runtime.fixture.target.x", "runtime.fixture.target.y", "runtime.fixture.target.z",
                "runtime.fixture.camera.x", "runtime.fixture.camera.y", "runtime.fixture.camera.z", "runtime.fixture.cameraPitch", "runtime.fixture.cameraYaw", "runtime.fixture.cameraRoll", "runtime.fixture.cameraFov" };
            Dictionary<string, float> Read(CandidateResult item, bool reference)
            {
                var values = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (string id in required)
                {
                    var matches = item?.metrics?.appliedValues?.Where(v => v != null && v.id == id).ToList();
                    if (matches == null || matches.Count != 1 || !CombatLab.Finite(matches[0].value))
                    {
                        validation.differences.Add(new FixtureConditionDifference { id = id,
                            reason = (reference ? "reference-" : "actual-") + (matches == null || matches.Count == 0 ? "missing" : matches.Count != 1 ? "duplicate" : "nonfinite") });
                        continue;
                    }
                    values.Add(id, matches[0].value);
                    if (!reference) validation.snapshot.Add(new ParameterValue(id, matches[0].value));
                }
                return values;
            }
            string Identity(CandidateResult item, bool reference)
            {
                var events = item?.metrics?.events?.Where(e => e != null && e.kind == "fixed-target-ready").ToList();
                if (events == null || events.Count != 1 || string.IsNullOrWhiteSpace(events[0].detail))
                {
                    validation.differences.Add(new FixtureConditionDifference { id = "fixed-target-identity",
                        reason = (reference ? "reference-" : "actual-") + "identity-unavailable" });
                    return null;
                }
                return events[0].detail;
            }
            var actual = Read(result, false);
            validation.targetIdentity = Identity(result, false);
            CandidateResult baseline = result.candidate?.isSourceControl == true ? null : output.controlResult;
            if (baseline == null && output.request.includeSourceControl && result.candidate?.isSourceControl != true)
                validation.differences.Add(new FixtureConditionDifference { id = "fixture-reference", reason = "source-control-unavailable" });
            else if (baseline == null && result.candidate?.isSourceControl != true && output.results != null && output.results.Count > 0) baseline = output.results[0];
            if (baseline == null)
            {
                validation.referenceCandidateId = result.candidate?.id;
                validation.referenceRepeatIndex = result.repeatIndex;
                validation.referenceTargetIdentity = validation.targetIdentity;
                validation.status = validation.differences.Count == 0 ? "baseline" : "unavailable";
            }
            else
            {
                validation.referenceCandidateId = baseline.candidate?.id;
                validation.referenceRepeatIndex = baseline.repeatIndex;
                var reference = Read(baseline, true);
                validation.referenceTargetIdentity = Identity(baseline, true);
                bool unavailable = validation.differences.Count != 0;
                if (!unavailable)
                {
                    if (!string.Equals(validation.referenceTargetIdentity, validation.targetIdentity, StringComparison.Ordinal))
                        validation.differences.Add(new FixtureConditionDifference { id = "fixed-target-identity", reason = "different-stable-target" });
                    void Position(string label, string prefix)
                    {
                        var expected = new Vector3(reference[prefix + ".x"], reference[prefix + ".y"], reference[prefix + ".z"]);
                        var observed = new Vector3(actual[prefix + ".x"], actual[prefix + ".y"], actual[prefix + ".z"]);
                        float distance = Vector3.Distance(expected, observed);
                        if (distance > validation.positionTolerance)
                            validation.differences.Add(new FixtureConditionDifference { id = label, reason = "position-distance",
                                referencePresent = true, actualPresent = true, referencePosition = expected, actualPosition = observed,
                                delta = distance, tolerance = validation.positionTolerance });
                    }
                    void Scalar(string id, float tolerance, bool angle)
                    {
                        float delta = angle ? Mathf.Abs(Mathf.DeltaAngle(reference[id], actual[id])) : Mathf.Abs(reference[id] - actual[id]);
                        if (delta > tolerance)
                            validation.differences.Add(new FixtureConditionDifference { id = id, reason = angle ? "yaw-angle" : "scalar-distance",
                                referencePresent = true, actualPresent = true, referenceValue = reference[id], actualValue = actual[id], delta = delta, tolerance = tolerance });
                    }
                    Position("player-position", "runtime.player.start");
                    Position("target-position", "runtime.fixture.target");
                    Position("camera-position", "runtime.fixture.camera");
                    Scalar("runtime.fixture.playerYaw", validation.angleTolerance, true);
                    Scalar("runtime.fixture.cameraPitch", validation.angleTolerance, true);
                    Scalar("runtime.fixture.cameraYaw", validation.angleTolerance, true);
                    Scalar("runtime.fixture.cameraRoll", validation.angleTolerance, true);
                    Scalar("runtime.fixture.cameraFov", validation.fovTolerance, false);
                }
                validation.status = unavailable ? "unavailable" : validation.differences.Count == 0 ? "matched" : "mismatch";
            }
            if (result.metrics == null) result.metrics = new CombatMetrics();
            if (validation.status == "mismatch" || validation.status == "unavailable")
            {
                string message = validation.status == "mismatch"
                    ? "고정 시작 조건이 기준 실행과 다릅니다. 이 결과를 같은 조건의 타격감 비교로 해석하지 마세요."
                    : "고정 시작 조건 또는 기준 실행을 확인할 수 없습니다. 동일 조건 비교가 검증되지 않았습니다.";
                result.metrics.notes.Add(message + " 기준=" + validation.referenceCandidateId + ", 대상=" + validation.targetIdentity);
                foreach (var difference in validation.differences)
                    result.metrics.notes.Add("시작 조건 " + difference.id + ": " + difference.reason + ", 차이="
                        + difference.delta.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", 허용="
                        + difference.tolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                if (result.status != "failed" && result.status != "cancelled" && result.status != "interrupted")
                    result.status = validation.status == "mismatch" ? "conditions-mismatch" : "conditions-unavailable";
            }
            else result.metrics.notes.Add("고정 시작 조건 " + validation.status + ": Arm 직전 실제 위치(0.02m), 플레이어 yaw·카메라 pitch/yaw/roll·FOV(0.1도)와 표적 선택 키를 비교했습니다. 전체 시뮬레이션의 동일성은 검증하지 않습니다.");
        }
        private static void Persist() { if (state != null) SessionState.SetString(StateKey, JsonUtility.ToJson(state)); }
        private static void SaveReport() { if (state != null && report != null) CombatLabReport.Write(state.directory, report); }
        private static void SetPhase(string phase) { state.phase = phase; state.phaseStarted = EditorApplication.timeSinceStartup; Persist(); }

        private static void EnterNext()
        {
            if (state.cancelled || state.candidateIndex >= state.request.candidates.Count) { Finish(); return; }
            if (SourceHash(state.request.sourceGuid) != report.sourceDependencyHash)
            {
                report.notes.Add("원본 설정이 실행 중 변경되어 남은 후보를 중단했습니다."); Finish(); return;
            }
            if (state.request.recordVideo && !EditorApplication.ExecuteMenuItem("Window/General/Game"))
                throw new InvalidOperationException("Game View를 열지 못했습니다. Unity에서 Game View를 연 뒤 다시 실행하세요.");
            state.earlyPreparationApplied = false; state.scenePrepared = false;
            state.preparationError = state.preparationNote = null; state.settledFrames = 0;
            SetPhase("entering");
            EditorApplication.EnterPlaymode();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (state == null || state.phase != "entering") return;
            ApplyEarlyPreparation();
        }

        private static void ApplyEarlyPreparation()
        {
            try
            {
                if (CombatLab.Adapter(state.request.adapterId) is ICombatLabEarlyPlayAdapter early)
                {
                    early.PrepareBeforeSceneStart(state.request);
                    state.earlyPreparationApplied = true;
                }
            }
            catch (Exception error) { state.preparationError = error.Message; }
            Persist();
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (state == null) return;
            if (change == PlayModeStateChange.EnteredPlayMode && state.phase == "entering")
            {
                // Fallback for editor versions that did not deliver sceneLoaded to the
                // restored editor domain. The adapter must reject an already wrong seed.
                if (!state.earlyPreparationApplied) ApplyEarlyPreparation();
                UnityEngine.Random.InitState(state.request.seed);
                Time.timeScale = 1f;
                Application.runInBackground = true;
                SetPhase("warming");
            }
            else if (change == PlayModeStateChange.ExitingPlayMode)
            {
                if (state.phase != "exiting") { state.cancelled = true; EndCandidate("cancelled", "사용자가 Play Mode를 종료했습니다."); }
                Cleanup();
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                RestoreGlobals();
                if (state.phase == "exiting") { Advance(); SetPhase("between"); }
                else { state.cancelled = true; Finish(); }
            }
        }

        private static void Update()
        {
            if (state == null || ending) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if ((state.phase == "entering" || state.phase == "warming") && !string.IsNullOrEmpty(state.preparationError))
                    throw new InvalidOperationException("초기 씬 준비 실패: " + state.preparationError);
                if (EditorApplication.isPaused) { Cancel(); return; }
                if (state.phase == "entering" && now - state.phaseStarted > 120)
                    throw new TimeoutException("Play Mode 진입 시간 초과.");
                if (state.phase == "warming" && now - state.phaseStarted >= 1.5 && !EditorApplication.isCompiling)
                {
                    if (state.cancelled) { EndCandidate("cancelled", "실행 취소"); return; }
                    var candidate = CurrentCandidate;
                    var adapter = CombatLab.Adapter(state.request.adapterId);
                    if (!state.scenePrepared && adapter is ICombatLabScenePreparation preparation)
                    {
                        if (!preparation.PrepareScenario(state.request, out string preparationReason))
                        {
                            if (now - state.phaseStarted < 30) return;
                            throw new TimeoutException("실험 시나리오 준비 시간 초과: " + preparationReason);
                        }
                        state.scenePrepared = true; state.preparationNote = preparationReason; Persist();
                    }
                    if (adapter is ICombatLabReadyAdapter readiness && !readiness.IsReady(state.request, out string reason))
                    {
                        if (now - state.phaseStarted < 30) return;
                        throw new TimeoutException("실험 준비 시간 초과: " + reason);
                    }
                    sessionStarted = Time.realtimeSinceStartupAsDouble;
                    session = adapter.Begin(state.request, candidate);
                    if (!string.IsNullOrEmpty(state.preparationNote)) session.Metrics.notes.Add(state.preparationNote);
                    if (state.request.schemaVersion == 2 && state.request.scenarioMode == "fixed")
                    { state.settledFrames = 0; lastSettlingFrame = Time.frameCount; SetPhase("settling"); }
                    else BeginRecording(now);
                }
                else if (state.phase == "settling")
                {
                    if (session == null) throw new InvalidOperationException("시작 조건 정착 중 스크립트가 다시 로드되었습니다.");
                    if (state.cancelled) { EndCandidate("cancelled", "실행 취소"); return; }
                    if (Time.frameCount == lastSettlingFrame)
                    {
                        if (now - state.phaseStarted >= 10) throw new TimeoutException("고정 시작 조건 정착 중 게임 프레임이 진행되지 않습니다.");
                        return;
                    }
                    lastSettlingFrame = Time.frameCount; state.settledFrames++;
                    bool ready = !(session is ICombatLabReadySession readySession) || readySession.IsReadyToArm(out _);
                    if (state.settledFrames >= 3 && ready) BeginRecording(now);
                    else if (now - state.phaseStarted >= 10)
                    {
                        string reason = "게임 프레임 또는 위치·카메라가 안정되지 않았습니다.";
                        if (session is ICombatLabReadySession pending) pending.IsReadyToArm(out reason);
                        throw new TimeoutException("고정 시작 조건 정착 시간 초과: " + reason);
                    }
                }
                else if (state.phase == "running")
                {
                    if (session == null) throw new InvalidOperationException("실험 중 스크립트가 다시 로드되었습니다. 실행을 중단합니다.");
                    if (recording != null && !string.IsNullOrEmpty(recording.Error))
                        throw new InvalidOperationException("녹화 실패: " + recording.Error);
                    if (Time.frameCount != lastFrame)
                    {
                        session.Tick((float)(now - lastTick)); lastTick = now; lastFrame = Time.frameCount;
                    }
                    while (eventCursor < session.Metrics.events.Count)
                    {
                        var evt = session.Metrics.events[eventCursor++];
                        if (evt.kind == "confirmed-impact" && (string.IsNullOrEmpty(state.request.precisionTargetId)
                            || evt.targetId == state.request.precisionTargetId)) recording?.MarkImpact(evt);
                    }
                    if (state.cancelled) EndCandidate("cancelled", "실행 취소");
                    else if (session.Complete || now - state.phaseStarted >= state.request.durationSeconds)
                    {
                        // Let the session validate its final observation even if the editor's
                        // wall-clock deadline arrives between two rendered game frames.
                        session.Tick(0f);
                        EndCandidate(session.Metrics.hits > 0 ? "completed" : "no-hits", "");
                    }
                    else if (now - lastTick > 15) throw new TimeoutException("게임 프레임이 진행되지 않습니다.");
                }
                else if (state.phase == "between" && !EditorApplication.isPlayingOrWillChangePlaymode && now - state.phaseStarted > 0.5)
                    EnterNext();
                else if (state.phase == "exiting" && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    RestoreGlobals(); Advance(); SetPhase("between");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                state.cancelled = true;
                EndCandidate("failed", e.Message);
            }
        }

        private static void BeginRecording(double now)
        {
            videoTimeOffset = 0f;
            if (state.request.recordVideo)
            {
                recording = new CombatLabRecording();
                string stem = state.request.schemaVersion == 2 ? CurrentCandidate.id + "-repeat-" + (state.repeatIndex + 1) : CurrentCandidate.id;
                recording.Start(Path.Combine(state.directory, stem), state.request.precisionCapture);
            }
            if (session is ICombatLabArmedSession armed)
            { sessionStarted = Time.realtimeSinceStartupAsDouble; armed.Arm(); }
            if (recording != null)
                videoTimeOffset = (float)(sessionStarted - (Time.realtimeSinceStartupAsDouble - recording.TimelineSeconds));
            eventCursor = 0;
            lastTick = now; lastFrame = -1;
            SetPhase("running");
        }

        private static void EndCandidate(string status, string error)
        {
            if (state == null || ending || state.phase == "exiting") return;
            ending = true;
            try
            {
                var result = new CandidateResult { candidate = CurrentCandidate, repeatIndex = state.repeatIndex + 1,
                    metrics = session?.Metrics ?? new CombatMetrics(), status = status, error = error,
                    videoPath = recording?.OutputPath ?? "", videoTimeOffsetSeconds = videoTimeOffset };
                if (result.status == "completed" && !string.IsNullOrEmpty(result.metrics.validationStatus))
                    result.status = result.metrics.validationStatus;
                try { recording?.Stop(); }
                catch (Exception e) { result.status = "failed"; result.error += " 녹화: " + e.Message; state.cancelled = true; }
                if (recording != null)
                {
                    result.capture = recording.Capture;
                    foreach (var warning in recording.Warnings) result.metrics.notes.Add(warning);
                    if (!string.IsNullOrEmpty(recording.Error))
                    { result.status = "failed"; result.error += " 녹화: " + recording.Error; state.cancelled = true; }
                }
                if (result.metrics.hits == 0) result.metrics.notes.Add("명중이 관측되지 않았습니다. 이 실행으로 타격감을 비교하지 마세요.");
                ValidateFixtureConditions(report, result);
                if (state.candidateIndex < 0) report.controlResult = result;
                else report.results.Add(result);
                SaveReport();
            }
            catch (Exception e)
            {
                state.cancelled = true;
                report.notes.Add("결과 파일 저장 중 오류가 발생하여 남은 후보를 중단했습니다: " + e.Message);
                Debug.LogError("[Combat Lab] 결과 저장 실패: " + e.Message);
            }
            finally
            {
                Cleanup(); SetPhase("exiting");
                if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
                ending = false;
            }
        }

        private static void Cleanup()
        {
            try { recording?.Dispose(); } catch (Exception e) { Debug.LogException(e); }
            recording = null;
            try { session?.Dispose(); } catch (Exception e) { Debug.LogException(e); }
            session = null;
            RestoreGlobals();
        }
        private static void RestoreGlobals()
        {
            if (state == null) return;
            Time.timeScale = state.originalTimeScale;
            Application.runInBackground = state.originalRunInBackground;
            Time.captureFramerate = state.originalCaptureFramerate;
            Time.captureDeltaTime = state.originalCaptureDeltaTime;
        }
        public static void Cancel()
        {
            if (state == null) return;
            state.cancelled = true; Persist();
            if (state.phase == "running" || state.phase == "warming" || state.phase == "settling") EndCandidate("cancelled", "실행 취소");
            else if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish();
        }
        private static void Finish()
        {
            try { Cleanup(); SaveReport(); }
            finally { state = null; SessionState.EraseString(StateKey); }
        }
        private static void BeforeReload()
        {
            if (state != null && (state.phase == "running" || state.phase == "settling"))
            {
                state.cancelled = true;
                EndCandidate("interrupted", "실행 중 스크립트 재컴파일");
            }
        }
        private static void OnQuit()
        {
            if (state == null) return;
            state.cancelled = true;
            EndCandidate("interrupted", "Unity 종료");
            Finish();
        }
    }
}
