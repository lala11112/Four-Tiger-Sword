using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal static class DungeonRewardChestOutline
{
    private static readonly int CenterOS = Shader.PropertyToID("_CenterOS");

    internal static GameObject[] Create(Transform model)
    {
        var mask = Resources.Load<Material>("DungeonRewardChestMask");
        var outline = Resources.Load<Material>("DungeonRewardChestOutline");
        var clear = Resources.Load<Material>("DungeonRewardChestStencilClear");
        if (mask == null || outline == null || clear == null)
        {
            Debug.LogWarning("[Dungeon] Reward chest outline materials are missing.", model);
            return Array.Empty<GameObject>();
        }

        var sources = model.GetComponentsInChildren<MeshRenderer>();
        var groups = new List<GameObject>(sources.Length);
        foreach (var source in sources)
        {
            if (!source.enabled || !source.TryGetComponent<MeshFilter>(out var filter)) continue;
            var mesh = filter.sharedMesh;
            if (mesh == null || mesh.subMeshCount == 0) continue;

            var group = new GameObject("RewardChestOutline") { layer = source.gameObject.layer };
            group.transform.SetParent(source.transform, false);
            groups.Add(group);

            CreatePass(group.transform, "StencilMask", mesh, mask);
            var hull = CreatePass(group.transform, "GoldOutline", mesh, outline);
            var properties = new MaterialPropertyBlock();
            properties.SetVector(CenterOS, mesh.bounds.center);
            hull.SetPropertyBlock(properties);
            CreatePass(group.transform, "StencilClear", mesh, clear);
        }
        return groups.ToArray();
    }

    private static MeshRenderer CreatePass(Transform parent, string name, Mesh mesh, Material material)
    {
        var pass = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer))
        {
            layer = parent.gameObject.layer
        };
        pass.transform.SetParent(parent, false);
        pass.GetComponent<MeshFilter>().sharedMesh = mesh;

        var renderer = pass.GetComponent<MeshRenderer>();
        var slots = new Material[mesh.subMeshCount];
        Array.Fill(slots, material);
        renderer.sharedMaterials = slots;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.allowOcclusionWhenDynamic = false;
        return renderer;
    }
}
