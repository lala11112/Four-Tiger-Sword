#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Authored compositions only. No boundary sweep, palette cycling or architectural replacement.
public static class RoomCompositionV3
{
    public const string Root="Assets/ARPGRoomSetV3";
    [Serializable] public class DirectionSet { public RoomDirection[] rooms; }
    [Serializable] public class RoomDirection
    {
        public string sourcePrefabGuid,name,purpose,status;
        public Assembly[] assemblies=new Assembly[0],zones=new Assembly[0];
        public Surface[] surfaces=new Surface[0];public Route[] routes=new Route[0];
    }
    [Serializable] public class Assembly
    {
        public string id,purpose;public Vector3 position,footprint,scale=Vector3.one;public float yaw;
        public Item[] items=new Item[0];public bool overheadFrame;
    }
    [Serializable] public class Item
    {
        public string asset,id;public Vector3 offset,scale=Vector3.one;public float yaw;
        public bool architectural,allowOverlap,required;
    }
    [Serializable] public class Surface
    {
        public string id,kind,material,accentHex,assemblyId;public Vector3 position,size;public float yaw;
    }
    [Serializable] public class Route { public Vector3 from,to;public float width=2; }
    [Serializable] public class Placement
    {
        public string asset,assembly,id;public Vector3 requestedPosition,position,adjustment,boundsCenter,boundsSize,scale;
        public float yaw;public bool accepted,retainedForReview,architectural;public string reason;
    }
    [Serializable] public class AssemblyEvidence
    {
        public string id,purpose,kind;public Vector3 requestedPosition,placedPosition,translation,footprint,boundsCenter,boundsSize;
        public int authoredParts,acceptedParts;public string status;
    }
    [Serializable] public class Result
    {
        public GameObject root;public string sourcePrefabGuid,name,purpose;
        public int authoredParts,acceptedParts,rejectedParts,renderers;
        public float usableFloorArea,perceivedZoneArea,perceivedOccupancyPercent;
        public List<Placement> placements=new List<Placement>();
        public List<AssemblyEvidence> assemblies=new List<AssemblyEvidence>();
        public int requiredPlacementFailures;
        public AssetCount[] uniqueAssetCounts;
        public List<MirrorPair> oppositeMirrorPairs=new List<MirrorPair>();
        public List<RowRun> sameRowRuns=new List<RowRun>();
        public string scope="Authored visual composition with static floor/door/stair/marker/main-route checks. Original gameplay/NavMesh not verified.";
    }
    class Floor { public Vector3 center,size;public Quaternion rotation; }
    [Serializable] public class AssetCount { public string asset;public int count;public bool architectural; }
    [Serializable] public class MirrorPair { public string asset,firstAssembly,secondAssembly,reflectionAxis;public Vector3 firstPosition,secondPosition;public bool architectural; }
    [Serializable] public class RowRun { public string asset,axis;public int count;public Vector3[] positions;public bool architectural;public string scope="Quality review hint; authored functional rows are not an automatic failure."; }
    class Body { public GameObject go;public Item item;public Placement evidence;public Bounds initial;public bool required; }
    class Context
    {
        public RoomSetV3.Map map;public RoomDirection direction;public Result result;
        public Transform art,equipment;public List<Floor> floors=new List<Floor>();
        public List<Bounds> occupied=new List<Bounds>();public Dictionary<string,int> repeats=new Dictionary<string,int>();
        public List<string> occupiedOwners=new List<string>();
        public List<RoomSetV3.Connection> doors=new List<RoomSetV3.Connection>();
        public List<Assembly> acceptedZones=new List<Assembly>();
        public Material dark,metal,paint;public float cell;
    }

