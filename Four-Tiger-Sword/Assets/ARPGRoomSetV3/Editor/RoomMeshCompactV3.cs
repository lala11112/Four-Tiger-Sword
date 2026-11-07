#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Unity.Collections;

// Compact material batching for newly composed STATIC ART. Not a shell/door/gameplay optimizer.
public static class RoomMeshCompactV3
{
    sealed class Batch {public Material material;public readonly List<CombineInstance> parts=new List<CombineInstance>();}
    public static void Combine(Transform root,string assetFolder,string category)
    {
        if(!root)throw new ArgumentNullException("root");
        assetFolder=assetFolder.Replace('\\','/').TrimEnd('/');
        if(!assetFolder.StartsWith("Assets/",StringComparison.Ordinal)||assetFolder.Contains(".."))throw new ArgumentException("A project Assets folder is required.");
        var batches=new Dictionary<Material,Batch>();var temporary=new List<Mesh>();var consumed=new HashSet<MeshFilter>();
        long sourceReferenced=0,compactVertices=0;
        try
        {
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer=filter.GetComponent<MeshRenderer>();var mesh=filter.sharedMesh;
                if(!renderer||!renderer.enabled||!filter.gameObject.activeInHierarchy||renderer.HasPropertyBlock()||!mesh||mesh.blendShapeCount>0||IsProtected(filter.transform,root))continue;
                var materials=renderer.sharedMaterials;
                // Partial consumption would drop unmatched submeshes, so preserve that whole renderer.
                if(mesh.subMeshCount==0||materials.Length!=mesh.subMeshCount||Enumerable.Range(0,mesh.subMeshCount).Any(s=>!materials[s]||mesh.GetTopology(s)!=MeshTopology.Triangles))continue;
                bool any=false;
                for(int sub=0;sub<mesh.subMeshCount;sub++)
                {
                    if(mesh.GetIndexCount(sub)==0)continue;
                    var compact=Extract(mesh,sub);temporary.Add(compact);sourceReferenced+=mesh.vertexCount;compactVertices+=compact.vertexCount;
                    Batch batch;if(!batches.TryGetValue(materials[sub],out batch)){batch=new Batch{material=materials[sub]};batches.Add(materials[sub],batch);}
                    batch.parts.Add(new CombineInstance{mesh=compact,subMeshIndex=0,transform=root.worldToLocalMatrix*filter.transform.localToWorldMatrix});any=true;
                }
                if(any)consumed.Add(filter);
            }
            Directory.CreateDirectory(assetFolder);AssetDatabase.Refresh();
            foreach(var batch in batches.Values.OrderBy(b=>AssetDatabase.GetAssetPath(b.material),StringComparer.Ordinal).ThenBy(b=>b.material.name,StringComparer.Ordinal))
            {
                var mesh=new Mesh{name=category+"_"+batch.material.name,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(batch.parts.ToArray(),true,true,false);mesh.RecalculateBounds();
                string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(batch.material));
                string path=assetFolder+"/"+Safe(category)+"_"+Safe(batch.material.name)+"_"+(string.IsNullOrEmpty(guid)?StableName(batch.material.name):guid.Substring(0,8))+".asset";
                var persistent=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(persistent){EditorUtility.CopySerialized(mesh,persistent);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(persistent);}else{AssetDatabase.CreateAsset(mesh,path);persistent=mesh;}
                var go=new GameObject("Compact_"+batch.material.name);go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=persistent;go.AddComponent<MeshRenderer>().sharedMaterial=batch.material;go.isStatic=true;
            }
            foreach(var filter in consumed)
            {
                if(!filter)continue;var go=filter.gameObject;UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshRenderer>());UnityEngine.Object.DestroyImmediate(filter);
                // Keep all colliders, scripts, animation, LOD and other inherited components intact.
                // A pure mesh leaf can be removed; parents and the caller's root survive.
                if(go.transform!=root&&go.transform.childCount==0&&go.GetComponents<Component>().All(c=>c is Transform))UnityEngine.Object.DestroyImmediate(go);
            }
            Debug.Log("Compact "+root.name+": "+consumed.Count+" static renderers -> "+batches.Count+" materials; per-submesh source vertex copies "+sourceReferenced+" -> "+compactVertices+" referenced vertices.");
        }
        finally {foreach(var mesh in temporary)if(mesh)UnityEngine.Object.DestroyImmediate(mesh);}
    }
    static bool IsProtected(Transform candidate,Transform root)
    {
        for(var at=candidate;at;at=at.parent)
        {
            if(at.GetComponent<TextMesh>()||at.GetComponent<Animator>()||at.GetComponent<Animation>()||at.GetComponent<SkinnedMeshRenderer>()||at.GetComponent<LODGroup>())return true;
            foreach(var script in at.GetComponents<MonoBehaviour>())
                if(script&&(script.GetType().Name.Contains("SlidingDoor")||(script.GetType().Namespace??"").StartsWith("TMPro",StringComparison.Ordinal)))return true;
            if(at==root)break;
        }
        return false;
    }
    static Mesh Extract(Mesh source,int submesh)
    {
        // Repeated CombineMeshes(subMeshIndex=N) copies the ENTIRE source vertex buffer for each
        // material slot. Build one tightly remapped buffer from this submesh's referenced indices.
        using(var snapshot=MeshUtility.AcquireReadOnlyMeshData(source))
        {
        var data=snapshot[0];int[] indices;
        using(var values=new NativeArray<int>(data.GetSubMesh(submesh).indexCount,Allocator.Temp)){data.GetIndices(values,submesh,true);indices=values.ToArray();}
        var remap=new Dictionary<int,int>();var order=new List<int>();
        for(int i=0;i<indices.Length;i++)
        {
            int mapped;if(!remap.TryGetValue(indices[i],out mapped)){mapped=order.Count;remap.Add(indices[i],mapped);order.Add(indices[i]);}indices[i]=mapped;
        }
        var output=new Mesh{name=source.name+"_sub"+submesh,indexFormat=IndexFormat.UInt32};
        using(var values=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp)){data.GetVertices(values);output.SetVertices(order.Select(i=>values[i]).ToList());}
        if(data.HasVertexAttribute(VertexAttribute.Normal))using(var values=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp)){data.GetNormals(values);output.SetNormals(order.Select(i=>values[i]).ToList());}
        if(data.HasVertexAttribute(VertexAttribute.Tangent))using(var values=new NativeArray<Vector4>(data.vertexCount,Allocator.Temp)){data.GetTangents(values);output.SetTangents(order.Select(i=>values[i]).ToList());}
        if(data.HasVertexAttribute(VertexAttribute.Color))using(var values=new NativeArray<Color>(data.vertexCount,Allocator.Temp)){data.GetColors(values);output.SetColors(order.Select(i=>values[i]).ToList());}
        for(int channel=0;channel<8;channel++)
        {
            var attribute=(VertexAttribute)((int)VertexAttribute.TexCoord0+channel);if(!data.HasVertexAttribute(attribute))continue;
            int dimension=data.GetVertexAttributeDimension(attribute);
            if(dimension<=2)using(var values=new NativeArray<Vector2>(data.vertexCount,Allocator.Temp)){data.GetUVs(channel,values);output.SetUVs(channel,order.Select(i=>values[i]).ToList());}
            else if(dimension==3)using(var values=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp)){data.GetUVs(channel,values);output.SetUVs(channel,order.Select(i=>values[i]).ToList());}
            else using(var values=new NativeArray<Vector4>(data.vertexCount,Allocator.Temp)){data.GetUVs(channel,values);output.SetUVs(channel,order.Select(i=>values[i]).ToList());}
        }
        output.SetIndices(indices,MeshTopology.Triangles,0,true);output.RecalculateBounds();return output;
        }
    }
    static string Safe(string name){foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');return name;}
    static string StableName(string value){unchecked{uint hash=2166136261;foreach(char c in value)hash=(hash^c)*16777619;return hash.ToString("x8");}}
}
#endif
