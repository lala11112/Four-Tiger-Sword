using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StudioBaekho
{
    /// <summary>Prepares explicit runtime metadata and isolated demonstration assets.</summary>
    public static class BaekhoDungeonSetup
    {
        public const string OutputFolder = "Assets/_Game/ProceduralDungeon";
        private const string BlockoutFolder = "Assets/_Game/Greybox/Blockout";
        private static readonly string[] SourceModules =
        {
            "BaekhoMap2/Stepped_Quad.prefab", "BaekhoMap3/Stepped_Quad.prefab",
            "BaekhoMap4/Fourfold_Raised_Arena.prefab", "BaekhoMap4-2/Fourfold_Raised_Arena.prefab",
            "BaekhoMap4-3/Fourfold_Raised_Arena.prefab", "BaekhoMap5/FlankingArena.prefab",
            "BaekhoMap6/Flanking_Pit_Arena.prefab"
        };

        [Serializable] private sealed class Manifest
        {
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
            public Coordinates position;
            public Coordinates rotation;
        }

        [Serializable] private sealed class Coordinates
        {
            public float x;
            public float y;
            public float z;
            public Vector3 Vector { get { return new Vector3(x, y, z); } }
        }

        /// <summary>Does not alter geometry, object activity, renderer settings, or collision settings.</summary>
        public static BaekhoMapModule PrepareModule(GameObject root, Bounds? nominalBounds = null,
            string manifestAssetPath = null)
        {
            RequireEditMode();
            if (root == null) throw new ArgumentNullException(nameof(root));
            if ((root.transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
                throw new InvalidDataException("Dungeon module roots require unit scale: " + root.name);
            Manifest manifest = ReadManifest(manifestAssetPath);
            if (!nominalBounds.HasValue && manifest != null)
                nominalBounds = new Bounds(Vector3.zero,
                    new Vector3(manifest.width * manifest.cellSize, 0, manifest.height * manifest.cellSize));

            var sockets = root.GetComponentsInChildren<BaekhoMapConnection>(true);
            if (sockets.Length == 0) throw new InvalidDataException("No BaekhoMapConnection sockets on " + root.name);
            Array.Sort(sockets, (a, b) => string.CompareOrdinal(a.id, b.id));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var placeholderAssignments = new List<GameObject[]>();
            var closedWalls = new List<BaekhoClosedWall>();
            foreach (BaekhoMapConnection socket in sockets)
            {
                if (string.IsNullOrEmpty(socket.id) || !ids.Add(socket.id))
                    throw new InvalidDataException("Socket IDs must be nonempty and unique within " + root.name);
                Vector3 forward = root.transform.InverseTransformDirection(socket.transform.forward).normalized;
                Vector3 up = root.transform.InverseTransformDirection(socket.transform.up).normalized;
                if (Vector3.Dot(up, Vector3.up) < 0.9999f ||
                    Mathf.Max(Mathf.Abs(forward.x), Mathf.Abs(forward.z)) < 0.9999f)
                    throw new InvalidDataException("Socket must face a cardinal direction without tilt: " + socket.id);
                if (!Finite(socket.width) || !Finite(socket.height) || socket.width <= 0 || socket.height <= 0)
                    throw new InvalidDataException("Socket dimensions must be finite and positive: " + socket.id);
                placeholderAssignments.Add(FindPlaceholders(root, socket, manifest));
                closedWalls.Add(BaekhoClosedWallPreparation.Bake(root, socket, manifest != null ? manifest.cellSize : 0f));
            }

            Bounds measured = MeasureGeometryBounds(root, true);
            if (nominalBounds.HasValue)
            {
                measured.Encapsulate(nominalBounds.Value.min);
                measured.Encapsulate(nominalBounds.Value.max);
            }
            var existingModule = root.GetComponent<BaekhoMapModule>();
            var ground = new List<GameObject>();
            // Custom replacements can explicitly identify ground even when their object names are not importer roles.
            foreach (GameObject item in existingModule != null ? existingModule.groundObjects ?? Array.Empty<GameObject>() : Array.Empty<GameObject>())
            {
                if (item == null || !item.transform.IsChildOf(root.transform))
                    throw new InvalidDataException("Ground objects must belong to their module: " + root.name);
                if (!ground.Contains(item)) ground.Add(item);
            }
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == root.transform || !IsGroundName(child.name)) continue;
                bool nested = false;
                for (Transform parent = child.parent; parent != null && parent != root.transform; parent = parent.parent)
                    if (IsGroundName(parent.name)) { nested = true; break; }
                if (!nested && !ground.Contains(child.gameObject)) ground.Add(child.gameObject);
            }
            ground.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            // Apply only after all source geometry and associations have passed validation.
            var module = root.GetComponent<BaekhoMapModule>() ?? root.AddComponent<BaekhoMapModule>();
            module.localBounds = measured;
            module.connections = sockets;
            module.groundObjects = ground.ToArray();
            for (int i = 0; i < sockets.Length; i++)
            {
                sockets[i].placeholders = placeholderAssignments[i];
                sockets[i].closedWall = closedWalls[i];
                EditorUtility.SetDirty(sockets[i]);
                if (PrefabUtility.IsPartOfPrefabInstance(sockets[i]))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(sockets[i]);
            }
            EditorUtility.SetDirty(module);
            return module;
        }

        private static GameObject[] FindPlaceholders(GameObject root, BaekhoMapConnection socket, Manifest manifest)
        {
            Vector3 position = root.transform.InverseTransformPoint(socket.transform.position);
            Vector3 forward = root.transform.InverseTransformDirection(socket.transform.forward).normalized;
            var found = new List<GameObject>();
            if (manifest != null)
            {
                foreach (Placement item in manifest.placements ?? Array.Empty<Placement>())
                {
                    if (item.role != "door" || item.position == null || item.rotation == null) continue;
                    if ((item.position.Vector - position).sqrMagnitude > 0.000001f ||
                        Vector3.Dot(Quaternion.Euler(item.rotation.Vector) * Vector3.forward, forward) < 0.9999f) continue;
                    Transform door = root.transform.Find("door_" + item.id);
                    if (door == null) throw new InvalidDataException("Missing authored door placeholder: " + item.id);
                    found.Add(door.gameObject);
                }
            }
            else if (socket.placeholders != null && socket.placeholders.Length > 0)
            {
                foreach (GameObject placeholder in socket.placeholders)
                {
                    if (placeholder == null || !placeholder.transform.IsChildOf(root.transform) || placeholder == root)
                        throw new InvalidDataException("Placeholder must belong to its module: " + socket.id);
                    found.Add(placeholder);
                }
                return found.ToArray();
            }
            else
            {
                foreach (Transform child in root.transform)
                {
                    if (!child.name.StartsWith("door_", StringComparison.Ordinal)) continue;
                    Vector3 p = root.transform.InverseTransformPoint(child.position);
                    Vector3 f = root.transform.InverseTransformDirection(child.forward).normalized;
                    if (Mathf.Abs(p.x - position.x) > 0.001f || Mathf.Abs(p.z - position.z) > 0.001f ||
                        Vector3.Dot(f, forward) < 0.9999f) continue;
                    Bounds doorBounds = MeasureGeometryBounds(child.gameObject, true);
                    Vector3 bottom = root.transform.InverseTransformPoint(child.TransformPoint(
                        new Vector3(doorBounds.center.x, doorBounds.min.y, doorBounds.center.z)));
                    if (Mathf.Abs(bottom.y - position.y) <= 0.001f) found.Add(child.gameObject);
                }
            }
            if (found.Count > 1)
                throw new InvalidDataException("Ambiguous door placeholders for " + socket.id + "; assign explicit references.");
            return found.ToArray();
        }

        /// <summary>Bounds are in the root's unscaled local coordinate system, including pivot offsets.</summary>
        public static Bounds MeasureGeometryBounds(GameObject root, bool includeSolidColliders = false)
        {
            bool found = false;
            Bounds result = default(Bounds);
            Matrix4x4 inverse = root.transform.worldToLocalMatrix;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled || !ActiveWithinRoot(renderer.transform, root.transform)) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    AddBounds(ref result, ref found, filter.sharedMesh.bounds, inverse * renderer.transform.localToWorldMatrix);
            }
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.enabled && renderer.sharedMesh != null && ActiveWithinRoot(renderer.transform, root.transform))
                    AddBounds(ref result, ref found, renderer.localBounds, inverse * renderer.transform.localToWorldMatrix);
            if (includeSolidColliders || !found)
            {
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (!collider.enabled || collider.isTrigger || !ActiveWithinRoot(collider.transform, root.transform)) continue;
                    Bounds bounds;
                    if (collider is BoxCollider box) bounds = new Bounds(box.center, box.size);
                    else if (collider is MeshCollider mesh && mesh.sharedMesh != null) bounds = mesh.sharedMesh.bounds;
                    else if (collider is SphereCollider sphere) bounds = new Bounds(sphere.center, Vector3.one * sphere.radius * 2);
                    else if (collider is CapsuleCollider capsule)
                    {
                        Vector3 size = Vector3.one * capsule.radius * 2;
                        size[capsule.direction] = Mathf.Max(size[capsule.direction], capsule.height);
                        bounds = new Bounds(capsule.center, size);
                    }
                    else continue;
                    AddBounds(ref result, ref found, bounds, inverse * collider.transform.localToWorldMatrix);
                }
            }
            if (!found) throw new InvalidDataException("No active supported mesh or solid collider in " + root.name);
            return result;
        }

        private static void AddBounds(ref Bounds result, ref bool found, Bounds source, Matrix4x4 matrix)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(source.center + Vector3.Scale(source.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (!Finite(point.x) || !Finite(point.y) || !Finite(point.z))
                    throw new InvalidDataException("Non-finite geometry bounds.");
                if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                else result.Encapsulate(point);
            }
        }

        private static bool ActiveWithinRoot(Transform transform, Transform root)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == root) return true;
            }
            return false;
        }

        private static bool IsGroundName(string name)
        {
            return name.StartsWith("floor_", StringComparison.Ordinal) || name.StartsWith("stair_", StringComparison.Ordinal) ||
                name.StartsWith("bridge_", StringComparison.Ordinal) || name.StartsWith("slope_", StringComparison.Ordinal);
        }

        [MenuItem("Tools/Baekho/Dungeon/Create Demo Assets")]
        private static void CreateDemoFromMenu() { Debug.Log("Baekho dungeon demo: " + CreateDemoAssets()); }

        /// <summary>Creates missing demo assets and refreshes metadata on prepared copies; leaves original scenes and prefabs untouched.</summary>
        public static string CreateDemoAssets()
        {
            RequireEditMode();
            EnsureFolder(OutputFolder + "/Modules");
            var maps = new List<BaekhoMapModule>();
            foreach (string relative in SourceModules)
            {
                string source = BlockoutFolder + "/" + relative;
                string name = relative.Replace('/', '_');
                string destination = OutputFolder + "/Modules/" + name;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(source) == null)
                    throw new FileNotFoundException("Required authored map prefab was not found.", source);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(destination) == null && !AssetDatabase.CopyAsset(source, destination))
                    throw new IOException("Could not copy module to " + destination);
                PreparePrefab(destination, Path.GetDirectoryName(source).Replace('\\', '/') + "/Map.baekhomap.json");
                maps.Add(AssetDatabase.LoadAssetAtPath<GameObject>(destination).GetComponent<BaekhoMapModule>());
            }
            // Role prefabs have their own asset identity so ordinary-map pools remain available.
            BaekhoMapModule startRoom = PrepareRoleCopy(maps[5], "StartRoom_FlankingArena.prefab",
                BlockoutFolder + "/BaekhoMap5/Map.baekhomap.json");
            BaekhoMapModule endRoom = PrepareRoleCopy(maps[6], "EndRoom_Flanking_Pit_Arena.prefab",
                BlockoutFolder + "/BaekhoMap6/Map.baekhomap.json");

            string stylePath = OutputFolder + "/GreyboxCorridorStyle.asset";
            var style = AssetDatabase.LoadAssetAtPath<BaekhoCorridorStyle>(stylePath);
            if (style == null)
            {
                style = ScriptableObject.CreateInstance<BaekhoCorridorStyle>();
                style.floor.prefab = RequirePrefab(BlockoutFolder + "/Prefabs/Floor.prefab");
                style.wall.prefab = RequirePrefab(BlockoutFolder + "/Prefabs/Wall.prefab");
                style.cap.prefab = style.wall.prefab;
                PrepareStyle(style);
                AssetDatabase.CreateAsset(style, stylePath);
            }
            string cornerPath = OutputFolder + "/Modules/TEMP_CornerRoom_6x6.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(cornerPath) == null) CreateCornerRoom(cornerPath, style);
            var corner = RequirePrefab(cornerPath).GetComponent<BaekhoMapModule>();

            string settingsPath = OutputFolder + "/DungeonSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<BaekhoDungeonSettings>(settingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<BaekhoDungeonSettings>();
                settings.maps = maps.ToArray();
                settings.startRoom = startRoom;
                settings.endRoom = endRoom;
                settings.cornerRoom = corner;
                settings.style = style;
                AssetDatabase.CreateAsset(settings, settingsPath);
            }
            string generatorPath = OutputFolder + "/DungeonGenerator.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(generatorPath) == null) CreateGeneratorPrefab(generatorPath, settings);
            string scenePath = OutputFolder + "/DungeonDemo.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) CreateDemoScene(scenePath, generatorPath);
            AssetDatabase.SaveAssetIfDirty(style);
            AssetDatabase.SaveAssetIfDirty(settings);
            return scenePath;
        }

        private static BaekhoMapModule PrepareRoleCopy(BaekhoMapModule source, string fileName, string manifestPath)
        {
            string path = OutputFolder + "/Modules/" + fileName;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null &&
                !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source.gameObject), path))
                throw new IOException("Could not create role module: " + path);
            PreparePrefab(path, manifestPath);
            return RequirePrefab(path).GetComponent<BaekhoMapModule>();
        }

        [MenuItem("Assets/Baekho/Prepare Dungeon Module Copy", true)]
        private static bool CanPrepareSelected() { return Selection.activeObject is GameObject && EditorUtility.IsPersistent(Selection.activeObject); }

        [MenuItem("Assets/Baekho/Prepare Dungeon Module Copy")]
        private static void PrepareSelected() { CreateSelectedCopy(false); }

        [MenuItem("Assets/Baekho/Prepare Corner Room Copy", true)]
        private static bool CanPrepareCorner() { return CanPrepareSelected(); }

        [MenuItem("Assets/Baekho/Prepare Corner Room Copy")]
        private static void PrepareSelectedCorner() { CreateSelectedCopy(true); }

        private static void CreateSelectedCopy(bool corner)
        {
            RequireEditMode();
            string source = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!source.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Select a prefab asset.");
            EnsureFolder(OutputFolder + "/Modules");
            string path = AssetDatabase.GenerateUniqueAssetPath(OutputFolder + "/Modules/" + Path.GetFileName(source));
            if (!AssetDatabase.CopyAsset(source, path)) throw new IOException("Could not copy " + source);
            try { PreparePrefab(path, corner ? null : FindSiblingManifest(source), corner); }
            catch { AssetDatabase.DeleteAsset(path); throw; }
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        [MenuItem("Assets/Baekho/Measure Corridor Style Pieces", true)]
        private static bool CanMeasureStyle() { return Selection.activeObject is BaekhoCorridorStyle; }

        [MenuItem("Assets/Baekho/Measure Corridor Style Pieces")]
        private static void MeasureSelectedStyle()
        {
            RequireEditMode();
            var style = Selection.activeObject as BaekhoCorridorStyle;
            Undo.RecordObject(style, "Measure corridor style pieces");
            PrepareStyle(style);
            AssetDatabase.SaveAssetIfDirty(style);
        }

        public static void PrepareStyle(BaekhoCorridorStyle style)
        {
            if (style == null) throw new ArgumentNullException(nameof(style));
            foreach (BaekhoPrefabPiece piece in new[] { style.floor, style.wall, style.cap })
            {
                if (piece == null || piece.prefab == null) throw new InvalidDataException("Assign all three style prefabs before measuring.");
                piece.localBounds = MeasureGeometryBounds(piece.prefab);
                if (piece.localBounds.size.x <= 0 || piece.localBounds.size.y <= 0 || piece.localBounds.size.z <= 0)
                    throw new InvalidDataException("Corridor pieces need nonzero width, height and depth: " + piece.prefab.name);
            }
            EditorUtility.SetDirty(style);
        }

        private static string FindSiblingManifest(string prefabPath)
        {
            string path = Path.GetDirectoryName(prefabPath).Replace('\\', '/') + "/Map.baekhomap.json";
            if (!File.Exists(path)) return null;
            Manifest manifest = ReadManifest(path);
            return manifest.name == RequirePrefab(prefabPath).name ? path : null;
        }

        private static void PreparePrefab(string path, string manifestPath, bool corner = false)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                BaekhoMapModule module = PrepareModule(root, null, File.Exists(manifestPath) ? manifestPath : null);
                if (corner)
                {
                    if (module.connections.Length != 2)
                        throw new InvalidDataException("Corner rooms require exactly two sockets.");
                    BaekhoSocketData a = module.GetSocket(0), b = module.GetSocket(1);
                    if (Mathf.Abs(a.position.y - b.position.y) > 0.001f || Mathf.Abs(Vector3.Dot(a.forward, b.forward)) > 0.001f)
                        throw new InvalidDataException("Corner room sockets must be perpendicular and on the same floor height.");
                    module.isCornerRoom = true;
                    EditorUtility.SetDirty(module);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (!success) throw new IOException("Could not save prepared module: " + path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void CreateCornerRoom(string path, BaekhoCorridorStyle style)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = NewObject("TEMP Corner Room 6x6", scene);
                Material floorMaterial = style.floor.prefab.GetComponentInChildren<Renderer>().sharedMaterial;
                Material wallMaterial = style.wall.prefab.GetComponentInChildren<Renderer>().sharedMaterial;
                CreateBox(root, "floor_Temp", new Vector3(0, -0.1f, 0), new Vector3(6, 0.2f, 6), floorMaterial);
                CreateBox(root, "wall_West", new Vector3(-2.875f, 1.5f, 0), new Vector3(0.25f, 3, 6), wallMaterial);
                CreateBox(root, "wall_South", new Vector3(0, 1.5f, -2.875f), new Vector3(6, 3, 0.25f), wallMaterial);
                CreateBox(root, "wall_NorthLeft", new Vector3(-2, 1.5f, 2.875f), new Vector3(2, 3, 0.25f), wallMaterial);
                CreateBox(root, "wall_NorthRight", new Vector3(2, 1.5f, 2.875f), new Vector3(2, 3, 0.25f), wallMaterial);
                CreateBox(root, "wall_EastBottom", new Vector3(2.875f, 1.5f, -2), new Vector3(0.25f, 3, 2), wallMaterial);
                CreateBox(root, "wall_EastTop", new Vector3(2.875f, 1.5f, 2), new Vector3(0.25f, 3, 2), wallMaterial);
                CreateSocket(root, "North", new Vector3(0, 0, 3), 0);
                CreateSocket(root, "East", new Vector3(3, 0, 0), 90);
                PrepareModule(root, new Bounds(new Vector3(0, 1.4f, 0), new Vector3(6, 3.2f, 6))).isCornerRoom = true;
                SavePrefab(root, path);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void CreateGeneratorPrefab(string path, BaekhoDungeonSettings settings)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = NewObject("Baekho Dungeon Generator", scene);
                var generator = root.AddComponent<BaekhoDungeonGenerator>();
                generator.settings = settings;
                generator.generateOnStart = true;
                SavePrefab(root, path);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void CreateDemoScene(string path, string generatorPath)
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var generatorObject = (GameObject)PrefabUtility.InstantiatePrefab(RequirePrefab(generatorPath), scene);
                var generator = generatorObject.GetComponent<BaekhoDungeonGenerator>();
                var camera = NewObject("Dungeon Overview Camera", scene).AddComponent<Camera>();
                camera.gameObject.AddComponent<BaekhoDungeonOverviewCamera>().generator = generator;
                camera.transform.position = new Vector3(120, 180, -120);
                camera.transform.rotation = Quaternion.LookRotation(-camera.transform.position, Vector3.up);
                camera.orthographic = true;
                camera.orthographicSize = 140;
                camera.farClipPlane = 1000;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
                var light = NewObject("Dungeon Directional Light", scene).AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(55, -30, 0);
                light.intensity = 1.2f;
                if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Could not save demo scene: " + path);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static GameObject NewObject(string name, Scene scene)
        {
            var result = new GameObject(name);
            SceneManager.MoveGameObjectToScene(result, scene);
            return result;
        }

        private static void CreateBox(GameObject root, string name, Vector3 position, Vector3 size, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(box, root.scene);
            box.name = name;
            box.transform.SetParent(root.transform, false);
            box.transform.localPosition = position;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void CreateSocket(GameObject root, string id, Vector3 position, float yaw)
        {
            GameObject child = NewObject(id, root.scene);
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = position;
            child.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var socket = child.AddComponent<BaekhoMapConnection>();
            socket.id = id;
            socket.label = id;
            socket.kind = "door";
            socket.width = 2;
            socket.height = 3;
        }

        private static void SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success) throw new IOException("Could not save prefab: " + path);
        }

        private static Manifest ReadManifest(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!File.Exists(path)) throw new FileNotFoundException("Map manifest was not found.", path);
            Manifest result = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            if (result == null || result.width < 1 || result.height < 1 || !Finite(result.cellSize) || result.cellSize <= 0)
                throw new InvalidDataException("Invalid nominal map dimensions in " + path);
            return result;
        }

        private static GameObject RequirePrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new FileNotFoundException("Required prefab was not found.", path);
            return prefab;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before preparing dungeon assets.");
        }
    }
}
