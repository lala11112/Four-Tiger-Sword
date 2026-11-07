#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace StudioBaekho.EnemyForge
{
    /// <summary>Measures a rendering-only copy in enemy-root space. No source or game scripts are instantiated.</summary>
    public static class BaekhoEnemyForgeModelAnalysis
    {
        public static JObject Analyze(JObject blueprint)
        {
            if (blueprint == null) throw new ArgumentException("모델 분석 설정이 없습니다.");
            var warnings = new JArray();
            var result = new JObject {
                ["bounds"] = null,
                ["hpBarPosition"] = blueprint["hpBarPosition"]?.DeepClone() ?? Vector(new Vector3(0, 3, 0)),
                ["vfxPosition"] = blueprint["vfxPosition"]?.DeepClone() ?? Vector(Vector3.up),
                ["warnings"] = warnings
            };
            GameObject root = null;
            Scene scene = default;
            try
            {
                var source = BaekhoEnemyForgeCatalog.Resolve(blueprint["model"], typeof(GameObject)) as GameObject;
                if (!source) throw new InvalidOperationException("분석할 모델을 선택하세요.");
                source = BaekhoEnemyForgePreview.ResolveRenderSource(blueprint, source);
                scene = EditorSceneManager.NewPreviewScene();
                root = new GameObject("Enemy Forge model analysis") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, scene);
                var map = new Dictionary<Transform, Transform>();
                var model = BaekhoEnemyForgePreview.CloneTransforms(source.transform, root.transform, map).gameObject;
                BaekhoEnemyForgePreview.CopyRenderers(map);
                model.SetActive(true);
                var transform = blueprint["modelTransform"] as JObject ?? throw new ArgumentException("모델 배치 설정이 없습니다.");
                model.transform.localPosition = ReadVector(transform["position"]);
                model.transform.localRotation = Quaternion.Euler(ReadVector(transform["rotation"]));
                model.transform.localScale = ReadVector(transform["scale"]);
                if (model.transform.localScale.x <= 0 || model.transform.localScale.y <= 0 || model.transform.localScale.z <= 0)
                    throw new ArgumentException("모델 크기는 0보다 커야 합니다.");
                if (!TryMeasure(model, root.transform, out var bounds))
                    throw new InvalidOperationException("활성 메시를 찾지 못해 기존 표시 위치를 유지했습니다.");
                result["bounds"] = new JObject { ["min"] = Vector(bounds.min), ["max"] = Vector(bounds.max), ["center"] = Vector(bounds.center), ["size"] = Vector(bounds.size) };
                result["hpBarPosition"] = Vector(new Vector3(bounds.center.x, bounds.max.y + Mathf.Max(.15f, bounds.size.y * .08f), bounds.center.z));
                result["vfxPosition"] = Vector(bounds.center);
            }
            catch (Exception exception) { warnings.Add(exception.GetBaseException().Message); }
            finally
            {
                if (root) Object.DestroyImmediate(root);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
            return result;
        }

        internal static bool TryMeasure(GameObject model, Transform root, out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Mesh temporary = null;
                try
                {
                    Mesh mesh = null;
                    if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh)
                    {
                        temporary = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                        // Unity 6000.3 must account for transform scale during baking to yield renderer-local vertices.
                        // With false the scale remains in the baked vertices and the matrix below applies it twice.
                        skin.BakeMesh(temporary, true);
                        temporary.RecalculateBounds();
                        mesh = temporary;
                    }
                    else if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
                    if (!mesh || mesh.vertexCount == 0) continue;
                    var local = mesh.bounds;
                    var matrix = root.worldToLocalMatrix * renderer.localToWorldMatrix;
                    for (var corner = 0; corner < 8; corner++)
                    {
                        var point = matrix.MultiplyPoint3x4(new Vector3((corner & 1) == 0 ? local.min.x : local.max.x,
                            (corner & 2) == 0 ? local.min.y : local.max.y, (corner & 4) == 0 ? local.min.z : local.max.z));
                        if (!Finite(point)) throw new InvalidOperationException("모델의 메시 범위에 유효하지 않은 숫자가 있습니다.");
                        if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                        else bounds.Encapsulate(point);
                    }
                }
                finally { if (temporary) Object.DestroyImmediate(temporary); }
            }
            return found && bounds.size.sqrMagnitude > .000001f;
        }

        static bool Finite(Vector3 value) => !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));
        static Vector3 ReadVector(JToken token)
        {
            var numbers = BaekhoEnemyForgeCatalog.Numbers(token, 3);
            return new Vector3(numbers[0], numbers[1], numbers[2]);
        }
        static JArray Vector(Vector3 value) => new JArray(value.x, value.y, value.z);

        public static string RunRegression()
        {
            GameObject root = null;
            Mesh mesh = null;
            Mesh skinMesh = null;
            Scene scene = default;
            var checks = 0;
            Action<bool, string> check = (valid, message) => { if (!valid) throw new InvalidOperationException("Model analysis regression: " + message); checks++; };
            try
            {
                scene = EditorSceneManager.NewPreviewScene();
                root = new GameObject("Enemy Forge bounds regression") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = new Vector3(40, -20, 17);
                root.transform.rotation = Quaternion.Euler(0, 70, 0);
                var model = new GameObject("Visible mesh") { hideFlags = HideFlags.HideAndDontSave };
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = new Vector3(3, 2, -4);
                model.transform.localScale = new Vector3(2, 3, .5f);
                mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, vertices = new[] { new Vector3(-1, 0, -1), new Vector3(1, 2, 1) } };
                mesh.RecalculateBounds();
                model.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = model.AddComponent<MeshRenderer>();
                check(TryMeasure(model, root.transform, out var bounds), "visible mesh measured");
                check(Vector3.Distance(bounds.min, new Vector3(1, 2, -4.5f)) < .0001f && Vector3.Distance(bounds.max, new Vector3(5, 8, -3.5f)) < .0001f, "offset and nonuniform scale in root space");
                model.transform.localRotation = Quaternion.Euler(0, 90, 0);
                check(TryMeasure(model, root.transform, out bounds) && Mathf.Abs(bounds.size.x - 1) < .0001f && Mathf.Abs(bounds.size.z - 4) < .0001f, "rotated mesh bounds");
                renderer.enabled = false;
                check(!TryMeasure(model, root.transform, out bounds), "disabled renderer ignored");
                renderer.enabled = true; model.SetActive(false);
                check(!TryMeasure(model, root.transform, out bounds), "inactive renderer ignored");
                var skinObject = new GameObject("Skinned mesh") { hideFlags = HideFlags.HideAndDontSave };
                skinObject.transform.SetParent(root.transform, false);
                skinObject.transform.localPosition = Vector3.up;
                skinObject.transform.localScale = new Vector3(2, 3, 1);
                skinMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, vertices = new[] { new Vector3(-1, 0, -.5f), new Vector3(1, 0, -.5f), new Vector3(0, 2, .5f) }, triangles = new[] { 0, 1, 2 },
                    boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 } }, bindposes = new[] { Matrix4x4.identity } };
                skinMesh.RecalculateBounds();
                var skin = skinObject.AddComponent<SkinnedMeshRenderer>();
                skin.bones = new[] { skinObject.transform }; skin.rootBone = skinObject.transform; skin.sharedMesh = skinMesh;
                skin.localBounds = new Bounds(Vector3.zero, Vector3.one * 1000);
                check(TryMeasure(skinObject, root.transform, out bounds) && Vector3.Distance(bounds.min, new Vector3(-2, 1, -.5f)) < .0001f && Vector3.Distance(bounds.max, new Vector3(2, 7, .5f)) < .0001f, "baked skinned bounds ignore inflated import bounds and apply scale once");
                skin.enabled = false;
                skinObject.AddComponent<TrailRenderer>();
                check(!TryMeasure(skinObject, root.transform, out bounds), "trail renderer excluded");
                return "PASS: " + checks + " model bounds checks";
            }
            finally
            {
                if (root) Object.DestroyImmediate(root);
                if (mesh) Object.DestroyImmediate(mesh);
                if (skinMesh) Object.DestroyImmediate(skinMesh);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
#endif
