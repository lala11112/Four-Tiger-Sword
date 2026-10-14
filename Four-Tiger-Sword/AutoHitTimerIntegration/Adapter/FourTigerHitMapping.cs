using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StudioBaekho.AutoHitTimer.FourTigerSword
{
    // Keep these helpers independent of AssetDatabase so regression tests exercise the actual mapping.
    public static class FourTigerHitMapping
    {
        public sealed class ActionSlot
        {
            public string Path;
            public string Group;
            public string Label;
            public WeaponActionData Action;
            public List<WeaponActionData> List;
            public int Index = -1;
        }

        public static readonly string[] Groups = { "combo", "air", "counter", "skill", "ultimate" };

        public static string GroupLabel(string group)
        {
            switch (group)
            {
                case "combo": return "일반 콤보";
                case "air": return "공중 마무리";
                case "counter": return "반격";
                case "skill": return "스킬";
                case "ultimate": return "궁극기";
                case "heavy": return "강공격";
                default: throw new ArgumentException("알 수 없는 공격 종류: " + group);
            }
        }

        public static string UnsupportedReason(WeaponActionDataSO source, string group)
        {
            if (group == "combo" || group == "air" || group == "counter") return null;
            if (group == "skill" && (source is FireFormActionDataSO || source is IronFormActionDataSO)) return null;
            if (group == "ultimate" && source is IronFormActionDataSO) return null;
            return "이 폼의 " + GroupLabel(group) + "은 별도 판정 코드 또는 버프를 사용하므로 자동 저장을 지원하지 않습니다.";
        }

        public static IEnumerable<ActionSlot> Slots(WeaponActionDataSO source)
        {
            if (source.Actions == null) yield break;
            foreach (var group in Groups)
            {
                var list = GetList(source, group, false);
                if (list != null)
                {
                    var field = ListField(group);
                    for (int i = 0; i < list.Count; i++)
                        yield return new ActionSlot { Path = "Actions." + field + ".Array.data[" + i + "]", Group = group,
                            Label = GroupLabel(group) + " " + (i + 1), Action = list[i], List = list, Index = i };
                }
                else if (group == "air" || group == "counter")
                {
                    yield return new ActionSlot { Path = "Actions." + (group == "air" ? "AirAttackStep" : "CounterStep"),
                        Group = group, Label = GroupLabel(group), Action = group == "air" ? source.AirAttackStep : source.CounterStep };
                }
            }
            if (source is WaterFormActionDataSO water)
                yield return new ActionSlot { Path = "HeavyAttackStep", Group = "heavy", Label = "강공격", Action = water.HeavyAttackStep };
        }

        public static ActionSlot Resolve(WeaponActionDataSO source, string path)
        {
            var slot = Slots(source).FirstOrDefault(item => item.Path == path);
            if (slot == null) throw new ArgumentException("공격 단계가 없거나 삭제되었습니다: " + path);
            return slot;
        }

        public static WeaponActionData Clone(WeaponActionData action)
        {
            if (action == null) throw new ArgumentException("기존 공격 템플릿을 선택하세요.");
            return JsonUtility.FromJson<WeaponActionData>(JsonUtility.ToJson(action));
        }

        public static string Put(WeaponActionDataSO source, string path, string group, bool register, WeaponActionData action)
        {
            if (source.Actions == null) source.Actions = new FormActionSet();
            if (register)
            {
                if (group == "air") { source.AirAttackStep = action; return "Actions.AirAttackStep"; }
                if (group == "counter") { source.CounterStep = action; return "Actions.CounterStep"; }
                var list = GetList(source, group, true);
                list.Add(action);
                return "Actions." + ListField(group) + ".Array.data[" + (list.Count - 1) + "]";
            }
            var slot = Resolve(source, path);
            if (slot.List != null) slot.List[slot.Index] = action;
            else if (slot.Group == "air") source.AirAttackStep = action;
            else if (slot.Group == "counter") source.CounterStep = action;
            else throw new ArgumentException("지원하지 않는 공격 단계입니다.");
            return path;
        }

        private static string ListField(string group)
        {
            switch (group)
            {
                case "combo": return "ComboSteps";
                case "skill": return "SkillSteps";
                case "ultimate": return "UltimateSteps";
                default: throw new ArgumentException("목록형 공격이 아닙니다: " + group);
            }
        }

        private static List<WeaponActionData> GetList(WeaponActionDataSO source, string group, bool create)
        {
            if (group == "combo") return source.ComboSteps ?? (create ? source.ComboSteps = new List<WeaponActionData>() : null);
            if (group == "skill") return source.SkillSteps ?? (create ? source.SkillSteps = new List<WeaponActionData>() : null);
            if (group == "ultimate") return source.UltimateSteps ?? (create ? source.UltimateSteps = new List<WeaponActionData>() : null);
            return null;
        }

        public static JArray Windows(WeaponActionData action)
        {
            var windows = new JArray();
            if (action == null) return windows;
            if (action.HitEvents != null && action.HitEvents.Count > 0)
            {
                for (int i = 0; i < action.HitEvents.Count; i++)
                {
                    var hit = action.HitEvents[i];
                    if (hit == null) continue;
                    windows.Add(new JObject { ["start"] = hit.StartTime, ["end"] = hit.StartTime + hit.Duration, ["sourceIndex"] = i });
                }
            }
            else if (action.HitDuration > 0)
                windows.Add(new JObject { ["start"] = action.HitStartTime, ["end"] = action.HitStartTime + action.HitDuration, ["sourceIndex"] = 0 });
            return windows;
        }

        public static WeaponActionData Map(WeaponActionData original, JArray windows, float clipLength)
        {
            if (windows == null) throw new ArgumentException("히트 구간 목록이 필요합니다.");
            if (windows.Count > 128) throw new ArgumentException("히트 구간은 최대 128개입니다.");
            var action = Clone(original);
            var oldEvents = original.HitEvents ?? new List<HitEvent>();
            var oldCount = oldEvents.Count > 0 ? oldEvents.Count : original.HitDuration > 0 ? 1 : 0;
            var used = new HashSet<int>();
            var hits = new List<HitEvent>();
            float previousStart = -1;
            foreach (JObject window in windows)
            {
                float start = RequiredFloat(window, "start");
                float end = RequiredFloat(window, "end");
                if (start < 0 || end <= start || end > clipLength + 0.0001f || start < previousStart)
                    throw new ArgumentException("히트 구간은 시간순이며 0 ≤ 시작 < 종료 ≤ 클립 길이여야 합니다.");
                previousStart = start;
                if (window["sourceIndex"] == null || window["sourceIndex"].Type != JTokenType.Integer)
                    throw new ArgumentException("각 히트의 기존 항목 번호(sourceIndex) 또는 신규(-1)를 선택하세요.");
                int index = (int)window["sourceIndex"];
                if (index < -1 || index >= oldCount || (index >= 0 && !used.Add(index)))
                    throw new ArgumentException("기존 히트 대응이 유효하지 않거나 중복되었습니다.");
                HitEvent hit;
                if (index >= 0 && oldEvents.Count > 0)
                {
                    if (oldEvents[index] == null) throw new ArgumentException("비어 있는 기존 히트는 대응할 수 없습니다.");
                    hit = JsonUtility.FromJson<HitEvent>(JsonUtility.ToJson(oldEvents[index]));
                }
                else
                {
                    // HitBalanceSettings defaults PoiseDamage to 10; explicit zero is required for inheritance.
                    hit = new HitEvent { Damage = 0, PoiseDamage = 0, OverrideKnockbackForce = false,
                        OverrideHitStop = false, OverrideHitVFX = false };
                }
                hit.StartTime = start;
                hit.Duration = end - start;
                hits.Add(hit);
            }
            if (oldEvents.Count == 0 && hits.Count <= 1)
            {
                action.HitEvents = new List<HitEvent>();
                action.HitStartTime = hits.Count == 0 ? 0 : hits[0].StartTime;
                action.HitDuration = hits.Count == 0 ? 0 : hits[0].Duration;
            }
            else
            {
                // A single surviving explicit event keeps all its per-hit overrides.
                action.HitEvents = hits;
                if (hits.Count == 0) { action.HitStartTime = 0; action.HitDuration = 0; }
            }
            return action;
        }

        public static float RequiredFloat(JObject obj, string name)
        {
            var token = obj[name];
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
                throw new ArgumentException(name + "은 숫자여야 합니다.");
            float value = (float)token;
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException(name + "은 유한한 숫자여야 합니다.");
            return value;
        }

        public static float DamageSum(WeaponActionData action)
        {
            if (action == null) return 0;
            return action.HitEvents != null && action.HitEvents.Count > 0
                ? action.HitEvents.Where(h => h != null && h.Duration > 0).Sum(h => h.Damage > 0 ? h.Damage : action.Damage)
                : action.HitDuration > 0 ? action.Damage : 0;
        }

        private static readonly string[] TimePaths = {
            "Execution.ComboTransitionTime", "Execution.Movement.MovementStartTime", "Execution.Movement.MovementEndTime",
            "Execution.Movement.TargetApproach.ApproachEndTime", "Execution.Movement.TargetApproach.RotationEndTime",
            "Feedback.Trail.TrailStartTime", "Feedback.Trail.TrailEndTime", "Feedback.Slash.SlashVFXStartTime"
        };

        public static void ApplyTimeOverrides(WeaponActionData action, JArray overrides)
        {
            if (overrides == null) return;
            foreach (JObject item in overrides)
            {
                string path = (string)item["path"];
                if (!TimePaths.Contains(path)) throw new ArgumentException("수정할 수 없는 시간 필드: " + path);
                var owner = TimeOwner(action, path, out var field);
                field.SetValue(owner, RequiredFloat(item, "value"));
            }
        }

        public static JArray TimeSettings(WeaponActionData action, float length)
        {
            var result = new JArray();
            foreach (string path in TimePaths)
            {
                var owner = TimeOwner(action, path, out var field);
                result.Add(new JObject { ["path"] = path, ["label"] = path, ["value"] = (float)field.GetValue(owner),
                    ["min"] = path == "Feedback.Slash.SlashVFXStartTime" ? -1 : 0, ["max"] = length });
            }
            return result;
        }

        public static JArray InvalidTimes(WeaponActionData action, float length)
        {
            var result = new JArray();
            foreach (string path in TimePaths)
            {
                var owner = TimeOwner(action, path, out var field);
                float value = (float)field.GetValue(owner);
                float min = path == "Feedback.Slash.SlashVFXStartTime" ? -1 : 0;
                if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > length)
                    result.Add(new JObject { ["path"] = path, ["label"] = path, ["value"] = value, ["min"] = min, ["max"] = length });
            }
            foreach (var pair in new[] { new[] { "Execution.Movement.MovementStartTime", "Execution.Movement.MovementEndTime" },
                         new[] { "Feedback.Trail.TrailStartTime", "Feedback.Trail.TrailEndTime" } })
            {
                var a = TimeOwner(action, pair[0], out var fa);
                var b = TimeOwner(action, pair[1], out var fb);
                if ((float)fa.GetValue(a) > (float)fb.GetValue(b))
                    result.Add(new JObject { ["path"] = pair[1], ["label"] = "종료는 시작보다 빠를 수 없습니다: " + pair[1],
                        ["value"] = (float)fb.GetValue(b), ["min"] = (float)fa.GetValue(a), ["max"] = length });
            }
            return result;
        }

        private static object TimeOwner(object action, string path, out FieldInfo field)
        {
            var parts = path.Split('.');
            object current = action;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                current = current.GetType().GetField(parts[i]).GetValue(current);
                if (current == null) throw new ArgumentException("시간 설정이 비어 있습니다: " + path);
            }
            field = current.GetType().GetField(parts[parts.Length - 1]);
            return current;
        }
    }
}
