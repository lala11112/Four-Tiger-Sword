#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Preserve the previously approved control reference; this is not a new V3 layout proposal.
public static class RoomApprovedControlV3
{
    public const string SourceGuid="2387d8dee7ad96a49b8c6a34f100bf62";

    public static RoomCompositionV3.Result Build(RoomSetV3.Map map,Transform room)
    {
        if(map==null||map.sourcePrefabGuid!=SourceGuid||!room)
            throw new ArgumentException("Approved reference helper is restricted to the exact control-room source GUID.");
        if(room.Find("AuthoredCompositionV3"))throw new InvalidOperationException("Existing V3 composition must be removed before preserving the reference.");
        Scene preview=default;GameObject reference=null,clone=null;
        try
        {
            preview=EditorSceneManager.NewPreviewScene();
            RoomCompositionV3.Result cached;
            var source=FindReference(out cached);
            reference=(GameObject)PrefabUtility.InstantiatePrefab(source,preview);
            if(PrefabUtility.IsPartOfPrefabInstance(reference))
                PrefabUtility.UnpackPrefabInstance(reference,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            // V2 room source contains the approved equipment and its retained floor/wall art.
            // A standalone V3 package can instead reuse its own previously saved approved overlay.
            var decoration=reference.transform.Find("RoomDecoration");
            if(!decoration&&reference.name=="AuthoredCompositionV3")decoration=reference.transform;
            if(!decoration)decoration=reference.transform.Find("AuthoredCompositionV3");
            if(!decoration)throw new InvalidOperationException("Approved reference has no decoration subtree.");
            clone=UnityEngine.Object.Instantiate(decoration.gameObject,room,false);
            clone.name="AuthoredCompositionV3";
            // The original decoration root is identity; child local transforms remain unchanged.
            if(clone.transform.localPosition.sqrMagnitude>.00001f||Quaternion.Angle(clone.transform.localRotation,Quaternion.identity)>.01f||Vector3.Distance(clone.transform.localScale,Vector3.one)>.001f)
                throw new InvalidOperationException("Approved decoration root is not identity. Refusing to alter reference alignment.");
            var art=clone.transform.Find("EnvironmentArt");if(!art)art=clone.transform.Find("Art_SurfacesAndServices");
            if(!art)throw new InvalidOperationException("Approved reference is missing its floor/wall art.");
            art.name="Art_SurfacesAndServices";
            var equipment=clone.transform.Find("ApprovedEquipment_14_ClearCenter");
            if(!equipment)equipment=clone.transform.Find("Equipment_AuthoredAssemblies");
            if(!equipment)throw new InvalidOperationException("Approved reference equipment subtree is missing.");
            equipment.name="Equipment_AuthoredAssemblies";
            // V2 already removed SupplementalEquipment from the main wrapper and imported the
            // separate density wrapper once. Never append another copy of either prefab here.
            var parts=cached==null?EquipmentParts(equipment):new List<Transform>();
            int partCount=cached==null?parts.Count:cached.placements.Count;
            if(partCount!=14)throw new InvalidOperationException("Expected the approved 14 equipment parts; found "+partCount+". Refusing an incomplete or duplicated reference.");
            foreach(var transform in clone.GetComponentsInChildren<Transform>(true))
            {
                foreach(var component in transform.GetComponents<Component>())
                    if(component&&!(component is Transform)&&!(component is MeshFilter)&&!(component is MeshRenderer)&&!(component is TextMesh)&&!(component is Collider))
                        UnityEngine.Object.DestroyImmediate(component);
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
            }
            RoomCollisionV3.EnsureEquipmentCollision(clone);
            var result=new RoomCompositionV3.Result{root=clone,name=map.name,sourcePrefabGuid=map.sourcePrefabGuid,
                purpose="Previously approved control-room reference preserved; no new V3 equipment arrangement",
                authoredParts=partCount,acceptedParts=partCount,rejectedParts=0,requiredPlacementFailures=0};
            bool any=false;Bounds aggregate=new Bounds();
            foreach(var part in parts)
            {
                if(!BoundsIn(part.gameObject,clone.transform,out Bounds bounds))throw new InvalidOperationException("Approved part has no static render mesh: "+part.name);
                var filter=part.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f=>f.sharedMesh);
                string path=filter?AssetDatabase.GetAssetPath(filter.sharedMesh):"";
                if(string.IsNullOrEmpty(path))path="PreviouslyApprovedControl/"+part.name;
                var position=clone.transform.InverseTransformPoint(part.position);
                result.placements.Add(new RoomCompositionV3.Placement{asset=path,assembly="PreviouslyApprovedReference14",id=part.name,
                    requestedPosition=position,position=position,adjustment=Vector3.zero,boundsCenter=bounds.center,boundsSize=bounds.size,
                    scale=part.lossyScale,yaw=(Quaternion.Inverse(clone.transform.rotation)*part.rotation).eulerAngles.y,
                    accepted=true,reason="Previously approved reference transform retained exactly"});
                if(!any){aggregate=bounds;any=true;}else aggregate.Encapsulate(bounds);
            }
            if(cached!=null)
            {
                // Compaction deliberately removes individual rendered components. Preserve the
                // previously verified 14-part metadata and verify its aggregate against cached art.
                foreach(var placement in cached.placements)
                {
                    var copy=JsonUtility.FromJson<RoomCompositionV3.Placement>(JsonUtility.ToJson(placement));
                    result.placements.Add(copy);var bounds=new Bounds(copy.boundsCenter,copy.boundsSize);
                    if(!any){aggregate=bounds;any=true;}else aggregate.Encapsulate(bounds);
                }
                if(!BoundsIn(equipment.gameObject,clone.transform,out Bounds rendered)||Vector3.Distance(rendered.center,aggregate.center)>.03f||Vector3.Distance(rendered.size,aggregate.size)>.03f)
                    throw new InvalidOperationException("Cached approved equipment bounds differ from the verified 14-part metadata.");
            }
            result.assemblies.Add(new RoomCompositionV3.AssemblyEvidence{id="PreviouslyApprovedReference14",purpose=result.purpose,
                kind="ApprovedReference",requestedPosition=Vector3.zero,placedPosition=Vector3.zero,translation=Vector3.zero,
                boundsCenter=aggregate.center,boundsSize=aggregate.size,footprint=new Vector3(aggregate.size.x,0,aggregate.size.z),
                authoredParts=partCount,acceptedParts=partCount,status="Previously approved 14-part reference retained; no new composition approval claimed"});
            result.uniqueAssetCounts=result.placements.GroupBy(p=>p.asset).Select(g=>new RoomCompositionV3.AssetCount{asset=g.Key,count=g.Count(),architectural=false}).ToArray();
            result.sameRowRuns=RoomCompositionV3.FindSameRowRuns(result.placements);
            float cell=map.cellSize>0?map.cellSize:2;
            result.usableFloorArea=map.placements.Where(p=>p.role=="floor"||p.role=="bridge").Sum(p=>p.fitSize.sqrMagnitude>.001f?p.fitSize.x*p.fitSize.z:cell*cell);
            // No invented interior-zone occupancy certificate is attached to the historical layout.
            result.perceivedZoneArea=0;result.perceivedOccupancyPercent=0;
            string folder=RoomSetV3.Root+"/Meshes/ApprovedControl/"+SourceGuid;
            RoomMeshCompactV3.Combine(art,folder,"Art");
            RoomMeshCompactV3.Combine(equipment,folder,"Equipment");
            // Preserve any extra visual group while eliminating residual V2 mesh references too.
            foreach(Transform child in clone.transform)
                if(child!=art&&child!=equipment)RoomMeshCompactV3.Combine(child,folder,"Extra_"+child.GetSiblingIndex());
            result.renderers=clone.GetComponentsInChildren<Renderer>(true).Length;
            Debug.Log("Preserved approved control reference: "+partCount+" equipment parts, original relative transforms and art retained in compact V3 meshes.");
            return result;
        }
        catch
        {
            if(clone)UnityEngine.Object.DestroyImmediate(clone);
            throw;
        }
        finally
        {
            if(reference)UnityEngine.Object.DestroyImmediate(reference);
            if(preview.IsValid())EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    static GameObject FindReference(out RoomCompositionV3.Result cached)
    {
        cached=null;
        const string v2="Assets/ARPGRoomSet/Manifests/RoomSet_manifest.json";
        if(File.Exists(v2))
        {
            var report=JsonUtility.FromJson<RoomSetV3.SetReport>(File.ReadAllText(v2));
            var room=(report.rooms??new RoomSetV3.RoomReport[0]).FirstOrDefault(r=>r.sourcePrefabGuid==SourceGuid);
            if(room!=null){var source=AssetDatabase.LoadAssetAtPath<GameObject>(room.prefab);if(source)return source;}
        }
        string manifest=RoomSetV3.Root+"/Manifests/RoomSet_manifest.json";
        if(File.Exists(manifest))
        {
            var report=JsonUtility.FromJson<RoomSetV3.SetReport>(File.ReadAllText(manifest));
            var room=(report.rooms??new RoomSetV3.RoomReport[0]).FirstOrDefault(r=>r.sourcePrefabGuid==SourceGuid);
            if(room!=null&&room.composition!=null&&room.composition.acceptedParts==14&&room.composition.requiredPlacementFailures==0&&room.composition.placements.Count==14)
            {var source=AssetDatabase.LoadAssetAtPath<GameObject>(room.artOverlay);if(source){cached=room.composition;return source;}}
        }
        throw new FileNotFoundException("Approved control reference is unavailable in either the V2 source manifest or the cached V3 overlay.");
    }
    static List<Transform> EquipmentParts(Transform equipment)
    {
        var parts=new List<Transform>();
        foreach(Transform wrapper in equipment)
        {
            // The two approved wrappers contain three primary modules and eleven density parts.
            bool childParts=false;
            foreach(Transform child in wrapper)
                if(child.GetComponentsInChildren<MeshFilter>(true).Any(f=>f.sharedMesh))
                {parts.Add(child);childParts=true;}
            if(!childParts&&wrapper.GetComponentsInChildren<MeshFilter>(true).Any(f=>f.sharedMesh))parts.Add(wrapper);
        }
        return parts;
    }
    static bool BoundsIn(GameObject go,Transform root,out Bounds bounds)
    {
        bool any=false;bounds=new Bounds();foreach(var filter in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if(!filter.sharedMesh||!filter.GetComponent<MeshRenderer>())continue;
            var source=filter.sharedMesh.bounds;var matrix=root.worldToLocalMatrix*filter.transform.localToWorldMatrix;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {var p=matrix.MultiplyPoint3x4(source.center+Vector3.Scale(source.extents,new Vector3(x,y,z)));if(!any){bounds=new Bounds(p,Vector3.zero);any=true;}else bounds.Encapsulate(p);}
        }
        return any;
    }
}
#endif
