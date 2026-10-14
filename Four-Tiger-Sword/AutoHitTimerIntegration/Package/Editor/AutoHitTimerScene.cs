using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace StudioBaekho.AutoHitTimer
{
    public static class AutoHitTimerScene
    {
        public static Animator ResolveAnimator(string actorId)
        {
            if (string.IsNullOrEmpty(actorId) || !GlobalObjectId.TryParse(actorId, out var id)) throw new AutoHitTimerException("ACTOR_MISSING", "캐릭터를 선택해 주세요.");
            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
            var actor = obj as Animator;
            if (!actor && obj is GameObject go) actor = go.GetComponent<Animator>();
            if (!actor || !actor.gameObject.scene.IsValid() || !actor.gameObject.scene.isLoaded) throw new AutoHitTimerException("ACTOR_MISSING", "캐릭터가 있는 저장된 씬을 열어 주세요.");
            return actor;
        }

        public static JObject Actors()
        {
            var result = new JArray();
            foreach (var actor in Resources.FindObjectsOfTypeAll<Animator>().Where(a => a.gameObject.scene.IsValid() && a.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(a) && (a.hideFlags & HideFlags.HideAndDontSave) == 0))
            {
                if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(actor.gameObject.scene)) continue;
                var id = GlobalObjectId.GetGlobalObjectIdSlow(actor);
                if (id.targetObjectId == 0) continue;
                var weapons = new JArray();
                foreach (var renderer in actor.GetComponentsInChildren<MeshRenderer>(true))
                    weapons.Add(new JObject { ["id"] = GlobalObjectId.GetGlobalObjectIdSlow(renderer.gameObject).ToString(), ["label"] = AnimationUtility.CalculateTransformPath(renderer.transform, actor.transform), ["bone"] = renderer.transform.parent ? AnimationUtility.CalculateTransformPath(renderer.transform.parent, actor.transform) : "", ["active"] = renderer.enabled && renderer.gameObject.activeInHierarchy });
                result.Add(new JObject { ["id"] = id.ToString(), ["label"] = actor.gameObject.scene.name + "/" + HierarchyPath(actor.transform), ["controllerGuid"] = actor.runtimeAnimatorController ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(actor.runtimeAnimatorController)) : "", ["weapons"] = weapons });
            }
            return new JObject { ["actors"] = result };
        }

        static string HierarchyPath(Transform t) => t.parent ? HierarchyPath(t.parent) + "/" + t.name : t.name;

        // Construct explicitly: Instantiate() can run ExecuteAlways/Awake on source scripts before they are removed.
        internal static GameObject CloneRenderHierarchy(Animator actor, string weaponId, string weaponBone)
        {
            var transforms = new Dictionary<Transform, Transform>();
            var root = CloneTransforms(actor.transform, null, transforms);
            try
            {
                root.transform.position = Vector3.zero;
                root.transform.rotation = Quaternion.identity;
                root.transform.localScale = actor.transform.lossyScale;
                CopyRenderers(transforms);
                if (!string.IsNullOrEmpty(weaponId))
                {
                    if (!GlobalObjectId.TryParse(weaponId, out var id)) throw new AutoHitTimerException("WEAPON_MISSING", "선택한 무기를 찾을 수 없습니다.");
                    var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                    var weapon = obj as GameObject;
                    if (!weapon && obj is Component component) weapon = component.gameObject;
                    if (!weapon) throw new AutoHitTimerException("WEAPON_MISSING", "선택한 무기를 찾을 수 없습니다.");
                    if (transforms.TryGetValue(weapon.transform, out var selected))
                    {
                        // Activate only this selected branch; preserve the actor's other renderers.
                        for (var t = selected; t && t != root.transform; t = t.parent) t.gameObject.SetActive(true);
                        foreach (var renderer in selected.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
                    }
                    else
                    {
                        var bone = string.IsNullOrEmpty(weaponBone) ? root.transform : root.transform.Find(weaponBone);
                        if (!bone) throw new AutoHitTimerException("WEAPON_BONE_MISSING", "선택한 무기의 부착 본 경로가 없습니다.");
                        var weaponTransforms = new Dictionary<Transform, Transform>();
                        var clone = CloneTransforms(weapon.transform, bone, weaponTransforms);
                        CopyRenderers(weaponTransforms);
                        clone.SetActive(true);
                    }
                }
                var animator = root.AddComponent<Animator>();
                animator.avatar = actor.avatar;
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.fireEvents = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                root.SetActive(true);
                return root;
            }
            catch { UnityEngine.Object.DestroyImmediate(root); throw; }
        }

        static GameObject CloneTransforms(Transform source, Transform parent, Dictionary<Transform, Transform> map)
        {
            var obj = new GameObject(source.name) { hideFlags = HideFlags.HideAndDontSave };
            var t = obj.transform;
            t.SetParent(parent, false);
            t.localPosition = source.localPosition;
            t.localRotation = source.localRotation;
            t.localScale = source.localScale;
            map[source] = t;
            foreach (Transform child in source) CloneTransforms(child, t, map);
            obj.SetActive(source.gameObject.activeSelf);
            return obj;
        }

        static void CopyRenderers(Dictionary<Transform, Transform> map)
        {
            foreach (var pair in map)
            {
                var source = pair.Key;
                var target = pair.Value.gameObject;
                if (source.TryGetComponent<MeshFilter>(out var filter)) target.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                if (source.TryGetComponent<MeshRenderer>(out var mesh))
                {
                    var copy = target.AddComponent<MeshRenderer>();
                    CopyRenderer(mesh, copy);
                }
                if (source.TryGetComponent<SkinnedMeshRenderer>(out var skin))
                {
                    var copy = target.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = skin.sharedMesh;
                    copy.bones = skin.bones.Select(b => b && map.TryGetValue(b, out var mapped) ? mapped : null).ToArray();
                    copy.rootBone = skin.rootBone && map.TryGetValue(skin.rootBone, out var bone) ? bone : null;
                    copy.localBounds = skin.localBounds;
                    copy.updateWhenOffscreen = true;
                    // Manual Playable sampling and two preview cameras can render multiple poses
                    // within one editor update. Do not reuse the previous pose's skinning matrices.
                    copy.forceMatrixRecalculationPerRender = true;
                    copy.quality = skin.quality;
                    if (skin.sharedMesh) for (var i = 0; i < skin.sharedMesh.blendShapeCount; i++) copy.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                    CopyRenderer(skin, copy);
                }
            }
        }

        static void CopyRenderer(Renderer source, Renderer target)
        {
            target.sharedMaterials = source.sharedMaterials;
            target.enabled = source.enabled;
            target.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            target.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            source.GetPropertyBlock(block);
            target.SetPropertyBlock(block);
        }
    }
}
