using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StudioBaekho;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class ValidateMap16
{
    public static object Main()
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../BaekhoMap16Import"));
        string prefabPath = File.ReadAllText(Path.Combine(folder, "prefab-path.txt"));
        JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(folder, "Map.baekhomap.json")));
        var errors = new List<string>();
        var report = new Dictionary<string, object>();
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var placements = (JArray)manifest["placements"];
            var sockets = root.GetComponentsInChildren<BaekhoMapConnection>(true);
            var markers = root.GetComponentsInChildren<BaekhoGameplayMarker>(true);
            var fixtures = root.GetComponentsInChildren<BaekhoMapFixture>(true);
            var module = root.GetComponent<BaekhoMapModule>();
            var surface = root.GetComponent<NavMeshSurface>();
            if (root.name != (string)manifest["name"] || root.transform.localScale != Vector3.one)
                errors.Add("Root name/scale mismatch.");
            if (root.transform.childCount != placements.Count + 2) errors.Add("Placement root count mismatch.");
            if (sockets.Length != ((JArray)manifest["connections"]).Count) errors.Add("Connection count mismatch.");
            if (markers.Length != ((JArray)manifest["markers"]).Count) errors.Add("Marker count mismatch.");
            foreach (JObject placement in placements)
            {
                string name = (string)placement["role"] + "_" + (string)placement["id"];
                var child = root.transform.Find(name);
                if (child == null) { errors.Add("Missing placement: " + name); continue; }
                string expectedPath = AssetDatabase.GUIDToAssetPath((string)placement["prefabGuid"]);
                string actualPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
                if (actualPath != expectedPath) errors.Add("Source prefab mismatch: " + name);
            }
            foreach (JObject connection in (JArray)manifest["connections"])
            {
                var socket = sockets.SingleOrDefault(s => s.id == (string)connection["id"]);
                if (socket == null) { errors.Add("Missing socket metadata."); continue; }
                if (Mathf.Abs(socket.width - (float)connection["width"]) > .001f ||
                    Mathf.Abs(socket.height - (float)connection["height"]) > .001f)
                    errors.Add("Socket dimensions mismatch: " + socket.id);
                if (socket.placeholders == null || socket.placeholders.Length != 1 || socket.placeholders[0] == null)
                    errors.Add("Invalid doorway placeholder: " + socket.id);
                if (socket.closedWall == null || socket.closedWall.template == null)
                    errors.Add("Missing closed wall template: " + socket.id);
            }
            foreach (JObject marker in (JArray)manifest["markers"])
            {
                var actual = markers.SingleOrDefault(m => m.id == (string)marker["id"]);
                if (actual == null || actual.kind != (string)marker["kind"] || actual.contentKey != (string)marker["contentKey"])
                    errors.Add("Marker metadata mismatch: " + (string)marker["id"]);
            }
            int expectedGround = placements.Count(p => new[] { "floor", "stair", "slope", "bridge" }.Contains((string)p["role"]));
            if (module == null || module.connections.Length != sockets.Length || module.groundObjects.Length != expectedGround)
                errors.Add("Dungeon module metadata mismatch.");
            int missingScripts = root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            int missingMaterials = root.GetComponentsInChildren<Renderer>(true).Sum(r => r.sharedMaterials.Count(m => m == null));
            if (missingScripts != 0 || missingMaterials != 0) errors.Add("Missing script or material references.");
            string navMeshPath = surface != null && surface.navMeshData != null ? AssetDatabase.GetAssetPath(surface.navMeshData) : "";
            int triangleCount = 0;
            if (string.IsNullOrEmpty(navMeshPath)) errors.Add("Missing persisted NavMesh data.");
            else
            {
                var instance = NavMesh.AddNavMeshData(surface.navMeshData, new Vector3(10000, 0, 10000), Quaternion.identity);
                try
                {
                    var triangulation = NavMesh.CalculateTriangulation();
                    for (int index = 0; index < triangulation.indices.Length; index += 3)
                    {
                        var vertex = triangulation.vertices[triangulation.indices[index]];
                        if (vertex.x > 9900 && vertex.z > 9900) triangleCount++;
                    }
                    if (!instance.valid || triangleCount == 0) errors.Add("NavMesh has no walkable triangles.");
                }
                finally { if (instance.valid) instance.Remove(); }
            }
            report["name"] = root.name;
            report["prefabPath"] = prefabPath;
            report["navMeshPath"] = navMeshPath;
            report["placements"] = placements.Count;
            report["connections"] = sockets.Length;
            report["markers"] = markers.Length;
            report["fixtures"] = fixtures.Length;
            report["groundObjects"] = module != null ? module.groundObjects.Length : 0;
            report["navMeshTriangles"] = triangleCount;
            report["missingScripts"] = missingScripts;
            report["missingMaterials"] = missingMaterials;

            // Render a disposable copy with the ceiling hidden to inspect the interior.
            var preview = new PreviewRenderUtility();
            Texture2D image = null;
            try
            {
                var display = UnityEngine.Object.Instantiate(root);
                preview.AddSingleGO(display);
                foreach (Transform child in display.transform)
                    if (child.name.StartsWith("ceiling_", StringComparison.Ordinal)) child.gameObject.SetActive(false);
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = 32f;
                preview.camera.nearClipPlane = .1f;
                preview.camera.farClipPlane = 250f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.10f, .12f, .15f, 1);
                preview.camera.transform.position = new Vector3(55, 70, -60);
                preview.camera.transform.LookAt(new Vector3(0, 1, 0));
                preview.lights[0].intensity = 1.4f;
                preview.lights[0].transform.rotation = Quaternion.Euler(45, 30, 0);
                preview.lights[1].intensity = .8f;
                preview.ambientColor = new Color(.6f, .6f, .6f, 1);
                preview.BeginPreview(new Rect(0, 0, 1200, 1000), GUIStyle.none);
                preview.Render(true);
                var rendered = preview.EndPreview() as RenderTexture;
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = rendered;
                    image = new Texture2D(rendered.width, rendered.height, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, rendered.width, rendered.height), 0, 0);
                    image.Apply();
                }
                finally { RenderTexture.active = previous; }
                string previewPath = Path.Combine(folder, "Map16-preview.png");
                File.WriteAllBytes(previewPath, image.EncodeToPNG());
                report["previewPath"] = previewPath;
            }
            finally
            {
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                preview.Cleanup();
            }
            report["errors"] = errors;
            report["passed"] = errors.Count == 0;
            File.WriteAllText(Path.Combine(folder, "validation.json"), JsonConvert.SerializeObject(report, Formatting.Indented));
            return report;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
