using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class PlayerKnockdownVisualAudit
{
    public static string Main()
    {
        string output=Path.GetFullPath("SwordRobotBossVerification");
        Directory.CreateDirectory(output);
        var preview=new PreviewRenderUtility(true);
        var log=new StringBuilder();
        Texture2D sheet=null;
        try
        {
            var source=UnityEngine.Object.FindFirstObjectByType<PlayerController>().GetComponentInChildren<Animator>();
            var model=UnityEngine.Object.Instantiate(source.gameObject);
            model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            model.transform.localScale=source.transform.lossyScale;
            preview.AddSingleGO(model);
            model.SetActive(true);
            foreach(var script in model.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled=false;
            var animator=model.GetComponent<Animator>();
            animator.enabled=true;animator.speed=1f;animator.applyRootMotion=false; animator.fireEvents=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind(); animator.Update(0f);
            var skins=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var meshes=new Mesh[skins.Length];
            for(int i=0;i<skins.Length;i++)
            {
                skins[i].enabled=false;
                var go=new GameObject("Baked");go.transform.SetParent(skins[i].transform,false);
                meshes[i]=new Mesh();go.AddComponent<MeshFilter>().sharedMesh=meshes[i];go.AddComponent<MeshRenderer>().sharedMaterials=skins[i].sharedMaterials;
            }
            preview.cameraFieldOfView=33;preview.camera.nearClipPlane=.05f;preview.camera.farClipPlane=100;
            preview.camera.clearFlags=CameraClearFlags.Color;preview.camera.backgroundColor=new Color(.14f,.16f,.19f,1);
            preview.lights[0].intensity=1.5f;preview.lights[0].transform.rotation=Quaternion.Euler(40,40,0);
            preview.lights[1].intensity=1.3f;preview.lights[1].transform.rotation=Quaternion.Euler(340,218,177);preview.ambientColor=Color.gray;
            string[] states={"Knockdown","Knockdown","GetUp","GetUp","GetUp","GetUp"};
            float[] times={.3f,.999f,0f,.3f,.65f,.999f};
            int size=480;sheet=new Texture2D(size*3,size*2,TextureFormat.RGB24,false);
            for(int i=0;i<states.Length;i++)
            {
                animator.Play(states[i],0,times[i]);animator.Update(0f);
                for(int j=0;j<skins.Length;j++) skins[j].BakeMesh(meshes[j]);
                preview.camera.transform.position=new Vector3(4,3.3f,5);preview.camera.transform.LookAt(new Vector3(0,.7f,0));
                preview.BeginStaticPreview(new Rect(0,0,size,size));preview.Render(true,true);var tex=preview.EndStaticPreview();
                sheet.SetPixels((i%3)*size,(1-i/3)*size,size,size,tex.GetPixels());UnityEngine.Object.DestroyImmediate(tex);
                log.AppendLine(states[i]+"@"+times[i]+" hips="+animator.GetBoneTransform(HumanBodyBones.Hips).position+" head="+animator.GetBoneTransform(HumanBodyBones.Head).position);
            }
            sheet.Apply();File.WriteAllBytes(Path.Combine(output,"player-knockdown-contact.png"),sheet.EncodeToPNG());
            return log.ToString();
        }
        finally{if(sheet!=null)UnityEngine.Object.DestroyImmediate(sheet);preview.Cleanup();}
    }
}
