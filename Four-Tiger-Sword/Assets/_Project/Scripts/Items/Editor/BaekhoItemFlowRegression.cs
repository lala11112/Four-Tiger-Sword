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

    private sealed class ProbeForm : BaseForm
    {
        private readonly ElementType element;
        public int SwitchBegins, SwitchUpdates, SwitchEnds, ParryBegins, ParryEnds, CounterBegins, CounterEnds;
        public int AttackBegins, AttackEnds;
        public bool SwitchComplete, CounterComplete, AttackComplete;
        public HitInfo LastSwitchHit;
        public ProbeForm(ElementType element, WeaponActionDataSO data) : base(data) => this.element = element;
        public override ElementType Element => element;
        public override bool CanSwitchAttack => true;
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
        public override void BeginParry() => ParryBegins++;
        public override void EndParry() => ParryEnds++;
        public override void BeginCounter() { CounterBegins++; CounterComplete = false; }
        public override void UpdateCounter(out bool isComplete) => isComplete = CounterComplete;
        public override void EndCounter() => CounterEnds++;
    }

    private sealed class Fixture
    {
        public PlayerController Player;
        public PlayerStatData Data;
        public ProbeForm Water, Fire, Metal;
        public int Events;
        public PlayerIdleState Idle;
        public PlayerParryState Parry;
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
            Input.AttackBuffer.Consume(); Input.DashBuffer.Consume(); Input.JumpBuffer.Consume();
            Input.SkillBuffer.Consume(); Input.UltimateBuffer.Consume(); Input.ConsumeFormInput();
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

        public void BeginParry()
        {
            Reset();
            Property(Input, "IsForm1Pressed", true);
            Check(Player.IsGround(), "fixture ground query");
            Check(Player.HasFormParryRequest, "numeric requested form plus telegraph makes actual parry request");
            Machine.Update();
            Check(Machine.CurrentState == Parry && Parry.IsParryWindowActive, "canonical state machine enters parry");
        }

        public void FirstParriedHit()
        {
            float before = Stats.CurrentSp, hp = Stats.CurrentHp;
            var result = Player.TakeDamage(9f, isParryable: true, source: Telegraph);
            Check(result.Outcome == DamageOutcome.Parried && Parry.ParriedHitCount == 1, "first hit is parried through controller");
            Near(Stats.CurrentSp, before - 6f, "first parried hit spends one switch cost");
            Near(Stats.CurrentHp, hp, "parry prevents health damage");
            Check(Forms.CurrentForm == Fire && Events == 1 && Player.HasPendingSwitchAttack, "first parry commits and queues exactly once");
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
        WeaponActionDataSO actions = null;
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

            ItemCatalog catalog = source.ItemRuntime != null ? source.ItemRuntime.Catalog : null;
            if (catalog == null || catalog.items.Length != 20)
                catalog = AssetDatabase.FindAssets("t:ItemCatalog", new[] { BaekhoItemImporter.OutputRoot })
                    .Select(g => AssetDatabase.LoadAssetAtPath<ItemCatalog>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(c => c != null && c.items.Length == 20);
            Check(catalog != null && catalog.items.Length == 20, "imported actual twenty-item catalog required");
            data = ScriptableObject.CreateInstance<PlayerStatData>();
            data.baseHP = 100; data.baseSP = 100; data.baseSpRegen = 20; data.baseSpRegenDelay = 1;
            player.StatManager.Initialize(data);
            player.ItemRuntime.Catalog = catalog;

            var manager = new FormManager(player);
            actions = ScriptableObject.CreateInstance<FireFormActionDataSO>();
            actions.ComboSteps = new List<WeaponActionData> { new WeaponActionData { AnimationName = "", Duration = 1f } };
            actions.SkillSteps = new List<WeaponActionData> { new WeaponActionData { AnimationName = "", Duration = 1f } };
            var water = new ProbeForm(ElementType.ELEMENT_WATER, actions);
            var fire = new ProbeForm(ElementType.ELEMENT_FIRE, actions);
            var metal = new ProbeForm(ElementType.ELEMENT_GOLD, actions);
            manager.AddTransition(fire, () => input.IsForm1Pressed);
            manager.AddTransition(water, () => input.IsForm2Pressed);
            manager.AddTransition(metal, () => input.IsForm4Pressed);
            manager.CanTransition = () => player.StateMachine.CurrentState is PlayerIdleState || player.StateMachine.CurrentState is PlayerRunState;
            Property(player, "FormManager", manager);
            manager.ChangeForm(water);
            player.ItemRuntime.Initialize(player);
            foreach (ItemDefinition item in catalog.items) Check(player.ItemRuntime.TryAddItem(item.itemId), "grant actual item " + item.itemId);
            Check(player.ItemRuntime.OwnedItems.Count == 20, "all twenty items granted through public API");
            Near(player.SwitchSpCost, 6, "actual package switch cost");
            Near(player.DodgeSpCost, 15, "actual package passive dodge cost");

            fixture = new Fixture
            {
                Player = player, Data = data, Water = water, Fire = fire, Metal = metal,
                Idle = State<PlayerIdleState>(player.StateMachine), Parry = State<PlayerParryState>(player.StateMachine),
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
            AttackDashFlow(fixture);
            ParryAutoAttack(fixture);
            DelayedSwitchSnapshot(fixture);
            CounterFlow(fixture);
            Cancellations(fixture);
            Check(inputStates.All(p => p.Key.enabled == p.Value), "live input action enable states unchanged");
            string result = "PASS: " + checks + " flow assertions; actual FormManager/controller/state instances, direct switch, rejection gates, attack-cancel dash spending, multihit parry, delayed effect snapshots, fresh counter, queued attack and interruption cleanup.";
            Debug.Log("[Baekho Items] " + result);
            return result;
        }
        finally
        {
            if (fixture != null) fixture.Telegraphs.Remove(fixture.Telegraph);
            PlayerUIManager.Instance = savedUi;
            if (stage != null) Object.DestroyImmediate(stage);
            if (data != null) Object.DestroyImmediate(data);
            if (actions != null) Object.DestroyImmediate(actions);
            Time.timeScale = savedScale;
            UnityEngine.Random.state = savedRandom;
        }
    }

    private static void DirectAndFailures(Fixture f)
    {
        f.Reset();
        int begins = f.Fire.SwitchBegins;
        float before = f.Stats.CurrentSp;
        Check(f.Forms.TrySwitchForm(f.Fire), "direct switch succeeds");
        Near(f.Stats.CurrentSp, before - 6, "direct switch spends exactly six SP");
        Check(f.Events == 1 && f.Machine.CurrentState == f.Switch && f.Fire.SwitchBegins == begins + 1, "direct success emits once and immediately enters canonical switch attack");
        Check(!f.Player.HasPendingSwitchAttack && !f.Player.CanChangeForm, "direct attack consumes pending slot and starts cooldown");
        f.Machine.Update();
        Near(f.Stats.CurrentSp, before - 6, "switch attack does not spend E cost");
        Near(f.Fire.SkillCooldownRemaining, 0, "switch attack does not start E cooldown");
        Check(!f.Forms.TrySwitchForm(f.Metal) && f.Events == 1, "cooldown rejects second switch without event");
        Near(f.Stats.CurrentSp, before - 6, "cooldown failure does not spend SP");

        f.Reset();
        Check(!f.Forms.TrySwitchForm(f.Water) && f.Events == 0, "same-form switch rejected");
        Near(f.Stats.CurrentSp, 100, "same-form request does not consume");
        f.Stats.TryConsumeSp(f.Stats.CurrentSp - 5);
        Check(!f.Forms.TrySwitchForm(f.Fire) && f.Events == 0 && f.Player.CanChangeForm, "insufficient SP rejects without cooldown/event");
        Near(f.Stats.CurrentSp, 5, "insufficient switch does not consume");

        f.Reset();
        f.Machine.ChangeState(f.Counter);
        Check(!f.Forms.TrySwitchForm(f.Fire) && f.Events == 0, "direct switch blocked during counter");
        Near(f.Stats.CurrentSp, 100, "state-blocked request does not consume");
        Check(f.Player.CanChangeForm, "state-blocked request does not start cooldown");
    }

    private static void DashFlow(Fixture f)
    {
        f.Reset();
        float cost = f.Player.DodgeSpCost;
        f.Input.DashBuffer.Set();
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Dash, "actual transition enters dash");
        Near(f.Stats.CurrentSp, 100 - cost, "dash spends only on entry");
        f.Machine.Update();
        Near(f.Stats.CurrentSp, 100 - cost, "dash update does not spend again");
        f.Machine.ChangeState(f.Idle);
        Check(!f.Player.CanDash, "successful dash starts cooldown on exit");

        f.Reset();
        f.Stats.TryConsumeSp(f.Stats.CurrentSp - (cost - 1));
        f.Input.DashBuffer.Set();
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Idle, "insufficient SP blocks actual dash transition");
        Near(f.Stats.CurrentSp, cost - 1, "blocked dash does not consume");
        Near((float)GetField(f.Player, "_dashCooldownTimer"), 0, "blocked dash does not start cooldown");
        f.Machine.ChangeState(f.Dash); // Even a forced entry must retain the resource guard.
        Check(f.Dash.IsDashComplete, "forced unaffordable dash immediately completes");
        f.Machine.ChangeState(f.Idle);
        Near((float)GetField(f.Player, "_dashCooldownTimer"), 0, "failed forced entry does not start cooldown");
    }

    private static void AttackDashFlow(Fixture f)
    {
        f.Reset();
        f.Machine.ChangeState(f.Attack);
        int ended = f.Water.AttackEnds;
        float cost = f.Player.DodgeSpCost;
        Check(!f.Attack.IsAttackComplete, "attack-cancel fixture starts with an unfinished normal attack");
        f.Input.DashBuffer.Set();
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Dash, "unfinished normal attack cancels directly into actual dash state");
        Check(f.Water.AttackEnds == ended + 1, "successful dodge ends the interrupted normal attack exactly once");
        Near(f.Stats.CurrentSp, 100 - cost, "attack-cancel dodge spends the item-adjusted cost once");
        f.Machine.Update();
        Near(f.Stats.CurrentSp, 100 - cost, "attack-cancel dodge update never spends a second cost");
        Check(f.Water.AttackEnds == ended + 1, "dash update does not end the interrupted attack again");

        f.Reset();
        f.Machine.ChangeState(f.Attack);
        ended = f.Water.AttackEnds;
        f.Stats.TryConsumeSp(f.Stats.CurrentSp - (cost - 1));
        Field(f.Stats, "_spRegenTimer", .37f);
        float regenDelay = (float)GetField(f.Stats, "_spRegenTimer");
        float cooldown = (float)GetField(f.Player, "_dashCooldownTimer");
        f.Input.DashBuffer.Set();
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Attack && !f.Attack.IsAttackComplete,
            "insufficient SP leaves the unfinished normal attack running");
        Check(f.Water.AttackEnds == ended, "rejected dodge must not run normal attack cancellation cleanup");
        Near(f.Stats.CurrentSp, cost - 1, "rejected attack-cancel dodge preserves SP");
        Near((float)GetField(f.Player, "_dashCooldownTimer"), cooldown, "rejected attack-cancel dodge preserves cooldown");
        Near((float)GetField(f.Stats, "_spRegenTimer"), regenDelay, "rejected attack-cancel dodge preserves regeneration delay");
    }

    private static void DelayedSwitchSnapshot(Fixture f)
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
            f.BeginParry();
            float baseline = f.Player.ItemRuntime.GetSwitchDamageMultiplier(f.Fire.Element);
            int begins = f.Fire.SwitchBegins;
            f.FirstParriedHit();
            Near(f.Player.ItemRuntime.GetSwitchDamageMultiplier(f.Fire.Element), baseline + .5f,
                "parry success activates the timed switch-damage source while attack is queued");
            Check(f.Fire.SwitchBegins == begins, "queued parry attack has not captured damage yet");
            f.Player.ItemRuntime.Tick(.6f);
            Near(f.Player.ItemRuntime.GetSwitchDamageMultiplier(f.Fire.Element), baseline,
                "timed switch bonus expires while parry still owns the action");
            f.Stats.AddModifier(StatType.ST_ATK, 5f, 0f);
            float attackAtStart = f.Stats.GetStat(StatType.ST_ATK);
            Property(f.Parry, "IsComplete", true);
            f.Machine.Update();
            Check(f.Machine.CurrentState == f.Switch && f.Fire.SwitchBegins == begins + 1,
                "delayed parry attack eventually enters the canonical switch state");
            Near(f.Fire.LastSwitchHit.ItemDamageMultiplier, baseline,
                "delayed switch attack excludes a buff that expired before actual start");
            Near(f.Fire.LastSwitchHit.BaseDamage, attackAtStart,
                "delayed switch attack captures the attack stat at actual start");
            Check(f.Fire.LastSwitchHit.Category == PlayerAttackCategory.SwitchAttack,
                "delayed parry attack retains switch damage category");

            f.BeginParry();
            f.FirstParriedHit();
            float activeAtStart = f.Player.ItemRuntime.GetSwitchDamageMultiplier(f.Fire.Element);
            Property(f.Parry, "IsComplete", true);
            f.Machine.Update();
            Near(f.Fire.LastSwitchHit.ItemDamageMultiplier, activeAtStart,
                "delayed switch captures a still-active timed source when it starts");
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

    private static void ParryAutoAttack(Fixture f)
    {
        f.BeginParry();
        int before = f.Fire.SwitchBegins;
        f.FirstParriedHit();
        float paid = f.Stats.CurrentSp;
        Check(f.Machine.CurrentState == f.Parry && f.Fire.SwitchBegins == before, "parry queues switch attack while its window runs");
        for (int i = 0; i < 2; i++) Check(f.Player.TakeDamage(9f, source: f.Telegraph).Outcome == DamageOutcome.Parried, "later combo hit remains parried");
        Check(f.Parry.ParriedHitCount == 3 && f.Events == 1, "multihit parry commits only once");
        Near(f.Stats.CurrentSp, paid, "later parried hits do not spend more SP");
        Property(f.Parry, "IsComplete", true);
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Switch && f.Fire.SwitchBegins == before + 1, "finished parry automatically enters pending switch attack");
        Check(!f.Player.HasPendingSwitchAttack, "automatic attack takes queued slot once");
        Near(f.Stats.CurrentSp, paid, "automatic post-parry attack has no second cost");
    }

    private static void CounterFlow(Fixture f)
    {
        f.BeginParry();
        f.FirstParriedHit();
        f.Input.AttackBuffer.Set();
        Check(!f.Parry.CanCounter, "same/pre-success press cannot counter");
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Parry, "stale press stays in parry");
        uint press = f.Input.AttackPressId;
        Property(f.Input, "AttackPressId", press + 1);
        f.Input.AttackBuffer.Set();
        Check(f.Parry.CanCounter, "fresh press activates counter");
        int counterBegins = f.Fire.CounterBegins, switchBegins = f.Fire.SwitchBegins;
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Counter && f.Fire.CounterBegins == counterBegins + 1, "canonical counter has priority");
        Check(f.Player.HasPendingSwitchAttack && f.Fire.SwitchBegins == switchBegins, "queued attack waits through counter");
        f.Fire.CounterComplete = true;
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Counter && f.Counter.IsComplete, "counter completion recorded before transition");
        f.Machine.Update();
        Check(f.Machine.CurrentState == f.Switch && f.Fire.SwitchBegins == switchBegins + 1, "queued switch attack starts after counter completion");
        Check(f.Events == 1 && !f.Player.HasPendingSwitchAttack, "counter path never recommits switch");
        Near(f.Stats.CurrentSp, 94, "counter plus queued attack preserves single switch spend");
    }

    private static void Cancellations(Fixture f)
    {
        f.BeginParry();
        f.FirstParriedHit();
        int begins = f.Fire.SwitchBegins;
        var damage = f.Player.TakeDamage(1, isParryable: false, source: f.Telegraph);
        Check(damage.Applied && !f.Player.HasPendingSwitchAttack, "non-parryable health damage cancels pending attack");
        Property(f.Parry, "IsComplete", true);
        f.Machine.Update();
        Check(f.Machine.CurrentState != f.Switch && f.Fire.SwitchBegins == begins, "damaged parry cannot start cancelled attack");

        f.BeginParry();
        f.FirstParriedHit();
        begins = f.Fire.SwitchBegins;
        f.Player.enabled = false;
        Check(!f.Player.HasPendingSwitchAttack && f.Fire.SwitchBegins == begins, "controller disable cancels pending attack");
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
