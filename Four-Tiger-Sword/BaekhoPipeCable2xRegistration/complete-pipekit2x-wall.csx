var project = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath).Replace("\\", "/");
if (project != "C:/Users/Yoon Youngseok/Documents/GitHub/Four-Tiger-Sword/Four-Tiger-Sword")
    throw new System.Exception("Unexpected Unity project.");
var export = typeof(StudioBaekho.BaekhoPropCatalog).GetMethod("ExportWallModule", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var teePaths = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] {"Assets/PipeCableTeeKit2x"}).Select(UnityEditor.AssetDatabase.GUIDToAssetPath).ToArray();
foreach (var path in teePaths)
{
    var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
    try
    {
        var module = root.GetComponent<StudioBaekho.BaekhoWallModule>();
        float radius = int.Parse(root.name.Split('_')[1].Substring(1)) / 2000f;
        var filter = root.GetComponentInChildren<UnityEngine.MeshFilter>();
        var matrix = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
        var vertices = filter.sharedMesh.vertices.Select(v => matrix.MultiplyPoint3x4(v)).ToArray();
        var triangles = filter.sharedMesh.triangles;
        var hub = new UnityEngine.Bounds(UnityEngine.Vector3.zero, UnityEngine.Vector3.zero);
        // Cover complete triangles that can enter either concave quadrant of the T.
        // This includes the rounded weld and preserves both empty spaces between arms.
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var a = vertices[triangles[i]]; var b = vertices[triangles[i+1]]; var c = vertices[triangles[i+2]];
            float maxY = UnityEngine.Mathf.Max(a.y, b.y, c.y);
            float maxX = UnityEngine.Mathf.Max(a.x, b.x, c.x);
            float minX = UnityEngine.Mathf.Min(a.x, b.x, c.x);
            if (maxY > radius + .000001f && (maxX > radius + .000001f || minX < -radius - .000001f))
            { hub.Encapsulate(a); hub.Encapsulate(b); hub.Encapsulate(c); }
        }
        var originalBox = module.collisionBoxes[0];
        var center = hub.center; center.z = originalBox.center.z;
        var size = hub.size; size.z = originalBox.size.z;
        module.collisionBoxes = module.collisionBoxes.Take(3).Concat(new[] {new UnityEngine.Bounds(center, size)}).ToArray();
        export.Invoke(null, new object[] {module, hub, path});
        bool saved;
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path, out saved);
        if (!saved) throw new System.Exception("Save failed: " + path);
    }
    finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}
var straightPaths = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] {"Assets/PipeCableKit2x"})
    .Select(UnityEditor.AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith("_Straight_1m.prefab")).OrderBy(p => p).ToArray();
foreach (var path in straightPaths)
{
    var destination = path.Replace("_Straight_1m.prefab", "_Straight_2m.prefab");
    if (System.IO.File.Exists(project + "/" + destination)) throw new System.Exception("Destination already exists: " + destination);
    var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
    try
    {
        root.name = root.name.Replace("_Straight_1m", "_Straight_2m");
        var originalModel = root.transform.Find("Model");
        var secondModel = UnityEngine.Object.Instantiate(originalModel.gameObject, root.transform);
        secondModel.name = "Model_SecondMetre";
        secondModel.transform.localPosition = originalModel.localPosition + UnityEngine.Vector3.right;
        root.transform.Find("End").localPosition = new UnityEngine.Vector3(2, 0, 0);
        var module = root.GetComponent<StudioBaekho.BaekhoWallModule>();
        module.collisionBoxes = module.collisionBoxes.Concat(module.collisionBoxes
            .Select(b => new UnityEngine.Bounds(b.center + UnityEngine.Vector3.right, b.size))).ToArray();
        var bounds = module.collisionBoxes[0];
        foreach (var box in module.collisionBoxes) bounds.Encapsulate(box);
        export.Invoke(null, new object[] {module, bounds, destination});
        bool saved;
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, destination, out saved);
        if (!saved) throw new System.Exception("Save failed: " + destination);
    }
    finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}
var allPaths = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] {"Assets/PipeCableKit2x", "Assets/PipeCableTeeKit2x"})
    .Select(UnityEditor.AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
var catalogPath = project + "/BaekhoPipeCable2xRegistration/PipeCableKit2x-wall-catalog.json";
int exported = StudioBaekho.BaekhoPropCatalog.ExportToPath(catalogPath, allPaths.Select(UnityEditor.AssetDatabase.AssetPathToGUID).ToArray());
if (exported != 40) throw new System.Exception("Expected 40 exported prefabs.");
return new {updatedTees = teePaths.Length, addedTwoMetreStraights = straightPaths.Length, exported, catalogPath};
