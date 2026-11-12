using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Imports a reviewed Boundary ZERO handoff into the game's authoritative catalog.</summary>
public static class BoundaryInventoryImporter
{
    public const string OutputRoot = "Assets/_Project/Generated/BoundaryInventory";
    public const string CatalogPath = OutputRoot + "/ItemCatalog.asset";
    public const string PresentationJsonPath = "Assets/BoundaryInventory/Catalog/catalog.json";
    private const string ArtRoot = "Assets/BoundaryInventory/Art";
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
    internal static Action<int> AfterItemImported;

    private sealed class Entry
    {
        internal string id, displayName, description, effectText, primary, sourceJson;
        internal string[] tags;
        internal int grade;
        internal Sprite icon;
        internal BoundaryItemEffect[] effects;
    }

    private sealed class Package
    {
        internal string json, sourceRevision;
        internal int contentRevision, balanceRevision;
        internal Entry[] items;
        internal ItemSynergyDefinition[] synergies;
    }

    [MenuItem("Tools/Boundary Zero/Inventory/Import Installed Catalog")]
    private static void ImportMenu()
    {
        ItemCatalog catalog = ImportDefault();
        Selection.activeObject = catalog;
        EditorGUIUtility.PingObject(catalog);
        Debug.Log($"[Boundary Inventory] {catalog.items.Length} items / {catalog.synergies.Length} synergies imported: {CatalogPath}", catalog);
    }

    public static ItemCatalog ImportDefault() => ImportPackage(PresentationJsonPath);

    /// <summary>Accepts the handoff root, its Data/catalog.ko.json, or the installed presentation JSON.</summary>
    public static ItemCatalog ImportPackage(string path) => Import(path, OutputRoot, true);

    internal static ItemCatalog ImportForRegression(string path, string outputRoot) => Import(path, outputRoot, false);

    private static ItemCatalog Import(string path, string outputRoot, bool updatePresentation)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("아이템 반입은 Edit Mode에서 실행하세요.");
        if (!outputRoot.StartsWith(OutputRoot, StringComparison.Ordinal) || outputRoot.Contains(".."))
            throw new InvalidDataException("생성 경로가 올바르지 않습니다.");
        Package package = ReadPackage(path); // Validate every effect, tag, number and Sprite before writing.
        string catalogPath = outputRoot + "/ItemCatalog.asset";
        RequireOwned<ItemCatalog>(catalogPath, c => c.projectId == BoundaryInventoryContent.ProjectId);
        foreach (Entry item in package.items)
            RequireOwned<ItemDefinition>(outputRoot + "/Definitions/" + item.id + ".asset",
                d => d.projectId == BoundaryInventoryContent.ProjectId && d.candidateKey == item.id);

