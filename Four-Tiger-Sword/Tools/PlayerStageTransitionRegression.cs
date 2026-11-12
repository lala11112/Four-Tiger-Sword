// Run in Edit Mode with Unity CLI execute-code, loading this source and calling Run.
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class PlayerStageTransitionRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static void Near(float actual, float expected, string message)
        => Check(Mathf.Abs(actual - expected) < 0.001f, message + ": " + actual + " != " + expected);
    private static void Set(object target, string property, object value)
        => target.GetType().GetProperty(property).SetValue(target, value);
    private static void Field(object target, Type owner, string name, object value)
        => owner.GetField(name, Hidden).SetValue(target, value);
    private static float Read(object target, Type owner, string name)
        => (float)owner.GetField(name, Hidden).GetValue(target);

    private sealed class ProbeForm : BaseForm
    {
        public ProbeForm() : base(null) { }
        public override ElementType Element => ElementType.ELEMENT_FIRE;
    }

    private sealed class ProbeState : IPlayerState
    {
        public int Exits;
        public void Enter() { }
        public void Update() { throw new Exception("A loading player updated an action."); }
        public void Exit() { Exits++; }
    }

    public static string Run()
    {
        Check(!Application.isPlaying, "Run in Edit Mode.");
        checks = 0;
        var savedUi = PlayerUIManager.Instance;
        float savedScale = Time.timeScale;
        var scene = EditorSceneManager.NewPreviewScene();
        var otherScene = EditorSceneManager.NewPreviewScene();
        PlayerStatData data = null;
        ItemCatalog catalog = null;
        ItemDefinition item = null;
        try
        {
            PlayerUIManager.Instance = null;
            var go = new GameObject("Player stage transition regression");
            SceneManager.MoveGameObjectToScene(go, scene);
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/_Project/Animations/AnimController/BDJ.controller");
            Check(animator.runtimeAnimatorController != null, "Fixture animator is required.");
            animator.Rebind(); animator.Update(0f);
            var player = go.AddComponent<PlayerController>();
            Set(player, "Animator", animator);
            Set(player, "Controller", go.GetComponent<CharacterController>());
            Set(player, "Input", go.GetComponent<PlayerInputHandler>());
            Set(player, "Movement", go.GetComponent<PlayerMovement>());
            Set(player, "StatManager", go.GetComponent<PlayerStatManager>());
            var runtime = go.AddComponent<PlayerItemRuntime>();
            Set(player, "ItemRuntime", runtime);
            player.Movement.Initialize(player);
            Field(player, typeof(PlayerController), "_attackTrails", Array.Empty<TrailVfxController>());
            Field(player, typeof(PlayerController), "_attackTrailsInitialized", true);
            var state = new ProbeState();
            var machine = new StateMachine();
            machine.ChangeState(state);
            Set(player, "StateMachine", machine);
            var forms = new FormManager(player);
            var form = new ProbeForm();
            forms.AddTransition(form, () => false);
            Set(player, "FormManager", forms);
            forms.ChangeForm(form);

            data = ScriptableObject.CreateInstance<PlayerStatData>();
            data.baseHP = 250f;
            player.StatManager.Initialize(data);
            catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            catalog.projectId = "stage-transition-regression";
            item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.projectId = catalog.projectId; item.itemId = "owned"; item.candidateKey = "owned";
            item.effects = new[] { new ItemEffect { stat = ItemStat.NormalDamage, amount = 0.2f } };
            catalog.items = new[] { item };
            runtime.Catalog = catalog;
            runtime.Initialize(player);
            Check(runtime.TryAddItem(item.itemId), "Fixture item ownership.");
            player.StatManager.TakeDamage(20f, ElementType.ELEMENT_NONE, false);
            player.StatManager.AddUltimateGauge(33f);
            var shield = new object();
            player.StatManager.GrantShield(shield, 40f, 5.5f);
            Field(player, typeof(PlayerController), "_dashCooldownTimer", 2f);
            Field(player, typeof(PlayerController), "_formChangeCooldownTimer", 1.25f);
            Field(form, typeof(BaseForm), "_skillCooldownTimer", 3.5f);
            Field(form, typeof(BaseForm), "_ultimateCooldownTimer", 8f);
            player.VerticalVelocity = -20f;
            player.Input.AttackBuffer.Set();
            player.Input.DashBuffer.Set();
            var transient = new GameObject("Old stage world VFX");
            SceneManager.MoveGameObjectToScene(transient, scene);
            StageTransientObject.Track(transient);
            var otherTransient = new GameObject("Other scene VFX");
            SceneManager.MoveGameObjectToScene(otherTransient, otherScene);
            StageTransientObject.Track(otherTransient);
            var persistent = new GameObject("Unmarked scene object");
            SceneManager.MoveGameObjectToScene(persistent, scene);

            player.BeginStageTransition();
            player.BeginStageTransition();
            Check(player.IsStageTransitioning && state.Exits == 1, "Transition is idempotent and exits action once.");
            Check(!player.Input.enabled && !player.Movement.enabled && !player.Controller.enabled && !animator.enabled,
                "Input, movement, controller and animation are frozen.");
            Check(!player.Input.AttackBuffer.IsActive && !player.Input.DashBuffer.IsActive, "Buffered actions are consumed.");
            Check(!player.CanDash && !player.CanChangeForm && !player.CanSkill && !player.CanUltimate, "Action entry is blocked.");
            Check(transient == null && otherTransient != null && persistent != null, "Only this scene's marked VFX are cleared.");
            Check(!player.TakeDamage(9999f).Applied, "Controller damage is blocked while loading.");
            Check(!player.StatManager.TakeDamage(9999f, ElementType.ELEMENT_NONE, false).Applied, "Direct stat damage is blocked while loading.");
            typeof(PlayerController).GetMethod("Update", Hidden).Invoke(player, null);
            typeof(PlayerStatManager).GetMethod("Update", Hidden).Invoke(player.StatManager, null);
            Near(player.StatManager.CurrentHp, 230f, "Health retained");
            Near(player.StatManager.CurrentUltimateGauge, 33f, "Ultimate retained");
            Near(player.StatManager.GetShieldRemaining(shield), 5.5f, "Shield duration frozen");
            Near(Read(player, typeof(PlayerController), "_dashCooldownTimer"), 2f, "Dash cooldown frozen");
            Near(Read(player, typeof(PlayerController), "_formChangeCooldownTimer"), 1.25f, "Form cooldown frozen");
            Near(form.SkillCooldownRemaining, 3.5f, "Skill cooldown frozen");
            Near(form.UltimateCooldownRemaining, 8f, "Ultimate cooldown frozen");
            Check(forms.CurrentForm == form && runtime.Contains("owned"), "Form and item ownership retained.");

            var spawn = new Vector3(12f, 2f, -5f);
            var rotation = Quaternion.Euler(0f, 80f, 0f);
            player.CompleteStageTransition(spawn, rotation);
            Check(!player.IsStageTransitioning && player.Input.enabled && player.Movement.enabled
                && player.Controller.enabled && animator.enabled, "Control resumes after spawn.");
            Check(Vector3.Distance(go.transform.position, spawn) < 0.001f && Quaternion.Angle(go.transform.rotation, rotation) < 0.01f,
                "Spawn transform is exact.");
            Check(player.StateMachine.CurrentState is PlayerIdleState && !animator.applyRootMotion, "Action resets to idle without root motion.");
            Near(player.VerticalVelocity, 0f, "Old falling velocity cleared");
            Near(player.StatManager.CurrentHp, 230f, "Completing transition does not heal");

            var hudObject = new GameObject("Scene HUD");
            SceneManager.MoveGameObjectToScene(hudObject, scene);
            var hud = hudObject.AddComponent<PlayerUIManager>();
            PlayerUIManager.Instance = hud;
            hud.Register(player); hud.Register(player);
            var hpSubscriptions = (Delegate)typeof(PlayerStatManager).GetField("OnHpChanged", Hidden).GetValue(player.StatManager);
            Check(hpSubscriptions.GetInvocationList().Count(d => ReferenceEquals(d.Target, hud)) == 1, "Re-registering HUD does not duplicate subscriptions.");
            hud.Register(null);
            hpSubscriptions = (Delegate)typeof(PlayerStatManager).GetField("OnHpChanged", Hidden).GetValue(player.StatManager);
            Check(hpSubscriptions == null || hpSubscriptions.GetInvocationList().All(d => !ReferenceEquals(d.Target, hud)), "HUD unbind removes subscriptions.");
            Object.DestroyImmediate(hudObject);
            Check(PlayerUIManager.Instance == null, "Destroyed HUD clears its singleton.");

            int deaths = 0;
            player.Died += () => deaths++;
            player.StatManager.TakeDamage(9999f, ElementType.ELEMENT_NONE, false);
            var death = new PlayerDieState(player);
            death.Enter(); death.Enter();
            Check(deaths == 1, "Actual death is published once.");
            string result = "Player stage transition: " + checks + " checks passed.";
            Debug.Log(result);
            return result;
        }
        finally
        {
            StageTransientObject.ClearScene(scene);
            StageTransientObject.ClearScene(otherScene);
            EditorSceneManager.ClosePreviewScene(scene);
            EditorSceneManager.ClosePreviewScene(otherScene);
            if (item != null) Object.DestroyImmediate(item);
            if (catalog != null) Object.DestroyImmediate(catalog);
            if (data != null) Object.DestroyImmediate(data);
            PlayerUIManager.Instance = savedUi;
            Time.timeScale = savedScale;
        }
    }
}
