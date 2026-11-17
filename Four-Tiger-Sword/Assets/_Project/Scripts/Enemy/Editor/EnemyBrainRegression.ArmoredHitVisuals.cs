using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static partial class EnemyBrainRegression
{
    private static async Task ExerciseArmoredHitVisuals(RuntimeFixture fixture)
    {
        var skill = fixture.New<EnemyBrainProbeSkill>();
        skill.superArmor = true;
        var enemy = fixture.Actor<Enemy>("Armored hit visual", fixture.SkillSet(skill), out var sensor);
        await Until(() => enemy.Brain != null, "Armored hit visual fixture did not initialize.");

        var target = fixture.Target;
        Vector3 originalTarget = target.position;
        try
        {
            target.position = enemy.transform.position + enemy.transform.forward;
            sensor.Target = target;
            await Until(() => enemy.CurrentAction?.HasNormalAttackArmor == true,
                "Armored hit visual fixture never started its attack.");
            var attack = enemy.CurrentAction;
            var generic = enemy.Animator;
            enemy.SetAnimationSpeed(0f);
            await Frames(2);

            var genericPose = generic.transform.localRotation;
            Vector3 worldPosition = enemy.transform.position;
            Quaternion worldRotation = enemy.transform.rotation;
            int baseState = generic.GetCurrentAnimatorStateInfo(0).fullPathHash;
            var result = enemy.TakeDamage(1f, power: Vector3.right * 5f, poiseDamage: 0f);
            var reaction = enemy.GetComponent<EnemyArmoredHitReaction>();
            Check(result.Applied && !result.CausedOrdinaryStagger && reaction != null && reaction.IsReacting,
                "Armored damage did not start a visual recoil on the generic rig.");
            await WaitForRecoil(reaction, generic.transform, genericPose,
                "Generic visual root never recoiled.");
            Check(enemy.CurrentAction == attack && attack.IsActive && !enemy.IsHurt && !enemy.IsKnockbacking
                && enemy.transform.position == worldPosition && enemy.transform.rotation == worldRotation
                && generic.GetCurrentAnimatorStateInfo(0).fullPathHash == baseState,
                "The recoil changed the action, base animation, or navigation/collision root.");

            Quaternion pausedPose = generic.transform.localRotation;
            enemy.ActorClock.Request(enemy, 1f, 0f, HitStopStacking.Replace);
            await Frames(3);
            await Awaitable.EndOfFrameAsync();
            Check(reaction.IsReacting && Quaternion.Angle(pausedPose, generic.transform.localRotation) < .1f,
                "Actor hitstop did not freeze the recoil pose.");
            enemy.ActorClock.ClearStops();

            const string RigPath = "Assets/_Project/Animations/BlackSlayer/Monster_Black Slayer_model_Idle.fbx";
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            Check(rig != null, "The humanoid enemy rig is missing.");
            var replacement = Object.Instantiate(rig, enemy.transform).GetComponentInChildren<Animator>();
            Check(replacement != null && replacement.isHuman && replacement.avatar.isValid,
                "The humanoid enemy rig has no valid avatar.");
            replacement.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/_Project/Animations/AnimController/BlackSlayer.controller");
            enemy.BindAnimator(replacement);
            Check(!reaction.IsReacting && Quaternion.Angle(genericPose, generic.transform.localRotation) < .1f,
                "Rig rebind left the previous model recoiling.");
            generic.gameObject.SetActive(false);
            await Frames(2);

            var torso = replacement.GetBoneTransform(HumanBodyBones.Chest)
                ?? replacement.GetBoneTransform(HumanBodyBones.Spine);
            Check(torso != null, "The humanoid rig has no torso bone.");
            Quaternion torsoPose = torso.localRotation;
            Quaternion visualRootPose = replacement.transform.localRotation;
            enemy.TakeDamage(1f, isCritical: true, poiseDamage: 0f);
            await WaitForRecoil(reaction, torso, torsoPose,
                "The humanoid torso never recoiled over the attack pose.");
            Check(Quaternion.Angle(visualRootPose, replacement.transform.localRotation) < .1f,
                "The humanoid reaction tilted the visual root instead of the torso.");
            await Until(() => !reaction.IsReacting, "The humanoid hit reaction did not finish.");
            Check(Quaternion.Angle(torsoPose, torso.localRotation) < .1f && enemy.CurrentAction == attack,
                "The torso did not return to its animated pose after the recoil.");

            enemy.TakeDamage(1f, poiseDamage: 0f);
            await WaitForRecoil(reaction, torso, torsoPose,
                "The humanoid reaction failed to replay.");
            attack.OnParried();
            Check(!reaction.IsReacting && Quaternion.Angle(torsoPose, torso.localRotation) < .1f,
                "Losing armor to a parry left a recoil applied.");

            await Until(() => enemy.CurrentAction != attack && enemy.CurrentAction?.HasNormalAttackArmor == true,
                "Armored hit visual fixture did not start a follow-up attack.");
            enemy.SetAnimationSpeed(0f);
            await Frames(2);
            torsoPose = torso.localRotation;
            enemy.TakeDamage(1f, poiseDamage: 0f);
            await WaitForRecoil(reaction, torso, torsoPose,
                "The follow-up attack did not react to armored damage.");
            enemy.enabled = false;
            Check(!reaction.IsReacting && Quaternion.Angle(torsoPose, torso.localRotation) < .1f,
                "Disabling only the enemy component left a recoil applied.");
        }
        finally
        {
            enemy.ActorClock.ClearStops();
            target.position = originalTarget;
            enemy.gameObject.SetActive(false);
        }
    }

    private static async Task WaitForRecoil(EnemyArmoredHitReaction reaction, Transform bone,
        Quaternion rest, string message)
    {
        bool moved;
        do
        {
            await Frames(1);
            await Awaitable.EndOfFrameAsync();
            moved = Quaternion.Angle(rest, bone.localRotation) > 1f;
        } while (!moved && reaction.IsReacting);
        Check(moved, message);
    }
}
