using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TutorialCombatRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Studio Baekho/Tutorial/Run Combat Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    private sealed class AirFinisherProbe : BaseForm
    {
        private readonly Action _contact;

        public AirFinisherProbe(Action contact) : base(null) => _contact = contact;
        public override void BeginAirAttackFinish() { }
        public override void UpdateAirAttack(out bool isComplete)
        {
            _contact();
            isComplete = true;
        }
    }

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Tutorial combat regression requires Edit Mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var actor = CreateObject(scene, "Tutorial player");
            var player = actor.AddComponent<PlayerController>();
            var stats = actor.GetComponent<PlayerStatManager>();
            stats.Initialize(null);
            typeof(PlayerController).GetProperty(nameof(PlayerController.StatManager)).SetValue(player, stats);
            var reporter = actor.AddComponent<TutorialCombatReporter>();
            // This isolated Edit Mode fixture does not run MonoBehaviour lifecycle callbacks.
            Invoke(reporter, "OnEnable");

            var target = CreateEnemy(scene, "Lesson target");
            var other = CreateEnemy(scene, "Unrelated enemy");
            reporter.SetTrainingProtection(true, target, true);
            var completions = new System.Collections.Generic.List<TutorialCombatAction>();
            reporter.ActionCompleted += action =>
            {
                completions.Add(action);
                if (action == TutorialCombatAction.AirHit)
                    reporter.ResetLessonResources(TutorialCombatAction.AirLanded);
            };
            reporter.ResetLessonResources(TutorialCombatAction.AirHit);
            Invoke(reporter, "ReportHit", TutorialCombatAction.GroundHit, target);
            Invoke(reporter, "ReportHit", TutorialCombatAction.AirHit, other);
            Check(completions.Count == 0, "An earlier action or another target satisfied the air attack lesson.");
            var forms = new FormManager(player);
            forms.ChangeForm(new AirFinisherProbe(() => Invoke(reporter, "ReportHit", TutorialCombatAction.AirHit, target)));
            typeof(PlayerController).GetProperty(nameof(PlayerController.FormManager)).SetValue(player, forms);
            var air = new PlayerAirAttackState(player);
            var phase = typeof(PlayerAirAttackState).GetField("_phase", Hidden);
            phase.SetValue(air, Enum.Parse(phase.FieldType, "Loop"));
            typeof(PlayerAirAttackState).GetMethod("BeginFinish", Hidden).Invoke(air, null);
            Check(completions.Count == 0, "Beginning a landing without a hit completed the lesson.");
            air.Update();
            air.Update();
            Check(completions.Count == 2 && completions[0] == TutorialCombatAction.AirHit
                && completions[1] == TutorialCombatAction.AirLanded,
                "The grounded hit and completion must advance in that order, once per lesson.");

            var delayed = new HitInfo(500f, ElementType.ELEMENT_FIRE, 0f, 0f, trueDamage: true,
                source: player, category: PlayerAttackCategory.Secondary);
            DamageResult targetDamage = DamageManager.Apply(delayed, target, target.gameObject);
            DamageResult repeatedDamage = DamageManager.Apply(delayed, target, target.gameObject);
            Check(targetDamage.Applied && repeatedDamage.Applied && !target.IsDead && target.EnemyStat.CurrentHp == 1f,
                "Delayed damage killed the training enemy or disabled its hit contact.");
            var normalDamage = DamageManager.Apply(delayed, other, other.gameObject);
            Check(normalDamage.Killed && other.IsDead, "Training protection covered an unrelated enemy.");
            Check(!stats.TakeDamage(500f, ElementType.ELEMENT_NONE, false).Killed && stats.CurrentHp == 1f,
                "The training player did not survive lethal damage.");

            reporter.enabled = false;
            Invoke(reporter, "OnDisable");
            Check(stats.TakeDamage(500f, ElementType.ELEMENT_NONE, false).Killed,
                "Disabling the reporter did not remove the player's protection.");
            Check(DamageManager.Apply(delayed, target, target.gameObject).Killed,
                "Disabling the reporter did not remove the target's protection.");
            return "Tutorial combat regression passed.";
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static GameObject CreateObject(Scene scene, string name)
    {
        var obj = new GameObject(name);
        SceneManager.MoveGameObjectToScene(obj, scene);
        return obj;
    }

    private static Enemy CreateEnemy(Scene scene, string name)
    {
        var obj = CreateObject(scene, name);
        var enemy = obj.AddComponent<Enemy>();
        obj.GetComponent<EnemyStat>().CurrentHp = 10f;
        return enemy;
    }

    private static void Invoke(TutorialCombatReporter reporter, string name, params object[] args)
        => typeof(TutorialCombatReporter).GetMethod(name, Hidden).Invoke(reporter, args);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