    public static Result Build(RoomSetV3.Map map,Transform parent)
    {
        if(map==null||!parent)throw new ArgumentException("Source map and composition parent are required.");
        const string path=Root+"/SourceData/ArtDirection.json";
        if(!File.Exists(path))throw new FileNotFoundException("Authored V3 direction is required: "+path);
        var directions=JsonUtility.FromJson<DirectionSet>(File.ReadAllText(path));
        var direction=(directions.rooms??new RoomDirection[0]).FirstOrDefault(r=>r.sourcePrefabGuid==map.sourcePrefabGuid);
        if(direction==null)throw new InvalidOperationException("No authored V3 composition for "+map.name+". Provisional room targets are never auto-filled.");
        if(parent.Find("AuthoredCompositionV3"))throw new InvalidOperationException("Remove the previous authored composition before building.");
        var result=new Result{root=new GameObject("AuthoredCompositionV3"),sourcePrefabGuid=map.sourcePrefabGuid,name=map.name,purpose=direction.purpose};
        result.root.transform.SetParent(parent,false);
        var c=new Context{map=map,direction=direction,result=result,cell=map.cellSize>0?map.cellSize:2};
        c.doors.AddRange((map.connections??new RoomSetV3.Connection[0]).Where(d=>string.IsNullOrEmpty(d.kind)||d.kind=="door"));
        foreach(var door in map.placements.Where(p=>p.role=="door"))
            if(!c.doors.Any(d=>Vector3.Distance(d.position,door.position)<.1f))
                c.doors.Add(new RoomSetV3.Connection{id="PlacementDoor_"+door.id,kind="door",position=door.position,rotation=door.rotation,width=door.fitWidth>0?door.fitWidth:(door.scale.x>0?door.scale.x:1)*c.cell,height=3});
        c.art=Group("Art_SurfacesAndServices",result.root.transform);c.equipment=Group("Equipment_AuthoredAssemblies",result.root.transform);
        c.dark=Material("Graphite_Recess");c.metal=Material("Brushed_Metal");c.paint=Material("RoutePaint");
        foreach(var p in map.placements.Where(p=>p.role=="floor"||p.role=="bridge"))
        {
            var size=p.fitSize.sqrMagnitude>.001f?p.fitSize:new Vector3(c.cell,.3f,c.cell);
            var center=p.role=="floor"&&p.fitSize.sqrMagnitude<=.001f?p.position-Vector3.up*size.y*.5f:SurfaceCenter(p.position,size,p.surface);
            c.floors.Add(new Floor{center=center+Vector3.up*size.y*.5f,size=size,rotation=Quaternion.Euler(p.rotation)});
            result.usableFloorArea+=size.x*size.z;
        }
        if(c.floors.Count==0)throw new InvalidOperationException("Authored composition requires supported source floor geometry.");
        foreach(var assembly in direction.assemblies??new Assembly[0])Compose(c,assembly,"InteriorAssembly");
        foreach(var zone in direction.zones??new Assembly[0])Compose(c,zone,"FunctionalPerimeterZone");
        foreach(var surface in direction.surfaces??new Surface[0])DrawSurface(c,surface);
        DrawDeckJoints(c);
        EstimateArea(c);
        AuditComposition(c);
        string folder=Root+"/Meshes/Composition/"+map.sourcePrefabGuid;
        RoomMeshCompactV3.Combine(c.art,folder,"Art");
        RoomMeshCompactV3.Combine(c.equipment,folder,"Equipment");
        result.renderers=result.root.GetComponentsInChildren<Renderer>(true).Length;
        Debug.Log("V3 authored "+map.name+": "+result.acceptedParts+"/"+result.authoredParts+" parts, "+result.perceivedOccupancyPercent.ToString("F1")+"% supported visual zone area. See per-part adjustment evidence.");
        return result;
    }

