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
        private const int MaxConnections = 1600;

        [Serializable] private sealed class Manifest
        {
            public int schemaVersion;
            public string name;
            public int width;
            public int height;
            public float cellSize;
            public Placement[] placements;
            public Connection[] connections;
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

        [Serializable] private sealed class Connection
        {
            public string id;
            public string label;
            public string kind;
            public float width;
            public float height;
            public Coordinates position;
            public Coordinates rotation;
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
                    if (manifest.schemaVersion == 3 && item.role != "stair")
                        AlignToSurface(instance, item, manifest.cellSize);
                    if (prefabs[index] != null)
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                    }
                }
                if (manifest.schemaVersion >= 2)
                {
                    var connections = new GameObject("Connections");
                    SceneManager.MoveGameObjectToScene(connections, preview);
                    connections.transform.SetParent(root.transform, false);
                    foreach (Connection item in manifest.connections)
                    {
                        var socket = new GameObject(item.id);
                        SceneManager.MoveGameObjectToScene(socket, preview);
                        socket.transform.SetParent(connections.transform, false);
                        socket.transform.localPosition = item.position.Vector;
                        socket.transform.localRotation = Quaternion.Euler(item.rotation.Vector);
                        var metadata = socket.AddComponent<BaekhoMapConnection>();
                        metadata.id = item.id;
                        metadata.label = item.label;
                        metadata.kind = item.kind;
                        metadata.width = item.width;
                        metadata.height = item.height;
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
            if (manifest == null || (manifest.schemaVersion != 1 && manifest.schemaVersion != 2 && manifest.schemaVersion != 3))
                throw new InvalidDataException("Unsupported map schema; expected schemaVersion 1, 2 or 3.");
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
                bool marker = manifest.schemaVersion == 1 && (item.role == "spawn" || item.role == "exit");
                if (!marker && item.role != "floor" && item.role != "wall" && item.role != "door" && item.role != "prop" && item.role != "stair")
                    throw new InvalidDataException("Unknown role on placement " + item.id);
                ValidateVector(item.position, item.id + ".position", false);
                ValidateVector(item.rotation, item.id + ".rotation", false);
                ValidateVector(item.scale, item.id + ".scale", true);
                if (manifest.schemaVersion == 3 && item.role == "floor" &&
                    (item.rotation.x != 0 || item.rotation.z != 0))
                    throw new InvalidDataException("Surface-aligned floors require rotation.x=0 and rotation.z=0: " + item.id);
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
            if (manifest.schemaVersion >= 2) ValidateConnections(manifest);
            return prefabs;
        }

        private static void ValidateConnections(Manifest manifest)
        {
            if (manifest.connections == null || manifest.connections.Length < 1 || manifest.connections.Length > MaxConnections)
                throw new InvalidDataException("A schemaVersion 2 or 3 map must contain 1–1600 connections.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Connection item in manifest.connections)
            {
                if (item == null || string.IsNullOrEmpty(item.id) || item.id.Length > 80 ||
                    !Regex.IsMatch(item.id, @"^[A-Za-z0-9_-]+$") || !ids.Add(item.id))
                    throw new InvalidDataException("Connection IDs must be unique, 1–80 letters, numbers, '_' or '-'.");
                if (string.IsNullOrWhiteSpace(item.label) || item.label.Length > 80 || Regex.IsMatch(item.label, @"[\x00-\x1F\x7F]"))
                    throw new InvalidDataException("Connection label must contain 1–80 characters without control characters.");
                if (string.IsNullOrEmpty(item.kind) || item.kind.Length > 40 || !Regex.IsMatch(item.kind, @"^[A-Za-z0-9_-]+$"))
                    throw new InvalidDataException("Connection kind must contain 1–40 letters, numbers, '_' or '-'.");
                if (!Finite(item.width) || item.width < 0.25f || item.width > 10 || item.width > manifest.cellSize ||
                    !Finite(item.height) || item.height < 0.25f || item.height > 20)
                    throw new InvalidDataException("Connection width must be 0.25–10 and at most cellSize; height must be 0.25–20.");
                ValidateVector(item.position, item.id + ".position", false);
                ValidateVector(item.rotation, item.id + ".rotation", false);
                if (item.rotation.x != 0 || item.rotation.z != 0 ||
                    (item.rotation.y != 0 && item.rotation.y != 90 && item.rotation.y != 180 && item.rotation.y != 270))
                    throw new InvalidDataException("Connection rotation must be x=0, z=0, y=0/90/180/270, facing outward.");
            }
        }

        private static bool Finite(float number) { return !float.IsNaN(number) && !float.IsInfinity(number); }

        // v3 uses a walkable surface Y, independent of the original prefab's pivot.
        // Measure at the origin so elevated maps do not lose thin geometry to float cancellation.
        private static void AlignToSurface(GameObject instance, Placement item, float cellSize)
        {
            Transform root = instance.transform;
            root.localPosition = Vector3.zero;
            if (item.role == "floor")
            {
                root.localScale = new Vector3(item.scale.x, 1, item.scale.z);
                VerticalBounds native = MeasureVerticalBounds(instance, item.id);
                double height = native.max - native.min;
                if (height > 0)
                {
                    double thickness = (double)cellSize * 0.12 * item.scale.y;
                    float fittedScale = CheckedFloat(thickness / height, item.id + ".fittedScale.y", true);
                    root.localScale = new Vector3(item.scale.x, fittedScale, item.scale.z);
                }
                else root.localScale = item.scale.Vector; // An authored plane has no thickness to resize.
            }
            VerticalBounds bounds = MeasureVerticalBounds(instance, item.id);
            double edge = item.role == "floor" ? bounds.max : bounds.min;
            root.localPosition = new Vector3(item.position.x,
                CheckedFloat((double)item.position.y - edge, item.id + ".alignedPosition.y", false), item.position.z);
        }

        private static float CheckedFloat(double value, string label, bool positive)
        {
            float result = (float)value;
            if (!Finite(result) || Math.Abs(value) > 1000000 || (positive && result <= 0))
                throw new InvalidDataException(label + " must be finite, within ±1000000" +
                    (positive ? " and a strictly positive Unity float." : "."));
            return result;
        }

        private struct VerticalBounds
        {
            public bool found;
            public double min;
            public double max;

            public void AddMesh(Mesh mesh, Transform transform, string id)
            {
                Bounds bounds = mesh.bounds;
                // Confirm near-flat source boxes from vertices. Unity's built-in Plane includes
                // ~1e-16 local Y roundoff; collapse only that source-space numerical residue.
                // A tiny scaled cube still has full source height and keeps overflow validation.
                if (mesh.isReadable && bounds.size.y <= Math.Max(bounds.size.x, bounds.size.z) * 0.000001f)
                {
                    Vector3[] vertices = mesh.vertices;
                    if (vertices.Length > 0)
                    {
                        bounds = new Bounds(vertices[0], Vector3.zero);
                        foreach (Vector3 vertex in vertices) bounds.Encapsulate(vertex);
                        if (bounds.size.y <= Math.Max(bounds.size.x, bounds.size.z) * 1e-12)
                            bounds.size = new Vector3(bounds.size.x, 0, bounds.size.z);
                    }
                }
                Add(bounds, transform, id);
            }

            public void Add(Bounds bounds, Transform transform, string id)
            {
                Vector3 low = bounds.min, high = bounds.max;
                Matrix4x4 matrix = transform.localToWorldMatrix;
                if (low.x > high.x || low.y > high.y || low.z > high.z)
                    throw new InvalidDataException("Invalid geometry bounds on " + id);
                for (int corner = 0; corner < 8; corner++)
                {
                    double x = (corner & 1) == 0 ? low.x : high.x;
                    double y = (corner & 2) == 0 ? low.y : high.y;
                    double z = (corner & 4) == 0 ? low.z : high.z;
                    double worldY = matrix.m10 * x + matrix.m11 * y + matrix.m12 * z + matrix.m13;
                    if (double.IsNaN(worldY) || double.IsInfinity(worldY))
                        throw new InvalidDataException("Non-finite geometry bounds on " + id);
                    if (!found) { min = max = worldY; found = true; }
                    else { min = Math.Min(min, worldY); max = Math.Max(max, worldY); }
                }
            }
        }

        private static VerticalBounds MeasureVerticalBounds(GameObject instance, string id)
        {
            var result = new VerticalBounds();
            // Local mesh bounds are stable in preview scenes and do not include particle/trail effects.
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    result.AddMesh(filter.sharedMesh, renderer.transform, id);
            }
            foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.sharedMesh != null)
                    result.Add(renderer.localBounds, renderer.transform, id);
            if (result.found) return result;
            // Collider-only blockouts are supported without querying physics-scene caches.
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger) continue;
                BoxCollider box = collider as BoxCollider;
                MeshCollider mesh = collider as MeshCollider;
                if (box != null) result.Add(new Bounds(box.center, box.size), box.transform, id);
                else if (mesh != null && mesh.sharedMesh != null) result.AddMesh(mesh.sharedMesh, mesh.transform, id);
            }
            if (!result.found)
                throw new InvalidDataException("Cannot measure " + id + ". Use an active mesh renderer or a non-trigger BoxCollider/MeshCollider for surface alignment.");
            return result;
        }

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
                float version = 0;
                bool hasConnections = false;
                ReadObject(new[] { "schemaVersion", "name", "width", "height", "cellSize", "placements" }, key =>
                {
                    if (key == "name") ReadString();
                    else if (key == "placements") ReadPlacements();
                    else if (key == "connections") { hasConnections = true; ReadConnections(); }
                    else if (key == "schemaVersion") version = ReadNumber(true);
                    else ReadNumber(key != "cellSize");
                }, new[] { "connections" });
                if ((version == 2 || version == 3) && !hasConnections) Fail("Missing field: connections");
                if (version != 2 && version != 3 && hasConnections) Fail("Connections require schemaVersion 2 or 3");
                White();
                if (cursor != json.Length) Fail("Unexpected content after the map object");
            }

            private void ReadConnections()
            {
                Expect('[');
                int count = 0;
                if (Take(']')) return;
                do
                {
                    if (++count > MaxConnections) Fail("Too many connections");
                    ReadObject(new[] { "id", "label", "kind", "width", "height", "position", "rotation" }, key =>
                    {
                        if (key == "id" || key == "label" || key == "kind") ReadString();
                        else if (key == "width" || key == "height") ReadNumber(false);
                        else ReadObject(new[] { "x", "y", "z" }, axis => ReadNumber(false));
                    });
                } while (Take(','));
                Expect(']');
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

            private void ReadObject(string[] keys, Action<string> read, string[] optionalKeys = null)
            {
                Expect('{');
                var remaining = new HashSet<string>(keys, StringComparer.Ordinal);
                var allowed = new HashSet<string>(keys, StringComparer.Ordinal);
                if (optionalKeys != null) allowed.UnionWith(optionalKeys);
                if (!Take('}'))
                {
                    do
                    {
                        string key = ReadString();
                        if (!allowed.Remove(key)) Fail("Unknown or duplicate field: " + key);
                        remaining.Remove(key);
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

            private float ReadNumber(bool integer)
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
                return number;
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
