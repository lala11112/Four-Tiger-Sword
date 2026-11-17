using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>Ordinary-enemy tactics: windup aim, cooldown strafing, group surround, and attack selection rhythm.</summary>
public static partial class EnemyBrainRegression
{
    /// <summary>Turns off every tactic added after the original contracts were written.</summary>
    private static NormalEnemyAISettings PinLegacyTactics(NormalEnemyAISettings settings)
    {
        settings.strafeEnabled = false;
        settings.surroundEnabled = false;
        settings.attackCooldownJitter = 0f;
        settings.punishCooldownCut = 0f;
        return settings;
    }

    private static void RestoreTactics(NormalEnemyAISettings target, NormalEnemyAISettings source)
    {
        target.strafeEnabled = source.strafeEnabled;
        target.surroundEnabled = source.surroundEnabled;
        target.attackCooldownJitter = source.attackCooldownJitter;
        target.punishCooldownCut = source.punishCooldownCut;
    }

    private static async Task ExerciseTactics(RuntimeFixture f)
    {
        await ExerciseWindupAim(f);
        await ExerciseCooldownStrafe(f);
        await ExerciseSurround(f);
        await ExerciseCooldownJitter(f);
        ExerciseSightOcclusion(f);
        await ExerciseDetectionTurn(f);
    }

    private static void ExerciseSightOcclusion(RuntimeFixture f)
    {
        var observer = new GameObject("Occlusion observer");
        SceneManager.MoveGameObjectToScene(observer, f.Scene);
        Vector3 origin = f.Origin + new Vector3(25f, 0f, 25f);
        observer.transform.position = origin;
        var sight = observer.AddComponent<FanVisionSensor>();
        Set(sight, "_player", f.Target);
        Set(sight, "_range", 12f);
        Set(sight, "_proximityRange", 0f);
        f.Target.position = origin + Vector3.forward * 8f;

        GameObject Block(string name, int layer, float height)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.layer = layer;
            SceneManager.MoveGameObjectToScene(block, f.Scene);
            block.transform.position = origin + Vector3.forward * 4f + Vector3.up * (height * .5f);
            block.transform.localScale = new Vector3(3f, height, .5f);
            Physics.SyncTransforms();
            return block;
        }

