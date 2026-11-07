#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using LabDoor;

public static class RoomReviewV3
{
    const string Root="Assets/ARPGRoomSetV3";
    static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../../arpg-environment-rooms-v3"));
    static RoomSetV3.SetReport Reports()=>JsonUtility.FromJson<RoomSetV3.SetReport>(File.ReadAllText(Root+"/Manifests/RoomSet_manifest.json"));
    static void Guard(){for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new Exception("Save the open preview scene before creating review scenes.");}
    [MenuItem("Tools/ARPG Room Set V3/Render All Reviews")]
    public static void RenderAll()
    {
        Guard();Directory.CreateDirectory(Output+"/Renders");Directory.CreateDirectory(Root+"/Scenes");
        foreach(var report in Reports().rooms)
        {
            EditorUtility.DisplayProgressBar("Native room review",report.name,.5f);
            try{RenderOne(report);}finally{EditorUtility.ClearProgressBar();}
        }
        AssetDatabase.Refresh();OpenControl();Debug.Log("RoomSet native reviews rendered; all saved scenes and prefabs retain active full ceilings.");
    }
    [MenuItem("Tools/ARPG Room Set V3/Open Full Control Room")]
    public static void OpenControl()
    {
        var r=Reports().rooms.First();
        EditorSceneManager.OpenScene(Root+"/Scenes/"+r.id+"_Full.unity");
    }
    static void RenderOne(RoomSetV3.RoomReport report)
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var room=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(report.prefab));
        var map=JsonUtility.FromJson<RoomSetV3.Map>(File.ReadAllText(report.sourceAsset));
        var floors=map.placements.Where(p=>p.role=="floor"||p.role=="bridge").Select(p=>p.position).ToArray();
        var bounds=new Bounds(floors[0],Vector3.zero);foreach(var p in floors)bounds.Encapsulate(p);
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.30f,.34f,.39f);RenderSettings.fog=false;
        var volume=new GameObject("RoomReview_NeutralTone").AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/ARPGEnvironmentArt/Materials/PreviewVolume.asset");
        var interior=Camera("RoomInteriorCamera");interior.tag="MainCamera";interior.fieldOfView=78;
        var desired=new Vector3(bounds.min.x+3,0,bounds.min.z+3);
        var start=floors.OrderBy(p=>Vector2.Distance(new Vector2(p.x,p.z),new Vector2(desired.x,desired.z))+Mathf.Abs(p.y-bounds.min.y)*3).First();
        interior.transform.position=start+Vector3.up*2.6f;interior.transform.LookAt(bounds.center+Vector3.up*2.1f);
        var overview=Camera("RoomOverviewCamera");overview.enabled=false;overview.orthographic=true;
        float span=Mathf.Max(bounds.size.x,bounds.size.z)+7;
        overview.orthographicSize=span*.49f;overview.transform.position=bounds.center+new Vector3(-span,span*1.2f,-span*1.2f);overview.transform.LookAt(bounds.center+Vector3.up*1.8f);
        var outside=Camera("ClosedShellCamera");outside.enabled=false;outside.orthographic=true;outside.orthographicSize=span*.66f;
        outside.transform.position=bounds.center+new Vector3(-span,span*1.2f,-span*1.2f);outside.transform.LookAt(bounds.center+Vector3.up*report.ceilingHeight*.35f);
        Camera detail=null;
        if(report.clusterCertificates!=null&&report.clusterCertificates.Length>0)
        {
            detail=Camera("EquipmentClusterCamera");detail.enabled=false;detail.orthographic=true;detail.orthographicSize=Mathf.Max(7,Mathf.Max(report.clusterCertificates[0].objectSize.x,report.clusterCertificates[0].objectSize.z)*.8f);
            var target=report.clusterCertificates[0].objectCenter;detail.transform.position=target+new Vector3(-8,10,-10);detail.transform.LookAt(target);
        }
        // Save only the enclosed state. Temporary inspection cutaway is never written to a scene or prefab.
        EditorSceneManager.SaveScene(scene,Root+"/Scenes/"+report.id+"_Full.unity");
        Capture(interior,Output+"/Renders/"+report.id+"_Interior.png");
        var sun=new GameObject("TemporaryInspectionKey").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2.1f;sun.color=new Color(1,.94f,.86f);sun.transform.rotation=Quaternion.Euler(48,-30,0);sun.shadows=LightShadows.Soft;
        Capture(outside,Output+"/Renders/"+report.id+"_Closed.png");
        room.transform.Find("Structure/Ceiling").gameObject.SetActive(false);
        room.transform.Find("Structure/Walls").gameObject.SetActive(false);
        room.transform.Find("Lighting").gameObject.SetActive(false);
        var sign=room.transform.Find("RoomIdentitySignage");
        if(sign&&(sign.position.x<bounds.min.x+map.cellSize*.6f||sign.position.z<bounds.min.z+map.cellSize*.6f))sign.gameObject.SetActive(false);
        var architecture=room.transform.Find("Architecture");if(architecture){var ceiling=architecture.Find("CeilingServiceGrid");if(ceiling)ceiling.gameObject.SetActive(false);}
        foreach(var door in room.GetComponentsInChildren<SlidingDoor>())door.SetOpenImmediate(true);
        var reviewWalls=new GameObject("TemporaryCutawayWallShell");
        float limit=bounds.max.y+4.3f;
        foreach(var p in map.placements.Where(p=>p.role=="wall"||p.role=="cliff"))
        {
            var size=p.fitSize.sqrMagnitude>.001f?p.fitSize:new Vector3(map.cellSize,p.fitHeight>0?p.fitHeight:6,map.cellSize);
            var c=p.surface=="pivot"?p.position:p.surface=="top"?p.position-Vector3.up*size.y*.5f:p.position+Vector3.up*size.y*.5f;
            float bottom=c.y-size.y*.5f,top=Mathf.Min(c.y+size.y*.5f,limit);
            bool front=p.position.x<bounds.min.x+map.cellSize*.6f||p.position.z<bounds.min.z+map.cellSize*.6f;
            if(front)top=Mathf.Min(top,bounds.min.y+.4f);
            if(top<=bottom)continue;
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name="InspectionWall";g.transform.SetParent(reviewWalls.transform);g.transform.position=new Vector3(c.x,(bottom+top)*.5f,c.z);g.transform.rotation=Quaternion.Euler(p.rotation);g.transform.localScale=new Vector3(size.x,top-bottom,size.z);UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());g.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/ARPGEnvironmentArt/Materials/Graphite_Recess.mat");
        }
        // Clip architectural inspection triangles on foreground walls and above the review cut plane.
        var temporaryMeshes=new List<Mesh>();
        var archFilters=architecture?architecture.GetComponentsInChildren<MeshFilter>():new MeshFilter[0];
        var decorArt=room.transform.Find("AuthoredCompositionV3/Art_SurfacesAndServices");
        var inspectionFilters=archFilters.Concat(decorArt?decorArt.GetComponentsInChildren<MeshFilter>():new MeshFilter[0]);
        foreach(var mf in inspectionFilters)
        {
            bool isArchitecture=architecture&&mf.transform.IsChildOf(architecture);
            if(!mf.sharedMesh||(isArchitecture&&mf.transform.IsChildOf(architecture.Find("CeilingServiceGrid"))))continue;
            var copy=UnityEngine.Object.Instantiate(mf.sharedMesh);var v=copy.vertices;
            for(int sub=0;sub<copy.subMeshCount;sub++)
            {
                var tris=copy.GetTriangles(sub);var keep=new List<int>();
                for(int t=0;t<tris.Length;t+=3){var a=mf.transform.TransformPoint(v[tris[t]]);var b=mf.transform.TransformPoint(v[tris[t+1]]);var c=mf.transform.TransformPoint(v[tris[t+2]]);var center=(a+b+c)/3;
                    bool foreground=center.x<bounds.min.x+map.cellSize*.6f||center.z<bounds.min.z+map.cellSize*.6f;
                    if(isArchitecture?(Mathf.Max(a.y,Mathf.Max(b.y,c.y))>limit||foreground):(foreground&&center.y>bounds.min.y+.15f))continue;keep.Add(tris[t]);keep.Add(tris[t+1]);keep.Add(tris[t+2]);}
                copy.SetTriangles(keep,sub);
            }
            mf.sharedMesh=copy;temporaryMeshes.Add(copy);
        }
        RenderSettings.ambientLight=new Color(.38f,.42f,.47f);
        Capture(overview,Output+"/Renders/"+report.id+"_Layout.png");
        if(detail)Capture(detail,Output+"/Renders/"+report.id+"_Cluster.png");
        EditorSceneManager.OpenScene(Root+"/Scenes/"+report.id+"_Full.unity");
        foreach(var mesh in temporaryMeshes)UnityEngine.Object.DestroyImmediate(mesh);
    }
    static Camera Camera(string name)
    {
        var c=new GameObject(name).AddComponent<Camera>();c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.035f,.046f,.058f);c.nearClipPlane=.08f;c.farClipPlane=250;c.allowHDR=true;
        var extra=c.gameObject.AddComponent<UniversalAdditionalCameraData>();extra.renderPostProcessing=true;extra.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;return c;
    }
    static void Capture(Camera c,string path)
    {
        var rt=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32);rt.Create();var prev=c.targetTexture;var active=RenderTexture.active;
        try{c.targetTexture=rt;c.Render();c.Render();RenderTexture.active=rt;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);}
        finally{c.targetTexture=prev;RenderTexture.active=active;rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
    }
    [MenuItem("Tools/ARPG Room Set V3/Export Full Package")]
    public static void Export()
    {
        var check=JsonUtility.FromJson<RoomValidationV3.Report>(File.ReadAllText(Root+"/Manifests/validation.json"));if(!check.passed)throw new Exception("Static room validation must pass before export.");
        AssetDatabase.SaveAssets();
        if(check.rooms.Length!=22)throw new Exception("The full 22-room authored set is required for distribution.");
        var directions=JsonUtility.FromJson<RoomCompositionV3.DirectionSet>(File.ReadAllText(Root+"/SourceData/ArtDirection.json"));
        // Mesh batches from earlier layout revisions may remain on disk. Export only
        // the meshes referenced by final prefabs/scenes, plus explicit source assets.
        var assets=Directory.GetFiles(Root,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".meta")).Select(p=>p.Replace('\\','/')).Where(p=>!p.StartsWith(Root+"/Meshes/",StringComparison.Ordinal)).ToList();
        assets.AddRange(directions.rooms.SelectMany(r=>(r.assemblies??new RoomCompositionV3.Assembly[0]).Concat(r.zones??new RoomCompositionV3.Assembly[0])).SelectMany(a=>a.items??new RoomCompositionV3.Item[0]).Select(i=>i.asset).Where(p=>!string.IsNullOrEmpty(p)));
        var dependencies=AssetDatabase.GetDependencies(assets.Distinct().ToArray(),true).Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal)).Distinct().ToArray();
        // Keep the full dependency closure for meshes, materials and textures, but
        // never distribute shared source prefabs over a project's collider edits.
        var sharedPrefabs=dependencies.Where(IsSharedLabPropPrefab).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        var manifestPath=Root+"/Manifests/shared_prefab_dependencies.json";
        File.WriteAllText(manifestPath,JsonUtility.ToJson(new SharedPrefabDependencies{sourcePrefabs=sharedPrefabs},true));
        AssetDatabase.ImportAsset(manifestPath);
        var packageAssets=dependencies.Where(p=>!IsSharedLabPropPrefab(p)).Concat(new[]{manifestPath}).Distinct().ToArray();
        Directory.CreateDirectory(Output);
        AssetDatabase.ExportPackage(packageAssets,Output+"/ARPG_Rooms_AuthoredV3_FullShell_URP.unitypackage",ExportPackageOptions.Default);
        Debug.Log("Full enclosed room package exported to "+Output+". Excluded "+sharedPrefabs.Length+" shared LabProps29 source prefabs to preserve project colliders and map metadata; see "+manifestPath+" for room-rebuild dependencies.");
    }
    static bool IsSharedLabPropPrefab(string path)=>path.StartsWith("Assets/LabProps29/Prefabs/",StringComparison.OrdinalIgnoreCase)&&path.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase);
    [Serializable] sealed class SharedPrefabDependencies
    {
        public string note="These shared source prefabs are excluded from the package to preserve existing project colliders and map metadata. Keep the project's installed copies. They are needed when rebuilding room compositions; their mesh, material and texture dependencies remain included.";
        public string[] sourcePrefabs;
    }
}
#endif

