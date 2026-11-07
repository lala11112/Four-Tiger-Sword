using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;

namespace EasternCyber.EventPropsV2.Editor
{
    public static class EventPropAssetBuilder
    {
        public const string Root = "Assets/EventPropsV2";
        public const string Generated = Root + "/Generated";
        static readonly string[] Ids = { "GoldDispenser", "RewardDispenser", "Roulette", "BloodDonor", "HealingStation", "CurrencyVault", "RelicShop" };
        [Serializable] public class Manifest { public string id; public float heightMeters; public Part[] parts; public ClipSpec[] clips; public MatSpec[] materials; public int triangles; public Metadata metadata; }
        [Serializable] public class Metadata { public OutcomeStops outcome_stop_degrees_blender_y; }
        [Serializable] public class OutcomeStops { public float X, Gold, Item; }
        [Serializable] public class Part { public string name, role; public float[] pivotBlender; }
        [Serializable] public class ClipSpec { public string name; public float duration, rewardTime; public bool loop; public Track[] tracks; }
        // JsonUtility cannot deserialize jagged arrays; scalar keys are normalized before import.
        [Serializable] public class Key { public float time, value; }
        [Serializable] public class Track { public string part, kind, axis; public Key[] keyframes; }
        [Serializable] public class MatSpec { public string name, texture, normalTexture, roughnessTexture, metallicSmoothnessTexture; public float[] baseColor, emission; public float metallic, roughness, emissionStrength; public bool transparent; }
        [Serializable] public class DeviceReport { public string id; public float height, expectedHeight, basisDeterminant, rotationMappingError, beamIdleExtent, beamUseExtent; public int meshes, materials, triangles, clips, missingMaterials, missingTracks, pbrTextureMaps; public bool passed, beamPosePassed=true, pbrMapsPassed=true; public string[] clipNames; }
        [Serializable] public class BuildReport { public bool passed; public DeviceReport[] devices; }

