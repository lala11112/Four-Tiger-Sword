using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoundaryZeroUI.Editor {
 public static class CompleteUIBuild {
  const string Root="Assets/BoundaryUI";
  static void PrepareTitleFont(string name,string file,string chars){
   string assetPath=Root+"/Resources/BoundaryUI/Fonts/"+name+".asset";var f=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
   if(f==null){var src=AssetDatabase.LoadAssetAtPath<Font>(Root+"/Resources/BoundaryUI/Fonts/"+file);if(!src)throw new Exception("Missing title typeface "+file);f=TMP_FontAsset.CreateFontAsset(src,128,12,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic);f.name=name;f.isMultiAtlasTexturesEnabled=true;AssetDatabase.CreateAsset(f,assetPath);AssetDatabase.AddObjectToAsset(f.material,f);foreach(var t in f.atlasTextures)AssetDatabase.AddObjectToAsset(t,f);}
   f.TryAddCharacters(chars,out string missing);EditorUtility.SetDirty(f);
  }
  public static void Prepare(){
   Directory.CreateDirectory(Root+"/Scenes");Directory.CreateDirectory(Root+"/Prefabs");
   if(!File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset")) TMP_PackageResourceImporter.ImportResources(true,false,false);
   AssetDatabase.Refresh();
   foreach(var file in Directory.GetFiles(Root+"/Resources/BoundaryUI/Art","*.png")){
    var ti=AssetImporter.GetAtPath(file.Replace('\\','/')) as TextureImporter;if(ti==null)continue;
    ti.textureType=Path.GetFileName(file).StartsWith("items")?TextureImporterType.Default:TextureImporterType.Sprite;
    ti.spriteImportMode=SpriteImportMode.Single;ti.alphaIsTransparency=true;ti.mipmapEnabled=false;ti.filterMode=FilterMode.Bilinear;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=2048;ti.SaveAndReimport();
   }
   string fontPath=Root+"/Resources/BoundaryUI/Fonts/BoundaryFont.asset";
   var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
   if(font==null){
    var source=AssetDatabase.LoadAssetAtPath<Font>(Root+"/Resources/BoundaryUI/Fonts/Pretendard-Regular.otf");
    if(source==null)throw new Exception("Missing approved typeface");
    font=TMP_FontAsset.CreateFontAsset(source,64,9,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic);
    font.name="Boundary Pretendard SDF";font.isMultiAtlasTexturesEnabled=true;
    AssetDatabase.CreateAsset(font,fontPath);AssetDatabase.AddObjectToAsset(font.material,font);
    foreach(var tex in font.atlasTextures)AssetDatabase.AddObjectToAsset(tex,font);
   }
   string all=string.Join("",Directory.GetFiles(Root+"/Runtime","*.cs").Select(File.ReadAllText));
   font.TryAddCharacters(new string(all.Distinct().ToArray()),out string missing);EditorUtility.SetDirty(font);
   PrepareTitleFont("TitleKorean","BlackHanSans-Regular.ttf","경계새 게임이어하기설정종료");
   PrepareTitleFont("TitleLatin","ChakraPetch-Bold.ttf","ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 /:-.");
   QualitySettings.renderPipeline=null;GraphicsSettings.defaultRenderPipeline=null;QualitySettings.antiAliasing=8;
   PlayerSettings.companyName="BoundaryZERO";PlayerSettings.productName="Boundary ZERO Legacy PC UI";
   PlayerSettings.defaultScreenWidth=2560;PlayerSettings.defaultScreenHeight=1440;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;PlayerSettings.runInBackground=true;PlayerSettings.colorSpace=ColorSpace.Gamma;PlayerSettings.usePlayerLog=true;
   PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
   PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
   var ps=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);var input=ps.FindProperty("activeInputHandler");if(input!=null){input.intValue=0;ps.ApplyModifiedPropertiesWithoutUndo();}
   var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var camera=new GameObject("UI Preview Camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color32(11,17,25,255);camera.orthographic=true;camera.cullingMask=-1;camera.allowMSAA=true;
   var boot=new GameObject("Boundary ZERO Complete UI");boot.AddComponent<BoundaryUIController>();boot.AddComponent<NativeCaptureQA>();
   PrefabUtility.SaveAsPrefabAsset(boot,Root+"/Prefabs/BoundaryCompleteUI.prefab");
   EditorSceneManager.SaveScene(scene,Root+"/Scenes/CompleteUI.unity");EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(Root+"/Scenes/CompleteUI.unity",true)};
   AssetDatabase.SaveAssets();
  }
  public static void Build(){
   Prepare();string[] args=Environment.GetCommandLineArgs();int ix=Array.IndexOf(args,"-buildOutput");
   string output=ix>=0?args[ix+1]:Path.GetFullPath("Build/Windows/BoundaryZERO_Complete_UI_QHD.exe");Directory.CreateDirectory(Path.GetDirectoryName(output));
   var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Root+"/Scenes/CompleteUI.unity"},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
   string result="result="+report.summary.result+"; errors="+report.summary.totalErrors+"; bytes="+report.summary.totalSize;
   File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"build-result.txt"),result);
   if(report.summary.result!=BuildResult.Succeeded)throw new Exception(result);
   string package=Path.Combine(Path.GetDirectoryName(output),"../경계ZERO_유니티에 넣을 UI 패키지.unitypackage");
   AssetDatabase.ExportPackage(new[]{Root,"Assets/TextMesh Pro/Resources","Assets/TextMesh Pro/Shaders"},package,ExportPackageOptions.Recurse|ExportPackageOptions.IncludeDependencies);
   Debug.Log("BOUNDARY_COMPLETE_UI_BUILD_OK "+result);
  }
 }
}
