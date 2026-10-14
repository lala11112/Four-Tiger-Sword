// Copy this file into Assets/Baekho/Editor in the destination Unity project.
// The map authoring application is external; this adapter only writes native prefabs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StudioBaekho
{
    public static class BaekhoMapImporter
    {
        private const string MenuPath = "Assets/Baekho/Build Prefab";
        private const string Extension = ".baekhomap.json";
        private const int MaxBytes = 8 * 1024 * 1024;
        private const int MaxPlacements = 10000;

        [Serializable] private sealed class Manifest
        {
            public int schemaVersion;
            public string name;
            public int width;
            public int height;
            public float cellSize;
            public Placement[] placements;
        }

        [Serializable] private sealed class Placement
        {
            public string id;
            public string role;
            public string prefabGuid;
            public Coordinates position;
            public Coordinates rotation;
            public Coordinates scale;
        }

        [Serializable] private sealed class Coordinates
        {
            public float x;
            public float y;
            public float z;
            public Vector3 Vector { get { return new Vector3(x, y, z); } }
        }

        [MenuItem(MenuPath, true)]
        private static bool CanBuildSelected()
        {
            return Selection.activeObject is TextAsset &&
                AssetDatabase.GetAssetPath(Selection.activeObject).EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
        }

        [MenuItem(MenuPath)]
        private static void BuildSelected()
        {
            try
            {
                string output = BuildFromPath(AssetDatabase.GetAssetPath(Selection.activeObject));
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(output);
                EditorGUIUtility.PingObject(Selection.activeObject);
                Debug.Log("Baekho map prefab created: " + output);
            }
            catch (Exception exception)
            {
                Debug.LogError("Baekho map import failed: " + exception.Message);
                EditorUtility.DisplayDialog("Baekho map import failed", exception.Message, "OK");
            }
        }

        /// <summary>Build a new prefab next to an Assets-relative manifest; never overwrites an existing asset.</summary>
        public static string BuildFromPath(string manifestAssetPath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building a prefab.");
            string absolute = RequireManifestPath(manifestAssetPath);
            var info = new FileInfo(absolute);
            if (!info.Exists || info.Length == 0 || info.Length > MaxBytes)
                throw new InvalidDataException("Manifest must exist and contain between 1 byte and 8 MiB.");
            string json = File.ReadAllText(absolute, new UTF8Encoding(false, true));
            // JsonUtility silently defaults omitted numbers. Validate the exact JSON shape first.
            new ManifestShape(json).Validate();
            Manifest manifest = JsonUtility.FromJson<Manifest>(json);
            GameObject[] prefabs = ValidateManifest(manifest);
            string folder = Path.GetDirectoryName(manifestAssetPath).Replace('\\', '/');
            string output = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + manifest.name + ".prefab");
            Scene preview = default(Scene);
            bool saved = false;
            bool attemptedSave = false;
            try
            {
                preview = EditorSceneManager.NewPreviewScene();
                // NewGameObject defaults to the active scene; immediately move it to the isolated preview.
                var root = new GameObject(manifest.name);
                SceneManager.MoveGameObjectToScene(root, preview);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                for (int index = 0; index < manifest.placements.Length; index++)
                {
                    Placement item = manifest.placements[index];
                    GameObject instance;
                    if (prefabs[index] != null)
                        instance = PrefabUtility.InstantiatePrefab(prefabs[index], preview) as GameObject;
                    else
                    {
                        instance = new GameObject();
                        SceneManager.MoveGameObjectToScene(instance, preview);
                    }
                    if (instance == null) throw new InvalidOperationException("Could not instantiate " + item.id);
                    instance.name = item.role + "_" + item.id;
                    instance.transform.SetParent(root.transform, false);
                    instance.transform.localPosition = item.position.Vector;
                    instance.transform.localRotation = Quaternion.Euler(item.rotation.Vector);
                    instance.transform.localScale = item.scale.Vector;
                    if (prefabs[index] != null)
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                    }
                }
                // GenerateUniqueAssetPath protects existing prefabs; check again immediately before saving.
                if (File.Exists(Path.GetFullPath(output)))
                    throw new IOException("The output path was created by another process. Try again.");
                attemptedSave = true;
                GameObject result = PrefabUtility.SaveAsPrefabAsset(root, output, out saved);
                if (!saved || result == null)
                {
                    saved = false;
                    throw new IOException("Unity could not save the generated prefab.");
                }
                return output;
            }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                if (attemptedSave && !saved) AssetDatabase.DeleteAsset(output);
            }
        }

        private static string RequireManifestPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                !assetPath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || assetPath.Contains("\\"))
                throw new InvalidDataException("Select a .baekhomap.json file inside this project's Assets folder.");
            string absolute = Path.GetFullPath(assetPath);
            string assetRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(assetRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Manifest must remain inside the project's Assets folder.");
            return absolute;
        }

        private static GameObject[] ValidateManifest(Manifest manifest)
        {
            if (manifest == null || manifest.schemaVersion != 1)
                throw new InvalidDataException("Unsupported map schema; expected schemaVersion 1.");
            if (string.IsNullOrWhiteSpace(manifest.name) || manifest.name.Length > 80 || manifest.name != manifest.name.Trim() ||
                manifest.name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0 || manifest.name.EndsWith(".") ||
                Regex.IsMatch(manifest.name, @"[\x00-\x1F\x7F]") ||
                Regex.IsMatch(manifest.name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)", RegexOptions.IgnoreCase))
                throw new InvalidDataException("Map name must be a valid file name of 1–80 characters.");
            if (manifest.width < 1 || manifest.width > 256 || manifest.height < 1 || manifest.height > 256 ||
                !Finite(manifest.cellSize) || manifest.cellSize <= 0 || manifest.cellSize > 1000)
                throw new InvalidDataException("Map dimensions must be 1–256; cellSize must be greater than 0 and at most 1000.");
            if (manifest.placements == null || manifest.placements.Length < 1 || manifest.placements.Length > MaxPlacements)
                throw new InvalidDataException("A map must contain 1–10000 placements.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var prefabs = new GameObject[manifest.placements.Length];
            var cache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < manifest.placements.Length; index++)
            {
                Placement item = manifest.placements[index];
                if (item == null || string.IsNullOrWhiteSpace(item.id) || item.id.Length > 100 ||
                    !Regex.IsMatch(item.id, @"^[A-Za-z0-9_-]+$") || !ids.Add(item.id))
                    throw new InvalidDataException("Placement IDs must be unique and contain only letters, numbers, '_' and '-'.");
                bool marker = item.role == "spawn" || item.role == "exit";
                if (!marker && item.role != "floor" && item.role != "wall" && item.role != "door" && item.role != "prop" && item.role != "stair")
                    throw new InvalidDataException("Unknown role on placement " + item.id);
                ValidateVector(item.position, item.id + ".position", false);
                ValidateVector(item.rotation, item.id + ".rotation", false);
                ValidateVector(item.scale, item.id + ".scale", true);
                if (marker && string.IsNullOrEmpty(item.prefabGuid)) continue;
                if (item.prefabGuid == null || !Regex.IsMatch(item.prefabGuid, @"^[a-fA-F0-9]{32}$"))
                    throw new InvalidDataException("A valid prefab GUID is required for " + item.id);
                GameObject prefab;
                if (!cache.TryGetValue(item.prefabGuid, out prefab))
                {
                    string path = AssetDatabase.GUIDToAssetPath(item.prefabGuid);
                    if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Prefab for " + item.id + " was not found in Assets. Rebind it in Baekho.");
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab))
                        throw new InvalidDataException("Invalid prefab for " + item.id + ": " + path);
                    cache.Add(item.prefabGuid, prefab);
                }
                prefabs[index] = prefab;
            }
            return prefabs;
        }

        private static bool Finite(float number) { return !float.IsNaN(number) && !float.IsInfinity(number); }

        private static void ValidateVector(Coordinates value, string label, bool positive)
        {
            if (value == null || !Finite(value.x) || !Finite(value.y) || !Finite(value.z) ||
                Mathf.Abs(value.x) > 1000000 || Mathf.Abs(value.y) > 1000000 || Mathf.Abs(value.z) > 1000000 ||
                (positive && (value.x <= 0 || value.y <= 0 || value.z <= 0)))
                throw new InvalidDataException(label + " must contain finite x, y, z values within ±1000000" +
                    (positive ? " and strictly positive scale." : "."));
        }

        // Small schema-specific reader: enforces required fields, number types, no duplicate keys,
        // and bounded arrays before JsonUtility can silently repair malformed/missing values.
        private sealed class ManifestShape
        {
            private readonly string json;
            private int cursor;
            public ManifestShape(string source) { json = source; }
            public void Validate()
            {
                ReadObject(new[] { "schemaVersion", "name", "width", "height", "cellSize", "placements" }, key =>
                {
                    if (key == "name") ReadString();
                    else if (key == "placements") ReadPlacements();
                    else ReadNumber(key != "cellSize");
                });
                White();
                if (cursor != json.Length) Fail("Unexpected content after the map object");
            }

            private void ReadPlacements()
            {
                Expect('[');
                int count = 0;
                if (Take(']')) return;
                do
                {
                    if (++count > MaxPlacements) Fail("Too many placements");
                    ReadObject(new[] { "id", "role", "prefabGuid", "position", "rotation", "scale" }, key =>
                    {
                        if (key == "id" || key == "role" || key == "prefabGuid") ReadString();
                        else ReadObject(new[] { "x", "y", "z" }, axis => ReadNumber(false));
                    });
                } while (Take(','));
                Expect(']');
            }

            private void ReadObject(string[] keys, Action<string> read)
            {
                Expect('{');
                var remaining = new HashSet<string>(keys, StringComparer.Ordinal);
                if (!Take('}'))
                {
                    do
                    {
                        string key = ReadString();
                        if (!remaining.Remove(key)) Fail("Unknown or duplicate field: " + key);
                        Expect(':');
                        read(key);
                    } while (Take(','));
                    Expect('}');
                }
                if (remaining.Count != 0) Fail("Missing field: " + string.Join(", ", remaining));
            }

            private string ReadString()
            {
                Expect('"');
                var result = new StringBuilder();
                while (cursor < json.Length)
                {
                    char next = json[cursor++];
                    if (next == '"') return result.ToString();
                    if (next < 32) Fail("Control character in string");
                    if (next != '\\') { result.Append(next); continue; }
                    if (cursor == json.Length) Fail("Unterminated escape");
                    next = json[cursor++];
                    switch (next)
                    {
                        case '"': case '\\': case '/': result.Append(next); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u':
                            ushort unicode;
                            if (cursor + 4 > json.Length || !ushort.TryParse(json.Substring(cursor, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out unicode)) { Fail("Invalid unicode escape"); break; }
                            result.Append((char)unicode);
                            cursor += 4;
                            break;
                        default: Fail("Invalid escape"); break;
                    }
                }
                Fail("Unterminated string");
                return null;
            }

            private void ReadNumber(bool integer)
            {
                White();
                int start = cursor;
                while (cursor < json.Length && "0123456789.eE+-".IndexOf(json[cursor]) >= 0) cursor++;
                string token = json.Substring(start, cursor - start);
                if (!Regex.IsMatch(token, integer ? @"^-?(0|[1-9][0-9]*)$" : @"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$"))
                    Fail("Expected a JSON " + (integer ? "integer" : "number"));
                float number;
                if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out number) || !Finite(number))
                    Fail("Number is outside the finite float range");
                int whole;
                if (integer && !int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out whole))
                    Fail("Integer is outside the supported range");
            }

            private void White() { while (cursor < json.Length && " \t\r\n".IndexOf(json[cursor]) >= 0) cursor++; }
            private bool Take(char value)
            {
                White();
                if (cursor == json.Length || json[cursor] != value) return false;
                cursor++;
                return true;
            }
            private void Expect(char value) { if (!Take(value)) Fail("Expected '" + value + "'"); }
            private void Fail(string message) { throw new InvalidDataException(message + " at JSON offset " + cursor + "."); }
        }
    }
}
