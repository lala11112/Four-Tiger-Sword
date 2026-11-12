using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

/// <summary>Runs synchronously before the temporary clone's Start; never registers it with the live HUD.</summary>
public static class BaekhoItemFlowRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    private sealed class DodgeProbeAction : EnemyAction
    {
        public DodgeProbeAction() : base(null, null) { }
    }

    private sealed class ProbeForm : BaseForm
    {
        private readonly ElementType element;
        public int SwitchBegins, SwitchUpdates, SwitchEnds, CounterBegins, CounterEnds;
        public int AttackBegins, AttackEnds;
        public bool SwitchComplete, CounterComplete, AttackComplete;
        public HitInfo LastSwitchHit;
        public ProbeForm(ElementType element, WeaponActionDataSO data) : base(data) => this.element = element;
        public override ElementType Element => element;
        public WeaponActionDataSO Data => _weaponActionData;
        public override void BeginAttack() { AttackBegins++; AttackComplete = false; base.BeginAttack(); }
        public override void UpdateAttack(out bool isComplete) => isComplete = AttackComplete;
        public override void EndAttack() { AttackEnds++; base.EndAttack(); }
        public override void BeginSwitchAttack()
        {
            SwitchBegins++;
            SwitchComplete = false;
            base.BeginSwitchAttack();
            LastSwitchHit = EffectHit(1f);
        }
        public override void UpdateSwitchAttack(out bool isComplete) { SwitchUpdates++; isComplete = SwitchComplete; }
        public override void EndSwitchAttack() { SwitchEnds++; base.EndSwitchAttack(); }
        public HitInfo CurrentHit() => EffectHit(1f);
        public override void BeginCounter() { CounterBegins++; CounterComplete = false; }
        public override void UpdateCounter(out bool isComplete) => isComplete = CounterComplete;
        public override void EndCounter() => CounterEnds++;
    }

    private sealed class Fixture
    {
        public PlayerController Player;
        public PlayerStatData Data;
        public ProbeForm Water, Fire, Metal;
        public EarthForm Earth;
        public int Events;
        public PlayerIdleState Idle;
        public PlayerCounterState Counter;
        public PlayerAttackState Attack;
        public PlayerDashState Dash;
        public PlayerSwitchAttackState Switch;
        public object Telegraph = new object();
        public HashSet<object> Telegraphs;
        public PlayerStatManager Stats => Player.StatManager;
        public PlayerInputHandler Input => Player.Input;
        public StateMachine Machine => Player.StateMachine;
        public FormManager Forms => Player.FormManager;

        public void Reset()
        {
            Player.enabled = true;
            Player.CancelPendingSwitchAttack();
            Forms.CleanupTransientEffects();
            Input.AttackBuffer.Consume(); Input.DashBuffer.Consume(); Input.JumpBuffer.Consume();
            Input.SkillBuffer.Consume(); Input.UltimateBuffer.Consume(); Input.ConsumeFormInput();
            Input.ChainSkillBuffer.Consume();
            Property(Input, "MoveInput", Vector2.zero);
            Property(Player, "PendingHitReaction", PlayerHitReaction.None);
            Machine.ChangeState(Idle);
            Forms.ChangeForm(Water);
            Field(Player, "_formChangeCooldownTimer", 0f);
            Field(Player, "_dashCooldownTimer", 0f);
            Player.VerticalVelocity = 0f;
            Player.ItemRuntime.ClearTimedEffects();
            Stats.Initialize(Data);
            Player.ItemRuntime.Initialize(Player);
            Player.ItemRuntime.RecalculateForCurrentForm();
            Events = 0;
        }

        public void BeginFireChain()
        {
            Reset();
            Forms.NotifyFinalComboHit(Water.Element);
            Input.ChainSkillBuffer.Set();
            Machine.Update();
            Check(Machine.CurrentState == Switch && Forms.CurrentForm == Fire && Events == 1,
                "F consumes a ready fire chain through the canonical state transition");
        }
    }

    public static string RunPlayMode()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("RunPlayMode requires Play Mode.");
        checks = 0;
        float savedScale = Time.timeScale;
        var savedRandom = UnityEngine.Random.state;
        PlayerUIManager savedUi = PlayerUIManager.Instance;
        GameObject stage = null;
        PlayerStatData data = null;
        var actionAssets = new List<WeaponActionDataSO>();
        Fixture fixture = null;
        var source = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None).FirstOrDefault(p => p.isActiveAndEnabled);
        Check(source != null, "active live PlayerController required");
        var inputStates = typeof(PlayerInputHandler).GetFields(Hidden)
            .Where(f => f.FieldType == typeof(InputActionReference)).Select(f => (InputActionReference)f.GetValue(source.Input))
            .Where(r => r != null && r.action != null).Select(r => r.action).Distinct().ToDictionary(a => a, a => a.enabled);
        try
        {
            stage = new GameObject("__BaekhoItemFlowRegression");
            stage.SetActive(false);
            GameObject clone = Object.Instantiate(source.gameObject, stage.transform, false);
            clone.name = "Temporary item flow player";
            clone.transform.position = new Vector3(123450f, 1000f, 123450f);
            var player = clone.GetComponent<PlayerController>();
            var input = clone.GetComponent<PlayerInputHandler>();
            foreach (FieldInfo field in typeof(PlayerInputHandler).GetFields(Hidden).Where(f => f.FieldType == typeof(InputActionReference)))
                field.SetValue(input, null);
            if (clone.GetComponent<PlayerItemRuntime>() == null) clone.AddComponent<PlayerItemRuntime>();
            foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && !(behaviour is PlayerController) && !(behaviour is PlayerInputHandler) &&
                    !(behaviour is PlayerMovement) && !(behaviour is PlayerStatManager) && !(behaviour is PlayerItemRuntime) && !(behaviour is WeaponManager))
                    behaviour.enabled = false;
            foreach (Camera camera in clone.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true))
                if (!(collider is CharacterController)) collider.enabled = false;
            foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            Field(player, "_combatCamera", null);
            Field(player, "_attackTrails", System.Array.Empty<TrailVfxController>());
            Field(player, "_attackTrailsInitialized", true);
            Field(player, "_hitStopDuration", 0f);
            player.Gravity = 0f;
            player.VerticalVelocity = 0f;
            player.HitStaggerResistance = StaggerResistLevel.NONE;

            var controller = clone.GetComponent<CharacterController>();
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Temporary flow ground";
            ground.transform.SetParent(stage.transform, false);
            ground.transform.localScale = new Vector3(20, 1, 20);
            Vector3 sphere = clone.transform.position + controller.center + Vector3.down * (controller.height / 2f - controller.radius);
            ground.transform.position = new Vector3(sphere.x, sphere.y - controller.radius - 0.55f, sphere.z);
            int groundLayer = LayerMask.NameToLayer("Ground");
            ground.layer = groundLayer >= 0 ? groundLayer : 0;
            ground.GetComponent<Renderer>().enabled = false;

            // WaterForm.Equip in the cloned Awake must not toggle the live singleton's elements.
            PlayerUIManager.Instance = null;
            stage.SetActive(true);
            PlayerUIManager.Instance = savedUi;
            player.ImpulseSource = null;
            Field(player, "_combatCamera", null);
            Field(player, "_groundLayer", (LayerMask)(1 << ground.layer));
            Physics.SyncTransforms();

            const string projectId = "93190cd9-a826-4b58-b257-cf4ba077b827";
            ItemCatalog catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(
                BaekhoItemImporter.OutputRoot + "/" + projectId + "/ItemCatalog.asset");
            Check(catalog != null && catalog.items.Length == 10, "migrated Baekho item catalog required");
            data = ScriptableObject.CreateInstance<PlayerStatData>();
            data.baseHP = 100;
            player.StatManager.Initialize(data);
            player.ItemRuntime.Catalog = catalog;

            var manager = new FormManager(player);
            var waterActions = ScriptableObject.CreateInstance<WaterFormActionDataSO>();
            var fireActions = ScriptableObject.CreateInstance<FireFormActionDataSO>();
            var metalActions = ScriptableObject.CreateInstance<IronFormActionDataSO>();
            var earthActions = ScriptableObject.CreateInstance<EarthFormActionDataSO>();
            actionAssets.AddRange(new WeaponActionDataSO[] { waterActions, fireActions, metalActions, earthActions });
            foreach (var actions in actionAssets)
            {
                actions.ComboSteps = new List<WeaponActionData> { new WeaponActionData { AnimationName = "", Duration = 1f } };
                actions.SkillSteps = new List<WeaponActionData> { new WeaponActionData { AnimationName = "", Duration = 1f } };
                actions.SwitchAttackSteps = new List<WeaponActionData> { new WeaponActionData { AnimationName = "", Duration = 1f } };
            }
            var water = new ProbeForm(ElementType.ELEMENT_WATER, waterActions);
            var fire = new ProbeForm(ElementType.ELEMENT_FIRE, fireActions);
            var metal = new ProbeForm(ElementType.ELEMENT_GOLD, metalActions);
            var earth = new EarthForm(earthActions);
            manager.AddTransition(fire, () => input.IsForm1Pressed);
            manager.AddTransition(water, () => input.IsForm2Pressed);
            manager.AddTransition(metal, () => input.IsForm4Pressed);
            manager.AddTransition(earth, () => false);
            manager.CanTransition = () => player.StateMachine.CurrentState is PlayerIdleState || player.StateMachine.CurrentState is PlayerRunState;
            Property(player, "FormManager", manager);
            manager.ChangeForm(water);
            player.ItemRuntime.Initialize(player);
            foreach (ItemDefinition item in catalog.items) Check(player.ItemRuntime.TryAddItem(item.itemId), "grant actual item " + item.itemId);
            Check(player.ItemRuntime.OwnedItems.Count == 10, "all retained items granted through public API");

            fixture = new Fixture
            {
                Player = player, Data = data, Water = water, Fire = fire, Metal = metal, Earth = earth,
                Idle = State<PlayerIdleState>(player.StateMachine),
                Counter = State<PlayerCounterState>(player.StateMachine), Dash = State<PlayerDashState>(player.StateMachine),
                Attack = State<PlayerAttackState>(player.StateMachine),
                Switch = State<PlayerSwitchAttackState>(player.StateMachine),
                Telegraphs = (HashSet<object>)typeof(ParryEventBus).GetField("_activeSources", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)
            };
            manager.OnFormSwitchSucceeded += (previous, next) => fixture.Events++;
            // Register only our source, without broadcasting UI events or clearing real enemies' sources.
            fixture.Telegraphs.Add(fixture.Telegraph);
            DirectAndFailures(fixture);
            DashFlow(fixture);
            SuccessfulDodgeFlow(fixture);
            AttackDashFlow(fixture);
            ChainFlow(fixture);
            ChainPriority(fixture);
            ChainSnapshot(fixture);
            CounterFlow(fixture);
            Cancellations(fixture);
            ChainInputBlocks(fixture);
            Check(inputStates.All(p => p.Key.enabled == p.Value), "live input action enable states unchanged");
            string result = "PASS: " + checks + " flow assertions; actual FormManager/controller/state instances, plain direct switch, attack-cancel dash and cooldown gates, F-chain selection/recovery, damage snapshots, earth counter and interruption cleanup.";
            Debug.Log("[Baekho Items] " + result);
            return result;
        }
        finally
        {
            if (fixture != null) fixture.Telegraphs.Remove(fixture.Telegraph);
            PlayerUIManager.Instance = savedUi;
            if (stage != null) Object.DestroyImmediate(stage);
            if (data != null) Object.DestroyImmediate(data);
            foreach (var actions in actionAssets) if (actions != null) Object.DestroyImmediate(actions);
            Time.timeScale = savedScale;
            UnityEngine.Random.state = savedRandom;
        }
    }

    private static void DirectAndFailures(Fixture f)
    {
        f.Reset();
        int begins = f.Fire.SwitchBegins;
        Check(f.Forms.TrySwitchForm(f.Fire), "direct switch succeeds");
        Check(f.Events == 1 && f.Machine.CurrentState == f.Idle && f.Fire.SwitchBegins == begins,
            "direct success emits once and stays in locomotion without an automatic attack");
        Check(!f.Player.HasPendingSwitchAttack && !f.Player.CanChangeForm, "plain swap starts its existing cooldown without a pending attack");
        Check(!f.Fire.IsChainBuffActive, "plain swap does not activate the fire chain buff");
        f.Machine.Update();
        Near(f.Fire.SkillCooldownRemaining, 0, "switch attack does not start E cooldown");
        Check(!f.Forms.TrySwitchForm(f.Metal) && f.Events == 1, "cooldown rejects second switch without event");

        f.Reset();
        Check(!f.Forms.TrySwitchForm(f.Water) && f.Events == 0, "same-form switch rejected");
        Check(f.Player.CanChangeForm, "same-form request does not start cooldown");

        f.Reset();
        f.Machine.ChangeState(f.Counter);
        Check(!f.Forms.TrySwitchForm(f.Fire) && f.Events == 0, "direct switch blocked during counter");
        Check(f.Player.CanChangeForm, "state-blocked request does not start cooldown");

        f.Reset();
        Property(f.Input, "IsForm1Pressed", true);
        f.Machine.Update();
        f.Forms.Update();
        Check(f.Forms.CurrentForm == f.Fire && f.Machine.CurrentState == f.Idle,
            "numeric selection during an enemy telegraph remains a plain swap");
        Check(f.Fire.SwitchBegins == begins && f.Events == 1,
            "numeric selection no longer enters a parry or chain attack");
    }

    private static void DashFlow(Fixture f)
    {
        f.Reset();
        f.Input.DashBuffer.Set();
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Dash, "actual transition enters dash");
        Check(!f.Dash.IsDashComplete, "available dash starts normally");
        f.Machine.ChangeState(f.Idle);
        Check(!f.Player.CanDash, "successful dash starts cooldown on exit");

        f.Reset();
        Field(f.Player, "_dashCooldownTimer", .25f);
        f.Input.DashBuffer.Set();
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Idle, "active cooldown blocks actual dash transition");
        Near((float)GetField(f.Player, "_dashCooldownTimer"), .25f, "blocked dash preserves cooldown");
        f.Machine.ChangeState(f.Dash);
        Check(f.Dash.IsDashComplete, "forced entry during cooldown immediately completes");
        f.Machine.ChangeState(f.Idle);
        Near((float)GetField(f.Player, "_dashCooldownTimer"), .25f, "failed forced entry does not reset cooldown");
    }

    private static void AttackDashFlow(Fixture f)
    {
        f.Reset();
        f.Machine.ChangeState(f.Attack);
        int ended = f.Water.AttackEnds;
        Check(!f.Attack.IsAttackComplete, "attack-cancel fixture starts with an unfinished normal attack");
        f.Input.DashBuffer.Set();
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Dash, "unfinished normal attack cancels directly into actual dash state");
        Check(f.Water.AttackEnds == ended + 1, "successful dodge ends the interrupted normal attack exactly once");
        f.Machine.Update();
        Check(f.Water.AttackEnds == ended + 1, "dash update does not end the interrupted attack again");

        f.Reset();
        f.Machine.ChangeState(f.Attack);
        ended = f.Water.AttackEnds;
        Field(f.Player, "_dashCooldownTimer", .25f);
        f.Input.DashBuffer.Set();
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Attack && !f.Attack.IsAttackComplete,
            "active dodge cooldown leaves the unfinished normal attack running");
        Check(f.Water.AttackEnds == ended, "rejected dodge must not run normal attack cancellation cleanup");
        Near((float)GetField(f.Player, "_dashCooldownTimer"), .25f, "rejected attack-cancel dodge preserves cooldown");
    }

    private static void SuccessfulDodgeFlow(Fixture f)
    {
        f.Reset();
        f.Forms.ChangeForm(f.Metal);
        f.Machine.ChangeState(f.Dash);
        Check(f.Forms.GetChainReadyRemaining(f.Water.Element) == 0f,
            "a dodge with no actual incoming hit does not prepare water");
        float hp = f.Stats.CurrentHp;
        var attack = new DodgeProbeAction();
        Check(!f.Player.TakeDamage(3f, source: new object()).Applied,
            "environmental damage is still avoided during a dash");
        f.Player.TakeDamage(0f, source: attack);
        f.Player.TakeDamage(float.NaN, source: attack);
        Check(f.Forms.GetChainReadyRemaining(f.Water.Element) == 0f,
            "environmental, zero and invalid damage cannot prepare water");
        var result = f.Player.TakeDamage(3f, isParryable: false, source: attack);
        Check(!result.Applied && f.Forms.ReadyChainSkill == f.Water,
            "a real enemy attack avoided during dash prepares water even when it cannot be parried");
        Near(f.Stats.CurrentHp, hp, "successful dodge prevents health damage");
        f.Forms.UpdateChainReadiness(1f);
        f.Player.TakeDamage(3f, source: attack);
        Near(f.Forms.GetChainReadyRemaining(f.Water.Element), 4f,
            "later hits in the same dash do not refresh water readiness");
        f.Input.ChainSkillBuffer.Set();
        Property(f.Dash, "IsDashComplete", true);
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Switch && f.Forms.CurrentForm == f.Water && f.Events == 1,
            "F buffered during dodge starts water immediately after dodge completion");
        Check(!f.Player.CanDash && !f.Input.ChainSkillBuffer.IsActive,
            "dodge exit keeps its cooldown and the chain consumes F once");
    }

    private static void ChainInputBlocks(Fixture f)
    {
        var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/PlayerInputActions.inputactions");
        var action = asset?.FindAction("Player/ChainSkill", false);
        Check(action != null && action.bindings.Any(binding => binding.path == "<Keyboard>/f"),
            "the authored Player action map contains the F chain binding");
        foreach (string method in new[] { "SetMapInputBlocked", "SetShopInputBlocked", "SetFinisherInputBlocked" })
        {
            f.Reset();
            f.Input.ChainSkillBuffer.Set();
            var block = typeof(PlayerInputHandler).GetMethod(method, Hidden);
            block.Invoke(f.Input, new object[] { true });
            Check(!f.Input.ChainSkillBuffer.IsActive, method + " clears buffered F");
            block.Invoke(f.Input, new object[] { false });
            Check((bool)GetField(f.Input, "_waitForGameplayRelease"), method + " requires release before gameplay resumes");
        }
    }

    private static void ChainSnapshot(Fixture f)
    {
        ItemCatalog original = f.Player.ItemRuntime.Catalog;
        ItemCatalog temporary = Object.Instantiate(original);
        var timed = ScriptableObject.CreateInstance<ItemDefinition>();
        timed.projectId = temporary.projectId;
        timed.candidateKey = Guid.NewGuid().ToString();
        timed.itemId = "FLOW_TIMED_SWITCH_DAMAGE";
        timed.effects = new[] { new ItemEffect {
            stat = ItemStat.SwitchDamage, amount = .5f,
            trigger = ItemEffectTrigger.FormSwitchSucceeded, durationSeconds = .5f
        } };
        temporary.items = original.items.Concat(new[] { timed }).ToArray();
        try
        {
            f.Player.ItemRuntime.Catalog = temporary;
            foreach (ItemDefinition item in temporary.items)
                Check(f.Player.ItemRuntime.TryAddItem(item.itemId), "grant temporary snapshot fixture " + item.itemId);
            f.Reset();
            float baseline = f.Player.ItemRuntime.GetSwitchDamageMultiplier(f.Fire.Element);
            int begins = f.Fire.SwitchBegins;
            f.Forms.NotifyFinalComboHit(f.Water.Element);
            Near(f.Player.ItemRuntime.GetSwitchDamageMultiplier(f.Fire.Element), baseline,
                "readiness alone does not activate a switch-success item");
            Check(f.Fire.SwitchBegins == begins, "readiness does not start an attack or capture damage");
            f.Stats.AddModifier(StatType.ST_ATK, 5f, 0f);
            f.Input.ChainSkillBuffer.Set();
            f.Machine.Update();
            Check(f.Machine.CurrentState == f.Switch && f.Fire.SwitchBegins == begins + 1,
                "F starts the ready attack through the canonical switch state");
            float activeAtStart = f.Player.ItemRuntime.GetSwitchDamageMultiplier(f.Fire.Element);
            Near(activeAtStart, baseline + .5f, "committed chain activates the timed switch-damage item");
            Near(f.Fire.LastSwitchHit.ItemDamageMultiplier, activeAtStart,
                "chain captures item effects reconciled by its own successful form change");
            Near(f.Fire.LastSwitchHit.BaseDamage, f.Stats.GetStat(StatType.ST_ATK),
                "chain captures attack after the fire buff is applied at actual start");
            Check(f.Fire.LastSwitchHit.Category == PlayerAttackCategory.SwitchAttack,
                "chain retains the switch damage category");
            HitInfo captured = f.Fire.LastSwitchHit;
            f.Player.ItemRuntime.Tick(.6f);
            Check(f.Player.ItemRuntime.TryRemoveItem(timed.itemId), "remove timed item after attack start");
            f.Stats.AddModifier(StatType.ST_ATK, 20f, 0f);
            Near(f.Player.ItemRuntime.GetSwitchDamageMultiplier(f.Fire.Element), baseline,
                "live switch multiplier drops after expiration and item loss");
            Near(f.Fire.CurrentHit().ItemDamageMultiplier, captured.ItemDamageMultiplier,
                "already-started switch attack keeps its item snapshot after source expiration/removal");
            Near(f.Fire.CurrentHit().BaseDamage, captured.BaseDamage,
                "already-started switch attack keeps its attack-stat snapshot");
        }
        finally
        {
            f.Player.ItemRuntime.Catalog = original;
            foreach (ItemDefinition item in original.items) f.Player.ItemRuntime.TryAddItem(item.itemId);
            f.Reset();
            Object.DestroyImmediate(temporary);
            Object.DestroyImmediate(timed);
        }
    }

    private static void ChainFlow(Fixture f)
    {
        f.Reset();
        int before = f.Fire.SwitchBegins;
        f.Input.ChainSkillBuffer.Set();
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Idle && f.Fire.SwitchBegins == before,
            "F without a ready condition does not change form");
        f.Input.ChainSkillBuffer.Consume();
        f.Forms.NotifyFinalComboHit(f.Water.Element);
        Field(f.Player, "_formChangeCooldownTimer", 1.5f);
        f.Input.ChainSkillBuffer.Set();
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Switch && f.Forms.CurrentForm == f.Fire
            && f.Fire.SwitchBegins == before + 1 && f.Events == 1,
            "F chain succeeds while the normal switch cooldown is active");
        Near((float)GetField(f.Player, "_formChangeCooldownTimer"), 1.5f, "chain preserves the normal switch cooldown");
        Near(f.Fire.SkillCooldownRemaining, 0f, "chain starts no E cooldown");
        Near(f.Forms.GetChainReadyRemaining(f.Fire.Element), 0f, "chain consumes readiness once");
        Check(!f.Player.HasPendingSwitchAttack && !f.Input.ChainSkillBuffer.IsActive,
            "chain uses no legacy pending slot and consumes the input reservation");
        f.Fire.SwitchComplete = true;
        f.Machine.Update();
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Idle && f.Fire.SwitchBegins == before + 1,
            "completed chain returns to locomotion without replaying");

        f.Reset();
        f.Machine.ChangeState(f.Attack);
        f.Forms.NotifyFinalComboHit(f.Water.Element);
        int ended = f.Water.AttackEnds;
        f.Input.ChainSkillBuffer.Set();
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Attack && f.Water.AttackEnds == ended,
            "chain cannot cancel an executing normal hit before its recovery gate");
        f.Water.AttackComplete = true;
        f.Machine.Update();
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Switch && f.Water.AttackEnds == ended + 1,
            "completed attack exits once before the fire chain begins");
    }

    private static void ChainPriority(Fixture f)
    {
        f.Reset();
        f.Forms.ChangeForm(f.Metal);
        f.Forms.NotifyFinalComboHit(f.Metal.Element);
        f.Forms.NotifySuccessfulDodge();
        Check(f.Forms.ReadyChainSkill == f.Water, "most recently readied eligible form wins");
        var steps = f.Water.Data.SwitchAttackSteps;
        try
        {
            f.Water.Data.SwitchAttackSteps = new List<WeaponActionData>();
            Check(f.Forms.ReadyChainSkill == f.Fire, "empty newest switch slot falls back to older eligible readiness");
            f.Forms.ChangeForm(f.Fire);
            Check(f.Forms.ReadyChainSkill == null, "current form and empty slots cannot be selected");
            Check(f.Forms.GetChainReadyRemaining(f.Fire.Element) > 0f,
                "plain form assignment preserves unconsumed readiness");
        }
        finally { f.Water.Data.SwitchAttackSteps = steps; }
        Check(f.Forms.ReadyChainSkill == f.Water, "an authored noncurrent destination becomes eligible again");
        f.Forms.UpdateChainReadiness(5f);
        Check(f.Forms.ReadyChainSkill == null, "expired readiness has no fallback candidate");
    }

    private static void CounterFlow(Fixture f)
    {
        f.Reset();
        int counterBegins = f.Water.CounterBegins, switchBegins = f.Fire.SwitchBegins;
        f.Machine.ChangeState(f.Counter);
        f.Forms.NotifyFinalComboHit(f.Water.Element);
        f.Input.ChainSkillBuffer.Set();
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Counter && f.Water.CounterBegins == counterBegins + 1,
            "F cannot interrupt a counter action");
        f.Input.ChainSkillBuffer.Consume();
        f.Water.CounterComplete = true;
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Counter && f.Counter.IsComplete, "counter completion recorded before transition");
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Idle && f.Fire.SwitchBegins == switchBegins,
            "counter completion no longer launches a queued switch attack");
        Check(f.Events == 0 && f.Forms.GetChainReadyRemaining(f.Fire.Element) > 0f,
            "blocked counter input preserves unused readiness");

        PlayerUIManager savedUi = PlayerUIManager.Instance;
        try
        {
            PlayerUIManager.Instance = null;
            f.Forms.ChangeForm(f.Earth);
            f.Machine.ChangeState(f.Attack);
            typeof(BaseForm).GetField("_timer", Hidden).SetValue(f.Earth, .2f);
            Check(f.Earth.BlocksIncomingDamage(1f, -f.Player.transform.forward), "earth just-guard still arms its counter");
            f.Machine.Update();
            Check(f.Machine.CurrentState == f.Counter && !f.Earth.GuardCounterReady,
                "earth guard still enters and consumes the existing counter transition");
            f.Machine.ChangeState(f.Idle);
            f.Forms.ChangeForm(f.Water);
        }
        finally { PlayerUIManager.Instance = savedUi; }
    }

    private static void Cancellations(Fixture f)
    {
        f.BeginFireChain();
        int ends = f.Fire.SwitchEnds;
        var damage = f.Player.TakeDamage(1, isParryable: false, source: f.Telegraph,
            staggerResistLevel: StaggerResistLevel.SUPER_ARMOR);
        Check(damage.Applied && !f.Player.HasPendingSwitchAttack, "actual damage does not leave a pending legacy attack");
        f.Machine.Update();
        Check(f.Machine.CurrentState != f.Switch && f.Fire.SwitchEnds == ends + 1,
            "hit reaction interrupts and cleans up the active chain once");
        Near(f.Forms.GetChainReadyRemaining(f.Fire.Element), 0f, "interrupted chain does not refund consumed readiness");

        f.BeginFireChain();
        ends = f.Fire.SwitchEnds;
        f.Forms.NotifySuccessfulDodge();
        f.Input.ChainSkillBuffer.Set();
        f.Player.enabled = false;
        Check(!f.Player.HasPendingSwitchAttack && f.Fire.SwitchEnds == ends + 1,
            "controller disable cancels and cleans up the active chain");
        Check(f.Forms.GetChainReadyRemaining(f.Water.Element) == 0f && !f.Input.ChainSkillBuffer.IsActive,
            "controller disable clears readiness and buffered F input");
        f.Player.enabled = true;
    }

    private static T State<T>(StateMachine machine) where T : class, IPlayerState
    {
        var transitions = (Dictionary<Type, List<Transition>>)GetField(machine, "_transitions");
        T state = machine.CurrentState as T ?? transitions.Values.SelectMany(t => t).Select(t => t.To).OfType<T>().FirstOrDefault();
        if (state == null) throw new InvalidOperationException("Canonical state not found: " + typeof(T).Name);
        return state;
    }
    private static object GetField(object target, string name) => target.GetType().GetField(name, Hidden)?.GetValue(target)
        ?? throw new InvalidOperationException("Field not found: " + name);
    private static void Field(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, Hidden);
        if (field == null) throw new InvalidOperationException("Field not found: " + name);
        field.SetValue(target, value);
    }
    private static void Property(object target, string name, object value)
    {
        PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property?.SetMethod == null) throw new InvalidOperationException("Property setter not found: " + name);
        property.SetValue(target, value);
    }
    private static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < 0.001f, message + ": " + actual + " != " + expected);
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException("Baekho Items flow: " + message); }
}
