using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioBaekho.AutoHitTimer.FourTigerSword
{
    [InitializeOnLoad]
    public sealed class FourTigerAutoHitTimerAdapter : IAutoHitTimerAdapter
    {
        public string Id => "four-tiger-sword";
        private static string Store => Path.GetFullPath("Library/AutoHitTimer/four-tiger-sword");
        internal static Action BeforePersistForTests;

        static FourTigerAutoHitTimerAdapter()
        {
            AutoHitTimerAdapters.Register(new FourTigerAutoHitTimerAdapter());
        }

        public JObject Targets(string actorId)
        {
            var animator = AutoHitTimerScene.ResolveAnimator(actorId);
            var sources = new JArray();
            var targets = new JArray();
            var groups = new JArray();
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponActionDataSO").OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
            {
                var source = LoadSource(guid);
                string snapshot = Snapshot(source, animator);
                sources.Add(new JObject { ["guid"] = guid, ["label"] = source.name,
                    ["type"] = source.GetType().Name, ["path"] = AssetDatabase.GetAssetPath(source) });
                foreach (string group in FourTigerHitMapping.Groups)
                {
                    string reason = FourTigerHitMapping.UnsupportedReason(source, group);
                    groups.Add(new JObject { ["sourceGuid"] = guid, ["id"] = group,
                        ["label"] = FourTigerHitMapping.GroupLabel(group), ["supported"] = reason == null, ["reason"] = reason });
                }
                foreach (var slot in FourTigerHitMapping.Slots(source))
                {
                    if (slot.Action == null) continue;
                    string reason = FourTigerHitMapping.UnsupportedReason(source, slot.Group);
                    var stateIssue = StateIssue(animator, slot, out var state);
                    if (reason == null) reason = stateIssue;
                    targets.Add(new JObject { ["sourceGuid"] = guid, ["actionPath"] = slot.Path, ["group"] = slot.Group,
                        ["label"] = source.name + " / " + slot.Label, ["animationName"] = slot.Action.AnimationName,
                        ["duration"] = slot.Action.Duration, ["supported"] = reason == null, ["reason"] = reason,
                        ["needsClipAssignment"] = state != null && state.State.motion == null,
                        ["currentClip"] = state?.State.motion is AnimationClip currentClip ? AutoHitTimerAssets.ClipRef(currentClip) : null,
                        ["timeSettings"] = FourTigerHitMapping.TimeSettings(slot.Action, slot.Action.Duration),
                        ["windows"] = FourTigerHitMapping.Windows(slot.Action), ["snapshot"] = snapshot });
                }
            }
            return new JObject { ["sources"] = sources, ["targets"] = targets, ["groups"] = groups };
        }

        public JObject PreviewChange(JObject request)
        {
            EnsureEditor();
            var prepared = Prepare(request);
            string id = Guid.NewGuid().ToString("N");
            string summary = prepared.Register
                ? prepared.Source.name + " / " + FourTigerHitMapping.GroupLabel(prepared.Group) + "에 공격 등록"
                : prepared.Source.name + " / " + prepared.Slot.Label + " 공격 및 시간 설정 변경";
            if (prepared.AssignClip) summary += prepared.PreviousClip == null ? " 및 빈 Animator 상태에 클립 연결" : " 및 Animator 클립 교체";
            var response = new JObject {
                ["changeId"] = id, ["snapshot"] = prepared.Snapshot, ["summary"] = summary,
                ["before"] = Describe(prepared.Before), ["after"] = Describe(prepared.After),
                ["canApply"] = prepared.Issues.Count == 0, ["issues"] = prepared.Issues,
                ["hitCountBefore"] = FourTigerHitMapping.Windows(prepared.Before).Count,
                ["hitCountAfter"] = FourTigerHitMapping.Windows(prepared.After).Count,
                ["proposedStateName"] = prepared.Register ? prepared.StateName : null, ["requiredTimeOverrides"] = prepared.InvalidTimes,
                ["editableTimeSettings"] = FourTigerHitMapping.TimeSettings(prepared.After, prepared.Clip.length),
                ["clipAssignment"] = prepared.AssignClip ? new JObject { ["stateName"] = prepared.StatePath,
                    ["clipName"] = prepared.Clip.name, ["clipPath"] = AssetDatabase.GetAssetPath(prepared.Clip),
                    ["previousClipName"] = prepared.PreviousClip != null ? prepared.PreviousClip.name : null,
                    ["previousClipPath"] = prepared.PreviousClip != null ? AssetDatabase.GetAssetPath(prepared.PreviousClip) : null } : null,
                ["controllerChanged"] = prepared.ChangesController,
                ["affectedAttacks"] = AffectedAttacks(prepared),
                ["damageBefore"] = FourTigerHitMapping.DamageSum(prepared.Before),
                ["damageAfter"] = FourTigerHitMapping.DamageSum(prepared.After),
                ["damageNote"] = "한 대상에 모든 히트가 적중했을 때의 기본 피해 계수 합계입니다. 공격력·치명타·폼 효과는 제외합니다.",
                ["mapping"] = MappingSummary(prepared.MappingSource, request["windows"] as JArray),
                ["template"] = prepared.Register ? Describe(prepared.MappingSource) : null,
                ["copy"] = prepared.Copy, ["outputFolder"] = prepared.OutputFolder
            };
            var savedRequest = (JObject)request.DeepClone();
            savedRequest["expectedSnapshot"] = prepared.Snapshot;
            savedRequest["stateName"] = prepared.StateName;
            WriteJson(ChangePath(id), new JObject { ["status"] = "preview", ["request"] = savedRequest,
                ["response"] = response, ["clipSnapshot"] = ClipSnapshot(prepared.Clip),
                ["createdUtc"] = DateTime.UtcNow.ToString("O") });
            return response;
        }

        public JObject Apply(JObject request)
        {
            EnsureEditor();
            string id = RequiredId((string)request["changeId"]);
            string path = ChangePath(id);
            if (!File.Exists(path)) throw new InvalidOperationException("변경 미리보기가 없습니다. 다시 검토하세요.");
            var record = JObject.Parse(File.ReadAllText(path));
            string status = (string)record["status"];
            if (status == "complete") return (JObject)record["result"].DeepClone();
            if (status != "preview") throw new InvalidOperationException("이 변경은 이미 처리 중이거나 복구가 필요합니다. Unity의 Tools > Studio Baekho > Auto Hit Timer > Recover Interrupted Save를 확인하세요.");
            foreach (string other in Directory.GetFiles(Path.GetDirectoryName(path), "*.json"))
            {
                var pending = JObject.Parse(File.ReadAllText(other));
                string pendingStatus = (string)pending["status"];
                if (pendingStatus == "applying" || pendingStatus == "recovery-required")
                    throw new InvalidOperationException("이전 저장이 중단되었습니다. Unity의 Recover Interrupted Save 메뉴에서 복구한 뒤 저장하세요.");
            }
            if (!(bool)record["response"]["canApply"]) throw new InvalidOperationException("미리보기의 문제를 해결한 다음 다시 검토하세요.");
            var prepared = Prepare((JObject)record["request"]);
            if (prepared.Issues.Count > 0) throw new InvalidOperationException(string.Join("\n", prepared.Issues.Values<string>()));
            if (ClipSnapshot(prepared.Clip) != (string)record["clipSnapshot"])
                throw new InvalidOperationException("클립이 분석 이후 변경되었습니다. 다시 분석하세요.");

            var created = new List<string>();
            var folders = new List<string>();
            var originals = new List<string>();
            var source = prepared.Source;
            var controller = prepared.Controller;
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            if (!prepared.Copy) originals.Add(sourcePath);
            if (prepared.ChangesController && !prepared.Copy) originals.Add(controllerPath);
            // Save only the assets the confirmed change is about to modify. Backups then include existing inspector edits.
            if (!prepared.Copy) AssetDatabase.SaveAssetIfDirty(source);
            if (prepared.ChangesController && !prepared.Copy)
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(controllerPath)) AssetDatabase.SaveAssetIfDirty(obj);
            string backupDir = Path.Combine(Store, "snapshots", id);
            Directory.CreateDirectory(backupDir);
            var backups = new JArray();
            foreach (string original in originals.Distinct())
            {
                string backup = Path.Combine(backupDir, backups.Count + Path.GetExtension(original));
                File.Copy(Path.GetFullPath(original), backup);
                backups.Add(new JObject { ["path"] = original, ["backup"] = backup });
            }
            if (prepared.Copy)
            {
                string folder = "Assets";
                foreach (string segment in prepared.OutputFolder.Split('/').Skip(1))
                {
                    folder += "/" + segment;
                    if (!AssetDatabase.IsValidFolder(folder)) folders.Add(folder);
                }
                sourcePath = UniqueOutputPath(prepared.OutputFolder + "/" + Path.GetFileNameWithoutExtension(sourcePath) + "_HitTiming.asset");
                created.Add(sourcePath);
                if (prepared.ChangesController)
                {
                    controllerPath = UniqueOutputPath(prepared.OutputFolder + "/" + Path.GetFileNameWithoutExtension(controllerPath) + "_HitTiming.controller");
                    created.Add(controllerPath);
                }
            }
            record["status"] = "applying";
            record["backups"] = backups;
            record["createdAssets"] = new JArray(created);
            record["createdFolders"] = new JArray(folders);
            record["beforeSource"] = EditorJsonUtility.ToJson(prepared.Source, true);
            WriteJson(path, record); // Write-ahead journal precedes any new asset or state.
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Auto Hit Timer: " + source.name);
            try
            {
                if (prepared.Copy)
                {
                    foreach (string folder in folders)
                    {
                        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(folder))))
                            throw new IOException("복사 폴더 생성 실패: " + folder);
                    }
                    source = Object.Instantiate(prepared.Source);
                    source.name = Path.GetFileNameWithoutExtension(sourcePath);
                    AssetDatabase.CreateAsset(source, sourcePath);
                    Undo.RegisterCreatedObjectUndo(source, "Auto Hit Timer copy");
                    if (prepared.ChangesController)
                    {
                        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(prepared.Controller), controllerPath))
                            throw new IOException("Animator Controller 복사에 실패했습니다.");
                        controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                        Undo.RegisterCreatedObjectUndo(controller, "Auto Hit Timer controller copy");
                    }
                }
                Undo.RegisterCompleteObjectUndo(source, "Auto Hit Timer action");
                if (prepared.Register)
                {
                    var machine = controller.layers[0].stateMachine;
                    Undo.RegisterCompleteObjectUndo(controller, "Auto Hit Timer controller");
                    Undo.RegisterCompleteObjectUndo(machine, "Auto Hit Timer state machine");
                    var state = machine.AddState(prepared.StateName);
                    Undo.RegisterCreatedObjectUndo(state, "Auto Hit Timer state");
                    state.motion = prepared.Clip;
                    state.speed = 1;
                    state.speedParameterActive = false;
                    state.cycleOffset = 0;
                    state.cycleOffsetParameterActive = false;
                    state.timeParameterActive = false;
                    state.writeDefaultValues = false;
                    EditorUtility.SetDirty(state);
                    EditorUtility.SetDirty(machine);
                    EditorUtility.SetDirty(controller);
                }
                else if (prepared.AssignClip)
                {
                    // Resolve on the destination controller so copy-saving never edits the original state.
                    var state = States(controller.layers[0].stateMachine, controller.layers[0].name)
                        .Single(entry => entry.Path == prepared.StatePath).State;
                    // CopyAsset can clone controller-owned clip subassets, so their destination identity differs.
                    // The source controller snapshot has already been revalidated before copying.
                    if (!prepared.Copy && state.motion != prepared.PreviousClip)
                        throw new InvalidOperationException("상태 클립이 검토 이후 변경되었습니다. 대상 목록을 새로 불러오세요.");
                    Undo.RegisterCompleteObjectUndo(state, "Auto Hit Timer assign clip");
                    state.motion = prepared.Clip;
                    EditorUtility.SetDirty(state);
                    EditorUtility.SetDirty(controller);
                }
                string actionPath = FourTigerHitMapping.Put(source, prepared.Slot?.Path, prepared.Group,
                    prepared.Register, prepared.After);
                EditorUtility.SetDirty(source);
                BeforePersistForTests?.Invoke();
                AssetDatabase.SaveAssetIfDirty(source);
                if (prepared.ChangesController)
                    foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(controllerPath)) AssetDatabase.SaveAssetIfDirty(obj);
                var assets = new JArray { AssetInfo(source) };
                if (prepared.ChangesController) assets.Add(AssetInfo(controller));
                var result = new JObject { ["assets"] = assets, ["actionPath"] = actionPath,
                    ["summary"] = prepared.Copy
                        ? prepared.ChangesController ? "복사 저장 완료. 반환된 SO와 Controller를 게임에 연결하세요." : "복사 저장 완료. 반환된 SO를 게임에 연결하세요."
                        : "원본 저장 완료. Unity Undo로 되돌릴 수 있습니다.",
                    ["snapshotFolder"] = backupDir, ["changeId"] = id };
                record["afterSource"] = EditorJsonUtility.ToJson(source, true);
                record["status"] = "complete";
                record["result"] = result;
                WriteJson(path, record);
                Undo.CollapseUndoOperations(undoGroup);
                return result;
            }
            catch (Exception failure)
            {
                try
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    if (prepared.Copy && source != null && source != prepared.Source && !EditorUtility.IsPersistent(source)) Object.DestroyImmediate(source);
                    Restore(record);
                    record["status"] = "rolled-back";
                    record["error"] = failure.Message;
                    WriteJson(path, record);
                }
                catch (Exception recoveryFailure)
                {
                    record["status"] = "recovery-required";
                    record["error"] = failure.Message + " / 복구 실패: " + recoveryFailure.Message;
                    WriteJson(path, record);
                }
                throw new InvalidOperationException("저장하지 못했습니다. " + (string)record["status"] + ": " + failure.Message, failure);
            }
        }

        public JObject SaveStatus(JObject request)
        {
            string id = RequiredId((string)request["changeId"]);
            string path = ChangePath(id);
            if (!File.Exists(path)) return new JObject { ["status"] = "missing", ["changeId"] = id };
            try
            {
                var record = JObject.Parse(File.ReadAllText(path));
                string status = (string)record["status"];
                if (!new[] { "complete", "preview", "applying", "recovery-required", "rolled-back" }.Contains(status))
                    return new JObject { ["status"] = "recovery-required", ["changeId"] = id, ["error"] = "알 수 없는 변경 기록 상태입니다. Unity에서 복구 기록을 확인하세요." };
                var result = new JObject { ["status"] = status, ["changeId"] = id };
                if (status == "complete") result["result"] = record["result"]?.DeepClone();
                if (record["error"] != null) result["error"] = record["error"].DeepClone();
                return result;
            }
            catch (Exception error) when (error is IOException || error is JsonException)
            {
                return new JObject { ["status"] = "recovery-required", ["changeId"] = id,
                    ["error"] = "저장 기록을 읽지 못했습니다. 재전송하지 말고 Unity에서 확인하세요: " + error.Message };
            }
        }

        private sealed class Prepared
        {
            public WeaponActionDataSO Source;
            public FourTigerHitMapping.ActionSlot Slot;
            public WeaponActionData Before;
            public WeaponActionData MappingSource;
            public WeaponActionData After;
            public AnimatorController Controller;
            public AnimationClip Clip, PreviousClip;
            public string Snapshot, StateName, StatePath, Group, OutputFolder;
            public bool Register, Copy, AssignClip;
            public bool ChangesController => Register || AssignClip;
            public JArray Issues = new JArray();
            public JArray InvalidTimes = new JArray();
        }

        private static Prepared Prepare(JObject request)
        {
            var result = new Prepared();
            result.Source = LoadSource((string)request["sourceGuid"]);
            var animator = AutoHitTimerScene.ResolveAnimator((string)request["actorId"]);
            result.Controller = animator.runtimeAnimatorController as AnimatorController;
            result.Clip = AutoHitTimerAssets.ResolveClip((JObject)request["clip"]);
            string rigIssue = AutoHitTimerAssets.RigReason(result.Clip, animator);
            if (rigIssue != null) result.Issues.Add(rigIssue);
            string analyzedClipHash = (string)request["expectedClipHash"] ?? (string)request["clip"]?["sourceFingerprint"];
            if (string.IsNullOrEmpty(analyzedClipHash) || analyzedClipHash != AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(result.Clip)).ToString())
                result.Issues.Add("분석한 클립의 원본 확인 정보가 없거나 클립이 변경되었습니다. 다시 촬영·분석하세요.");
            result.Snapshot = Snapshot(result.Source, animator);
            string expected = (string)request["expectedSnapshot"];
            if (!string.IsNullOrEmpty(expected) && expected != result.Snapshot)
                result.Issues.Add("SO 또는 Animator가 검토 이후 변경되었습니다. 대상 목록을 새로 불러오세요.");
            string mode = (string)request["mode"] ?? "update";
            if (mode != "update" && mode != "register") throw new ArgumentException("mode는 update 또는 register여야 합니다.");
            result.Register = mode == "register";
            result.Copy = (bool?)request["copy"] ?? false;
            result.OutputFolder = (string)request["outputFolder"] ?? "Assets/StudioBaekho/AutoHitTimer/Results";
            if (result.Copy)
            {
                if (!IsAssetFolder(result.OutputFolder)) result.Issues.Add("복사 폴더는 Assets 아래의 Unity 폴더 경로여야 합니다.");
                else if (File.Exists(result.OutputFolder)) result.Issues.Add("복사 폴더 위치에 파일이 있습니다.");
            }
            string sourcePath = AssetDatabase.GetAssetPath(result.Source);
            if (!sourcePath.StartsWith("Assets/", StringComparison.Ordinal)) result.Issues.Add("Assets 안의 SO만 저장할 수 있습니다.");
            if (result.Controller == null || result.Controller.layers.Length == 0)
                result.Issues.Add("AnimatorController의 기본 레이어가 필요합니다. Override Controller는 v1에서 지원하지 않습니다.");
            if (result.Clip == null || result.Clip.length <= 0) throw new ArgumentException("유효한 애니메이션 클립이 필요합니다.");
            if (result.Register)
            {
                result.Group = (string)request["group"];
                if (!FourTigerHitMapping.Groups.Contains(result.Group)) throw new ArgumentException("새 공격 종류를 선택하세요.");
                result.Slot = FourTigerHitMapping.Resolve(result.Source, (string)request["templatePath"]);
                result.MappingSource = result.Slot.Action;
                result.Before = result.Group == "air" ? result.Source.AirAttackStep : result.Group == "counter" ? result.Source.CounterStep : null;
                string reason = FourTigerHitMapping.UnsupportedReason(result.Source, result.Group);
                if (reason != null) result.Issues.Add(reason);
                result.StateName = UniqueStateName(result.Controller, (string)request["stateName"] ?? "AHT_" + result.Clip.name);
                result.After = FourTigerHitMapping.Map(result.MappingSource, request["windows"] as JArray, result.Clip.length);
                result.After.AnimationName = result.StateName;
                result.After.Duration = result.Clip.length;
                if (result.Controller != null && result.Controller.layers.Length > 0 && result.Controller.layers[0].syncedLayerIndex >= 0)
                    result.Issues.Add("동기화된 기본 레이어는 지원하지 않습니다.");
            }
            else
            {
                result.Slot = FourTigerHitMapping.Resolve(result.Source, (string)request["actionPath"]);
                result.Group = result.Slot.Group;
                result.Before = result.Slot.Action;
                result.MappingSource = result.Before;
                var stateIssue = StateIssue(animator, result.Slot, out var state);
                string reason = FourTigerHitMapping.UnsupportedReason(result.Source, result.Group) ?? stateIssue;
                if (reason != null) result.Issues.Add(reason);
                result.PreviousClip = state?.State.motion as AnimationClip;
                result.AssignClip = state != null && stateIssue == null && state.State.motion != result.Clip;
                result.StatePath = state?.Path;
                if (result.Before == null) throw new ArgumentException("비어 있는 공격은 새 공격 등록을 사용하세요.");
                result.StateName = result.Before.AnimationName;
                result.After = FourTigerHitMapping.Map(result.Before, request["windows"] as JArray, result.Clip.length);
                result.After.Duration = result.Clip.length;
            }
            FourTigerHitMapping.ApplyTimeOverrides(result.After, request["timeOverrides"] as JArray);
            result.InvalidTimes = FourTigerHitMapping.InvalidTimes(result.After, result.Clip.length);
            if (result.InvalidTimes.Count > 0) result.Issues.Add("공격의 시간 설정을 선택한 클립 길이에 맞게 수정하세요.");
            if (result.ChangesController && result.Controller != null)
            {
                if (!AssetDatabase.GetAssetPath(result.Controller).StartsWith("Assets/", StringComparison.Ordinal))
                    result.Issues.Add("Assets 안의 Animator Controller만 변경할 수 있습니다.");
                if (result.Copy && AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(result.Controller)).Any(EditorUtility.IsDirty))
                    result.Issues.Add("Controller의 미저장 편집 내용을 Unity에서 저장한 뒤 복사하세요.");
            }
            return result;
        }

        public static string Snapshot(WeaponActionDataSO source, Animator animator)
        {
            var text = new StringBuilder(EditorJsonUtility.ToJson(source));
            string sourcePath = AssetDatabase.GetAssetPath(source);
            text.Append('|').Append(AssetDatabase.GetAssetDependencyHash(sourcePath));
            AppendDiskSnapshot(text, sourcePath);
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                var runtime = animator.runtimeAnimatorController;
                string path = AssetDatabase.GetAssetPath(runtime);
                text.Append('|').Append(path).Append('|').Append(AssetDatabase.GetAssetDependencyHash(path));
                AppendDiskSnapshot(text, path);
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path).OrderBy(o => o.GetInstanceID()))
                    text.Append('|').Append(EditorJsonUtility.ToJson(obj));
            }
            return Hash(text.ToString());
        }

        private static void AppendDiskSnapshot(StringBuilder text, string assetPath)
        {
            // Include pending external edits even before Unity's importer refreshes its dependency cache.
            foreach (string path in new[] { assetPath, assetPath + ".meta" })
            {
                text.Append('|');
                if (!File.Exists(path)) { text.Append("missing"); continue; }
                using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sha = SHA256.Create()) text.Append(BitConverter.ToString(sha.ComputeHash(stream)));
            }
        }

        private static string ClipSnapshot(AnimationClip clip)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            return Hash(AutoHitTimerAssets.ClipRef(clip).ToString(Formatting.None) + AssetDatabase.GetAssetDependencyHash(path));
        }

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        private sealed class StateEntry
        {
            public AnimatorState State;
            public string Path;
        }

        private static IEnumerable<StateEntry> States(AnimatorStateMachine machine, string prefix)
        {
            foreach (var item in machine.states) yield return new StateEntry { State = item.state, Path = prefix + "." + item.state.name };
            foreach (var child in machine.stateMachines)
                foreach (var entry in States(child.stateMachine, prefix + "." + child.stateMachine.name)) yield return entry;
        }

        private static string StateIssue(Animator animator, FourTigerHitMapping.ActionSlot slot, out StateEntry entry)
        {
            entry = null;
            if (slot.Action == null) return "공격 데이터가 없습니다.";
            var controller = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
            if (controller == null || controller.layers.Length == 0) return "기본 AnimatorController를 선택하세요. Override Controller는 v1에서 지원하지 않습니다.";
            string name = slot.Action.AnimationName;
            if (string.IsNullOrWhiteSpace(name)) return "공격의 Animator 상태 이름이 비어 있습니다.";
            var layer = controller.layers[0];
            if (layer.syncedLayerIndex >= 0) return "동기화된 기본 레이어는 지원하지 않습니다.";
            var matches = States(layer.stateMachine, layer.name).Where(s => s.State.name == name || s.Path == name).ToArray();
            if (matches.Length != 1) return matches.Length == 0 ? "기본 레이어에서 Animator 상태를 찾지 못했습니다: " + name : "동일한 이름의 Animator 상태가 여러 개입니다: " + name;
            entry = matches[0];
            var state = entry.State;
            // Keep the state and its transitions; only a simple clip motion can be assigned or replaced.
            if (state.motion != null && !(state.motion is AnimationClip)) return "Blend Tree 상태는 자동 적용할 수 없습니다.";
            if (!Mathf.Approximately(state.speed, 1) || state.speedParameterActive || state.timeParameterActive)
                return "상태 속도 1이며 동적 속도·시간 파라미터가 없는 상태만 지원합니다.";
            if (!Mathf.Approximately(state.cycleOffset, 0) || state.cycleOffsetParameterActive)
                return "시작 오프셋 0인 상태만 지원합니다.";
            if (state.mirror || state.mirrorParameterActive) return "미러링 상태는 클립 미리보기와 달라 자동 적용할 수 없습니다.";
            if (slot.List != null)
            {
                Func<WeaponActionData, bool> sameState = action => action != null
                    && (action.AnimationName == state.name || action.AnimationName == matches[0].Path);
                bool previous = slot.Index > 0 && sameState(slot.List[slot.Index - 1]);
                bool next = slot.Index + 1 < slot.List.Count && sameState(slot.List[slot.Index + 1]);
                if (previous || next) return "연속 단계에서 같은 Animator 상태를 재사용하므로 클립과 공격 시간 대응이 불명확합니다.";
            }
            return null;
        }

        private static string UniqueStateName(AnimatorController controller, string requested)
        {
            string stem = Regex.Replace(requested ?? "AHT_Attack", @"[^\p{L}\p{N}_-]", "_").Trim('_');
            if (string.IsNullOrEmpty(stem)) stem = "AHT_Attack";
            if (stem.Length > 80) stem = stem.Substring(0, 80);
            var names = controller == null || controller.layers.Length == 0 ? new HashSet<string>()
                : new HashSet<string>(States(controller.layers[0].stateMachine, controller.layers[0].name).Select(s => s.State.name));
            string name = stem;
            for (int i = 2; names.Contains(name); i++) name = stem + "_" + i;
            return name;
        }

        private static JObject Describe(WeaponActionData action)
        {
            if (action == null) return new JObject();
            return new JObject { ["animationName"] = action.AnimationName, ["duration"] = action.Duration,
                ["windows"] = FourTigerHitMapping.Windows(action), ["baseDamage"] = action.Damage,
                ["damageSum"] = FourTigerHitMapping.DamageSum(action),
                ["serialized"] = JObject.Parse(JsonUtility.ToJson(action)) };
        }

        private static JArray AffectedAttacks(Prepared prepared)
        {
            var result = new JArray();
            if (!prepared.AssignClip) return result;
            var state = States(prepared.Controller.layers[0].stateMachine, prepared.Controller.layers[0].name)
                .Single(entry => entry.Path == prepared.StatePath);
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponActionDataSO").OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
            {
                var source = LoadSource(guid);
                foreach (var slot in FourTigerHitMapping.Slots(source))
                {
                    if (source == prepared.Source && slot.Path == prepared.Slot.Path) continue;
                    if (slot.Action == null || (slot.Action.AnimationName != state.State.name && slot.Action.AnimationName != state.Path)) continue;
                    result.Add(new JObject { ["sourceName"] = source.name, ["sourcePath"] = AssetDatabase.GetAssetPath(source),
                        ["actionPath"] = slot.Path, ["label"] = slot.Label });
                }
            }
            return result;
        }

        private static JObject MappingSummary(WeaponActionData before, JArray windows)
        {
            var mapped = new HashSet<int>((windows ?? new JArray()).OfType<JObject>().Select(w => (int?)w["sourceIndex"] ?? -1).Where(i => i >= 0));
            return new JObject { ["retained"] = new JArray(mapped.OrderBy(i => i)),
                ["removed"] = new JArray(FourTigerHitMapping.Windows(before).OfType<JObject>().Select(w => (int)w["sourceIndex"]).Where(i => !mapped.Contains(i))),
                ["added"] = (windows ?? new JArray()).Count(w => (int?)w["sourceIndex"] == -1) };
        }

        private static WeaponActionDataSO LoadSource(string guid)
        {
            var source = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(AssetDatabase.GUIDToAssetPath(guid ?? ""));
            if (source == null) throw new ArgumentException("사인검 공격 SO를 찾을 수 없습니다.");
            return source;
        }

        private static JObject AssetInfo(Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return new JObject { ["guid"] = AssetDatabase.AssetPathToGUID(path), ["path"] = path, ["type"] = asset.GetType().Name };
        }

        private static bool IsAssetFolder(string path)
        {
            if (path == "Assets") return true;
            if (string.IsNullOrEmpty(path) || path.Contains('\\') || !path.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            if (path.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) || segment == "." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return false;
            string root = Path.GetFullPath("Assets").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase) && !path.Split('/').Contains("..");
        }

        private static string UniqueOutputPath(string candidate)
        {
            // Unity returns an empty unique path until the parent folder exists. Planning must not create it.
            string folder = Path.GetDirectoryName(candidate).Replace('\\', '/');
            string result = AssetDatabase.IsValidFolder(folder) ? AssetDatabase.GenerateUniqueAssetPath(candidate) : candidate;
            if (string.IsNullOrEmpty(result)) throw new IOException("복사 에셋 경로를 만들지 못했습니다: " + candidate);
            return result;
        }

        private static void EnsureEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Unity가 편집 모드이고 컴파일·임포트가 끝난 뒤 다시 시도하세요.");
        }

        private static string RequiredId(string id)
        {
            if (id == null || !Regex.IsMatch(id, "^[a-f0-9]{32}$")) throw new ArgumentException("유효한 changeId가 필요합니다.");
            return id;
        }

        private static string ChangePath(string id) => Path.Combine(Store, "changes", RequiredId(id) + ".json");

        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string source, string destination, uint flags);

        private static void WriteJson(string path, JObject value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, value.ToString(Formatting.Indented), new UTF8Encoding(false));
            // ReplaceFile can fail while a Windows indexer/virus scanner briefly holds a new journal.
            // MoveFileEx uses a same-volume atomic rename without merging the old file's metadata.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (Application.platform == RuntimePlatform.WindowsEditor)
                    {
                        if (!MoveFileEx(temporary, path, 0x1 | 0x8))
                            throw new IOException("변경 기록 원자적 저장 실패 (Win32 " + Marshal.GetLastWin32Error() + ")");
                    }
                    else if (File.Exists(path)) File.Replace(temporary, path, null, true);
                    else File.Move(temporary, path);
                    return;
                }
                catch (IOException) when (attempt < 5) { System.Threading.Thread.Sleep(25 * (attempt + 1)); }
            }
        }

        private static void Restore(JObject record)
        {
            foreach (string created in (record["createdAssets"] as JArray ?? new JArray()).Values<string>().Reverse())
            {
                if (string.IsNullOrWhiteSpace(created)) continue; // A pre-create failure cannot own an asset at an empty path.
                ValidateAssetPath(created);
                if ((File.Exists(Path.GetFullPath(created)) || AssetDatabase.LoadMainAssetAtPath(created) != null) && !AssetDatabase.DeleteAsset(created))
                    throw new IOException("복사 에셋을 제거할 수 없습니다: " + created);
            }
            foreach (JObject item in record["backups"] as JArray ?? new JArray())
            {
                string target = (string)item["path"];
                string backup = (string)item["backup"];
                ValidateAssetPath(target);
                string backupRoot = Path.Combine(Store, "snapshots") + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(backup).StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("복구 경로가 잘못되었습니다.");
                File.Copy(backup, Path.GetFullPath(target), true);
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
            foreach (string folder in (record["createdFolders"] as JArray ?? new JArray()).Values<string>().Reverse())
            {
                if (!IsAssetFolder(folder) || folder == "Assets") throw new IOException("복구 폴더 경로가 잘못되었습니다.");
                // Never remove unrelated assets that appeared in an output folder after a crash.
                if (AssetDatabase.IsValidFolder(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void ValidateAssetPath(string path)
        {
            if (!IsAssetFolder(Path.GetDirectoryName(path)?.Replace('\\', '/')))
                throw new IOException("Assets 외부 파일은 복구할 수 없습니다.");
        }

        [MenuItem("Tools/Studio Baekho/Auto Hit Timer/Recover Interrupted Save")]
        public static void RecoverInterruptedSaves()
        {
            EnsureEditor();
            string directory = Path.Combine(Store, "changes");
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory, "*.json"))
            {
                var record = JObject.Parse(File.ReadAllText(file));
                string status = (string)record["status"];
                if (status != "applying" && status != "recovery-required") continue;
                if (!EditorUtility.DisplayDialog("Auto Hit Timer 저장 복구", "중단된 저장의 변경 전 스냅샷을 복원할까요?\n" +
                        (string)record["response"]?["summary"] + "\n저장 시도 이후 같은 에셋을 수정했다면 먼저 별도로 보존하세요.", "복원", "취소")) continue;
                Restore(record);
                record["status"] = "rolled-back";
                WriteJson(file, record);
                Debug.Log("[Auto Hit Timer] 저장 복구 완료: " + file);
            }
        }
    }
}
