#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Renders disposable cutaway instances; never saves a scene or prefab.</summary>
public static class MapRefreshPreviews
{
    [Serializable] public sealed class Report
    {
        public bool passed;
        public List<Preview> previews = new List<Preview>();
        public List<string> errors = new List<string>();
    }
    [Serializable] public sealed class Preview
    {
        public string category, source, derived, beforeImage, afterImage;
    }

    public static string Render()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Render room comparisons in Edit Mode.");
        var manifest = MapRefreshValidation.ReadManifest();
        var ordinary = manifest.entries.Where(e => e.role == "normal").ToArray();
        var examples = new[]
        {
            new KeyValuePair<string, MapRefreshValidation.Entry>("01_Small", ordinary.OrderBy(Area).First()),
            new KeyValuePair<string, MapRefreshValidation.Entry>("02_Large", ordinary.OrderByDescending(Area).First()),
            new KeyValuePair<string, MapRefreshValidation.Entry>("03_Multilevel", ordinary.OrderByDescending(ElevationRange).First()),
            new KeyValuePair<string, MapRefreshValidation.Entry>("04_Shop", manifest.entries.First(e => e.role == "shop")),
            new KeyValuePair<string, MapRefreshValidation.Entry>("05_Boss", manifest.entries.First(e => e.role == "boss"))
        };
        string directory = Path.Combine(MapRefreshValidation.VerificationRoot, "Previews");
        Directory.CreateDirectory(directory);
        var report = new Report();
        foreach (var example in examples)
        {
            var entry = example.Value;
            var preview = new Preview { category = example.Key, source = entry.sourcePath, derived = entry.prefabPath,
                beforeImage = Path.Combine(directory, example.Key + "_Before.png"), afterImage = Path.Combine(directory, example.Key + "_After.png") };
            try
            {
                RenderOne(entry.sourcePath, preview.beforeImage);
                RenderOne(entry.prefabPath, preview.afterImage);
                report.previews.Add(preview);
            }
            catch (Exception exception) { report.errors.Add(example.Key + ": " + exception.Message); }
        }
        report.passed = report.errors.Count == 0 && report.previews.Count == 5;
        return JsonUtility.ToJson(report, true);
    }

    public static string RenderOne(string prefabPath, string outputPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (!prefab) throw new FileNotFoundException(prefabPath);
        var scene = EditorSceneManager.NewPreviewScene();
        var temporaryMeshes = new List<Mesh>();
        RenderTexture target = null;
        Texture2D pixels = null;
        Camera camera = null;
        var previous = RenderTexture.active;
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            foreach (var surface in instance.GetComponentsInChildren<Unity.AI.Navigation.NavMeshSurface>(true))
            { surface.RemoveData(); surface.enabled = false; }
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var light in instance.GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (var storedCamera in instance.GetComponentsInChildren<Camera>(true)) storedCamera.enabled = false;
            var module = instance.GetComponent<BaekhoMapModule>();
            if (module)
                foreach (var socket in module.connections ?? Array.Empty<BaekhoMapConnection>())
                    if (socket)
                        foreach (var placeholder in socket.placeholders ?? Array.Empty<GameObject>())
                            if (placeholder && placeholder != instance && placeholder.transform.IsChildOf(instance.transform))
                                placeholder.SetActive(false);
            var bounds = module ? module.localBounds : BaekhoDungeonSetup.MeasureGeometryBounds(instance, true);
            if (module) Cutaway(instance.transform, bounds, temporaryMeshes);
            foreach (var door in instance.GetComponentsInChildren<LabDoor.SlidingDoor>(true)) door.SetOpenImmediate(true);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            { renderer.lightmapIndex = -1; renderer.realtimeLightmapIndex = -1; }

            var cameraObject = new GameObject("Map refresh inspection camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.055f, .069f, .085f);
            camera.orthographic = true;
            camera.allowHDR = true;
            camera.nearClipPlane = .1f;
            float span = Mathf.Max(bounds.size.x, bounds.size.z, 8f);
            camera.farClipPlane = span * 8 + 256;
            camera.orthographicSize = span * .54f + bounds.size.y * .12f;
            var aim = new Vector3(bounds.center.x, bounds.min.y + Mathf.Min(bounds.size.y * .23f, 3.2f), bounds.center.z);
            camera.transform.position = aim + new Vector3(-span, span * 1.3f, -span * 1.15f);
            camera.transform.LookAt(aim);
            var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            AddLight(scene, "Inspection key", Quaternion.Euler(48, -35, 0), 2f, new Color(1f, .96f, .89f));
            AddLight(scene, "Inspection fill", Quaternion.Euler(55, 145, 0), .85f, new Color(.72f, .84f, 1f));
            target = new RenderTexture(1600, 1100, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            string absolute = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllBytes(absolute, pixels.EncodeToPNG());
            return absolute;
        }
        finally
        {
            if (camera) camera.targetTexture = null;
            RenderTexture.active = previous;
            if (target) { target.Release(); Object.DestroyImmediate(target); }
            if (pixels) Object.DestroyImmediate(pixels);
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var mesh in temporaryMeshes) if (mesh) Object.DestroyImmediate(mesh);
        }
    }

    static void Cutaway(Transform root, Bounds bounds, List<Mesh> temporaryMeshes)
    {
        float frontX = bounds.min.x + bounds.size.x * .15f;
        float frontZ = bounds.min.z + bounds.size.z * .15f;
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var names = new List<string>();
            for (var at = renderer.transform; at && at != root; at = at.parent) names.Add(at.name.ToLowerInvariant());
            if (names.Any(n => n.Contains("ceiling") || n.Contains("roof"))) { renderer.enabled = false; continue; }
            if (!names.Any(n => n.Contains("wall") || n.StartsWith("cliff"))) continue;
            var filter = renderer.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh) continue;
            bool spansRoom = renderer.bounds.size.x > bounds.size.x * .6f || renderer.bounds.size.z > bounds.size.z * .6f;
            if (!spansRoom)
            {
                if (renderer.bounds.center.x < frontX || renderer.bounds.center.z < frontZ) renderer.enabled = false;
                continue;
            }
            // Batched wall visuals are clipped on a temporary mesh, retaining the far shell.
            var mesh = Object.Instantiate(filter.sharedMesh);
            temporaryMeshes.Add(mesh);
            var vertices = mesh.vertices;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                var indices = mesh.GetTriangles(sub);
                var kept = new List<int>(indices.Length);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    var center = root.InverseTransformPoint(filter.transform.TransformPoint((vertices[indices[i]] + vertices[indices[i + 1]] + vertices[indices[i + 2]]) / 3f));
                    if ((center.x < frontX || center.z < frontZ) && center.y > bounds.min.y + .5f) continue;
                    kept.Add(indices[i]); kept.Add(indices[i + 1]); kept.Add(indices[i + 2]);
                }
                mesh.SetTriangles(kept, sub);
            }
            filter.sharedMesh = mesh;
        }
    }

    static void AddLight(Scene scene, string name, Quaternion rotation, float intensity, Color color)
    {
        var root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.rotation = rotation;
        var light = root.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
        light.shadows = LightShadows.Soft;
    }
    static BaekhoMapModule Module(MapRefreshValidation.Entry entry)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(entry.sourcePath);
        return source ? source.GetComponent<BaekhoMapModule>() : null;
    }
    static float Area(MapRefreshValidation.Entry entry)
    {
        var module = Module(entry);
        return module ? module.localBounds.size.x * module.localBounds.size.z : 0;
    }
    static float ElevationRange(MapRefreshValidation.Entry entry)
    {
        var module = Module(entry);
        if (!module) return 0;
        var heights = (module.groundObjects ?? Array.Empty<GameObject>()).Where(g => g)
            .SelectMany(g => g.GetComponentsInChildren<Collider>(true)).Where(c => c.enabled && !c.isTrigger)
            .Select(c => module.transform.InverseTransformPoint(c.bounds.center).y).ToArray();
        return heights.Length > 0 ? heights.Max() - heights.Min() : 0;
    }
}
#endif
