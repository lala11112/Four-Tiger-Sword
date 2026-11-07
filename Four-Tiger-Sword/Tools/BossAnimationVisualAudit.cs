using System;
using System.Linq;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class BossAnimationVisualAudit
{
    const string Base = "Assets/Nephilite Studios/Straight Sword Animation Set/Animations/Straight Sword Animations/Nephilim Animations (Remastered)/Straight Sword Two Hand/";
    public static string Main() => Run(false);
    public static string Source() => Run(true);
    public static string Candidates() => Run(false,true);
    static string Run(bool sourceModel, bool candidates=false)
    {
        string output = Path.GetFullPath("SwordRobotBossVerification");
        Directory.CreateDirectory(output);
        var preview = new PreviewRenderUtility(true);
        var log = new StringBuilder();
        AnimatorController controller = null;
        Avatar correctedAvatar = null;
        Texture2D sheet = null;
        try
        {
            var model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(sourceModel ? "Assets/Nephilite Studios/Straight Sword Animation Set/Models/Md_Char_Low_Poly_Man.fbx" : "Assets/BossMonster_Rigged/Boss_Monster.prefab"));
            preview.AddSingleGO(model);
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = model.GetComponentInChildren<Animator>();
            foreach (bool rightSide in (sourceModel ? new bool[0] : new[]{false,true}))
            {
                var upper = animator.GetBoneTransform(rightSide ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
                var lower = animator.GetBoneTransform(rightSide ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
                var hand = animator.GetBoneTransform(rightSide ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                Vector3 direction = rightSide ? Vector3.right : Vector3.left;
                upper.rotation = Quaternion.FromToRotation(lower.position-upper.position,direction)*upper.rotation;
                lower.rotation = Quaternion.FromToRotation(hand.position-lower.position,direction)*lower.rotation;
                var middle = animator.GetBoneTransform(rightSide ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                var index = animator.GetBoneTransform(rightSide ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
                var little = animator.GetBoneTransform(rightSide ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
                Vector3 palm = Vector3.Cross(middle.position-hand.position,index.position-little.position).normalized*(rightSide ? 1f : -1f);
                var oldBasis = Quaternion.LookRotation(palm,middle.position-hand.position);
                var newBasis = Quaternion.LookRotation(Vector3.down,direction);
                hand.rotation = newBasis*Quaternion.Inverse(oldBasis)*hand.rotation;
            }
            if(!sourceModel)
            {
                var human = animator.avatar.humanDescription;
                human.skeleton = animator.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone { name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale }).ToArray();
                correctedAvatar = AvatarBuilder.BuildHumanAvatar(animator.gameObject, human);
                correctedAvatar.name = "SwordRobotBoss_ScaledAvatar";
                animator.avatar = correctedAvatar;
            }
            log.AppendLine("avatarValid="+animator.avatar.isValid+" humanScale="+animator.humanScale);
            var tposeHand=animator.GetBoneTransform(HumanBodyBones.RightHand);
            Quaternion tposeGrip=Quaternion.Inverse(tposeHand.rotation)*Quaternion.LookRotation(Vector3.down,Vector3.forward);
            Vector3 tposeGripPos=tposeHand.InverseTransformVector(Vector3.right*.10f+Vector3.forward*.05f);
            animator.applyRootMotion = false;
            animator.fireEvents = false;
            var weapon = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BossWeapon_3m/Boss_Weapon_For_3m_Boss.prefab"));
            preview.AddSingleGO(weapon);
            controller = new AnimatorController();
            controller.AddLayer("Preview");
            var machine = controller.layers[0].stateMachine;
            var clipStates = new System.Collections.Generic.Dictionary<string, AnimationClip>();
            foreach (string stem in new[]{"guard_idle_01","light_attack_01","light_attack_02","light_attack_03","charged_attack_01","charged_attack_02","run_attack_01","roll_attack_01","roll_attack_02","back_step_attack_01","back_step_attack_02_release","guard_counter_01"})
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath(Base + "straight_sword_th_" + stem + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
                machine.AddState(stem).motion = clip;
                clipStates[stem] = clip;
            }
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);
            var skinSources = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            var bakedMeshes = new Mesh[skinSources.Length];
            for(int i=0;i<skinSources.Length;i++)
            {
                var skin=skinSources[i];
                skin.enabled=false;
                var bakedObject=new GameObject("BakedPreview");
                bakedObject.transform.SetParent(skin.transform,false);
                bakedMeshes[i]=new Mesh();
                bakedObject.AddComponent<MeshFilter>().sharedMesh=bakedMeshes[i];
                bakedObject.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
            }
            Action<string, float> sample = (stem, time) => {
                animator.Play(stem, 0, time / clipStates[stem].length);
                animator.Update(0f);
                for(int i=0;i<skinSources.Length;i++) skinSources[i].BakeMesh(bakedMeshes[i]);
            };
            sample("guard_idle_01", .3f);
            var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var blade = (left.position - right.position).normalized;
            var side = Vector3.Cross(model.transform.forward, blade).normalized;
            if (side.sqrMagnitude < .1f) side = model.transform.right;
            var facing = Vector3.Cross(side, blade).normalized;
            weapon.transform.position = right.TransformPoint(tposeGripPos);
            weapon.transform.rotation = right.rotation*tposeGrip;
            weapon.transform.localScale = Vector3.one;
            weapon.transform.SetParent(right, true);
            log.AppendLine("right=" + right.position.ToString("F4") + " left=" + left.position.ToString("F4") + " blade=" + blade.ToString("F4"));
            log.AppendLine("gripLocalPos=" + weapon.transform.localPosition.ToString("F5") + " gripLocalEuler=" + weapon.transform.localEulerAngles.ToString("F4") + " gripScale=" + weapon.transform.localScale.ToString("F5"));
            preview.cameraFieldOfView = 33;
            preview.camera.nearClipPlane = .05f;
            preview.camera.farClipPlane = 100;
            preview.camera.clearFlags = CameraClearFlags.Color;
            preview.camera.backgroundColor = new Color(.14f,.16f,.19f,1);
            preview.lights[0].intensity = 1.6f;
            preview.lights[0].transform.rotation = Quaternion.Euler(40,40,0);
            preview.lights[1].intensity = 1.2f;
            preview.lights[1].transform.rotation = Quaternion.Euler(340,218,177);
            preview.ambientColor = new Color(.5f,.5f,.5f,1);
            string[] names = {"guard_idle_01","light_attack_01","light_attack_02","light_attack_03","charged_attack_01","charged_attack_02","run_attack_01"};
            float[][] times = {new[]{.3f,.3f,.3f,.3f},new[]{.2f,.46f,.54f,.68f},new[]{.2f,.46f,.54f,.68f},new[]{.2f,.4f,.5f,.65f},new[]{.53f,1.4f,1.52f,1.65f},new[]{.53f,1.4f,1.52f,1.65f},new[]{.2f,.38f,.46f,.6f}};
            if(candidates)
            {
                names=new[]{"roll_attack_01","roll_attack_02","back_step_attack_01","back_step_attack_02_release","guard_counter_01"};
                times=names.Select(n=>new[]{.15f,.35f,.55f,.75f}.Select(t=>t*clipStates[n].length).ToArray()).ToArray();
            }
            int size = 320;
            sheet = new Texture2D(size*4,size*names.Length,TextureFormat.RGB24,false);
            for(int row=0;row<names.Length;row++)
            {
                for(int col=0;col<4;col++)
                {
                    sample(names[row],times[row][col]);
                    preview.camera.transform.position = new Vector3(5f,3.7f,6f);
                    preview.camera.transform.LookAt(new Vector3(0,1.4f,0));
                    preview.BeginStaticPreview(new Rect(0,0,size,size));
                    preview.Render(true,true);
                    var tex = preview.EndStaticPreview();
                    sheet.SetPixels(col*size,(names.Length-row-1)*size,size,size,tex.GetPixels());
                    UnityEngine.Object.DestroyImmediate(tex);
                    log.AppendLine(names[row]+"@"+times[row][col]+" R="+right.position.ToString("F3")+" L="+left.position.ToString("F3")+" tip="+weapon.transform.TransformPoint(new Vector3(0,2.10623f,0)).ToString("F3"));
                }
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(output,candidates ? "candidate-contact.png" : sourceModel ? "source-contact.png" : "animation-contact.png"),sheet.EncodeToPNG());
            File.WriteAllText(Path.Combine(output,"animation-contact.txt"), log.ToString());
            return output + "\n" + log;
        }
        finally
        {
            if(sheet != null) UnityEngine.Object.DestroyImmediate(sheet);
            preview.Cleanup();
            if(controller != null) UnityEngine.Object.DestroyImmediate(controller);
            if(correctedAvatar != null) UnityEngine.Object.DestroyImmediate(correctedAvatar);
        }
    }
}
