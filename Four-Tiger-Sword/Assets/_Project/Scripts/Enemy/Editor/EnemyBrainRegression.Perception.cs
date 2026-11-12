using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static partial class EnemyBrainRegression
{
    private static async Task ExercisePerceptionAndGroupCombat(RuntimeFixture fixture)
    {
        var observer = new GameObject("Independent vision observer");
        SceneManager.MoveGameObjectToScene(observer, fixture.Scene);
        Vector3 origin = fixture.Origin + new Vector3(-15f, 0f, -15f);
        observer.transform.position = origin;
        var sight = observer.AddComponent<FanVisionSensor>();
        Set(sight, "_player", fixture.Target);
        Set(sight, "_range", 12f);
        Set(sight, "_proximityRange", 2f);

        fixture.Target.position = origin + Vector3.back * 8f;
        Physics.SyncTransforms();
        Check(!sight.DetectTarget() && sight.DetectedTarget == null,
            "An unaware enemy must not acquire a distant player behind its initial vision cone.");
        fixture.Target.position = origin + Vector3.back;
        Physics.SyncTransforms();
        Check(sight.DetectTarget(), "Nearby rear players must still be detected by proximity.");
        sight.enabled = false;
        Check(sight.DetectedTarget == null && !sight.DetectTarget(),
            "Disabling vision must clear its visible target and suppress detection.");
        sight.enabled = true;
        fixture.Target.position = origin + Vector3.back * 8f;
        Physics.SyncTransforms();
        Check(!sight.DetectTarget(), "Re-enabling a sensor must not restore its previous tracking lock.");
        fixture.Target.position = origin + Vector3.forward * 8f;
        Physics.SyncTransforms();
        Check(sight.DetectTarget(), "A clear player in front must be acquired.");
        fixture.Target.position = origin + Vector3.back * 8f;
        Physics.SyncTransforms();
        Check(sight.DetectTarget(), "A visible tracked player must not disappear when crossing behind the enemy.");
        fixture.Target.position = origin + Vector3.forward * 13f;
        Physics.SyncTransforms();
        Check(!sight.DetectTarget() && sight.DetectedTarget == null,
            "Tracking must still enforce the sensor's maximum range.");

        var ally = fixture.Actor<Enemy>("Vision-blocking ally", fixture.SkillSet(), out _);
        ally.CombatLabPassive = true;
        ally.GetComponent<NavMeshAgent>().Warp(origin + Vector3.forward * 4f);
        var child = new GameObject("Ally child hit collider");
        child.transform.SetParent(ally.transform, false);
        child.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
        fixture.Target.position = origin + Vector3.forward * 8f;
        Physics.SyncTransforms();
        Check(sight.DetectTarget(), "An ally's root and child colliders must not hide the player behind it.");
        for (int i = 0; i < 36; i++)
            child.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
        Physics.SyncTransforms();
        Check(sight.DetectTarget(), "Allied colliders filling the visibility buffer must not hide a clear player.");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Opaque wall behind ally";
        SceneManager.MoveGameObjectToScene(wall, fixture.Scene);
        wall.transform.position = origin + Vector3.forward * 6f + Vector3.up * .5f;
        wall.transform.localScale = new Vector3(3f, 3f, .5f);
        Physics.SyncTransforms();
        Check(!sight.DetectTarget() && sight.DetectedTarget == null,
            "A full buffer of allied colliders must not hide an opaque wall farther along the sight ray.");
        observer.SetActive(false);
        ally.gameObject.SetActive(false);
        wall.SetActive(false);

        var alerted = fixture.Actor<Enemy>("Entry-cue vision observer", fixture.SkillSet(), out _);
        alerted.CombatLabPassive = true;
        alerted.GetComponent<NavMeshAgent>().Warp(fixture.Origin + new Vector3(15f, 0f, -12f));
        await Until(() => alerted.Brain != null, "Entry-cue vision fixture did not initialize.");
        var alertedSight = BindRealVision(alerted, fixture.Target);
        fixture.Target.position = alerted.transform.position + Vector3.back * 6f;
        var cueWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cueWall.name = "Entry-cue occluding wall";
        SceneManager.MoveGameObjectToScene(cueWall, fixture.Scene);
        cueWall.transform.position = alerted.transform.position + Vector3.back * 3f + Vector3.up * .5f;
        cueWall.transform.localScale = new Vector3(3f, 3f, .5f);
        alerted.RememberEncounterPosition(fixture.Target.position);
        Physics.SyncTransforms();
        Check(alerted.HasTargetMemory && !alertedSight.DetectTarget() && alerted.DetectedTarget == null,
            "An encounter cue must not grant a visible target through an opaque wall.");
        cueWall.SetActive(false);
        Physics.SyncTransforms();
        Check(alertedSight.DetectTarget() && alerted.DetectedTarget == fixture.Target,
            "An alerted enemy must acquire a clear player behind it while its target memory is active.");
        alertedSight.enabled = false;
        alertedSight.enabled = true;
        Set(alerted, "_targetMemoryExpiresAt", float.NegativeInfinity);
        Check(!alertedSight.DetectTarget(), "Expired target memory must restore initial rear-vision limits.");
        alerted.gameObject.SetActive(false);

        var skill = fixture.New<EnemyBrainProbeSkill>();
        skill.executeRange = 4f;
        skill.engageRange = 6f;
        var set = fixture.SkillSet(skill);
        var group = new Enemy[3];
        for (int i = 0; i < group.Length; i++)
        {
            var enemy = fixture.Actor<Enemy>("Group combat " + i, set, out _);
            enemy.CombatLabPassive = true;
            enemy.GetComponent<NavMeshAgent>().Warp(fixture.Origin + Vector3.forward * (i * 1.2f));
            group[i] = enemy;
        }
        await Until(() => group[0].Brain != null && group[1].Brain != null && group[2].Brain != null,
            "Group combat fixtures did not initialize.");
        fixture.Target.position = fixture.Origin + Vector3.forward * 3.7f;
        foreach (var enemy in group) BindRealVision(enemy, fixture.Target);
        Physics.SyncTransforms();
        foreach (var enemy in group) enemy.CombatLabPassive = false;
        await Until(() => group[0].CurrentAction?.IsActive == true
            && group[1].CurrentAction?.IsActive == true && group[2].CurrentAction?.IsActive == true,
            "Three aligned normal enemies with real vision must be able to attack concurrently.");
        Check(skill.Actions.Count == 3 && group[0].CurrentAction != group[1].CurrentAction
            && group[1].CurrentAction != group[2].CurrentAction,
            "Group combat must create one independent attack action per enemy.");
        foreach (var enemy in group)
        {
            Check(enemy.DetectedTarget == fixture.Target && enemy.stateName == nameof(EnemyAttackState),
                "Every group member must independently see and attack the player.");
            enemy.CombatLabPassive = true;
        }
    }

    private static FanVisionSensor BindRealVision(Enemy enemy, Transform target)
    {
        var sight = enemy.gameObject.AddComponent<FanVisionSensor>();
        Set(sight, "_player", target);
        Set(enemy, "_sensor", sight);
        return sight;
    }
}
