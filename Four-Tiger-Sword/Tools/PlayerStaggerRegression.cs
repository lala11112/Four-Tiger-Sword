// Run in Edit Mode with Unity Pipeline run_script, entry PlayerStaggerRegression.Run.
using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class PlayerStaggerRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Set(PlayerController player, string name, object value)
        => typeof(PlayerController).GetProperty(name).SetValue(player, value);

    public static string Run()
    {
        Check(!Application.isPlaying, "Run in Edit Mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var go = new GameObject("PlayerStaggerRegression");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = new Vector3(50000f, 50000f, 50000f);
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/_Project/Animations/AnimController/BDJ.controller");
            animator.Rebind();
            animator.Update(0f);
            var player = go.AddComponent<PlayerController>();
            var stats = go.GetComponent<PlayerStatManager>();
            Set(player, "Animator", animator);
            Set(player, "Input", go.GetComponent<PlayerInputHandler>());
            Set(player, "Controller", go.GetComponent<CharacterController>());
            Set(player, "Movement", go.GetComponent<PlayerMovement>());
            Set(player, "StatManager", stats);
            Set(player, "FormManager", new FormManager(player));
            player.Movement.Initialize(player);
            var hp = typeof(PlayerStatManager).GetField("_currentHp", Hidden);
            int checkedPairs = 0;
            foreach (StaggerResistLevel resistance in Enum.GetValues(typeof(StaggerResistLevel)))
            foreach (StaggerResistLevel attack in Enum.GetValues(typeof(StaggerResistLevel)))
            {
                player.ConsumeHitReaction();
                player.HitStaggerResistance = resistance;
                player.VerticalVelocity = 0f;
                Set(player, "StateMachine", new PlayerStateMachineSetup(player).Build());
                hp.SetValue(stats, 100f);
                var result = player.TakeDamage(10f, power: Vector3.right * 10f,
                    poiseDamage: 1000f, staggerResistLevel: attack, isParryable: false);
                Check(result.Applied && stats.CurrentHp == 90f, "Damage lost: " + resistance + "/" + attack);
                bool reacts = attack > resistance && resistance != StaggerResistLevel.SUPER_ARMOR;
                Check(player.PendingHitReaction == (reacts ? PlayerHitReaction.Knockback : PlayerHitReaction.None),
                    "Wrong reaction: " + resistance + "/" + attack);
                player.StateMachine.Update();
                Check((player.StateMachine.CurrentState is PlayerHitState) == reacts,
                    "Wrong state: " + resistance + "/" + attack);
                checkedPairs++;
            }

            player.ConsumeHitReaction();
            player.HitStaggerResistance = StaggerResistLevel.MEDIUM;
            Set(player, "StateMachine", new PlayerStateMachineSetup(player).Build());
            hp.SetValue(stats, 100f);
            player.TakeDamage(10f, power: Vector3.right * 10f, staggerResistLevel: StaggerResistLevel.HIGH);
            player.TakeDamage(10f, power: Vector3.left * 100f, staggerResistLevel: StaggerResistLevel.MEDIUM);
            Check(player.PendingHitReaction == PlayerHitReaction.Knockback && player.ConsumeHitReaction().x > 0f,
                "Resisted hit overwrote a pending strong hit.");

            player.TakeDamage(10f, power: Vector3.zero, poiseDamage: 100f,
                staggerResistLevel: StaggerResistLevel.HIGH);
            Check(player.PendingHitReaction == PlayerHitReaction.Stagger, "Strong hit without force lost stagger.");
            player.ConsumeHitReaction();
            player.TakeDamage(10f, power: Vector3.zero, poiseDamage: 0f,
                staggerResistLevel: StaggerResistLevel.HIGH);
            Check(player.PendingHitReaction == PlayerHitReaction.Hit, "Strong hit without force lost hit reaction.");
            player.ConsumeHitReaction();
            player.HitStaggerResistance = StaggerResistLevel.SUPER_ARMOR;
            hp.SetValue(stats, 5f);
            var fatal = player.TakeDamage(10f, staggerResistLevel: StaggerResistLevel.NONE);
            Check(fatal.Killed && stats.CurrentHp == 0f, "Stagger resistance prevented lethal damage.");
            return "PASS: " + checkedPairs + " attack/resistance pairs (damage + state transition), pending strong hit preserved, zero-force hit/stagger, lethal damage.";
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
