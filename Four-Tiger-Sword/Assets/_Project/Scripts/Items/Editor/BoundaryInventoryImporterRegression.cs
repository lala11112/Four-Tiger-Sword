using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Isolated importer regressions; does not modify the active scene or production catalog.</summary>
public static class BoundaryInventoryImporterRegression
{
    private static int checks;

    [MenuItem("Tools/Boundary Zero/Inventory/Run Import Regression")]
    private static void RunMenu() => Debug.Log(Run());

    public static string Run() => RunPackage(BoundaryInventoryImporter.PresentationJsonPath);

    public static string RunPackage(string sourcePath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
        checks = 0;
        string suffix = Guid.NewGuid().ToString("N");
        string output = BoundaryInventoryImporter.OutputRoot + "Regression-" + suffix;
        string failedOutput = output + "-rollback";
        string scratch = Path.Combine(Path.GetTempPath(), "BoundaryInventoryRegression-" + suffix);
        string fixture = Path.Combine(scratch, "catalog.ko.json");
        Action<int> priorHook = BoundaryInventoryImporter.AfterItemImported;
        try
        {
            Directory.CreateDirectory(scratch);
            ItemCatalog catalog = BoundaryInventoryImporter.ImportForRegression(sourcePath, output);
            bool expanded = catalog.contentRevision == BoundaryInventoryContent.ExpandedRevision;
            int itemCount = expanded ? 81 : 59;
            int maxTier = expanded ? 8 : 6;
            int[] thresholds = expanded ? new[] { 2, 4, 6, 8 } : new[] { 2, 4, 6 };
            int[] gradeCounts = expanded ? new[] { 19, 31, 22, 9 } : new[] { 13, 26, 14, 6 };
            Check(catalog.items.Length == itemCount && catalog.synergies.Length == 11, "expected items / 11 synergies");
            Check(catalog.items.Select(i => i.itemId).Distinct().Count() == itemCount, "unique IDs");
            Check(catalog.items.SelectMany(i => i.synergyIds).Count() == (expanded ? 89 : 67), "approved tag memberships");
            Check(Enumerable.Range(1, 4).All(grade => catalog.items.Count(i => i.grade == grade) == gradeCounts[grade - 1]),
                "approved grades");
            foreach (ItemDefinition item in catalog.items)
            {
                Check(item.grade >= 1 && item.grade <= 4 && item.purchasePrice == (20 << (item.grade - 1)), "price: " + item.itemId);
                Check(item.effects.Length == 0 && item.boundaryEffects.Length > 0 && item.boundaryEffects.All(e => e.IsValid()), "typed effects: " + item.itemId);
                Check(item.icon && item.icon.texture.width == 512 && item.icon.texture.height == 512, "icon: " + item.itemId);
                Check(item.element == ElementType.ELEMENT_NONE && item.synergyIds.Length == item.synergyNames.Length
                    && item.synergyIds.Contains(item.primarySynergyId) && item.primarySynergyIcon
                    && !string.IsNullOrWhiteSpace(item.effectText), "display metadata: " + item.itemId);
            }
            foreach (ItemSynergyDefinition set in catalog.synergies)
            {
                Check(set.tiers.Select(t => t.count).SequenceEqual(thresholds), "tiers: " + set.id);
                Check(set.tiers.All(t => t.effects.Length > 0 && t.effects.All(e => e.IsValid())), "typed tiers: " + set.id);
                Check(set.members.Length == maxTier + (set.id == "S16" ? 1 : 0)
                    && set.members.All(id => catalog.Find(id) != null), "members: " + set.id);
                if (expanded)
                    Check(set.members.Count(id => !BoundaryInventoryContent.LegacyGrades.ContainsKey(id)) == 2,
                        "two added items: " + set.id);
                var sourceIds = set.tiers[0].effects.Select(effect => effect.effectId).ToArray();
                Check(set.tiers.All(tier => tier.effects.Select(effect => effect.effectId).SequenceEqual(sourceIds)),
                    "stable tier sources: " + set.id);
            }
            ItemDefinition i89 = catalog.Find("I89");
            Check(i89.effectText.Contains("1~10") && i89.boundaryEffects[0].minimumBaseGold == 1
                && i89.boundaryEffects[0].maximumBaseGold == 10, "I89 authored threshold override");
            Check(catalog.Find("I16").boundaryEffects.Length == 2, "two elemental bonuses");
            Check(new[] { "I04", "I43", "I44", "I64", "I65", "I66" }.All(id =>
                catalog.Find(id).boundaryEffects.All(e => e.family == BoundaryAttackFamily.NormalOrSkill)), "explicit normal-and-skill scope");
            Check(new[] { "S10", "S14", "S15" }.All(id => catalog.synergies.Single(s => s.id == id)
                .tiers.All(t => t.effects.All(e => e.family == BoundaryAttackFamily.NormalOrSkill))), "explicit normal-and-skill synergy scope");
            Check(catalog.Find("I68").boundaryEffects[0].amount == .005f
                && catalog.Find("I68").boundaryEffects[0].cooldownSeconds == 20, "I68 healing/cooldown");

            var identities = catalog.items.ToDictionary(i => i.itemId, i => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(i)));
            string catalogGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(catalog));
            ItemCatalog repeated = BoundaryInventoryImporter.ImportForRegression(sourcePath, output);
            Check(repeated == catalog && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(repeated)) == catalogGuid, "catalog GUID retained");
            Check(repeated.items.All(i => identities[i.itemId] == AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(i))), "all item GUIDs retained");

            JObject valid = JObject.Parse(catalog.sourceJson);
            var invalid = new List<Action<JObject>> {
                j => j["version"] = 2,
                j => j["revision"] = "2026-11-09",
                j => ((JArray)j["items"]).RemoveAt(0),
                j => j["items"][1]["id"] = j["items"][0]["id"].DeepClone(),
                j => j["items"][0]["id"] = "I_UNKNOWN",
                j => j["items"][0]["synergies"] = new JArray("S_UNKNOWN"),
                j => j["items"][0]["effect"] = "공격력이 999% 증가한다.",
                j => j["items"][0]["primarySynergy"] = "S19",
                j => j["synergies"][0]["tiers"][0]["count"] = 3,
                j => j["synergies"][0]["tiers"][0]["values"]["critChanceBonusPoints"] = -1,
                j => j["synergies"][0]["tiers"][0]["values"]["unsupported"] = 1,
                j => ((JArray)j["synergies"][0]["members"]).RemoveAt(0)
            };
            if (expanded)
                invalid.Add(j => j["synergies"][0]["tiers"][3]["values"]["critChanceBonusPoints"] = 21);
            Dictionary<string, string> baseline = Snapshot(output);
            foreach (Action<JObject> mutate in invalid)
            {
                JObject broken = (JObject)valid.DeepClone();
                mutate(broken);
                File.WriteAllText(fixture, broken.ToString(Formatting.Indented));
                bool rejected = false;
                try { BoundaryInventoryImporter.ImportForRegression(fixture, output); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "invalid source rejected");
                Check(Same(baseline, Snapshot(output)), "invalid source leaves assets unchanged");
            }
            File.WriteAllText(fixture, "{broken");
            bool malformed = false;
            try { BoundaryInventoryImporter.ImportForRegression(fixture, output); }
            catch (JsonReaderException) { malformed = true; }
            Check(malformed && Same(baseline, Snapshot(output)), "malformed JSON leaves assets unchanged");

            JObject changedNames = (JObject)valid.DeepClone();
            foreach (JObject item in (JArray)changedNames["items"]) item["name"] = "Rollback fixture " + (string)item["id"];
            File.WriteAllText(fixture, changedNames.ToString(Formatting.Indented));
            BoundaryInventoryImporter.AfterItemImported = n => { if (n == 3) throw new IOException("Expected regression fault"); };
            bool rolledBack = false;
            try { BoundaryInventoryImporter.ImportForRegression(fixture, output); }
            catch (IOException) { rolledBack = true; }
            Check(rolledBack && Same(baseline, Snapshot(output)), "partial existing import rolled back with GUIDs intact");
            rolledBack = false;
            try { BoundaryInventoryImporter.ImportForRegression(fixture, failedOutput); }
            catch (IOException) { rolledBack = true; }
            Check(rolledBack && !Directory.Exists(failedOutput), "failed first import removes newly generated assets");
            BoundaryInventoryImporter.AfterItemImported = priorHook;

            return new JObject { ["success"] = true, ["checks"] = checks, ["items"] = itemCount,
                ["synergies"] = 11, ["guidReimport"] = true, ["invalidInputRejection"] = true,
                ["rollback"] = true }.ToString(Formatting.None);
        }
        finally
        {
            BoundaryInventoryImporter.AfterItemImported = priorHook;
            if (AssetDatabase.IsValidFolder(output)) AssetDatabase.DeleteAsset(output);
            if (AssetDatabase.IsValidFolder(failedOutput)) AssetDatabase.DeleteAsset(failedOutput);
            string tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (Directory.Exists(scratch) && Path.GetFullPath(scratch).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(scratch) == "BoundaryInventoryRegression-" + suffix) Directory.Delete(scratch, true);
        }
    }

    private static void Check(bool condition, string name)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("Boundary import regression failed: " + name);
    }

    private static Dictionary<string, string> Snapshot(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(root)) return result;
        using (var sha = SHA256.Create())
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                result[file] = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file)));
        return result;
    }
    private static bool Same(Dictionary<string, string> a, Dictionary<string, string> b)
        => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out string hash) && hash == p.Value);
}
