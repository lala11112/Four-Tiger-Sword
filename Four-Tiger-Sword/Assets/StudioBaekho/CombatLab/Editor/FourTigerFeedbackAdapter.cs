using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioBaekho.CombatLab.FourTigerSword
{
    public sealed partial class FourTigerCombatAdapter
    {
        private sealed class Binding
        {
            public ParameterSpec spec;
            public CurveSpec curve;
            public Action<float> set;
            public Action<AnimationCurve> setCurve;
            public Action activate;
        }

        public List<ParameterSpec> DescribeFeedback(string sourceGuid, string actionId)
            => DescribeBindings(sourceGuid, actionId).Where(b => b.spec != null).Select(b => b.spec).ToList();
        public List<CurveSpec> DescribeCurves(string sourceGuid, string actionId)
            => DescribeBindings(sourceGuid, actionId).Where(b => b.curve != null).Select(b => b.curve).ToList();
        public static Dictionary<string, float> AppliedFeedbackValues(WeaponActionDataSO source, string actionId)
            => FeedbackBindings(source, actionId).Where(b => b.spec != null).ToDictionary(b => b.spec.id, b => b.spec.baseline);

        private static List<Binding> DescribeBindings(string guid, string actionId)
        {
            var source = ResolveSource(guid);
            ValidateSource(source, actionId);
            var copy = Object.Instantiate(source);
            try { return FeedbackBindings(copy, actionId); }
            finally { Object.DestroyImmediate(copy); }
        }

        [Serializable] private sealed class FeedbackContext
        {
            public string actionId, form, semantics = "Absolute values. Omitted channels preserve source; original curves are not resampled.";
            public string sourceCamera = "Inline v2 or inherited AttackCameraProfile/Feel. Tool shake uses its independent mixer, without DefaultVelocity multiplication.";
            public string executionNote;
            public string[] supportedScopes = { "Global", "Attacker", "Victim", "Both" };
            public List<StepContext> steps = new List<StepContext>();
        }
        [Serializable] private sealed class StepContext
        {
            public string targetId, animation, cameraProfile;
            public float duration;
            public float legacyImpulse, legacyCameraShakeStrength;
            public bool legacyFeelAssigned;
            public List<HitContext> hits = new List<HitContext>();
        }
        [Serializable] private sealed class HitContext
        {
            public string targetId;
            public float start, duration, effectiveHitStop, effectiveTimeScale;
            public bool inherited;
        }
        public string DescribeContext(string sourceGuid, string actionId)
        {
            var source = ResolveSource(sourceGuid);
            var context = new FeedbackContext { actionId = NormalizeActionId(actionId), form = source.GetType().Name };
            context.executionNote = IsBuffUltimate(source, actionId)
                ? "Buff ultimate: no direct hit/camera authoring channel. The experiment follows with the unchanged normal combo. Tune that combo as its own action."
                : "Only actual supported hit-event targets are exposed. Special actions keep their original timing.";
            var steps = FeedbackSteps(source, actionId);
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var item = new StepContext { targetId = "step." + i, animation = step.Execution.AnimationName,
                    duration = step.Execution.Duration, cameraProfile = step.Feedback.Camera.CameraProfile?.name,
                    legacyImpulse = step.Feedback.Camera.CameraProfile?.HitImpulse ?? 0,
                    legacyCameraShakeStrength = step.Feedback.Camera.CameraShakeStrength,
                    legacyFeelAssigned = step.Feedback.Camera.CameraProfile?.FeelFeedbackPrefab != null };
                var hits = step.HitEvents;
                bool waterUltimate = source is WaterFormActionDataSO && NormalizeActionId(actionId) == "ultimate";
                var indices = FeedbackHitIndices(source, actionId, step).ToList();
                if (indices.Count == 0) indices.Add(-1);
                foreach (int h in indices)
                {
                    var hit = hits != null && h >= 0 && h < hits.Count ? hits[h] : null;
                    var effective = EffectiveFeedbackStop(source, actionId, step, hit);
                    item.hits.Add(new HitContext { targetId = item.targetId + (hit == null && !waterUltimate ? "" : ".hit." + h),
                        start = waterUltimate ? (h + 1) * .2f : hit?.Execution?.StartTime ?? step.Execution.HitStartTime,
                        duration = waterUltimate ? 0 : hit?.Execution?.Duration ?? step.Execution.HitDuration,
                        effectiveHitStop = effective.HitStopDuration, effectiveTimeScale = effective.HitStopTimeScale,
                        inherited = !(hit?.Feedback?.HitStop?.OverrideHitStop ?? step.Feedback.HitStop.OverrideHitStop) });
                }
                context.steps.Add(item);
            }
            return JsonUtility.ToJson(context);
        }

        private static List<WeaponActionData> FeedbackSteps(WeaponActionDataSO source, string actionId)
            => SelectedSteps(source, actionId);
        public static int ExpectedFeedbackHits(WeaponActionDataSO source, string actionId)
        {
            if (IsBuffUltimate(source, actionId)) actionId = "normal-combo";
            return SelectedSteps(source, actionId).Sum(step => Math.Max(1, FeedbackHitIndices(source, actionId, step).Count()));
        }

        private static IEnumerable<int> FeedbackHitIndices(WeaponActionDataSO source, string actionId, WeaponActionData step)
        {
            string action = NormalizeActionId(actionId);
            if (action == "heavy") return Enumerable.Empty<int>();
            if (action == "ultimate" && source is WaterFormActionDataSO) return Enumerable.Range(0, 10);
            var indices = Enumerable.Range(0, step.HitEvents?.Count ?? 0).Where(i => step.HitEvents[i] != null);
            if (action == "skill" && (source is EarthFormActionDataSO || source is WoodFormActionDataSO))
                return indices.OrderBy(i => step.HitEvents[i].Execution.StartTime).Take(1);
            return indices;
        }

        private static HitStopSettings EffectiveFeedbackStop(WeaponActionDataSO source, string actionId, WeaponActionData step, HitEvent hit)
        {
            if (NormalizeActionId(actionId) == "skill")
            {
                if (source is WoodFormActionDataSO && !step.Feedback.HitStop.EnableV2 && !(hit?.Feedback.HitStop.EnableV2 ?? false))
                    return new HitStopSettings { HitStopDuration = 0, HitStopTimeScale = 0 };
                if (source is EarthFormActionDataSO && !(hit?.Feedback.HitStop.EnableV2 ?? false)) hit = null;
            }
            return EffectiveHitStop(step, hit, ReadSceneHitStop(), ReadSceneHitStopTimeScale());
        }

        private static List<Binding> FeedbackBindings(WeaponActionDataSO source, string actionId)
        {
            var bindings = new List<Binding>();
            var steps = FeedbackSteps(source, actionId);
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                string target = "step." + i;
                Number(bindings, target + ".knockback", target, "전투", "넉백 세기", step.Balance.KnockbackForce,
                    v => { step.Balance.EnableV2 = true; step.Balance.KnockbackForce = v; }, 0, float.MaxValue, 0, 12, "게임 값");
                HitStopBindings(bindings, target, step.Feedback.HitStop,
                    EffectiveFeedbackStop(source, actionId, step, null));
                var camera = MaterializeCamera(step.Feedback.Camera);
                CameraBindings(bindings, target, camera, () => { camera.Authored = true; step.Feedback.Camera.V2 = camera; }, true);
                bool waterUltimate = source is WaterFormActionDataSO && NormalizeActionId(actionId) == "ultimate";
                foreach (int h in FeedbackHitIndices(source, actionId, step))
                {
                    int hitIndex = h;
                    var hit = step.HitEvents != null && h < step.HitEvents.Count ? step.HitEvents[h] : null;
                    bool virtualHit = hit == null && waterUltimate;
                    if (virtualHit) hit = new HitEvent();
                    if (hit?.Feedback == null) continue;
                    Action enableHit = () => {
                        if (virtualHit) {
                            if (step.HitEvents == null) step.HitEvents = new List<HitEvent>();
                            while (step.HitEvents.Count <= hitIndex) step.HitEvents.Add(null);
                            step.HitEvents[hitIndex] = hit;
                        }
                    };
                    string hitTarget = target + ".hit." + h;
                    HitStopBindings(bindings, hitTarget, hit.Feedback.HitStop,
                        EffectiveFeedbackStop(source, actionId, step, hit), enableHit);
                    if (hit.Balance != null)
                    {
                        var balance = hit.Balance;
                        Number(bindings, hitTarget + ".knockback", hitTarget, "전투", "타격 넉백", balance.OverrideKnockbackForce ? balance.KnockbackForce : step.Balance.KnockbackForce,
                            v => { balance.EnableV2 = true; balance.OverrideKnockbackForce = true; balance.KnockbackForce = v; }, 0, float.MaxValue, 0, 12, "게임 값", enableHit);
                    }
                    var hitCamera = MaterializeHitCamera(hit.Feedback.CameraV2, camera);
                    CameraBindings(bindings, hitTarget, hitCamera, () => { enableHit(); hitCamera.Authored = true; hit.Feedback.CameraV2 = hitCamera; }, false);
                }
            }
            string action = NormalizeActionId(actionId);
            if (action == "skill" && source is WoodFormActionDataSO wood)
            {
                Number(bindings, "form.skillRadius", "form", "전투", "스킬 반경", wood.FormExecution.SkillRadius, v => wood.FormExecution.SkillRadius = v, .1f, float.MaxValue, .5f, 10, "m");
                Number(bindings, "form.pullSpeed", "form", "전투", "끌어당김 속도", wood.FormExecution.PullSpeed, v => wood.FormExecution.PullSpeed = v, 0, float.MaxValue, 0, 15, "m/s");
                Number(bindings, "form.skillDamage", "form", "전투", "스킬 피해 계수", wood.FormBalance.SkillDamageMultiplier, v => wood.FormBalance.SkillDamageMultiplier = v, 0, float.MaxValue, 0, 5, "계수");
            }
            if (action == "ultimate")
            {
                if (source is WaterFormActionDataSO water)
                    Number(bindings, "form.areaRadius", "form", "전투", "궁극기 반경", water.FormExecution.AreaRadius, v => water.FormExecution.AreaRadius = v, .1f, float.MaxValue, .5f, 15, "m");
                if (source is FireFormActionDataSO fire)
                {
                    Number(bindings, "form.damageBonus", "form", "전투", "피해 증가", fire.FormBalance.FireUltimateDamageDealtBonusPercent, v => fire.FormBalance.FireUltimateDamageDealtBonusPercent = v, 0, float.MaxValue, 0, 100, "%");
                    Number(bindings, "form.attackSpeedBonus", "form", "전투", "공격속도 증가", fire.FormBalance.FireUltimateAttackSpeedBonusPercent, v => fire.FormBalance.FireUltimateAttackSpeedBonusPercent = v, 0, float.MaxValue, 0, 100, "%");
                    Number(bindings, "form.damageTakenBonus", "form", "전투", "받는 피해 증가", fire.FormBalance.FireUltimateDamageTakenBonusPercent, v => fire.FormBalance.FireUltimateDamageTakenBonusPercent = v, 0, float.MaxValue, 0, 100, "%");
                }
                if (source is EarthFormActionDataSO earth)
                {
                    Number(bindings, "form.shield", "form", "전투", "보호막", earth.FormBalance.UltimateShieldAmount, v => earth.FormBalance.UltimateShieldAmount = v, 0, float.MaxValue, 0, 200, "HP");
                    Number(bindings, "form.buffDuration", "form", "전투", "버프 시간", earth.FormBalance.UltimateBuffDuration, v => earth.FormBalance.UltimateBuffDuration = v, 0, float.MaxValue, 1, 20, "s");
                    Number(bindings, "form.damageBonus", "form", "전투", "보호막 피해 증가", earth.FormBalance.UltimateShieldDamageBonusPercent, v => earth.FormBalance.UltimateShieldDamageBonusPercent = v, 0, float.MaxValue, 0, 100, "%");
                }
                if (source is WoodFormActionDataSO tree)
                {
                    Number(bindings, "form.fieldRadius", "form", "전투", "영역 반경", tree.FormExecution.UltimateRadius, v => tree.FormExecution.UltimateRadius = v, 0, float.MaxValue, 1, 15, "m");
                    Number(bindings, "form.fieldDuration", "form", "전투", "영역 시간", tree.FormBalance.UltimateFieldDuration, v => tree.FormBalance.UltimateFieldDuration = v, 0, float.MaxValue, 1, 20, "s");
                    Number(bindings, "form.elementBonus", "form", "전투", "속성 피해 증가", tree.FormBalance.ElementDamageBonusPercent, v => tree.FormBalance.ElementDamageBonusPercent = v, 0, float.MaxValue, 0, 100, "%");
                }
            }
            return bindings;
        }

        private static CombatCameraV2Settings MaterializeCamera(AttackCameraSettings camera)
        {
            var profile = camera.CameraProfile;
            // Match BaseForm.Camera: an inline channel override selects the inline
            // settings; otherwise an opted-in profile supplies the v2 settings.
            var settings = CloneCamera(camera.V2?.HasOverrides == true ? camera.V2
                : profile != null && profile.EnableV2 ? profile.V2 : camera.V2);
            var inherited = profile != null && profile.EnableV2 ? profile.V2 : null;
            if (camera.V2?.HasOverrides == true && inherited != null)
            {
                var profileCopy = CloneCamera(inherited);
                if (settings.Mode == CombatFeedbackChannelMode.Inherit) settings.Motion = profileCopy.Motion;
                if (settings.Impact.Mode == CombatFeedbackChannelMode.Inherit)
                { settings.Impact = profileCopy.Impact; settings.Impact.Mode = CombatFeedbackChannelMode.Inherit; }
                if (settings.Shake.Mode == CombatFeedbackChannelMode.Inherit)
                { settings.Shake = profileCopy.Shake; settings.Shake.Mode = CombatFeedbackChannelMode.Inherit; }
            }
            var effective = CombatCameraV2Settings.Resolve(profile, camera.V2);
            if (profile == null || effective?.Mode == CombatFeedbackChannelMode.Override
                || effective?.Mode == CombatFeedbackChannelMode.Off) return settings;
            // Inherit applies per channel. Even an inline shake override must keep
            // the effective legacy attack motion as the starting point for an edit.
            var motion = settings.Motion;
            motion.FovOffset = CopyCurve(profile.FovOffset);
            motion.FramingWeight = CopyCurve(profile.FramingWeight);
            motion.TargetWeight = profile.TargetWeight;
            motion.MaxTargetDistance = profile.MaxTargetDistance;
            motion.MaxFramingOffset = profile.MaxFramingOffset;
            motion.DirectionLookAhead = profile.DirectionLookAhead;
            motion.ResponseTime = profile.ResponseTime;
            motion.ReturnDuration = profile.ReturnDuration;
            motion.CancelDuration = profile.CancelDuration;
            motion.PreserveChainViewDirection = profile.PreserveChainViewDirection;
            motion.UseFeelZoom = profile.FeelFeedbackPrefab != null && profile.FeelFeedbackPrefab.IsConfigured;
            motion.Clock = CombatFeedbackClock.World;
            motion.ReturnClock = CombatFeedbackClock.World;
            motion.Space = CombatFeedbackSpace.Camera;
            // Same original envelope, including its tangents, until explicitly replaced.
            motion.PositionX = ScaleCurve(profile.FramingWeight, profile.CameraLocalOffset.x);
            motion.PositionY = ScaleCurve(profile.FramingWeight, profile.CameraLocalOffset.y);
            motion.PositionZ = ScaleCurve(profile.FramingWeight, profile.CameraLocalOffset.z);
            return settings;
        }

        private static CombatCameraHitSettings MaterializeHitCamera(CombatCameraHitSettings authored, CombatCameraV2Settings step)
        {
            var settings = CloneHitCamera(authored);
            if (settings.Impact.Mode == CombatFeedbackChannelMode.Inherit)
            {
                var inherited = CloneCamera(step);
                settings.Impact = inherited.Impact;
                settings.Impact.Mode = CombatFeedbackChannelMode.Inherit;
            }
            if (settings.Shake.Mode == CombatFeedbackChannelMode.Inherit)
            {
                var inherited = CloneCamera(step);
                settings.Shake = inherited.Shake;
                settings.Shake.Mode = CombatFeedbackChannelMode.Inherit;
            }
            if (authored?.HasOverrides != true)
            {
                settings.TriggerPolicy = step.TriggerPolicy;
                settings.PreserveFeelFlash = step.PreserveFeelFlash;
            }
            return settings;
        }

        private static CombatCameraHitSettings CloneHitCamera(CombatCameraHitSettings source)
        {
            if (source == null) return new CombatCameraHitSettings();
            var copy = JsonUtility.FromJson<CombatCameraHitSettings>(JsonUtility.ToJson(source));
            if (copy.Impact == null) copy.Impact = new CombatCameraImpactV2();
            if (copy.Shake == null) copy.Shake = new CombatCameraShakeV2();
            CopyCurveFields(source.Impact, copy.Impact);
            CopyCurveFields(source.Shake, copy.Shake);
            return copy;
        }

        private static CombatCameraV2Settings CloneCamera(CombatCameraV2Settings source)
        {
            if (source == null) return new CombatCameraV2Settings();
            var copy = JsonUtility.FromJson<CombatCameraV2Settings>(JsonUtility.ToJson(source));
            if (copy.Motion == null) copy.Motion = new CombatCameraMotionV2();
            if (copy.Impact == null) copy.Impact = new CombatCameraImpactV2();
            if (copy.Shake == null) copy.Shake = new CombatCameraShakeV2();
            CopyCurveFields(source.Motion, copy.Motion);
            CopyCurveFields(source.Impact, copy.Impact);
            CopyCurveFields(source.Shake, copy.Shake);
            return copy;
        }

        private static void CopyCurveFields(object source, object destination)
        {
            if (source == null) return;
            foreach (var field in source.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (field.FieldType == typeof(AnimationCurve)) field.SetValue(destination, CopyCurve((AnimationCurve)field.GetValue(source)));
        }

        private static AnimationCurve CopyCurve(AnimationCurve original)
            => original == null ? null : new AnimationCurve(original.keys)
                { preWrapMode = original.preWrapMode, postWrapMode = original.postWrapMode };

        private static AnimationCurve ScaleCurve(AnimationCurve original, float scale)
        {
            if (original == null) return AnimationCurve.Linear(0, 0, 1, 0);
            var keys = original.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].value *= scale;
                // A stepped source curve has infinite tangents. 0 * infinity is
                // NaN, although a zero offset must be a perfectly flat channel.
                keys[i].inTangent = scale == 0 ? 0 : keys[i].inTangent * scale;
                keys[i].outTangent = scale == 0 ? 0 : keys[i].outTangent * scale;
            }
            return new AnimationCurve(keys) { preWrapMode = original.preWrapMode, postWrapMode = original.postWrapMode };
        }

        private static void HitStopBindings(List<Binding> list, string target, HitStopSettings settings, HitStopSettings effective, Action enable = null)
        {
            string prefix = target + ".hitstop";
            float duration = effective.HitStopDuration, timeScale = effective.HitStopTimeScale;
            bool initialized = settings.OverrideHitStop;
            Action activate = () => {
                enable?.Invoke();
                settings.EnableV2 = true;
                if (!initialized) {
                    settings.HitStopDuration = duration; settings.HitStopTimeScale = timeScale;
                    settings.Scope = effective.Scope; settings.Stacking = effective.Stacking; initialized = true;
                }
                settings.OverrideHitStop = true;
            };
            Number(list, prefix + ".mode", target, "히트스톱", "적용 방식", settings.OverrideHitStop ? duration == 0 ? 1 : 2 : 0,
                v => { settings.EnableV2 = true; settings.OverrideHitStop = v != 0; if (v == 1) settings.HitStopDuration = 0; }, 0, 2, 0, 2, "", enable,
                new[] { "원본 유지", "끄기", "직접 설정" });
            Number(list, prefix + ".duration", target, "히트스톱", "정지·감속 시간", duration, v => settings.HitStopDuration = v, 0, float.MaxValue, 0, .15f, "s", activate);
            Number(list, prefix + ".timeScale", target, "히트스톱", "시간 배율", timeScale, v => settings.HitStopTimeScale = v, 0, 1, 0, 1, "", activate);
            Number(list, prefix + ".scope", target, "히트스톱", "적용 대상", (float)effective.Scope, v => settings.Scope = (HitStopScope)(int)v, 0, 3, 0, 3, "", activate, new[] { "전체 게임", "공격자", "피격자", "양쪽" });
            Number(list, prefix + ".stacking", target, "히트스톱", "중첩", (float)effective.Stacking, v => settings.Stacking = (HitStopStacking)(int)v, 0, 2, 0, 2, "", activate, new[] { "교체", "연장", "더 느리게·더 늦게 종료" });
        }

        private static void CameraBindings(List<Binding> list, string target, CombatCameraHitSettings camera, Action enable, bool motion)
        {
            string prefix = target + ".camera";
            if (motion)
            {
                var attackCamera = (CombatCameraV2Settings)camera;
                Number(list, prefix + ".mode", target, "카메라 연출", "공격 진행 연출", (float)attackCamera.Mode, v => attackCamera.Mode = (CombatFeedbackChannelMode)(int)v, 0, 2, 0, 2, "", enable, new[] { "원본 유지", "끄기", "직접 설정" });
                Fields(list, attackCamera.Motion, prefix + ".motion", target, "카메라 연출", () => { enable(); attackCamera.Mode = CombatFeedbackChannelMode.Override; });
            }
            Fields(list, camera.Impact, prefix + ".impact", target, "카메라 명중", () => { enable(); camera.Impact.Mode = CombatFeedbackChannelMode.Override; }, enable);
            Fields(list, camera.Shake, prefix + ".shake", target, "화면 흔들림", () => { enable(); camera.Shake.Mode = CombatFeedbackChannelMode.Override; }, enable);
            Number(list, prefix + ".triggerPolicy", target, "카메라 명중", "중복 단위", (float)camera.TriggerPolicy, v => camera.TriggerPolicy = (CombatFeedbackTriggerPolicy)(int)v, 0, 2, 0, 2, "", enable, new[] { "단계당 한 번", "타격당 한 번", "피격자마다" });
            Number(list, prefix + ".preserveFeelFlash", target, "카메라 명중", "Feel 명중 효과 유지", camera.PreserveFeelFlash ? 1 : 0,
                v => camera.PreserveFeelFlash = v == 1, 0, 1, 0, 1, "", enable, new[] { "아니요", "예" }, "boolean");
        }

        // Only these known settings types are reflected. IDs are generated by this adapter,
        // never accepted as arbitrary reflection paths from a request.
        private static void Fields(List<Binding> list, object owner, string prefix, string target, string group, Action activate, Action modeActivate = null)
        {
            foreach (var field in owner.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                string id = prefix + "." + char.ToLowerInvariant(field.Name[0]) + field.Name.Substring(1);
                var value = field.GetValue(owner);
                Action onEdit = field.Name == "Mode" ? modeActivate : activate;
                if (field.FieldType == typeof(AnimationCurve))
                {
                    var curve = (AnimationCurve)value;
                    var spec = new CurveSpec { id = id, label = field.Name, targetId = target, group = group,
                        timeUnit = prefix.EndsWith("motion") ? "attack-progress" : "effect-progress",
                        description = "편집한 곡선만 최대 16키 선형 곡선으로 교체합니다. 기존 곡선의 키/탄젠트는 미편집 시 보존됩니다." };
                    if (curve != null) spec.keys = curve.keys.Select(k => new CurveKey(k.time, k.value)).ToList();
                    Action editCurve = () => {
                        activate?.Invoke();
                        // An explicitly edited FOV curve owns zoom. Preserve Feel's
                        // hit/flash player while leaving its zoom player inactive.
                        if (owner is CombatCameraMotionV2 m && field.Name == nameof(CombatCameraMotionV2.FovOffset)) m.UseFeelZoom = false;
                    };
                    list.Add(new Binding { curve = spec, setCurve = c => field.SetValue(owner, c), activate = editCurve });
                }
                else if (field.FieldType.IsEnum)
                {
                    string[] names = Enum.GetNames(field.FieldType);
                    Number(list, id, target, group, field.Name, Convert.ToSingle(value), v => field.SetValue(owner, Enum.ToObject(field.FieldType, (int)v)), 0, names.Length - 1, 0, names.Length - 1, "", onEdit, names);
                }
                else if (field.FieldType == typeof(bool))
                    Number(list, id, target, group, field.Name, (bool)value ? 1 : 0, v => field.SetValue(owner, v == 1), 0, 1, 0, 1, "", onEdit, new[] { "아니요", "예" }, "boolean");
                else if (field.FieldType == typeof(Vector3))
                    for (int axis = 0; axis < 3; axis++)
                    {
                        int a = axis;
                        Number(list, id + "." + "xyz"[a], target, group, field.Name + " " + "XYZ"[a], ((Vector3)value)[a], v => { var vector = (Vector3)field.GetValue(owner); vector[a] = v; field.SetValue(owner, vector); }, -float.MaxValue, float.MaxValue, field.Name == "Rotation" ? -2 : -.1f, field.Name == "Rotation" ? 2 : .1f, field.Name == "Rotation" ? "deg" : "m", onEdit);
                    }
                else if (field.FieldType == typeof(float))
                {
                    bool seconds = field.Name.Contains("Duration") || field.Name.Contains("Delay") || field.Name.Contains("Time");
                    bool weight = field.Name == "TargetWeight";
                    float maximum = float.MaxValue;
                    Number(list, id, target, group, field.Name, (float)value, v => field.SetValue(owner, v), 0, maximum, 0,
                        weight ? 1 : seconds ? .6f : field.Name == "Frequency" ? 60 : 10, seconds ? "s" : field.Name == "Frequency" ? "Hz" : "", onEdit);
                }
                // Seed is deterministic experiment metadata, not an unconstrained AI parameter.
            }
        }
        private static void Number(List<Binding> list, string id, string target, string group, string label, float baseline, Action<float> setter,
            float min, float max, float suggestedMin, float suggestedMax, string unit, Action activate = null, string[] options = null, string kind = null)
            => list.Add(new Binding { spec = new ParameterSpec { id = id, targetId = target, group = group, label = label, baseline = baseline,
                minimum = min, maximum = max, useSuggestedRange = true, suggestedMinimum = suggestedMin, suggestedMaximum = suggestedMax,
                unit = unit, kind = kind ?? (options == null ? "number" : "enum"), options = options ?? new string[0],
                description = "실제 필드에 설정할 절대값. 추천 탐색 범위는 상한이 아닙니다." }, set = setter, activate = activate });

        public static void ApplyFeedback(WeaponActionDataSO clone, CombatCandidate candidate, string actionId)
        {
            var bindings = FeedbackBindings(clone, actionId);
            var numbers = bindings.Where(b => b.spec != null).ToDictionary(b => b.spec.id);
            var curves = bindings.Where(b => b.curve != null).ToDictionary(b => b.curve.id);
            if (candidate?.values == null) throw new ArgumentException("Candidate values are missing.");
            var seen = new HashSet<string>();
            foreach (var value in candidate.values)
            {
                if (value == null || string.IsNullOrEmpty(value.id) || !seen.Add(value.id) || !numbers.TryGetValue(value.id, out var binding))
                    throw new ArgumentException("Unknown or duplicate v2 value: " + value?.id);
                RequireRange(value.value, binding.spec.minimum, binding.spec.maximum, value.id);
                if (binding.spec.kind != "number" && value.value != Mathf.Floor(value.value)) throw new ArgumentException("Enum value must be an integer: " + value.id);
            }
            seen.Clear();
            foreach (var value in candidate.curves ?? new List<CurveValue>())
            {
                if (value == null || string.IsNullOrEmpty(value.id) || !seen.Add(value.id) || !curves.ContainsKey(value.id)
                    || value.keys == null || value.keys.Count < 2 || value.keys.Count > 16)
                    throw new ArgumentException("Invalid v2 curve: " + value?.id);
                float previous = -1;
                foreach (var key in value.keys)
                {
                    if (key == null || !CombatLab.Finite(key.time) || !CombatLab.Finite(key.value) || key.time < 0 || key.time > 1 || key.time <= previous)
                        throw new ArgumentException("Curve keys must be finite and strictly increasing in 0..1: " + value.id);
                    previous = key.time;
                }
            }
            // UI Inherit means keep this source channel, including existing local
            // overrides. It must not turn a source override into engine inheritance.
            var keepModes = new HashSet<string>(candidate.values.Where(v => v.id.EndsWith(".mode", StringComparison.Ordinal)
                && v.value == 0).Select(v => v.id), StringComparer.Ordinal);
            bool Kept(string id)
            {
                foreach (string mode in keepModes)
                {
                    string channel = mode.Substring(0, mode.Length - ".mode".Length);
                    if (id == mode) return true;
                    if (mode.EndsWith(".camera.mode", StringComparison.Ordinal))
                    { if (id.StartsWith(channel + ".motion.", StringComparison.Ordinal)) return true; }
                    else if (id.StartsWith(channel + ".", StringComparison.Ordinal)) return true;
                }
                return false;
            }
            var activeValues = candidate.values.Where(v => !Kept(v.id)).ToList();
            var activeCurves = (candidate.curves ?? new List<CurveValue>()).Where(v => !Kept(v.id)).ToList();
            var restoreChannels = new List<Action>();
            void PreserveCamera(string prefix, Func<CombatCameraHitSettings> read)
            {
                var original = read();
                bool motion = keepModes.Contains(prefix + ".mode");
                bool impact = keepModes.Contains(prefix + ".impact.mode");
                bool shake = keepModes.Contains(prefix + ".shake.mode");
                if (!motion && !impact && !shake) return;
                restoreChannels.Add(() => {
                    var current = read();
                    if (current == null || ReferenceEquals(current, original)) return;
                    // Editing another channel materializes a detached camera block.
                    // Restore the kept raw channel too, so profile inheritance and
                    // dormant curves are not silently made into inline overrides.
                    if (motion && current is CombatCameraV2Settings attackCamera)
                    {
                        var originalAttack = original as CombatCameraV2Settings;
                        attackCamera.Mode = originalAttack?.Mode ?? CombatFeedbackChannelMode.Inherit;
                        attackCamera.Motion = originalAttack?.Motion ?? new CombatCameraMotionV2();
                    }
                    if (impact) current.Impact = original?.Impact ?? new CombatCameraImpactV2();
                    if (shake) current.Shake = original?.Shake ?? new CombatCameraShakeV2();
                });
            }
            var sourceSteps = FeedbackSteps(clone, actionId);
            for (int s = 0; s < sourceSteps.Count; s++)
            {
                var step = sourceSteps[s];
                string prefix = "step." + s + ".camera";
                PreserveCamera(prefix, () => step.Feedback.Camera.V2);
                foreach (int h in FeedbackHitIndices(clone, actionId, step))
                {
                    int hit = h;
                    PreserveCamera("step." + s + ".hit." + h + ".camera", () => step.HitEvents != null
                        && hit < step.HitEvents.Count ? step.HitEvents[hit]?.Feedback?.CameraV2 : null);
                }
            }
            // Explicit nonzero modes win over automatic activation, independent of JSON order.
            foreach (var value in activeValues.OrderBy(v => v.id.EndsWith(".mode") ? 1 : 0))
            {
                if (!numbers.TryGetValue(value.id, out var binding)) throw new ArgumentException("Unknown v2 value: " + value.id);
                RequireRange(value.value, binding.spec.minimum, binding.spec.maximum, value.id);
                binding.activate?.Invoke(); binding.set(value.value);
            }
            foreach (var value in activeCurves)
            {
                if (!curves.TryGetValue(value.id, out var binding)) throw new ArgumentException("Unknown v2 curve: " + value.id);
                var keys = value.keys.Select(k => new Keyframe(k.time, k.value)).ToArray();
                var curve = new AnimationCurve(keys);
                for (int i = 0; i < keys.Length; i++)
                {
                    UnityEditor.AnimationUtility.SetKeyLeftTangentMode(curve, i, UnityEditor.AnimationUtility.TangentMode.Linear);
                    UnityEditor.AnimationUtility.SetKeyRightTangentMode(curve, i, UnityEditor.AnimationUtility.TangentMode.Linear);
                }
                binding.activate?.Invoke(); binding.setCurve(curve);
            }
            foreach (var value in activeValues.Where(v => v.id.EndsWith(".mode"))) numbers[value.id].set(value.value);
            // An explicit zoom-source choice wins over the FOV edit default.
            foreach (var value in activeValues.Where(v => v.id.EndsWith(".motion.useFeelZoom"))) numbers[value.id].set(value.value);
            foreach (var restore in restoreChannels) restore();
        }
    }
}
