#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using LabDoor;

public static class RoomValidationV3
{
    [Serializable] public class Report {public string scope="Static reconstructed room geometry, retained anchors and recovered door opening checks. Original gameplay, dynamic player movement and NavMesh are not validated.";public bool passed;public Room[] rooms;}
    [Serializable] public class Room {public string name,prefab;public bool passed,rootIdentity,ceilingActive;public int missingScripts,missingMeshes,missingMaterials,structuralColliders,artColliders,artScripts,architectureArtColliders,architectureArtScripts,doors,roofSamples,roofFailures,doorBlockSamples,artDoorApproachOverlaps;public string[] findings;}
    static readonly List<string> findings=new List<string>();
    static void Note(string s){if(findings.Count<120)findings.Add(s);}
    static bool Contains(BoxCollider c,Vector3 world)
    {
        Vector3 p=c.transform.InverseTransformPoint(world)-c.center,h=c.size*.5f;
        return Mathf.Abs(p.x)<h.x-.002f&&Mathf.Abs(p.y)<h.y-.002f&&Mathf.Abs(p.z)<h.z-.002f;
    }
    static bool Separated(Vector3 axis,Vector3 a,Vector3 b,Vector3 c,Vector3 half)
    {
        if(axis.sqrMagnitude<.0000000001f)return false;
        float x=Vector3.Dot(axis,a),y=Vector3.Dot(axis,b),z=Vector3.Dot(axis,c);
        float r=Mathf.Abs(axis.x)*half.x+Mathf.Abs(axis.y)*half.y+Mathf.Abs(axis.z)*half.z;
        return Mathf.Min(x,Mathf.Min(y,z))>r+.00001f||Mathf.Max(x,Mathf.Max(y,z))< -r-.00001f;
    }
    static bool TriangleIntersectsBox(Vector3 a,Vector3 b,Vector3 c,Vector3 center,Vector3 half)
    {
        a-=center;b-=center;c-=center;
        if(Separated(Vector3.right,a,b,c,half)||Separated(Vector3.up,a,b,c,half)||Separated(Vector3.forward,a,b,c,half))return false;
        var e0=b-a;var e1=c-b;var e2=a-c;
        if(Separated(Vector3.Cross(e0,e1),a,b,c,half))return false;
        for(int i=0;i<3;i++)
        {var edge=i==0?e0:i==1?e1:e2;if(Separated(Vector3.Cross(edge,Vector3.right),a,b,c,half)||Separated(Vector3.Cross(edge,Vector3.up),a,b,c,half)||Separated(Vector3.Cross(edge,Vector3.forward),a,b,c,half))return false;}
        return true;
    }
    static bool EquipmentMeshEntersApproach(MeshFilter filter,Transform door)
    {
        var mesh=filter.sharedMesh;if(!mesh)return false;
        var matrix=door.worldToLocalMatrix*filter.transform.localToWorldMatrix;
        var vertices=mesh.vertices;for(int i=0;i<vertices.Length;i++)vertices[i]=matrix.MultiplyPoint3x4(vertices[i]);
        // Local door approach: full 4m opening width, 2m on each side of the plane,
        // head-height volume excluding the sill. Geometry is tested, never combined renderer AABB.
        var center=new Vector3(0,1.575f,0);var half=new Vector3(2,1.425f,2);
        for(int s=0;s<mesh.subMeshCount;s++)
        {
            if(mesh.GetTopology(s)!=MeshTopology.Triangles)continue;
            var indices=mesh.GetTriangles(s);
            for(int i=0;i+2<indices.Length;i+=3)if(TriangleIntersectsBox(vertices[indices[i]],vertices[indices[i+1]],vertices[indices[i+2]],center,half))return true;
        }
        return false;
    }
    static bool RoofHit(BoxCollider c,Vector3 world,float ceiling)
    {
        // Segment-box slab intersection in collider local coordinates: no global Physics query.
        Vector3 a=c.transform.InverseTransformPoint(world)-c.center;
        Vector3 b=c.transform.InverseTransformPoint(new Vector3(world.x,ceiling+2,world.z))-c.center;
        Vector3 d=b-a,h=c.size*.5f;float lo=0,hi=1;
        for(int k=0;k<3;k++)
        {
            if(Mathf.Abs(d[k])<.00001f){if(a[k]<-h[k]-.005f||a[k]>h[k]+.005f)return false;}
            else{float p=(-h[k]-a[k])/d[k],q=(h[k]-a[k])/d[k];if(p>q){float t=p;p=q;q=t;}lo=Mathf.Max(lo,p);hi=Mathf.Min(hi,q);if(lo>hi)return false;}
        }
        return c.bounds.max.y>=ceiling-.05f&&lo>=0&&hi<=1;
    }
    static bool Anchor(Transform root,string name,Vector3 position,Vector3 rotation)
    {
        var t=root.Find("ReferenceAnchors/"+name);
        if(!t||Vector3.Distance(t.localPosition,position)>.015f||Quaternion.Angle(t.localRotation,Quaternion.Euler(rotation))>.2f){Note("Reference anchor mismatch: "+name);return false;}return true;
    }
    [MenuItem("Tools/ARPG Room Set V3/Validate All")]
    public static void ValidateAll()
    {
        const string root="Assets/ARPGRoomSetV3";
        var manifest=JsonUtility.FromJson<RoomSetV3.SetReport>(File.ReadAllText(root+"/Manifests/RoomSet_manifest.json"));var results=new List<Room>();
        foreach(var source in manifest.rooms)
        {
            findings.Clear();var result=new Room{name=source.name,prefab=source.prefab};var scene=EditorSceneManager.NewPreviewScene();GameObject instance=null;
            try
            {
                var map=JsonUtility.FromJson<RoomSetV3.Map>(File.ReadAllText(source.sourceAsset));var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(source.prefab);
                if(!prefab)throw new FileNotFoundException(source.prefab);
                instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);Transform rt=instance.transform;
                result.rootIdentity=rt.position.sqrMagnitude<.0001f&&Quaternion.Angle(rt.rotation,Quaternion.identity)<.01f&&Vector3.Distance(rt.localScale,Vector3.one)<.001f;
                foreach(var t in instance.GetComponentsInChildren<Transform>(true))result.missingScripts+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                foreach(var m in instance.GetComponentsInChildren<MeshFilter>(true))if(!m.sharedMesh)result.missingMeshes++;
                foreach(var r in instance.GetComponentsInChildren<Renderer>(true))foreach(var m in r.sharedMaterials)if(!m||!m.shader)result.missingMaterials++;
                var structure=rt.Find("Structure");result.structuralColliders=structure?structure.GetComponentsInChildren<Collider>(true).Count(c=>c.enabled&&!c.isTrigger):0;
                var decoration=rt.Find("AuthoredCompositionV3");
                if(decoration){result.artColliders=decoration.GetComponentsInChildren<Collider>(true).Length;result.artScripts=decoration.GetComponentsInChildren<MonoBehaviour>(true).Count(s=>!(s is Unity.AI.Navigation.NavMeshModifier));}
                foreach(var artRoot in instance.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Architecture"||t.name=="ArchitectureArt"||t.name=="EnvironmentArt"))
                {result.architectureArtColliders+=artRoot.GetComponentsInChildren<Collider>(true).Length;result.architectureArtScripts+=artRoot.GetComponentsInChildren<MonoBehaviour>(true).Count(s=>!(s is Unity.AI.Navigation.NavMeshModifier));}
                bool anchors=true;foreach(var m in map.markers??new RoomSetV3.Marker[0])anchors&=Anchor(rt,"ReferenceOnly_Marker_"+m.kind+"_"+m.id,m.position,m.rotation);
                foreach(var d in map.connections??new RoomSetV3.Connection[0])anchors&=Anchor(rt,"ReferenceOnly_Connection_"+d.id,d.position,d.rotation);
                var ceiling=rt.Find("Structure/Ceiling");result.ceilingActive=ceiling&&ceiling.gameObject.activeInHierarchy;
                var roofs=ceiling?ceiling.GetComponentsInChildren<BoxCollider>(true).Where(c=>c.enabled&&c.gameObject.activeInHierarchy).ToArray():new BoxCollider[0];
                float cell=map.cellSize>0?map.cellSize:2;
                var cells=map.placements.Where(p=>p.role=="floor"||p.role=="bridge"||p.role=="stair"||p.role=="door").ToArray();
                foreach(var p in cells)
                {
                    float floor=p.position.y;
                    if(p.role=="bridge"&&p.fitSize.sqrMagnitude>.001f)floor+=p.surface=="bottom"?p.fitSize.y:p.surface=="pivot"?p.fitSize.y*.5f:0;
                    if(p.role=="stair")floor+=Mathf.Max(.25f,p.scale.y);
                    foreach(var offset in new[]{Vector3.zero,new Vector3(-.4f,0,-.4f)*cell,new Vector3(.4f,0,-.4f)*cell,new Vector3(-.4f,0,.4f)*cell,new Vector3(.4f,0,.4f)*cell})
                    {
                        var sample=p.position+Quaternion.Euler(p.rotation)*offset;sample.y=floor+2;
                        result.roofSamples++;if(!roofs.Any(c=>RoofHit(c,sample,source.ceilingHeight))){result.roofFailures++;Note("Roof gap "+p.id+" at "+sample);}
                    }
                }
                var doors=instance.GetComponentsInChildren<SlidingDoor>(true);result.doors=doors.Length;bool doorAnchors=doors.Length==source.doors;
                foreach(var d in map.connections??new RoomSetV3.Connection[0])
                {
                    if(!string.IsNullOrEmpty(d.kind)&&d.kind!="door")continue;
                    if(!doors.Any(o=>Vector3.Distance(o.transform.position,d.position)<.02f&&Quaternion.Angle(o.transform.rotation,Quaternion.Euler(d.rotation))<.2f)){doorAnchors=false;Note("Recovered door anchor mismatch "+d.id);}
                }
                foreach(var d in doors)d.SetOpenImmediate(true);
                var solid=instance.GetComponentsInChildren<BoxCollider>(true).Where(c=>c.enabled&&!c.isTrigger&&c.gameObject.activeInHierarchy).ToArray();
                foreach(var d in doors)
                {
                    float width=4*d.transform.lossyScale.x;
                    for(int x=0;x<7;x++)for(int y=0;y<5;y++)for(int z=0;z<3;z++)
                    {
                        var sample=d.transform.TransformPoint(new Vector3(Mathf.Lerp(-1.8f,1.8f,x/6f),Mathf.Lerp(.2f,2.8f,y/4f),Mathf.Lerp(-.3f,.3f,z/2f)));
                        var blocker=solid.FirstOrDefault(c=>Contains(c,sample));if(blocker){result.doorBlockSamples++;Note("Open door "+d.name+" blocked by "+blocker.name+" at "+sample);}
                    }
                    var equipment=decoration?decoration.Find("Equipment_AuthoredAssemblies"):null;
                    if(!equipment&&decoration)equipment=decoration.Find("ApprovedEquipment_14_ClearCenter");
                    if(equipment)foreach(var filter in equipment.GetComponentsInChildren<MeshFilter>())
                    {
                        if(EquipmentMeshEntersApproach(filter,d.transform)){result.artDoorApproachOverlaps++;Note("Actual equipment mesh triangle enters door approach: "+filter.name+" at "+d.name);}
                    }
                }
                bool composition=source.composition!=null&&source.composition.requiredPlacementFailures==0;
                if(!composition)Note("Missing authored composition or unresolved required placement failure.");
                bool equipmentCollision=RoomCollisionV3.HasValidEquipmentCollision(instance);
                if(!equipmentCollision)Note("Authored equipment is missing valid solid collision.");
                result.passed=composition&&equipmentCollision&&result.rootIdentity&&anchors&&doorAnchors&&result.ceilingActive&&result.missingScripts==0&&result.missingMeshes==0&&result.missingMaterials==0&&result.structuralColliders>0&&result.artScripts==0&&result.architectureArtColliders==0&&result.architectureArtScripts==0&&result.roofFailures==0&&result.doorBlockSamples==0&&result.artDoorApproachOverlaps==0;
            }
            catch(Exception e){Note(e.GetType().Name+": "+e.Message);result.passed=false;}
            finally{if(instance)UnityEngine.Object.DestroyImmediate(instance);EditorSceneManager.ClosePreviewScene(scene);}
            result.findings=findings.ToArray();results.Add(result);
        }
        var report=new Report{passed=results.Count==manifest.rooms.Length&&results.All(r=>r.passed),rooms=results.ToArray()};
        File.WriteAllText(root+"/Manifests/validation.json",JsonUtility.ToJson(report,true));AssetDatabase.Refresh();Debug.Log("Room set static validation passed="+report.passed+" rooms="+results.Count);
    }
}
#endif

