using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Isolated readiness/commit tests; never authors real form attacks or changes open scenes.</summary>
public static class FormChainRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    private sealed class ProbeForm : BaseForm
    {
        private readonly ElementType _element;
        public int Starts;
        public bool OutgoingExited;
        public bool SawExit;
        public ProbeForm(WeaponActionDataSO data, ElementType element) : base(data) => _element = element;
        public override ElementType Element => _element;
        public override void BeginSwitchAttack() { Starts++; SawExit = OutgoingExited; }
        public override void UpdateSwitchAttack(out bool complete) => complete = false;
        public override void EndSwitchAttack() { }
    }

    private sealed class OutgoingState : IPlayerState
    {
        private readonly ProbeForm _target;
        public OutgoingState(ProbeForm target) => _target = target;
        public void Enter() { }
        public void Update() { }
        public void Exit() => _target.OutgoingExited = true;
    }

    [MenuItem("Tools/Studio Baekho/Combat/Run Form Chain Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run this fixture in Edit Mode.");
        _checks = 0;
        var scene = EditorSceneManager.NewPreviewScene();
        var assets = new List<Object>();
        GameObject host = null;
        try
        {
            T Asset<T>() where T : ScriptableObject
            { var asset = ScriptableObject.CreateInstance<T>(); assets.Add(asset); return asset; }
            var statsData = Asset<PlayerStatData>();
            var fireData = Asset<FireFormActionDataSO>();
            var waterData = Asset<WaterFormActionDataSO>();
            var woodData = Asset<WoodFormActionDataSO>();
            Check(fireData.ChainSettings.Enabled && fireData.ChainSettings.Trigger == FormChainTrigger.OtherFormFinalHit,
                "New fire data enables its final-hit condition.");
            Check(waterData.ChainSettings.Enabled && waterData.ChainSettings.Trigger == FormChainTrigger.SuccessfulDodge,
                "New water data enables its dodge condition.");
            Check(!woodData.ChainSettings.Enabled, "Deferred forms stay disabled.");
            Check(fireData.SwitchAttackSteps.Count == 0 && waterData.SwitchAttackSteps.Count == 0,
                "New dedicated slots remain empty.");

            host = new GameObject("__FormChainRegression");
            host.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
            host.transform.position = new Vector3(16500f, 1100f, 16500f);
            var player = host.AddComponent<PlayerController>();
            var input = host.GetComponent<PlayerInputHandler>();
            var stats = host.GetComponent<PlayerStatManager>();
            stats.Initialize(statsData);
            Property(player, "Controller", host.GetComponent<CharacterController>());
            Property(player, "Input", input);
            Property(player, "StatManager", stats);
            Property(player, "Movement", host.GetComponent<PlayerMovement>());
            player.Movement.Initialize(player);
            player.Gravity = player.VerticalVelocity = 0f;
            Field(player, "_attackTrails", Array.Empty<TrailVfxController>());
            Field(player, "_attackTrailsInitialized", true);
            var animator = host.AddComponent<Animator>();
            var controller = new AnimatorController { name = "Temporary chain controller" };
            assets.Add(controller);
            controller.AddLayer("Base Layer");
            assets.Add(controller.layers[0].stateMachine);
            var idleAnimation = controller.layers[0].stateMachine.AddState("Idle");
            assets.Add(idleAnimation);
            animator.runtimeAnimatorController = controller;
            Property(player, "Animator", animator);
            var forms = new FormManager(player);
            var machine = new StateMachine();
            Property(player, "FormManager", forms);
            Property(player, "StateMachine", machine);
            var fire = new ProbeForm(fireData, ElementType.ELEMENT_FIRE);
            var water = new ProbeForm(waterData, ElementType.ELEMENT_WATER);
            var wood = new ProbeForm(woodData, ElementType.ELEMENT_WOOD);
            forms.AddTransition(fire, () => false);
            forms.AddTransition(water, () => false);
            forms.AddTransition(wood, () => false);
            forms.ChangeForm(wood);
            host.SetActive(true);
            machine.ChangeState(new PlayerIdleState(player));

            forms.NotifyFinalComboHit(wood.Element);
            Near(forms.GetChainReadyRemaining(fire.Element), 5, "Other form finisher grants five seconds.");
            Check(forms.ReadyChainSkill == null, "Empty attack does not fall back to E.");
            fireData.SkillSteps = new List<WeaponActionData> { Step() };
            Check(forms.ReadyChainSkill == null, "Authored E alone cannot enable a chain.");
            fireData.SwitchAttackSteps.Add(Step());
            Check(forms.ReadyChainSkill == fire, "Authored dedicated attack becomes eligible.");
            forms.UpdateChainReadiness(2);
            Near(forms.GetChainReadyRemaining(fire.Element), 3, "Ready time decrements.");
            forms.NotifyFinalComboHit(wood.Element);
            Near(forms.GetChainReadyRemaining(fire.Element), 5, "Repeated condition refreshes one token.");
            forms.NotifySuccessfulDodge();
            Check(forms.ReadyChainSkill == fire, "Newest unconfigured water does not hide usable fire.");
            waterData.SwitchAttackSteps.Add(Step());
            Check(forms.ReadyChainSkill == water, "Newest usable token wins.");
            forms.ChangeForm(water);
            Check(forms.ReadyChainSkill == fire, "Current form is excluded without consuming its token.");
            Near(forms.GetChainReadyRemaining(water.Element), 5, "Plain assignment preserves ready token.");
            forms.UpdateChainReadiness(5);
            Check(forms.ReadyChainSkill == null && forms.GetChainReadyRemaining(fire.Element) == 0,
                "Exact five-second boundary expires before selection.");

            forms.ChangeForm(fire);
            forms.NotifyFinalComboHit(fire.Element);
            Near(forms.GetChainReadyRemaining(fire.Element), 0, "Fire cannot ready itself by its final hit.");
            forms.ChangeForm(wood);
            forms.NotifyFinalComboHit(wood.Element);
            forms.UpdateChainReadiness(float.NaN);
            forms.UpdateChainReadiness(float.PositiveInfinity);
            forms.UpdateChainReadiness(-1);
            Near(forms.GetChainReadyRemaining(fire.Element), 5, "Invalid tick cannot corrupt timer.");
            fireData.SwitchAttackSteps[0].Duration = float.NaN;
            Check(forms.ReadyChainSkill == null, "Malformed dedicated duration is ineligible.");
            fireData.SwitchAttackSteps[0].Duration = 1;
            Check(!forms.TryUseReadyChainSkill(out _), "Commit outside switch-state Enter is refused.");
            Near(forms.GetChainReadyRemaining(fire.Element), 5, "Failed commit preserves ready token.");

            int events = 0;
            forms.OnFormSwitchSucceeded += (_, __) => events++;
            Check(forms.TrySwitchForm(fire), "Plain switch accepts authored form.");
            Check(machine.CurrentState is PlayerIdleState && fire.Starts == 0 && !player.HasPendingSwitchAttack,
                "Plain switch does not attack or reserve an attack.");
            Near(forms.GetChainReadyRemaining(fire.Element), 5, "Plain switch does not spend chain token.");
            Check(events == 1 && !player.CanChangeForm, "Plain switch emits once and starts shared cooldown.");
            Check(!forms.TrySwitchForm(water, FormManager.SwitchReason.Parry), "Old numeric parry path is disabled.");

            forms.ChangeForm(wood);
            float cooldown = (float)GetField(player, "_formChangeCooldownTimer");
            machine.ChangeState(new OutgoingState(fire));
            machine.ChangeState(new PlayerSwitchAttackState(player));
            Check(forms.CurrentForm == fire && fire.Starts == 1 && fire.SawExit,
                "F commits after outgoing Exit and starts only the new form.");
            Near((float)GetField(player, "_formChangeCooldownTimer"), cooldown, "F leaves shared cooldown unchanged.");
            Near(forms.GetChainReadyRemaining(fire.Element), 0, "Successful F consumes one ready token.");
            Check(events == 2, "F emits exactly one switch success.");

            forms.NotifySuccessfulDodge();
            forms.CleanupStageEffects();
            Near(forms.GetChainReadyRemaining(water.Element), 0, "Stage transition clears ready tokens.");
            forms.NotifySuccessfulDodge();
            input.ChainSkillBuffer.Set();
            forms.CleanupTransientEffects();
            Check(forms.ReadyChainSkill == null && !input.ChainSkillBuffer.IsActive,
                "Transient cleanup clears ready tokens and buffered F.");

            var ironData = Asset<IronFormActionDataSO>();
            var earthData = Asset<EarthFormActionDataSO>();
            fireData.ComboSteps = new List<WeaponActionData> { Step() };
            waterData.FinisherStep = Step();
            var savedUi = PlayerUIManager.Instance;
            try
            {
                PlayerUIManager.Instance = null;
                var gameplay = new FormManagerSetup(player).Build(fireData, waterData, woodData, ironData, earthData);
                Property(player, "FormManager", gameplay);
                VerifyFireOnlyGameplay(player, statsData);
            }
            finally
            {
                PlayerUIManager.Instance = savedUi;
            }
            return $"PASS: {_checks} form switching, chain readiness and commit assertions.";
        }
        finally
        {
            if (host != null) Object.DestroyImmediate(host);
            for (int i = assets.Count - 1; i >= 0; i--)
                if (assets[i] != null) Object.DestroyImmediate(assets[i]);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void VerifyFireOnlyGameplay(PlayerController player, PlayerStatData statsData)
    {
        var forms = player.FormManager;
        var input = player.Input;
        var stats = player.StatManager;
        var machine = player.StateMachine;
        var fire = forms.CurrentForm;
        Check(fire is FireForm && forms.IsElementAvailable(ElementType.ELEMENT_FIRE),
            "Gameplay starts with Fire equipped and available.");
        Check(!forms.IsElementAvailable(ElementType.ELEMENT_NONE), "The neutral element is not a playable form.");

        var idle = new PlayerIdleState(player);
        machine.ChangeState(idle);
        stats.Initialize(statsData);
        Field(player, "_formChangeCooldownTimer", 0f);
        Check(player.CanChangeForm, "The test player has no switch cooldown.");
        int changes = 0, switches = 0;
        forms.OnFormChanged += _ => changes++;
        forms.OnFormSwitchSucceeded += (_, __) => switches++;

        var lockedElements = new[]
        {
            ElementType.ELEMENT_WATER, ElementType.ELEMENT_WOOD, ElementType.ELEMENT_GOLD, ElementType.ELEMENT_EARTH
        };
        for (int i = 0; i < lockedElements.Length; i++)
        {
            var element = lockedElements[i];
            var target = forms.GetForm(element);
            Check(target != null && !forms.IsElementAvailable(element), $"{element} is authored but locked.");
            Property(input, $"IsForm{i + 2}Pressed", true);
            Check(forms.GetRequestedForm() == null, $"Numeric input does not request {element}.");
            input.ConsumeFormInput();
            Check(!forms.CanSwitchForm(target) && !forms.TrySwitchForm(target), $"Direct switch cannot equip {element}.");
        }

        Property(input, nameof(PlayerInputHandler.IsFastFormPressed), true);
        var getTransition = typeof(FormManager).GetMethod("GetTransition", Hidden);
        Check(getTransition.Invoke(forms, null) == null, "Quick switch has no available destination from Fire.");
        input.ConsumeFormInput();

        var water = forms.GetForm(ElementType.ELEMENT_WATER);
        Check(water.CanSwitchAttack && water.CanBeginFinisher, "Locked Water has valid chain and finisher attacks.");
        forms.NotifySuccessfulDodge();
        Check(forms.GetChainReadyRemaining(water.Element) == 0f && forms.ReadyChainSkill == null,
            "A successful dodge never exposes a locked chain target.");
        var chain = new PlayerSwitchAttackState(player);
        machine.ChangeState(chain);
        Check(chain.IsComplete && forms.CurrentForm == fire, "Even entering the F switch state cannot equip locked Water.");

        machine.ChangeState(new PlayerAttackState(player));
        Check(!forms.TrySwitchForFinisher(water) && forms.CurrentForm == fire,
            "A finisher cannot equip a locked, otherwise valid destination.");
        machine.ChangeState(idle);
        Check(changes == 0 && switches == 0 && player.CanChangeForm,
            "Denied switches emit no events and never start the switch cooldown.");

        forms.ChangeForm(water);
        Check(forms.CurrentForm == water && !forms.IsElementAvailable(water.Element),
            "An explicit editor assignment can inspect a locked form without unlocking it.");
        forms.ChangeForm(fire);
    }

    private static WeaponActionData Step() => new WeaponActionData { Duration = 1, AnimationName = "", HitDuration = .1f };
    private static void Property(object owner, string name, object value)
        => owner.GetType().GetProperty(name).SetValue(owner, value);
    private static object GetField(object owner, string name) => owner.GetType().GetField(name, Hidden).GetValue(owner);
    private static void Field(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Form chain: " + message); _checks++; }
    private static void Near(float actual, float expected, string message)
        => Check(Mathf.Abs(actual - expected) < .0001f, message + $" ({actual}, expected {expected})");
}
