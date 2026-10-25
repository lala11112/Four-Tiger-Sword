// Copy this file into Assets/Baekho/Editor in the destination Unity project.
// The map authoring application is external; this adapter only writes native prefabs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
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
        private const int MaxMarkers = 10000;
        private static readonly HashSet<string> ExtendedRoles = new HashSet<string>(StringComparer.Ordinal)
        {
            "ceiling", "bridge", "cliff", "slope", "light", "railing", "column", "arch", "water",
            "hazard", "trap", "gate", "switch", "secretDoor", "rest", "merchant", "altar", "decoration"
        };
        private static readonly HashSet<string> FixtureRoles = new HashSet<string>(StringComparer.Ordinal)
        {
            "light", "railing", "column", "arch", "water", "hazard", "trap", "gate", "switch",
            "secretDoor", "rest", "merchant", "altar", "decoration"
        };

        [Serializable] private sealed class Manifest
        {
            public int schemaVersion;
            public string name;
            public int width;
            public int height;
            public float cellSize;
            public Placement[] placements;
            public Connection[] connections;
            public GameplayMarker[] markers;
        }

        [Serializable] private sealed class Placement
        {
            public string id;
            public string role;
            public string prefabGuid;
            public Coordinates position;
            public Coordinates rotation;
            public Coordinates scale;
            public string surface;
            public float fitHeight;
            public float fitWidth;
            public Coordinates fitSize;
            public bool centerXZ;
            public string linkedId;
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

        [Serializable] private sealed class GameplayMarker
        {
            public string id;
            public string label;
            public string kind;
            public string contentKey;
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
                Debug.Log("Baekho map prefab created with baked NavMesh: " + output);
            }
            catch (Exception exception)
            {
                Debug.LogError("Baekho map import failed: " + exception.Message);
                EditorUtility.DisplayDialog("Baekho map import failed", exception.Message, "OK");
            }
        }

        /// <summary>Build a new prefab and baked NavMesh next to an Assets-relative manifest.</summary>
        public static string BuildFromPath(string manifestAssetPath)
        {
            return BuildFromPath(manifestAssetPath, true);
        }

        /// <summary>Set bakeNavMesh to false for geometry-only imports; -1 uses the project's first agent type.</summary>
        public static string BuildFromPath(string manifestAssetPath, bool bakeNavMesh, int agentTypeID = -1)
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
            Type surfaceType = null;
            if (bakeNavMesh)
            {
                surfaceType = RequireNavMeshSurface();
                if (agentTypeID == -1 && NavMesh.GetSettingsCount() > 0)
                    agentTypeID = NavMesh.GetSettingsByIndex(0).agentTypeID;
                bool validAgent = false;
                for (int index = 0; index < NavMesh.GetSettingsCount(); index++)
                    if (NavMesh.GetSettingsByIndex(index).agentTypeID == agentTypeID) validAgent = true;
                if (!validAgent)
                    throw new InvalidOperationException("Choose a valid Agent Type in Window > AI > Navigation before baking the map.");
            }
            string folder = Path.GetDirectoryName(manifestAssetPath).Replace('\\', '/');
            string output = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + manifest.name + ".prefab");
            string navigationOutput = bakeNavMesh
                ? AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(output, ".NavMesh.asset")) : null;
            NavMeshData navigationData = null;
            bool attemptedNavigationSave = false;
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
                    if (HasFitSize(item))
                    {
                        FitCenteredSize(instance, item);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                        continue;
                    }
                    Vector3 widthOffset = Vector3.zero;
                    if (manifest.schemaVersion >= 4 && item.fitWidth > 0)
                        widthOffset = FitDoorWidth(instance, item);
                    bool bottomStair = manifest.schemaVersion >= 4 && (item.role == "stair" || item.role == "slope") && PlacementSurface(item) == "bottom";
                    if (item.centerXZ || bottomStair) AlignBottomCenter(instance, item, item.centerXZ);
                    if (!item.centerXZ && !bottomStair && manifest.schemaVersion == 3 && item.role != "stair")
                        AlignToSurface(instance, item, manifest.cellSize);
                    else if (!item.centerXZ && !bottomStair && manifest.schemaVersion >= 4 && PlacementSurface(item) != "pivot")
                        AlignToSurface(instance, item, manifest.cellSize, PlacementSurface(item));
                    if (item.fitWidth > 0)
                    {
                        Vector3 aligned = instance.transform.localPosition;
                        instance.transform.localPosition = new Vector3(
                            CheckedFloat((double)aligned.x - widthOffset.x, item.id + ".alignedPosition.x", false),
                            aligned.y, CheckedFloat((double)aligned.z - widthOffset.z, item.id + ".alignedPosition.z", false));
                    }
                    if (manifest.schemaVersion >= 4 && FixtureRoles.Contains(item.role))
                    {
                        var metadata = instance.GetComponent<BaekhoMapFixture>() ?? instance.AddComponent<BaekhoMapFixture>();
                        metadata.id = item.id;
                        metadata.role = item.role;
                        metadata.linkedId = item.linkedId ?? "";
                        PrefabUtility.RecordPrefabInstancePropertyModifications(metadata);
                    }
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
                if (manifest.schemaVersion >= 4)
                {
                    var markers = new GameObject("GameplayMarkers");
                    SceneManager.MoveGameObjectToScene(markers, preview);
                    markers.transform.SetParent(root.transform, false);
                    foreach (GameplayMarker item in manifest.markers)
                    {
                        var marker = new GameObject(item.id);
                        SceneManager.MoveGameObjectToScene(marker, preview);
                        marker.transform.SetParent(markers.transform, false);
                        marker.transform.localPosition = item.position.Vector;
                        marker.transform.localRotation = Quaternion.Euler(item.rotation.Vector);
                        var metadata = marker.AddComponent<BaekhoGameplayMarker>();
                        metadata.id = item.id;
                        metadata.label = item.label;
                        metadata.kind = item.kind;
                        metadata.contentKey = item.contentKey;
                    }
                }
                if (bakeNavMesh)
                {
                    navigationData = BakeNavigation(root, surfaceType, agentTypeID);
                    // BuildNavMesh creates transient data. Persist it before serializing the prefab reference.
                    if (File.Exists(Path.GetFullPath(navigationOutput)))
                        throw new IOException("The NavMesh output path was created by another process. Try again.");
                    attemptedNavigationSave = true;
                    AssetDatabase.CreateAsset(navigationData, navigationOutput);
                    AssetDatabase.SaveAssetIfDirty(navigationData);
                    if (AssetDatabase.LoadAssetAtPath<NavMeshData>(navigationOutput) != navigationData)
                        throw new IOException("Unity could not save the generated NavMesh data.");
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
                if (attemptedNavigationSave && !saved) AssetDatabase.DeleteAsset(navigationOutput);
                if (navigationData != null && !EditorUtility.IsPersistent(navigationData))
                    UnityEngine.Object.DestroyImmediate(navigationData);
            }
        }

        // Resolve the optional package at runtime so copying this importer never breaks compilation.
        private static Type RequireNavMeshSurface()
        {
            Type type = Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation");
            if (type == null)
                throw new InvalidOperationException("Automatic NavMesh baking requires AI Navigation (com.unity.ai.navigation). " +
                    "Install AI Navigation from Window > Package Manager > Unity Registry, wait for compilation, then run Build Prefab again.");
            return type;
        }

        private static NavMeshData BakeNavigation(GameObject root, Type surfaceType, int agentTypeID)
        {
            // Rebuild one surface for the assembled map instead of also loading stale per-piece NavMeshes.
            foreach (Component child in root.GetComponentsInChildren(surfaceType, true))
            {
                var behaviour = child as Behaviour;
                if (behaviour == null || !behaviour.enabled) continue;
                behaviour.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
            }
            Component surface = root.AddComponent(surfaceType);
            SetNavigationProperty(surface, "agentTypeID", agentTypeID);
            SetNavigationProperty(surface, "collectObjects", "Children");
            SetNavigationProperty(surface, "useGeometry", NavMeshCollectGeometry.RenderMeshes);
            SetNavigationProperty(surface, "layerMask", (LayerMask)(-1));
            PropertyInfo dataProperty = NavigationProperty(surfaceType, "navMeshData");
            MethodInfo build = surfaceType.GetMethod("BuildNavMesh", Type.EmptyTypes);
            if (build == null) throw new InvalidOperationException("AI Navigation does not expose NavMeshSurface.BuildNavMesh. Update the package.");
            NavMeshData data = null;
            bool built = false;
            try
            {
                build.Invoke(surface, null);
                data = dataProperty.GetValue(surface, null) as NavMeshData;
                if (data == null)
                    throw new InvalidOperationException("NavMesh baking produced no data. Check the map geometry and Navigation Agent settings.");
                data.name = root.name + " NavMesh";
                built = true;
                return data;
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException("NavMesh baking failed: " +
                    (exception.InnerException ?? exception).Message, exception.InnerException ?? exception);
            }
            finally
            {
                // Preview scenes do not register surfaces with the navigation system. Still release the
                // temporary data on failure; destroying a Surface itself does not destroy its NavMeshData.
                if (!built)
                {
                    data = dataProperty.GetValue(surface, null) as NavMeshData;
                    if (data != null && !EditorUtility.IsPersistent(data)) UnityEngine.Object.DestroyImmediate(data);
                }
            }
        }

        private static PropertyInfo NavigationProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(name);
            if (property == null)
                throw new InvalidOperationException("AI Navigation is missing NavMeshSurface." + name + ". Update the package.");
            return property;
        }

        private static void SetNavigationProperty(Component surface, string name, object value)
        {
            PropertyInfo property = NavigationProperty(surface.GetType(), name);
            if (value is string && property.PropertyType.IsEnum)
                value = Enum.Parse(property.PropertyType, (string)value);
            property.SetValue(surface, value, null);
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
            if (manifest == null || manifest.schemaVersion < 1 || manifest.schemaVersion > 5)
                throw new InvalidDataException("Unsupported map schema; expected schemaVersion 1, 2, 3, 4 or 5.");
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
                if (!marker && item.role != "floor" && item.role != "wall" && item.role != "door" && item.role != "prop" && item.role != "stair" &&
                    !(manifest.schemaVersion >= 4 && ExtendedRoles.Contains(item.role)) &&
                    !(manifest.schemaVersion == 5 && item.role == "deadzone"))
                    throw new InvalidDataException("Unknown role on placement " + item.id);
                ValidateVector(item.position, item.id + ".position", false);
                ValidateVector(item.rotation, item.id + ".rotation", false);
                ValidateVector(item.scale, item.id + ".scale", true);
                if (item.centerXZ && (manifest.schemaVersion != 5 || (item.role != "prop" && item.role != "decoration") ||
                    PlacementSurface(item) != "bottom" || HasFitSize(item) || item.fitHeight != 0 || item.fitWidth != 0 || item.rotation.x != 0 || item.rotation.z != 0))
                    throw new InvalidDataException("centerXZ requires a v5 measured prop or decoration, bottom alignment, no X/Z tilt and no fit fields: " + item.id);
                if (HasFitSize(item))
                {
                    ValidateVector(item.fitSize, item.id + ".fitSize", true);
                    if (manifest.schemaVersion != 5 || (item.role != "wall" && item.role != "cliff" && item.role != "floor" && item.role != "deadzone") ||
                        item.fitHeight != 0 || item.fitWidth != 0 || item.surface != "pivot" || item.rotation.x != 0 || item.rotation.z != 0)
                        throw new InvalidDataException("fitSize requires a v5 wall, cliff, floor or deadzone with pivot centering, no X/Z tilt, and no other fit fields: " + item.id);
                }
                if (manifest.schemaVersion == 3 && item.role == "floor" &&
                    (item.rotation.x != 0 || item.rotation.z != 0))
                    throw new InvalidDataException("Surface-aligned floors require rotation.x=0 and rotation.z=0: " + item.id);
                if (manifest.schemaVersion >= 4)
                {
                    if (item.surface != null && item.surface != "top" && item.surface != "bottom" && item.surface != "pivot")
                        throw new InvalidDataException("Surface must be top, bottom or pivot: " + item.id);
                    if (item.fitHeight != 0 && (item.role != "wall" && item.role != "cliff" ||
                        !Finite(item.fitHeight) || item.fitHeight <= 0 || item.fitHeight > 1000000 || PlacementSurface(item) == "pivot"))
                        throw new InvalidDataException("fitHeight requires a surface-aligned wall or cliff and a positive height at most 1000000: " + item.id);
                    if (item.fitWidth != 0 && (item.role != "door" || !Finite(item.fitWidth) || item.fitWidth <= 0 || item.fitWidth > 40 ||
                        PlacementSurface(item) != "bottom" || item.rotation.x != 0 || item.rotation.z != 0))
                        throw new InvalidDataException("fitWidth requires a bottom-aligned door without X/Z tilt and a positive width at most 40: " + item.id);
                    if (PlacementSurface(item) != "pivot" &&
                        (item.role == "floor" || item.role == "ceiling" || item.role == "stair" || item.role == "slope" || item.fitHeight > 0) &&
                        (item.rotation.x != 0 || item.rotation.z != 0))
                        throw new InvalidDataException("Fitted geometry requires rotation.x=0 and rotation.z=0: " + item.id);
                }
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
            if (manifest.schemaVersion >= 4)
                foreach (Placement item in manifest.placements)
                    if (!string.IsNullOrEmpty(item.linkedId) && (!FixtureRoles.Contains(item.role) ||
                        item.linkedId == item.id || !ids.Contains(item.linkedId)))
                        throw new InvalidDataException("Fixture linkedId must refer to another placement in this map: " + item.id);
            if (manifest.schemaVersion >= 2) ValidateConnections(manifest);
            if (manifest.schemaVersion >= 4) ValidateMarkers(manifest);
            return prefabs;
        }

        private static void ValidateConnections(Manifest manifest)
        {
            if (manifest.connections == null || manifest.connections.Length < 1 || manifest.connections.Length > MaxConnections)
                throw new InvalidDataException("A schemaVersion 2, 3 or 4 map must contain 1–1600 connections.");
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
                float maxWidth = manifest.schemaVersion >= 4 ? 40 : Math.Min(10, manifest.cellSize);
                if (!Finite(item.width) || item.width < 0.25f || item.width > maxWidth ||
                    !Finite(item.height) || item.height < 0.25f || item.height > 20)
                    throw new InvalidDataException("Connection width must be 0.25–40 for v4 (v1–3: at most 10 and cellSize); height must be 0.25–20.");
                ValidateVector(item.position, item.id + ".position", false);
                ValidateVector(item.rotation, item.id + ".rotation", false);
                if (item.rotation.x != 0 || item.rotation.z != 0 ||
                    (item.rotation.y != 0 && item.rotation.y != 90 && item.rotation.y != 180 && item.rotation.y != 270))
                    throw new InvalidDataException("Connection rotation must be x=0, z=0, y=0/90/180/270, facing outward.");
            }
        }

        private static void ValidateMarkers(Manifest manifest)
        {
            if (manifest.markers == null || manifest.markers.Length > MaxMarkers)
                throw new InvalidDataException("A schemaVersion 4 map must contain a markers array of at most 10000 entries.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (GameplayMarker item in manifest.markers)
            {
                if (item == null || string.IsNullOrEmpty(item.id) || item.id.Length > 100 ||
                    !Regex.IsMatch(item.id, @"^[A-Za-z0-9_-]+$") || !ids.Add(item.id))
                    throw new InvalidDataException("Marker IDs must be unique, 1–100 letters, numbers, '_' or '-'.");
                if (string.IsNullOrWhiteSpace(item.label) || item.label.Length > 80 || Regex.IsMatch(item.label, @"[\x00-\x1F\x7F]"))
                    throw new InvalidDataException("Marker label must contain 1–80 characters without control characters.");
                if (item.kind != "enemy" && item.kind != "reward" && item.kind != "event")
                    throw new InvalidDataException("Marker kind must be enemy, reward or event: " + item.id);
                if (item.contentKey == null || item.contentKey.Length > 200 || Regex.IsMatch(item.contentKey, @"[\x00-\x1F\x7F]"))
                    throw new InvalidDataException("Marker contentKey must contain at most 200 characters without control characters.");
                ValidateVector(item.position, item.id + ".position", false);
                ValidateVector(item.rotation, item.id + ".rotation", false);
            }
        }

        private static string PlacementSurface(Placement item)
        {
            if (!string.IsNullOrEmpty(item.surface)) return item.surface;
            if (item.role == "floor" || item.role == "bridge") return "top";
            if (item.role == "stair" || item.role == "slope" || item.role == "light") return "pivot";
            return "bottom";
        }

        private static bool Finite(float number) { return !float.IsNaN(number) && !float.IsInfinity(number); }

        // JsonUtility may materialize an omitted nested object with zero fields. Explicit zero
        // vectors are already rejected by ManifestShape, so this also preserves v1-v4 imports.
        private static bool HasFitSize(Placement item)
        {
            return item.fitSize != null && (item.fitSize.x != 0 || item.fitSize.y != 0 || item.fitSize.z != 0);
        }

        // Props use catalog unions including triggers. Bottom-aligned stairs/slopes instead use
        // renderer-first bounds (non-trigger collider fallback) so interaction triggers cannot move
        // their support base. Both preserve configured root scales and center the footprint in XZ.
        private static void AlignBottomCenter(GameObject instance, Placement item, bool unionColliders)
        {
            instance.transform.localPosition = Vector3.zero;
            VerticalBounds x = MeasureVerticalBounds(instance, item.id, 0, false, unionColliders);
            VerticalBounds y = MeasureVerticalBounds(instance, item.id, 1, false, unionColliders);
            VerticalBounds z = MeasureVerticalBounds(instance, item.id, 2, false, unionColliders);
            instance.transform.localPosition = new Vector3(
                CheckedFloat((double)item.position.x - (x.min + x.max) / 2, item.id + ".centeredPosition.x", false),
                CheckedFloat((double)item.position.y - y.min, item.id + ".centeredPosition.y", false),
                CheckedFloat((double)item.position.z - (z.min + z.max) / 2, item.id + ".centeredPosition.z", false));
        }

        // v5 fitSize is target size in the unrotated authored XYZ axes. Position is the measured
        // box center after yaw. Resize all three axes together, then compensate the original pivot.
        private static void FitCenteredSize(GameObject instance, Placement item)
        {
            Transform root = instance.transform;
            Quaternion rotation = root.localRotation;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            bool colliders = item.role == "deadzone";
            var measured = new VerticalBounds[3];
            for (int axis = 0; axis < 3; axis++) measured[axis] = MeasureVerticalBounds(instance, item.id, axis, colliders);
            Vector3 target = item.fitSize.Vector;
            var scale = Vector3.one;
            for (int axis = 0; axis < 3; axis++)
            {
                double size = measured[axis].max - measured[axis].min;
                if (size <= 0) throw new InvalidDataException("Cannot fit zero-size geometry on axis " + axis + ": " + item.id);
                scale[axis] = CheckedFloat(target[axis] / size, item.id + ".fittedScale." + axis, true);
            }
            root.localScale = scale;
            Vector3 center = Vector3.zero;
            for (int axis = 0; axis < 3; axis++)
            {
                VerticalBounds fitted = MeasureVerticalBounds(instance, item.id, axis, colliders);
                center[axis] = CheckedFloat((fitted.min + fitted.max) / 2, item.id + ".fittedCenter." + axis, false);
            }
            root.localRotation = rotation;
            Vector3 correction = rotation * center;
            root.localPosition = new Vector3(
                CheckedFloat((double)item.position.x - correction.x, item.id + ".alignedPosition.x", false),
                CheckedFloat((double)item.position.y - correction.y, item.id + ".alignedPosition.y", false),
                CheckedFloat((double)item.position.z - correction.z, item.id + ".alignedPosition.z", false));
        }

        // A wide door uses its authored local X as the width axis. Recenter off-axis pivots
        // before restoring yaw so the entire measured prefab stays on the opening center.
        private static Vector3 FitDoorWidth(GameObject instance, Placement item)
        {
            Transform root = instance.transform;
            Quaternion rotation = root.localRotation;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = new Vector3(1, item.scale.y, item.scale.z);
            VerticalBounds native = MeasureVerticalBounds(instance, item.id, 0);
            double width = native.max - native.min;
            if (width <= 0) throw new InvalidDataException("Cannot fit a zero-width door: " + item.id);
            root.localScale = new Vector3(CheckedFloat(item.fitWidth / width, item.id + ".fittedScale.x", true), item.scale.y, item.scale.z);
            VerticalBounds fitted = MeasureVerticalBounds(instance, item.id, 0);
            float center = CheckedFloat((fitted.min + fitted.max) / 2, item.id + ".widthCenter", false);
            root.localRotation = rotation;
            return rotation * new Vector3(center, 0, 0);
        }

        // v3 uses a walkable surface Y, independent of the original prefab's pivot.
        // Measure at the origin so elevated maps do not lose thin geometry to float cancellation.
        private static void AlignToSurface(GameObject instance, Placement item, float cellSize, string surface = null)
        {
            Transform root = instance.transform;
            root.localPosition = Vector3.zero;
            if (item.role == "floor" || item.role == "ceiling" || item.fitHeight > 0)
            {
                root.localScale = new Vector3(item.scale.x, 1, item.scale.z);
                VerticalBounds native = MeasureVerticalBounds(instance, item.id);
                double height = native.max - native.min;
                if (height > 0)
                {
                    double thickness = item.fitHeight > 0 ? item.fitHeight : (double)cellSize * 0.12 * item.scale.y;
                    float fittedScale = CheckedFloat(thickness / height, item.id + ".fittedScale.y", true);
                    root.localScale = new Vector3(item.scale.x, fittedScale, item.scale.z);
                }
                else if (item.fitHeight > 0)
                    throw new InvalidDataException("Cannot fit a zero-height wall or cliff: " + item.id);
                else root.localScale = item.scale.Vector; // An authored plane has no thickness to resize.
            }
            VerticalBounds bounds = MeasureVerticalBounds(instance, item.id);
            double edge = (surface ?? (item.role == "floor" ? "top" : "bottom")) == "top" ? bounds.max : bounds.min;
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
            public int axis;
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
                    double worldY = axis == 0 ? matrix.m00 * x + matrix.m01 * y + matrix.m02 * z + matrix.m03
                        : axis == 1 ? matrix.m10 * x + matrix.m11 * y + matrix.m12 * z + matrix.m13
                        : matrix.m20 * x + matrix.m21 * y + matrix.m22 * z + matrix.m23;
                    if (double.IsNaN(worldY) || double.IsInfinity(worldY))
                        throw new InvalidDataException("Non-finite geometry bounds on " + id);
                    if (!found) { min = max = worldY; found = true; }
                    else { min = Math.Min(min, worldY); max = Math.Max(max, worldY); }
                }
            }
        }

        private static VerticalBounds MeasureVerticalBounds(GameObject instance, string id, int axis = 1, bool colliderOnly = false, bool unionColliders = false)
        {
            var result = new VerticalBounds { axis = axis };
            // Local mesh bounds are stable in preview scenes and do not include particle/trail effects.
            foreach (MeshRenderer renderer in colliderOnly ? new MeshRenderer[0] : instance.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    result.AddMesh(filter.sharedMesh, renderer.transform, id);
            }
            foreach (SkinnedMeshRenderer renderer in colliderOnly ? new SkinnedMeshRenderer[0] : instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.sharedMesh != null)
                    result.Add(renderer.localBounds, renderer.transform, id);
            if (result.found && !unionColliders) return result;
            // Collider-only blockouts are supported without querying physics-scene caches.
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy || (!colliderOnly && !unionColliders && collider.isTrigger)) continue;
                BoxCollider box = collider as BoxCollider;
                MeshCollider mesh = collider as MeshCollider;
                if (box != null) result.Add(new Bounds(box.center, box.size), box.transform, id);
                else if (mesh != null && mesh.sharedMesh != null) result.AddMesh(mesh.sharedMesh, mesh.transform, id);
                else if (collider is SphereCollider)
                {
                    var sphere = (SphereCollider)collider;
                    result.Add(new Bounds(sphere.center, Vector3.one * sphere.radius * 2), sphere.transform, id);
                }
                else if (collider is CapsuleCollider)
                {
                    var capsule = (CapsuleCollider)collider;
                    Vector3 size = Vector3.one * capsule.radius * 2;
                    size[capsule.direction] = Math.Max(capsule.height, capsule.radius * 2);
                    result.Add(new Bounds(capsule.center, size), capsule.transform, id);
                }
            }
            if (!result.found)
                throw new InvalidDataException("Cannot measure " + id + (colliderOnly ? ". Deadzones require an active Collider (triggers included)." : ". Use an active mesh renderer or supported Collider for surface alignment."));
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
            private bool hasPlacementV4Fields;
            private bool hasPlacementV5Fields;
            public ManifestShape(string source) { json = source; }
            public void Validate()
            {
                float version = 0;
                bool hasConnections = false;
                bool hasMarkers = false;
                ReadObject(new[] { "schemaVersion", "name", "width", "height", "cellSize", "placements" }, key =>
                {
                    if (key == "name") ReadString();
                    else if (key == "placements") ReadPlacements();
                    else if (key == "connections") { hasConnections = true; ReadConnections(); }
                    else if (key == "markers") { hasMarkers = true; ReadMarkers(); }
                    else if (key == "schemaVersion") version = ReadNumber(true);
                    else ReadNumber(key != "cellSize");
                }, new[] { "connections", "markers" });
                if ((version >= 2 && version <= 5) && !hasConnections) Fail("Missing field: connections");
                if ((version < 2 || version > 5) && hasConnections) Fail("Connections require schemaVersion 2, 3, 4 or 5");
                if (version >= 4 && !hasMarkers) Fail("Missing field: markers");
                if (version < 4 && (hasMarkers || hasPlacementV4Fields)) Fail("Markers and placement surface/fitHeight/fitWidth/linkedId require schemaVersion 4 or 5");
                if (version != 5 && hasPlacementV5Fields) Fail("fitSize requires schemaVersion 5");
                White();
                if (cursor != json.Length) Fail("Unexpected content after the map object");
            }

            private void ReadMarkers()
            {
                Expect('[');
                int count = 0;
                if (Take(']')) return;
                do
                {
                    if (++count > MaxMarkers) Fail("Too many markers");
                    ReadObject(new[] { "id", "label", "kind", "contentKey", "position", "rotation" }, key =>
                    {
                        if (key == "position" || key == "rotation")
                            ReadObject(new[] { "x", "y", "z" }, axis => ReadNumber(false));
                        else ReadString();
                    });
                } while (Take(','));
                Expect(']');
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
                        else if (key == "surface" || key == "linkedId") { hasPlacementV4Fields = true; ReadString(); }
                        else if (key == "fitHeight" || key == "fitWidth")
                        {
                            hasPlacementV4Fields = true;
                            if (ReadNumber(false) <= 0) Fail(key + " must be strictly positive");
                        }
                        else if (key == "fitSize")
                        {
                            hasPlacementV5Fields = true;
                            ReadObject(new[] { "x", "y", "z" }, axis => { if (ReadNumber(false) <= 0) Fail("fitSize axes must be strictly positive"); });
                        }
                        else if (key == "centerXZ")
                        {
                            hasPlacementV5Fields = true;
                            White();
                            if (cursor + 4 > json.Length || json.Substring(cursor, 4) != "true") Fail("centerXZ must be true when supplied");
                            cursor += 4;
                        }
                        else ReadObject(new[] { "x", "y", "z" }, axis => ReadNumber(false));
                    }, new[] { "surface", "fitHeight", "fitWidth", "fitSize", "centerXZ", "linkedId" });
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
