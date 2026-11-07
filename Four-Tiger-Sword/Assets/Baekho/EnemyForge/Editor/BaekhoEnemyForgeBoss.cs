using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioBaekho.EnemyForge
{
    public static class BaekhoEnemyForgeBoss
    {
        public const string TemplatePath = "Assets/_Game/RunProgression/Prefabs/ChapterBoss.prefab";
        public const string OutputRoot = "Assets/_Project/Generated/Bosses";
        public static readonly string[] Immunities = { "Bind", "Slow", "Airborne", "Silence", "Vulnerability", "DefenseReduction", "WeakPointExposed", "WaterDelayedDamage" };
        private static readonly string[] Labels = { "속박", "둔화", "공중 띄우기", "침묵", "받는 피해 증가", "방어력 감소", "약점 노출", "수속성 지연 피해" };
        public static bool IsBoss(JObject blueprint) => (string)blueprint?["kind"] == "boss";
        public static JObject Normalize(JObject source)
        {
            if (source == null) throw new InvalidDataException("적 설정이 없습니다.");
            var blueprint = (JObject)source.DeepClone();
            if (!IsBoss(blueprint)) return blueprint;
            int version = (int?)blueprint["version"] ?? 0;
            var config = blueprint["boss"] as JObject ?? throw new InvalidDataException("보스 설정이 없습니다.");
            var phases = config["phases"] as JArray ?? throw new InvalidDataException("페이즈 목록이 필요합니다.");
            if (phases.Count < 1 || phases.Count > 16) throw new InvalidDataException("페이즈를 1~16개 지정하세요.");
            if (version == 2)
            {
                double total = Number(blueprint["stats"] as JObject, "maxHp", double.Epsilon);
                for (int i = 0; i < phases.Count; i++)
                {
                    var phase = phases[i] as JObject ?? throw new InvalidDataException("페이즈 객체가 필요합니다.");
                    double current = Number(phase, "triggerHpPercent", double.Epsilon, 100);
                    double next = i + 1 < phases.Count ? Number(phases[i + 1] as JObject, "triggerHpPercent", double.Epsilon, 100) : 0;
                    if (i == 0 && current != 100 || current <= next) throw new InvalidDataException("기존 페이즈 HP 조건은 100%부터 엄격한 내림차순이어야 합니다.");
                    phase["phaseMaxHp"] = total * (current - next) / 100;
                }
                config["phaseDownSeconds"] = 1; config["phaseHoldSeconds"] = .5; config["phaseReviveSeconds"] = 1.5;
                blueprint["version"] = 3;
            }
            if ((int?)blueprint["version"] != 3) throw new InvalidDataException("보스는 버전 3 설계가 필요합니다.");
            double sum = 0;
            foreach (var token in phases)
            {
                var phase = token as JObject ?? throw new InvalidDataException("페이즈 객체가 필요합니다.");
                sum += Number(phase, "phaseMaxHp", float.Epsilon);
                phase.Remove("triggerHpPercent");
            }
            if (double.IsInfinity(sum) || sum > int.MaxValue) throw new InvalidDataException("총 보스 HP가 정수 요약의 지원 범위를 벗어났습니다.");
            var stats = blueprint["stats"] as JObject ?? throw new InvalidDataException("능력치 설정이 필요합니다.");
            stats["maxHp"] = Math.Max(1, (int)Math.Round(sum, MidpointRounding.AwayFromZero));
            return blueprint;
        }
        public static JObject Defaults() => new JObject {
            ["enableWeakness"] = false, ["weaknessElement"] = 0, ["weakDamageMult"] = 1.5,
            ["enableResistance"] = false, ["resistElement"] = 0, ["resistDamageMult"] = .5,
            ["groggyDamageMult"] = 1.5, ["statusImmunityList"] = new JArray(), ["initialShield"] = 0,
            ["phaseDownSeconds"] = 1, ["phaseHoldSeconds"] = .5, ["phaseReviveSeconds"] = 1.5,
            ["reward"] = new JObject { ["gold"] = 50, ["items"] = new JArray() },
            ["phases"] = new JArray(new JObject {
                ["phaseId"] = "phase_1", ["phaseMaxHp"] = 1000, ["atkMultiplier"] = 1, ["defMultiplier"] = 1,
                ["skillIds"] = null, ["pattern"] = new JObject { ["mode"] = "weighted", ["loop"] = true, ["steps"] = new JArray() },
                ["shield"] = null, ["music"] = new JObject { ["mode"] = "keep", ["clip"] = null, ["volume"] = 1, ["fadeSeconds"] = .5 }, ["note"] = ""
            })
        };
        public static ItemDefinition[] Items()
        {
            return AssetDatabase.FindAssets("t:ItemCatalog", new[] { "Assets" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<ItemCatalog>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(c => c != null && c.items != null)
                .SelectMany(c => c.items.Where(i => i != null && !string.IsNullOrWhiteSpace(i.itemId) && !string.IsNullOrWhiteSpace(i.candidateKey) && c.Find(i.itemId) == i))
                .Distinct().OrderBy(i => i.itemId, StringComparer.Ordinal).ToArray();
        }
        public static JObject Catalog() => new JObject {
            ["immunities"] = new JArray(Immunities.Select((id, i) => new JObject { ["id"] = id, ["label"] = Labels[i] })),
            ["items"] = new JArray(Items().Select(item => { var r = BaekhoEnemyForgeCatalog.AssetRef(item); r["name"] = string.IsNullOrWhiteSpace(item.displayName) ? item.itemId : item.displayName; return r; }))
        };
        private static void Error(JArray issues, string path, string message) => issues.Add(new JObject { ["severity"] = "error", ["path"] = path, ["message"] = message });
        private static double Number(JObject value, string field, double min = 0, double max = float.MaxValue)
        {
            var token = value?[field];
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)) throw new InvalidDataException(field + ": 숫자가 필요합니다.");
            double n = (double)token;
            if (double.IsNaN(n) || double.IsInfinity(n) || n < min || n > max) throw new InvalidDataException(field + ": 범위를 벗어난 숫자입니다.");
            return n;
        }
        private static bool Bool(JObject value, string field)
        { if (value?[field]?.Type != JTokenType.Boolean) throw new InvalidDataException(field + ": 참/거짓 값이 필요합니다."); return (bool)value[field]; }
        public static void Validate(JObject blueprint, JArray issues)
        {
            if (!IsBoss(blueprint)) return;
            try
            {
                if ((int?)blueprint["version"] != 3) throw new InvalidDataException("보스는 버전 3 설계가 필요합니다.");
                var config = blueprint["boss"] as JObject ?? throw new InvalidDataException("보스 설정이 없습니다.");
                if (blueprint["selfDestruct"] is JObject) Error(issues, "selfDestruct", "보스는 일반 몬스터 자동 자폭을 지원하지 않습니다.");
                bool weak = Bool(config, "enableWeakness"), resist = Bool(config, "enableResistance");
                foreach (string key in new[] { "weaknessElement", "resistElement" })
                { double element = Number(config, key); if (element != Math.Truncate(element) || !Enum.IsDefined(typeof(ElementType), (int)element)) throw new InvalidDataException("잘못된 약점/저항 속성입니다."); }
                if (weak && resist && (int)config["weaknessElement"] == (int)config["resistElement"]) Error(issues, "boss.weaknessElement", "같은 속성을 약점과 저항으로 지정할 수 없습니다.");
                foreach (string key in new[] { "weakDamageMult", "resistDamageMult", "groggyDamageMult", "initialShield" }) Number(config, key);
                Number(config, "phaseDownSeconds", .01); Number(config, "phaseHoldSeconds"); Number(config, "phaseReviveSeconds", .01);
                var immunity = config["statusImmunityList"] as JArray ?? throw new InvalidDataException("면역 목록이 필요합니다.");
                if (immunity.Any(x => x.Type != JTokenType.String || !Immunities.Contains((string)x)) || immunity.Values<string>().Distinct().Count() != immunity.Count) throw new InvalidDataException("알 수 없거나 중복된 면역입니다.");
                var attacks = (blueprint["attacks"] as JArray)?.OfType<JObject>().ToDictionary(a => (string)a["id"], a => a) ?? throw new InvalidDataException("공격 목록이 필요합니다.");
                foreach (var a in attacks.Values)
                { var type = BaekhoEnemyForgeCatalog.AttackType((string)a["typeId"]); if (type != null && typeof(MonsterSelfDestructSO).IsAssignableFrom(type)) Error(issues, "attacks", "보스 공격에 일반 자동 자폭을 사용할 수 없습니다."); }
                var phases = config["phases"] as JArray;
                if (phases == null || phases.Count < 1 || phases.Count > 16) throw new InvalidDataException("페이즈를 1~16개 지정하세요.");
                var ids = new HashSet<string>(); var active = attacks.Keys.ToArray();
                for (int i = 0; i < phases.Count; i++)
                {
                    var p = phases[i] as JObject ?? throw new InvalidDataException("페이즈 객체가 필요합니다."); string path = "boss.phases." + i;
                    string id = (string)p["phaseId"];
                    if (string.IsNullOrWhiteSpace(id) || id.Length > 80 || !ids.Add(id)) Error(issues, path + ".phaseId", "페이즈 ID는 비어 있지 않고 중복되지 않아야 합니다.");
                    Number(p, "phaseMaxHp", float.Epsilon);
                    Number(p, "atkMultiplier"); Number(p, "defMultiplier");
                    if (p["shield"] != null && p["shield"].Type != JTokenType.Null) Number(p, "shield");
                    if (p["note"]?.Type != JTokenType.String || ((string)p["note"]).Length > 4000) Error(issues, path + ".note", "메모는 4000자 이하 텍스트여야 합니다.");
                    if (p["skillIds"] != null && p["skillIds"].Type != JTokenType.Null)
                    {
                        var skills = p["skillIds"] as JArray ?? throw new InvalidDataException("페이즈 공격 목록이 올바르지 않습니다.");
                        active = skills.Values<string>().ToArray();
                        if (active.Length > 32 || active.Distinct().Count() != active.Length) Error(issues, path + ".skillIds", "페이즈 공격은 중복 없이 최대 32개입니다.");
                    }
                    if (active.Any(a => a == null || !attacks.ContainsKey(a))) Error(issues, path + ".skillIds", "삭제되었거나 없는 공격 참조입니다.");
                    var pattern = p["pattern"] as JObject ?? throw new InvalidDataException("페이즈 패턴이 필요합니다."); Bool(pattern, "loop");
                    string mode = (string)pattern["mode"];
                    if (mode == "weighted")
                    { if (!active.Any(a => a != null && attacks.ContainsKey(a) && ((double?)attacks[a]["values"]?["weight"] ?? 1) > 0)) Error(issues, path + ".skillIds", "가중치가 양수인 공격이 하나 이상 필요합니다."); }
                    else if (mode != "sequence") Error(issues, path + ".pattern", "알 수 없는 패턴 방식입니다.");
                    var steps = pattern["steps"] as JArray ?? throw new InvalidDataException("패턴 단계 목록이 필요합니다.");
                    if (steps.Count > 128 || mode == "sequence" && !steps.Any(s => (string)s["kind"] == "attack")) Error(issues, path + ".pattern.steps", "순차 패턴은 공격을 포함한 최대 128개 단계입니다.");
                    foreach (JObject step in steps)
                    {
                        Number(step, "waitSeconds"); string stepKind = (string)step["kind"];
                        if (stepKind == "attack") { if (string.IsNullOrEmpty((string)step["attackId"]) || !attacks.ContainsKey((string)step["attackId"])) Error(issues, path + ".pattern.steps", "공격 단계에 유효한 공격을 선택하세요."); }
                        else if (stepKind != "wait") Error(issues, path + ".pattern.steps", "공격 또는 대기 단계만 지원합니다.");
                    }
                    var music = p["music"] as JObject ?? throw new InvalidDataException("페이즈 음악 설정이 필요합니다.");
                    Number(music, "volume", 0, 1); Number(music, "fadeSeconds");
                    string musicMode = (string)music["mode"];
                    if (!new[] { "keep", "play", "stop" }.Contains(musicMode)) Error(issues, path + ".music", "잘못된 BGM 방식입니다.");
                    var clip = BaekhoEnemyForgeCatalog.Resolve(music["clip"], typeof(AudioClip));
                    if (musicMode == "play" && clip == null) Error(issues, path + ".music.clip", "재생할 BGM을 선택하세요.");
                }
                var reward = config["reward"] as JObject ?? throw new InvalidDataException("보상이 필요합니다.");
                double gold = Number(reward, "gold", 0, int.MaxValue); if (gold != Math.Truncate(gold)) Error(issues, "boss.reward.gold", "골드는 정수여야 합니다.");
                var items = reward["items"] as JArray ?? throw new InvalidDataException("보상 아이템 목록이 필요합니다.");
                var available = new HashSet<ItemDefinition>(Items()); var itemIds = new HashSet<string>(); var keys = new HashSet<string>();
                if (items.Count > 64) Error(issues, "boss.reward.items", "보상 아이템은 최대 64개입니다.");
                foreach (var reference in items)
                {
                    var item = BaekhoEnemyForgeCatalog.Resolve(reference, typeof(ItemDefinition)) as ItemDefinition;
                    if (item == null || !available.Contains(item) || !itemIds.Add(item.itemId) || !keys.Add(item.candidateKey)) Error(issues, "boss.reward.items", "현재 카탈로그의 중복 없는 아이템을 선택하세요.");
                }
                if ((int?)blueprint["stats"]?["staggerResistLevel"] == (int)StaggerResistLevel.SUPER_ARMOR)
                    issues.Add(new JObject { ["severity"] = "warning", ["path"] = "stats.staggerResistLevel", ["message"] = "현재 SUPER_ARMOR는 피격과 그로기 진입을 억제합니다." });
            }
            catch (Exception e) { Error(issues, "boss", e.GetBaseException().Message); }
        }
        public static ConfiguredBoss ConvertRoot(GameObject root)
        {
            var enemies = root.GetComponents<Enemy>();
            if (enemies.Length != 1) throw new InvalidDataException("템플릿 루트의 Enemy는 하나여야 합니다.");
            if (enemies[0] is ConfiguredBoss configured) return configured;
            if (enemies[0].GetType() != typeof(Enemy)) throw new InvalidDataException("일반 Enemy 또는 ConfiguredBoss 템플릿을 선택하세요.");
            var original = enemies[0]; var boss = root.AddComponent<ConfiguredBoss>();
            // Copy only common serialized fields, never m_Script or a prefab's runtime state.
            BaekhoEnemyForgeCatalog.ApplyValues(boss, (JObject)BaekhoEnemyForgeCatalog.Describe(original, BaekhoEnemyForgeCatalog.ComponentPaths["Enemy"])["defaults"], BaekhoEnemyForgeCatalog.ComponentPaths["Enemy"]);
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component == original) continue;
                var so = new SerializedObject(component); var it = so.GetIterator(); bool changed = false;
                while (it.Next(true)) if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == original) { it.objectReferenceValue = boss; changed = true; }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
            Object.DestroyImmediate(original); return boss;
        }
        private static void Save(Object asset, string folder, string name, JArray assets)
        { asset.name = name; string path = folder + "/" + name + ".asset"; AssetDatabase.CreateAsset(asset, path); assets.Add(path); }
        public static void Build(GameObject root, JObject blueprint, BossData data, MonsterSkillDataSO baseSkills, string folder, JArray assets)
        {
            var config = (JObject)blueprint["boss"]; var boss = root.GetComponent<ConfiguredBoss>();
            data.bossId = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data)); data.bossName = root.name;
            var combat = (JObject)config.DeepClone(); combat.Remove("phases"); combat.Remove("reward");
            BaekhoEnemyForgeCatalog.ApplyValues(data, combat, new[] { "enableWeakness", "weaknessElement", "weakDamageMult", "enableResistance", "resistElement", "resistDamageMult", "groggyDamageMult", "statusImmunityList", "initialShield", "phaseDownSeconds", "phaseHoldSeconds", "phaseReviveSeconds" });
            var reward = ScriptableObject.CreateInstance<BossRewardGroupSO>(); reward.gold = (int)config["reward"]["gold"];
            reward.items = ((JArray)config["reward"]["items"]).Select(r => (ItemDefinition)BaekhoEnemyForgeCatalog.Resolve(r, typeof(ItemDefinition))).ToList();
            Save(reward, folder, root.name + "_Rewards", assets); data.rewardGroup = reward;
            var attacks = ((JArray)blueprint["attacks"]).Select((a, i) => new { id = (string)a["id"], skill = baseSkills.skillData[i] }).ToDictionary(a => a.id, a => a.skill);
            var phaseAssets = new List<BossPhaseData>(); int phaseIndex = 0;
            foreach (JObject p in (JArray)config["phases"])
            {
                string prefix = root.name + "_Phase" + (++phaseIndex); var phase = ScriptableObject.CreateInstance<BossPhaseData>();
                phase.phaseId = (string)p["phaseId"]; phase.phaseMaxHp = (float)p["phaseMaxHp"]; phase.atkMultiplier = (float)p["atkMultiplier"]; phase.defMultiplier = (float)p["defMultiplier"]; phase.note = (string)p["note"];
                phase.overrideShield = p["shield"] != null && p["shield"].Type != JTokenType.Null; phase.shieldAmount = phase.overrideShield ? (float)p["shield"] : 0;
                var music = (JObject)p["music"]; phase.musicMode = (string)music["mode"] == "play" ? BossMusicMode.Play : (string)music["mode"] == "stop" ? BossMusicMode.Stop : BossMusicMode.Keep;
                phase.musicClip = (AudioClip)BaekhoEnemyForgeCatalog.Resolve(music["clip"], typeof(AudioClip)); phase.musicVolume = (float)music["volume"]; phase.musicFadeSeconds = (float)music["fadeSeconds"];
                if (p["skillIds"] is JArray ids)
                {
                    var skills = ScriptableObject.CreateInstance<MonsterSkillDataSO>(); skills.monsterName = root.name; skills.skillData = ids.Values<string>().Select(id => attacks[id]).ToList();
                    Save(skills, folder, prefix + "_Skills", assets); phase.skillSet = skills;
                }
                if ((string)p["pattern"]["mode"] == "sequence")
                {
                    var pattern = ScriptableObject.CreateInstance<BossSequencePatternSO>(); pattern.loop = (bool)p["pattern"]["loop"];
                    pattern.steps = ((JArray)p["pattern"]["steps"]).OfType<JObject>().Select(s => new BossSequenceStep { kind = (string)s["kind"] == "wait" ? BossPatternStepKind.Wait : BossPatternStepKind.Attack, skill = (string)s["kind"] == "attack" ? attacks[(string)s["attackId"]] : null, waitSeconds = (float)s["waitSeconds"] }).ToList();
                    Save(pattern, folder, prefix + "_Pattern", assets); phase.pattern = pattern;
                }
                Save(phase, folder, prefix, assets); phaseAssets.Add(phase);
            }
            var serialized = new SerializedObject(boss); serialized.FindProperty("BossData").objectReferenceValue = data;
            var phasesProperty = serialized.FindProperty("PhaseDatas"); phasesProperty.arraySize = phaseAssets.Count;
            for (int i = 0; i < phaseAssets.Count; i++) phasesProperty.GetArrayElementAtIndex(i).objectReferenceValue = phaseAssets[i];
            serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(data);
            if (root.GetComponents<Enemy>().Length != 1) throw new InvalidDataException("보스 루트에 중복된 Enemy 컴포넌트가 있습니다.");
        }
    }
}
