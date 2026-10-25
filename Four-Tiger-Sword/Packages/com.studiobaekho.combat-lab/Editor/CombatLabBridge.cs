using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace StudioBaekho.CombatLab
{
    /// <summary>
    /// Local desktop-app transport. All Unity work stays on the editor main thread.
    /// The mailbox survives the domain reloads between experiment candidates.
    /// Clients publish requests with a same-directory atomic rename, then retain responses
    /// as receipts so retrying an existing request ID cannot repeat a side effect.
    /// </summary>
    [InitializeOnLoad]
    public static class CombatLabBridge
    {
        private const int ProtocolVersion = 1;
        private const int MaximumRequestBytes = 1024 * 1024;
        private const double PollSeconds = 0.25;
        private static readonly Regex RequestId = new Regex(@"\A[a-f0-9]{32}\z", RegexOptions.CultureInvariant);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly string ProjectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static readonly string RootPath = Path.Combine(ProjectPath, "Library", "CombatLabBridge");
        private static readonly string RequestsPath = Path.Combine(RootPath, "requests");
        private static readonly string ProcessingPath = Path.Combine(RootPath, "processing");
        private static readonly string ResponsesPath = Path.Combine(RootPath, "responses");
        private static readonly Dictionary<string, Response> PendingResponses = new Dictionary<string, Response>();
        private static double nextPoll;
        private static double nextWarning;

        [Serializable] private sealed class Request
        {
            public string id;
            public int protocolVersion;
            public string command;
            public string payloadJson;
            public long expiresUtcMs;
        }
        [Serializable] private sealed class Response
        {
            public string id;
            public bool ok;
            public string payloadJson = "";
            public string error = "";
        }
        [Serializable] private sealed class Status
        {
            public int protocolVersion = ProtocolVersion;
            public string projectPath;
            public string unityVersion;
            public long timestampUtcMs;
            public bool isPlaying;
            public bool isCompiling;
            public bool running;
            public string status;
            public string directory;
        }
        [Serializable] private sealed class Description
        {
            public List<AdapterDescription> adapters = new List<AdapterDescription>();
        }
        [Serializable] private sealed class AdapterDescription
        {
            public string id;
            public string displayName;
            public bool supportsCombinedExport;
            public int[] supportedRequestVersions;
            public string error = "";
            public List<SourceDescription> sources = new List<SourceDescription>();
        }
        [Serializable] private sealed class SourceDescription
        {
            public string guid;
            public string label;
            public List<ParameterSpec> parameters = new List<ParameterSpec>();
            public ExperimentRequest defaultRequest;
            public List<ActionDescription> actions = new List<ActionDescription>();
            public string error = "";
        }
        [Serializable] private sealed class ActionDescription
        {
            public string id;
            public string label;
            public bool available;
            public string unavailableReason = "";
            public List<ParameterSpec> parameters = new List<ParameterSpec>();
            public List<CurveSpec> curves = new List<CurveSpec>();
            public List<ParameterSpec> legacyParameters = new List<ParameterSpec>();
            public int[] supportedRequestVersions = new[] { 1 };
            public string context;
            public ExperimentRequest defaultRequest;
        }
        [Serializable] private sealed class StartResult { public string directory; }
        [Serializable] private sealed class CancelResult { public bool cancelled; }
        [Serializable] private sealed class ExportRequest
        {
            public ExperimentRequest request;
            public string candidateId;
            public string assetPath;
        }
        [Serializable] private sealed class ExportResult { public string assetPath; public string guid; }
        [Serializable] private sealed class CombinedExportPayload
        {
            public CombinedExportRequest request;
            public string assetPath;
        }

        static CombatLabBridge()
        {
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + PollSeconds;
            try
            {
                Directory.CreateDirectory(RequestsPath);
                Directory.CreateDirectory(ProcessingPath);
                Directory.CreateDirectory(ResponsesPath);
                // Recover claimed requests first. An interrupted command is never replayed:
                // its side effect may already have occurred immediately before a reload.
                RecoverProcessing();
                ProcessRequests();
                WriteStatus();
            }
            catch (Exception error)
            {
                if (EditorApplication.timeSinceStartup >= nextWarning)
                {
                    nextWarning = EditorApplication.timeSinceStartup + 15;
                    Debug.LogWarning("[Combat Lab Bridge] " + error.Message);
                }
            }
        }

        private static IEnumerable<string> MailboxFiles(string path)
        {
            // Bound work per editor tick, including malformed files from a broken client.
            return Directory.EnumerateFiles(path, "*.json").Take(256)
                .Where(file => RequestId.IsMatch(Path.GetFileNameWithoutExtension(file))).Take(8).ToArray();
        }

        private static void RecoverProcessing()
        {
            foreach (string file in MailboxFiles(ProcessingPath))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (File.Exists(ResponsePath(id))) { FinishClaim(file, id); continue; }
                if (!PendingResponses.TryGetValue(id, out var response))
                    response = new Response { id = id, error = "Unity가 요청 처리 도중 다시 로드되었습니다. 작업이 이미 실행되었을 수 있으므로 현재 실행 상태와 저장 결과를 확인하세요." };
                AtomicWrite(ResponsePath(id), JsonUtility.ToJson(response));
                FinishClaim(file, id);
            }
        }

        private static void ProcessRequests()
        {
            foreach (string file in MailboxFiles(RequestsPath))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                // A response is also the idempotency receipt; never overwrite it on a retry.
                if (File.Exists(ResponsePath(id))) { File.Delete(file); continue; }
                string claimed = Path.Combine(ProcessingPath, id + ".json");
                if (File.Exists(claimed)) continue;
                try { File.Move(file, claimed); }
                catch (IOException) { continue; }
                var response = new Response { id = id };
                try
                {
                    var request = JsonUtility.FromJson<Request>(ReadRequest(claimed));
                    if (request == null || request.id != id || !RequestId.IsMatch(request.id))
                        throw new ArgumentException("요청 ID는 파일 이름과 같은 32자리 소문자 GUID여야 합니다.");
                    if (request.protocolVersion != ProtocolVersion)
                        throw new ArgumentException("지원하지 않는 Combat Lab 연결 프로토콜입니다.");
                    if (request.expiresUtcMs > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > request.expiresUtcMs)
                        throw new TimeoutException("요청의 유효 시간이 지났습니다. 현재 상태를 확인한 뒤 다시 요청하세요.");
                    response.payloadJson = Execute(request);
                    response.ok = true;
                }
                catch (Exception error) { response.error = error.Message; }
                // EnterPlaymode schedules the transition. Publish the acknowledgement in
                // this same callback, before Unity gets control and performs domain reload.
                PendingResponses[id] = response;
                AtomicWrite(ResponsePath(id), JsonUtility.ToJson(response));
                FinishClaim(claimed, id);
                // Do not accept another command during the transition requested by Start.
                if (EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying) break;
            }
        }

        private static void FinishClaim(string path, string id)
        {
            File.Delete(path);
            PendingResponses.Remove(id);
        }

        private static string ResponsePath(string id) => Path.Combine(ResponsesPath, id + ".json");

        private static string Execute(Request request)
        {
            switch (request.command)
            {
                case "describe": return Describe();
                case "start":
                    RequirePayload(request);
                    return JsonUtility.ToJson(new StartResult { directory = CombatLab.StartJson(request.payloadJson) });
                case "cancel":
                    CombatLab.Cancel();
                    return JsonUtility.ToJson(new CancelResult { cancelled = true });
                case "export":
                    RequirePayload(request);
                    if (EditorApplication.isCompiling) throw new InvalidOperationException("Unity 컴파일을 마친 뒤 저장하세요.");
                    var export = JsonUtility.FromJson<ExportRequest>(request.payloadJson);
                    if (export == null || export.request == null || string.IsNullOrWhiteSpace(export.assetPath) ||
                        string.IsNullOrWhiteSpace(export.candidateId))
                        throw new ArgumentException("프리셋 저장 요청에 실험 설정, 후보 ID, 새 에셋 경로가 필요합니다.");
                    var asset = CombatLab.Export(export.request, export.candidateId, export.assetPath);
                    string assetPath = AssetDatabase.GetAssetPath(asset);
                    return JsonUtility.ToJson(new ExportResult { assetPath = assetPath, guid = AssetDatabase.AssetPathToGUID(assetPath) });
                case "export-combined":
                    RequirePayload(request);
                    var combined = JsonUtility.FromJson<CombinedExportPayload>(request.payloadJson);
                    if (combined == null || combined.request == null || string.IsNullOrWhiteSpace(combined.assetPath))
                        throw new ArgumentException("통합 저장 요청에 동작별 후보와 새 에셋 경로가 필요합니다.");
                    var combinedAsset = CombatLab.ExportCombined(combined.request, combined.assetPath);
                    string combinedPath = AssetDatabase.GetAssetPath(combinedAsset);
                    return JsonUtility.ToJson(new ExportResult { assetPath = combinedPath, guid = AssetDatabase.AssetPathToGUID(combinedPath) });
                default: throw new ArgumentException("지원하지 않는 명령입니다: " + request.command);
            }
        }

        private static void RequirePayload(Request request)
        {
            if (string.IsNullOrWhiteSpace(request.payloadJson)) throw new ArgumentException("요청 데이터가 비어 있습니다.");
        }

        private static string Describe()
        {
            var description = new Description();
            foreach (var adapter in CombatLab.Adapters())
            {
                var entry = new AdapterDescription { id = adapter.Id, displayName = adapter.DisplayName,
                    supportsCombinedExport = adapter is ICombatLabMultiActionAdapter,
                    supportedRequestVersions = adapter is ICombatLabFeedbackAdapter ? new[] { 1, 2 } : new[] { 1 } };
                description.adapters.Add(entry);
                try
                {
                    foreach (var source in adapter.DiscoverSources())
                    {
                        var sourceEntry = new SourceDescription { guid = source.guid, label = source.label };
                        entry.sources.Add(sourceEntry);
                        if (adapter is ICombatLabActionAdapter actions)
                        {
                            try
                            {
                                foreach (var action in actions.DiscoverActions(source.guid))
                                {
                                    var actionEntry = new ActionDescription { id = action.id, label = action.label,
                                        available = action.available, unavailableReason = action.unavailableReason };
                                    sourceEntry.actions.Add(actionEntry);
                                    if (!action.available) continue;
                                    try
                                    {
                                        if (adapter is ICombatLabFeedbackAdapter feedback)
                                        {
                                            actionEntry.parameters = feedback.DescribeFeedback(source.guid, action.id);
                                            actionEntry.legacyParameters = actions.Describe(source.guid, action.id);
                                            actionEntry.curves = feedback.DescribeCurves(source.guid, action.id);
                                            actionEntry.context = feedback.DescribeContext(source.guid, action.id);
                                            actionEntry.supportedRequestVersions = new[] { 1, 2 };
                                            actionEntry.defaultRequest = CombatLab.CreateFeedbackRequest(adapter.Id, source.guid, action.id);
                                        }
                                        else
                                        {
                                            actionEntry.parameters = actions.Describe(source.guid, action.id);
                                            actionEntry.defaultRequest = CombatLab.CreateComparison(adapter.Id, source.guid, action.id);
                                        }
                                    }
                                    catch (Exception error)
                                    { actionEntry.available = false; actionEntry.unavailableReason = error.Message; }
                                }
                            }
                            catch (Exception error) { sourceEntry.error = error.Message; }
                        }
                        try
                        {
                            sourceEntry.parameters = adapter.Describe(source.guid);
                            sourceEntry.defaultRequest = CombatLab.CreateComparison(adapter.Id, source.guid);
                        }
                        catch (Exception error)
                        {
                            var fallback = sourceEntry.actions.FirstOrDefault(action => action.available);
                            if (fallback == null) sourceEntry.error = error.Message;
                            else
                            {
                                sourceEntry.parameters = fallback.parameters;
                                sourceEntry.defaultRequest = fallback.defaultRequest;
                            }
                        }
                    }
                }
                catch (Exception error) { entry.error = error.Message; }
            }
            return JsonUtility.ToJson(description);
        }

        private static void WriteStatus()
        {
            var runner = JsonUtility.FromJson<Status>(CombatLab.StatusJson());
            AtomicWrite(Path.Combine(RootPath, "status.json"), JsonUtility.ToJson(new Status {
                projectPath = ProjectPath, unityVersion = Application.unityVersion,
                timestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                isPlaying = EditorApplication.isPlaying, isCompiling = EditorApplication.isCompiling,
                running = runner.running, status = runner.status, directory = runner.directory
            }));
        }

        private static string ReadRequest(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaximumRequestBytes) throw new ArgumentException("요청 파일은 1MB 이하여야 합니다. 타격·동작 대상을 나누어 생성하세요. 항목은 자동 생략되지 않습니다.");
                var bytes = new byte[(int)stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int count = stream.Read(bytes, read, bytes.Length - read);
                    if (count == 0) throw new IOException("요청 파일을 끝까지 읽지 못했습니다.");
                    read += count;
                }
                if (stream.ReadByte() != -1) throw new IOException("읽는 도중 요청 파일이 변경되었습니다.");
                int offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
                return Utf8.GetString(bytes, offset, bytes.Length - offset);
            }
        }

        private static void AtomicWrite(string path, string json)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, Utf8);
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(path)) File.Replace(temporary, path, null);
                        else File.Move(temporary, path);
                        return;
                    }
                    catch (IOException) when (attempt < 2) { }
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
