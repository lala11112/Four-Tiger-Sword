using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace StudioBaekho.EnemyForge
{
    /// <summary>Project adapter: these files intentionally compile in Assembly-CSharp-Editor.</summary>
    public static class BaekhoEnemyForgeCatalog
    {
        public const string TemplatePath = "Assets/_Project/Prefabs/Drone.prefab";
        public const string GeneratedAttacks = "Assets/_Project/Scripts/Enemy/Generated";
        private static readonly HashSet<string> RegistrationRecoveryFailures = new HashSet<string>();
        public static readonly Dictionary<string, string[]> ComponentPaths = new Dictionary<string, string[]>
        {
            ["Enemy"] = new[] { "HurtDuration", "_targetMemoryDuration", "ApproachTimeout", "ApproachStuckTimeout", "FailedAttackRetryDelay", "GroggyDuration", "RootDuration", "_telegraphEffect" },
            ["PoiseHandler"] = new[] { "_maxPoise", "_poiseRegen", "_breakDuration" },
            ["NavMeshAgent"] = new[] { "m_Speed", "m_Acceleration", "m_AngularSpeed", "m_Radius", "m_Height", "m_BaseOffset", "m_StoppingDistance" },
            ["SphereCollider"] = new[] { "m_Radius", "m_Center" },
            ["CapsuleCollider"] = new[] { "m_Radius", "m_Height", "m_Center", "m_Direction" },
            ["FanVisionSensor"] = new[] { "_range", "_halfAngle", "_proximityRange", "_rayCount", "_heightOffset", "_visibilityMask" },
            ["EnemyDeathFade"] = new[] { "_delay", "_duration" }
        };

        public static Type AttackType(string typeId, bool requireRegistered = true)
            => TypeCache.GetTypesDerivedFrom<MonsterSkillData>().FirstOrDefault(t => !t.IsAbstract && !t.ContainsGenericParameters && t.FullName == typeId && (!requireRegistered || IsRegistered(t)));

        public static string AttackFingerprint(string attackId)
        {
            string folder = GeneratedAttacks + "/" + attackId;
            BaekhoEnemyForgeBridge.CheckPath(Path.GetFullPath(folder));
            var bytes = new MemoryStream();
            foreach (string path in Directory.GetFiles(folder, "*.cs", SearchOption.TopDirectoryOnly).Concat(new[] { folder + "/Attack.enemyattack.json" }).OrderBy(p => p, StringComparer.Ordinal))
            {
                byte[] name = Encoding.UTF8.GetBytes(Path.GetFileName(path) + "\n"), data = File.ReadAllBytes(path);
                bytes.Write(name, 0, name.Length); bytes.Write(data, 0, data.Length); bytes.WriteByte(0);
            }
            using (bytes) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
        }

        public static bool IsRegistered(Type type)
        {
            var descriptor = Descriptor(type.FullName);
            if (descriptor == null) return !type.FullName.StartsWith("Baekho.Generated.", StringComparison.Ordinal);
            string id = (string)descriptor["attackId"];
            if (string.IsNullOrEmpty(id) || !System.Text.RegularExpressions.Regex.IsMatch(id, @"\A[a-zA-Z0-9_-]{1,80}\z")) return false;
            string marker = "Library/BaekhoEnemyForge/attacks/" + id + ".json";
            try
            {
                string fingerprint = AttackFingerprint(id);
                if (File.Exists(marker)) return (string)JObject.Parse(File.ReadAllText(marker))["fingerprint"] == fingerprint;
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed) return false;
                string recovery = id + ":" + fingerprint;
                if (RegistrationRecoveryFailures.Contains(recovery)) return false;
                try
                {
                    // Library is disposable. Re-run the same admission checks; never publish a type merely because its descriptor exists.
                    var validation = BaekhoEnemyForgeBridge.ValidateGeneratedAttack(new JObject { ["typeId"] = type.FullName, ["attackId"] = id });
                    return (bool?)validation["valid"] == true;
                }
                catch { RegistrationRecoveryFailures.Add(recovery); return false; }
            }
            catch (Exception) { return false; }
        }

        public static Object Resolve(JToken reference, Type expected = null)
        {
            if (reference == null || reference.Type == JTokenType.Null) return null;
            var value = reference as JObject ?? throw new InvalidDataException("Asset reference must be an object.");
            string guid = (string)value["guid"], local = (string)value["localId"];
            if (string.IsNullOrEmpty(guid) || guid.Length != 32 || !guid.All(Uri.IsHexDigit) || !long.TryParse(local, out long id))
                throw new InvalidDataException("Asset reference requires a GUID and string localId.");
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) throw new InvalidDataException("Asset no longer exists: " + guid);
            foreach (Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (candidate == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out string candidateGuid, out long candidateId)) continue;
                if (candidateGuid != guid || candidateId != id) continue;
                if (expected != null && !expected.IsInstanceOfType(candidate)) throw new InvalidDataException("Asset is not a " + expected.Name + ": " + path);
                return candidate;
            }
            throw new InvalidDataException("Asset subobject no longer exists: " + path + " #" + local);
        }

        public static JObject AssetRef(Object asset)
        {
            if (asset == null) return null;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long local)) return null;
            return new JObject { ["guid"] = guid, ["localId"] = local.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["path"] = AssetDatabase.GetAssetPath(asset), ["name"] = asset.name, ["kind"] = Kind(asset) };
        }

        private static string Kind(Object value)
        {
            if (value is AnimationClip) return "AnimationClip";
            if (value is RuntimeAnimatorController) return "AnimatorController";
            if (value is Material) return "Material";
            if (value is AudioClip) return "AudioClip";
            if (value is GameObject gameObject)
                return AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(gameObject)) is ModelImporter ? "Model" : "GameObject";
            return value.GetType().Name;
        }

        public static JObject Describe(Object instance, IEnumerable<string> paths = null)
        {
            var serialized = new SerializedObject(instance);
            var fields = new JArray();
            var values = new JObject();
            if (paths != null)
            {
                foreach (string path in paths)
                {
                    var property = serialized.FindProperty(path);
                    if (property == null) continue;
                    fields.Add(Schema(property, instance.GetType())); values[path] = Read(property);
                }
            }
            else
            {
                var iterator = serialized.GetIterator();
                bool enter = true;
                while (iterator.NextVisible(enter))
                {
                    enter = false;
                    if (iterator.name == "m_Script" || iterator.name.StartsWith("m_", StringComparison.Ordinal)) continue;
                    fields.Add(Schema(iterator, instance.GetType())); values[iterator.name] = Read(iterator);
                }
            }
            return new JObject { ["fields"] = fields, ["defaults"] = values };
        }

        private static JObject Schema(SerializedProperty p, Type owner)
        {
            string type;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: case SerializedPropertyType.LayerMask: type = "integer"; break;
                case SerializedPropertyType.Float: type = "number"; break;
                case SerializedPropertyType.Boolean: type = "boolean"; break;
                case SerializedPropertyType.String: type = "string"; break;
                case SerializedPropertyType.Enum: type = "enum"; break;
                case SerializedPropertyType.Vector2: type = "vector2"; break;
                case SerializedPropertyType.Vector3: type = "vector3"; break;
                case SerializedPropertyType.Color: type = "color"; break;
                case SerializedPropertyType.ObjectReference: type = "asset"; break;
                case SerializedPropertyType.Generic: type = p.isArray ? "array" : "object"; break;
                default: type = "unsupported"; break;
            }
            var field = FindField(owner, p.name);
            var schema = new JObject { ["path"] = p.name, ["label"] = p.displayName, ["type"] = type, ["tooltip"] = p.tooltip ?? "" };
            var min = field?.GetCustomAttribute<MinAttribute>();
            var range = field?.GetCustomAttribute<RangeAttribute>();
            if (min != null) schema["min"] = min.min;
            if (range != null) { schema["min"] = range.min; schema["max"] = range.max; }
            if (p.name == "criticalChance") { schema["percent"] = true; schema["min"] = 0; schema["max"] = 1; }
            if (p.propertyType == SerializedPropertyType.ObjectReference) schema["assetType"] = field?.FieldType.Name ?? p.type;
            if (p.propertyType == SerializedPropertyType.Enum)
            {
                var choices = new JArray();
                Type enumType = field?.FieldType ?? (owner?.IsEnum == true ? owner : null);
                if (enumType?.IsEnum == true)
                    foreach (object choice in Enum.GetValues(enumType)) choices.Add(new JObject { ["label"] = Enum.GetName(enumType, choice), ["value"] = Convert.ToInt64(choice) });
                else for (int i = 0; i < p.enumNames.Length; i++) choices.Add(new JObject { ["label"] = p.enumDisplayNames[i], ["value"] = i });
                schema["options"] = choices;
            }
            if (type == "array")
            {
                var children = new JArray();
                int originalSize = p.arraySize;
                if (originalSize == 0) p.arraySize = 1;
                if (p.arraySize > 0)
                {
                    var element = p.GetArrayElementAtIndex(0);
                    Type elementType = field?.FieldType.IsArray == true ? field.FieldType.GetElementType()
                        : field?.FieldType.IsGenericType == true ? field.FieldType.GetGenericArguments()[0] : null;
                    if (element.propertyType == SerializedPropertyType.Generic)
                        foreach (var child in Children(element)) children.Add(Schema(child, elementType));
                    else schema["element"] = Schema(element, elementType);
                }
                if (originalSize == 0) p.arraySize = 0;
                schema["children"] = children;
            }
            else if (type == "object")
            {
                var children = new JArray();
                foreach (var child in Children(p)) children.Add(Schema(child, field?.FieldType));
                schema["children"] = children;
            }
            return schema;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        private static IEnumerable<SerializedProperty> Children(SerializedProperty parent)
        {
            var cursor = parent.Copy(); var end = parent.GetEndProperty();
            if (!cursor.NextVisible(true)) yield break;
            do
            {
                if (SerializedProperty.EqualContents(cursor, end) || cursor.depth <= parent.depth) yield break;
                if (cursor.depth == parent.depth + 1) yield return cursor.Copy();
            } while (cursor.NextVisible(false));
        }

        private static JArray Vector(params float[] values) => new JArray(values.Select(v => new JValue(v)));

        public static JToken Read(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return new JValue(p.longValue);
                case SerializedPropertyType.LayerMask: case SerializedPropertyType.Enum: return new JValue(p.intValue);
                case SerializedPropertyType.Float: return new JValue(p.doubleValue);
                case SerializedPropertyType.Boolean: return new JValue(p.boolValue);
                case SerializedPropertyType.String: return new JValue(p.stringValue);
                case SerializedPropertyType.ObjectReference: return (JToken)AssetRef(p.objectReferenceValue) ?? JValue.CreateNull();
                case SerializedPropertyType.Vector2: return Vector(p.vector2Value.x, p.vector2Value.y);
                case SerializedPropertyType.Vector3: return Vector(p.vector3Value.x, p.vector3Value.y, p.vector3Value.z);
                case SerializedPropertyType.Color: return Vector(p.colorValue.r, p.colorValue.g, p.colorValue.b, p.colorValue.a);
                case SerializedPropertyType.Generic:
                    if (p.isArray) { var array = new JArray(); for (int i = 0; i < p.arraySize; i++) array.Add(Read(p.GetArrayElementAtIndex(i))); return array; }
                    var result = new JObject(); foreach (var child in Children(p)) result[child.name] = Read(child); return result;
                default: return JValue.CreateNull();
            }
        }

        public static void ApplyValues(Object target, JObject values, IEnumerable<string> allowed = null)
        {
            if (values == null) return;
            var serialized = new SerializedObject(target);
            HashSet<string> whitelist = allowed == null ? null : new HashSet<string>(allowed);
            foreach (var pair in values)
            {
                if (pair.Key == "m_Script" || pair.Key.Contains(".") || pair.Key.Contains("[")) throw new InvalidDataException("Unsupported property: " + pair.Key);
                if (whitelist != null && !whitelist.Contains(pair.Key)) throw new InvalidDataException("Property is not editable: " + pair.Key);
                var property = serialized.FindProperty(pair.Key) ?? throw new InvalidDataException("Unknown serialized field: " + pair.Key);
                Write(property, pair.Value, target.GetType());
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Write(SerializedProperty p, JToken value, Type owner)
        {
            var member = FindField(owner, p.name);
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (value?.Type != JTokenType.Integer) throw new InvalidDataException(p.propertyPath + " must be an integer.");
                    p.longValue = value.Value<long>(); break;
                case SerializedPropertyType.LayerMask: case SerializedPropertyType.Enum:
                    if (value?.Type != JTokenType.Integer) throw new InvalidDataException(p.propertyPath + " must be an integer.");
                    p.intValue = value.Value<int>(); break;
                case SerializedPropertyType.Float:
                    if (value == null || (value.Type != JTokenType.Float && value.Type != JTokenType.Integer) || double.IsNaN(value.Value<double>()) || double.IsInfinity(value.Value<double>())) throw new InvalidDataException(p.propertyPath + " must be finite.");
                    p.doubleValue = value.Value<double>(); break;
                case SerializedPropertyType.Boolean:
                    if (value?.Type != JTokenType.Boolean) throw new InvalidDataException(p.propertyPath + " must be boolean.");
                    p.boolValue = value.Value<bool>(); break;
                case SerializedPropertyType.String:
                    if (value?.Type != JTokenType.String || value.Value<string>().Length > 4096) throw new InvalidDataException(p.propertyPath + " must be text of at most 4096 characters.");
                    p.stringValue = value.Value<string>(); break;
                case SerializedPropertyType.ObjectReference:
                    Type expected = member?.FieldType ?? (owner != null && typeof(Object).IsAssignableFrom(owner) ? owner : null);
                    var asset = Resolve(value, expected);
                    var old = p.objectReferenceValue; p.objectReferenceValue = asset;
                    if (asset != null && p.objectReferenceValue != asset) { p.objectReferenceValue = old; throw new InvalidDataException("Incompatible asset for " + p.propertyPath); }
                    break;
                case SerializedPropertyType.Vector2: var v2 = Numbers(value, 2); p.vector2Value = new Vector2(v2[0], v2[1]); break;
                case SerializedPropertyType.Vector3: var v3 = Numbers(value, 3); p.vector3Value = new Vector3(v3[0], v3[1], v3[2]); break;
                case SerializedPropertyType.Color: var c = Numbers(value, 4); p.colorValue = new Color(c[0], c[1], c[2], c[3]); break;
                case SerializedPropertyType.Generic:
                    if (p.isArray)
                    {
                        if (!(value is JArray array) || array.Count > 128) throw new InvalidDataException(p.propertyPath + " requires an array of at most 128 values.");
                        p.arraySize = array.Count;
                        Type elementType = member?.FieldType.IsArray == true ? member.FieldType.GetElementType()
                            : member?.FieldType.IsGenericType == true ? member.FieldType.GetGenericArguments()[0] : null;
                        for (int i = 0; i < array.Count; i++) Write(p.GetArrayElementAtIndex(i), array[i], elementType);
                    }
                    else
                    {
                        if (!(value is JObject fields)) throw new InvalidDataException(p.propertyPath + " must be an object.");
                        foreach (var field in fields)
                        {
                            if (field.Key.Contains(".") || field.Key.Contains("[")) throw new InvalidDataException("Invalid nested property.");
                            var child = p.FindPropertyRelative(field.Key) ?? throw new InvalidDataException("Unknown field: " + p.propertyPath + "." + field.Key);
                            Write(child, field.Value, member?.FieldType ?? owner);
                        }
                    }
                    break;
                default:
                    if (value == null || value.Type == JTokenType.Null) break; // Read-only fields retain their constructed/default value.
                    throw new InvalidDataException("Unsupported property: " + p.propertyPath + " (" + p.propertyType + ")");
            }
        }

        public static float[] Numbers(JToken value, int count)
        {
            if (!(value is JArray array) || array.Count != count) throw new InvalidDataException("Expected " + count + " numeric coordinates.");
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (array[i].Type != JTokenType.Integer && array[i].Type != JTokenType.Float) throw new InvalidDataException("Coordinates must be numeric.");
                result[i] = array[i].Value<float>();
                if (float.IsNaN(result[i]) || float.IsInfinity(result[i])) throw new InvalidDataException("Coordinates must be finite.");
            }
            return result;
        }

        public static JObject Descriptor(string typeId)
        {
            if (!Directory.Exists(GeneratedAttacks)) return null;
            foreach (string file in Directory.GetFiles(GeneratedAttacks, "Attack.enemyattack.json", SearchOption.AllDirectories))
            {
                if (new FileInfo(file).Length > 256 * 1024) continue;
                try { var descriptor = JObject.Parse(File.ReadAllText(file)); if ((string)descriptor["typeId"] == typeId && (int?)descriptor["version"] == 1) return descriptor; }
                catch (Exception) { /* A broken descriptor never hides working built-in types. */ }
            }
            return null;
        }

        public static JObject AttackDefinition(Type type)
        {
            var instance = ScriptableObject.CreateInstance(type);
            try
            {
                var descriptor = Descriptor(type.FullName);
                if (descriptor?["defaults"] is JObject defaults) ApplyValues(instance, defaults);
                var described = Describe(instance);
                string name = type.Name;
                if (type == typeof(MonsterDefaultAttackSO)) name = "일반 공격";
                else if (type == typeof(MonsterDashAttackSO)) name = "돌진 공격";
                else if (type == typeof(MonsterComboAttackSO)) name = "연속 공격";
                else if (type == typeof(MonsterJumpSlamSO)) name = "점프 내려찍기";
                else if (type == typeof(MonsterSelfDestructSO)) name = "HP 조건 자폭";
                return new JObject { ["typeId"] = type.FullName, ["name"] = (string)descriptor?["name"] ?? name,
                    ["description"] = (string)descriptor?["description"] ?? "기존 프로젝트 전투 로직을 사용합니다. 피해 값은 공격력에 곱하는 배율입니다.",
                    ["category"] = typeof(MonsterSelfDestructSO).IsAssignableFrom(type) ? "selfDestruct" : "normal",
                    ["fields"] = described["fields"], ["defaults"] = described["defaults"],
                    ["previewSupported"] = descriptor == null ? type.Assembly == typeof(MonsterDefaultAttackSO).Assembly && new[] { typeof(MonsterDefaultAttackSO), typeof(MonsterDashAttackSO), typeof(MonsterComboAttackSO), typeof(MonsterJumpSlamSO), typeof(MonsterSelfDestructSO) }.Contains(type) : (bool?)descriptor["previewSupported"] == true,
                    ["generated"] = descriptor != null, ["preview"] = descriptor?["preview"]?.DeepClone() };
            }
            finally { Object.DestroyImmediate(instance); }
        }

        public static JObject Get(string kind = "normal")
        {
            if (kind != "normal" && kind != "boss") throw new InvalidDataException("Unknown monster kind.");
            string templatePath = kind == "boss" ? BaekhoEnemyForgeBoss.TemplatePath : TemplatePath;
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(templatePath) ?? throw new InvalidDataException("Template prefab was not found: " + templatePath);
            var stat = template.GetComponent<EnemyStat>() ?? throw new InvalidDataException(template.name + " has no EnemyStat.");
            var statSerialized = new SerializedObject(stat);
            var data = statSerialized.FindProperty("baseData").objectReferenceValue as EnemyData ?? throw new InvalidDataException(template.name + " has no EnemyData.");
            var stats = Describe(data);
            ((JArray)stats["fields"]).Where(f => (string)f["path"] == "monsterSkillData").ToList().ForEach(f => f.Remove());
            ((JObject)stats["defaults"]).Remove("monsterSkillData");
            var warnings = new JArray();
            if (data.criticalChance < 0f || data.criticalChance > 1f) warnings.Add(template.name + " 치명 확률 원본값은 " + data.criticalChance + "이며 실제 런타임 확률 " + (Mathf.Clamp01(data.criticalChance) * 100f) + "%로 불러왔습니다.");
            stats["defaults"]["criticalChance"] = Mathf.Clamp01(data.criticalChance);
            var enemy = template.GetComponent<Enemy>();
            if (kind != "boss" && enemy.StaggerResistLevel != data.staggerResistLevel) warnings.Add(template.name + "의 EnemyData 경직저항과 Enemy 경직저항이 다릅니다. 실제 Enemy 값 " + enemy.StaggerResistLevel + "을 기준으로 불러왔습니다.");
            stats["defaults"]["staggerResistLevel"] = (int)enemy.StaggerResistLevel;

            var components = new JObject(); var componentFields = new JObject();
            foreach (var entry in ComponentPaths)
            {
                var component = template.GetComponent(entry.Key);
                if (component == null) continue;
                var description = Describe(component, entry.Value);
                components[entry.Key] = description["defaults"]; componentFields[entry.Key] = description["fields"];
            }
            var model = template.transform.Cast<Transform>().FirstOrDefault(t => t.GetComponentInChildren<Animator>(true) != null);
            var animator = model?.GetComponentInChildren<Animator>(true);
            var modelSource = model == null ? null : PrefabUtility.GetCorrespondingObjectFromOriginalSource(model.gameObject);
            if (modelSource == null && model != null)
                modelSource = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(model.gameObject));
            if (modelSource == null) modelSource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Models/Drone.fbx");
            var controller = animator?.runtimeAnimatorController as AnimatorController;
            var states = new JObject();
            if (controller != null)
                foreach (var state in controller.layers[0].stateMachine.states)
                {
                    var motion = state.state.motion;
                    if (state.state.name == "Move" && motion is BlendTree movement)
                    {
                        var forward = movement.children.FirstOrDefault(c => c.position.y > 0f && Mathf.Abs(c.position.x) < .01f);
                        if (forward.motion != null) motion = forward.motion;
                    }
                    states[state.state.name] = AssetRef(FirstClip(motion));
                }
            var idleClip = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(modelSource)).OfType<AnimationClip>().FirstOrDefault(c => c.name.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0);
            if (states["Idle"] == null || states["Idle"].Type == JTokenType.Null) states["Idle"] = AssetRef(idleClip) ?? states["Move"]?.DeepClone();
            if (states["Groggy"] == null || states["Groggy"].Type == JTokenType.Null)
            {
                states["Groggy"] = states["Idle"]?.DeepClone();
                if (states["Groggy"] is JObject) warnings.Add("템플릿에 전용 Groggy 클립이 없어 초기 매핑은 Idle 클립을 사용합니다. 필요하면 그로기 클립을 지정하세요.");
            }
            var attacks = new JArray();
            if (data.monsterSkillData?.skillData != null)
                foreach (var skill in data.monsterSkillData.skillData.Where(s => s != null)) attacks.Add(Config(skill));
            var hp = template.transform.Find("HpBar"); var vfx = template.transform.Find("VFXPoint");
            var blueprint = new JObject { ["version"] = 3, ["kind"] = kind, ["name"] = kind == "boss" ? "NewBoss" : "NewEnemy", ["template"] = AssetRef(template), ["model"] = AssetRef(modelSource),
                ["modelTransform"] = new JObject { ["position"] = model == null ? Vector(0, 0, 0) : Vector(model.localPosition.x, model.localPosition.y, model.localPosition.z),
                    ["rotation"] = model == null ? Vector(0, 0, 0) : Vector(model.localEulerAngles.x, model.localEulerAngles.y, model.localEulerAngles.z),
                    ["scale"] = model == null ? Vector(1, 1, 1) : Vector(model.localScale.x, model.localScale.y, model.localScale.z) },
                ["stats"] = stats["defaults"], ["components"] = components,
                ["animation"] = new JObject { ["mode"] = "clips", ["controller"] = AssetRef(animator?.runtimeAnimatorController), ["states"] = states,
                    ["move"] = BaekhoEnemyForgeAnimation.ImportMove(controller, warnings) },
                ["attacks"] = attacks, ["selfDestruct"] = data.monsterSkillData?.selfDestruct == null ? null : Config(data.monsterSkillData.selfDestruct),
                ["anchors"] = new JObject { ["hpBar"] = "auto", ["vfx"] = "auto" },
                ["hpBarPosition"] = hp == null ? Vector(0, 3, 0) : Vector(hp.localPosition.x, hp.localPosition.y, hp.localPosition.z),
                ["vfxPosition"] = vfx == null ? Vector(0, 2, 0) : Vector(vfx.localPosition.x, vfx.localPosition.y, vfx.localPosition.z) };
            if (blueprint["animation"]["move"]?.Type == JTokenType.Null) ((JObject)blueprint["animation"]).Remove("move");
            if (kind == "boss")
            {
                blueprint["boss"] = BaekhoEnemyForgeBoss.Defaults(); blueprint["selfDestruct"] = null;
                blueprint["boss"]["phases"][0]["phaseMaxHp"] = blueprint["stats"]["maxHp"].DeepClone();
                if (states["PhaseDown"] == null) states["PhaseDown"] = states["Die"]?.DeepClone() ?? JValue.CreateNull();
                if (states["PhaseRevive"] == null) states["PhaseRevive"] = JValue.CreateNull();
                blueprint["stats"]["staggerResistLevel"] = (int)StaggerResistLevel.HIGH;
                blueprint["animation"]["mode"] = "controller";
                foreach (JObject config in attacks.OfType<JObject>().ToArray()) if (typeof(MonsterSelfDestructSO).IsAssignableFrom(AttackType((string)config["typeId"]))) config.Remove();
                foreach (JObject config in attacks.OfType<JObject>())
                    if (string.IsNullOrWhiteSpace((string)config["values"]["animName"]))
                    {
                        string stateName = (string)config["typeId"] == typeof(MonsterDashAttackSO).FullName ? "Dash" : "Attack_" + attacks.IndexOf(config);
                        config["values"]["animName"] = stateName; states[stateName] = null;
                        warnings.Add("원본 공격의 상태명이 비어 있어 " + stateName + "으로 지정했습니다. 해당 클립을 연결하세요.");
                    }
                var required = new HashSet<string>(new[] { "Idle", "Move", "Groggy" }.Concat(attacks.Select(a => (string)a["values"]["animName"])));
                if (required.Any(state => states[state] == null || states[state].Type == JTokenType.Null))
                {
                    blueprint["animation"]["mode"] = "clips";
                    warnings.Add("기존 보스 컨트롤러의 애니메이션 연결이 누락되어 있습니다. 필수 상태에 모델과 호환되는 클립을 연결한 뒤 생성하세요. 원본 컨트롤러는 변경하지 않습니다.");
                }
            }
            var definitions = new JArray(TypeCache.GetTypesDerivedFrom<MonsterSkillData>().Where(t => !t.IsAbstract && !t.ContainsGenericParameters && (kind != "boss" || !typeof(MonsterSelfDestructSO).IsAssignableFrom(t)) && IsRegistered(t)).OrderBy(t => t.FullName).Select(AttackDefinition));
            var assets = Assets();
            // Existing defaults can refer to shared SFX/VFX outside _Project. Include those exact
            // dependencies without expanding the AI context to every third-party asset in Assets.
            IncludeReferencedAssets(assets, blueprint, definitions);
            var result = new JObject { ["projectRoot"] = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/'), ["unityVersion"] = Application.unityVersion, ["kind"] = kind,
                ["assets"] = assets, ["attacks"] = definitions,
                ["statsFields"] = stats["fields"], ["componentFields"] = componentFields, ["template"] = blueprint, ["warnings"] = warnings };
            if (kind == "boss") result["boss"] = BaekhoEnemyForgeBoss.Catalog();
            return result;
        }

        public static AnimationClip FirstClip(Motion motion)
        {
            if (motion is AnimationClip clip) return clip;
            if (motion is BlendTree tree)
                foreach (var child in tree.children) { var found = FirstClip(child.motion); if (found != null) return found; }
            return null;
        }

        private static JObject Config(MonsterSkillData skill)
            => new JObject { ["id"] = Guid.NewGuid().ToString("D"), ["typeId"] = skill.GetType().FullName, ["values"] = Describe(skill)["defaults"] };

        private static void IncludeReferencedAssets(JArray assets, params JToken[] sources)
        {
            var seen = new HashSet<string>(assets.OfType<JObject>().Select(a => (string)a["guid"] + ":" + (string)a["localId"]));
            void Visit(JToken token)
            {
                if (token == null) return;
                if (token is JObject reference && reference["guid"]?.Type == JTokenType.String && reference["localId"]?.Type == JTokenType.String)
                {
                    var asset = Resolve(reference);
                    var canonical = AssetRef(asset);
                    if (canonical == null || !seen.Add((string)canonical["guid"] + ":" + (string)canonical["localId"])) return;
                    if (asset is AnimationClip clip) { canonical["duration"] = clip.length; canonical["rig"] = clip.humanMotion ? "humanoid" : "generic"; }
                    if (asset is GameObject model)
                    {
                        var animator = model.GetComponentInChildren<Animator>(true);
                        canonical["rig"] = animator == null ? "none" : animator.isHuman ? "humanoid" : "generic";
                    }
                    assets.Add(canonical);
                    return;
                }
                foreach (var child in token.Children()) Visit(child);
            }
            foreach (var source in sources) Visit(source);
        }

        private static JArray Assets()
        {
            var results = new JArray(); var seen = new HashSet<string>();
            foreach (string filter in new[] { "t:GameObject", "t:AnimationClip", "t:RuntimeAnimatorController", "t:AudioClip", "t:Material" })
                foreach (string guid in AssetDatabase.FindAssets(filter, new[] { "Assets/_Project" }).OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        if (!(asset is AnimationClip) && !(asset is RuntimeAnimatorController) && !(asset is AudioClip) && !(asset is Material) && !(asset is GameObject game && AssetDatabase.LoadMainAssetAtPath(path) == game)) continue;
                        if (asset.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                        var reference = AssetRef(asset); if (reference == null || !seen.Add((string)reference["guid"] + ":" + (string)reference["localId"])) continue;
                        if (asset is AnimationClip clip) { reference["duration"] = clip.length; reference["rig"] = clip.humanMotion ? "humanoid" : "generic"; }
                        if (asset is GameObject model)
                        {
                            var animator = model.GetComponentInChildren<Animator>(true);
                            reference["rig"] = animator == null ? "none" : animator.isHuman ? "humanoid" : "generic";
                        }
                        results.Add(reference);
                    }
                }
            return results;
        }

        public static JObject SearchAssets(JObject payload)
        {
            string query = ((string)payload["query"] ?? "").Trim();
            if (query.Length > 160) throw new InvalidDataException("Asset query is too long.");
            var allowed = new HashSet<string>(new[] { "Model", "GameObject", "AnimationClip", "AnimatorController", "AudioClip", "Material" });
            var kinds = payload["kinds"] is JArray requested ? requested.Values<string>().ToArray() : allowed.ToArray();
            if (kinds.Length == 0 || kinds.Any(k => !allowed.Contains(k))) throw new InvalidDataException("Select a supported asset kind.");
            int limit = Mathf.Clamp((int?)payload["limit"] ?? 150, 1, 150);
            var wanted = new HashSet<string>(kinds); var seen = new HashSet<string>(); var results = new JArray(); int total = 0;
            string[] filters = kinds.Select(k => k == "Model" || k == "GameObject" ? "GameObject" : k).Distinct().ToArray();
            foreach (string filter in filters)
            {
                // User text is never interpolated into Unity's query grammar. Search names/paths after type discovery.
                foreach (string guid in AssetDatabase.FindAssets("t:" + filter, new[] { "Assets" }).OrderBy(g => AssetDatabase.GUIDToAssetPath(g), StringComparer.OrdinalIgnoreCase))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (filter != "AnimationClip" && query.Length > 0 && path.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        if (asset == null || asset.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                        if (asset is GameObject && AssetDatabase.LoadMainAssetAtPath(path) != asset) continue;
                        string kind = Kind(asset);
                        if (!wanted.Contains(kind)) continue;
                        if (query.Length > 0 && path.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 && asset.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var reference = AssetRef(asset);
                        if (reference == null || !seen.Add((string)reference["guid"] + ":" + (string)reference["localId"])) continue;
                        total++;
                        if (results.Count >= limit) continue;
                        if (asset is AnimationClip clip) { reference["duration"] = clip.length; reference["rig"] = clip.humanMotion ? "humanoid" : "generic"; }
                        if (asset is GameObject model)
                        {
                            var animator = model.GetComponentInChildren<Animator>(true);
                            reference["rig"] = animator == null ? "none" : animator.isHuman ? "humanoid" : "generic";
                        }
                        results.Add(reference);
                    }
                }
            }
            return new JObject { ["assets"] = results, ["total"] = total, ["truncated"] = total > results.Count };
        }
    }
}
