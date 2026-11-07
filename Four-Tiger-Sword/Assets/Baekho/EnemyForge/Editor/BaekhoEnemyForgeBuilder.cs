using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace StudioBaekho.EnemyForge
{
    public static class BaekhoEnemyForgeBuilder
    {
        public const string OutputRoot = "Assets/_Project/Generated/Enemies";
        private static readonly HashSet<string> Stats = new HashSet<string>(new[] { "elementType", "maxHp", "baseAtk", "baseDef", "criticalChance", "criticalDamage", "combatRange", "combatExitRange", "knockbackMoveTime", "knockbackMaxDistance", "attackCooldown", "staggerResistLevel" });
        public static string OutputFolder(JObject blueprint)
        {
            string name = (string)blueprint?["name"];
            if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, @"\A[\p{L}\p{N}_ -]{1,64}\z") || name.Trim() != name ||
                Regex.IsMatch(name, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z", RegexOptions.IgnoreCase))
                throw new InvalidDataException("적 이름은 1~64자의 문자·숫자·공백·밑줄·하이픈을 사용하세요.");
            return (BaekhoEnemyForgeBoss.IsBoss(blueprint) ? BaekhoEnemyForgeBoss.OutputRoot : OutputRoot) + "/" + name;
        }

        private static void Issue(JArray issues, string severity, string path, string message)
            => issues.Add(new JObject { ["severity"] = severity, ["path"] = path, ["message"] = message });

        public static JObject ValidateConfiguration(JObject blueprint) => Validate(blueprint, false, true);

        public static JObject Validate(JObject blueprint, bool requireFreshFolder = true, bool configurationOnly = false)
        {
            var issues = new JArray();
            if (blueprint == null) return new JObject { ["valid"] = false, ["issues"] = new JArray(new JObject { ["severity"] = "error", ["path"] = "blueprint", ["message"] = "적 설정이 없습니다." }) };
            try
            {
                blueprint = BaekhoEnemyForgeBoss.Normalize(blueprint);
                int version = (int?)blueprint["version"] ?? 0; string kind = (string)blueprint["kind"] ?? "normal";
                if ((version != 1 && version != 2 && version != 3) || (kind != "normal" && kind != "boss") || version >= 2 && blueprint["kind"] == null || version == 1 && kind != "normal") throw new InvalidDataException("Unsupported enemy blueprint version/kind.");
                if (kind == "normal" && blueprint["boss"] != null && blueprint["boss"].Type != JTokenType.Null) throw new InvalidDataException("Normal enemies cannot contain boss settings.");
                string output = OutputFolder(blueprint);
                BaekhoEnemyForgeBridge.CheckPath(Path.GetFullPath(output));
                if (requireFreshFolder && (AssetDatabase.IsValidFolder(output) || Directory.Exists(output))) Issue(issues, "error", "name", "같은 이름의 출력 폴더가 있습니다. 새 이름을 입력하세요.");
                var template = ResolveTemplate(blueprint);
                if (template == null) throw new InvalidDataException("Template prefab was not found.");
                if (template.GetComponents<Enemy>().Length != 1 || kind == "normal" && template.GetComponent<Boss>() != null) Issue(issues, "error", "template", "설계 종류와 템플릿 전투 컴포넌트가 맞지 않습니다.");
                foreach (Type required in new[] { typeof(Enemy), typeof(EnemyStat), typeof(PoiseHandler), typeof(NavMeshAgent), typeof(FanVisionSensor) })
                    if (template.GetComponent(required) == null) Issue(issues, "error", "template", "템플릿 루트에 " + required.Name + " 컴포넌트가 필요합니다.");
                if (template.GetComponent<SphereCollider>() == null && template.GetComponent<CapsuleCollider>() == null) Issue(issues, "error", "template", "템플릿 루트에 SphereCollider 또는 CapsuleCollider가 필요합니다.");
                if (LayerMask.NameToLayer("Enemy") < 0 || LayerMask.NameToLayer("Player") < 0) Issue(issues, "error", "project", "프로젝트에 Enemy와 Player 레이어가 필요합니다.");
                var model = configurationOnly ? null : BaekhoEnemyForgeCatalog.Resolve(blueprint["model"], typeof(GameObject)) as GameObject;
                var modelAnimator = model?.GetComponentInChildren<Animator>(true);
                if (!configurationOnly && (model == null || modelAnimator == null)) Issue(issues, "error", "model", "Animator를 포함한 모델/모델 프리팹을 선택하세요.");
                else if (model != null && model.GetComponentsInChildren<Animator>(true).Length != 1) Issue(issues, "error", "model", "모델에는 Animator가 하나만 있어야 합니다.");
                var transform = blueprint["modelTransform"] as JObject ?? throw new InvalidDataException("modelTransform is required.");
                BaekhoEnemyForgeCatalog.Numbers(transform["position"], 3); BaekhoEnemyForgeCatalog.Numbers(transform["rotation"], 3);
                if (BaekhoEnemyForgeCatalog.Numbers(transform["scale"], 3).Any(s => s <= 0f)) Issue(issues, "error", "modelTransform.scale", "모델 스케일은 0보다 커야 합니다.");
                BaekhoEnemyForgeCatalog.Numbers(blueprint["hpBarPosition"], 3); BaekhoEnemyForgeCatalog.Numbers(blueprint["vfxPosition"], 3);
                var stats = blueprint["stats"] as JObject ?? throw new InvalidDataException("stats is required.");
                var statData = ScriptableObject.CreateInstance<EnemyData>();
                try
                {
                    BaekhoEnemyForgeCatalog.ApplyValues(statData, stats, Stats);
                    ValidateFields(BaekhoEnemyForgeCatalog.Describe(statData)["fields"] as JArray, stats, "stats", issues);
                    if (statData.maxHp <= 0) Issue(issues, "error", "stats.maxHp", "최대 HP는 0보다 커야 합니다.");
                    if (statData.baseAtk < 0 || statData.baseDef < 0 || statData.criticalDamage < 0) Issue(issues, "error", "stats", "공격력·방어력·치명 배율은 음수일 수 없습니다.");
                    if (statData.criticalChance < 0 || statData.criticalChance > 1) Issue(issues, "error", "stats.criticalChance", "치명 확률은 0~100%여야 합니다.");
                    if (statData.attackCooldown < .1f || statData.combatRange < 0 || statData.combatExitRange < statData.combatRange) Issue(issues, "error", "stats", "공격 쿨타임은 0.1초 이상이고 전투 종료 거리는 진입 거리 이상이어야 합니다.");
                    if (!Enum.IsDefined(typeof(ElementType), statData.elementType) || !Enum.IsDefined(typeof(StaggerResistLevel), statData.staggerResistLevel)) Issue(issues, "error", "stats", "알 수 없는 속성 또는 경직 저항입니다.");
                }
                finally { Object.DestroyImmediate(statData); }
                var components = blueprint["components"] as JObject ?? throw new InvalidDataException("components is required.");
                foreach (var component in components)
                {
                    if (!BaekhoEnemyForgeCatalog.ComponentPaths.TryGetValue(component.Key, out string[] paths)) { Issue(issues, "error", "components." + component.Key, "지원하지 않는 컴포넌트입니다."); continue; }
                    if (!(component.Value is JObject values)) throw new InvalidDataException("Component values must be an object.");
                    var source = template.GetComponent(component.Key);
                    if (source == null) { Issue(issues, "error", "components." + component.Key, "템플릿에 컴포넌트가 없습니다."); continue; }
                    foreach (var value in values)
                        if (!paths.Contains(value.Key)) Issue(issues, "error", "components." + component.Key + "." + value.Key, "편집이 허용되지 않은 필드입니다.");
                    ValidateFields(BaekhoEnemyForgeCatalog.Describe(source, paths)["fields"] as JArray, values, "components." + component.Key, issues);
                    foreach (var value in values)
                    {
                        if (value.Value.Type != JTokenType.Float && value.Value.Type != JTokenType.Integer) continue;
                        double number = value.Value.Value<double>();
                        if (number < 0 && value.Key != "m_BaseOffset" && value.Key != "_visibilityMask" && value.Key != "_heightOffset") Issue(issues, "error", "components." + component.Key + "." + value.Key, "이 값은 음수일 수 없습니다.");
                        if ((value.Key == "m_Radius" || value.Key == "m_Height" || value.Key == "_maxPoise" || value.Key == "_rayCount") && number <= 0) Issue(issues, "error", "components." + component.Key + "." + value.Key, "이 값은 0보다 커야 합니다.");
                    }
                }
                var attacks = blueprint["attacks"] as JArray ?? throw new InvalidDataException("attacks must be an array.");
                if (blueprint["components"]?["CapsuleCollider"] is JObject capsule && (double?)capsule["m_Height"] < (double?)capsule["m_Radius"] * 2) Issue(issues, "error", "components.CapsuleCollider", "몸체 캡슐 높이는 반지름의 두 배 이상이어야 합니다.");
                if (attacks.Count < 1 || attacks.Count > 32) Issue(issues, "error", "attacks", "일반 공격을 1~32개 설정하세요.");
                var ids = new HashSet<string>(); var requiredStates = new HashSet<string>(new[] { "Idle", "Move", "Groggy" });
                if (kind == "boss" && ((JArray)blueprint["boss"]["phases"]).Count > 1) { requiredStates.Add("PhaseDown"); requiredStates.Add("PhaseRevive"); }
                for (int i = 0; i < attacks.Count; i++) ValidateAttack(attacks[i] as JObject, false, "attacks." + i, issues, ids, requiredStates);
                if (blueprint["selfDestruct"] is JObject selfDestruct) ValidateAttack(selfDestruct, true, "selfDestruct", issues, ids, requiredStates);
                BaekhoEnemyForgeBoss.Validate(blueprint, issues);
                if (!configurationOnly) BaekhoEnemyForgeAnimation.Validate(blueprint["animation"] as JObject, modelAnimator, requiredStates, issues);
                if (!configurationOnly) Issue(issues, "warning", "placement", "생성 후 유효한 NavMesh 위에 배치하세요. 프리팹 생성은 열린 씬을 변경하지 않습니다.");
                if (model != null && !SameModel(template, model))
                {
                    Issue(issues, "warning", "model", "모델 교체 시 원본 사망용 재질 연결은 제거됩니다. 사망 후 제거는 유지되며 전용 페이드 재질은 별도로 설정할 수 있습니다.");
                    if (model.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m == null)))
                        Issue(issues, "warning", "model", "선택한 모델의 일부 재질이 비어 있습니다. 재질을 연결한 모델 프리팹을 사용하면 색상을 유지할 수 있습니다.");
                }
            }
            catch (Exception exception) { Issue(issues, "error", "blueprint", exception.Message); }
            return new JObject { ["valid"] = !issues.Any(i => (string)i["severity"] == "error"), ["issues"] = issues };
        }

        private static void ValidateFields(JArray schema, JObject values, string path, JArray issues)
        {
            if (schema == null || values == null) return;
            var byName = schema.OfType<JObject>().ToDictionary(f => (string)f["path"]);
            foreach (var value in values)
            {
                string location = path + "." + value.Key;
                if (!byName.TryGetValue(value.Key, out var field)) { Issue(issues, "error", location, "알 수 없는 필드입니다."); continue; }
                string type = (string)field["type"];
                try
                {
                    if (type == "number" || type == "integer" || type == "enum")
                    {
                        if (value.Value.Type != JTokenType.Integer && value.Value.Type != JTokenType.Float) throw new InvalidDataException("숫자가 필요합니다.");
                        double number = value.Value.Value<double>();
                        if (double.IsNaN(number) || double.IsInfinity(number) || ((type == "integer" || type == "enum") && number != Math.Truncate(number))) throw new InvalidDataException("유효한 숫자가 아닙니다.");
                        if (field["min"] != null && number < (double)field["min"]) throw new InvalidDataException("최소값 " + field["min"] + " 이상이어야 합니다.");
                        if (field["max"] != null && number > (double)field["max"]) throw new InvalidDataException("최대값 " + field["max"] + " 이하여야 합니다.");
                        if (type == "enum" && !((JArray)field["options"]).Any(o => (double)o["value"] == number)) throw new InvalidDataException("알 수 없는 선택값입니다.");
                    }
                    else if (type == "boolean" && value.Value.Type != JTokenType.Boolean) throw new InvalidDataException("참/거짓 값이 필요합니다.");
                    else if (type == "string" && (value.Value.Type != JTokenType.String || value.Value.Value<string>().Length > 4096)) throw new InvalidDataException("텍스트 값이 필요합니다.");
                    else if (type == "asset") BaekhoEnemyForgeCatalog.Resolve(value.Value);
                    else if (type == "vector2" || type == "vector3" || type == "color") BaekhoEnemyForgeCatalog.Numbers(value.Value, type == "vector2" ? 2 : type == "vector3" ? 3 : 4);
                    else if (type == "object") ValidateFields(field["children"] as JArray, value.Value as JObject ?? throw new InvalidDataException("객체 값이 필요합니다."), location, issues);
                    else if (type == "array")
                    {
                        var array = value.Value as JArray ?? throw new InvalidDataException("배열 값이 필요합니다.");
                        if (array.Count > 128) throw new InvalidDataException("배열은 최대 128개입니다.");
                        for (int i = 0; i < array.Count; i++)
                        {
                            if (field["element"] is JObject element)
                            {
                                var elementSchema = (JObject)element.DeepClone(); elementSchema["path"] = "value";
                                ValidateFields(new JArray(elementSchema), new JObject { ["value"] = array[i].DeepClone() }, location + "." + i, issues);
                            }
                            else if (array[i] is JObject item) ValidateFields(field["children"] as JArray, item, location + "." + i, issues);
                            else Issue(issues, "error", location + "." + i, "배열 요소의 형식이 올바르지 않습니다.");
                        }
                    }
                    else if (type == "unsupported" && value.Value.Type != JTokenType.Null) throw new InvalidDataException("지원되지 않는 필드입니다.");
                }
                catch (Exception exception) { Issue(issues, "error", location, exception.Message); }
            }
        }

        private static void ValidateAttack(JObject config, bool selfDestruct, string path, JArray issues, HashSet<string> ids, HashSet<string> states)
        {
            if (config == null) { Issue(issues, "error", path, "공격 설정이 올바르지 않습니다."); return; }
            string id = (string)config["id"], typeId = (string)config["typeId"];
            if (string.IsNullOrEmpty(id) || !ids.Add(id)) Issue(issues, "error", path + ".id", "공격 ID가 없거나 중복되었습니다.");
            Type type = BaekhoEnemyForgeCatalog.AttackType(typeId);
            if (type == null) { Issue(issues, "error", path, "컴파일된 공격 타입을 찾을 수 없습니다: " + typeId); return; }
            if (typeof(MonsterSelfDestructSO).IsAssignableFrom(type) != selfDestruct) { Issue(issues, "error", path, "일반 공격과 HP 조건 자폭의 등록 위치가 다릅니다."); return; }
            var skill = (MonsterSkillData)ScriptableObject.CreateInstance(type);
            try
            {
                var descriptor = BaekhoEnemyForgeCatalog.Descriptor(typeId);
                if (descriptor?["defaults"] is JObject defaults) BaekhoEnemyForgeCatalog.ApplyValues(skill, defaults);
                var values = config["values"] as JObject ?? throw new InvalidDataException("공격 파라미터가 없습니다.");
                ValidateFields(BaekhoEnemyForgeCatalog.Describe(skill)["fields"] as JArray, values, path + ".values", issues);
                BaekhoEnemyForgeCatalog.ApplyValues(skill, values);
                if (string.IsNullOrWhiteSpace(skill.animName) || skill.animName.Length > 100 || skill.animName.IndexOfAny(new[] { '.', '/', '\\' }) >= 0) Issue(issues, "error", path + ".values.animName", "유효한 단일 Animator 상태명을 입력하세요.");
                else states.Add(skill.animName);
                if (skill.attackSpeed <= 0 || skill.minimumRange < 0 || skill.minimumRange > skill.executeRange || skill.executeRange > skill.engageRange) Issue(issues, "error", path, "최소거리 ≤ 실행거리 ≤ 선택거리, 공격속도 > 0이어야 합니다.");
                if (!selfDestruct && skill.weight <= 0) Issue(issues, "warning", path + ".values.weight", "가중치가 0이면 이 공격을 선택하지 않습니다.");
                if (skill is MonsterDefaultAttackSO basic)
                {
                    if (basic.damage < 0 || basic.hitStartTime < 0 || basic.hitDuration <= 0 || basic.attackDuration < basic.hitStartTime + basic.hitDuration) Issue(issues, "error", path, "타격 시간 구간이 공격 전체 시간 안에 있어야 하며 피해 배율은 음수일 수 없습니다.");
                    if (basic.hitBoxRadius <= 0 || (basic.hitBoxShape == HitBoxShape.Box && (basic.hitBoxSize.x <= 0 || basic.hitBoxSize.y <= 0 || basic.hitBoxSize.z <= 0)) || (basic.hitBoxShape == HitBoxShape.Capsule && basic.hitBoxHeight < basic.hitBoxRadius * 2f)) Issue(issues, "error", path, "히트박스 크기가 잘못되었습니다. 캡슐 높이는 반지름의 두 배 이상이어야 합니다.");
                }
                if (skill is MonsterComboAttackSO combo)
                {
                    if (combo.hits == null || combo.hits.Count == 0 || combo.hits.Any(h => h.hitStartTime < 0 || h.hitDuration <= 0 || h.hitStartTime + h.hitDuration > combo.attackDuration || h.damage < 0) || combo.baseDamage < 0 || combo.hitBoxRadius <= 0) Issue(issues, "error", path, "연속 공격 타격 구간·피해·반경을 확인하세요.");
                    Issue(issues, "warning", path, "프로젝트 연속 공격은 구형 히트박스를 사용합니다.");
                }
                if (skill is MonsterDashAttackSO dash)
                {
                    if (dash.startupTime < 0 || dash.dashDuration <= 0 || dash.recoveryTime < 0 || dash.dashSpeed < 0 || dash.damage < 0 || dash.hitBoxRadius <= 0) Issue(issues, "error", path, "돌진 시간·속도·피해·반경을 확인하세요.");
                    Issue(issues, "warning", path, "프로젝트 돌진 공격은 구형 히트박스를 사용합니다.");
                }
                if (skill is MonsterJumpSlamSO jump && (jump.startupTime < 0 || jump.jumpDuration <= 0 || jump.recoveryTime < 0 || jump.jumpHeight < 0 || jump.slamRadius <= 0 || jump.damage < 0)) Issue(issues, "error", path, "점프 시간·높이·피해·반경을 확인하세요.");
                if (skill is MonsterSelfDestructSO bomb && (bomb.explosionRadius <= 0 || bomb.explosionDelay < 0 || bomb.damage < 0)) Issue(issues, "error", path, "자폭 시간·피해·반경을 확인하세요.");
                if (descriptor != null && (bool?)descriptor["previewSupported"] != true) Issue(issues, "warning", path, "이 사용자 정의 공격은 전용 타격 범위 미리보기를 제공하지 않습니다.");
            }
            catch (Exception exception) { Issue(issues, "error", path, exception.Message); }
            finally { Object.DestroyImmediate(skill); }
        }

        public static GameObject ResolveTemplate(JObject blueprint)
            => blueprint["template"] == null || blueprint["template"].Type == JTokenType.Null
                ? AssetDatabase.LoadAssetAtPath<GameObject>(BaekhoEnemyForgeBoss.IsBoss(blueprint) ? BaekhoEnemyForgeBoss.TemplatePath : BaekhoEnemyForgeCatalog.TemplatePath)
                : (GameObject)BaekhoEnemyForgeCatalog.Resolve(blueprint["template"], typeof(GameObject));

        private static Transform ModelChild(GameObject root)
            => root.transform.Cast<Transform>().FirstOrDefault(t => t.GetComponentInChildren<Animator>(true) != null);

        private static bool SameModel(GameObject root, GameObject model)
        {
            var child = ModelChild(root);
            if (child == null) return false;
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(child.gameObject);
            return source == model || (source != null && AssetDatabase.GetAssetPath(source) == AssetDatabase.GetAssetPath(model));
        }

        public static JObject Build(JObject blueprint)
        {
            blueprint = BaekhoEnemyForgeBoss.Normalize(blueprint);
            var validation = Validate(blueprint);
            if (!(bool)validation["valid"]) throw new InvalidDataException(string.Join("\n", ((JArray)validation["issues"]).Where(i => (string)i["severity"] == "error").Select(i => (string)i["message"])));
            string folder = OutputFolder(blueprint);
            var assets = new JArray(); var createdFolders = new List<string>(); GameObject root = null;
            try
            {
                EnsureFolder(folder, createdFolders);
                var template = ResolveTemplate(blueprint);
                root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(template));
                if (BaekhoEnemyForgeBoss.IsBoss(blueprint)) BaekhoEnemyForgeBoss.ConvertRoot(root);
                root.name = (string)blueprint["name"];
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.transform.localScale = Vector3.one;
                var model = (GameObject)BaekhoEnemyForgeCatalog.Resolve(blueprint["model"], typeof(GameObject));
                var modelRoot = ModelChild(root);
                bool sameModel = SameModel(root, model);
                if (!sameModel)
                {
                    if (modelRoot != null) Object.DestroyImmediate(modelRoot.gameObject);
                    var clone = (GameObject)PrefabUtility.InstantiatePrefab(model, root.scene);
                    clone.transform.SetParent(root.transform, false); modelRoot = clone.transform;
                    var fade = root.GetComponent<EnemyDeathFade>();
                    if (fade != null) { var serialized = new SerializedObject(fade); serialized.FindProperty("_targets").arraySize = 0; serialized.ApplyModifiedPropertiesWithoutUndo(); }
                }
                var modelTransform = (JObject)blueprint["modelTransform"];
                modelRoot.localPosition = Vector(modelTransform["position"]); modelRoot.localEulerAngles = Vector(modelTransform["rotation"]); modelRoot.localScale = Vector(modelTransform["scale"]);
                int enemyLayer = LayerMask.NameToLayer("Enemy");
                root.layer = enemyLayer;
                foreach (var transform in modelRoot.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = enemyLayer;
                var animator = modelRoot.GetComponentInChildren<Animator>(true);
                string controllerPath = folder + "/" + root.name + ".controller";
                var controller = BaekhoEnemyForgeAnimation.CreateController((JObject)blueprint["animation"], controllerPath);
                assets.Add(controllerPath); animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
                BaekhoEnemyForgeAnimation.ConfigureDriver(root, animator, (JObject)blueprint["animation"]);
                var data = BaekhoEnemyForgeBoss.IsBoss(blueprint) ? (EnemyData)ScriptableObject.CreateInstance<BossData>() : ScriptableObject.CreateInstance<EnemyData>(); data.name = root.name + "_Stats";
                BaekhoEnemyForgeCatalog.ApplyValues(data, (JObject)blueprint["stats"], Stats);
                var skills = ScriptableObject.CreateInstance<MonsterSkillDataSO>(); skills.name = root.name + "_Skills"; skills.monsterName = root.name; skills.skillData = new List<MonsterSkillData>();
                int index = 0;
                foreach (JObject config in (JArray)blueprint["attacks"])
                {
                    var skill = CreateSkill(config); skill.name = root.name + "_Attack" + (++index);
                    string path = folder + "/" + skill.name + ".asset"; AssetDatabase.CreateAsset(skill, path); assets.Add(path); skills.skillData.Add(skill);
                }
                if (blueprint["selfDestruct"] is JObject selfConfig)
                {
                    skills.selfDestruct = (MonsterSelfDestructSO)CreateSkill(selfConfig); skills.selfDestruct.name = root.name + "_SelfDestruct";
                    string path = folder + "/" + skills.selfDestruct.name + ".asset"; AssetDatabase.CreateAsset(skills.selfDestruct, path); assets.Add(path);
                }
                string skillsPath = folder + "/" + skills.name + ".asset"; AssetDatabase.CreateAsset(skills, skillsPath); assets.Add(skillsPath);
                data.monsterSkillData = skills;
                string dataPath = folder + "/" + data.name + ".asset"; AssetDatabase.CreateAsset(data, dataPath); assets.Add(dataPath);
                var enemyStat = new SerializedObject(root.GetComponent<EnemyStat>()); enemyStat.FindProperty("baseData").objectReferenceValue = data; enemyStat.FindProperty("CurrentHp").floatValue = 0; enemyStat.ApplyModifiedPropertiesWithoutUndo();
                foreach (var component in (JObject)blueprint["components"])
                    BaekhoEnemyForgeCatalog.ApplyValues(root.GetComponent(component.Key), (JObject)component.Value, BaekhoEnemyForgeCatalog.ComponentPaths[component.Key]);
                var enemy = root.GetComponent<Enemy>(); enemy.EnemyStat = root.GetComponent<EnemyStat>(); enemy.stateName = ""; enemy.IsHurt = false; enemy.IsGroggy = false; enemy.IsRoot = false; enemy.PendingGroggy = false; enemy.StaggerResistLevel = data.staggerResistLevel;
                var hp = root.transform.Find("HpBar"); if (hp != null) hp.localPosition = Vector(blueprint["hpBarPosition"]);
                var point = root.transform.Find("VFXPoint");
                if (point == null) { point = new GameObject("VFXPoint").transform; point.SetParent(root.transform, false); }
                point.gameObject.layer = enemyLayer; point.localPosition = Vector(blueprint["vfxPosition"]);
                var anchor = root.GetComponent<HitVfxAnchor>() ?? root.AddComponent<HitVfxAnchor>(); var anchorSerialized = new SerializedObject(anchor); anchorSerialized.FindProperty("spawnPoint").objectReferenceValue = point; anchorSerialized.ApplyModifiedPropertiesWithoutUndo();
                if (data is BossData bossData) BaekhoEnemyForgeBoss.Build(root, blueprint, bossData, skills, folder, assets);
                string prefabPath = folder + "/" + root.name + ".prefab";
                if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null) throw new IOException("Prefab save did not return an asset.");
                assets.Add(prefabPath);
                foreach (string path in assets.Values<string>())
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path)) if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (saved == null || saved.GetComponent<EnemyStat>() == null) throw new IOException("Saved prefab could not be loaded.");
                return new JObject { ["prefabPath"] = prefabPath, ["assets"] = assets, ["warnings"] = validation["issues"] };
            }
            catch
            {
                // This folder was verified absent before admission, so rollback cannot remove user assets.
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
                for (int i = createdFolders.Count - 1; i >= 0; i--)
                {
                    string candidate = createdFolders[i];
                    if (candidate == folder || !Directory.Exists(candidate)) continue;
                    if (!Directory.EnumerateFileSystemEntries(candidate).Any()) AssetDatabase.DeleteAsset(candidate);
                }
                throw;
            }
            finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
        }

        public static MonsterSkillData CreateSkill(JObject config)
        {
            Type type = BaekhoEnemyForgeCatalog.AttackType((string)config["typeId"]) ?? throw new InvalidDataException("Unknown attack type.");
            var skill = (MonsterSkillData)ScriptableObject.CreateInstance(type);
            try
            {
                var descriptor = BaekhoEnemyForgeCatalog.Descriptor(type.FullName);
                if (descriptor?["defaults"] is JObject defaults) BaekhoEnemyForgeCatalog.ApplyValues(skill, defaults);
                BaekhoEnemyForgeCatalog.ApplyValues(skill, (JObject)config["values"]); return skill;
            }
            catch { Object.DestroyImmediate(skill); throw; }
        }

        private static Vector3 Vector(JToken token)
        { var values = BaekhoEnemyForgeCatalog.Numbers(token, 3); return new Vector3(values[0], values[1], values[2]); }

        public static void EnsureFolder(string path, List<string> created)
        {
            string current = "Assets";
            foreach (string segment in path.Split('/').Skip(1))
            {
                string next = current + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next))
                {
                    BaekhoEnemyForgeBridge.CheckPath(Path.GetFullPath(next));
                    if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, segment))) throw new IOException("Could not create folder: " + next);
                    created?.Add(next);
                }
                current = next;
            }
        }
    }
}