        var transaction = new ImportTransaction();
        try
        {
            transaction.EnsureFolder(outputRoot + "/Definitions");
            transaction.Capture(catalogPath);
            var synergyById = package.synergies.ToDictionary(s => s.id, StringComparer.Ordinal);
            var definitions = new List<ItemDefinition>();
            foreach (Entry entry in package.items)
            {
                string assetPath = outputRoot + "/Definitions/" + entry.id + ".asset";
                transaction.Capture(assetPath);
                ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, assetPath);
                }
                definition.name = entry.id;
                definition.projectId = BoundaryInventoryContent.ProjectId;
                definition.candidateKey = entry.id;
                definition.itemId = entry.id;
                definition.displayName = entry.displayName;
                definition.description = entry.description;
                definition.effectText = entry.effectText;
                definition.synergyIds = entry.tags;
                definition.synergyNames = entry.tags.Select(t => synergyById[t].displayName).ToArray();
                definition.primarySynergyId = entry.primary;
                definition.primarySynergyName = synergyById[entry.primary].displayName;
                definition.primarySynergyIcon = synergyById[entry.primary].icon;
                // These items have synergy categories, not an item/form affinity.
                definition.element = ElementType.ELEMENT_NONE;
                definition.grade = entry.grade;
                definition.purchasePrice = 20 << (entry.grade - 1);
                definition.effects = Array.Empty<ItemEffect>();
                definition.boundaryEffects = entry.effects;
                definition.icon = entry.icon;
                definition.contentStatus = "draft";
                definition.contentRevision = package.contentRevision == BoundaryInventoryContent.RevisedRevision
                    || !BoundaryInventoryContent.LegacyGrades.ContainsKey(entry.id)
                    ? package.contentRevision : BoundaryInventoryContent.LegacyRevision;
                definition.sourceJson = entry.sourceJson;
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssetIfDirty(definition);
                definitions.Add(definition);
                AfterItemImported?.Invoke(definitions.Count);
            }

            ItemCatalog catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemCatalog>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }
            catalog.name = "Boundary ZERO Items";
            catalog.projectId = BoundaryInventoryContent.ProjectId;
            catalog.contentRevision = package.contentRevision;
            catalog.contentStatus = "draft";
            catalog.items = definitions.ToArray();
            catalog.synergies = package.synergies;
            catalog.sourceJson = package.json;
            catalog.catalogSha256 = Hash(Utf8.GetBytes(package.json));
            catalog.manifestJson = BalanceManifest(package);
            catalog.manifestSha256 = Hash(Utf8.GetBytes(catalog.manifestJson));
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);

            if (updatePresentation && File.ReadAllText(PresentationJsonPath, Utf8) != package.json)
            {
                transaction.Capture(PresentationJsonPath);
                File.WriteAllText(PresentationJsonPath, package.json, Utf8);
                AssetDatabase.ImportAsset(PresentationJsonPath, ImportAssetOptions.ForceSynchronousImport);
            }
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
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("패키지 경로를 지정하세요.", nameof(input));
        string path = Path.GetFullPath(input);
        if (Directory.Exists(path))
        {
            string[] candidates = { Path.Combine(path, "Data", "catalog.ko.json"), Path.Combine(path, "Catalog", "catalog.json"), Path.Combine(path, "catalog.ko.json") };
            path = candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("catalog.ko.json을 찾지 못했습니다.", path);
        }
        if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024)
            throw new InvalidDataException("카탈로그가 없거나 너무 큽니다.");
        JObject json = JObject.Parse(File.ReadAllText(path, Utf8));
        string sourceRevision = (string)json["revision"];
        if ((int?)json["version"] != 1)
            throw new InvalidDataException("지원하지 않는 Boundary Inventory 버전입니다.");
        var (contentRevision, balanceRevision, expectedGrades) = sourceRevision switch
        {
            BoundaryInventoryContent.LegacySourceRevision => (BoundaryInventoryContent.LegacyRevision, 1, BoundaryInventoryContent.LegacyGrades),
            BoundaryInventoryContent.ExpandedSourceRevision => (BoundaryInventoryContent.ExpandedRevision, 4, BoundaryInventoryContent.Grades),
            BoundaryInventoryContent.RevisedSourceRevision => (BoundaryInventoryContent.RevisedRevision, 5, BoundaryInventoryContent.RevisedGrades),
            _ => throw new InvalidDataException("지원하지 않는 Boundary Inventory 버전입니다.")
        };
        bool expanded = contentRevision >= BoundaryInventoryContent.ExpandedRevision;
        bool revised = contentRevision == BoundaryInventoryContent.RevisedRevision;
        int[] standardTiers = expanded ? new[] { 2, 4, 6, 8 } : new[] { 2, 4, 6 };
        var items = json["items"] as JArray;
        var synergies = json["synergies"] as JArray;
        if (items == null || items.Count != expectedGrades.Count || synergies == null || synergies.Count != 11)
            throw new InvalidDataException($"{expectedGrades.Count}개 아이템과 11개 시너지가 필요합니다.");

        var setIds = new HashSet<string>(StringComparer.Ordinal);
        var setDefinitions = new List<ItemSynergyDefinition>();
        foreach (JToken value in synergies)
        {
            JObject set = value as JObject ?? throw new InvalidDataException("시너지 객체가 필요합니다.");
            string id = Text(set, "id");
            if (!setIds.Add(id) || !BoundaryInventorySourceText.TryGetSynergyRule(id, revised, out string expectedDescription) || id.Contains(':'))
                throw new InvalidDataException("중복되거나 검토되지 않은 시너지 ID: " + id);
            string description = Text(set, "effect");
            if (description != expectedDescription)
                throw new InvalidDataException("시너지 규칙이 변경됐습니다. typed mapping을 먼저 갱신하세요: " + id);
            string summary = revised ? string.Empty : Text(set, "summary");
            if (revised && (set["summary"]?.Type != JTokenType.String || (string)set["summary"] != summary))
                throw new InvalidDataException("시너지 요약은 비워 두어야 합니다: " + id);
            int[] expectedTiers = revised && id == "S19" ? new[] { 1, 2, 3 } : standardTiers;
            var tiers = set["tiers"] as JArray;
            if (tiers == null || tiers.Count != expectedTiers.Length)
                throw new InvalidDataException($"시너지에 {string.Join("/", expectedTiers)} 단계가 필요합니다.");
            var counts = new HashSet<int>();
            var tierDefinitions = new List<ItemSynergyTier>();
            foreach (JToken tierValue in tiers)
            {
                JObject tier = tierValue as JObject ?? throw new InvalidDataException("시너지 단계 객체가 필요합니다.");
                int count = (int?)tier["count"] ?? 0;
                string text = Text(tier, "text");
                if (!expectedTiers.Contains(count) || !counts.Add(count)
                    || !BoundaryInventorySourceText.TryGetSynergyRule(id + ":" + count, revised, out string expected) || text != expected)
                    throw new InvalidDataException("지원하지 않거나 변경된 시너지 단계: " + id + "/" + count);
                BoundaryItemEffect[] effects = BoundaryInventoryContent.SynergyEffects(id, count, tier["values"] as JObject, revised);
                ValidateEffects(effects, id);
                tierDefinitions.Add(new ItemSynergyTier { count = count, text = text, effects = effects });
            }
            setDefinitions.Add(new ItemSynergyDefinition { id = id, displayName = Text(set, "name"),
                summary = summary, description = description,
                icon = SpriteAt(ArtRoot + "/Synergies/" + id + ".png", 256),
                members = Strings(set, "members"), tiers = tierDefinitions.OrderBy(t => t.count).ToArray() });
        }

        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<Entry>();
        foreach (JToken value in items)
        {
            JObject item = value as JObject ?? throw new InvalidDataException("아이템 객체가 필요합니다.");
            string id = Text(item, "id");
            if (!itemIds.Add(id) || !expectedGrades.TryGetValue(id, out int grade))
                throw new InvalidDataException("중복되거나 검토되지 않은 아이템 ID: " + id);
            // Materialize the reviewed grade for the inventory artwork catalog and exported source.
            item["grade"] = grade;
            string effect = BoundaryInventorySourceText.ReviewedEffect(id, Text(item, "effect"), revised);
            if (effect == null)
                throw new InvalidDataException("효과 문구가 변경됐습니다. typed mapping을 먼저 갱신하세요: " + id);
            item["effect"] = effect;
            string[] tags = Strings(item, "synergies");
            string primary = Text(item, "primarySynergy");
            if (tags.Length < 1 || tags.Length > 2 || tags.Distinct(StringComparer.Ordinal).Count() != tags.Length
                || !tags.Contains(primary) || tags.Any(t => !setIds.Contains(t)))
                throw new InvalidDataException("아이템 시너지 참조 오류: " + id);
            BoundaryItemEffect[] effects = revised ? BoundaryInventoryContent.RevisedItemEffects(id) : BoundaryInventoryContent.Effects(id);
            ValidateEffects(effects, id);
            entries.Add(new Entry { id = id, displayName = Text(item, "name"), description = Text(item, "description"),
                effectText = effect, primary = primary, tags = tags, grade = grade, effects = effects,
                sourceJson = item.ToString(Formatting.None), icon = SpriteAt(ArtRoot + "/Items/" + id + ".png", 512) });
        }
        foreach (ItemSynergyDefinition set in setDefinitions)
        {
            string[] actual = entries.Where(e => e.tags.Contains(set.id)).Select(e => e.id).OrderBy(s => s).ToArray();
            bool harvest = revised && set.id == "S19";
            int expectedMembers = harvest ? 3 : standardTiers[standardTiers.Length - 1] + (set.id == "S16" ? 1 : 0);
            if (set.members.Length != expectedMembers
                || harvest && !actual.SequenceEqual(new[] { "I122", "I89", "I91" })
                || expanded && !harvest && set.members.Count(id => !BoundaryInventoryContent.LegacyGrades.ContainsKey(id)) != 2
                || set.members.Distinct(StringComparer.Ordinal).Count() != set.members.Length
                || !actual.SequenceEqual(set.members.OrderBy(s => s)))
                throw new InvalidDataException("시너지 멤버 목록이 일치하지 않습니다: " + set.id);
        }
        return new Package { json = json.ToString(Formatting.Indented), sourceRevision = sourceRevision,
            contentRevision = contentRevision, balanceRevision = balanceRevision, items = entries.ToArray(), synergies = setDefinitions.ToArray() };
    }

    private static void ValidateEffects(BoundaryItemEffect[] effects, string id)
    {
        if (effects == null || effects.Length == 0 || effects.Any(e => e == null || !e.IsValid())
            || effects.Select(e => e.effectId).Distinct(StringComparer.Ordinal).Count() != effects.Length)
            throw new InvalidDataException("잘못된 typed effect: " + id);
    }

    private static string Text(JObject value, string field)
    {
        if (value[field]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)value[field]))
            throw new InvalidDataException("문자열 필드 누락: " + field);
        return (string)value[field];
    }

    private static string[] Strings(JObject value, string field)
    {
        if (!(value[field] is JArray array) || array.Any(t => t.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)t)))
            throw new InvalidDataException("문자열 배열 필드 오류: " + field);
        return array.Values<string>().ToArray();
    }

    private static Sprite SpriteAt(string path, int size)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null || sprite.texture.width != size || sprite.texture.height != size)
            throw new InvalidDataException("Sprite 참조 또는 크기 오류: " + path);
        return sprite;
    }

    private static string BalanceManifest(Package package)
    {
        var grades = new JObject();
        foreach (var item in package.items.OrderBy(i => i.id)) grades[item.id] = item.grade;
        var manifest = new JObject { ["balanceRevision"] = package.balanceRevision,
            ["sourceRevision"] = package.sourceRevision,
            ["prices"] = new JArray(20, 40, 80, 160) };
        if (package.contentRevision == BoundaryInventoryContent.RevisedRevision)
            manifest["i89RoomClearBaseGold"] = 8;
        else
            manifest["i89MaximumBaseGold"] = 10;
        manifest["ordinaryEnemyBaseGoldMin"] = 5;
        manifest["ordinaryEnemyBaseGoldMax"] = 15;
        manifest["grades"] = grades;
        return manifest.ToString(Formatting.Indented);
    }

    private static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static void RequireOwned<T>(string path, Func<T, bool> owns) where T : Object
    {
        if (!File.Exists(path)) return;
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null || !owns(asset)) throw new InvalidDataException("다른 에셋을 덮어쓸 수 없습니다: " + path);
        if (EditorUtility.IsDirty(asset)) throw new InvalidOperationException("수정 중인 생성 에셋을 먼저 저장하세요: " + path);
    }

    private sealed class ImportTransaction
    {
        private readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
        private readonly List<string> folders = new List<string>();
        internal void Capture(string path)
        {
            foreach (string file in new[] { path, path + ".meta" })
                if (!files.ContainsKey(file)) files[file] = File.Exists(file) ? File.ReadAllBytes(file) : null;
        }
        internal void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            if (Directory.Exists(path) || File.Exists(path)) throw new InvalidDataException("폴더 경로 충돌: " + path);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
                throw new IOException("폴더 생성 실패: " + path);
            folders.Add(path);
        }
        internal void Rollback()
        {
            foreach (var pair in files.Where(f => !f.Key.EndsWith(".meta", StringComparison.Ordinal) && f.Value == null))
                AssetDatabase.DeleteAsset(pair.Key);
            foreach (var pair in files)
                if (pair.Value != null) File.WriteAllBytes(pair.Key, pair.Value);
                else if (File.Exists(pair.Key)) File.Delete(pair.Key);
            foreach (string path in files.Keys.Where(p => !p.EndsWith(".meta", StringComparison.Ordinal) && File.Exists(p)))
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            for (int i = folders.Count - 1; i >= 0; i--)
                if (Directory.Exists(folders[i]) && !Directory.EnumerateFileSystemEntries(folders[i]).Any()) AssetDatabase.DeleteAsset(folders[i]);
        }
    }
}