        [MenuItem("Tools/Eastern Cyber V2/Build All Event Props")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(Generated);
            if(File.Exists("../README_Unity.txt"))File.Copy("../README_Unity.txt",Root+"/README.txt",true);
            AssetDatabase.Refresh();
            SetupUrp();
            var reports = new List<DeviceReport>();
            foreach (string id in Ids) reports.Add(BuildOne(id));
            CreateDemo();
            AssetDatabase.SaveAssets();
            var report = new BuildReport { devices = reports.ToArray(), passed = reports.All(r => r.passed) };
            File.WriteAllText(Path.GetFullPath("../asset-validation.json"), JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException("Event prop validation failed; inspect asset-validation.json.");
            AssetDatabase.ExportPackage(Root, Path.GetFullPath("../EventProps.unitypackage"), ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);
            Debug.Log("Built and validated seven event prop prefabs and EventProps.unitypackage.");
        }

        static DeviceReport BuildOne(string id)
        {
            string modelPath = Root + "/Models/" + id + ".fbx";
            string jsonPath = Root + "/Models/" + id + ".json";
            if (!File.Exists(jsonPath)) throw new FileNotFoundException(jsonPath);
            var spec = JsonUtility.FromJson<Manifest>(File.ReadAllText(jsonPath));
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (!importer) throw new FileNotFoundException(modelPath);
            importer.importAnimation = false; importer.globalScale = 1f; importer.useFileScale = true;
            importer.bakeAxisConversion = true; importer.animationType = ModelImporterAnimationType.Generic;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.isReadable = true; importer.SaveAndReimport();
            string folder = Generated + "/" + id; Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var source = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            PrefabUtility.UnpackPrefabInstance(source, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var root = new GameObject(id);
            try
            {
                var basis = ReadBasis(source);
                var renderers = source.GetComponentsInChildren<MeshRenderer>();
                if(renderers.Length==0) throw new InvalidOperationException(id+": no meshes");
                var bounds = renderers[0].bounds; foreach(var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                float sizeCorrection = spec.heightMeters / bounds.size.y;
                var wrappers = new Dictionary<string,Transform>();
                foreach (Part part in spec.parts)
                {
                    Transform original = Find(source.transform, part.name);
                    if(!original) throw new InvalidOperationException(id+": missing part "+part.name);
                    var wrapper = new GameObject(part.name).transform;
                    wrapper.SetParent(root.transform,false); wrapper.position=original.position;
                    original.SetParent(wrapper,true); original.name="Geometry";
                    // A scale animation may export its first frame at zero. Restore
                    // the geometry basis; native wrapper curves own visibility.
                    if(spec.clips.Any(c=>(c.tracks??Array.Empty<Track>()).Any(t=>t.part==part.name && t.kind=="scale")))
                    {
                        var scale=original.localScale;
                        var scaleTracks=spec.clips.SelectMany(c=>c.tracks??Array.Empty<Track>()).Where(t=>t.part==part.name && t.kind=="scale").GroupBy(t=>t.axis).Select(g=>g.First());
                        foreach(var track in scaleTracks)
                        {
                            Vector3 mapped=basis.MultiplyVector(Axis(track.axis));
                            int axis=Mathf.Abs(mapped.x)>.5f?0:Mathf.Abs(mapped.y)>.5f?1:2;
                            float first=track.keyframes!=null && track.keyframes.Length>0 ? track.keyframes[0].value:1;
                            scale[axis]=Mathf.Abs(first)>.00001f?scale[axis]/first:1;
                        }
                        original.localScale=scale;
                    }
                    wrappers.Add(part.name,wrapper);
                }
                UnityEngine.Object.DestroyImmediate(source); source=null;
                foreach(Transform wrapper in root.transform)
                {
                    wrapper.localPosition = (wrapper.localPosition-Vector3.up*bounds.min.y)*sizeCorrection;
                    foreach(Transform geometry in wrapper) { geometry.localPosition*=sizeCorrection; geometry.localScale*=sizeCorrection; }
                }
                RemapMaterials(root,spec,folder);
                var controllerPath=folder+"/"+id+".controller";
                var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if(!controller)controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                var sm=controller.layers[0].stateMachine;
                foreach(var child in sm.states)sm.RemoveState(child.state);
                var useSpec=spec.clips.FirstOrDefault(c=>c.name.Equals("Use",StringComparison.OrdinalIgnoreCase)) ?? spec.clips.First();
                int missingTracks=0;
                var clipNames=new List<string>();
                var idle=CreateClip("Idle",useSpec,wrappers,basis,sizeCorrection,true,0,folder,ref missingTracks);
                var idleState=sm.AddState("Idle"); idleState.motion=idle; sm.defaultState=idleState; clipNames.Add("Idle");
                string[] names=id=="Roulette" ? new[]{"UseMiss","UseGold","UseItem"} : new[]{"Use"};
                for(int i=0;i<names.Length;i++)
                {
                    // Outcome variants differ only in physical wheel stop angle; the host controls outcomes.
                    var stops=spec.metadata?.outcome_stop_degrees_blender_y;
                    float outcomeOffset=id=="Roulette" ? (i==0 ? stops?.X??0 : i==1 ? stops?.Gold??45 : stops?.Item??90) : 0;
                    var clip=CreateClip(names[i],useSpec,wrappers,basis,sizeCorrection,false,outcomeOffset,folder,ref missingTracks);
                    var state=sm.AddState(names[i]); state.motion=clip;
                    var transition=state.AddTransition(idleState); transition.hasExitTime=true; transition.exitTime=1; transition.duration=0;
                    clipNames.Add(names[i]);
                }
                var animator=root.AddComponent<Animator>(); animator.runtimeAnimatorController=controller; animator.applyRootMotion=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var component=root.AddComponent<EventPropController>();
                float ready=id=="Roulette" ? .77f : useSpec.rewardTime>0 ? Mathf.Clamp01(useSpec.rewardTime/useSpec.duration) : .65f;
                component.Configure(id,useSpec.duration,id=="Roulette",ready,spec.parts.Where(p=>p.role=="moving").Select(p=>p.name).ToArray());
                foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    if(!filter.sharedMesh)continue;
                    var partSpec=spec.parts.FirstOrDefault(p=>p.name==filter.transform.parent.name);
                    if(partSpec!=null && spec.clips.Any(c=>(c.tracks??Array.Empty<Track>()).Any(t=>t.part==partSpec.name && t.kind=="scale")))continue;
                    if(partSpec!=null && partSpec.role=="static")
                    { var collider=filter.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;collider.convex=false; }
                    else
                    { var collider=filter.gameObject.AddComponent<BoxCollider>(); collider.center=filter.sharedMesh.bounds.center; collider.size=filter.sharedMesh.bounds.size; }
                }
                idle.SampleAnimation(root,0);
                string prefabPath=folder+"/"+id+".prefab"; PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
                var rr=root.GetComponentsInChildren<MeshRenderer>(); var finalBounds=rr[0].bounds;
                foreach(var r in rr.Skip(1))finalBounds.Encapsulate(r.bounds);
                var report=new DeviceReport {id=id,height=finalBounds.size.y,expectedHeight=spec.heightMeters,meshes=rr.Length,
                    basisDeterminant=basis.determinant,rotationMappingError=VerifyBasisRotation(basis),
                    materials=rr.SelectMany(r=>r.sharedMaterials).Distinct().Count(),triangles=root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3),
                    clips=clipNames.Count,clipNames=clipNames.ToArray(),missingTracks=missingTracks,
                    missingMaterials=rr.SelectMany(r=>r.sharedMaterials).Count(m=>!m || !m.shader || m.shader.name!="Universal Render Pipeline/Lit")};
                report.pbrMapsPassed=VerifyPbrMaps(spec,folder,out report.pbrTextureMaps);
                if(id=="HealingStation")
                {
                    var beam=wrappers["Healing_Active_Beam"].GetComponentInChildren<MeshRenderer>();
                    report.beamIdleExtent=beam.bounds.size.magnitude;
                    AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"/Use.anim").SampleAnimation(root,useSpec.duration*.5f);
                    report.beamUseExtent=beam.bounds.size.magnitude;
                    report.beamPosePassed=report.beamIdleExtent<.01f && report.beamUseExtent>.1f && beam.sharedMaterial.GetFloat("_Surface")==1 && beam.sharedMaterial.GetColor("_BaseColor").a<.5f;
                    idle.SampleAnimation(root,0);
                }
                report.passed=Mathf.Abs(report.height-spec.heightMeters)<.005f && report.meshes>0 && report.triangles>0 && report.missingTracks==0 && report.missingMaterials==0 && report.rotationMappingError<.0001f && report.beamPosePassed && report.pbrMapsPassed;
                return report;
            }
            finally { if(source)UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(root); }
        }
        static Transform Find(Transform root,string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
        static Matrix4x4 ReadBasis(GameObject source)
        {
            var x=Find(source.transform,"Basis_X"); var y=Find(source.transform,"Basis_Y"); var z=Find(source.transform,"Basis_Z");
            if(!x||!y||!z) throw new InvalidOperationException("Model must include Basis_X/Y/Z conversion markers.");
            // Markers are unit-point empties in Blender coordinates, transformed by the imported root.
            Vector3 origin=source.transform.position;
            var matrix=Matrix4x4.identity;
            matrix.SetColumn(0,(x.position-origin).normalized); matrix.SetColumn(1,(y.position-origin).normalized); matrix.SetColumn(2,(z.position-origin).normalized);
            return matrix;
        }
        static Vector3 Axis(string name) => name=="X" ? Vector3.right : name=="Y" ? Vector3.up : Vector3.forward;
        static float VerifyBasisRotation(Matrix4x4 basis)
        {
            float error=0; Vector3 point=new Vector3(.23f,.41f,.67f);
            foreach(var axis in new[]{Vector3.right,Vector3.up,Vector3.forward})
            {
                Vector3 expected=basis.MultiplyVector(Quaternion.AngleAxis(37,axis)*point);
                Vector3 actual=Quaternion.AngleAxis(37,basis.MultiplyVector(axis)*Mathf.Sign(basis.determinant))*basis.MultiplyVector(point);
                error=Mathf.Max(error,Vector3.Distance(expected,actual));
            }
            return error;
        }
        static AnimationClip CreateClip(string name,ClipSpec spec,Dictionary<string,Transform> wrappers,Matrix4x4 basis,float sizeCorrection,bool idle,float outcomeOffset,string folder,ref int missing)
        {
            var clip=new AnimationClip { name=name,frameRate=30 };
            float duration=idle ? 1 : spec.duration;
            // Explicit baseline for every movable part, so reset/Idle restores all tracks.
            foreach(var pair in wrappers)
            {
                SetVector(clip,pair.Key,"m_LocalPosition",new[]{new Keyframe(0,pair.Value.localPosition.x),new Keyframe(duration,pair.Value.localPosition.x)},new[]{new Keyframe(0,pair.Value.localPosition.y),new Keyframe(duration,pair.Value.localPosition.y)},new[]{new Keyframe(0,pair.Value.localPosition.z),new Keyframe(duration,pair.Value.localPosition.z)});
                SetVector(clip,pair.Key,"localEulerAnglesRaw",new[]{new Keyframe(0,0),new Keyframe(duration,0)},new[]{new Keyframe(0,0),new Keyframe(duration,0)},new[]{new Keyframe(0,0),new Keyframe(duration,0)});
                SetVector(clip,pair.Key,"m_LocalScale",new[]{new Keyframe(0,1),new Keyframe(duration,1)},new[]{new Keyframe(0,1),new Keyframe(duration,1)},new[]{new Keyframe(0,1),new Keyframe(duration,1)});
            }
            foreach(var track in spec.tracks ?? Array.Empty<Track>())
            {
                if(!wrappers.TryGetValue(track.part,out var part)) {missing++;continue;}
                Vector3 axis=basis.MultiplyVector(Axis(track.axis)).normalized;
                // A reflection changes rotation handedness. Axial vectors transform by det(B)*B.
                if(track.kind=="rotate") axis*=Mathf.Sign(basis.determinant);
                string property=track.kind=="move"?"m_LocalPosition":track.kind=="rotate"?"localEulerAnglesRaw":"m_LocalScale";
                Vector3 baseline=track.kind=="move" ? part.localPosition : track.kind=="scale"?Vector3.one:Vector3.zero;
                for(int c=0;c<3;c++)
                {
                    if(Mathf.Abs(axis[c])<.0001f) continue;
                    var keys=new List<Keyframe>();
                    var sourceKeys=track.keyframes ?? Array.Empty<Key>();
                    if(idle && sourceKeys.Length>0) sourceKeys=new[]{new Key{time=0,value=sourceKeys[0].value},new Key{time=duration,value=sourceKeys[0].value}};
                    if(!idle && track.kind=="rotate" && track.part.ToLowerInvariant().Contains("wheel"))
                        sourceKeys=new[]{new Key{time=0,value=0},new Key{time=.37f,value=0},new Key{time=1.17f,value=720},new Key{time=spec.duration*.70f,value=2160+outcomeOffset},new Key{time=spec.duration*.92f,value=2160+outcomeOffset},new Key{time=spec.duration,value=2160}};
                    foreach(var k in sourceKeys)
                    {
                        float value=track.kind=="move" ? baseline[c]+axis[c]*k.value*sizeCorrection : track.kind=="rotate" ? axis[c]*k.value : 1+Mathf.Abs(axis[c])*(k.value-1);
                        keys.Add(new Keyframe(k.time,value));
                    }
                    if(keys.Count>0)
                    {
                        var curve=new AnimationCurve(keys.ToArray());
                        for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
                        clip.SetCurve(track.part,typeof(Transform),property+"."+"xyz"[c],curve);
                    }
                }
            }
            var settings=AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime=idle; AnimationUtility.SetAnimationClipSettings(clip,settings);
            string path=folder+"/"+name+".anim";
            var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(existing){EditorUtility.CopySerialized(clip,existing);UnityEngine.Object.DestroyImmediate(clip);EditorUtility.SetDirty(existing);return existing;}
            AssetDatabase.CreateAsset(clip,path);return clip;
        }
        static void SetVector(AnimationClip clip,string path,string property,Keyframe[] x,Keyframe[] y,Keyframe[] z)
        { clip.SetCurve(path,typeof(Transform),property+".x",new AnimationCurve(x));clip.SetCurve(path,typeof(Transform),property+".y",new AnimationCurve(y));clip.SetCurve(path,typeof(Transform),property+".z",new AnimationCurve(z)); }
        static Color ColorOf(float[] v,Color fallback) => v!=null && v.Length>=3 ? new Color(v[0],v[1],v[2],v.Length>3?v[3]:1):fallback;
        static void RemapMaterials(GameObject root,Manifest spec,string folder)
        {
            var map=new Dictionary<string,Material>();
            foreach(var s in spec.materials)
            {
                string path=folder+"/"+s.name+".mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
                mat.shader=Shader.Find("Universal Render Pipeline/Lit"); mat.SetColor("_BaseColor",ColorOf(s.baseColor,Color.gray));
                mat.SetFloat("_Metallic",s.metallic); mat.SetFloat("_Smoothness",1-s.roughness);
                if(!string.IsNullOrEmpty(s.texture))
                { var tex=ImportTexture(s.texture,true,false);mat.SetTexture("_BaseMap",tex);mat.SetColor("_BaseColor",new Color(1,1,1,ColorOf(s.baseColor,Color.white).a)); }
                else mat.SetTexture("_BaseMap",null);
                if(!string.IsNullOrEmpty(s.normalTexture))
                { mat.SetTexture("_BumpMap",ImportTexture(s.normalTexture,false,true));mat.SetFloat("_BumpScale",.55f);mat.EnableKeyword("_NORMALMAP"); }
                else {mat.SetTexture("_BumpMap",null);mat.DisableKeyword("_NORMALMAP");}
                // URP Lit reads metallic from R and smoothness from A. The packed
                // texture contains the physical values, so scalar multipliers are 1.
                if(!string.IsNullOrEmpty(s.metallicSmoothnessTexture))
                { mat.SetTexture("_MetallicGlossMap",ImportTexture(s.metallicSmoothnessTexture,false,false));mat.SetFloat("_Metallic",1);mat.SetFloat("_Smoothness",1);mat.SetFloat("_SmoothnessTextureChannel",0);mat.EnableKeyword("_METALLICSPECGLOSSMAP");mat.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"); }
                else {mat.SetTexture("_MetallicGlossMap",null);mat.DisableKeyword("_METALLICSPECGLOSSMAP");}
                // Retain the standalone roughness map as linear source data. URP
                // uses its inverted values in metallicSmoothnessTexture's alpha.
                if(!string.IsNullOrEmpty(s.roughnessTexture))ImportTexture(s.roughnessTexture,false,false);
                Color emission=ColorOf(s.emission,Color.black)*s.emissionStrength;
                mat.SetColor("_EmissionColor",emission); if(emission.maxColorComponent>0)mat.EnableKeyword("_EMISSION");else mat.DisableKeyword("_EMISSION");
                if(s.transparent){mat.SetFloat("_Surface",1);mat.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);mat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);mat.SetFloat("_ZWrite",0);mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");mat.renderQueue=3000;}
                else {mat.SetFloat("_Surface",0);mat.SetFloat("_SrcBlend",(float)BlendMode.One);mat.SetFloat("_DstBlend",(float)BlendMode.Zero);mat.SetFloat("_ZWrite",1);mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");mat.renderQueue=-1;}
                EditorUtility.SetDirty(mat); map[s.name]=mat;
            }
            foreach(var renderer in root.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m && map.TryGetValue(m.name.Replace(" (Instance)",""),out var replacement)?replacement:m).ToArray();
            }
        }
        static Texture2D ImportTexture(string relative,bool srgb,bool normal)
        {
            string path=Root+"/Models/"+relative;
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(!importer)throw new FileNotFoundException(path);
            bool changed=importer.textureType!=(normal?TextureImporterType.NormalMap:TextureImporterType.Default) || importer.sRGBTexture!=srgb || importer.alphaIsTransparency || importer.wrapMode!=TextureWrapMode.Repeat || !importer.mipmapEnabled || importer.filterMode!=FilterMode.Trilinear || importer.anisoLevel!=4 || importer.maxTextureSize!=2048 || importer.textureCompression!=TextureImporterCompression.CompressedHQ;
            importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.sRGBTexture=srgb;importer.alphaIsTransparency=false;importer.mipmapEnabled=true;
            importer.wrapMode=TextureWrapMode.Repeat;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;
            importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.CompressedHQ;
            if(normal){importer.convertToNormalmap=false;importer.flipGreenChannel=false;}
            if(changed)importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(!texture)throw new FileNotFoundException(path);return texture;
        }
        static bool VerifyPbrMaps(Manifest spec,string folder,out int count)
        {
            count=0;bool passed=true;
            foreach(var s in spec.materials)
            {
                var mat=AssetDatabase.LoadAssetAtPath<Material>(folder+"/"+s.name+".mat");
                string[] paths={s.texture,s.normalTexture,s.roughnessTexture,s.metallicSmoothnessTexture};
                for(int i=0;i<paths.Length;i++)
                {
                    if(string.IsNullOrEmpty(paths[i]))continue;count++;
                    string path=Root+"/Models/"+paths[i];var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                    passed &= importer && importer.sRGBTexture==(i==0) && importer.textureType==(i==1?TextureImporterType.NormalMap:TextureImporterType.Default);
                    string property=i==0?"_BaseMap":i==1?"_BumpMap":i==3?"_MetallicGlossMap":null;
                    if(property!=null)passed &= mat && mat.GetTexture(property)==AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                if(!string.IsNullOrEmpty(s.texture))passed &= mat.GetColor("_BaseColor").r==1 && mat.GetColor("_BaseColor").g==1 && mat.GetColor("_BaseColor").b==1;
                if(!string.IsNullOrEmpty(s.normalTexture))passed &= mat.IsKeywordEnabled("_NORMALMAP");
                if(!string.IsNullOrEmpty(s.metallicSmoothnessTexture))passed &= mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP") && mat.GetFloat("_Smoothness")==1 && mat.GetFloat("_SmoothnessTextureChannel")==0;
            }
            return passed;
        }
        static void SetupUrp()
        {
            string rendererPath=Generated+"/EventPropsRenderer.asset", pipelinePath=Generated+"/EventPropsURP.asset";
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if(!renderer){renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,rendererPath);}
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if(!pipeline)
            {
                pipeline=ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
                var so=new SerializedObject(pipeline);var list=so.FindProperty("m_RendererDataList");list.arraySize=1;list.GetArrayElementAtIndex(0).objectReferenceValue=renderer;so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(pipeline,pipelinePath);
            }
            GraphicsSettings.defaultRenderPipeline=pipeline; QualitySettings.renderPipeline=pipeline;
        }
        static void CreateDemo()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            for(int i=0;i<Ids.Length;i++)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Generated+"/"+Ids[i]+"/"+Ids[i]+".prefab");
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab);instance.transform.position=new Vector3((i%4)*2.9f-4.35f,0,(i/4)*3.5f);
            }
            var reference=GameObject.CreatePrimitive(PrimitiveType.Capsule);reference.name="155cm Height Reference";reference.transform.position=new Vector3(4.8f,.775f,3.5f);reference.transform.localScale=new Vector3(.38f,.775f,.38f);
            var referenceMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));referenceMat.SetColor("_BaseColor",new Color(.72f,.75f,.78f));AssetDatabase.DeleteAsset(Generated+"/HeightReference.mat");AssetDatabase.CreateAsset(referenceMat,Generated+"/HeightReference.mat");reference.GetComponent<Renderer>().sharedMaterial=referenceMat;
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.name="Presentation Floor";ground.transform.localScale=new Vector3(10,1,10);ground.transform.position=new Vector3(0,-.025f,1.5f);
            var floorMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMat.SetColor("_BaseColor",new Color(.055f,.067f,.085f));AssetDatabase.DeleteAsset(Generated+"/Floor.mat");AssetDatabase.CreateAsset(floorMat,Generated+"/Floor.mat");ground.GetComponent<Renderer>().sharedMaterial=floorMat;
            var light=new GameObject("Key Light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.25f;light.transform.rotation=Quaternion.Euler(45,-30,0);light.shadows=LightShadows.Soft;
            var fill=new GameObject("Fill Light").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.35f;fill.transform.rotation=Quaternion.Euler(30,140,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.35f,.39f,.45f);
            var camera=new GameObject("Demo Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(8,6.5f,-15);camera.transform.LookAt(new Vector3(0,1,1.5f));camera.orthographic=true;camera.orthographicSize=3.8f;camera.backgroundColor=new Color(.12f,.145f,.18f);camera.clearFlags=CameraClearFlags.SolidColor;
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            new GameObject("Runtime Validation").AddComponent<EventPropRuntimeProbe>();
            EditorSceneManager.SaveScene(scene,Generated+"/EventPropsDemo.unity");
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(Generated+"/EventPropsDemo.unity",true)};
        }
        public static void CapturePreview()
        {
            var camera=Camera.main;if(!camera)throw new InvalidOperationException("Demo camera missing");
            var texture=new RenderTexture(1920,1080,24);camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;
            var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
            File.WriteAllBytes(Path.GetFullPath("../UnityPreview.png"),image.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=null;
            UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(texture);
        }
        public static void RefreshDemoAndExport()
        {
            CreateDemo();AssetDatabase.SaveAssets();CapturePreview();
            CaptureScaleComparison();
            AssetDatabase.ExportPackage(Root,Path.GetFullPath("../EventProps.unitypackage"),ExportPackageOptions.Recurse|ExportPackageOptions.IncludeDependencies);
        }
        public static void UpdateRoulette()
        {
            SetupUrp();var result=BuildOne("Roulette");
            string path=Path.GetFullPath("../asset-validation.json");
            var report=JsonUtility.FromJson<BuildReport>(File.ReadAllText(path));
            for(int i=0;i<report.devices.Length;i++)if(report.devices[i].id=="Roulette")report.devices[i]=result;
            report.passed=report.devices.All(d=>d.passed);File.WriteAllText(path,JsonUtility.ToJson(report,true));
            if(!report.passed)throw new InvalidOperationException("Roulette asset check failed.");
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(Generated+"/EventPropsDemo.unity",OpenSceneMode.Single);
            CapturePreview();CaptureScaleComparison();
            ExportRouletteFixedPackage();
        }
        public static void ExportRouletteFixedPackage()
        {
            AssetDatabase.SaveAssets();
            string output=Path.GetFullPath("../EventProps.unitypackage");
            AssetDatabase.ExportPackage(Root,output,ExportPackageOptions.Recurse|ExportPackageOptions.IncludeDependencies);
            File.Copy(output,Path.GetFullPath("../EventProps_RouletteFixed.unitypackage"),true);
        }
        public static void CaptureScaleComparison()
        {
            var camera=Camera.main;var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;float oldSize=camera.orthographicSize;
            var objects=Ids.Select(id=>GameObject.Find(id)).Concat(new[]{GameObject.Find("155cm Height Reference")}).ToArray();
            var positions=objects.Select(o=>o.transform.position).ToArray();
            try
            {
                for(int i=0;i<objects.Length;i++)objects[i].transform.position=new Vector3(i*2.4f-8.4f,i==7?.775f:0,0);
                camera.transform.position=new Vector3(0,3.8f,-20);camera.transform.LookAt(new Vector3(0,.9f,0));camera.orthographicSize=2.8f;
                var texture=new RenderTexture(3840,1080,24);camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;
                var image=new Texture2D(3840,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,3840,1080),0,0);image.Apply();
                File.WriteAllBytes(Path.GetFullPath("../ScaleComparison155cm.png"),image.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=null;
                UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(texture);
            }
            finally
            { for(int i=0;i<objects.Length;i++)objects[i].transform.position=positions[i];camera.transform.position=oldPosition;camera.transform.rotation=oldRotation;camera.orthographicSize=oldSize; }
        }
    }
}