    static void Compose(Context c,Assembly spec,string kind)
    {
        if(spec==null)return;
        var group=Group(spec.id??kind,c.equipment);group.localPosition=spec.position;group.localRotation=Quaternion.Euler(0,spec.yaw,0);group.localScale=Scale(spec.scale);
        var report=new AssemblyEvidence{id=spec.id,purpose=spec.purpose,kind=kind,requestedPosition=spec.position,placedPosition=spec.position,footprint=spec.footprint,authoredParts=(spec.items??new Item[0]).Length};
        c.result.assemblies.Add(report);
        var bodies=new List<Body>();var tentativeRepeats=new Dictionary<string,int>();
        int itemIndex=0;
        foreach(var item in spec.items??new Item[0])
        {
            bool required=item.required||(kind=="InteriorAssembly"&&itemIndex==0);itemIndex++;
            c.result.authoredParts++;
            var evidence=new Placement{asset=item.asset,assembly=spec.id,id=item.id,requestedPosition=spec.position+Quaternion.Euler(0,spec.yaw,0)*Vector3.Scale(item.offset,Scale(spec.scale)),scale=Vector3.Scale(Scale(item.scale),Scale(spec.scale)),yaw=spec.yaw+item.yaw,architectural=item.architectural};
            c.result.placements.Add(evidence);
            string path=Resolve(item.asset);int prior=c.repeats.ContainsKey(path)?c.repeats[path]:0;
            int pending=tentativeRepeats.ContainsKey(path)?tentativeRepeats[path]:0;
            if(!item.architectural&&prior+pending>=3){Reject(c,evidence,"Room asset repeat cap of three");continue;}
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!source){Reject(c,evidence,"Missing explicit authored asset: "+path);if(required){c.result.requiredPlacementFailures++;Debug.LogError("REQUIRED V3 hero asset is missing: "+path);}continue;}
            var go=(GameObject)PrefabUtility.InstantiatePrefab(source,group.gameObject.scene);
            if(PrefabUtility.IsPartOfPrefabInstance(go))PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            go.name=string.IsNullOrEmpty(item.id)?Path.GetFileNameWithoutExtension(path):item.id;
            go.transform.SetParent(group,false);go.transform.localPosition=item.offset;go.transform.localRotation=Quaternion.Euler(0,item.yaw,0);go.transform.localScale=Scale(item.scale);
            foreach(var component in go.GetComponentsInChildren<Component>(true))
                if(component&&!(component is Transform)&&!(component is MeshFilter)&&!(component is MeshRenderer)&&!(component is Collider))UnityEngine.Object.DestroyImmediate(component);
            RoomCollisionV3.EnsurePartCollision(go);
            foreach(var renderer in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials=renderer.sharedMaterials;
                if(path.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase))materials=materials.Select(m=>Remap(m,c.metal)).ToArray();
                for(int m=0;m<materials.Length;m++)if(!materials[m])materials[m]=c.metal;
                renderer.sharedMaterials=materials;
            }
            if(!BoundsIn(go,c.result.root.transform,out Bounds bounds)){Reject(c,evidence,"No static render mesh");UnityEngine.Object.DestroyImmediate(go);continue;}
            bodies.Add(new Body{go=go,item=item,evidence=evidence,initial=bounds,required=required});tentativeRepeats[path]=pending+1;
        }
        // Rigid group movement first preserves L/U relationships; it never searches the whole room.
        Vector3 shift=Vector3.zero;int best=-1;
        foreach(var candidate in Offsets(1.5f,.5f))
        {
            int score=0,total=0;foreach(var body in bodies){int weight=body.required?100:1;total+=weight;var b=body.initial;b.center+=candidate;if(Check(c,b,spec.position.y,false,out _))score+=weight;}
            if(score>best){best=score;shift=candidate;}if(score==total)break;
        }
        group.localPosition=spec.position+shift;report.translation=shift;report.placedPosition=group.localPosition;
        bool any=false;Bounds aggregate=new Bounds();
        foreach(var body in bodies)
        {
            var start=body.go.transform.localPosition;bool accepted=false;string reason="";
            foreach(var adjustment in Offsets(.75f,.25f))
            {
                // Small local translations only; no independent rotation or random redistribution.
                body.go.transform.localPosition=start+adjustment;
                BoundsIn(body.go,c.result.root.transform,out Bounds b);
                if(!Check(c,b,spec.position.y,true,out reason,body.item.allowOverlap?spec.id:null))continue;
                body.evidence.accepted=true;body.evidence.boundsCenter=b.center;body.evidence.boundsSize=b.size;
                body.evidence.position=c.result.root.transform.InverseTransformPoint(body.go.transform.position);
                body.evidence.adjustment=body.evidence.position-body.evidence.requestedPosition;
                body.evidence.reason=body.evidence.adjustment.sqrMagnitude>.0001f?"Small authored-group/part clearance adjustment":"Authored position retained";
                Occupy(c,b,spec.id);string path=Resolve(body.item.asset);c.repeats[path]=(c.repeats.ContainsKey(path)?c.repeats[path]:0)+1;
                c.result.acceptedParts++;report.acceptedParts++;accepted=true;
                if(!any){aggregate=b;any=true;}else aggregate.Encapsulate(b);break;
            }
            if(!accepted)
            {
                Reject(c,body.evidence,reason);
                if(body.required)
                {
                    // Never silently delete the main silhouette. Keep it visible ONLY for review,
                    // mark the result invalid, and require an authored position fix before delivery.
                    body.go.transform.localPosition=start;BoundsIn(body.go,c.result.root.transform,out Bounds b);
                    body.evidence.retainedForReview=true;body.evidence.position=c.result.root.transform.InverseTransformPoint(body.go.transform.position);
                    body.evidence.adjustment=body.evidence.position-body.evidence.requestedPosition;
                    body.evidence.boundsCenter=b.center;body.evidence.boundsSize=b.size;
                    body.evidence.reason="REQUIRED HERO RETAINED FOR PREVIEW; clearance fix required: "+reason;
                    c.result.requiredPlacementFailures++;Occupy(c,b,spec.id);
                    Debug.LogError("V3 required hero retained for review only: "+c.map.name+" / "+spec.id+" / "+reason);
                    if(!any){aggregate=b;any=true;}else aggregate.Encapsulate(b);
                }
                else UnityEngine.Object.DestroyImmediate(body.go);
            }
        }
        if(any)
        {
            report.boundsCenter=aggregate.center;report.boundsSize=aggregate.size;
            report.status=report.acceptedParts==report.authoredParts?"All authored parts accepted":"Partial composition: inspect rejected parts before approval";
            c.acceptedZones.Add(new Assembly{position=report.placedPosition,yaw=spec.yaw,footprint=spec.footprint,scale=Scale(spec.scale)});
            if(spec.overheadFrame)ServiceFrame(c,spec,report.placedPosition);
        }
        else report.status="Rejected composition: no parts accepted";
    }
    static void Reject(Context c,Placement item,string reason)
    {item.accepted=false;item.reason=reason;c.result.rejectedParts++;Debug.LogWarning("V3 "+c.map.name+" / "+item.assembly+" / "+item.asset+": "+reason);}
    static IEnumerable<Vector3> Offsets(float maximum,float step)
    {
        var values=new List<Vector3>();int count=Mathf.RoundToInt(maximum/step);
        for(int x=-count;x<=count;x++)for(int z=-count;z<=count;z++)values.Add(new Vector3(x*step,0,z*step));
        return values.OrderBy(v=>v.sqrMagnitude).ThenBy(v=>v.x).ThenBy(v=>v.z);
    }

    static bool Check(Context c,Bounds b,float floor,bool checkOtherProps,out string reason,string ignoreAssembly=null)
    {
        // Full footprint sample against exact supported source rectangles, including explicit bridges.
        for(float x=b.min.x;x<b.max.x+.199f;x+=.2f)for(float z=b.min.z;z<b.max.z+.199f;z+=.2f)
        {
            var p=new Vector3(Mathf.Min(x,b.max.x),floor,Mathf.Min(z,b.max.z));
            if(!Supported(c,p)){reason="Renderer footprint leaves same-height source floor";return false;}
        }
        foreach(var marker in c.map.markers??new RoomSetV3.Marker[0])
            if(b.min.y<marker.position.y+2&&b.max.y>marker.position.y-.1f&&DistanceXZ(b,marker.position)<1.15f)
            {reason="Protected source marker "+marker.id;return false;}
        foreach(var door in c.doors)
        {
            if(b.min.y>door.position.y+door.height+.1f||b.max.y<door.position.y-.1f)continue;
            var inv=Quaternion.Inverse(Quaternion.Euler(door.rotation));var q=inv*(b.center-door.position);
            var ex=inv*Vector3.right*b.extents.x;var ez=inv*Vector3.forward*b.extents.z;
            if(Mathf.Abs(q.x)<door.width*.5f+Mathf.Abs(ex.x)+Mathf.Abs(ez.x)+.15f&&Mathf.Abs(q.z)<2+Mathf.Abs(ex.z)+Mathf.Abs(ez.z))
            {reason="Protected door approach "+door.id;return false;}
        }
        foreach(var stair in c.map.placements.Where(p=>p.role=="stair"))
        {
            float rise=stair.scale.y>0?stair.scale.y:2;
            if(b.min.y>stair.position.y+rise+2||b.max.y<stair.position.y-.1f)continue;
            var inv=Quaternion.Inverse(Quaternion.Euler(stair.rotation));var q=inv*(b.center-stair.position);
            var ex=inv*Vector3.right*b.extents.x;var ez=inv*Vector3.forward*b.extents.z;
            float width=stair.scale.x>0?stair.scale.x:c.cell,depth=stair.scale.z>0?stair.scale.z:c.cell;
            if(Mathf.Abs(q.x)<width*.5f+Mathf.Abs(ex.x)+Mathf.Abs(ez.x)+.55f&&Mathf.Abs(q.z)<depth*.5f+Mathf.Abs(ex.z)+Mathf.Abs(ez.z)+1)
            {reason="Protected stair and landing "+stair.id;return false;}
        }
        foreach(var route in c.direction.routes??new Route[0])
        {
            if(b.min.y>Mathf.Max(route.from.y,route.to.y)+2||b.max.y<Mathf.Min(route.from.y,route.to.y)-.1f)continue;
            var delta=route.to-route.from;delta.y=0;float length=delta.magnitude;if(length<.01f)continue;
            var tangent=delta/length;var normal=new Vector3(-tangent.z,0,tangent.x);
            var rel=b.center-(route.from+route.to)*.5f;
            float along=Mathf.Abs(Vector3.Dot(rel,tangent)),across=Mathf.Abs(Vector3.Dot(rel,normal));
            float alongExtent=Mathf.Abs(tangent.x)*b.extents.x+Mathf.Abs(tangent.z)*b.extents.z;
            float acrossExtent=Mathf.Abs(normal.x)*b.extents.x+Mathf.Abs(normal.z)*b.extents.z;
            if(along<length*.5f+alongExtent&&across<Mathf.Max(2,route.width)*.5f+acrossExtent)
            {reason="Explicit two-meter main route";return false;}
        }
        foreach(var wall in c.map.placements.Where(p=>p.role=="wall"||p.role=="cliff"))
        {
            var size=wall.fitSize.sqrMagnitude>.001f?wall.fitSize:new Vector3(c.cell,wall.fitHeight>0?wall.fitHeight:6,c.cell);
            var center=SurfaceCenter(wall.position,size,wall.surface);var local=TransformBounds(b,Matrix4x4.TRS(center,Quaternion.Euler(wall.rotation),Vector3.one).inverse);
            var wb=new Bounds(Vector3.zero,size-Vector3.one*.025f);
            if(local.Intersects(wb)){reason="Reconstructed source wall solid "+wall.id;return false;}
        }
        if(checkOtherProps)for(int i=0;i<c.occupied.Count;i++)
        {
            // Docked tanks/rings may overlap the enclosing rack/gantry AABB only in their own authored assembly.
            if(ignoreAssembly!=null&&c.occupiedOwners[i]==ignoreAssembly)continue;
            var padded=c.occupied[i];padded.Expand(.035f);if(b.Intersects(padded)){reason="Another authored equipment footprint";return false;}
        }
        reason="";return true;
    }
    static bool Supported(Context c,Vector3 p)
    {
        foreach(var floor in c.floors)
        {
            if(Mathf.Abs(p.y-floor.center.y)>.16f)continue;
            var q=Quaternion.Inverse(floor.rotation)*(p-floor.center);
            if(Mathf.Abs(q.x)<=floor.size.x*.5f+.001f&&Mathf.Abs(q.z)<=floor.size.z*.5f+.001f)return true;
        }
        return false;
    }
    static float DistanceXZ(Bounds b,Vector3 p)
    {float x=Mathf.Max(b.min.x-p.x,0,p.x-b.max.x),z=Mathf.Max(b.min.z-p.z,0,p.z-b.max.z);return Mathf.Sqrt(x*x+z*z);}

    static void DrawSurface(Context c,Surface surface)
    {
        // Explicit linking lets a bay outline follow its rigid clearance adjustment.
        if(!string.IsNullOrEmpty(surface.assemblyId))
        {
            var assembly=c.result.assemblies.FirstOrDefault(a=>a.id==surface.assemblyId);
            if(assembly!=null)surface.position+=assembly.translation;
        }
        var material=Accent(c,surface.accentHex,surface.material);var q=Quaternion.Euler(0,surface.yaw,0);
        if(surface.kind=="serviceBand"||surface.kind=="cableRun")
        {
            float width=Mathf.Max(.06f,surface.size.x),length=Mathf.Max(.2f,surface.size.z);
            for(float z=-length*.5f;z<length*.5f;z+=.2f)
            {
                float segment=Mathf.Min(.2f,length*.5f-z);
                var p=surface.position+q*new Vector3(0,0,z+segment*.5f);float floor=NearestFloor(c,p);
                if(float.IsNaN(floor))continue;
                if(float.IsNaN(NearestFloor(c,p+q*Vector3.right*width*.5f))||float.IsNaN(NearestFloor(c,p-q*Vector3.right*width*.5f)))continue;
                p.y=floor+.028f;
                Box(c.art,surface.id,p,new Vector3(width,.008f,segment),surface.kind=="cableRun"?c.dark:material,q);
                if(surface.kind=="cableRun")foreach(int side in new[]{-1,1})
                    Box(c.art,"RecessedCableTrack",p+q*new Vector3(width*.29f*side,.006f,0),new Vector3(Mathf.Min(.045f,width*.15f),.006f,segment),c.metal,q);
            }
        }
        else if(surface.kind=="cornerOutline")
        {
            for(int axis=0;axis<2;axis++)
            {
                float span=axis==0?surface.size.x:surface.size.z;
                for(float d=-span*.5f;d<span*.5f;d+=.2f)
                {
                    var local=axis==0?new Vector3(d+.1f,0,surface.size.z*.5f):new Vector3(-surface.size.x*.5f,0,d+.1f);
                    var p=surface.position+q*local;float floor=NearestFloor(c,p);if(float.IsNaN(floor))continue;p.y=floor+.035f;
                    Box(c.art,surface.id,p,axis==0?new Vector3(.2f,.008f,.085f):new Vector3(.085f,.008f,.2f),material,q);
                }
            }
        }
        else if(surface.kind=="outline")
        {
            // Clip each perimeter dash to supported floor instead of bridging source holes/height changes.
            for(int axis=0;axis<2;axis++)foreach(int side in new[]{-1,1})
            {
                float span=axis==0?surface.size.x:surface.size.z;
                for(float d=-span*.5f+.12f;d<span*.5f;d+=.5f)
                {
                    var local=axis==0?new Vector3(d,0,side*surface.size.z*.5f):new Vector3(side*surface.size.x*.5f,0,d);
                    var p=surface.position+q*local;float floor=NearestFloor(c,p);
                    if(float.IsNaN(floor))continue;p.y=floor+.034f;
                    Box(c.art,surface.id,p,axis==0?new Vector3(.28f,.008f,.045f):new Vector3(.045f,.008f,.28f),material,q);
                }
            }
        }
        else if(surface.kind=="serviceFrame")
            ServiceFrame(c,new Assembly{id=surface.id,position=surface.position,footprint=surface.size,yaw=surface.yaw},surface.position);
        else
        {
            // Pads are clipped per floor cell; no unsupported oversized rectangle across voids.
            foreach(var floor in c.floors)
            {
                var local=Quaternion.Inverse(q)*(floor.center-surface.position);
                if(Mathf.Abs(local.x)>surface.size.x*.5f||Mathf.Abs(local.z)>surface.size.z*.5f||Mathf.Abs(floor.center.y-surface.position.y)>.2f)continue;
                bool grille=surface.kind=="grille";
                Box(c.art,surface.id,floor.center+Vector3.up*.021f,new Vector3(floor.size.x*.96f,.008f,floor.size.z*.96f),grille?c.dark:material,floor.rotation);
                if(grille)
                    for(float z=-floor.size.z*.45f;z<floor.size.z*.46f;z+=.16f)
                        Box(c.art,"MaintenanceGrilleBar",floor.center+floor.rotation*new Vector3(0,.032f,z),new Vector3(floor.size.x*.88f,.008f,.028f),c.metal,floor.rotation);
            }
        }
    }
    static float NearestFloor(Context c,Vector3 p)
    {foreach(var floor in c.floors){var q=Quaternion.Inverse(floor.rotation)*(p-floor.center);if(Mathf.Abs(p.y-floor.center.y)<.2f&&Mathf.Abs(q.x)<=floor.size.x*.5f&&Mathf.Abs(q.z)<=floor.size.z*.5f)return floor.center.y;}return float.NaN;}
    static void DrawDeckJoints(Context c)
    {
        foreach(var floor in c.floors)foreach(int side in new[]{-1,1})
        {
            var q=floor.rotation;
            Box(c.art,"SourceDeckJoint",floor.center+q*new Vector3(side*(floor.size.x*.5f-.012f),.012f,0),new Vector3(.024f,.006f,floor.size.z),c.dark,q);
            Box(c.art,"SourceDeckJoint",floor.center+q*new Vector3(0,.012f,side*(floor.size.z*.5f-.012f)),new Vector3(floor.size.x,.006f,.024f),c.dark,q);
        }
    }
    static void ServiceFrame(Context c,Assembly spec,Vector3 position)
    {
        var q=Quaternion.Euler(0,spec.yaw,0);float w=Mathf.Max(3,spec.footprint.x),d=Mathf.Max(2,spec.footprint.z);
        // Unequal U-shaped service frame: three perimeter posts, no fourth mirrored corner.
        foreach(var local in new[]{new Vector3(-w*.5f,0,-d*.5f),new Vector3(-w*.5f,0,d*.5f),new Vector3(w*.5f,0,d*.5f)})
        {
            var basePoint=position+q*local;var bounds=new Bounds(basePoint+Vector3.up*1.6f,new Vector3(.13f,3.2f,.13f));
            if(Check(c,bounds,position.y,true,out _)){Box(c.art,"ServiceFramePost",bounds.center,bounds.size,c.metal,q);Occupy(c,bounds,spec.id);}
        }
        // Overhead members sit above player head height, never occluding combat space at eye level.
        Box(c.art,"ServiceTrunk",position+q*new Vector3(0,3.25f,d*.5f),new Vector3(w,.14f,.18f),c.metal,q);
        Box(c.art,"ServiceTrunkReturn",position+q*new Vector3(-w*.5f,3.25f,0),new Vector3(.18f,.14f,d),c.dark,q);
    }
    static void EstimateArea(Context c)
    {
        float area=0;
        foreach(var floor in c.floors)
        for(int x=0;x<4;x++)for(int z=0;z<4;z++)
        {
            var point=floor.center+floor.rotation*new Vector3(((x+.5f)/4-.5f)*floor.size.x,0,((z+.5f)/4-.5f)*floor.size.z);
            bool inside=c.acceptedZones.Any(a=>{
                if(Mathf.Abs(point.y-a.position.y)>.15f)return false;
                var q=Quaternion.Inverse(Quaternion.Euler(0,a.yaw,0))*(point-a.position);
                return Mathf.Abs(q.x)<=a.footprint.x*Mathf.Abs(Scale(a.scale).x)*.5f&&Mathf.Abs(q.z)<=a.footprint.z*Mathf.Abs(Scale(a.scale).z)*.5f;
            });
            if(inside)area+=floor.size.x*floor.size.z/16;
        }
        c.result.perceivedZoneArea=area;c.result.perceivedOccupancyPercent=c.result.usableFloorArea>0?area/c.result.usableFloorArea*100:0;
    }
    static void AuditComposition(Context c)
    {
        var visible=c.result.placements.Where(p=>p.accepted||p.retainedForReview).ToArray();
        c.result.uniqueAssetCounts=visible.GroupBy(p=>p.asset).Select(g=>new AssetCount{asset=g.Key,count=g.Count(),architectural=g.All(p=>p.architectural)}).OrderByDescending(a=>a.count).ThenBy(a=>a.asset,StringComparer.Ordinal).ToArray();
        var floorBounds=new Bounds(c.floors[0].center,Vector3.zero);foreach(var floor in c.floors)floorBounds.Encapsulate(floor.center);var center=floorBounds.center;
        for(int i=0;i<visible.Length;i++)for(int j=i+1;j<visible.Length;j++)
        {
            var a=visible[i];var b=visible[j];if(a.asset!=b.asset||a.assembly==b.assembly||Mathf.Abs(a.position.y-b.position.y)>.25f)continue;
            string axis=null;
            if(Vector2.Distance(new Vector2(2*center.x-a.position.x,a.position.z),new Vector2(b.position.x,b.position.z))<.65f)axis="X";
            else if(Vector2.Distance(new Vector2(a.position.x,2*center.z-a.position.z),new Vector2(b.position.x,b.position.z))<.65f)axis="Z";
            else if(Vector2.Distance(new Vector2(2*center.x-a.position.x,2*center.z-a.position.z),new Vector2(b.position.x,b.position.z))<.65f)axis="Point";
            if(axis!=null)c.result.oppositeMirrorPairs.Add(new MirrorPair{asset=a.asset,firstAssembly=a.assembly,secondAssembly=b.assembly,reflectionAxis=axis,firstPosition=a.position,secondPosition=b.position,architectural=a.architectural&&b.architectural});
        }
        c.result.sameRowRuns=FindSameRowRuns(visible);
    }
    public static List<RowRun> FindSameRowRuns(IEnumerable<Placement> placements)
    {
        var rows=new List<RowRun>();var items=placements.Where(p=>p.accepted||p.retainedForReview).ToArray();
        foreach(var assets in items.GroupBy(p=>p.asset))for(int axis=0;axis<2;axis++)
        {
            // Repeated props within half a meter of a common axis are review hints, not errors.
            foreach(var run in assets.GroupBy(p=>Mathf.RoundToInt(p.position.y*2)+":"+Mathf.RoundToInt((axis==0?p.position.z:p.position.x)*2)))
            {
                var positions=run.Select(p=>p.position).OrderBy(p=>axis==0?p.x:p.z).ToArray();
                if(positions.Length<3)continue;
                rows.Add(new RowRun{asset=assets.Key,axis=axis==0?"X":"Z",count=positions.Length,positions=positions,architectural=run.All(p=>p.architectural)});
            }
        }
        return rows;
    }
    static bool BoundsIn(GameObject go,Transform root,out Bounds bounds)
    {
        bool any=false;bounds=new Bounds();foreach(var f in go.GetComponentsInChildren<MeshFilter>(true))
        {if(!f.sharedMesh||!f.GetComponent<MeshRenderer>())continue;var b=TransformBounds(f.sharedMesh.bounds,root.worldToLocalMatrix*f.transform.localToWorldMatrix);if(!any){bounds=b;any=true;}else bounds.Encapsulate(b);}return any;
    }
    static Bounds TransformBounds(Bounds b,Matrix4x4 m)
    {var result=new Bounds(m.MultiplyPoint3x4(b.center),Vector3.zero);for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)result.Encapsulate(m.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z))));return result;}
    static Vector3 Scale(Vector3 s){return s==Vector3.zero?Vector3.one:s;}
    static void Occupy(Context c,Bounds bounds,string owner){c.occupied.Add(bounds);c.occupiedOwners.Add(owner);}
    static Vector3 SurfaceCenter(Vector3 p,Vector3 size,string surface){return surface=="pivot"?p:surface=="top"?p-Vector3.up*size.y*.5f:p+Vector3.up*size.y*.5f;}
    static string Resolve(string asset){return !string.IsNullOrEmpty(asset)&&asset.Length==32?AssetDatabase.GUIDToAssetPath(asset):asset??"";}
    static Material Material(string name){var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/ARPGEnvironmentArt/Materials/"+name+".mat");if(!m)throw new InvalidOperationException("Missing shared URP material "+name);return m;}
    static Material Accent(Context c,string hex,string fallback)
    {
        if(string.IsNullOrEmpty(hex))return Material(string.IsNullOrEmpty(fallback)?"RoutePaint":fallback);
        string folder=Root+"/Materials/Composition";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        string path=folder+"/"+c.map.sourcePrefabGuid+"_"+hex.TrimStart('#')+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(!material){material=new Material(c.paint);AssetDatabase.CreateAsset(material,path);}
        if(ColorUtility.TryParseHtmlString(hex,out Color color))material.SetColor("_BaseColor",color);EditorUtility.SetDirty(material);return material;
    }
    static Material Remap(Material material,Material fallback)
    {
        string name=material?material.name.ToLowerInvariant():"";
        if(name.Contains("pale")||name.Contains("ceramic"))return Material("Armor_WarmCeramic");
        if(name.Contains("dark")||name.Contains("black"))return Material("Graphite_Recess");
        if(name.Contains("screen")||name.Contains("turquoise")||name.Contains("cyan"))return Material("Display_Blue");
        if(name.Contains("amber")||name.Contains("warning"))return Material("Safety_Amber");
        if(name.Contains("light")||name.Contains("emiss"))return Material("Light_WarmWhite");
        if(name.Contains("navy")||name.Contains("blue"))return Material("Equipment_Navy");return fallback;
    }
    static Transform Group(string name,Transform parent){var g=new GameObject(name);g.transform.SetParent(parent,false);return g.transform;}
    static void Box(Transform root,string name,Vector3 position,Vector3 size,Material material,Quaternion rotation)
    {var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(root,false);g.transform.localPosition=position;g.transform.localRotation=rotation;g.transform.localScale=size;UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());g.GetComponent<MeshRenderer>().sharedMaterial=material;}
}
#endif
