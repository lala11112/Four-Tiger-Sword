var project = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath).Replace("\\", "/");
if (project != "C:/Users/Yoon Youngseok/Documents/GitHub/Four-Tiger-Sword/Four-Tiger-Sword")
    throw new System.Exception("Unexpected Unity project.");
if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || UnityEditor.EditorApplication.isCompiling)
    throw new System.Exception("Editor must be idle in Edit Mode.");
var reportDir = project + "/BaekhoPipeCable2xRegistration";
var paths = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] {"Assets/PipeCableKit2x", "Assets/PipeCableTeeKit2x"})
    .Select(UnityEditor.AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
if (paths.Length != 24) throw new System.Exception("Expected exactly 24 original prefabs.");
var specs = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(project + "/Assets/PipeCableKit2x/Documentation/asset_manifest.json"))["assets"];
var export = typeof(StudioBaekho.BaekhoPropCatalog).GetMethod("ExportWallModule", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var roots = new System.Collections.Generic.List<UnityEngine.GameObject>();
var destinations = new System.Collections.Generic.List<string>();
var capMeshes = new System.Collections.Generic.List<UnityEngine.Mesh>();
var capMeshPaths = new System.Collections.Generic.List<string>();
var reports = new System.Collections.Generic.List<object>();
var scenesBefore = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i => {
    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
    return scene.path + "|" + scene.isDirty + "|" + scene.rootCount;
}).ToArray();
System.Func<UnityEngine.Vector3, float[]> vector = v => new[] {v.x, v.y, v.z};
System.Action<UnityEngine.GameObject, StudioBaekho.BaekhoWallModule, string> common = (root, module, family) => {
    module.family = family;
    module.sites = new[] {StudioBaekho.BaekhoWallModule.InstallSite.Wall};
    module.heightMode = StudioBaekho.BaekhoWallModule.HeightMode.Top;
    module.height = .35f;
    module.spacing = 3f;
    module.probability = 100f;
    module.doorSide = StudioBaekho.BaekhoWallModule.DoorSide.Both;
    var marker = root.GetComponent<StudioBaekho.BaekhoMapAsset>() ?? root.AddComponent<StudioBaekho.BaekhoMapAsset>();
    marker.importEnabled = true;
    marker.role = StudioBaekho.BaekhoMapAsset.AssetRole.Decoration;
};
try
{
    foreach (var path in paths)
    {
        var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
        roots.Add(root); destinations.Add(path);
        if (root.GetComponent<StudioBaekho.BaekhoWallModule>() != null)
            throw new System.Exception("Unexpected pre-existing WallModule: " + path);
        var pieces = root.name.Split('_');
        var family = "PipeCableKit2x_" + pieces[0] + "_" + pieces[1];
        float radius = int.Parse(pieces[1].Substring(1)) / 2000f;
        bool elbow = root.name.Contains("_Elbow_"), tee = root.name.Contains("_Tee_");
        var module = root.AddComponent<StudioBaekho.BaekhoWallModule>();
        common(root, module, family);
        module.kind = tee ? StudioBaekho.BaekhoWallModule.ModuleKind.Tee :
            elbow ? StudioBaekho.BaekhoWallModule.ModuleKind.Elbow : StudioBaekho.BaekhoWallModule.ModuleKind.Straight;
        var mount = new UnityEngine.GameObject("BaekhoMount").transform;
        mount.SetParent(root.transform, false);
        mount.localPosition = new UnityEngine.Vector3(0, 0, -radius - .02f);
        mount.localRotation = UnityEngine.Quaternion.identity;
        module.mounts = new[] {mount};
        module.sockets = (tee ? new[] {"Left", "Right", "Top"} : new[] {"Start", "End"}).Select(n => root.transform.Find(n)).ToArray();
        if (module.sockets.Any(s => s == null)) throw new System.Exception("Missing original socket: " + path);
        var filter = root.GetComponentInChildren<UnityEngine.MeshFilter>();
        var matrix = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
        var vertices = filter.sharedMesh.vertices.Select(v => matrix.MultiplyPoint3x4(v)).ToArray();
        var bounds = new UnityEngine.Bounds(vertices[0], UnityEngine.Vector3.zero);
        foreach (var v in vertices) bounds.Encapsulate(v);
        if (elbow)
        {
            var spec = specs.First(s => (string)s["name"] == root.name);
            float bend = (float)spec["bend_radius_m"], cx = 1f - bend, cy = bend;
            var boxes = new UnityEngine.Bounds[18];
            var populated = new bool[18];
            var triangles = filter.sharedMesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]];
                var mid = (a + b + c) / 3f;
                int group = mid.x <= cx ? 0 : mid.y >= cy ? 17 :
                    1 + UnityEngine.Mathf.Clamp((int)(UnityEngine.Mathf.Atan2(mid.x - cx, cy - mid.y) * 32f / UnityEngine.Mathf.PI), 0, 15);
                if (!populated[group]) { boxes[group] = new UnityEngine.Bounds(a, UnityEngine.Vector3.zero); populated[group] = true; }
                boxes[group].Encapsulate(a); boxes[group].Encapsulate(b); boxes[group].Encapsulate(c);
            }
            if (populated.Any(p => !p)) throw new System.Exception("Empty curve segment: " + path);
            module.collisionBoxes = boxes;
        }
        else if (tee)
        {
            float z = bounds.center.z, depth = bounds.size.z;
            module.collisionBoxes = new[] {
                new UnityEngine.Bounds(new UnityEngine.Vector3(-.5f, 0, z), new UnityEngine.Vector3(1, 2*radius, depth)),
                new UnityEngine.Bounds(new UnityEngine.Vector3(.5f, 0, z), new UnityEngine.Vector3(1, 2*radius, depth)),
                new UnityEngine.Bounds(new UnityEngine.Vector3(0, .5f, z), new UnityEngine.Vector3(2*radius, 1, depth))
            };
            // The source T has a small rounded fillet outside the ideal three cylinders.
            var excess = vertices.Where(v => !module.collisionBoxes.Any(b => {
                b.Expand(.00002f); return b.Contains(v);
            })).ToArray();
            if (excess.Length > 0)
            {
                var hub = new UnityEngine.Bounds(new UnityEngine.Vector3(0, 0, z), UnityEngine.Vector3.zero);
                foreach (var v in excess) hub.Encapsulate(v);
                var size = hub.size; size.z = depth;
                var center = hub.center; center.z = z;
                module.collisionBoxes = module.collisionBoxes.Concat(new[] {new UnityEngine.Bounds(center, size)}).ToArray();
            }
        }
        else module.collisionBoxes = new[] {bounds};
        var testBoxes = module.collisionBoxes.Select(b => { b.Expand(.00002f); return b; }).ToArray();
        if (vertices.Any(v => !testBoxes.Any(b => b.Contains(v)))) throw new System.Exception("Geometry outside occupancy boxes: " + path);
        export.Invoke(null, new object[] {module, bounds, path});
        reports.Add(new {path, family, kind = module.kind.ToString(), sockets = module.sockets.Length,
            boxes = module.collisionBoxes.Length, mount = vector(mount.localPosition), vertices = vertices.Length});
    }
    foreach (var sourceRoot in roots.ToArray().Where(r => r.name.Contains("_Straight_")))
    {
        var parts = sourceRoot.name.Split('_');
        var oldDiameter = (int.Parse(parts[1].Substring(1)) / 2).ToString("D3");
        var oldPath = "Assets/Baekho/PipeCableKitWall/Prefabs/Wall_" + parts[0] + "_D" + oldDiameter + "_EndCap.prefab";
        var newName = parts[0] + "_" + parts[1] + "_EndCap";
        var destination = "Assets/PipeCableKit2x/Prefabs/" + newName + ".prefab";
        if (System.IO.File.Exists(project + "/" + destination)) throw new System.Exception("Cap already exists: " + destination);
        var root = UnityEditor.PrefabUtility.LoadPrefabContents(oldPath);
        roots.Add(root); destinations.Add(destination);
        root.name = newName;
        var module = root.GetComponent<StudioBaekho.BaekhoWallModule>();
        common(root, module, sourceRoot.GetComponent<StudioBaekho.BaekhoWallModule>().family);
        module.kind = StudioBaekho.BaekhoWallModule.ModuleKind.EndCap;
        module.mounts = new UnityEngine.Transform[0];
        module.collisionBoxes = module.collisionBoxes.Select(b => new UnityEngine.Bounds(b.center * 2, b.size * 2)).ToArray();
        var filter = root.GetComponent<UnityEngine.MeshFilter>();
        var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
        mesh.name = module.family + "_EndCap";
        mesh.vertices = mesh.vertices.Select(v => v * 2).ToArray();
        mesh.RecalculateBounds();
        filter.sharedMesh = mesh;
        var material = sourceRoot.GetComponentInChildren<UnityEngine.MeshRenderer>().sharedMaterials[0];
        root.GetComponent<UnityEngine.MeshRenderer>().sharedMaterials = Enumerable.Repeat(material, mesh.subMeshCount).ToArray();
        capMeshes.Add(mesh);
        capMeshPaths.Add("Assets/PipeCableKit2x/BaekhoWallMeshes/" + mesh.name + ".asset");
        export.Invoke(null, new object[] {module, mesh.bounds, destination});
        reports.Add(new {path = destination, family = module.family, kind = "EndCap", sockets = module.sockets.Length, boxes = module.collisionBoxes.Length});
    }
    if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/PipeCableKit2x/BaekhoWallMeshes"))
        UnityEditor.AssetDatabase.CreateFolder("Assets/PipeCableKit2x", "BaekhoWallMeshes");
    for (int i = 0; i < capMeshes.Count; i++) UnityEditor.AssetDatabase.CreateAsset(capMeshes[i], capMeshPaths[i]);
    for (int i = 0; i < roots.Count; i++)
    {
        bool saved;
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(roots[i], destinations[i], out saved);
        if (!saved) throw new System.Exception("Prefab save failed: " + destinations[i]);
    }
}
finally
{
    foreach (var root in roots) if (root != null) UnityEditor.PrefabUtility.UnloadPrefabContents(root);
}
var scenesAfter = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i => {
    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
    return scene.path + "|" + scene.isDirty + "|" + scene.rootCount;
}).ToArray();
if (!scenesBefore.SequenceEqual(scenesAfter)) throw new System.Exception("Scene state changed unexpectedly.");
var catalogPath = reportDir + "/PipeCableKit2x-wall-catalog.json";
int exported = StudioBaekho.BaekhoPropCatalog.ExportToPath(catalogPath, destinations.Select(UnityEditor.AssetDatabase.AssetPathToGUID).ToArray());
System.IO.File.WriteAllText(reportDir + "/authoring-report.json",
    Newtonsoft.Json.JsonConvert.SerializeObject(new {originals = paths.Length, addedCaps = capMeshes.Count, exported, scenesBefore, scenesAfter, prefabs = reports}, Newtonsoft.Json.Formatting.Indented));
return new {configured = paths.Length, addedCaps = capMeshes.Count, exported, catalogPath, scenePreserved = true};
