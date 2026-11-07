using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SwordRobotBossPrefabVisualAudit
{
    public static string Main()
    {
        var preview=new PreviewRenderUtility(true);
        Texture2D sheet=null;
        var log=new StringBuilder();
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Generated/Bosses/SwordRobotBoss/SwordRobotBoss.prefab");
            var root=UnityEngine.Object.Instantiate(prefab); preview.AddSingleGO(root);
            var animator=root.GetComponentInChildren<Animator>();
            foreach(var script in root.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled=script is SwordRobotBossWeaponGrip;
            root.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled=false;
            var owner=root.GetComponent<Boss>(); owner.enabled=true;
            var grip=animator.GetComponent<SwordRobotBossWeaponGrip>(); grip.owner=owner;
            animator.enabled=true;animator.speed=1;animator.Rebind();animator.Update(0f);
            var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>();
            var meshes=new Mesh[skins.Length];
            for(int i=0;i<skins.Length;i++)
            {
                skins[i].enabled=false;
                var go=new GameObject("Baked");go.transform.SetParent(skins[i].transform,false);
                meshes[i]=new Mesh();go.AddComponent<MeshFilter>().sharedMesh=meshes[i];go.AddComponent<MeshRenderer>().sharedMaterials=skins[i].sharedMaterials;
            }
            preview.cameraFieldOfView=37;preview.camera.nearClipPlane=.05f;preview.camera.farClipPlane=100;
            preview.camera.clearFlags=CameraClearFlags.Color;preview.camera.backgroundColor=new Color(.14f,.16f,.19f,1);
            preview.lights[0].intensity=1.5f;preview.lights[0].transform.rotation=Quaternion.Euler(40,40,0);
            preview.lights[1].intensity=1.3f;preview.lights[1].transform.rotation=Quaternion.Euler(340,218,177);preview.ambientColor=Color.gray;
            string[] states={"Idle","Basic","Combo1","Combo2","Combo3","Charge","Dash","Sweep","PhaseDown","PhaseRevive"};
            float[] times={.3f,.54f,.54f,.54f,.58f,.53f,.46f,.45f,2.26f,0f};
            int size=400;sheet=new Texture2D(size*5,size*2,TextureFormat.RGB24,false);
            var controller=animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
            log.AppendLine("humanScale="+animator.humanScale+" IKPass="+controller.layers[0].iKPass);
            for(int i=0;i<states.Length;i++)
            {
                var clip=controller.layers[0].stateMachine.states.First(s=>s.state.name==states[i]).state.motion as AnimationClip;
                animator.Play(states[i],0,times[i]/clip.length);animator.Update(0f);
                typeof(SwordRobotBossWeaponGrip).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(grip,null);
                for(int j=0;j<skins.Length;j++) skins[j].BakeMesh(meshes[j]);
                preview.camera.transform.position=new Vector3(5,3.7f,6);preview.camera.transform.LookAt(new Vector3(0,1.6f,0));
                preview.BeginStaticPreview(new Rect(0,0,size,size));preview.Render(true,true);var tex=preview.EndStaticPreview();
                sheet.SetPixels((i%5)*size,(1-i/5)*size,size,size,tex.GetPixels());UnityEngine.Object.DestroyImmediate(tex);
                log.AppendLine(states[i]+"@"+times[i]+" R="+animator.GetBoneTransform(HumanBodyBones.RightHand).position.ToString("F3")+" L="+animator.GetBoneTransform(HumanBodyBones.LeftHand).position.ToString("F3")+" leftGripError="+Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftHand).position,grip.leftHandGrip.position));
            }
            sheet.Apply();string path=Path.GetFullPath("SwordRobotBossVerification/generated-prefab-contact.png");File.WriteAllBytes(path,sheet.EncodeToPNG());
            return path+"\n"+log;
        }
        finally{if(sheet!=null)UnityEngine.Object.DestroyImmediate(sheet);preview.Cleanup();}
    }
}
