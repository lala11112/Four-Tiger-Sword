using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Imports the versioned JSON/PNG package produced by Studio Baekho's item generator.</summary>
public static class BaekhoItemImporter
{
    public const string OutputRoot = "Assets/_Project/Generated/BaekhoItems";
    public const string CatalogFile = "saingeom-items.v1.json";
    private const int MaxFileBytes = 32 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
    // Regression fault injection is editor-only and never assigned by the importer itself.
    internal static Action<int> AfterItemImported;

    private sealed class Package
    {
        public string projectId, status, json, manifestJson, jsonHash, manifestHash;
        public int revision;
        public readonly List<Entry> items = new List<Entry>();
    }

    private sealed class Entry
    {
        public string key, id, name, description, json;
        public int grade, price, revision;
        public ElementType element;
        public ItemEffect[] effects;
        public byte[] png;
        public bool droppedSp;
        public bool Retained => !droppedSp || effects.Length > 0;
    }

    [MenuItem("Tools/Studio Baekho/Items/Import Item Package")]
    private static void ImportMenu()
    {
        string path = EditorUtility.OpenFolderPanel("백호툴 아이템 출력 폴더", "", "");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var catalog = ImportPackage(path);
            Selection.activeObject = catalog;
            EditorGUIUtility.PingObject(catalog);
            Debug.Log($"[Baekho Items] {catalog.items.Length}개 반입 완료 ({catalog.contentStatus}): {AssetDatabase.GetAssetPath(catalog)}", catalog);
        }
        catch (Exception error) { Debug.LogError("[Baekho Items] " + error.Message); }
    }

    /// <summary>Accepts an export folder or its catalog JSON. Existing candidate GUIDs survive ID edits.</summary>
    public static ItemCatalog ImportPackage(string path)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("아이템 반입은 편집 모드에서 실행하세요.");
        Package source = ReadPackage(path); // Complete validation precedes all asset writes.
        string folder = OutputRoot + "/" + source.projectId;
        string catalogPath = folder + "/ItemCatalog.asset";
        RequireOwnedAsset<ItemCatalog>(catalogPath, a => a.projectId == source.projectId);
        foreach (Entry entry in source.items)
            RequireOwnedAsset<ItemDefinition>(folder + "/Definitions/" + entry.key + ".asset",
                a => a.projectId == source.projectId && a.candidateKey == entry.key);

        var transaction = new AssetTransaction();
        try
        {
            transaction.EnsureFolder(folder + "/Definitions");
            transaction.EnsureFolder(folder + "/Icons");
            transaction.Capture(catalogPath);
            var definitions = new List<ItemDefinition>();
            foreach (Entry entry in source.items.Where(entry => entry.Retained))
            {
                string definitionPath = folder + "/Definitions/" + entry.key + ".asset";
                transaction.Capture(definitionPath);
                Sprite sprite = null;
                if (entry.png != null)
                {
                    string iconPath = folder + "/Icons/" + entry.key + ".png";
                    transaction.Capture(iconPath);
                    if (!File.Exists(iconPath) || !File.ReadAllBytes(iconPath).SequenceEqual(entry.png))
                        File.WriteAllBytes(iconPath, entry.png);
                    AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceSynchronousImport);
                    var importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
                    if (importer == null) throw new InvalidDataException("PNG TextureImporter를 만들지 못했습니다: " + iconPath);
                    if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single ||
                        !importer.alphaIsTransparency || importer.mipmapEnabled)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        importer.spriteImportMode = SpriteImportMode.Single;
                        importer.alphaIsTransparency = true;
                        importer.mipmapEnabled = false;
                        importer.SaveAndReimport();
                    }
                    sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
                    if (sprite == null) throw new InvalidDataException("Sprite 반입 실패: " + iconPath);
                }

                ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(definitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, definitionPath);
                }
                definition.name = entry.id;
                definition.projectId = source.projectId;
                definition.candidateKey = entry.key;
                definition.itemId = entry.id;
                definition.displayName = entry.name;
                definition.description = entry.description;
                definition.effectText = entry.droppedSp ? entry.description : string.Empty;
                definition.element = entry.element;
                definition.grade = entry.grade;
                definition.purchasePrice = entry.price;
                definition.effects = entry.effects;
                definition.icon = sprite;
                definition.contentStatus = source.status;
                definition.contentRevision = entry.revision;
                definition.sourceJson = entry.json;
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssetIfDirty(definition);
                definitions.Add(definition);
                AfterItemImported?.Invoke(definitions.Count);
            }

            foreach (Entry entry in source.items.Where(entry => !entry.Retained))
            {
                string definitionPath = folder + "/Definitions/" + entry.key + ".asset";
                if (!File.Exists(definitionPath)) continue;
                transaction.Capture(definitionPath);
                if (!AssetDatabase.DeleteAsset(definitionPath))
                    throw new IOException("폐기된 아이템 정의를 삭제하지 못했습니다: " + definitionPath);
            }

            ItemCatalog catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemCatalog>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }
            catalog.name = "Baekho Items " + source.projectId;
            catalog.projectId = source.projectId;
            catalog.contentRevision = source.revision;
            catalog.contentStatus = source.status;
            catalog.sourceJson = source.json;
            catalog.manifestJson = source.manifestJson;
            catalog.catalogSha256 = source.jsonHash;
            catalog.manifestSha256 = source.manifestHash;
            catalog.items = definitions.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            return catalog;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static Package ReadPackage(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("아이템 출력 폴더를 지정하세요.", nameof(input));
        string root = Path.GetFullPath(File.Exists(input) ? Path.GetDirectoryName(input) : input);
        byte[] manifestBytes = ReadFile(root, "manifest.json");
        string manifestJson = Utf8.GetString(manifestBytes);
        JObject manifest = Parse(manifestJson);
        Equal(Int(manifest, "schemaVersion"), 1, "manifest.schemaVersion");
        string projectId = Uuid(Text(manifest, "projectId"), "manifest.projectId");
        string kind = Choice(manifest, "kind", "draft", "release");
        int revision = Int(manifest, "contentRevision", 0);
        Uuid(Text(manifest, "exportId"), "manifest.exportId");
        var fileBytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (JToken value in Array(manifest, "files", 1, 200))
        {
            JObject file = AsObject(value, "manifest.files[]");
            string relative = SafeRelative(Text(file, "path"));
            if (fileBytes.ContainsKey(relative)) Fail("manifest에 중복 파일이 있습니다: " + relative);
            byte[] bytes = ReadFile(root, relative);
            if (Hash(bytes) != HashText(file, "sha256")) Fail("파일 SHA-256 불일치: " + relative);
            fileBytes.Add(relative, bytes);
        }
        byte[] catalogBytes = Listed(fileBytes, CatalogFile);
        string json = Utf8.GetString(catalogBytes);
        JObject catalog = Parse(json);
        Equal(Int(catalog, "schemaVersion"), 1, "catalog.schemaVersion");
        Equal(Uuid(Text(catalog, "projectId"), "catalog.projectId"), projectId, "projectId");
        Equal(Int(catalog, "contentRevision", 0), revision, "contentRevision");
        string status = Choice(catalog, "contentStatus", "draft", "adopted");
        Equal(status, kind == "release" ? "adopted" : "draft", "contentStatus/manifest.kind");
        JObject report = Parse(Utf8.GetString(Listed(fileBytes, "validation-report.json")));
        Equal(Text(report, "projectId"), projectId, "validation-report.projectId");
        Equal(Int(report, "revision", 0), revision, "validation-report.revision");
        Equal(Text(report, "mode"), kind, "validation-report.mode");
        Listed(fileBytes, "README.md");
        // A draft may have review errors. Preserve its status rather than silently adopting it.
        Bool(report, "canExport");
        Array(report, "issues", 0, 10000);

        JObject profile = AsObject(Required(catalog, "ruleProfile"), "ruleProfile");
        Equal(Int(profile, "revision", 1), Int(manifest, "policyRevision", 1), "policyRevision");
        JObject confirmed = AsObject(Required(profile, "confirmed"), "ruleProfile.confirmed");
        string[] confirmedKeys = { "itemCount", "maxOwnedPerId", "baseMaxSp", "baseRegen", "regenDelaySeconds", "dodgeCost", "switchCost", "switchCooldownSeconds" };
        int[] confirmedValues = { 20, 1, 100, 20, 1, 20, 10, 2 };
        for (int i = 0; i < confirmedKeys.Length; i++) Equal(Int(confirmed, confirmedKeys[i]), confirmedValues[i], "confirmed." + confirmedKeys[i]);
        JArray prices = Array(confirmed, "gradePrices", 4, 4);
        int[] expectedPrices = { 20, 40, 80, 160 };
        for (int i = 0; i < 4; i++)
        {
            if (prices[i].Type != JTokenType.Integer) Fail("등급 가격은 정수여야 합니다.");
            Equal((int)prices[i], expectedPrices[i], "gradePrices");
        }
        JObject policy = AsObject(Required(profile, "proposed"), "ruleProfile.proposed");
        ExactKeys(policy, "maxSpCap", "regenCap", "minDodgeCost", "minSwitchCost", "stacking", "sourceStackLimit", "timedRefresh", "removeOnItemLoss", "refillOnMaxIncrease", "clampOnMaxDecrease");
        Equal(Text(policy, "stacking"), "additiveBySource", "stacking");
        Equal(Int(policy, "sourceStackLimit"), 1, "sourceStackLimit");
        Equal(Text(policy, "timedRefresh"), "refresh", "timedRefresh");
        Equal(Bool(policy, "removeOnItemLoss"), true, "removeOnItemLoss");
        Equal(Bool(policy, "refillOnMaxIncrease"), false, "refillOnMaxIncrease");
        Equal(Bool(policy, "clampOnMaxDecrease"), true, "clampOnMaxDecrease");
        Positive(policy, "maxSpCap");
        Positive(policy, "regenCap");
        Nonnegative(policy, "minDodgeCost");
        Nonnegative(policy, "minSwitchCost");
        var result = new Package
        {
            projectId = projectId, revision = revision, status = status, json = json,
            manifestJson = manifestJson, jsonHash = Hash(catalogBytes), manifestHash = Hash(manifestBytes)
        };
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var elementGrades = new HashSet<string>();
        foreach (JToken value in Array(catalog, "items", 1, 100))
        {
            JObject item = AsObject(value, "items[]");
            string key = Uuid(Text(item, "candidateKey"), "candidateKey"), id = Text(item, "itemId", 64);
            if (!keys.Add(key) || !ids.Add(id)) Fail("중복 candidateKey 또는 itemId: " + id);
            if (kind == "release" && (!Regex.IsMatch(id, "^[A-Z][A-Z0-9_]{0,63}$") || Regex.IsMatch(id, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$")))
                Fail("잘못된 release itemId: " + id);
            int grade = Int(item, "grade", 1, 4), price = Int(item, "purchasePrice");
            Equal(price, expectedPrices[grade - 1], id + ".purchasePrice");
            ElementType element = Element(Text(item, "element"));
            if (!elementGrades.Add(element + ":" + grade) && kind == "release") Fail("중복 오행/등급: " + id);
            if (!string.IsNullOrWhiteSpace(Text(item, "unsupportedEffectNotes", 12000, true))) Fail("지원하지 않는 효과 메모가 있습니다: " + id);
            Bool(item, "descriptionStale");
            var effects = new List<ItemEffect>();
            bool droppedSp = false;
            foreach (JToken effectValue in Array(item, "effects", 0, 20))
            {
                ItemEffect effect = Effect(AsObject(effectValue, id + ".effects[]"));
                if (effect != null) effects.Add(effect);
                else droppedSp = true;
            }
            string description = Text(item, "description", 12000, true);
            var entry = new Entry
            {
                key = key, id = id, name = Text(item, "name", 200, true),
                description = droppedSp ? string.Join(", ", effects.Select(DescribeEffect)) : description,
                grade = grade, price = price, revision = Int(item, "contentRevision", 1), element = element,
                effects = effects.ToArray(), droppedSp = droppedSp, json = item.ToString(Formatting.None)
            };
            JToken iconPathToken = Required(item, "iconPath");
            if (iconPathToken.Type != JTokenType.Null)
            {
                string relative = SafeRelative(Text(item, "iconPath"));
                Equal(relative, "icons/" + (kind == "release" ? id : key) + ".png", id + ".iconPath");
                entry.png = Listed(fileBytes, relative);
                JObject icon = AsObject(Required(item, "icon"), id + ".icon");
                Equal(Hash(entry.png), HashText(icon, "thumbnailSha256"), id + ".thumbnailSha256");
                HashText(icon, "sourceSha256"); // The source PNG is provenance; only the thumbnail ships in the package.
                ValidatePng(entry.png, id);
            }
            else if (kind == "release") Fail("release 아이콘이 누락되었습니다: " + id);
            result.items.Add(entry);
        }
        if (kind == "release") Equal(result.items.Count, 20, "release itemCount");
        // ID references must still point at the corresponding immutable candidate.
        var itemIds = result.items.ToDictionary(i => i.key, i => i.id, StringComparer.OrdinalIgnoreCase);
        foreach (JToken reference in Array(catalog, "synergyReferences", 0, 1000))
            foreach (JToken value in Array(AsObject(reference, "synergyReferences[]"), "items", 0, 100))
            {
                JObject target = AsObject(value, "synergyReferences.items[]");
                string key = Uuid(Text(target, "candidateKey"), "reference.candidateKey");
                if (!itemIds.TryGetValue(key, out string current) || current != Text(target, "itemId"))
                    Fail("현재 아이템 ID와 시너지 참조가 다릅니다: " + key);
            }
        var manifestIcons = Array(manifest, "icons", result.items.Count, result.items.Count);
        for (int i = 0; i < result.items.Count; i++)
        {
            JObject icon = AsObject(manifestIcons[i], "manifest.icons[]");
            Equal(Text(icon, "itemId"), result.items[i].id, "manifest.icons.itemId");
            string expected = result.items[i].png == null ? null : "icons/" + (kind == "release" ? result.items[i].id : result.items[i].key) + ".png";
            JToken token = Required(icon, "iconPath");
            Equal(token.Type == JTokenType.Null ? null : Text(icon, "iconPath"), expected, "manifest.icons.iconPath");
        }
        return result;
    }

    private static ItemEffect Effect(JObject source)
    {
        ExactKeys(source, "stat", "operation", "amount", "unit", "trigger", "targetForms", "durationSeconds", "stackLimit", "refreshPolicy", "removeOnItemLoss");
        string statName = Choice(source, "stat", "maxHp", "maxSp", "spRegen", "normalDamage", "switchDamage", "dodgeSpCost", "switchSpCost");
        bool ratio = statName == "maxHp" || statName == "normalDamage" || statName == "switchDamage";
        Equal(Text(source, "operation"), ratio ? "addRatio" : "add", "effect.operation");
        Equal(Text(source, "unit"), ratio ? "ratio" : statName == "spRegen" ? "SPPerSecond" : "SP", "effect.unit");
        string trigger = Choice(source, "trigger", "whileOwned", "formSwitchSucceeded");
        Equal(Int(source, "stackLimit"), 1, "effect.stackLimit");
        Equal(Text(source, "refreshPolicy"), "refresh", "effect.refreshPolicy");
        Equal(Bool(source, "removeOnItemLoss"), true, "effect.removeOnItemLoss");
        JToken duration = Required(source, "durationSeconds");
        if (trigger == "whileOwned" && duration.Type != JTokenType.Null) Fail("보유 효과 durationSeconds는 null이어야 합니다.");
        float seconds = trigger == "whileOwned" ? 0 : Positive(source, "durationSeconds");
        JToken forms = Required(source, "targetForms");
        bool all = forms.Type == JTokenType.String && (string)forms == "all";
        ElementType[] targets = System.Array.Empty<ElementType>();
        if (!all)
        {
            if (!(forms is JArray array) || array.Count < 1 || array.Count > 5) Fail("targetForms는 all 또는 1~5개 오행이어야 합니다.");
            targets = ((JArray)forms).Select(t => t.Type == JTokenType.String ? Element((string)t) : throw new InvalidDataException("targetForms는 문자열 배열이어야 합니다.")).ToArray();
            if (targets.Distinct().Count() != targets.Length) Fail("targetForms에 중복 오행이 있습니다.");
        }
        float amount = Number(source, "amount");
        // v1 packages include retired resource effects. Validate those before discarding them.
        if (!ratio) return null;
        var stat = (ItemStat)Enum.Parse(typeof(ItemStat), char.ToUpperInvariant(statName[0]) + statName.Substring(1));
        return new ItemEffect { stat = stat, amount = amount, trigger = trigger == "whileOwned" ? ItemEffectTrigger.WhileOwned : ItemEffectTrigger.FormSwitchSucceeded,
            allForms = all, targetForms = targets, durationSeconds = seconds };
    }

    private static string DescribeEffect(ItemEffect effect)
    {
        string stat = effect.stat switch
        {
            ItemStat.MaxHp => "최대 체력",
            ItemStat.NormalDamage => "기본 공격 피해",
            ItemStat.SwitchDamage => "전환 강화 스킬 피해",
            _ => throw new ArgumentOutOfRangeException(nameof(effect))
        };
        string scope = effect.allForms ? "모든 폼" : string.Join("·", effect.targetForms.Select(FormName)) + " 폼";
        string trigger = effect.trigger == ItemEffectTrigger.WhileOwned ? "보유 중"
            : $"전환 성공 후 {effect.durationSeconds.ToString("0.####", CultureInfo.InvariantCulture)}초 동안";
        string amount = ((double)effect.amount * 100).ToString("+0.####;-0.####;0", CultureInfo.InvariantCulture);
        return $"{trigger} {scope} {stat} {amount}%";
    }

    private static string FormName(ElementType form) => form switch
    {
        ElementType.ELEMENT_WOOD => "목",
        ElementType.ELEMENT_FIRE => "화",
        ElementType.ELEMENT_EARTH => "토",
        ElementType.ELEMENT_GOLD => "금",
        ElementType.ELEMENT_WATER => "수",
        _ => throw new ArgumentOutOfRangeException(nameof(form))
    };

    private static ElementType Element(string name)
    {
        switch (name)
        {
            case "wood": return ElementType.ELEMENT_WOOD;
            case "fire": return ElementType.ELEMENT_FIRE;
            case "earth": return ElementType.ELEMENT_EARTH;
            case "metal": return ElementType.ELEMENT_GOLD;
            case "water": return ElementType.ELEMENT_WATER;
            default: throw new InvalidDataException("지원하지 않는 오행: " + name);
        }
    }

    private static void ValidatePng(byte[] bytes, string id)
    {
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes.Length < 33 || !bytes.Take(8).SequenceEqual(signature) || Encoding.ASCII.GetString(bytes, 12, 4) != "IHDR" ||
            bytes[16] != 0 || bytes[17] != 0 || bytes[18] != 1 || bytes[19] != 0 ||
            bytes[20] != 0 || bytes[21] != 0 || bytes[22] != 1 || bytes[23] != 0)
            Fail("아이콘은 유효한 256×256 PNG여야 합니다: " + id);
        var texture = new Texture2D(2, 2);
        try
        {
            if (!ImageConversion.LoadImage(texture, bytes, true) || texture.width != 256 || texture.height != 256)
                Fail("PNG 해독 실패: " + id);
        }
        finally { Object.DestroyImmediate(texture); }
    }

    private static JObject Parse(string json)
    {
        using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = 64 })
        {
            JObject result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read()) Fail("JSON 끝에 다른 데이터가 있습니다.");
            return result;
        }
    }
    internal static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private static string HashText(JObject source, string key) { string value = Text(source, key); if (!Regex.IsMatch(value, "^[a-f0-9]{64}$")) Fail("잘못된 SHA-256: " + key); return value; }
    private static string Uuid(string value, string label) { if (!Guid.TryParseExact(value, "D", out Guid id)) Fail("잘못된 UUID: " + label); return id.ToString("D"); }
    private static string SafeRelative(string value)
    {
        if (Path.IsPathRooted(value) || value.Contains("\\") || value.Contains(":") || value.Split('/').Any(p => p.Length == 0 || p == "." || p == ".."))
            Fail("안전하지 않은 패키지 상대 경로: " + value);
        return value;
    }
    private static byte[] ReadFile(string root, string relative)
    {
        string path = Path.Combine(root, SafeRelative(relative));
        var file = new FileInfo(path);
        if (!file.Exists || file.Length == 0 || file.Length > MaxFileBytes) Fail("파일 누락 또는 파일 크기 오류: " + relative);
        return File.ReadAllBytes(path);
    }
    private static byte[] Listed(Dictionary<string, byte[]> files, string name) { if (!files.TryGetValue(name, out byte[] bytes)) Fail("manifest에서 파일을 찾을 수 없습니다: " + name); return bytes; }
    private static JObject AsObject(JToken value, string label) => value as JObject ?? throw new InvalidDataException("객체가 필요합니다: " + label);
    private static JToken Required(JObject value, string key) => value[key] ?? throw new InvalidDataException("필수 필드 누락: " + key);
    private static string Text(JObject source, string key, int max = 12000, bool empty = false)
    {
        JToken token = Required(source, key);
        if (token.Type != JTokenType.String) Fail("문자열이 필요합니다: " + key);
        string value = (string)token;
        if (value.Length > max || (!empty && string.IsNullOrWhiteSpace(value))) Fail("문자열 길이 오류: " + key);
        return value;
    }
    private static string Choice(JObject source, string key, params string[] choices) { string value = Text(source, key); if (!choices.Contains(value)) Fail("지원하지 않는 값: " + key + "=" + value); return value; }
    private static int Int(JObject source, string key, int min = int.MinValue, int max = int.MaxValue)
    {
        JToken token = Required(source, key);
        if (token.Type != JTokenType.Integer || (double)token < min || (double)token > max) Fail("정수 범위 오류: " + key);
        return (int)token;
    }
    private static float Number(JObject source, string key)
    {
        JToken token = Required(source, key);
        if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) Fail("숫자가 필요합니다: " + key);
        double value = (double)token;
        if (double.IsNaN(value) || double.IsInfinity(value) || value > float.MaxValue || value < -float.MaxValue) Fail("유한한 float 숫자가 필요합니다: " + key);
        return (float)value;
    }
    private static float Positive(JObject source, string key) { float value = Number(source, key); if (value <= 0) Fail("양수가 필요합니다: " + key); return value; }
    private static float Nonnegative(JObject source, string key) { float value = Number(source, key); if (value < 0) Fail("음수는 사용할 수 없습니다: " + key); return value; }
    private static bool Bool(JObject source, string key) { JToken token = Required(source, key); if (token.Type != JTokenType.Boolean) Fail("bool이 필요합니다: " + key); return (bool)token; }
    private static JArray Array(JObject source, string key, int min, int max) { var array = Required(source, key) as JArray; if (array == null || array.Count < min || array.Count > max) Fail("배열 크기 오류: " + key); return array; }
    private static void ExactKeys(JObject source, params string[] supported)
    {
        foreach (JProperty property in source.Properties())
            if (!supported.Contains(property.Name)) Fail("지원하지 않는 필드: " + property.Name);
    }
    private static void Equal<T>(T actual, T expected, string label) { if (!EqualityComparer<T>.Default.Equals(actual, expected)) Fail("값 불일치: " + label); }
    private static void Fail(string message) => throw new InvalidDataException(message);

    private static void RequireOwnedAsset<T>(string path, Func<T, bool> owns) where T : Object
    {
        if (!File.Exists(path)) return;
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null || !owns(asset)) Fail("다른 에셋을 덮어쓸 수 없습니다: " + path);
        if (EditorUtility.IsDirty(asset)) Fail("수정 중인 생성 에셋을 먼저 저장하세요: " + path);
    }

    private sealed class AssetTransaction
    {
        private readonly Dictionary<string, byte[]> backup = new Dictionary<string, byte[]>();
        private readonly List<string> folders = new List<string>();
        public void Capture(string path)
        {
            foreach (string file in new[] { path, path + ".meta" })
                if (!backup.ContainsKey(file)) backup[file] = File.Exists(file) ? File.ReadAllBytes(file) : null;
        }
        public void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            if (File.Exists(folder) || Directory.Exists(folder)) Fail("폴더 경로 충돌: " + folder);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(folder)))) Fail("폴더 생성 실패: " + folder);
            folders.Add(folder);
        }
        public void Rollback()
        {
            // Restore only captured generated paths; unrelated dirty assets are never saved.
            foreach (var pair in backup.Where(p => !p.Key.EndsWith(".meta", StringComparison.Ordinal) && p.Value == null))
                AssetDatabase.DeleteAsset(pair.Key);
            foreach (var pair in backup)
            {
                if (pair.Value != null) File.WriteAllBytes(pair.Key, pair.Value);
                else if (File.Exists(pair.Key)) File.Delete(pair.Key);
            }
            foreach (string path in backup.Keys.Where(p => !p.EndsWith(".meta", StringComparison.Ordinal) && File.Exists(p)))
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            for (int i = folders.Count - 1; i >= 0; i--)
                if (Directory.Exists(folders[i]) && !Directory.EnumerateFileSystemEntries(folders[i]).Any()) AssetDatabase.DeleteAsset(folders[i]);
        }
    }
}
