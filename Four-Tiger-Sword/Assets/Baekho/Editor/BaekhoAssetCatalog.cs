using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StudioBaekho
{
    /// <summary>Explicit prefab registrations and prop lists. Does not save assets, refresh the database or touch open scenes.</summary>
    public static class BaekhoAssetCatalog
    {
        public const int MaxEntries = 20000;
        public const int MaxBytes = 8 * 1024 * 1024;
        public const int MaxPropSets = 256;
        public const int MaxSetMembers = 256;
        [Serializable] private sealed class Entry
        {
            public string guid;
            public string path;
            public string role;
            public Vector3 size;
            public BaekhoPropCatalog.LocalBounds bounds;
        }
        [Serializable] private sealed class Skipped { public string path; public string reason; }
        [Serializable] private sealed class StringValue { public string value; }
        [Serializable] private sealed class PropSetEntry
        {
            public string guid;
            public string path;
            public string label;
            public string usage;
            public string arrangement;
            public string[] prefabGuids;
        }
        private sealed class PropSetCandidate
        {
            public PropSetEntry entry;
            public List<string> members = new List<string>();
        }

        public static void RequireReadyEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before reading prefab registrations.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for Unity to finish compiling and importing assets.");
            if (EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Fix Unity script compilation errors before reading prefab registrations.");
        }

        public static string CreateJson()
        {
            RequireReadyEditor();
            return BuildJson(AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }),
                AssetDatabase.FindAssets("t:BaekhoPropSet", new[] { "Assets" }));
        }

        /// <summary>Read only the supplied assets; omitted propSetGuids means no prop sets.</summary>
        public static string BuildJson(string[] prefabGuids, string[] propSetGuids = null)
        {
            RequireReadyEditor();
            if (prefabGuids == null) throw new ArgumentNullException("prefabGuids");
            var diagnostics = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var candidates = ReadPropSets(propSetGuids ?? new string[0], diagnostics);
            var memberGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PropSetCandidate candidate in candidates)
                foreach (string guid in candidate.members) memberGuids.Add(guid);
            var allGuids = new HashSet<string>(prefabGuids, StringComparer.OrdinalIgnoreCase);
            allGuids.UnionWith(memberGuids);
            var guids = new List<string>(allGuids);
            guids.Sort(StringComparer.Ordinal);
            var entries = new List<string>();
            var floorPropGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int bytes = 100;
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;
                    // Root-only lookup deliberately ignores nested/child registrations. Variants use their effective inherited values.
                    BaekhoMapAsset metadata = prefab.GetComponent<BaekhoMapAsset>();
                    if (metadata == null && !memberGuids.Contains(guid)) continue;
                    if (entries.Count + diagnostics.Count >= MaxEntries)
                        throw new InvalidDataException("The asset catalog exceeds 20,000 registered prefabs or diagnostics. Limit the registered prefabs before importing.");
                    string json;
                    try
                    {
                        if (metadata != null)
                        {
                            if (!metadata.importEnabled) throw new InvalidDataException("Map Asset registration is disabled.");
                            ValidateMetadata(metadata);
                        }
                        BaekhoPropCatalog.Measurement measured = BaekhoPropCatalog.MeasurePrefab(prefab, path, preview);
                        if (measured == null) throw new InvalidDataException("No measurable 3D renderer/collider bounds or valid wall occupancy boxes.");
                        if (!ValidVector(measured.bounds.center, 1000) || !PositiveVector(measured.bounds.size, 1000))
                            throw new InvalidDataException("Measured bounds must have finite centres and positive XYZ sizes within 1000m.");
                        if (measured.wallMount != null && metadata != null && (int)metadata.role <= (int)BaekhoMapAsset.AssetRole.Deadzone)
                            throw new InvalidDataException("Wall module metadata requires a fixture role, such as Light or Decoration.");
                        if (metadata == null && measured.wallMount != null)
                            throw new InvalidDataException("Wall-mounted prefabs cannot be imported as floor props through a Prop Set.");
                        json = EntryJson(guid, path, metadata, measured);
                        if (measured.wallMount == null && (metadata == null || IsFloorPropRole(metadata.role))) floorPropGuids.Add(guid);
                    }
                    catch (Exception exception)
                    {
                        AddDiagnostic(diagnostics, path, DiagnosticReason(exception));
                        continue;
                    }
                    bytes += Encoding.UTF8.GetByteCount(json) + 1;
                    if (bytes > MaxBytes) throw new InvalidDataException("The asset catalog exceeds 8 MiB. Limit registered prefabs before importing.");
                    entries.Add(json);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            var sets = new List<string>();
            foreach (PropSetCandidate candidate in candidates)
            {
                var included = new List<string>();
                foreach (string guid in candidate.members)
                {
                    if (floorPropGuids.Contains(guid)) included.Add(guid.ToLowerInvariant());
                    else AddDiagnostic(diagnostics, candidate.entry.path, "Excluded member " + AssetDatabase.GUIDToAssetPath(guid) + ": requires an enabled, measurable floor Prop, Decoration or Vegetation prefab without wall-mount metadata.");
                }
                if (included.Count == 0)
                {
                    AddDiagnostic(diagnostics, candidate.entry.path, "Prop Set has no valid floor prop members and was not imported.");
                    continue;
                }
                candidate.entry.prefabGuids = included.ToArray();
                sets.Add(JsonUtility.ToJson(candidate.entry));
            }
            var skipped = new List<string>();
            foreach (var diagnostic in diagnostics)
                skipped.Add(JsonUtility.ToJson(new Skipped { path = diagnostic.Key, reason = diagnostic.Value }));
            string result = "{\"format\":\"baekho-asset-catalog\",\"version\":1,\"assets\":[" + string.Join(",", entries.ToArray()) + "],\"propSets\":[" + string.Join(",", sets.ToArray()) + "],\"skipped\":[" + string.Join(",", skipped.ToArray()) + "]}";
            if (Encoding.UTF8.GetByteCount(result) > MaxBytes) throw new InvalidDataException("The asset catalog exceeds 8 MiB.");
            return result;
        }

        private static string EntryJson(string guid, string path, BaekhoMapAsset metadata, BaekhoPropCatalog.Measurement measured)
        {
            var json = new StringBuilder(JsonUtility.ToJson(new Entry
            {
                guid = guid.ToLowerInvariant(), path = path, role = metadata == null ? "prop" : LowerFirst(metadata.role.ToString()), size = measured.bounds.size,
                bounds = new BaekhoPropCatalog.LocalBounds { center = measured.bounds.center, size = measured.bounds.size }
            }));
            json.Length--;
            if (metadata == null) return json.Append(",\"scale\":").Append(JsonUtility.ToJson(Vector3.one)).Append('}').ToString();
            string label = (metadata.label ?? "").Trim();
            if (label.Length > 0) json.Append(",\"label\":").Append(Quote(label));
            if (metadata.useInitialScale) json.Append(",\"scale\":").Append(JsonUtility.ToJson(metadata.initialScale));
            if (metadata.useInitialOffset) json.Append(",\"offset\":").Append(JsonUtility.ToJson(metadata.initialOffset));
            if (metadata.useInitialRotation) json.Append(",\"rotationY\":").Append(metadata.initialRotationY.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (metadata.role == BaekhoMapAsset.AssetRole.Stair || metadata.role == BaekhoMapAsset.AssetRole.Slope)
                json.Append(",\"stairNativeRise\":").Append(metadata.stairNativeRise.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (metadata.role == BaekhoMapAsset.AssetRole.Light && metadata.lightAnchor != BaekhoMapAsset.LightAnchor.Either)
                json.Append(",\"lightAnchor\":").Append(Quote(LowerFirst(metadata.lightAnchor.ToString())));
            if (measured.wallMount != null) json.Append(",\"wallMount\":").Append(JsonUtility.ToJson(measured.wallMount));
            return json.Append('}').ToString();
        }

        private static List<PropSetCandidate> ReadPropSets(string[] setGuids, SortedDictionary<string, string> diagnostics)
        {
            var unique = new HashSet<string>(setGuids, StringComparer.OrdinalIgnoreCase);
            if (unique.Count > MaxPropSets) throw new InvalidDataException("The asset catalog exceeds 256 Prop Sets. Limit the Prop Set assets before importing.");
            var sorted = new List<string>(unique); sorted.Sort(StringComparer.Ordinal);
            var result = new List<PropSetCandidate>();
            foreach (string guid in sorted)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("A Prop Set asset is missing or outside Assets: " + guid);
                try
                {
                    BaekhoPropSet set = AssetDatabase.LoadAssetAtPath<BaekhoPropSet>(path);
                    if (set == null || !AssetDatabase.IsMainAsset(set)) throw new InvalidDataException("A Prop Set must be a standalone .asset file.");
                    if (!set.importEnabled) throw new InvalidDataException("Prop Set import is disabled.");
                    string label = string.IsNullOrWhiteSpace(set.label) ? set.name.Trim() : set.label.Trim();
                    if (label.Length == 0 || label.Length > 80 || Array.Exists(label.ToCharArray(), char.IsControl))
                        throw new InvalidDataException("Prop Set name must contain 1–80 characters without control characters.");
                    if (!Enum.IsDefined(typeof(BaekhoPropSet.Usage), set.usage) || !Enum.IsDefined(typeof(BaekhoPropSet.Arrangement), set.arrangement))
                        throw new InvalidDataException("Select valid Prop Set usage and arrangement values.");
                    if (set.prefabs == null || set.prefabs.Length == 0) throw new InvalidDataException("Prop Set has no prefab members.");
                    if (set.prefabs.Length > MaxSetMembers) throw new InvalidDataException("Prop Set exceeds 256 prefab members.");
                    var candidate = new PropSetCandidate { entry = new PropSetEntry
                    {
                        guid = guid.ToLowerInvariant(), path = path, label = label,
                        usage = LowerFirst(set.usage.ToString()), arrangement = LowerFirst(set.arrangement.ToString())
                    } };
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int index = 0; index < set.prefabs.Length; index++)
                    {
                        GameObject prefab = set.prefabs[index];
                        string memberPath = prefab == null ? "" : AssetDatabase.GetAssetPath(prefab);
                        if (prefab == null || !EditorUtility.IsPersistent(prefab) || !AssetDatabase.IsMainAsset(prefab) || prefab.transform.parent != null ||
                            !memberPath.StartsWith("Assets/", StringComparison.Ordinal) || !memberPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                        {
                            AddDiagnostic(diagnostics, path, "Member " + (index + 1) + " must reference a Project .prefab root; null, scene, model and child references are excluded.");
                            continue;
                        }
                        string memberGuid = AssetDatabase.AssetPathToGUID(memberPath);
                        if (seen.Add(memberGuid)) candidate.members.Add(memberGuid);
                    }
                    candidate.members.Sort(StringComparer.Ordinal);
                    result.Add(candidate);
                }
                catch (Exception exception) { AddDiagnostic(diagnostics, path, DiagnosticReason(exception)); }
            }
            return result;
        }

        private static bool IsFloorPropRole(BaekhoMapAsset.AssetRole role)
        {
            return role == BaekhoMapAsset.AssetRole.Prop || role == BaekhoMapAsset.AssetRole.Decoration || role == BaekhoMapAsset.AssetRole.Vegetation;
        }

        private static void AddDiagnostic(SortedDictionary<string, string> diagnostics, string path, string reason)
        {
            string previous;
            if (diagnostics.TryGetValue(path, out previous)) reason = previous + " " + reason;
            diagnostics[path] = DiagnosticReason(new InvalidDataException(reason));
        }

        private static void ValidateMetadata(BaekhoMapAsset metadata)
        {
            if (!Enum.IsDefined(typeof(BaekhoMapAsset.AssetRole), metadata.role) || metadata.role == BaekhoMapAsset.AssetRole.Unspecified)
                throw new InvalidDataException("Select a valid Map Asset role; Unspecified is not imported.");
            string label = (metadata.label ?? "").Trim();
            if (label.Length > 80 || Array.Exists(label.ToCharArray(), char.IsControl))
                throw new InvalidDataException("The initial name must have at most 80 characters without control characters.");
            if (metadata.useInitialScale && !PositiveVector(metadata.initialScale, 1000000))
                throw new InvalidDataException("Initial scale must have finite positive XYZ values within 1,000,000.");
            if (metadata.useInitialOffset && !ValidVector(metadata.initialOffset, 1000000))
                throw new InvalidDataException("Initial offset must have finite XYZ values within ±1,000,000.");
            if (metadata.useInitialRotation && (!Finite(metadata.initialRotationY) || Math.Abs(metadata.initialRotationY) > 1000000))
                throw new InvalidDataException("Initial Y rotation must be finite and within ±1,000,000 degrees.");
            if ((metadata.role == BaekhoMapAsset.AssetRole.Stair || metadata.role == BaekhoMapAsset.AssetRole.Slope) &&
                (!Finite(metadata.stairNativeRise) || metadata.stairNativeRise <= 0 || metadata.stairNativeRise > 1000))
                throw new InvalidDataException("Native stair/slope rise must be greater than 0 and at most 1000m.");
            if (metadata.role == BaekhoMapAsset.AssetRole.Light && !Enum.IsDefined(typeof(BaekhoMapAsset.LightAnchor), metadata.lightAnchor))
                throw new InvalidDataException("Select a valid light installation surface.");
        }

        private static string Quote(string value)
        {
            string wrapped = JsonUtility.ToJson(new StringValue { value = value });
            return wrapped.Substring(9, wrapped.Length - 10);
        }
        private static string DiagnosticReason(Exception exception)
        {
            string message = exception.Message ?? "";
            var text = new StringBuilder(Math.Min(message.Length, 2000));
            foreach (char character in message)
            {
                if (text.Length >= 2000) break;
                text.Append(char.IsControl(character) ? ' ' : character);
            }
            string result = text.ToString().Trim();
            return result.Length == 0 ? "Could not read or measure this prefab." : result;
        }
        private static string LowerFirst(string value) { return char.ToLowerInvariant(value[0]) + value.Substring(1); }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool ValidVector(Vector3 value, float limit) { return Finite(value.x) && Finite(value.y) && Finite(value.z) && Math.Abs(value.x) <= limit && Math.Abs(value.y) <= limit && Math.Abs(value.z) <= limit; }
        private static bool PositiveVector(Vector3 value, float limit) { return ValidVector(value, limit) && value.x > 0 && value.y > 0 && value.z > 0; }
    }
}