        var skillEffect = Block("Player skill effect", LayerMask.NameToLayer("PlayerSkill"), 3f);
        Check(sight.DetectTarget(), "A player skill effect hid the player from an enemy.");
        skillEffect.SetActive(false);
        var lowProp = Block("Low prop", 0, .9f);
        Check(sight.DetectTarget(), "A knee-high prop hid a standing player from an enemy.");
        lowProp.SetActive(false);
        var wall = Block("Full wall", 0, 3f);
        Check(!sight.DetectTarget(), "A full wall no longer hides the player.");
        wall.SetActive(false);
        observer.SetActive(false);
        Physics.SyncTransforms();
    }

    private static async Task ExerciseDetectionTurn(RuntimeFixture f)
    {
        var skill = f.New<EnemyBrainProbeSkill>();
        var skills = f.SkillSet(skill);
        var group = TacticsGroup(f, "Detection turn", new NormalEnemyAISettings { strafeEnabled = false, surroundEnabled = false });
        Vector3 center = f.Origin + new Vector3(-25f, 0f, 25f);
        f.Target.position = center + Vector3.forward * 8f;
        var enemy = GroupActor(f, group, "Detection turner", center, skills, out var sensor, null, 3f);
        await Until(() => enemy.Brain != null, "Detection-turn fixture did not initialize.");
        enemy.transform.rotation = Quaternion.LookRotation(Vector3.back);
        float started = Time.time;
        sensor.Target = f.Target;
        await Until(() => enemy.stateName == nameof(EnemyTraceState)
                && Vector3.Angle(Vector3.ProjectOnPlane(enemy.transform.forward, Vector3.up),
                    Vector3.ProjectOnPlane(f.Target.position - enemy.transform.position, Vector3.up)) < 20f,
            "An enemy that spotted the player behind it never turned to face the player.");
        Check(Time.time - started < .75f, "An enemy turned toward a newly spotted player too slowly.");
        enemy.CombatLabPassive = true;
        enemy.gameObject.SetActive(false);
    }

    private static EnemyCombatGroup TacticsGroup(RuntimeFixture f, string name, NormalEnemyAISettings settings)
    {
        var root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, f.Scene);
        var group = root.AddComponent<EnemyCombatGroup>();
        group.Initialize(settings);
        return group;
    }

    private static EnemyData TacticsData(RuntimeFixture f, MonsterSkillDataSO skills, float attackCooldown)
    {
        var data = f.New<EnemyData>();
        data.maxHp = 1000; data.baseAtk = 1f; data.combatRange = 12f; data.combatExitRange = 20f;
        data.attackCooldown = attackCooldown; data.monsterSkillData = skills;
        return data;
    }

    private static int PlayingState(Animator animator) => animator.IsInTransition(0)
        ? animator.GetNextAnimatorStateInfo(0).shortNameHash
        : animator.GetCurrentAnimatorStateInfo(0).shortNameHash;

    private static float GroundAngle(Vector3 position, Vector3 center)
    {
        Vector3 offset = position - center;
        return Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
    }

    private static async Task ExerciseWindupAim(RuntimeFixture f)
    {
        var attack = f.New<MonsterDefaultAttackSO>();
        attack.animName = "Attack";
        attack.hitStartTime = .8f;
        attack.hitDuration = .1f;
        attack.attackDuration = 1.2f;
        attack.telegraphDuration = .8f;
        attack.knockbackForce = 0f;
        attack.trackTurnSpeed = 360f;
        attack.trackLockFraction = .5f;
        var group = TacticsGroup(f, "Windup aim", new NormalEnemyAISettings { strafeEnabled = false, surroundEnabled = false });
        Vector3 center = f.Origin;
        f.Target.position = center + Vector3.forward * 1.5f;
        var enemy = GroupActor(f, group, "Windup aimer", center, f.SkillSet(attack), out var sensor);
        await Until(() => enemy.Brain != null, "Windup aim fixture did not initialize.");
        sensor.Target = f.Target;
        await Until(() => enemy.CurrentAction?.IsActive == true, "Windup aim fixture never started its attack.");
        var action = enemy.CurrentAction;
        Check(action.AttackTimeline < attack.hitStartTime * attack.trackLockFraction,
            "Windup aim fixture started too late to sidestep before the lock.");

        // A sidestep during the early windup is followed.
        f.Target.position = enemy.transform.position + Vector3.right * 1.5f;
        float lockTime = attack.hitStartTime * attack.trackLockFraction;
        await Until(() => !action.IsActive || action.AttackTimeline >= lockTime, "Windup never reached its aim lock.");
        Check(action.IsActive && Vector3.Angle(Vector3.ProjectOnPlane(enemy.transform.forward, Vector3.up), Vector3.right) < 20f,
            "A normal enemy did not track a sidestep made before its aim lock.");

        // After the lock the swing is committed, so a late dodge still works.
        Quaternion locked = enemy.transform.rotation;
        f.Target.position = enemy.transform.position + Vector3.left * 1.5f;
        await Until(() => !action.IsActive || action.AttackTimeline >= attack.hitStartTime, "Windup never reached its hit.");
        Check(Quaternion.Angle(enemy.transform.rotation, locked) < 1f,
            "A normal enemy kept tracking after its aim lock, making late dodges impossible.");
        enemy.CombatLabPassive = true;
        enemy.gameObject.SetActive(false);
    }

    private static async Task ExerciseCooldownStrafe(RuntimeFixture f)
    {
        var skill = f.New<EnemyBrainProbeSkill>();
        skill.preferredRange = 3f; skill.executeRange = 3f; skill.engageRange = 6f;
        var skills = f.SkillSet(skill);
        var settings = new NormalEnemyAISettings { surroundEnabled = false, feintChance = 0f, punishCooldownCut = 0f };
        var group = TacticsGroup(f, "Cooldown strafe", settings);
        Vector3 center = f.Origin + Vector3.right * 20f;
        f.Target.position = center;
        var enemy = GroupActor(f, group, "Strafer", center + Vector3.back * 3f, skills, out var sensor,
            TacticsData(f, skills, 100f), 3f);
        await Until(() => enemy.Brain != null, "Strafe fixture did not initialize.");
        enemy.StartAttackCooldown();
        sensor.Target = f.Target;
        await Until(() => enemy.stateName == nameof(EnemyCombatIdleState), "Strafe fixture never entered combat idle.");
        float startAngle = GroundAngle(enemy.transform.position, center);
        await Until(() => Mathf.Abs(Mathf.DeltaAngle(startAngle, GroundAngle(enemy.transform.position, center))) > 20f,
            "A normal enemy stood still during its attack cooldown instead of circling the player.");
        float distance = Vector3.ProjectOnPlane(enemy.transform.position - center, Vector3.up).magnitude;
        Check(distance > 1.5f && distance < 4.5f, "Cooldown strafing left the preferred distance band.");
        Check(Vector3.Dot(Vector3.ProjectOnPlane(enemy.transform.forward, Vector3.up).normalized,
                Vector3.ProjectOnPlane(center - enemy.transform.position, Vector3.up).normalized) > .7f,
            "A strafing enemy must keep facing the player.");
        Check(enemy.CurrentAction == null, "Strafing selected an attack during cooldown.");

        // The move animation must follow real motion, even after something else replaced it.
        var animator = enemy.Animator;
        await Until(() => PlayingState(animator) == Animator.StringToHash("Move"),
            "A strafing enemy was not playing its move animation.");
        animator.Play("Hit", 0, 0f);
        await Frames(2);
        await Until(() => PlayingState(animator) == Animator.StringToHash("Move"),
            "A moving enemy whose move animation was replaced never recovered it.");
        enemy.CombatLabPassive = true;
        enemy.gameObject.SetActive(false);
    }

    private static async Task ExerciseSurround(RuntimeFixture f)
    {
        var skill = f.New<EnemyBrainProbeSkill>();
        skill.preferredRange = 2.5f; skill.executeRange = 2.5f; skill.engageRange = 6f;
        var skills = f.SkillSet(skill);
        var settings = new NormalEnemyAISettings
        {
            strafeAngleStep = 45f, strafeSpeedMultiplier = 1f, feintChance = 0f, punishCooldownCut = 0f
        };
        var group = TacticsGroup(f, "Surround", settings);
        Vector3 center = f.Origin + Vector3.left * 20f;
        f.Target.position = center;
        var fighters = new Enemy[3];
        for (int i = 0; i < fighters.Length; i++)
        {
            Vector3 start = center + Quaternion.Euler(0f, -20f + i * 20f, 0f) * Vector3.back * 2.5f;
            fighters[i] = GroupActor(f, group, "Surrounder " + i, start, skills, out var sight,
                TacticsData(f, skills, 100f), 4f);
            fighters[i].GetComponent<NavMeshAgent>().radius = .4f;
            await Until(() => fighters[i].Brain != null, "Surround fixture did not initialize.");
            fighters[i].StartAttackCooldown();
            sight.Target = f.Target;
        }
        await Until(() =>
        {
            for (int i = 0; i < fighters.Length; i++)
                for (int j = i + 1; j < fighters.Length; j++)
                    if (Mathf.Abs(Mathf.DeltaAngle(GroundAngle(fighters[i].transform.position, center),
                            GroundAngle(fighters[j].transform.position, center))) < 60f) return false;
            return true;
        }, "A group that started on one side of the player did not spread around it.");
        foreach (var fighter in fighters)
        {
            fighter.CombatLabPassive = true;
            fighter.gameObject.SetActive(false);
        }
    }

    private static async Task ExerciseCooldownJitter(RuntimeFixture f)
    {
        var skills = f.SkillSet(f.New<EnemyBrainProbeSkill>());
        var group = TacticsGroup(f, "Cooldown jitter",
            new NormalEnemyAISettings { strafeEnabled = false, surroundEnabled = false, attackCooldownJitter = .3f });
        var enemy = GroupActor(f, group, "Jittered cooldown", f.Origin + Vector3.back * 20f, skills, out _,
            TacticsData(f, skills, 10f));
        await Until(() => enemy.Brain != null, "Cooldown jitter fixture did not initialize.");
        Check(AttackCooldownRemaining(enemy) >= 7f - Time.deltaTime - .001f && AttackCooldownRemaining(enemy) <= 13f + .001f,
            "An ordinary enemy must start with a cooldown inside the jitter band.");
        enemy.CombatLabPassive = true;
        var samples = new HashSet<float>();
        for (int i = 0; i < 12; i++)
        {
            enemy.StartAttackCooldown();
            float value = AttackCooldownRemaining(enemy);
            Check(value >= 7f - .001f && value <= 13f + .001f, "Cooldown jitter left its ±30% band.");
            samples.Add(value);
        }
        Check(samples.Count > 1, "Cooldown jitter produced a fixed rhythm.");
        enemy.gameObject.SetActive(false);
    }
}
