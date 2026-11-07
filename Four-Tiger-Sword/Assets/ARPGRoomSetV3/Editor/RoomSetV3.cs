#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class RoomSetV3
{
    public const string Root="Assets/ARPGRoomSetV3";
    [Serializable] public class Map {public string name,sourcePrefabGuid,sourcePrefabPath;public int width,height;public float cellSize=2;public Placement[] placements;public Marker[] markers;public Connection[] connections;}
    [Serializable] public class Placement {public string id,role,prefabGuid,surface;public Vector3 position,rotation,scale,fitSize;public float fitHeight,fitWidth;}
    [Serializable] public class Marker {public string id,kind,label,contentKey;public Vector3 position,rotation;}
    [Serializable] public class Connection {public string id,kind,label;public Vector3 position,rotation;public float width=4,height=3;}
    [Serializable] public class ClusterCertificate {public Vector3 objectCenter,objectSize;}
    [Serializable] public class RoomReport
    {
        public string id,name,prefab,artOverlay,sourceAsset,sourcePrefabGuid,sourcePrefabPath;
        public float ceilingHeight;public int doors,normalizationInferenceCount;
        public RoomCompositionV3.Result composition;
        public ClusterCertificate[] clusterCertificates;
    }
    [Serializable] public class SetReport {public string generatedUtc,scope;public RoomReport[] rooms;}

    [MenuItem("Tools/ARPG Room Set V3/Build Authored Rooms")]
    public static void BuildAuthored(){BuildRooms(null);}
    [MenuItem("Tools/ARPG Room Set V3/Rebuild MCP Rooms")]
    public static void BuildMcp(){BuildRooms(new[]{"c9b39e1706d1e40448db0889617bf47b","5731e44f117b60d449d788b8f5f82c5e","140443c0c47b1d347a0ee5f5bc58a243"});}
    static void BuildRooms(string[] selected)
    {
        var direction=JsonUtility.FromJson<RoomCompositionV3.DirectionSet>(File.ReadAllText(Root+"/SourceData/ArtDirection.json"));
        string shellManifest=Root+"/SourceData/Shell_manifest.json";
        var sources=new Dictionary<string,RoomReport>();
        foreach(string path in new[]{"Assets/ARPGRoomSet/Manifests/RoomSet_manifest.json",shellManifest})if(File.Exists(path))foreach(var record in JsonUtility.FromJson<SetReport>(File.ReadAllText(path)).rooms)sources[record.sourcePrefabGuid]=record;
        foreach(var folder in new[]{"Prefabs/Rooms","Prefabs/Overlays","Prefabs/Shells","Manifests","Meshes/Shell","SourceData"})Directory.CreateDirectory(Root+"/"+folder);
        var reports=new List<RoomReport>();var shells=new Dictionary<string,RoomReport>();
        string existingManifest=Root+"/Manifests/RoomSet_manifest.json";
        if(selected!=null&&File.Exists(existingManifest))reports.AddRange(JsonUtility.FromJson<SetReport>(File.ReadAllText(existingManifest)).rooms.Where(r=>!selected.Contains(r.sourcePrefabGuid)));
        if(File.Exists(shellManifest))foreach(var entry in JsonUtility.FromJson<SetReport>(File.ReadAllText(shellManifest)).rooms)shells[entry.sourcePrefabGuid]=entry;
        foreach(var art in direction.rooms.Where(r=>selected==null||selected.Contains(r.sourcePrefabGuid)))
        {
            var basis=sources[art.sourcePrefabGuid];
            var map=JsonUtility.FromJson<Map>(File.ReadAllText(basis.sourceAsset));
            string source=Root+"/SourceData/"+map.sourcePrefabGuid+".json";if(!string.Equals(Path.GetFullPath(source),Path.GetFullPath(basis.sourceAsset),StringComparison.OrdinalIgnoreCase))File.Copy(basis.sourceAsset,source,true);
            var scene=EditorSceneManager.NewPreviewScene();GameObject room=null;
            EditorUtility.DisplayProgressBar("Authored room composition",map.name,(float)reports.Count/direction.rooms.Length);
            try
            {
                room=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(basis.prefab),scene);
                PrefabUtility.UnpackPrefabInstance(room,PrefabUnpackMode.OutermostRoot,InteractionMode.AutomatedAction);
                foreach(string name in new[]{"RoomDecoration","RoomIdentitySignage"}){var old=room.transform.Find(name);if(old)UnityEngine.Object.DestroyImmediate(old.gameObject);}
                // Preserve the verified shell geometry/colliders while dropping unused vertices and V2 mesh dependencies.
                foreach(string group in new[]{"Structure/Floors","Structure/Walls","Structure/Stairs","Structure/Ceiling","Architecture/WallPanelSystem","Architecture/CeilingServiceGrid","Lighting"})
                {
                    var t=room.transform.Find(group);if(t)RoomMeshCompactV3.Combine(t,Root+"/Meshes/Shell/"+map.sourcePrefabGuid,group.Replace('/','_'));
                }
                string shellPath=Root+"/Prefabs/Shells/"+basis.id+"_Shell.prefab";PrefabUtility.SaveAsPrefabAsset(room,shellPath);
                shells[map.sourcePrefabGuid]=new RoomReport{id=basis.id,name=map.name,sourcePrefabGuid=map.sourcePrefabGuid,sourcePrefabPath=map.sourcePrefabPath,sourceAsset=source,prefab=shellPath,ceilingHeight=basis.ceilingHeight,doors=basis.doors,normalizationInferenceCount=basis.normalizationInferenceCount};
                var composition=map.sourcePrefabGuid=="2387d8dee7ad96a49b8c6a34f100bf62"
                    ?RoomApprovedControlV3.Build(map,room.transform):RoomCompositionV3.Build(map,room.transform);
                var report=new RoomReport{id=basis.id,name=map.name,sourcePrefabGuid=map.sourcePrefabGuid,sourcePrefabPath=map.sourcePrefabPath,sourceAsset=source,
                    prefab=Root+"/Prefabs/Rooms/"+basis.id+".prefab",artOverlay=Root+"/Prefabs/Overlays/"+basis.id+"_Art.prefab",ceilingHeight=basis.ceilingHeight,doors=basis.doors,normalizationInferenceCount=basis.normalizationInferenceCount,composition=composition};
                report.clusterCertificates=composition.assemblies.Where(a=>a.kind=="InteriorAssembly").Select(a=>new ClusterCertificate{objectCenter=a.boundsCenter,objectSize=a.boundsSize}).ToArray();
                StudioBaekho.BaekhoV3DungeonIntegration.PrepareMetadata(room,source);
                PrefabUtility.SaveAsPrefabAsset(composition.root,report.artOverlay);
                PrefabUtility.SaveAsPrefabAsset(room,report.prefab);
                composition.root=null;
                File.WriteAllText(Root+"/Manifests/"+basis.id+".json",JsonUtility.ToJson(report,true));reports.Add(report);
            }
            finally{if(room)UnityEngine.Object.DestroyImmediate(room);EditorSceneManager.ClosePreviewScene(scene);EditorUtility.ClearProgressBar();}
        }
        File.WriteAllText(Root+"/Manifests/RoomSet_manifest.json",JsonUtility.ToJson(new SetReport{generatedUtc=DateTime.UtcNow.ToString("o"),scope="V3 authored asymmetrical environment compositions on verified reconstructed V2 shell. Original game integration remains unverified.",rooms=direction.rooms.Select(d=>reports.First(r=>r.sourcePrefabGuid==d.sourcePrefabGuid)).ToArray()},true));
        File.WriteAllText(shellManifest,JsonUtility.ToJson(new SetReport{generatedUtc=DateTime.UtcNow.ToString("o"),rooms=shells.Values.ToArray()},true));
        AssetDatabase.SaveAssets();AssetDatabase.Refresh();Debug.Log("V3 authored rooms built: "+reports.Count);
        Debug.Log(StudioBaekho.BaekhoV3DungeonIntegration.BeginInstall(true));
    }
}
#endif
