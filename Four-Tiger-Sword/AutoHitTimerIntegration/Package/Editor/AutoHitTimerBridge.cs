using System;
using System.IO;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace StudioBaekho.AutoHitTimer
{
    [InitializeOnLoad]
    public static class AutoHitTimerBridge
    {
        public const int ProtocolVersion = 1;
        public const int MaximumMessageBytes = 1024 * 1024;
        public static string ProjectPath => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string RootPath => Path.Combine(ProjectPath, "Library", "AutoHitTimerBridge");
        static string Requests => Path.Combine(RootPath, "requests");
        static string Processing => Path.Combine(RootPath, "processing");
        static string Responses => Path.Combine(RootPath, "responses");
        static string Receipts => Path.Combine(RootPath, "receipts");
        static AutoHitTimerCapture capture;
        static string captureRequestId;
        static long captureExpires;
        static double nextPoll;
        static double nextHeartbeat;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

        static AutoHitTimerBridge()
        {
            EnsureDirectories();
            RecoverInterruptedRequests();
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += () => AbortCapture("EDITOR_RELOAD", "에디터 컴파일로 촬영이 중단되었습니다. 다시 실행해 주세요.");
            EditorApplication.quitting += () => AbortCapture("EDITOR_CLOSED", "Unity가 종료되어 촬영이 중단되었습니다.");
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) AbortCapture("PLAY_MODE", "Play Mode 전환으로 촬영이 중단되었습니다."); };
        }

        static void EnsureDirectories()
        {
            foreach (var directory in new[] { Requests, Processing, Responses, Receipts }) Directory.CreateDirectory(directory);
        }

        static void Update()
        {
            try
            {
                if (EditorApplication.timeSinceStartup >= nextHeartbeat)
                {
                    nextHeartbeat = EditorApplication.timeSinceStartup + 1;
                    var status = Status();
                    status["timestampUtcMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    WriteAtomic(Path.Combine(RootPath, "status.json"), status.ToString(Formatting.None));
                }
                if (capture != null)
                {
                    if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isUpdating) AbortCapture("EDITOR_BUSY", "에디터 상태 변경으로 촬영이 중단되었습니다.");
                    else if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > captureExpires) AbortCapture("EXPIRED", "촬영 요청 시간이 만료되었습니다.");
                    else
                    {
                        try
                        {
                            if (capture.Step(out var result))
                            {
                                var id = captureRequestId;
                                capture.Dispose(); capture = null; captureRequestId = null;
                                Complete(id, Success(id, result));
                            }
                        }
                        catch (Exception exception) { AbortCapture(ErrorCode(exception), exception.Message); }
                    }
                }
                if (EditorApplication.timeSinceStartup < nextPoll) return;
                nextPoll = EditorApplication.timeSinceStartup + 0.1;
                foreach (var path in Directory.GetFiles(Requests, "*.json").OrderBy(File.GetCreationTimeUtc).Take(10)) Process(path);
            }
            catch (Exception exception) { Debug.LogError("[AutoHitTimer] Bridge: " + exception.Message); }
        }

        public static JObject Status() => new JObject
        {
            ["ready"] = !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode,
            ["unityVersion"] = Application.unityVersion,
            ["projectPath"] = ProjectPath,
            ["protocolVersion"] = ProtocolVersion,
            ["capturing"] = capture != null,
            ["message"] = EditorApplication.isPlayingOrWillChangePlaymode ? "Play Mode를 종료해 주세요." : EditorApplication.isCompiling || EditorApplication.isUpdating ? "Unity가 컴파일 또는 임포트 중입니다." : "Unity 연결 준비 완료"
        };

        static void Process(string requestPath)
        {
            var id = Path.GetFileNameWithoutExtension(requestPath);
            if (!Guid.TryParseExact(id, "D", out _)) { File.Delete(requestPath); return; }
            var claimed = Path.Combine(Processing, id + ".json");
            var receipt = Path.Combine(Receipts, id + ".json");
            try
            {
                // Moving within one volume is an atomic claim. A completed receipt is the source of truth.
                if (File.Exists(receipt))
                {
                    var previous = JObject.Parse(File.ReadAllText(receipt));
                    if ((string)previous["state"] == "completed") WriteAtomic(Path.Combine(Responses, id + ".json"), previous["response"].ToString(Formatting.None));
                    File.Delete(requestPath);
                    return;
                }
                if (File.Exists(claimed)) return;
                File.Move(requestPath, claimed);
                if (new FileInfo(claimed).Length > MaximumMessageBytes) throw new AutoHitTimerException("MESSAGE_TOO_LARGE", "요청은 1 MiB 이하여야 합니다.");
                var envelope = JObject.Parse(File.ReadAllText(claimed));
                ValidateEnvelope(envelope, id);
                var command = (string)envelope["command"];
                var payload = JObject.Parse((string)envelope["payloadJson"]);
                var expires = (long)envelope["expiresUtcMs"];
                if (expires < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) throw new AutoHitTimerException("EXPIRED", "요청 시간이 만료되었습니다.");
                // Persist intent BEFORE dispatch. An interrupted mutation is never automatically replayed.
                WriteAtomic(receipt, new JObject { ["state"] = "started", ["command"] = command, ["startedUtcMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }.ToString(Formatting.None));
                if (command == "status") { Complete(id, Success(id, Status())); return; }
                if (command == "saveStatus") { Complete(id, Success(id, AutoHitTimerAdapters.Resolve((string)payload["adapterId"]).SaveStatus(payload))); return; }
                if (command == "cancel")
                {
                    var target = (string)payload["requestId"] ?? (string)payload["targetRequestId"];
                    var session = (string)payload["sessionId"];
                    var cancelled = capture != null && (string.IsNullOrEmpty(target) || target == captureRequestId) && (string.IsNullOrEmpty(session) || session == capture.SessionId);
                    if (cancelled) AbortCapture("CANCELLED", "사용자가 촬영을 취소했습니다.");
                    Complete(id, Success(id, new JObject { ["cancelled"] = cancelled }));
                    return;
                }
                if (!(bool)Status()["ready"]) throw new AutoHitTimerException("EDITOR_BUSY", "Unity가 컴파일·임포트 중이거나 Play Mode입니다.");
                if (capture != null) throw new AutoHitTimerException("CAPTURE_BUSY", "촬영이 진행 중입니다. 완료 후 다시 실행해 주세요.");
                if (command == "capture")
                {
                    capture = new AutoHitTimerCapture(payload);
                    captureRequestId = id;
                    captureExpires = expires;
                    return;
                }
                JObject result;
                switch (command)
                {
                    case "actors": result = AutoHitTimerScene.Actors(); break;
                    case "search": result = AutoHitTimerAssets.Search(payload); break;
                    case "import": result = AutoHitTimerAssets.Import(payload); break;
                    case "targets": result = AutoHitTimerAdapters.Resolve((string)payload["adapterId"]).Targets((string)payload["actorId"]); break;
                    case "previewChange": result = AutoHitTimerAdapters.Resolve((string)payload["adapterId"]).PreviewChange(payload); break;
                    case "apply": result = AutoHitTimerAdapters.Resolve((string)payload["adapterId"]).Apply(payload); break;
                    default: throw new AutoHitTimerException("UNKNOWN_COMMAND", "알 수 없는 명령입니다: " + command);
                }
                Complete(id, Success(id, result));
            }
            catch (IOException exception)
            {
                if (File.Exists(claimed)) Complete(id, Failure(id, "IO_ERROR", exception.Message));
            }
            catch (Exception exception) { Complete(id, Failure(id, ErrorCode(exception), exception.Message)); }
        }

        public static void ValidateEnvelope(JObject envelope, string id)
        {
            if ((int?)envelope["protocolVersion"] != ProtocolVersion) throw new AutoHitTimerException("PROTOCOL_MISMATCH", "지원하지 않는 프로토콜 버전입니다.");
            if ((string)envelope["id"] != id || !Guid.TryParseExact(id, "D", out _)) throw new AutoHitTimerException("INVALID_ID", "요청 ID와 파일명이 일치해야 합니다.");
            if (envelope["command"]?.Type != JTokenType.String || envelope["payloadJson"]?.Type != JTokenType.String || envelope["expiresUtcMs"]?.Type != JTokenType.Integer)
                throw new AutoHitTimerException("INVALID_REQUEST", "command, payloadJson, expiresUtcMs가 필요합니다.");
        }

        static void AbortCapture(string code, string message)
        {
            if (capture == null) return;
            var id = captureRequestId;
            try { capture.Dispose(); }
            finally { capture = null; captureRequestId = null; }
            Complete(id, Failure(id, code, message));
        }

        static void RecoverInterruptedRequests()
        {
            foreach (var path in Directory.GetFiles(Receipts, "*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                if (!Guid.TryParseExact(id, "D", out _)) continue;
                try
                {
                    var receipt = JObject.Parse(File.ReadAllText(path));
                    if ((string)receipt["state"] == "started") Complete(id, Failure(id, "INTERRUPTED", "이전 요청이 중단되었습니다. 저장 요청이었다면 에셋과 변경 전 스냅샷을 확인한 뒤 새 요청으로 다시 진행해 주세요."));
                    else if ((string)receipt["state"] == "completed" && File.Exists(Path.Combine(Processing, id + ".json")))
                    {
                        WriteAtomic(Path.Combine(Responses, id + ".json"), receipt["response"].ToString(Formatting.None));
                        File.Delete(Path.Combine(Processing, id + ".json"));
                    }
                }
                catch (Exception exception) { Debug.LogError("[AutoHitTimer] Receipt recovery: " + exception.Message); }
            }
            foreach (var path in Directory.GetFiles(Processing, "*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                if (Guid.TryParseExact(id, "D", out _) && !File.Exists(Path.Combine(Receipts, id + ".json"))) Complete(id, Failure(id, "INTERRUPTED", "이전 요청이 실행 전에 중단되었습니다. 다시 실행해 주세요."));
            }
        }

        static JObject Success(string id, JObject result) => new JObject { ["protocolVersion"] = ProtocolVersion, ["id"] = id, ["ok"] = true, ["resultJson"] = (result ?? new JObject()).ToString(Formatting.None), ["error"] = null };
        static JObject Failure(string id, string code, string message) => new JObject { ["protocolVersion"] = ProtocolVersion, ["id"] = id, ["ok"] = false, ["resultJson"] = "{}", ["error"] = new JObject { ["code"] = code, ["message"] = (message ?? "오류").Substring(0, Math.Min(message?.Length ?? 2, 2000)) } };
        static string ErrorCode(Exception exception) => exception is AutoHitTimerException known ? known.Code : "UNITY_ERROR";

        static void Complete(string id, JObject response)
        {
            var json = response.ToString(Formatting.None);
            if (Encoding.UTF8.GetByteCount(json) > MaximumMessageBytes)
            {
                response = Failure(id, "MESSAGE_TOO_LARGE", "응답이 1 MiB를 초과했습니다. 검색 조건 또는 촬영 구간을 줄여 주세요.");
                json = response.ToString(Formatting.None);
            }
            WriteAtomic(Path.Combine(Receipts, id + ".json"), new JObject { ["state"] = "completed", ["response"] = response }.ToString(Formatting.None));
            WriteAtomic(Path.Combine(Responses, id + ".json"), json);
            var claim = Path.Combine(Processing, id + ".json");
            if (File.Exists(claim)) File.Delete(claim);
        }

        internal static void WriteAtomic(string path, string text)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (Application.platform == RuntimePlatform.WindowsEditor)
                        {
                            // Avoid File.Replace's Windows metadata merge; preserve same-volume atomic replacement.
                            if (!MoveFileEx(temporary, path, 0x1 | 0x8)) throw new IOException("원자적 파일 쓰기 실패: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
                        }
                        else if (File.Exists(path)) File.Replace(temporary, path, null);
                        else File.Move(temporary, path);
                        break;
                    }
                    catch (IOException) when (attempt < 3) { Thread.Sleep(15 * (attempt + 1)); }
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
