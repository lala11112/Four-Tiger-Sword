using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Isolated checks for finisher windows, explicit stage three, and independent authoring data.</summary>
public static class FinisherFormRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class ProbeForm : BaseForm
    {
        public ProbeForm(WeaponActionDataSO data) : base(data) { }
        public void At(int stage, float elapsed)
        {
            _currentAction = ActionType.Attack;
            _comboStep = stage;
            _timer = elapsed;
            typeof(BaseForm).GetField("_normalAttackActive", Hidden).SetValue(this, true);
        }
        public float Elapsed => _timer;
        public HitInfo CurrentHit() => EffectHit(CurrentNormalAttackStep.Damage);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Finisher forms: " + message);
    }

    [MenuItem("Tools/Studio Baekho/Combat/Run Finisher Form Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        Require(!Application.isPlaying, "Run requires Edit Mode.");
        var data = ScriptableObject.CreateInstance<FireFormActionDataSO>();
        var earth = ScriptableObject.CreateInstance<EarthFormActionDataSO>();
        var statsData = ScriptableObject.CreateInstance<PlayerStatData>();
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject playerObject = null;
        try
        {
            data.ComboSteps = new List<WeaponActionData> { Step(), Step(), Step() };
            var stageTwo = data.ComboSteps[1];
            stageTwo.Duration = 1.4f;
            stageTwo.ComboTransitionTime = 1f;
            stageTwo.HitEvents = new List<HitEvent>
            {
                new HitEvent { StartTime = .56f, Duration = .04f },
                new HitEvent { StartTime = .33f, Duration = .04f }
            };
            var form = new ProbeForm(data);
            var actions = data.Actions;
            data.Actions = null;
            Require(!form.HasNormalAttackStage(2) && !form.CanBeginFinisher, "Missing action group was not rejected safely.");
            data.Actions = actions;
            float thirdDuration = data.ComboSteps[2].Duration;
            data.ComboSteps[2].Duration = float.NaN;
            Require(!form.HasNormalAttackStage(2) && !form.CanBeginFinisher, "Non-finite third-stage duration was accepted.");
            data.ComboSteps[2].Duration = thirdDuration;
            form.At(1, .59f);
            Require(!form.CanSelectFinisher, "Multi-hit stage opened before its final hit ended.");
            float lastHitEnd = stageTwo.HitEvents[0].StartTime + stageTwo.HitEvents[0].Duration;
            form.At(1, lastHitEnd);
            Require(form.CanSelectFinisher && !form.CanChainAttack,
                "Finisher must open immediately at the latest hit end even before normal combo chaining is allowed.");
            Require(Mathf.Abs(form.FinisherSelectionWindowRemaining - (stageTwo.Duration - lastHitEnd)) < .0001f,
                "Earlier finisher opening changed the original recovery deadline.");
            form.At(1, .8f);
            Require(form.CanSelectFinisher && !form.CanChainAttack,
                "Finisher selection incorrectly waited for the later normal combo transition time.");
            form.At(1, 1f);
            Require(form.CanSelectFinisher, "Valid second-stage recovery did not open.");
            Require(Mathf.Abs(form.FinisherSelectionWindowRemaining - .4f) < .0001f,
                "Selection did not retain only the remaining recovery.");
            form.At(1, 1.4f);
            Require(!form.CanSelectFinisher, "Selection accepted the duration endpoint.");
            form.At(1, 1.5f);
            Require(!form.CanSelectFinisher, "Selection reopened after the original duration deadline.");
            var multiHits = stageTwo.HitEvents;
            stageTwo.HitEvents = new List<HitEvent>();
            stageTwo.HitStartTime = .2f;
            stageTwo.HitDuration = .1f;
            form.At(1, .29f);
            Require(!form.CanSelectFinisher, "Single-hit stage opened before its active hit window ended.");
            form.At(1, stageTwo.HitStartTime + stageTwo.HitDuration);
            Require(form.CanSelectFinisher && !form.CanChainAttack,
                "Single-hit stage incorrectly waited for normal combo transition after its hit ended.");
            stageTwo.HitEvents = multiHits;
            form.At(0, 1f);
            Require(!form.CanSelectFinisher, "Selection opened on stage one.");

            playerObject = new GameObject("Inactive finisher regression player");
            playerObject.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(playerObject, scene);
            var player = playerObject.AddComponent<PlayerController>();
            var stats = playerObject.GetComponent<PlayerStatManager>();
            stats.Initialize(statsData);
            Set(player, nameof(PlayerController.StatManager), stats);
            Set(player, nameof(PlayerController.Controller), playerObject.GetComponent<CharacterController>());
            Set(player, nameof(PlayerController.Input), playerObject.GetComponent<PlayerInputHandler>());
            Set(player, nameof(PlayerController.Animator), playerObject.AddComponent<Animator>());
            Set(player, nameof(PlayerController.FormManager), new FormManager(player));
            form.Equip(player);
            form.At(1, 1f);
            form.LockFinisherFacing(Vector3.right);
            Require(form.Elapsed == 1f && form.CanSelectFinisher, "Locking selection rewound or ended source recovery.");
            Require(data.FinisherStep == null && form.CanBeginFinisher
                && form.BeginFinisher(Vector3.forward, true), "Empty finisher slot did not reuse normal stage three.");
            Require(ReferenceEquals(form.CurrentNormalAttackStep, data.ComboSteps[2]), "Fallback copied or replaced normal stage three.");
            form.EndAttack();
            data.FinisherStep = FinisherDataSetup.CloneStep(data.ComboSteps[2]);
            data.FinisherStep.Damage = 3f;
            Require(form.BeginFinisher(Vector3.right, true), "Explicit transformed stage three failed.");
            Require(form.CurrentComboStage == 2 && ReferenceEquals(form.CurrentNormalAttackStep, data.FinisherStep),
                "Transformed execution did not use the separate stage-three slot.");
            var hit = form.CurrentHit();
            Require(hit.Category == PlayerAttackCategory.Normal && hit.HasSourceDamageSnapshot
                && Mathf.Abs(hit.BaseDamage - data.FinisherStep.Damage * stats.GetStat(StatType.ST_ATK)) < .0001f,
                "Finisher lost normal damage/category/snapshot semantics or added an implicit multiplier.");
            Require(Vector3.Dot(player.transform.forward, Vector3.right) > .999f, "Finisher facing was not locked.");
            Require(!form.CanSelectFinisher && !form.TryContinueAttack(), "Finisher permitted another selection or fourth attack.");
            form.EndAttack();
            Require(form.CurrentComboStage == -1, "Attack cleanup retained a live finisher stage.");
            Require(form.BeginFinisher(Vector3.forward, false), "Current-form normal stage three failed.");
            Require(ReferenceEquals(form.CurrentNormalAttackStep, data.ComboSteps[2]),
                "Current-form stage three used transformed data.");
            form.EndAttack();
            Require(!form.BeginFinisher(Vector3.zero, true), "Invalid direction began a finisher.");

            earth.ComboSteps = new List<WeaponActionData> { Step(), Step() };
            string firstBefore = JsonUtility.ToJson(earth.ComboSteps[0]);
            string secondBefore = JsonUtility.ToJson(earth.ComboSteps[1]);
            WeaponActionData slash = Step();
            slash.Duration = 1.5f; slash.HitStartTime = .38333333f; slash.HitDuration = .116666675f;
            Require(FinisherDataSetup.EnsureEarthThirdStage(earth, slash), "Earth third stage was not appended.");
            Require(firstBefore == JsonUtility.ToJson(earth.ComboSteps[0])
                && secondBefore == JsonUtility.ToJson(earth.ComboSteps[1]), "Earth first or second stage changed.");
            Require(earth.ComboSteps[2].AnimationName == "OutSlash" && earth.ComboSteps[2].ComboTransitionTime == 0f,
                "Earth temporary stage did not use its existing terminal slash.");
            Require(!FinisherDataSetup.EnsureEarthThirdStage(earth, slash), "Setup appended a duplicate Earth stage.");
            earth.FinisherStep = FinisherDataSetup.CloneStep(earth.ComboSteps[2]);
            earth.FinisherStep.Damage += 1f;
            earth.FinisherStep.Execution.Hitbox.HitBoxRadius += 1f;
            Require(earth.FinisherStep.Damage != earth.ComboSteps[2].Damage
                && earth.FinisherStep.HitBoxRadius != earth.ComboSteps[2].HitBoxRadius,
                "Finisher authoring data shared mutable normal-stage objects.");
            var earthForm = new EarthForm(earth);
            typeof(BaseForm).GetField("_playerController", Hidden).SetValue(earthForm, player);
            typeof(EarthForm).GetField("_guardCounterReady", Hidden).SetValue(earthForm, true);
            Require(!earthForm.BeginFinisher(Vector3.zero, true) && earthForm.GuardCounterReady,
                "Rejected Earth finisher changed guard readiness.");
            Require(earthForm.BeginFinisher(Vector3.forward, true) && !earthForm.GuardCounterReady,
                "Earth finisher retained stale guard-counter readiness.");
            earthForm.EndAttack();
            using (var serialized = new SerializedObject(earth))
            {
                var slot = serialized.FindProperty("Actions.FinisherStep");
                Require(slot != null && slot.propertyType == SerializedPropertyType.ManagedReference,
                    "Optional finisher slot is absent from inherited form Inspector serialization.");
            }
            return "PASS: second-stage multi-hit/recovery boundaries, explicit normal/transformed third-stage routing, terminal execution, facing, cleanup, independent slots, and idempotent Earth setup.";
        }
        finally
        {
            if (playerObject != null) Object.DestroyImmediate(playerObject);
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(data);
            Object.DestroyImmediate(earth);
            Object.DestroyImmediate(statsData);
        }
    }

    private static WeaponActionData Step() => new WeaponActionData
    {
        Duration = .6f, ComboTransitionTime = .3f, HitStartTime = .2f, HitDuration = .1f
    };

    private static void Set(object target, string property, object value)
        => target.GetType().GetProperty(property).SetValue(target, value);
}
