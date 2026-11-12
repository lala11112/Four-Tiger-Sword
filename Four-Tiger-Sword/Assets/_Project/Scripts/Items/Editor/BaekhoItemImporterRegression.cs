using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Callable Editor regressions; no Assembly-CSharp/NUnit assembly restructuring required.</summary>
public static class BaekhoItemImporterRegression
{
    private static int checks;

    [MenuItem("Tools/Studio Baekho/Items/Run Import Regression")]
    private static void RunMenu() => Run();

    public static string Run()
    {
        string fixture = Path.Combine(Path.GetTempPath(), "BaekhoItemFixture-" + Guid.NewGuid().ToString("N"));
        try { CreateFixture(fixture); return RunPackage(fixture); }
        finally { if (Directory.Exists(fixture)) Directory.Delete(fixture, true); }
    }

    /// <summary>Runs against a copied package with a new project UUID; the supplied source is never edited.</summary>
    public static string RunPackage(string sourcePath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
        checks = 0;
        string source = File.Exists(sourcePath) ? Path.GetDirectoryName(sourcePath) : sourcePath;
        string scratch = Path.Combine(Path.GetTempPath(), "BaekhoItemRegression-" + Guid.NewGuid().ToString("N"));
        string projectId = Guid.NewGuid().ToString("D");
        string destination = BaekhoItemImporter.OutputRoot + "/" + projectId;
        Action<int> priorHook = BaekhoItemImporter.AfterItemImported;
        try
        {
            Directory.CreateDirectory(scratch);
            JObject manifest = Read(source, "manifest.json");
            foreach (JObject file in (JArray)manifest["files"])
            {
                string relative = (string)file["path"];
                if (Path.IsPathRooted(relative) || relative.Contains("..") || relative.Contains("\\") || relative.Contains(":"))
                    throw new InvalidDataException("Unsafe regression source path.");
                string output = Path.Combine(scratch, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.Copy(Path.Combine(source, relative), output);
            }
            Write(scratch, "manifest.json", manifest);
            JObject catalogJson = Read(scratch, BaekhoItemImporter.CatalogFile);
            catalogJson["projectId"] = projectId;
            Write(scratch, BaekhoItemImporter.CatalogFile, catalogJson);
            JObject report = Read(scratch, "validation-report.json");
            report["projectId"] = projectId;
            Write(scratch, "validation-report.json", report);
            manifest["projectId"] = projectId;
            Write(scratch, "manifest.json", manifest);
            Rehash(scratch);

            var sourceItems = ((JArray)catalogJson["items"]).Cast<JObject>().ToArray();
            var excludedItems = sourceItems.Where(item => ((JArray)item["effects"]).Count > 0
                && !RetainedEffects(item).Any()).ToArray();
            string excludedPath = null;
            if (excludedItems.Length > 0)
            {
                string excludedKey = (string)excludedItems[0]["candidateKey"];
                AssetDatabase.CreateFolder(BaekhoItemImporter.OutputRoot, projectId);
                AssetDatabase.CreateFolder(destination, "Definitions");
                excludedPath = destination + "/Definitions/" + excludedKey + ".asset";
                var retired = ScriptableObject.CreateInstance<ItemDefinition>();
                retired.projectId = projectId;
                retired.candidateKey = excludedKey;
                retired.itemId = (string)excludedItems[0]["itemId"];
                AssetDatabase.CreateAsset(retired, excludedPath);
            }
            ItemCatalog catalog = BaekhoItemImporter.ImportPackage(scratch);
            Check(catalog != null && catalog.items.Length == sourceItems.Length - excludedItems.Length,
                "catalog excludes only items with exclusively retired effects");
            if (excludedPath != null)
                Check(!File.Exists(excludedPath) && !File.Exists(excludedPath + ".meta"), "stale retired definition was deleted");
            foreach (JObject excluded in excludedItems)
                Check(catalog.Find((string)excluded["itemId"]) == null, "retired item is unavailable");
            Check(catalog.contentStatus == (string)catalogJson["contentStatus"], "draft/adopted state preserved");
            Check(catalog.sourceJson == File.ReadAllText(Path.Combine(scratch, BaekhoItemImporter.CatalogFile)), "catalog provenance preserved");
            Check(catalog.manifestSha256 == BaekhoItemImporter.Hash(File.ReadAllBytes(Path.Combine(scratch, "manifest.json"))), "manifest fingerprint");
            foreach (ItemDefinition item in catalog.items)
            {
                JObject expected = sourceItems.Single(i => (string)i["candidateKey"] == item.candidateKey);
                var surviving = RetainedEffects(expected).ToArray();
                Check(item.effects.Select(effect => effect.stat).SequenceEqual(surviving.Select(effect =>
                    (ItemStat)Enum.Parse(typeof(ItemStat), (string)effect["stat"], true))), "surviving effects imported: " + item.itemId);
                if (surviving.Length < ((JArray)expected["effects"]).Count)
                    Check(!item.description.Contains("SP") && !item.effectText.Contains("SP")
                        && item.effectText.Length > 0 && item.sourceJson.Contains("SP"), "mixed item visible text reflects retained effects");
                if (expected["iconPath"].Type != JTokenType.Null)
                    Check(item.icon != null && item.icon.texture.width == 256 && item.icon.texture.height == 256, "256px Sprite: " + item.itemId);
                if ((string)expected["element"] == "metal") Check(item.element == ElementType.ELEMENT_GOLD, "metal enum mapping");
            }
            string catalogGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(catalog));
            ItemDefinition first = catalog.items[0];
            int firstSourceIndex = System.Array.FindIndex(sourceItems, i => (string)i["candidateKey"] == first.candidateKey);
            string definitionPath = AssetDatabase.GetAssetPath(first), definitionGuid = AssetDatabase.AssetPathToGUID(definitionPath);
            string iconPath = first.icon == null ? null : AssetDatabase.GetAssetPath(first.icon);
            string iconGuid = iconPath == null ? null : AssetDatabase.AssetPathToGUID(iconPath);
            ItemCatalog repeated = BaekhoItemImporter.ImportPackage(Path.Combine(scratch, BaekhoItemImporter.CatalogFile));
            Check(repeated == catalog && repeated.items[0] == first, "repeat import retains object references");
            Check(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(repeated)) == catalogGuid, "catalog GUID retained");

            RenameItem(scratch, firstSourceIndex, first.itemId.Substring(0, Math.Min(48, first.itemId.Length)) + "_REIMPORT");
            ItemCatalog renamed = BaekhoItemImporter.ImportPackage(scratch);
            Check(renamed.items[0] == first && renamed.items[0].itemId.EndsWith("_REIMPORT", StringComparison.Ordinal), "mutable ID updates existing candidate");
            Check(AssetDatabase.AssetPathToGUID(definitionPath) == definitionGuid, "definition GUID survives ID edit");
            if (iconPath != null) Check(AssetDatabase.AssetPathToGUID(iconPath) == iconGuid, "Sprite GUID survives ID edit");

            byte[] goodCatalog = File.ReadAllBytes(Path.Combine(scratch, BaekhoItemImporter.CatalogFile));
            var baseline = Snapshot(destination);
            var invalid = new Action<JObject>[]
            {
                j => j["schemaVersion"] = 2,
                j => ((JObject)j["items"][0]).Remove("candidateKey"),
                j => j["items"][0]["grade"] = 5,
                j => j["items"][0]["effects"][0]["stat"] = "unknownDamage",
                j => j["items"][0]["effects"][0]["operation"] = "multiply",
                j => j["items"][0]["effects"][0]["targetForms"] = new JArray("metal", "metal"),
                j => j["items"][0]["effects"][0]["durationSeconds"] = -1,
                j => j["ruleProfile"]["proposed"]["stacking"] = "multiplicative",
                j => j["items"][1]["itemId"] = j["items"][0]["itemId"].DeepClone(),
                j => j["items"][1]["candidateKey"] = j["items"][0]["candidateKey"].DeepClone(),
                j => j["items"][0]["unsupportedEffectNotes"] = "unsupported runtime effect"
            };
            foreach (Action<JObject> mutate in invalid)
            {
                JObject broken = JObject.Parse(Encoding.UTF8.GetString(goodCatalog));
                mutate(broken);
                Write(scratch, BaekhoItemImporter.CatalogFile, broken);
                Rehash(scratch);
                Throws(() => BaekhoItemImporter.ImportPackage(scratch), "invalid schema/semantics rejected");
                SameSnapshot(destination, baseline, "preflight failure wrote generated assets");
            }
            File.WriteAllBytes(Path.Combine(scratch, BaekhoItemImporter.CatalogFile), goodCatalog);
            Rehash(scratch);

            JObject currentManifest = Read(scratch, "manifest.json");
            byte[] goodManifest = File.ReadAllBytes(Path.Combine(scratch, "manifest.json"));
            currentManifest["files"][0]["sha256"] = new string('0', 64);
            Write(scratch, "manifest.json", currentManifest);
            Throws(() => BaekhoItemImporter.ImportPackage(scratch), "hash mismatch rejected");
            SameSnapshot(destination, baseline, "hash failure wrote generated assets");
            File.WriteAllBytes(Path.Combine(scratch, "manifest.json"), goodManifest);

            string firstPng = ((JArray)Read(scratch, "manifest.json")["files"]).Cast<JObject>()
                .Select(f => (string)f["path"]).FirstOrDefault(p => p.EndsWith(".png", StringComparison.Ordinal));
            if (firstPng != null)
            {
                byte[] pngBytes = File.ReadAllBytes(Path.Combine(scratch, firstPng));
                File.Delete(Path.Combine(scratch, firstPng));
                Throws(() => BaekhoItemImporter.ImportPackage(scratch), "missing PNG rejected");
                SameSnapshot(destination, baseline, "missing PNG wrote generated assets");
                File.WriteAllBytes(Path.Combine(scratch, firstPng), pngBytes);
            }

            JObject changed = Read(scratch, BaekhoItemImporter.CatalogFile);
            changed["items"][firstSourceIndex]["name"] = "Rollback probe";
            Write(scratch, BaekhoItemImporter.CatalogFile, changed);
            Rehash(scratch);
            BaekhoItemImporter.AfterItemImported = count => { if (count == 1) throw new IOException("Injected write failure"); };
            Throws(() => BaekhoItemImporter.ImportPackage(scratch), "partial write failure propagated");
            BaekhoItemImporter.AfterItemImported = priorHook;
            SameSnapshot(destination, baseline, "rollback changed files or metas");
            Check(AssetDatabase.LoadAssetAtPath<ItemDefinition>(definitionPath) == first && first.displayName != "Rollback probe", "rollback restores live object and references");

            // A new candidate created immediately before failure must also be removed.
            changed["items"][firstSourceIndex]["candidateKey"] = Guid.NewGuid().ToString("D");
            string newKey = (string)changed["items"][firstSourceIndex]["candidateKey"];
            foreach (JObject reference in (JArray)changed["synergyReferences"])
                foreach (JObject referenceItem in (JArray)reference["items"])
                    if ((string)referenceItem["itemId"] == first.itemId) referenceItem["candidateKey"] = newKey;
            if ((string)Read(scratch, "manifest.json")["kind"] == "draft"
                && changed["items"][firstSourceIndex]["iconPath"].Type != JTokenType.Null)
            {
                string previousPng = (string)changed["items"][firstSourceIndex]["iconPath"], newPng = "icons/" + newKey + ".png";
                File.Move(Path.Combine(scratch, previousPng), Path.Combine(scratch, newPng));
                changed["items"][firstSourceIndex]["iconPath"] = newPng;
                JObject newManifest = Read(scratch, "manifest.json");
                ((JArray)newManifest["files"]).Cast<JObject>().Single(f => (string)f["path"] == previousPng)["path"] = newPng;
                newManifest["icons"][firstSourceIndex]["iconPath"] = newPng;
                Write(scratch, "manifest.json", newManifest);
            }
            Write(scratch, BaekhoItemImporter.CatalogFile, changed);
            Rehash(scratch);
            BaekhoItemImporter.AfterItemImported = count => { if (count == 1) throw new IOException("Injected new asset failure"); };
            Throws(() => BaekhoItemImporter.ImportPackage(scratch), "new asset failure propagated");
            BaekhoItemImporter.AfterItemImported = priorHook;
            SameSnapshot(destination, baseline, "rollback left new candidate or icon");

            string summary = "PASS: " + checks + " import assertions; real package schema/effects/Sprites, fingerprints, repeat import, mutable IDs/GUIDs, rejected inputs and existing/new asset rollback.";
            Debug.Log("[Baekho Items] " + summary);
            return summary;
        }
        finally
        {
            BaekhoItemImporter.AfterItemImported = priorHook;
            if (AssetDatabase.IsValidFolder(destination)) AssetDatabase.DeleteAsset(destination);
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
        }
    }

    private static IEnumerable<JObject> RetainedEffects(JObject item)
        => ((JArray)item["effects"]).Cast<JObject>().Where(effect =>
            (string)effect["stat"] is "maxHp" or "normalDamage" or "switchDamage");

    private static void RenameItem(string root, int index, string newId)
    {
        JObject catalog = Read(root, BaekhoItemImporter.CatalogFile), manifest = Read(root, "manifest.json");
        string key = (string)catalog["items"][index]["candidateKey"];
        catalog["items"][index]["itemId"] = newId;
        foreach (JObject reference in (JArray)catalog["synergyReferences"])
            foreach (JObject item in (JArray)reference["items"])
                if ((string)item["candidateKey"] == key) item["itemId"] = newId;
        manifest["icons"][index]["itemId"] = newId;
        if ((string)manifest["kind"] == "release")
        {
            string oldPath = (string)catalog["items"][index]["iconPath"], newPath = "icons/" + newId + ".png";
            File.Move(Path.Combine(root, oldPath), Path.Combine(root, newPath));
            catalog["items"][index]["iconPath"] = newPath;
            manifest["icons"][index]["iconPath"] = newPath;
            ((JArray)manifest["files"]).Cast<JObject>().Single(f => (string)f["path"] == oldPath)["path"] = newPath;
        }
        Write(root, BaekhoItemImporter.CatalogFile, catalog);
        Write(root, "manifest.json", manifest);
        Rehash(root);
    }

    private static void Rehash(string root)
    {
        JObject manifest = Read(root, "manifest.json");
        foreach (JObject file in (JArray)manifest["files"])
            file["sha256"] = BaekhoItemImporter.Hash(File.ReadAllBytes(Path.Combine(root, (string)file["path"])));
        Write(root, "manifest.json", manifest);
    }
    private static JObject Read(string root, string relative) => JObject.Parse(File.ReadAllText(Path.Combine(root, relative)));
    private static void Write(string root, string relative, JObject json) => File.WriteAllText(Path.Combine(root, relative), json.ToString(Formatting.Indented), new UTF8Encoding(false));
    private static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(p => p, p => BaekhoItemImporter.Hash(File.ReadAllBytes(p)), StringComparer.OrdinalIgnoreCase);
    private static void SameSnapshot(string root, Dictionary<string, string> baseline, string message)
    {
        var after = Snapshot(root);
        Check(after.Count == baseline.Count && baseline.All(p => after.TryGetValue(p.Key, out string hash) && hash == p.Value), message);
    }
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException("Baekho Items regression: " + message); }
    private static void Throws(Action action, string message) { try { action(); } catch (InvalidDataException) { checks++; return; } catch (IOException) { checks++; return; } throw new InvalidOperationException("Expected rejection: " + message); }

    private static void CreateFixture(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "icons"));
        var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        byte[] png;
        try { texture.SetPixels(Enumerable.Repeat(new Color(0.7f, 0.2f, 0.5f, 0.8f), 256 * 256).ToArray()); texture.Apply(); png = texture.EncodeToPNG(); }
        finally { Object.DestroyImmediate(texture); }
        string projectId = Guid.NewGuid().ToString("D"), pngHash = BaekhoItemImporter.Hash(png);
        var items = new JArray();
        var icons = new JArray();
        var files = new JArray();
        string[] elements = { "wood", "fire", "earth", "metal", "water" };
        string[] stats = { "maxHp", "maxSp", "spRegen", "normalDamage", "switchDamage", "dodgeSpCost", "switchSpCost" };
        for (int i = 0; i < 20; i++)
        {
            string key = Guid.NewGuid().ToString("D"), id = "TEST_ITEM_" + i, icon = "icons/" + key + ".png", stat = stats[i % stats.Length];
            bool ratio = stat == "maxHp" || stat == "normalDamage" || stat == "switchDamage", timed = i % 2 != 0;
            int grade = i % 4 + 1;
            File.WriteAllBytes(Path.Combine(root, icon), png);
            files.Add(new JObject { ["path"] = icon, ["sha256"] = pngHash });
            icons.Add(new JObject { ["itemId"] = id, ["iconPath"] = icon });
            items.Add(new JObject
            {
                ["candidateKey"] = key, ["itemId"] = id, ["name"] = id, ["description"] = "Synthetic import fixture",
                ["element"] = elements[i / 4], ["grade"] = grade, ["purchasePrice"] = new[] { 20, 40, 80, 160 }[grade - 1],
                ["unsupportedEffectNotes"] = "", ["descriptionStale"] = false, ["contentRevision"] = 1,
                ["iconPath"] = icon, ["icon"] = new JObject { ["thumbnailSha256"] = pngHash, ["sourceSha256"] = pngHash },
                ["effects"] = new JArray(new JObject
                {
                    ["stat"] = stat, ["operation"] = ratio ? "addRatio" : "add", ["amount"] = ratio ? 0.08 : 2,
                    ["unit"] = ratio ? "ratio" : stat == "spRegen" ? "SPPerSecond" : "SP",
                    ["trigger"] = timed ? "formSwitchSucceeded" : "whileOwned", ["targetForms"] = i % 3 == 0 ? (JToken)new JArray(elements[i / 4]) : "all",
                    ["durationSeconds"] = timed ? (JToken)3 : JValue.CreateNull(), ["stackLimit"] = 1,
                    ["refreshPolicy"] = "refresh", ["removeOnItemLoss"] = true
                })
            });
        }
        var mixed = (JObject)items[3];
        mixed["description"] = "전환 성공 후 모든 폼 기본 공격 피해 +8%, 최대 SP +2 SP";
        ((JArray)mixed["effects"]).Add(items[1]["effects"][0].DeepClone());
        Write(root, BaekhoItemImporter.CatalogFile, new JObject
        {
            ["schemaVersion"] = 1, ["contentStatus"] = "draft", ["projectId"] = projectId, ["contentRevision"] = 1,
            ["items"] = items, ["synergyReferences"] = new JArray(), ["sourceRefs"] = new JArray(),
            ["ruleProfile"] = new JObject
            {
                ["revision"] = 1, ["adoptions"] = new JObject(),
                ["confirmed"] = new JObject { ["itemCount"] = 20, ["maxOwnedPerId"] = 1, ["baseMaxSp"] = 100, ["baseRegen"] = 20,
                    ["regenDelaySeconds"] = 1, ["dodgeCost"] = 20, ["switchCost"] = 10, ["switchCooldownSeconds"] = 2, ["gradePrices"] = new JArray(20, 40, 80, 160) },
                ["proposed"] = new JObject { ["maxSpCap"] = 200, ["regenCap"] = 40, ["minDodgeCost"] = 8, ["minSwitchCost"] = 4,
                    ["stacking"] = "additiveBySource", ["sourceStackLimit"] = 1, ["timedRefresh"] = "refresh", ["removeOnItemLoss"] = true,
                    ["refillOnMaxIncrease"] = false, ["clampOnMaxDecrease"] = true }
            }
        });
        Write(root, "validation-report.json", new JObject { ["projectId"] = projectId, ["revision"] = 1, ["mode"] = "draft", ["canExport"] = true, ["issues"] = new JArray() });
        File.WriteAllText(Path.Combine(root, "README.md"), "Synthetic PNGs; importer regression fixture only.");
        foreach (string file in new[] { BaekhoItemImporter.CatalogFile, "validation-report.json", "README.md" })
            files.Add(new JObject { ["path"] = file, ["sha256"] = BaekhoItemImporter.Hash(File.ReadAllBytes(Path.Combine(root, file))) });
        Write(root, "manifest.json", new JObject { ["schemaVersion"] = 1, ["exportId"] = Guid.NewGuid().ToString("D"), ["kind"] = "draft", ["projectId"] = projectId,
            ["contentRevision"] = 1, ["policyRevision"] = 1, ["icons"] = icons, ["files"] = files });
    }
}
