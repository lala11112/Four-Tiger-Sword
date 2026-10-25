// Copy into Assets/Baekho/Editor. Reads original prefab bounds without modifying source assets.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StudioBaekho
{
    public static class BaekhoPropCatalog
    {
        [Serializable] private sealed class Catalog { public int version = 1; public Entry[] assets; }
        [Serializable] private sealed class Entry { public string guid; public Vector3 size; }

        [MenuItem("Assets/Baekho/Export Prop Bounds Catalog")]
        private static void ExportSelected()
        {
            try
            {
                string[] guids = ResolveSelectedPrefabGuids(Selection.objects);
                string output = EditorUtility.SaveFilePanel("Export Baekho prop bounds (" + guids.Length + " selected)", "", "baekho-prop-bounds", "json");
                if (string.IsNullOrEmpty(output)) return;
                int count = ExportToPath(output, guids);
                Debug.Log("Baekho prop bounds catalog exported: " + count + " prefabs, " + output);
                EditorUtility.DisplayDialog("Baekho 소품 크기", count + "개 프리팹의 크기를 내보냈습니다. 맵 제작기의 소품 크기 가져오기를 사용하세요.", "OK");
            }
            catch (Exception exception) { EditorUtility.DisplayDialog("Baekho prop catalog failed", exception.Message, "OK"); }
        }

        /// <summary>Only explicitly selected Project prefab assets; folders and scene objects are never expanded.</summary>
        public static string[] ResolveSelectedPrefabGuids(UnityEngine.Object[] selection)
        {
            const string instruction = "Unity Project 창에서 내보낼 .prefab 에셋만 직접 선택하세요. 폴더, 씬 오브젝트, 다른 종류의 에셋은 선택에서 제외하세요.";
            if (selection == null || selection.Length == 0)
                throw new InvalidOperationException(instruction);
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UnityEngine.Object selected in selection)
            {
                string path = selected == null ? "" : AssetDatabase.GetAssetPath(selected);
                if (!(selected is GameObject) || !EditorUtility.IsPersistent(selected) || !AssetDatabase.IsMainAsset(selected) ||
                    !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(instruction);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException(instruction);
                guids.Add(guid);
            }
            var list = new List<string>(guids);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        /// <summary>Root-local bounds at root scale one; renderers and colliders (including triggers) are unioned.</summary>
        public static int ExportToPath(string output, string[] guids)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before exporting prefab bounds.");
            if (guids == null) throw new ArgumentNullException("guids");
            var entries = new List<Entry>();
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string guid in guids)
                {
                    if (!seen.Add(guid)) continue;
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("A source prefab is missing: " + guid);
                    GameObject instance = PrefabUtility.InstantiatePrefab(prefab, preview) as GameObject;
                    if (instance == null) throw new InvalidDataException("Could not inspect prefab: " + path);
                    try
                    {
                        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        instance.transform.localScale = Vector3.one;
                        Vector3 size;
                        if (!TryMeasure(instance, out size)) { Debug.LogWarning("Baekho: skipped prefab without measurable 3D bounds: " + path); continue; }
                        entries.Add(new Entry { guid = guid, size = size });
                    }
                    finally { UnityEngine.Object.DestroyImmediate(instance); }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            File.WriteAllText(output, JsonUtility.ToJson(new Catalog { assets = entries.ToArray() }, true), new UTF8Encoding(false));
            return entries.Count;
        }

        private static bool TryMeasure(GameObject instance, out Vector3 size)
        {
            Bounds result = new Bounds(); bool found = false;
            Action<Bounds, Transform> add = (bounds, transform) =>
            {
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) || float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z))
                        throw new InvalidDataException("Non-finite prefab bounds: " + instance.name);
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                    else result.Encapsulate(point);
                }
            };
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>())
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (renderer.enabled && filter != null && filter.sharedMesh != null) add(filter.sharedMesh.bounds, renderer.transform);
            }
            foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (renderer.enabled && renderer.sharedMesh != null) add(renderer.localBounds, renderer.transform);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled) continue;
                var box = collider as BoxCollider; var mesh = collider as MeshCollider;
                var sphere = collider as SphereCollider; var capsule = collider as CapsuleCollider;
                if (box != null) add(new Bounds(box.center, box.size), box.transform);
                else if (mesh != null && mesh.sharedMesh != null) add(mesh.sharedMesh.bounds, mesh.transform);
                else if (sphere != null) add(new Bounds(sphere.center, Vector3.one * sphere.radius * 2), sphere.transform);
                else if (capsule != null)
                {
                    Vector3 capsuleSize = Vector3.one * capsule.radius * 2;
                    capsuleSize[capsule.direction] = Math.Max(capsule.height, capsule.radius * 2);
                    add(new Bounds(capsule.center, capsuleSize), capsule.transform);
                }
            }
            size = result.size;
            return found && size.x > 0 && size.y > 0 && size.z > 0;
        }
    }
}
