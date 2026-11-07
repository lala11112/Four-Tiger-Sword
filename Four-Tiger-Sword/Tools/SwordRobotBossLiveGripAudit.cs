using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;

public static class SwordRobotBossLiveGripAudit
{
    public static string Main()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Play mode required.");
        GameObject root=null;
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Generated/Bosses/SwordRobotBoss/SwordRobotBoss.prefab");
            root=UnityEngine.Object.Instantiate(prefab,new Vector3(18000,0,18000),Quaternion.identity);
            foreach(var script in root.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled=script is SwordRobotBossWeaponGrip || script is Boss;
            root.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled=false;
            var boss=root.GetComponent<Boss>();boss.CombatLabPassive=true;boss.SetCombatTarget(null);
            var animator=root.GetComponentInChildren<Animator>();var grip=animator.GetComponent<SwordRobotBossWeaponGrip>();grip.owner=boss;
            animator.enabled=true;animator.speed=1;animator.Rebind();animator.Update(0);
            var controller=animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
            string[] names={"Idle","Basic","Combo2","Combo3","Charge","Dash","Sweep"};
            float[] times={.3f,.54f,.54f,.58f,.53f,.46f,.45f};
            var log=new StringBuilder();log.AppendLine("active="+boss.isActiveAndEnabled+" playingObj="+Application.IsPlaying(grip.gameObject)+" enabled="+grip.enabled);
            for(int i=0;i<names.Length;i++)
            {
                var clip=controller.layers[0].stateMachine.states.First(s=>s.state.name==names[i]).state.motion as AnimationClip;
                animator.Play(names[i],0,times[i]/clip.length);animator.Update(0);
                log.AppendLine(names[i]+" leftGripError="+Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftHand).position,grip.leftHandGrip.position)+" right="+(animator.GetBoneTransform(HumanBodyBones.RightHand).position-root.transform.position));
            }
            return log.ToString();
        }
        finally{if(root!=null)UnityEngine.Object.DestroyImmediate(root);}
    }
}
