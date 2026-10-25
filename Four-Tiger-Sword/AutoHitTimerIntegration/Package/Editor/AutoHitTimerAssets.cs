using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace StudioBaekho.AutoHitTimer
{
    public static class AutoHitTimerAssets
    {
        public static AnimationClip ResolveClip(JObject reference)
        {
            var guid = (string)reference?["guid"];
            if (string.IsNullOrEmpty(guid) || !long.TryParse((string)reference?["localId"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                throw new AutoHitTimerException("INVALID_CLIP", "클립 GUID 및 하위 에셋 ID가 필요합니다.");
            var path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string actualGuid, out long actualId) && actualGuid == guid && actualId == id) return clip;
            throw new AutoHitTimerException("CLIP_MISSING", "클립을 찾을 수 없습니다. 다시 검색해 주세요.");
        }

        public static JObject ClipRef(AnimationClip clip)
        {
            if (!clip || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long id))
                throw new AutoHitTimerException("INVALID_CLIP", "저장된 AnimationClip 에셋이 필요합니다.");
            var path = AssetDatabase.GetAssetPath(clip);
            return new JObject { ["guid"] = guid, ["localId"] = id.ToString(CultureInfo.InvariantCulture), ["path"] = path, ["name"] = clip.name, ["length"] = clip.length, ["frameRate"] = clip.frameRate, ["sourceFingerprint"] = AssetDatabase.GetAssetDependencyHash(path).ToString() };
        }

        public static JObject Search(JObject request)
        {
            var query = ((string)request?["query"] ?? "").Trim();
            var offset = PageArgument(request, "offset", 0, 0, int.MaxValue);
            var limit = PageArgument(request, "limit", 1000, 1, 1000);
            Animator actor = string.IsNullOrEmpty((string)request?["actorId"]) ? null : AutoHitTimerScene.ResolveAnimator((string)request["actorId"]);
            var clips = new JArray();
            // One extra candidate distinguishes a full final page from a truncated page. Expensive
            // dependency hashes and rig checks are only evaluated for the rows being returned.
            var page = SearchCandidates(actor, query).Skip(offset).Take(limit + 1).ToArray();
            foreach (var candidate in page.Take(limit))
            {
                var row = Describe(candidate.clip, actor);
                if (candidate.state)
                {
                    row["stateName"] = candidate.stateName;
                    var reason = StateReason(candidate.state, candidate.layer);
                    if (reason != null) { row["compatible"] = false; row["reason"] = reason; }
                }
                clips.Add(row);
            }
            var truncated = page.Length > limit;
            return new JObject { ["clips"] = clips, ["truncated"] = truncated, ["nextOffset"] = truncated ? new JValue((long)offset + clips.Count) : JValue.CreateNull() };
        }

        sealed class SearchCandidate
        {
            public AnimationClip clip;
            public AnimatorState state;
            public string stateName;
            public int layer;
            public string identity;
            public long localId;
        }

        static int PageArgument(JObject request, string name, int fallback, int minimum, int maximum)
        {
            var value = request?[name];
            if (value == null) return fallback;
            if (value.Type != JTokenType.Integer || !long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < minimum || number > maximum)
                throw new AutoHitTimerException("INVALID_SEARCH", name + " 값은 " + minimum + "부터 " + maximum + " 사이의 정수여야 합니다.");
            return (int)number;
        }

        static SearchCandidate Candidate(AnimationClip clip)
        {
            if (!clip || clip.name.StartsWith("__preview__", StringComparison.Ordinal) || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long id)) return null;
            return new SearchCandidate { clip = clip, identity = guid + ":" + id.ToString(CultureInfo.InvariantCulture), localId = id };
        }

        static IEnumerable<SearchCandidate> SearchCandidates(Animator actor, string query)
        {
            var seen = new HashSet<string>();
            var controller = actor ? actor.runtimeAnimatorController as AnimatorController : null;
            if (actor && actor.runtimeAnimatorController is AnimatorOverrideController) controller = null;
            if (controller)
                for (var layerIndex = 0; layerIndex < controller.layers.Length; layerIndex++)
                    foreach (var item in EnumerateStates(controller.layers[layerIndex].stateMachine, controller.layers[layerIndex].name).OrderBy(x => x.path, StringComparer.Ordinal))
                    {
                        if (!(item.state.motion is AnimationClip clip)) continue;
                        if (!Matches(item.path, query) && !Matches(clip.name, query) && !Matches(AssetDatabase.GetAssetPath(clip), query)) continue;
                        var candidate = Candidate(clip);
                        if (candidate == null) continue;
                        candidate.state = item.state;
                        candidate.stateName = item.path;
                        candidate.layer = layerIndex;
                        // Keep each state context: a shared clip can be compatible in one state
                        // and incompatible in another, or have different searchable state names.
                        seen.Add(candidate.identity);
                        yield return candidate;
                    }
            // Include model assets explicitly and inspect their sub-assets: name/type searches
            // alone can omit animation takes embedded in imported FBX files.
            var paths = AssetDatabase.FindAssets("t:AnimationClip").Concat(AssetDatabase.FindAssets("t:Model"))
                .Select(AssetDatabase.GUIDToAssetPath).Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal);
            foreach (var path in paths)
                foreach (var candidate in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Select(Candidate).Where(item => item != null)
                    .OrderBy(item => item.clip.name, StringComparer.Ordinal).ThenBy(item => item.localId))
                {
                    if (!Matches(candidate.clip.name, query) && !Matches(path, query)) continue;
                    if (seen.Add(candidate.identity)) yield return candidate;
                }
        }

        public static JObject Import(JObject request)
        {
            var source = Path.GetFullPath((string)request["path"] ?? "");
            var extension = Path.GetExtension(source).ToLowerInvariant();
            if (extension != ".fbx" && extension != ".anim") throw new AutoHitTimerException("INVALID_FILE", ".fbx 또는 .anim 파일만 가져올 수 있습니다.");
            if (!File.Exists(source)) throw new AutoHitTimerException("FILE_MISSING", "파일을 찾을 수 없습니다.");
            if (new FileInfo(source).Length > 512L * 1024 * 1024) throw new AutoHitTimerException("FILE_TOO_LARGE", "파일은 512 MiB 이하여야 합니다.");
            var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string assetPath;
            if (source.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase)) assetPath = "Assets/" + source.Substring(assetsRoot.Length).Replace('\\', '/');
            else
            {
                string hash;
                using (var stream = File.OpenRead(source)) using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").Substring(0, 16).ToLowerInvariant();
                var safeName = string.Concat(Path.GetFileNameWithoutExtension(source).Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));
                assetPath = "Assets/AutoHitTimer/Imports/" + safeName + "_" + hash + extension;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(AutoHitTimerBridge.ProjectPath, assetPath)));
                if (!File.Exists(Path.Combine(AutoHitTimerBridge.ProjectPath, assetPath))) File.Copy(source, Path.Combine(AutoHitTimerBridge.ProjectPath, assetPath));
            }
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var actor = string.IsNullOrEmpty((string)request["actorId"]) ? null : AutoHitTimerScene.ResolveAnimator((string)request["actorId"]);
            var rows = new JArray(AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).Select(c => Describe(c, actor)));
            if (rows.Count == 0) throw new AutoHitTimerException("NO_CLIPS", "가져온 파일에 애니메이션 클립이 없습니다.");
            return new JObject { ["clips"] = rows };
        }

        public static string RigReason(AnimationClip clip, Animator actor)
        {
            if (!clip || clip.length <= 0) return "재생할 수 있는 길이의 클립이 필요합니다.";
            if (clip.legacy) return "Legacy 클립은 Animator 미리보기를 지원하지 않습니다. Generic 또는 Humanoid로 가져와 주세요.";
            if (!actor) return null;
            if (clip.humanMotion && (!actor.avatar || !actor.avatar.isValid || !actor.avatar.isHuman)) return "Humanoid 클립에는 유효한 Humanoid Avatar가 필요합니다.";
            if (!clip.humanMotion)
            {
                var transformBindings = AnimationUtility.GetCurveBindings(clip).Where(x => x.type == typeof(Transform)).ToArray();
                var missing = transformBindings.FirstOrDefault(x => !string.IsNullOrEmpty(x.path) && !actor.transform.Find(x.path));
                if (!string.IsNullOrEmpty(missing.path)) return "클립의 본 경로가 캐릭터에 없습니다: " + missing.path;
            }
            return null;
        }

        public static string StateReason(AnimatorState state, int layer)
        {
            if (layer != 0) return "기본 레이어의 단일 클립 상태만 자동 적용할 수 있습니다.";
            if (!(state.motion is AnimationClip)) return "Blend Tree 또는 클립이 없는 상태는 자동 적용할 수 없습니다.";
            if (!Mathf.Approximately(state.speed, 1f) || state.speedParameterActive) return "상태 속도가 1이 아니거나 속도 파라미터를 사용합니다.";
            if (!Mathf.Approximately(state.cycleOffset, 0f) || state.cycleOffsetParameterActive || state.timeParameterActive) return "시작 오프셋 또는 동적 재생 시간을 사용하는 상태입니다.";
            if (state.mirror || state.mirrorParameterActive) return "미러링 상태는 클립 미리보기와 다르므로 자동 적용할 수 없습니다.";
            return null;
        }

        public static IEnumerable<(AnimatorState state, string path)> EnumerateStates(AnimatorStateMachine machine, string prefix)
        {
            foreach (var child in machine.states) yield return (child.state, prefix + "." + child.state.name);
            foreach (var child in machine.stateMachines) foreach (var state in EnumerateStates(child.stateMachine, prefix + "." + child.stateMachine.name)) yield return state;
        }

        static bool Matches(string text, string query) => text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        static JObject Describe(AnimationClip clip, Animator actor)
        {
            var row = ClipRef(clip);
            var reason = RigReason(clip, actor);
            row["compatible"] = reason == null;
            row["reason"] = reason;
            return row;
        }
    }
}
