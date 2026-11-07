// Edit Mode: Unity Pipeline run_script, KnockbackRegression.Run.
using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class KnockbackRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class ActionProbe : EnemyAction
    {
        public ActionProbe(MonsterSkillData data, Enemy enemy) : base(data, enemy) { }
        public void BeginRecovery() => EndNormalAttackArmor();
    }
    public static string Run()
    {
        if (Application.isPlaying) throw new Exception("Edit Mode required.");
        int checks = 0;
        Action<bool, string> check = (ok, message) => { if (!ok) throw new Exception(message); checks++; };
        foreach (float duration in new[] { 0.01f, 0.25f, 1f })
        foreach (int fps in new[] { 5, 15, 30, 60, 144 })
        {
            Vector3 total = Vector3.zero;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                total += KnockbackMotion.Displacement(Vector3.right * 8f, elapsed, 1f / fps, duration);
                elapsed += 1f / fps;
            }
            check(Mathf.Abs(total.x - 4f * duration) < 0.0001f, "FPS-dependent distance.");
        }
        check(KnockbackMotion.Velocity(Vector3.right * 10f, 2f) == Vector3.right * 8f, "Resistance subtraction.");
        check(KnockbackMotion.Velocity(Vector3.right, 2f) == Vector3.zero, "Resistance threshold.");
        check(KnockbackMotion.Velocity(Vector3.up * 10f, 0f) == Vector3.zero, "Vertical knockback.");
        check(KnockbackMotion.Velocity(new Vector3(float.NaN, 0, 0), 0f) == Vector3.zero, "NaN power.");
        check(KnockbackMotion.Power(Vector3.forward, -1f) == Vector3.zero, "Negative speed.");
        check(KnockbackMotion.Displacement(Vector3.one, 0, 1, 0) == Vector3.zero, "Zero duration.");
        check(KnockbackMotion.Displacement(Vector3.one, 0, 1, float.PositiveInfinity) == Vector3.zero, "Infinite duration.");

        foreach (float duration in new[] { 0.01f, 0.16f, 0.25f })
        foreach (int fps in new[] { 5, 15, 30, 60, 144 })
        {
            Vector3 total = Vector3.zero;
            for (float time = 0f; time < duration + 0.1f; time += 1f / fps)
                total += KnockbackMotion.ImpactDisplacement(Vector3.right * 8f, time, 1f / fps, duration);
            check(Mathf.Abs(total.x - 8f * duration / 3f) < 0.0001f, "Impact distance depends on FPS.");
        }
        float impactTotal = KnockbackMotion.ImpactDisplacement(Vector3.right * 8f, 0f, 1f, 0.16f).x;
        check(KnockbackMotion.ImpactDisplacement(Vector3.right * 8f, 0f, 0.08f, 0.16f).x > impactTotal * 0.85f,
            "Impact retains too much late sliding.");
        check(KnockbackMotion.ImpactDisplacement(Vector3.right * 8f, 0.16f, 0.5f, 0.16f) == Vector3.zero,
            "Impact continued moving during recovery.");
        check(KnockbackMotion.ImpactDisplacement(Vector3.one, 0f, 1f, 0f) == Vector3.zero, "Zero impact duration moved.");
        check(KnockbackMotion.ImpactDisplacement(Vector3.one, 0f, 1f, float.NaN) == Vector3.zero, "NaN impact duration moved.");

        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        MonsterDefaultAttackSO skill = null;
        EnemyData data = null;
        try
        {
            // Edit Mode does not simulate NavMeshAgents. Exercise state ownership separately
            // from live navigation, and test the no-navigation safety path end to end.
            var go = new GameObject("KnockbackTest");
            SceneManager.MoveGameObjectToScene(go, scene);
            var animator = go.AddComponent<Animator>();
            var enemy = go.AddComponent<Enemy>();
            enemy.EnemyStat = go.GetComponent<EnemyStat>();
            var authoredData = AssetDatabase.LoadAssetAtPath<EnemyData>("Assets/_Project/EnemyStats/Drone.asset");
            check(authoredData != null, "Default Drone stat data is missing.");
            data = UnityEngine.Object.Instantiate(authoredData);
            typeof(EnemyStat).GetField("baseData", Hidden).SetValue(enemy.EnemyStat, data);
            typeof(EnemyStat).GetMethod("InitializeFromData", Hidden).Invoke(enemy.EnemyStat, null);
            enemy.GetComponent<PoiseHandler>().ResetPoise();
            check(enemy.EnemyStat.Data == data && enemy.EnemyStat.CurrentHp == data.maxHp
                && enemy.KnockbackMoveTime == data.knockbackMoveTime
                && enemy.KnockbackMaxDistance == data.knockbackMaxDistance,
                "Enemy knockback settings must come from its actual stat data.");
            typeof(Enemy).GetProperty("Animator").SetValue(enemy, animator);
            var clock = CombatActorClock.GetOrCreate(enemy);
            typeof(CombatActorClock).GetMethod("Awake", Hidden).Invoke(clock, null);
            typeof(Enemy).GetProperty("ActorClock").SetValue(enemy, clock);
            var stop = typeof(Enemy).GetMethod("StopKnockback", Hidden);
            var begin = typeof(Enemy).GetMethod("BeginKnockback", Hidden);
            var gate = typeof(Enemy).GetMethod("CanReceiveKnockback", Hidden);
            var tick = typeof(Enemy).GetMethod("UpdateKnockback", Hidden);
            Func<int> locks = () => (int)typeof(Enemy).GetField("_moveLockCount", Hidden).GetValue(enemy);
            Action start = () => begin.Invoke(enemy,
                new object[] { Vector3.right * 8f, enemy.KnockbackMoveTime, enemy.KnockbackMaxDistance });
            Action stopMotion = () => stop.Invoke(enemy, new object[] { "regression" });
            Action<StaggerResistLevel> hit = level => enemy.TakeDamage(1f, power: Vector3.right * 10f,
                poiseDamage: 0f, staggerResistLevel: level);
            foreach (StaggerResistLevel resistance in Enum.GetValues(typeof(StaggerResistLevel)))
            foreach (StaggerResistLevel level in Enum.GetValues(typeof(StaggerResistLevel)))
            {
                enemy.GetComponent<PoiseHandler>().ResetPoise();
                enemy.IsHurt = enemy.IsGroggy = enemy.PendingGroggy = false;
                enemy.StaggerResistLevel = resistance;
                float hp = enemy.EnemyStat.CurrentHp;
                hit(level);
                bool expected = resistance != StaggerResistLevel.SUPER_ARMOR && level >= resistance;
                check((bool)gate.Invoke(enemy, new object[] { level }) == expected && enemy.EnemyStat.CurrentHp == hp - 1f,
                    "Enemy resistance/damage mismatch: " + level + "/" + resistance);
            }
            enemy.StaggerResistLevel = StaggerResistLevel.NONE;
            enemy.LockMovement(); // Another owner must retain its lock after knockback.
            animator.applyRootMotion = true;
            start();
            check(enemy.IsKnockbacking && !animator.applyRootMotion && !enemy.CanAttack, "Movement ownership missing.");
            check(locks() == 2, "Knockback lock missing.");
            check((float)typeof(Enemy).GetField("_knockbackMoveDuration", Hidden).GetValue(enemy) <= 0.16f,
                "Monster movement lasts beyond impact window.");
            start();
            check(locks() == 2, "Repeated hit leaked a lock.");
            float originalMoveTime = data.knockbackMoveTime;
            data.knockbackMoveTime = 10f;
            check((float)typeof(Enemy).GetField("_knockbackMoveDuration", Hidden).GetValue(enemy) == originalMoveTime,
                "Duration did not remain a snapshot.");
            data.knockbackMoveTime = originalMoveTime;
            stopMotion();
            check(!enemy.IsKnockbacking && locks() == 1 && animator.applyRootMotion,
                "External lock/root motion restoration failed.");
            stopMotion();
            check(locks() == 1, "Double stop released another owner's lock.");
            enemy.UnlockMovement();
            begin.Invoke(enemy, new object[] { Vector3.right * 1000f, enemy.KnockbackMoveTime, enemy.KnockbackMaxDistance });
            var cappedVelocity = (Vector3)typeof(Enemy).GetField("_knockbackVelocity", Hidden).GetValue(enemy);
            float moveTime = (float)typeof(Enemy).GetField("_knockbackMoveDuration", Hidden).GetValue(enemy);
            check(KnockbackMotion.ImpactDisplacement(cappedVelocity, 0f, 1f, moveTime).magnitude <= enemy.KnockbackMaxDistance + 0.001f,
                "Heavy hit exceeded distance cap.");
            stopMotion();

            // Missing Hit controllers freeze locomotion, re-hits refresh recovery, Exit restores playback.
            clock.SetAnimationSpeed(0.75f);
            var hurt = new EnemyHurtState(enemy);
            hurt.Enter();
            check(animator.speed == 0f && locks() == 1, "Missing-hit fallback kept walking or failed to lock.");
            typeof(EnemyHurtState).GetField("_remaining", Hidden).SetValue(hurt, 0.01f);
            uint revision = enemy.HurtVersion;
            hit(StaggerResistLevel.HIGH);
            hurt.Update();
            check(enemy.HurtVersion == revision + 1
                && (float)typeof(EnemyHurtState).GetField("_remaining", Hidden).GetValue(hurt) > 0.1f,
                "Repeated hit did not refresh recovery.");
            hurt.Exit();
            check(animator.speed == 0.75f && locks() == 0, "Reaction fallback leaked animator speed/lock.");
            enemy.StaggerResistLevel = StaggerResistLevel.SUPER_ARMOR;
            revision = enemy.HurtVersion;
            hit(StaggerResistLevel.HIGH);
            check(enemy.HurtVersion == revision && !enemy.IsHurt, "Super armor entered a hit animation.");
            enemy.StaggerResistLevel = StaggerResistLevel.NONE;
            start(); go.SetActive(false);
            // Non-ExecuteAlways behaviours do not receive normal lifecycle callbacks in Edit Mode.
            typeof(Enemy).GetMethod("OnDisable", Hidden).Invoke(enemy, null);
            check(!enemy.IsKnockbacking && locks() == 0, "Disable leaked knockback state.");
            go.SetActive(true); start();
            Vector3 before = go.transform.position; tick.Invoke(enemy, new object[] { 0.1f });
            check(!enemy.IsKnockbacking && locks() == 0 && go.transform.position == before, "Navigation loss teleported or leaked lock.");
            hit(StaggerResistLevel.HIGH);
            check(!enemy.IsKnockbacking && go.transform.position == before, "Off-navmesh fallback moved transform.");
            skill = ScriptableObject.CreateInstance<MonsterDefaultAttackSO>();
            skill.isParryable = false;
            enemy.IsHurt = enemy.IsGroggy = enemy.PendingGroggy = false;
            var action = new ActionProbe(skill, enemy);
            enemy.CurrentAction = action; action.Enter();
            check(action.IsActive && action.HasNormalAttackArmor, "An accepted normal attack did not gain armor.");
            float armoredHp = enemy.EnemyStat.CurrentHp;
            revision = enemy.HurtVersion;
            hit(StaggerResistLevel.HIGH);
            check(action.IsActive && action.HasNormalAttackArmor && !enemy.IsHurt && !enemy.IsKnockbacking
                && enemy.HurtVersion == revision && enemy.EnemyStat.CurrentHp == armoredHp - 1f
                && !(bool)gate.Invoke(enemy, new object[] { StaggerResistLevel.HIGH }),
                "Normal attack armor must retain damage while resisting ordinary stagger and knockback.");
            action.BeginRecovery();
            hit(StaggerResistLevel.HIGH);
            check(!action.IsActive && !action.HasNormalAttackArmor && enemy.IsHurt
                && enemy.HurtVersion == revision + 1 && !enemy.IsKnockbacking,
                "Unarmored recovery must be interrupted while the off-navmesh safety guard prevents movement.");
            enemy.IsHurt = false;
            enemy.GetComponent<PoiseHandler>().ResetPoise();
            action = new ActionProbe(skill, enemy);
            enemy.CurrentAction = action; action.Enter();
            enemy.GetComponent<PoiseHandler>().ForceBreak();
            check(enemy.PendingGroggy && !action.IsActive && !action.HasNormalAttackArmor && enemy.CurrentAction == null,
                "A real poise break must cancel a normal armored action immediately.");
            enemy.PendingGroggy = false;
            enemy.GetComponent<PoiseHandler>().ResetPoise();
            start();
            enemy.TakeDamage(10000f, power: Vector3.right * 10f, poiseDamage: 0f, staggerResistLevel: StaggerResistLevel.HIGH);
            check(!enemy.IsKnockbacking && locks() == 0 && enemy.EnemyStat.IsDead, "Death leaked knockback state.");
            return checks + " knockback math / reaction / lifecycle checks passed (live NavMesh motion not simulated).";
        }
        finally
        {
            SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(scene, true);
            if (skill != null) UnityEngine.Object.DestroyImmediate(skill);
            if (data != null) UnityEngine.Object.DestroyImmediate(data);
        }
    }
}
