using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace StudioBaekho.CombatLab
{
    public static class CombatLab
    {
        public static List<ICombatLabAdapter> Adapters() => TypeCache.GetTypesDerivedFrom<ICombatLabAdapter>()
            .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (ICombatLabAdapter)Activator.CreateInstance(t)).OrderBy(a => a.Id).ToList();

        public static ICombatLabAdapter Adapter(string id) => Adapters().FirstOrDefault(a => a.Id == id)
            ?? throw new InvalidOperationException("프로젝트 어댑터를 찾을 수 없습니다: " + id);

        public static List<ParameterSpec> Describe(ICombatLabAdapter adapter, string sourceGuid, string actionId)
        {
            if (adapter is ICombatLabActionAdapter actions) return actions.Describe(sourceGuid, actionId);
            if (!string.IsNullOrEmpty(actionId) && actionId != "normal-combo")
                throw new ArgumentException("이 어댑터는 동작 선택을 지원하지 않습니다.");
            return adapter.Describe(sourceGuid);
        }

        public static ExperimentRequest CreateComparison(string adapterId, string sourceGuid, string actionId = null)
        {
            var specs = Describe(Adapter(adapterId), sourceGuid, actionId);
            foreach (var spec in specs)
                if (!Finite(spec.baseline) || spec.baseline < spec.minimum || spec.baseline > spec.maximum)
                    throw new InvalidOperationException($"현재 {spec.label} 값({spec.baseline})이 실험 범위를 벗어납니다. 기준값을 유지하도록 어댑터 범위를 먼저 검토하세요.");
            var request = new ExperimentRequest { adapterId = adapterId, sourceGuid = sourceGuid, actionId = actionId };
            var ids = new[] { "baseline", "soft", "strong" };
            var labels = new[] { "기준", "약한 반응", "강한 반응" };
            var scales = new[] { 1f, 0.7f, 1.3f };
            for (int i = 0; i < ids.Length; i++)
                request.candidates.Add(new CombatCandidate {
                    id = ids[i], label = labels[i], values = specs.Select(s =>
                        new ParameterValue(s.id, Mathf.Clamp(s.baseline * scales[i], s.minimum, s.maximum))).ToList()
                });
            return request;
        }

        public static ExperimentRequest CreateFeedbackRequest(string adapterId, string sourceGuid, string actionId)
        {
            if (!(Adapter(adapterId) is ICombatLabFeedbackAdapter))
                throw new InvalidOperationException("이 어댑터는 연출 v2를 지원하지 않습니다.");
            return new ExperimentRequest { schemaVersion = 2, adapterId = adapterId, sourceGuid = sourceGuid,
                actionId = actionId, includeSourceControl = true, scenarioMode = "fixed", precisionCapture = true,
                sourceHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GUIDToAssetPath(sourceGuid)).ToString() };
        }

        public static void Validate(ExperimentRequest request)
        {
            ValidateData(request);
            Adapter(request.adapterId).Validate(request);
        }

        public static void ValidateData(ExperimentRequest request)
        {
            if (request == null || (request.schemaVersion != 1 && request.schemaVersion != 2)) throw new ArgumentException("지원하지 않는 실험 형식입니다.");
            if (!Finite(request.durationSeconds) || request.durationSeconds < 3 || request.durationSeconds > 60)
                throw new ArgumentException("실험 길이는 후보당 3~60초입니다.");
            if (request.candidates == null || request.candidates.Count < 1 || request.candidates.Count > 12)
                throw new ArgumentException("후보는 1~12개여야 합니다.");
            var adapter = Adapter(request.adapterId);
            if (request.schemaVersion == 2)
            {
                ValidateFeedbackData(request, adapter);
                return;
            }
            var specs = Describe(adapter, request.sourceGuid, request.actionId);
            if (specs == null || specs.Count == 0 || specs.Select(s => s.id).Distinct().Count() != specs.Count)
                throw new ArgumentException("어댑터의 조절 항목 정의가 유효하지 않습니다.");
            var ids = new HashSet<string>();
            foreach (var candidate in request.candidates)
            {
                if (candidate == null || candidate.id == null || !Regex.IsMatch(candidate.id, @"\A[a-zA-Z0-9_-]{1,64}\z") || !ids.Add(candidate.id))
                    throw new ArgumentException("후보 ID는 중복 없는 영문/숫자/밑줄/하이픈이어야 합니다.");
                if (candidate.values == null || candidate.values.Count != specs.Count || candidate.values.Any(v => v == null))
                    throw new ArgumentException("각 후보에 모든 조절 항목을 정확히 한 번 지정하세요.");
                foreach (var spec in specs)
                {
                    var matches = candidate.values.Where(v => v.id == spec.id).ToArray();
                    if (matches.Length != 1 || !Finite(matches[0].value) || matches[0].value < spec.minimum || matches[0].value > spec.maximum)
                        throw new ArgumentException($"{candidate.label}: {spec.label} 허용 범위는 {spec.minimum}~{spec.maximum} {spec.unit}입니다.");
                }
            }
        }

        public static void ValidateFeedbackData(ExperimentRequest request, ICombatLabAdapter adapter)
        {
            if (!(adapter is ICombatLabFeedbackAdapter feedback))
                throw new ArgumentException("현재 연결은 연출 v2를 지원하지 않습니다. 연결 패키지와 어댑터를 업데이트하세요.");
            if (request.scenarioMode != "fixed" && request.scenarioMode != "live") throw new ArgumentException("지원하지 않는 실험 모드입니다.");
            if (request.repeatCount != 1 && request.repeatCount != 3) throw new ArgumentException("반복은 1회 또는 3회입니다.");
            if (!string.IsNullOrEmpty(request.sourceHash) && request.sourceHash != AssetDatabase.GetAssetDependencyHash(
                AssetDatabase.GUIDToAssetPath(request.sourceGuid)).ToString())
                throw new ArgumentException("원본이 변경되었습니다. 설정을 다시 불러오고 새 후보를 생성하세요.");
            var specs = feedback.DescribeFeedback(request.sourceGuid, request.actionId).ToDictionary(s => s.id);
            var curves = feedback.DescribeCurves(request.sourceGuid, request.actionId).ToDictionary(s => s.id);
            var ids = new HashSet<string>();
            foreach (var candidate in request.candidates)
            {
                if (candidate == null || candidate.isSourceControl || string.IsNullOrWhiteSpace(candidate.id)
                    || !Regex.IsMatch(candidate.id, @"\A[a-zA-Z0-9_-]{1,64}\z") || !ids.Add(candidate.id))
                    throw new ArgumentException("유효한 고유 후보 ID가 필요합니다. 원본 비교는 별도 실행됩니다.");
                var seen = new HashSet<string>();
                if (candidate.values == null) throw new ArgumentException("후보 값이 없습니다.");
                foreach (var value in candidate.values)
                {
                    if (value == null || !seen.Add(value.id) || !specs.TryGetValue(value.id, out var spec)
                        || !Finite(value.value) || value.value < spec.minimum || value.value > spec.maximum)
                        throw new ArgumentException("지원하지 않거나 유효 범위를 벗어난 값: " + value?.id);
                    if ((spec.kind == "enum" || spec.kind == "boolean") && value.value != Mathf.Floor(value.value))
                        throw new ArgumentException("선택 항목에는 정수 값을 사용하세요: " + value.id);
                }
                seen.Clear();
                foreach (var value in candidate.curves ?? new List<CurveValue>())
                {
                    if (value == null || !seen.Add(value.id) || !curves.TryGetValue(value.id, out var spec)
                        || value.keys == null || value.keys.Count < 2 || value.keys.Count > spec.maximumKeys)
                        throw new ArgumentException("유효한 곡선과 2~16개 키가 필요합니다: " + value?.id);
                    float previous = -1f;
                    foreach (var key in value.keys)
                    {
                        if (key == null || !Finite(key.time) || !Finite(key.value) || key.time <= previous || key.time < 0
                            || key.time > spec.maximumTime || key.value < spec.minimum || key.value > spec.maximum)
                            throw new ArgumentException("곡선 키는 유효한 범위에서 시간 오름차순이어야 합니다: " + value.id);
                        previous = key.time;
                    }
                }
            }
        }

        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static string StartJson(string json) => CombatLabRunner.Start(JsonUtility.FromJson<ExperimentRequest>(json));
        public static string StatusJson() => CombatLabRunner.StatusJson();
        public static void Cancel() => CombatLabRunner.Cancel();

        public static UnityEngine.Object Export(ExperimentRequest request, string candidateId, string assetPath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || CombatLabRunner.IsRunning)
                throw new InvalidOperationException("실험을 마친 뒤 편집 모드에서 저장하세요.");
            ValidateData(request);
            var candidate = request.candidates.Single(c => c.id == candidateId);
            string normalized = assetPath.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) || normalized.Contains("..") ||
                !normalized.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || File.Exists(normalized))
                throw new ArgumentException("Assets 안의 새로운 .asset 경로를 선택하세요.");
            return Adapter(request.adapterId).Export(request, candidate, normalized);
        }

        public static void ValidateCombinedData(CombinedExportRequest request)
        {
            if (request == null || request.schemaVersion != 1)
                throw new ArgumentException("지원하지 않는 통합 저장 형식입니다.");
            if (string.IsNullOrWhiteSpace(request.adapterId) || string.IsNullOrWhiteSpace(request.sourceGuid))
                throw new ArgumentException("통합 저장할 어댑터와 원본 폼을 선택하세요.");
            if (request.selections == null || request.selections.Count < 1 || request.selections.Count > 32)
                throw new ArgumentException("통합 저장할 동작은 1~32개여야 합니다. 각 동작의 후보를 하나씩 선택하세요.");
            if (!(Adapter(request.adapterId) is ICombatLabMultiActionAdapter))
                throw new InvalidOperationException("이 프로젝트 어댑터는 여러 동작의 통합 저장을 지원하지 않습니다.");
            var actions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var selection in request.selections)
            {
                var experiment = selection?.request;
                if (experiment == null || experiment.adapterId != request.adapterId || experiment.sourceGuid != request.sourceGuid)
                    throw new ArgumentException("모든 동작은 같은 프로젝트 어댑터와 원본 폼을 사용해야 합니다.");
                ValidateData(experiment);
                string actionId = string.IsNullOrEmpty(experiment.actionId) ? "normal-combo" : experiment.actionId;
                if (!actions.Add(actionId)) throw new ArgumentException("각 동작은 한 번만 저장할 수 있습니다: " + actionId);
                if (string.IsNullOrWhiteSpace(selection.candidateId) || !experiment.candidates.Any(candidate => candidate.id == selection.candidateId))
                    throw new ArgumentException("선택 동작의 후보를 찾을 수 없습니다: " + actionId);
            }
        }

        public static UnityEngine.Object ExportCombined(CombinedExportRequest request, string assetPath)
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || CombatLabRunner.IsRunning)
                throw new InvalidOperationException("컴파일과 실험을 마친 뒤 편집 모드에서 저장하세요.");
            ValidateCombinedData(request);
            if (string.IsNullOrWhiteSpace(assetPath)) throw new ArgumentException("Assets 안의 새로운 .asset 경로를 선택하세요.");
            string normalized = assetPath.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) || normalized.Contains("..") ||
                !normalized.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || File.Exists(normalized))
                throw new ArgumentException("Assets 안의 새로운 .asset 경로를 선택하세요.");
            return ((ICombatLabMultiActionAdapter)Adapter(request.adapterId)).ExportCombined(request, normalized);
        }
    }
}
