// Edit Mode: Unity Pipeline run_script, KnockbackRegression.Run.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class KnockbackRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class ActionProbe : EnemyAction
    {
        public ActionProbe(MonsterSkillData data, Enemy enemy) : base(data, enemy) { }
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

        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        MonsterDefaultAttackSO skill = null;
        try
        {
            // Edit Mode does not simulate NavMeshAgents. Exercise state ownership separately
            // from live navigation, and test the no-navigation safety path end to end.
            var go = new GameObject("KnockbackTest");
            SceneManager.MoveGameObjectToScene(go, scene);
            var animator = go.AddComponent<Animator>();
            var enemy = go.AddComponent<Enemy>();
            enemy.EnemyStat = go.GetComponent<EnemyStat>();
            enemy.EnemyStat.CurrentHp = 1000;
            var values = (Dictionary<EnemyStatType, float>)typeof(EnemyStat).GetField("finalValues", Hidden).GetValue(enemy.EnemyStat);
            values[EnemyStatType.KnockbackResistance] = 2f;
            values[EnemyStatType.KnockbackDuration] = 0.25f;
            typeof(Enemy).GetProperty("Animator").SetValue(enemy, animator);
            var stop = typeof(Enemy).GetMethod("StopKnockback", Hidden);
            var begin = typeof(Enemy).GetMethod("BeginKnockback", Hidden);
            var gate = typeof(Enemy).GetMethod("CanReceiveKnockback", Hidden);
            var tick = typeof(Enemy).GetMethod("UpdateKnockback", Hidden);
            Func<int> locks = () => (int)typeof(Enemy).GetField("_moveLockCount", Hidden).GetValue(enemy);
            Action start = () => begin.Invoke(enemy, new object[] { Vector3.right * 8f, 0.25f });
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
            start();
            check(locks() == 2, "Repeated hit leaked a lock.");
            values[EnemyStatType.KnockbackDuration] = 10f;
            check((float)typeof(Enemy).GetField("_knockbackDurationSnapshot", Hidden).GetValue(enemy) == 0.25f,
                "Duration did not remain a snapshot.");
            stop.Invoke(enemy, null);
            check(!enemy.IsKnockbacking && locks() == 1 && animator.applyRootMotion,
                "External lock/root motion restoration failed.");
            stop.Invoke(enemy, null);
            check(locks() == 1, "Double stop released another owner's lock.");
            enemy.UnlockMovement();
            values[EnemyStatType.KnockbackDuration] = 0.25f;
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
            var action = new ActionProbe(skill, enemy);
            enemy.CurrentAction = action; action.Enter();
            hit(StaggerResistLevel.HIGH);
            check(!action.IsActive, "Manual attack movement was not interrupted.");
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
        }
    }
}
