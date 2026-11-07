using System;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class SwordRobotBossLiveGripRender
{
 public static string Main()
 {
  GameObject root=null;var preview=new PreviewRenderUtility(true);Texture2D sheet=null;var meshes=new System.Collections.Generic.List<Mesh>();
  try
  {
   root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Generated/Bosses/SwordRobotBoss/SwordRobotBoss.prefab"),new Vector3(18000,0,18000),Quaternion.identity);
   foreach(var script in root.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=script is SwordRobotBossWeaponGrip||script is Boss;
   root.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled=false;
   var boss=root.GetComponent<Boss>();boss.CombatLabPassive=true;boss.SetCombatTarget(null);
   var animator=root.GetComponentInChildren<Animator>();var grip=animator.GetComponent<SwordRobotBossWeaponGrip>();grip.owner=boss;
   animator.enabled=true;animator.speed=1;animator.Rebind();animator.Update(0);
   var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>();var staticSources=root.GetComponentsInChildren<MeshRenderer>();
   var rendered=new Transform[skins.Length+staticSources.Length];
   for(int i=0;i<rendered.Length;i++)
   {
    var go=new GameObject("BakedRuntimePose");preview.AddSingleGO(go);rendered[i]=go.transform;
    var mf=go.AddComponent<MeshFilter>();var mr=go.AddComponent<MeshRenderer>();
    if(i<skins.Length){var m=new Mesh();meshes.Add(m);mf.sharedMesh=m;mr.sharedMaterials=skins[i].sharedMaterials;skins[i].enabled=false;}
    else{var source=staticSources[i-skins.Length];mf.sharedMesh=source.GetComponent<MeshFilter>().sharedMesh;mr.sharedMaterials=source.sharedMaterials;}
   }
   preview.cameraFieldOfView=33;preview.camera.nearClipPlane=.05f;preview.camera.farClipPlane=100;preview.camera.clearFlags=CameraClearFlags.Color;preview.camera.backgroundColor=new Color(.14f,.16f,.19f,1);
   preview.lights[0].intensity=1.5f;preview.lights[0].transform.rotation=Quaternion.Euler(40,40,0);preview.lights[1].intensity=1.3f;preview.lights[1].transform.rotation=Quaternion.Euler(340,218,177);preview.ambientColor=Color.gray;
   var controller=animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
   string[] names={"Idle","Basic","Combo2","Combo3","Charge","Dash","Sweep","PhaseDown","PhaseRevive"};float[] times={.3f,.54f,.54f,.58f,.53f,.46f,.45f,2.66f,0f};
   int size=480;sheet=new Texture2D(size*3,size*3,TextureFormat.RGB24,false);
   for(int row=0;row<names.Length;row++)
   {
    var clip=controller.layers[0].stateMachine.states.First(s=>s.state.name==names[row]).state.motion as AnimationClip;animator.Play(names[row],0,times[row]/clip.length);animator.Update(0);
    typeof(SwordRobotBossWeaponGrip).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(grip,null);
    for(int i=0;i<rendered.Length;i++)
    {
     Transform source;
     if(i<skins.Length){skins[i].BakeMesh(meshes[i]);source=skins[i].transform;}
     else source=staticSources[i-skins.Length].transform;
     rendered[i].SetPositionAndRotation(source.position-root.transform.position,source.rotation);rendered[i].localScale=source.lossyScale;
    }
    preview.camera.transform.position=new Vector3(4.8f,3.5f,6.4f);preview.camera.transform.LookAt(new Vector3(0,1.7f,0));preview.BeginStaticPreview(new Rect(0,0,size,size));preview.Render(true,true);var tex=preview.EndStaticPreview();sheet.SetPixels(row%3*size,(2-row/3)*size,size,size,tex.GetPixels());UnityEngine.Object.DestroyImmediate(tex);
   }
   sheet.Apply();string path=Path.GetFullPath("SwordRobotBossVerification/live-grip-contact.png");File.WriteAllBytes(path,sheet.EncodeToPNG());return path;
  }
  finally{if(root!=null)UnityEngine.Object.DestroyImmediate(root);if(sheet!=null)UnityEngine.Object.DestroyImmediate(sheet);preview.Cleanup();foreach(var m in meshes)UnityEngine.Object.DestroyImmediate(m);}
 }
}
