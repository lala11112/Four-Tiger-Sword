#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Static equipment collision, kept independently of visual mesh batching.</summary>
public static class RoomCollisionV3
{
    public static int EnsurePartCollision(GameObject part)
    {
        if (!part) throw new ArgumentNullException(nameof(part));
        var existing = part.GetComponentsInChildren<Collider>(true);
        if (existing.Any(c => c.enabled && !c.isTrigger)) return existing.Count(c => c.enabled && !c.isTrigger);

        int count = 0;
        int ground = LayerMask.NameToLayer("Ground");
        foreach (var filter in part.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (!filter.sharedMesh || !renderer || !renderer.enabled || !filter.gameObject.activeSelf) continue;
            // Match the actual triangles. A box around a combined equipment mesh would block
            // the empty walkways between props as well as the props themselves.
            var collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
            collider.isTrigger = false;
            if (ground >= 0) filter.gameObject.layer = ground;
            count++;
        }
        return count;
    }

    public static Transform Equipment(GameObject root)
    {
        if (!root) return null;
        // Unity names a standalone overlay after its asset file when loading contents.
        var direct = root.transform.Find("Equipment_AuthoredAssemblies");
        if (direct) return direct;
        var composition = root.transform.Find("AuthoredCompositionV3");
        return composition ? composition.Find("Equipment_AuthoredAssemblies") : null;
    }

    public static int EnsureEquipmentCollision(GameObject root)
    {
        var equipment = Equipment(root);
        if (!equipment) throw new InvalidOperationException("Missing V3 equipment: " + (root ? root.name : "null"));
        return EnsurePartCollision(equipment.gameObject);
    }

    public static bool HasValidEquipmentCollision(GameObject root)
    {
        var equipment = Equipment(root);
        if (!equipment) return false;
        var colliders = equipment.GetComponentsInChildren<Collider>(true);
        return colliders.Any(c => c.enabled && !c.isTrigger)
            && colliders.All(c => !(c is MeshCollider mesh) || mesh.sharedMesh != null);
    }

    [MenuItem("Tools/ARPG Room Set V3/Repair Equipment Collision")]
    public static void RepairAll()
    {
        var manifest = JsonUtility.FromJson<RoomSetV3.SetReport>(File.ReadAllText(RoomSetV3.Root + "/Manifests/RoomSet_manifest.json"));
        int assets = 0, colliders = 0;
        foreach (var path in manifest.rooms.SelectMany(r => new[] { r.prefab, r.artOverlay }).Distinct())
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                colliders += EnsureEquipmentCollision(root);
                if (!HasValidEquipmentCollision(root)) throw new InvalidOperationException("Equipment collision repair failed: " + path);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                assets++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("V3 equipment collision repaired: " + assets + " room/overlay assets, " + colliders + " solid colliders.");
    }
}
#endif
