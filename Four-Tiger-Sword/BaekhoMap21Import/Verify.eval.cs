var path = System.IO.File.ReadAllText("BaekhoMap21Import/generated-prefab-path.txt").Trim();
var root = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
if (root == null) throw new System.Exception("Prefab failed to reload");
var map = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Assets/_Game/Greybox/Blockout/BaekhoMap21/Map.baekhomap.json"));
var placements = (Newtonsoft.Json.Linq.JArray)map["placements"];
var placementFailures = placements.Where(p => root.transform.Find((string)p["role"] + "_" + (string)p["id"]) == null).Select(p => (string)p["id"]).ToArray();
var transforms = root.GetComponentsInChildren<UnityEngine.Transform>(true);
var missingScripts = transforms.Sum(t => UnityEditor.GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
var renderers = root.GetComponentsInChildren<UnityEngine.Renderer>(true);
var missingMaterials = renderers.Where(r => r.sharedMaterials.Any(m => m == null)).Select(r => r.name).ToArray();
var missingMeshes = root.GetComponentsInChildren<UnityEngine.MeshFilter>(true).Where(m => m.sharedMesh == null).Select(m => m.name).ToArray();
var module = root.GetComponent<StudioBaekho.BaekhoMapModule>();
var connections = root.GetComponentsInChildren<StudioBaekho.BaekhoMapConnection>(true);
var markers = root.GetComponentsInChildren<StudioBaekho.BaekhoGameplayMarker>(true);
var surface = root.GetComponent<Unity.AI.Navigation.NavMeshSurface>();
var navPath = surface == null ? "" : UnityEditor.AssetDatabase.GetAssetPath(surface.navMeshData);
if (surface == null || surface.navMeshData == null || string.IsNullOrEmpty(navPath)) throw new System.Exception("Missing persisted NavMesh");
var navSerialized = new UnityEditor.SerializedObject(surface.navMeshData);
var navTiles = navSerialized.FindProperty("m_NavMeshTiles");
var tiles = navTiles != null && navTiles.isArray ? navTiles.arraySize : -1;
var connectionReport = connections.Select(c => new { c.id, c.label, c.kind, c.width, c.height, position = c.transform.localPosition.ToString("F3"), placeholderCount = c.placeholders.Length, closedWall = c.closedWall != null && c.closedWall.template != null }).ToArray();
var report = new {
    prefabPath = path, navMeshPath = navPath, placements = placements.Count, rootChildren = root.transform.childCount,
    missingPlacements = placementFailures, missingScripts, missingMaterials, missingMeshes,
    rootPosition = root.transform.localPosition.ToString(), rootRotation = root.transform.localRotation.eulerAngles.ToString(), rootScale = root.transform.localScale.ToString(),
    module = module != null, moduleConnections = module == null ? -1 : module.connections.Length,
    groundObjects = module == null ? -1 : module.groundObjects.Length,
    connections = connectionReport, markerCount = markers.Length, markerKinds = markers.Select(m => m.kind).ToArray(),
    renderers = renderers.Length, colliders = root.GetComponentsInChildren<UnityEngine.Collider>(true).Length,
    navMeshTiles = tiles, navMeshBytes = new System.IO.FileInfo(navPath).Length,
    navMeshAgentType = surface.agentTypeID, collectObjects = surface.collectObjects.ToString(), geometry = surface.useGeometry.ToString(), layerMask = surface.layerMask.value,
    scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path, sceneDirty = UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty
};
System.IO.File.WriteAllText("BaekhoMap21Import/verification.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
if (placementFailures.Length != 0 || missingScripts != 0 || missingMaterials.Length != 0 || missingMeshes.Length != 0 || module == null || module.connections.Length != 4 || connections.Length != 4 || markers.Length != 8 || root.transform.localScale != UnityEngine.Vector3.one || tiles <= 0) throw new System.Exception("Map verification failed; see verification.json");
return report;


