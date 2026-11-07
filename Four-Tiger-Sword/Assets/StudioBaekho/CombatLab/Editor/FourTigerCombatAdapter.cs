using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace StudioBaekho.CombatLab.FourTigerSword
{
    /// <summary>Project-specific bindings. The reusable package never references game classes.</summary>
    public sealed partial class FourTigerCombatAdapter : ICombatLabFeedbackAdapter, ICombatLabMultiActionAdapter, ICombatLabReadyAdapter,
        ICombatLabEarlyPlayAdapter, ICombatLabScenePreparation
    {
        public const string AdapterId = "four-tiger-sword.water";
        public const string KnockbackId = "knockbackMultiplier";
        public const string CameraId = "cameraShakeMultiplier";
        public const string HitStopId = "hitStopDuration";
        public string Id => AdapterId;
        public string DisplayName => "Four Tiger Sword / 공격·스킬";

        public void PrepareBeforeSceneStart(ExperimentRequest request)
        {
            if (request.schemaVersion != 2 || request.scenarioMode != "fixed") return;
            foreach (var run in SceneComponents<RunManager>().Where(item => item.isActiveAndEnabled))
            {
                // A late fallback cannot repair a map already generated with another seed.
                if ((run.State != RunState.Preparing || run.CurrentChapterIndex >= 0) && run.RunSeed != request.seed)
                    throw new InvalidOperationException("RunManager가 다른 seed로 이미 시작했습니다. 요청=" + request.seed + ", 실제=" + run.RunSeed);
                run.randomizeSeed = false;
                run.seed = request.seed;
            }
        }

        public bool PrepareScenario(ExperimentRequest request, out string reason)
        {
            reason = null;
            if (request.schemaVersion != 2 || request.scenarioMode != "fixed") return true;
            var runs = SceneComponents<RunManager>().Where(item => item.isActiveAndEnabled).ToArray();
            if (runs.Length > 1) throw new InvalidOperationException("고정 실험에는 활성 RunManager가 하나만 필요합니다.");
            if (runs.Length == 1)
            {
                var run = runs[0];
                if (run.State == RunState.Error || run.State == RunState.Lost || run.State == RunState.Won)
                    throw new InvalidOperationException("던전 시작 실패: " + run.State + " " + run.LastError);
                if (run.State != RunState.Playing)
                { reason = "같은 seed의 절차 생성 스테이지가 준비되기를 기다립니다."; return false; }
                if (run.RunSeed != request.seed)
                    throw new InvalidOperationException("고정 실험 seed 불일치: 요청=" + request.seed + ", RunManager=" + run.RunSeed + ". 다른 맵을 같은 조건으로 비교할 수 없습니다.");
            }
            if (SceneComponents<Enemy>().Any(IsFixedTargetReady))
            { reason = "기존 활성 표적을 사용했습니다. 런타임 던전 seed=" + (runs.Length == 1 ? runs[0].RunSeed.ToString() : "RunManager 없음") + "."; return true; }

            var room = SceneComponents<DungeonRoomEncounter>()
                .Where(item => item.isActiveAndEnabled && item.State == DungeonEncounterState.Waiting && item.Members.Count > 0)
                .OrderBy(item => StableSceneKey(item.transform), StringComparer.Ordinal).FirstOrDefault();
            if (room == null)
            { reason = "활성 표적 또는 준비된 Waiting 전투방을 기다립니다."; return false; }
            // Suppress AI before enabling Enemy.Start. Body collisions and reactions stay live.
            foreach (var member in room.Members)
                if (member != null && member.Enemy != null) member.Enemy.CombatLabPassive = true;
            if (!room.BeginEncounter())
                throw new InvalidOperationException("고정 실험 전투방 활성화 실패: " + StableSceneKey(room.transform) + " " + room.State + " " + room.Error);
            reason = "고정 실험용 Waiting 전투방을 기존 BeginEncounter API로 런타임에만 활성화했습니다: "
                + StableSceneKey(room.transform) + "; seed=" + (runs.Length == 1 ? runs[0].RunSeed.ToString() : "RunManager 없음")
                + ". 씬·에셋에 저장하지 않으며 후보마다 새 Play Mode에서 준비합니다.";
            return true;
        }

        internal static string StableSceneKey(Transform value)
        {
            var segments = new List<string>();
            for (var current = value; current != null; current = current.parent)
                segments.Add(current.name + "[" + current.GetSiblingIndex().ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + "]");
            segments.Reverse();
            return value.gameObject.scene.path + ":" + string.Join("/", segments);
        }

        public List<CombatSource> DiscoverSources()
        {
            return AssetDatabase.FindAssets("t:WeaponActionDataSO")
                .Where(guid => IsSupported(AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(AssetDatabase.GUIDToAssetPath(guid))))
                .Select(guid => new CombatSource { guid = guid, label = FormLabel(ResolveSource(guid)) + " / " + AssetDatabase.GUIDToAssetPath(guid) })
                .OrderBy(source => source.label, StringComparer.Ordinal).ToList();
        }

        public List<CombatAction> DiscoverActions(string sourceGuid)
        {
            var source = ResolveSource(sourceGuid);
            var ids = new List<string> { "normal-combo" };
            for (int i = 0; i < (source.Actions?.ComboSteps?.Count ?? 0); i++) ids.Add("normal-" + (i + 1));
            ids.AddRange(new[] { "heavy", "skill", "ultimate" });
            return ids.Select(id => {
                var item = new CombatAction { id = id, label = ActionLabel(source, id) };
                try { ValidateSource(source, id); }
                catch (Exception error) { item.available = false; item.unavailableReason = error.Message; }
                return item;
            }).ToList();
        }

        public List<ParameterSpec> Describe(string sourceGuid) => Describe(sourceGuid, null);
        public List<ParameterSpec> Describe(string sourceGuid, string actionId)
        {
            var source = ResolveSource(sourceGuid);
            ValidateSource(source, actionId);
            return ParameterSpecs(source, actionId);
        }

        public static string NormalizeActionId(string actionId) => string.IsNullOrEmpty(actionId) ? "normal-combo" : actionId;
        public static bool IsBuffUltimate(WeaponActionDataSO source, string actionId)
            => NormalizeActionId(actionId) == "ultimate" && (source is FireFormActionDataSO || source is EarthFormActionDataSO || source is WoodFormActionDataSO);
        public static string FormLabel(WeaponActionDataSO source)
            => source is WaterFormActionDataSO ? "수(水)" : source is FireFormActionDataSO ? "화(火)"
                : source is EarthFormActionDataSO ? "토(土)" : source is IronFormActionDataSO ? "금(金)" : "목(木)";
        public static string ActionLabel(WeaponActionDataSO source, string actionId)
        {
            string id = NormalizeActionId(actionId);
            if (id == "normal-combo") return "일반 공격 · 전체 콤보";
            if (id.StartsWith("normal-", StringComparison.Ordinal)) return "일반 공격 · " + id.Substring(7) + "타 단독";
            if (id == "heavy") return "강공격 · 길게 누르기";
            if (id == "skill") return "스킬 · E";
            if (id == "ultimate") return IsBuffUltimate(source, id) ? "궁극기 · R + 일반 콤보" : "궁극기 · R";
            throw new ArgumentException("지원하지 않는 동작: " + id);
        }
        private static bool IsSupported(WeaponActionDataSO source) => source is WaterFormActionDataSO || source is FireFormActionDataSO
            || source is EarthFormActionDataSO || source is IronFormActionDataSO || source is WoodFormActionDataSO;
        public static BaseForm CreateForm(WeaponActionDataSO source)
        {
            if (source is WaterFormActionDataSO water) return new WaterForm(water);
            if (source is FireFormActionDataSO fire) return new FireForm(fire);
            if (source is EarthFormActionDataSO earth) return new EarthForm(earth);
            if (source is IronFormActionDataSO iron) return new IronForm(iron);
            if (source is WoodFormActionDataSO wood) return new WoodForm(wood);
            throw new ArgumentException("지원하지 않는 폼 데이터입니다.");
        }
        public static List<WeaponActionData> SelectedSteps(WeaponActionDataSO source, string actionId)
        {
            if (source == null || source.Actions == null)
                throw new ArgumentException("폼 에셋의 Actions 그룹이 비어 있습니다.");
            string id = NormalizeActionId(actionId);
            if (id == "normal-combo") return source.Actions.ComboSteps;
            if (id == "skill") return source is EarthFormActionDataSO || source is WoodFormActionDataSO
                ? FirstStep(source.Actions.SkillSteps) : source.Actions.SkillSteps;
            if (id == "ultimate") return IsBuffUltimate(source, id) ? new List<WeaponActionData>()
                : source is WaterFormActionDataSO ? FirstStep(source.Actions.UltimateSteps) : source.Actions.UltimateSteps;
            if (id == "heavy") return source is WaterFormActionDataSO water && water.HeavyAttackStep != null
                ? new List<WeaponActionData> { water.HeavyAttackStep } : new List<WeaponActionData>();
            if (id.StartsWith("normal-", StringComparison.Ordinal) && int.TryParse(id.Substring(7), out int index)
                && index > 0 && index <= (source.Actions.ComboSteps?.Count ?? 0) && id == "normal-" + index)
                return new List<WeaponActionData> { source.Actions.ComboSteps[index - 1] };
            throw new ArgumentException("지원하지 않는 동작: " + id);
        }

        private static List<WeaponActionData> FirstStep(List<WeaponActionData> steps)
            => steps != null && steps.Count > 0 ? new List<WeaponActionData> { steps[0] } : new List<WeaponActionData>();

        // These custom hit loops call StartHitStop(step) and never pass a HitEvent.
        public static bool UsesHitEvents(WeaponActionDataSO source, string actionId)
            => NormalizeActionId(actionId) != "heavy"
                && !(NormalizeActionId(actionId) == "skill" && (source is EarthFormActionDataSO || source is WoodFormActionDataSO));

        private static ParameterSpec HitStopSpec(WeaponActionDataSO source, string actionId)
            => Spec(HitStopId, "선택 동작 히트스톱", "초", 0f, 0.3f, ReadSelectedHitStop(source, actionId),
                "Feedback.HitStop: 선택 동작의 모든 실제 타격을 이 시간으로 통일합니다. 기준값은 첫 타격의 유효 시간이며, 타격마다 다른 원본 시간도 기준 후보에서 통일됩니다. 시간 배율과 다른 동작·패링은 유지합니다.");

        private static ParameterSpec Spec(string id, string label, string unit, float minimum, float maximum, float baseline, string description)
            => new ParameterSpec { id = id, label = label, unit = unit, minimum = minimum, maximum = maximum, baseline = baseline, description = description };
        public static List<ParameterSpec> ParameterSpecs(WeaponActionDataSO source, string actionId)
        {
            string id = NormalizeActionId(actionId);
            if (id == "skill" && source is WoodFormActionDataSO)
                return new List<ParameterSpec> {
                    Spec("skillRadiusMultiplier", "끌어모으기 범위", "× 기준", 0.25f, 2f, 1f, "SkillRadius에 곱합니다. 이 스킬은 일반 넉백·카메라·히트스톱을 사용하지 않습니다."),
                    Spec("pullSpeedMultiplier", "끌어당기는 속도", "× 기준", 0f, 2f, 1f, "PullSpeed에 곱합니다. 끌어당김 시간과 정지 간격은 유지합니다."),
                    Spec("skillDamageMultiplier", "끌어모으기 피해", "× 기준", 0.25f, 2f, 1f, "실제로 사용되는 SkillDamageMultiplier에 곱합니다.") };
            if (id == "ultimate" && source is WaterFormActionDataSO)
                return new List<ParameterSpec> {
                    Spec("areaRadiusMultiplier", "궁극기 범위", "× 기준", 0.25f, 2f, 1f, "10회 연속 타격의 AreaRadius에 곱합니다. 피해·횟수·간격은 게임 코드의 고정값입니다."),
                    Spec(CameraId, "궁극기 적중 흔들림", "× 기준", 0f, 3f, 1f, "UltimateSteps[0].CameraShakeStrength에 곱합니다. 이 궁극기는 CameraProfile과 넉백을 사용하지 않습니다."),
                    HitStopSpec(source, id) };
            if (id == "ultimate" && source is FireFormActionDataSO fire)
                return new List<ParameterSpec> {
                    Spec("ultimateDamageBonusPercent", "궁극기 피해 증가", "%", 0f, 300f, fire.FormBalance.FireUltimateDamageDealtBonusPercent, "궁극기 활성화 후 일반 콤보로 피해 증가를 관찰합니다."),
                    Spec("ultimateAttackSpeedBonusPercent", "궁극기 공격 속도 증가", "%", 0f, 150f, fire.FormBalance.FireUltimateAttackSpeedBonusPercent, "활성 상태의 실제 공격 속도 증가율입니다."),
                    Spec("ultimateDamageTakenBonusPercent", "궁극기 받는 피해 증가", "%", 0f, 200f, fire.FormBalance.FireUltimateDamageTakenBonusPercent, "궁극기 활성 상태의 IncomingDamageMultiplier에 반영되는 받는 피해 증가율입니다.") };
            if (id == "ultimate" && source is EarthFormActionDataSO earth)
                return new List<ParameterSpec> {
                    Spec("ultimateShieldMultiplier", "궁극기 방어막", "× 기준", 0.25f, 2f, 1f, "UltimateShieldAmount에 곱합니다. 활성화 후 일반 콤보를 실행합니다."),
                    Spec("ultimateBuffDuration", "궁극기 버프 지속", "초", 0f, 60f, earth.FormBalance.UltimateBuffDuration, "방어막이 남은 동안 피해 증가가 가능한 시간입니다. 방어막 자체 수명은 유지합니다."),
                    Spec("ultimateDamageBonusPercent", "방어막 중 피해 증가", "%", 0f, 300f, earth.FormBalance.UltimateShieldDamageBonusPercent, "방어막과 버프가 모두 남은 동안 적용됩니다.") };
            if (id == "ultimate" && source is WoodFormActionDataSO wood)
                return new List<ParameterSpec> {
                    Spec("ultimateRadiusMultiplier", "침묵 영역 범위", "× 기준", 0.25f, 2f, 1f, "UltimateRadius에 곱합니다. 활성화 후 일반 콤보를 실행합니다."),
                    Spec("ultimateFieldDuration", "침묵 영역 지속", "초", 0f, 60f, wood.FormBalance.UltimateFieldDuration, "영역이 남아 있는 시간을 설정합니다."),
                    Spec("ultimateElementBonusPercent", "속성 피해 증가", "%", 0f, 300f, wood.FormBalance.ElementDamageBonusPercent, "ElementBuffDuration 동안 적용되는 속성 피해 증가율입니다.") };
            return new List<ParameterSpec>
            {
                new ParameterSpec { id = KnockbackId, label = "선택 동작 넉백", unit = "× 기준",
                    minimum = 0f, maximum = 2f, baseline = 1f,
                    description = "선택 동작의 단계와 활성화된 히트별 넉백에 곱합니다. 적의 거리 제한은 유지합니다." },
                new ParameterSpec { id = CameraId, label = "선택 동작 적중 흔들림", unit = "× 기준",
                    minimum = 0f, maximum = 3f, baseline = 1f,
                    description = "선택 동작의 CameraProfile.HitImpulse 또는 CameraShakeStrength에 곱합니다. 다른 동작은 유지합니다." },
                HitStopSpec(source, id)
            };
        }

        public void Validate(ExperimentRequest request)
        {
            var source = ValidateRequestData(request);
            ValidateScene(source, request.actionId, request.schemaVersion == 2 && request.scenarioMode == "fixed");
        }

        public bool IsReady(ExperimentRequest request, out string reason)
        {
            if (!Application.isPlaying) { reason = "전투 씬의 Play Mode 초기화를 기다리고 있습니다."; return false; }
            try
            {
                if (request == null) throw new ArgumentNullException(nameof(request));
                ValidateScene(ResolveSource(request.sourceGuid), request.actionId,
                    request.schemaVersion == 2 && request.scenarioMode == "fixed");
                reason = string.Empty;
                return true;
            }
            catch (Exception exception) { reason = exception.Message; return false; }
        }

        public ICombatLabSession Begin(ExperimentRequest request, CombatCandidate candidate)
        {
            Validate(request);
            if (request.schemaVersion == 1 && !candidate.isSourceControl) ValidateCandidate(candidate, ResolveSource(request.sourceGuid), request.actionId);
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before starting a combat session.");
            return new FourTigerCombatSession(request, candidate);
        }

        public Object Export(ExperimentRequest request, CombatCandidate candidate, string assetPath)
        {
            var source = ValidateRequestData(request);
            if (request.schemaVersion == 1) ValidateCandidate(candidate, source, request.actionId);
            ValidateExportPath(assetPath);
            var owned = new List<Object>();
            CombatLabPreset preset = null;
            bool created = false;
            try
            {
                var action = CloneAndApply(source, candidate, owned, request.actionId, request.schemaVersion);
                preset = ScriptableObject.CreateInstance<CombatLabPreset>();
                preset.name = Path.GetFileNameWithoutExtension(assetPath);
                preset.adapterId = AdapterId;
                preset.sourceGuid = request.sourceGuid;
                preset.actionId = NormalizeActionId(request.actionId);
                preset.candidate = CopyCandidate(candidate);
                preset.actionAsset = action;
                AssetDatabase.CreateAsset(preset, assetPath);
                created = true;
                foreach (var item in owned)
                {
                    item.hideFlags = HideFlags.None;
                    AssetDatabase.AddObjectToAsset(item, preset);
                }
                // Save only this new artifact; do not save unrelated dirty source assets.
                EditorUtility.SetDirty(preset);
                foreach (var item in owned) EditorUtility.SetDirty(item);
                AssetDatabase.SaveAssetIfDirty(preset);
                AssetDatabase.ImportAsset(assetPath);
                var saved = AssetDatabase.LoadAssetAtPath<CombatLabPreset>(assetPath);
                if (saved == null) throw new InvalidOperationException("Unity did not save the candidate preset.");
                return saved;
            }
            catch
            {
                if (created) AssetDatabase.DeleteAsset(assetPath);
                foreach (var item in owned)
                    if (item != null && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item);
                if (preset != null && !EditorUtility.IsPersistent(preset)) Object.DestroyImmediate(preset);
                throw;
            }
        }

        public Object ExportCombined(CombinedExportRequest request, string assetPath)
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || CombatLabRunner.IsRunning)
                throw new InvalidOperationException("컴파일과 실험을 마친 뒤 편집 모드에서 저장하세요.");
            CombatLab.ValidateCombinedData(request);
            if (request.adapterId != AdapterId) throw new ArgumentException("Experiment adapter ID does not match this adapter.");
            var source = ResolveSource(request.sourceGuid);
            var actionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var selection in request.selections)
            {
                ValidateRequestData(selection.request);
                actionIds.Add(NormalizeActionId(selection.request.actionId));
            }
            // The combo owns every normal step; choosing it alongside an individual step would
            // apply relative multipliers twice and make the result depend on selection order.
            if (actionIds.Contains("normal-combo") && actionIds.Any(id => id != "normal-combo" && id.StartsWith("normal-", StringComparison.Ordinal)))
                throw new ArgumentException("일반 공격 전체 콤보와 개별 타격은 함께 저장할 수 없습니다. 전체 콤보 또는 개별 타격들을 선택하세요.");
            ValidateExportPath(assetPath);
            var owned = new List<Object>();
            bool created = false;
            try
            {
                var clone = Object.Instantiate(source);
                clone.name = Path.GetFileNameWithoutExtension(assetPath);
                clone.hideFlags = HideFlags.HideAndDontSave;
                owned.Add(clone);
                foreach (var selection in request.selections)
                {
                    var candidate = selection.request.candidates.Single(item => item.id == selection.candidateId);
                    if (selection.request.schemaVersion == 2) ApplyFeedback(clone, candidate, selection.request.actionId);
                    else ApplyCandidateToClone(clone, candidate, owned, selection.request.actionId);
                }
                // The form itself is the main object so the exported file can be assigned directly
                // to the player's form field. Only newly tuned camera profiles become subassets.
                clone.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(clone, assetPath);
                created = true;
                foreach (var item in owned.Skip(1))
                {
                    item.hideFlags = HideFlags.None;
                    AssetDatabase.AddObjectToAsset(item, clone);
                }
                AssetDatabase.SetMainObject(clone, assetPath);
                foreach (var item in owned) EditorUtility.SetDirty(item);
                AssetDatabase.SaveAssetIfDirty(clone);
                AssetDatabase.ImportAsset(assetPath);
                var saved = AssetDatabase.LoadMainAssetAtPath(assetPath) as WeaponActionDataSO;
                if (saved == null || saved.GetType() != source.GetType())
                    throw new InvalidOperationException("Unity did not save the combined form asset.");
                return saved;
            }
            catch
            {
                if (created) AssetDatabase.DeleteAsset(assetPath);
                foreach (var item in owned)
                    if (item != null && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item);
                throw;
            }
        }

        public static WeaponActionDataSO ResolveSource(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid)) throw new ArgumentException("폼 공격 데이터 에셋을 선택하세요.");
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var source = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(path);
            if (!IsSupported(source)) throw new ArgumentException("지원되는 수·화·토·금·목 폼 에셋이 아닙니다.");
            return source;
        }

        /// <summary>Only owned clones are edited. Referenced VFX/audio and untouched skill profiles remain shared.</summary>
        public static WaterFormActionDataSO CloneAndApply(WaterFormActionDataSO source,
            CombatCandidate candidate, List<Object> owned)
            => (WaterFormActionDataSO)CloneAndApply((WeaponActionDataSO)source, candidate, owned, null);

        public static WeaponActionDataSO CloneAndApply(WeaponActionDataSO source,
            CombatCandidate candidate, List<Object> owned, string actionId = null, int schemaVersion = 1)
        {
            if (owned == null) throw new ArgumentNullException(nameof(owned));
            ValidateSource(source, actionId);
            if (schemaVersion == 1 && !candidate.isSourceControl) ValidateCandidate(candidate, source, actionId);
            int firstOwned = owned.Count;
            try
            {
                var clone = Object.Instantiate(source);
                clone.name = source.name + " - " + candidate.label;
                clone.hideFlags = HideFlags.HideAndDontSave;
                owned.Add(clone);
                if (!candidate.isSourceControl)
                {
                    if (schemaVersion == 2) ApplyFeedback(clone, candidate, actionId);
                    else ApplyCandidateToClone(clone, candidate, owned, actionId);
                }
                return clone;
            }
            catch
            {
                for (int i = owned.Count - 1; i >= firstOwned; i--)
                {
                    if (owned[i] != null) Object.DestroyImmediate(owned[i]);
                    owned.RemoveAt(i);
                }
                throw;
            }
        }

        private static void ApplyCandidateToClone(WeaponActionDataSO clone, CombatCandidate candidate,
            List<Object> owned, string actionId)
        {
            float knockback = ReadValue(candidate, KnockbackId, 1f);
            float camera = ReadValue(candidate, CameraId, 1f);
            // Cache only within this action: different actions sharing an original profile can
            // select different camera multipliers without modifying each other's tuned clone.
            var cameraCopies = new Dictionary<AttackCameraProfile, AttackCameraProfile>();
            if (candidate.values.Any(value => value.id == HitStopId))
                ApplyHitStop(clone, actionId, ReadHitStop(candidate, 0f));
            if (ApplySpecialParameters(clone, candidate, actionId)) return;
            foreach (var step in SelectedSteps(clone, actionId))
            {
                step.Balance.KnockbackForce = Scale(step.Balance.KnockbackForce, knockback);
                if (UsesHitEvents(clone, actionId) && step.HitEvents != null)
                    foreach (var hit in step.HitEvents)
                        if (hit.Balance.OverrideKnockbackForce) hit.Balance.KnockbackForce = Scale(hit.Balance.KnockbackForce, knockback);
                // The fallback is used even with an assigned profile when the combat camera is absent.
                step.Feedback.Camera.CameraShakeStrength = Scale(step.Feedback.Camera.CameraShakeStrength, camera);
                if (step.Feedback.Camera.CameraProfile == null) continue;
                if (!cameraCopies.TryGetValue(step.Feedback.Camera.CameraProfile, out var profile))
                {
                    profile = Object.Instantiate(step.Feedback.Camera.CameraProfile);
                    profile.name = step.Feedback.Camera.CameraProfile.name + " - " + candidate.label;
                    profile.hideFlags = HideFlags.HideAndDontSave;
                    owned.Add(profile);
                    cameraCopies.Add(step.Feedback.Camera.CameraProfile, profile);
                    profile.HitImpulse = Scale(profile.HitImpulse, camera);
                }
                step.Feedback.Camera.CameraProfile = profile;
            }
        }

        private static bool ApplySpecialParameters(WeaponActionDataSO clone, CombatCandidate candidate, string actionId)
        {
            string id = NormalizeActionId(actionId);
            if (id == "skill" && clone is WoodFormActionDataSO woodSkill)
            {
                woodSkill.FormExecution.SkillRadius = Scale(woodSkill.FormExecution.SkillRadius, ReadValue(candidate, "skillRadiusMultiplier", 1f));
                woodSkill.FormExecution.PullSpeed = Scale(woodSkill.FormExecution.PullSpeed, ReadValue(candidate, "pullSpeedMultiplier", 1f));
                woodSkill.FormBalance.SkillDamageMultiplier = Scale(woodSkill.FormBalance.SkillDamageMultiplier, ReadValue(candidate, "skillDamageMultiplier", 1f));
                return true;
            }
            if (id != "ultimate") return false;
            if (clone is WaterFormActionDataSO water)
            {
                water.FormExecution.AreaRadius = Scale(water.FormExecution.AreaRadius, ReadValue(candidate, "areaRadiusMultiplier", 1f));
                water.Actions.UltimateSteps[0].Feedback.Camera.CameraShakeStrength = Scale(water.Actions.UltimateSteps[0].Feedback.Camera.CameraShakeStrength, ReadValue(candidate, CameraId, 1f));
                return true;
            }
            if (clone is FireFormActionDataSO fire)
            {
                fire.FormBalance.FireUltimateDamageDealtBonusPercent = ReadValue(candidate, "ultimateDamageBonusPercent", fire.FormBalance.FireUltimateDamageDealtBonusPercent);
                fire.FormBalance.FireUltimateAttackSpeedBonusPercent = ReadValue(candidate, "ultimateAttackSpeedBonusPercent", fire.FormBalance.FireUltimateAttackSpeedBonusPercent);
                fire.FormBalance.FireUltimateDamageTakenBonusPercent = ReadValue(candidate, "ultimateDamageTakenBonusPercent", fire.FormBalance.FireUltimateDamageTakenBonusPercent);
                return true;
            }
            if (clone is EarthFormActionDataSO earth)
            {
                earth.FormBalance.UltimateShieldAmount = Scale(earth.FormBalance.UltimateShieldAmount, ReadValue(candidate, "ultimateShieldMultiplier", 1f));
                earth.FormBalance.UltimateBuffDuration = ReadValue(candidate, "ultimateBuffDuration", earth.FormBalance.UltimateBuffDuration);
                earth.FormBalance.UltimateShieldDamageBonusPercent = ReadValue(candidate, "ultimateDamageBonusPercent", earth.FormBalance.UltimateShieldDamageBonusPercent);
                return true;
            }
            if (clone is WoodFormActionDataSO wood)
            {
                wood.FormExecution.UltimateRadius = Scale(wood.FormExecution.UltimateRadius, ReadValue(candidate, "ultimateRadiusMultiplier", 1f));
                wood.FormBalance.UltimateFieldDuration = ReadValue(candidate, "ultimateFieldDuration", wood.FormBalance.UltimateFieldDuration);
                wood.FormBalance.ElementDamageBonusPercent = ReadValue(candidate, "ultimateElementBonusPercent", wood.FormBalance.ElementDamageBonusPercent);
                return true;
            }
            return false;
        }

        public static float ReadHitStop(CombatCandidate candidate, float fallback)
        {
            float value = ReadValue(candidate, HitStopId, fallback);
            RequireRange(value, 0f, 0.3f, HitStopId);
            return value;
        }

        public static float ReadSceneHitStop()
            => ReadScenePlayerSetting("_hitStopDuration", 0.25f);

        public static float ReadSceneHitStopTimeScale()
            => ReadScenePlayerSetting("_hitStopTimeScale", 0f);

        private static float ReadScenePlayerSetting(string name, float defaultValue)
        {
            var players = SceneComponents<PlayerController>().Where(p => p.isActiveAndEnabled).ToArray();
            if (players.Length == 0) return defaultValue; // PlayerController's declared defaults, for asset-only editing.
            if (players.Length > 1)
                throw new InvalidOperationException("히트스톱 기본값을 확인하려면 활성 PlayerController가 하나인 씬을 여세요.");
            var property = new SerializedObject(players[0]).FindProperty(name);
            if (property == null || property.propertyType != SerializedPropertyType.Float)
                throw new InvalidOperationException("PlayerController 히트스톱 구조가 변경되었습니다: " + name);
            return property.floatValue;
        }

        /// <summary>Matches PlayerController.StartHitStop: hit override, attack override, then player defaults.</summary>
        public static HitStopSettings EffectiveHitStop(WeaponActionData step, HitEvent hit,
            float playerDuration, float playerTimeScale)
        {
            var hitSettings = hit?.Feedback?.HitStop;
            if (hitSettings != null && hitSettings.OverrideHitStop) return hitSettings;
            var settings = step?.Feedback?.HitStop;
            if (settings != null && settings.OverrideHitStop) return settings;
            return new HitStopSettings { HitStopDuration = playerDuration, HitStopTimeScale = playerTimeScale };
        }

        public static float ReadSelectedHitStop(WeaponActionDataSO source, string actionId)
        {
            if (source == null) return ReadSceneHitStop(); // Legacy candidate validation overload.
            var step = SelectedSteps(source, actionId)?.FirstOrDefault();
            HitEvent hit = null;
            if (UsesHitEvents(source, actionId) && step?.HitEvents?.Count > 0)
                hit = source is WaterFormActionDataSO && NormalizeActionId(actionId) == "ultimate"
                    ? step.HitEvents[0] : step.HitEvents.Where(item => item != null).OrderBy(item => item.Execution.StartTime).FirstOrDefault();
            var settings = EffectiveHitStop(step, hit, -1f, 0f);
            return settings.HitStopDuration < 0f && !settings.OverrideHitStop ? ReadSceneHitStop() : settings.HitStopDuration;
        }

        private static void ApplyHitStop(WeaponActionDataSO clone, string actionId, float duration)
        {
            foreach (var step in SelectedSteps(clone, actionId))
            {
                var settings = step.Feedback.HitStop;
                // Materialize the inherited time scale only when creating an attack override.
                // Never edit the PlayerController: that would also affect unrelated attacks and parries.
                if (!settings.OverrideHitStop) settings.HitStopTimeScale = ReadSceneHitStopTimeScale();
                RequireRange(settings.HitStopTimeScale, 0f, 1f, "Feedback.HitStop.HitStopTimeScale");
                settings.OverrideHitStop = true;
                settings.HitStopDuration = duration;
                if (!UsesHitEvents(clone, actionId) || step.HitEvents == null) continue;
                int count = clone is WaterFormActionDataSO && NormalizeActionId(actionId) == "ultimate"
                    ? Math.Min(10, step.HitEvents.Count) : step.HitEvents.Count;
                for (int i = 0; i < count; i++)
                {
                    var hitSettings = step.HitEvents[i]?.Feedback?.HitStop;
                    if (hitSettings != null && hitSettings.OverrideHitStop) hitSettings.HitStopDuration = duration;
                }
            }
        }

        public static void ValidateCandidate(CombatCandidate candidate) => ValidateCandidate(candidate, null, null);
        public static void ValidateCandidate(CombatCandidate candidate, WeaponActionDataSO source, string actionId)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.id) || string.IsNullOrWhiteSpace(candidate.label))
                throw new ArgumentException("Each candidate needs an ID and label.");
            if (candidate.values == null) throw new ArgumentException("Candidate values are missing.");
            var specs = ParameterSpecs(source, actionId).ToDictionary(spec => spec.id, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in candidate.values)
            {
                if (item == null || string.IsNullOrEmpty(item.id) || !seen.Add(item.id))
                    throw new ArgumentException("Candidate contains a missing or duplicate parameter.");
                if (!specs.TryGetValue(item.id, out var spec)) throw new ArgumentException("Unsupported parameter for this action: " + item.id);
                RequireRange(item.value, spec.minimum, spec.maximum, item.id);
            }
            if (seen.Count != specs.Count) throw new ArgumentException("선택 동작의 모든 조절 항목을 정확히 한 번 지정하세요.");
        }

        private static WeaponActionDataSO ValidateRequestData(ExperimentRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.schemaVersion != 1 && request.schemaVersion != 2) throw new ArgumentException("Unsupported experiment schema version.");
            if (request.adapterId != AdapterId) throw new ArgumentException("Experiment adapter ID does not match this adapter.");
            RequireRange(request.durationSeconds, 1f, 60f, "durationSeconds");
            if (request.candidates == null || request.candidates.Count < 1 || request.candidates.Count > 12)
                throw new ArgumentException("Use between 1 and 12 candidates per experiment.");
            var source = ResolveSource(request.sourceGuid);
            ValidateSource(source, request.actionId);
            if (request.schemaVersion == 2) { CombatLab.ValidateData(request); return source; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in request.candidates)
            {
                ValidateCandidate(candidate, source, request.actionId);
                if (!ids.Add(candidate.id)) throw new ArgumentException("Candidate IDs must be unique.");
            }
            return source;
        }

        public static void ValidateSource(WeaponActionDataSO source, string actionId = null)
        {
            if (!IsSupported(source)) throw new ArgumentException("지원되는 폼 에셋이 아닙니다.");
            if (source.Actions == null || source.CommonBalance == null || source.CommonExecution == null || source.CommonFeedback == null)
                throw new ArgumentException("폼의 Actions / CommonBalance / CommonExecution / CommonFeedback 그룹이 필요합니다.");
            if (source is WaterFormActionDataSO waterGroups && (waterGroups.FormBalance == null || waterGroups.FormExecution == null)
                || source is FireFormActionDataSO fireGroups && (fireGroups.FormBalance == null || fireGroups.FormExecution == null)
                || source is EarthFormActionDataSO earthGroups && (earthGroups.FormBalance == null || earthGroups.FormExecution == null)
                || source is WoodFormActionDataSO woodGroups && (woodGroups.FormBalance == null || woodGroups.FormExecution == null))
                throw new ArgumentException("폼의 FormBalance / FormExecution 그룹이 필요합니다.");
            string id = NormalizeActionId(actionId);
            if (id == "heavy" && !(source is WaterFormActionDataSO))
                throw new ArgumentException("현재 게임에서 이 폼의 강공격은 구현되어 있지 않습니다.");
            var selected = SelectedSteps(source, id);
            if (IsBuffUltimate(source, id)) selected = source.Actions.ComboSteps; // Explicitly documented follow-up attack scenario.
            if (selected == null || selected.Count == 0) throw new ArgumentException("선택 동작에 설정된 공격 단계가 없습니다.");
            for (int i = 0; i < selected.Count; i++)
            {
                var step = selected[i];
                string prefix = ActionLabel(source, id) + " / " + (i + 1) + ": ";
                if (step == null) throw new ArgumentException(prefix + "공격 데이터가 비어 있습니다.");
                if (step.Balance == null || step.Execution == null || step.Execution.Hitbox == null
                    || step.Execution.Movement == null || step.Execution.Movement.TargetApproach == null
                    || step.Feedback == null || step.Feedback.HitStop == null || step.Feedback.Camera == null
                    || step.Feedback.Trail == null || step.Feedback.Slash == null || step.Feedback.HitEffect == null)
                    throw new ArgumentException(prefix + "Balance / Execution / Feedback 하위 그룹이 비어 있습니다.");
                if (id == "heavy" && string.IsNullOrWhiteSpace(step.Execution.AnimationName))
                    throw new ArgumentException(prefix + "강공격은 AnimationName이 필요합니다.");
                RequireRange(step.Execution.Duration, 0.001f, 60f, prefix + "Duration");
                if (id.StartsWith("normal-", StringComparison.Ordinal) || IsBuffUltimate(source, id))
                    RequireRange(step.Execution.ComboTransitionTime, 0f, step.Execution.Duration, prefix + "ComboTransitionTime");
                RequireRange(step.Balance.KnockbackForce, 0f, float.MaxValue, prefix + "KnockbackForce");
                RequireRange(step.Feedback.Camera.CameraShakeStrength, 0f, float.MaxValue, prefix + "CameraShakeStrength");
                if (step.Feedback.Camera.CameraProfile != null)
                    RequireRange(step.Feedback.Camera.CameraProfile.HitImpulse, 0f, float.MaxValue, prefix + "HitImpulse");
                ValidateHitStopSettings(step.Feedback.HitStop, prefix + "Feedback.HitStop");
                if (id == "ultimate" && source is WaterFormActionDataSO && step.HitEvents != null)
                    for (int hitIndex = 0; hitIndex < Math.Min(10, step.HitEvents.Count); hitIndex++)
                    {
                        var hit = step.HitEvents[hitIndex];
                        // Water permits missing hit entries and inherits the first step's settings.
                        if (hit == null) continue;
                        if (hit.Feedback?.HitStop == null)
                            throw new ArgumentException(prefix + "HitEvents[" + hitIndex + "].Feedback.HitStop 그룹이 비어 있습니다.");
                        ValidateHitStopSettings(hit.Feedback.HitStop, prefix + "HitEvents[" + hitIndex + "].Feedback.HitStop");
                    }
                bool customHit = (id == "skill" && (source is EarthFormActionDataSO || source is WoodFormActionDataSO))
                    || (id == "ultimate" && source is WaterFormActionDataSO);
                if (customHit) continue; // These implementations do not run the common step hit-window loop.
                if (UsesHitEvents(source, id) && step.HitEvents != null && step.HitEvents.Count > 0)
                {
                    foreach (var hit in step.HitEvents)
                    {
                        if (hit == null) throw new ArgumentException(prefix + "null hit event.");
                        if (hit.Balance == null || hit.Execution == null || hit.Feedback == null
                            || hit.Feedback.HitStop == null || hit.Feedback.HitEffect == null)
                            throw new ArgumentException(prefix + "HitEvents의 Balance / Execution / Feedback 그룹이 비어 있습니다.");
                        ValidateHitStopSettings(hit.Feedback.HitStop, prefix + "HitEvents.Feedback.HitStop");
                        ValidateHitWindow(hit.Execution.StartTime, hit.Execution.Duration, step.Execution.Duration, prefix);
                        if (hit.Balance.OverrideKnockbackForce)
                            RequireRange(hit.Balance.KnockbackForce, 0f, float.MaxValue, prefix + "hit KnockbackForce");
                    }
                }
                else ValidateHitWindow(step.Execution.HitStartTime, step.Execution.HitDuration, step.Execution.Duration, prefix);
            }
            if (id == "skill" && source is WoodFormActionDataSO wood)
            {
                RequireRange(wood.FormExecution.SkillRadius, 0.1f, float.MaxValue, "SkillRadius");
                RequireRange(wood.FormExecution.PullSpeed, 0f, float.MaxValue, "PullSpeed");
                RequireRange(wood.FormBalance.SkillDamageMultiplier, 0f, float.MaxValue, "SkillDamageMultiplier");
            }
            if (id == "ultimate" && source is WaterFormActionDataSO water)
                RequireRange(water.FormExecution.AreaRadius, 0.1f, float.MaxValue, "AreaRadius");
        }

        private static void ValidateHitStopSettings(HitStopSettings settings, string prefix)
        {
            if (!settings.OverrideHitStop) return;
            RequireRange(settings.HitStopDuration, 0f, float.MaxValue, prefix + ".HitStopDuration");
            RequireRange(settings.HitStopTimeScale, 0f, 1f, prefix + ".HitStopTimeScale");
        }

        private static void ValidateHitWindow(float start, float duration, float attackDuration, string prefix)
        {
            RequireRange(start, 0f, attackDuration, prefix + "hit start");
            // BaseForm.ProcessHit clips observation to the attack duration. Existing skills
            // intentionally author longer windows; ending after the step is valid gameplay data.
            RequireRange(duration, 0.0001f, 60f, prefix + "hit duration");
        }

        private static void ValidateScene(WeaponActionDataSO source, string actionId, bool fixedScenario = false)
        {
            if (InputSystem.settings.updateMode != InputSettings.UpdateMode.ProcessEventsInDynamicUpdate)
                throw new InvalidOperationException("Combat playback requires Input System events in Dynamic Update.");
            var players = SceneComponents<PlayerController>().Where(p => p.isActiveAndEnabled).ToArray();
            if (players.Length != 1) throw new InvalidOperationException("The loaded combat scene must have exactly one active PlayerController.");
            var player = players[0];
            var animator = player.GetComponentInChildren<Animator>();
            var controller = player.GetComponent<CharacterController>();
            var input = player.GetComponent<PlayerInputHandler>();
            if (animator == null || !animator.enabled || animator.runtimeAnimatorController == null
                || controller == null || !controller.enabled || input == null || !input.enabled
                || player.GetComponent<PlayerStatManager>() == null)
                throw new InvalidOperationException("Player needs an enabled Animator, CharacterController, PlayerInputHandler and PlayerStatManager.");
            var serializedInput = new SerializedObject(input);
            string id = NormalizeActionId(actionId);
            var requiredInputs = new List<string> { "_moveAction", "_attackAction" };
            if (id == "skill") requiredInputs.Add("_skillAction");
            if (id == "ultimate") requiredInputs.Add("_ultimateAction");
            foreach (string name in requiredInputs)
            {
                var reference = serializedInput.FindProperty(name)?.objectReferenceValue as InputActionReference;
                if (reference == null || reference.action == null)
                    throw new InvalidOperationException("Player input is missing " + name + ".");
            }
            int groundLayer = LayerMask.NameToLayer("Ground");
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (groundLayer < 0 || enemyLayer < 0)
                throw new InvalidOperationException("The project needs Ground and Enemy layers.");
            ValidateGroundColliders(SceneComponents<Collider>(), groundLayer, fixedScenario, Application.isPlaying,
                SceneComponents<RunManager>().Any(run => run.isActiveAndEnabled));
            var settings = SceneComponents<CombatSettings>().Where(s => s.isActiveAndEnabled).ToArray();
            if (settings.Length != 1 || settings[0].ElementTable == null || !settings[0].ElementTable.ValidateTable())
                throw new InvalidOperationException("The scene needs one CombatSettings with a valid element matchup table.");
            var enemies = SceneComponents<Enemy>().Where(e => e.isActiveAndEnabled).ToArray();
            if (fixedScenario)
            {
                // Dungeon enemies can be instantiated after Play Mode starts. Pool
                // entries with disabled colliders are not the fixed experiment's target.
                if (Application.isPlaying && !enemies.Any(IsFixedTargetReady))
                    throw new InvalidOperationException("고정 표적 준비 대기: 살아 있고 Enemy 콜라이더·EnemyStat 데이터·NavMeshAgent가 활성화되어 NavMesh 위에 있는 적이 필요합니다.");
            }
            else
            {
                if (enemies.Length == 0) throw new InvalidOperationException("The scene needs at least one active Enemy.");
                foreach (var enemy in enemies)
                {
                    var stat = enemy.GetComponent<EnemyStat>();
                    var agent = enemy.GetComponent<NavMeshAgent>();
                    if (stat == null || new SerializedObject(stat).FindProperty("baseData")?.objectReferenceValue == null
                        || agent == null || !agent.enabled)
                        throw new InvalidOperationException("Enemy " + enemy.name + " needs configured EnemyStat and enabled NavMeshAgent.");
                    if (!enemy.GetComponentsInChildren<Collider>().Any(c => c.enabled && c.gameObject.layer == enemyLayer))
                        throw new InvalidOperationException("Enemy " + enemy.name + " needs a collider on the Enemy layer.");
                    if (Application.isPlaying && (!agent.isOnNavMesh || stat.IsDead))
                        throw new InvalidOperationException("Enemy " + enemy.name + " must be alive and on the NavMesh.");
                }
            }
            if (!Application.isPlaying) return;
            if (player.FormManager == null || player.Input == null || player.Animator == null || player.StatManager == null
                || player.StateMachine == null || !player.IsGround())
                throw new InvalidOperationException("Player must be initialized and grounded before starting the experiment.");
            if (!(player.StateMachine.CurrentState is PlayerIdleState) && !(player.StateMachine.CurrentState is PlayerRunState))
                throw new InvalidOperationException("Player must be idle or running before starting the experiment.");
            // Unassigned animations are valid in this project's current forms: BaseForm deliberately
            // skips them while still executing hit logic. Report this limitation in the session.
            if (CombatSettings.Active != settings[0])
                throw new InvalidOperationException("The scene's CombatSettings has not registered as active.");
        }

        internal static void ValidateGroundColliders(IEnumerable<Collider> colliders, int groundLayer,
            bool fixedScenario, bool playing, bool hasRuntimeRunManager)
        {
            // RunManager creates the dungeon floor after entering Play Mode. Only
            // its fixed-v2 Edit Mode preflight may defer this check. IsReady and
            // Begin call it again in Play Mode before recording or arming an attack.
            bool runtimeFloorPending = fixedScenario && !playing && hasRuntimeRunManager;
            if (!runtimeFloorPending && !colliders.Any(c => c != null && c.enabled && c.gameObject.activeInHierarchy
                    && !c.isTrigger && c.gameObject.layer == groundLayer))
                throw new InvalidOperationException("The loaded scene needs an active Ground collider.");
        }

        internal static bool IsFixedTargetReady(Enemy enemy)
        {
            if (enemy == null || !enemy.isActiveAndEnabled || !enemy.gameObject.scene.IsValid()
                || !enemy.gameObject.scene.isLoaded) return false;
            // A nested Enemy cannot be isolated by deactivating the other Enemy roots.
            if (enemy.transform.parent != null && enemy.transform.parent.GetComponentInParent<Enemy>() != null) return false;
            var stat = enemy.GetComponent<EnemyStat>();
            var agent = enemy.GetComponent<NavMeshAgent>();
            if (stat == null || new SerializedObject(stat).FindProperty("baseData")?.objectReferenceValue == null
                || agent == null || !agent.enabled) return false;
            int layer = LayerMask.NameToLayer("Enemy");
            if (layer < 0 || !enemy.GetComponentsInChildren<Collider>().Any(c => c.enabled
                && c.gameObject.activeInHierarchy && c.gameObject.layer == layer
                && c.GetComponentInParent<Enemy>() == enemy)) return false;
            return !Application.isPlaying || (enemy.EnemyStat == stat && stat.CurrentHp > 0f && !stat.IsDead && agent.isOnNavMesh);
        }

        private static T[] SceneComponents<T>() where T : Component
            => Object.FindObjectsByType<T>(FindObjectsSortMode.None)
                .Where(item => item.gameObject.scene.IsValid() && item.gameObject.scene.isLoaded).ToArray();

        public static void ValidateExportPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                || assetPath.IndexOfAny(new[] { '\\', ':', '*', '?', '"', '<', '>', '|', '\r', '\n' }) >= 0
                || assetPath.Split('/').Any(part => string.IsNullOrWhiteSpace(part) || part == "." || part == ".."))
                throw new ArgumentException("Choose a new .asset path under Assets with no traversal segments.");
            string folder = assetPath.Substring(0, assetPath.LastIndexOf('/'));
            if (!AssetDatabase.IsValidFolder(folder)) throw new ArgumentException("The export folder does not exist: " + folder);
            string project = Directory.GetParent(Application.dataPath).FullName;
            string fullPath = Path.GetFullPath(Path.Combine(project, assetPath));
            string assetsPrefix = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase)
                || File.Exists(fullPath) || Directory.Exists(fullPath) || File.Exists(fullPath + ".meta")
                || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)))
                throw new ArgumentException("Export only creates a new asset under Assets; the destination must not exist.");
        }

        private static float ReadValue(CombatCandidate candidate, string id, float fallback)
        {
            if (candidate?.values != null)
                foreach (var item in candidate.values)
                    if (item != null && item.id == id) return item.value;
            return fallback;
        }

        private static CombatCandidate CopyCandidate(CombatCandidate source)
            => JsonUtility.FromJson<CombatCandidate>(JsonUtility.ToJson(source));

        private static float Scale(float value, float multiplier)
        {
            float result = value * multiplier;
            if (float.IsNaN(result) || float.IsInfinity(result)) throw new ArgumentException("A scaled parameter overflows.");
            return result;
        }

        private static void RequireRange(float value, float minimum, float maximum, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
                throw new ArgumentException(name + " must be finite and between " + minimum + " and " + maximum + ".");
        }
    }
}
