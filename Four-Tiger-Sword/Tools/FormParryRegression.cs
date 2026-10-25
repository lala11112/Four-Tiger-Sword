// Run in Edit Mode with Unity Pipeline run_script, entry FormParryRegression.Run.
// Timing is advanced through existing private timers; production entry, damage,
// form selection, transitions and lifecycle methods are exercised directly.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class FormParryRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Set(object target, string property, object value)
        => target.GetType().GetProperty(property).SetValue(target, value);
    private static void Field(object target, Type owner, string name, object value)
        => owner.GetField(name, Hidden).SetValue(target, value);
    private static float Cooldown(PlayerController player)
        => (float)typeof(PlayerController).GetField("_formChangeCooldownTimer", Hidden).GetValue(player);
    private static void Cooldown(PlayerController player, float value)
        => Field(player, typeof(PlayerController), "_formChangeCooldownTimer", value);
    private static void Select(PlayerInputHandler input, int number, bool fast = false)
    {
        for (int i = 1; i <= 5; i++) Set(input, "IsForm" + i + "Pressed", i == number);
        Set(input, "IsFastFormPressed", fast);
    }
    private static void Press(PlayerInputHandler input, uint id, bool held = true)
    {
        Set(input, "AttackPressId", id);
        Set(input, "AttackHeldTime", held ? 2f : 0f);
        Set(input, "IsAttackHeld", held);
        input.AttackBuffer.Set(0.5f);
    }

    public static string Run()
    {
        Check(!Application.isPlaying, "Run FormParryRegression in Edit Mode.");
        var previousUI = PlayerUIManager.Instance;
        var sourceSet = (HashSet<object>)typeof(ParryEventBus).GetField("_activeSources", StaticHidden).GetValue(null);
        var previousSources = new List<object>(sourceSet);
        var previousAttackSource = ParryEventBus.CurrentAttackSource;
        var firstEvent = typeof(ParryEventBus).GetField("OnFirstTelegraphStart", StaticHidden);
        var lastEvent = typeof(ParryEventBus).GetField("OnLastTelegraphEnd", StaticHidden);
        var previousFirst = firstEvent.GetValue(null);
        var previousLast = lastEvent.GetValue(null);
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject ground = null;
        EarthFormActionDataSO earthData = null;
        try
        {
            PlayerUIManager.Instance = null;
            firstEvent.SetValue(null, null);
            lastEvent.SetValue(null, null);
            sourceSet.Clear();
            var go = new GameObject("FormParryRegressionPlayer");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = new Vector3(50000f, 50000f, 50000f);
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/_Project/Animations/AnimController/BDJ.controller");
            Check(animator.runtimeAnimatorController != null, "Player animation controller missing.");
            animator.Rebind(); animator.Update(0f);
            var player = go.AddComponent<PlayerController>();
            Set(player, "Animator", animator);
            Set(player, "Input", go.GetComponent<PlayerInputHandler>());
            Set(player, "Controller", go.GetComponent<CharacterController>());
            Set(player, "Movement", go.GetComponent<PlayerMovement>());
            Set(player, "StatManager", go.GetComponent<PlayerStatManager>());
            player.Movement.Initialize(player);
            Field(player, typeof(PlayerController), "_hitStopDuration", 0f);
            Field(player, typeof(PlayerController), "_groundLayer", (LayerMask)1);
            var values = (Dictionary<StatType, float>)typeof(PlayerStatManager).GetField("finalValues", Hidden).GetValue(player.StatManager);
            values[StatType.ST_ATK_SPD] = 1f;
            values[StatType.ST_HP] = 1000f;

            // IsGround queries the default physics scene, even for a preview-scene player.
            ground = new GameObject("FormParryRegressionGround", typeof(BoxCollider));
            ground.transform.position = go.transform.position + Vector3.down * 1.52f;
            ground.transform.localScale = new Vector3(100f, 1f, 100f);
            // CharacterController.Move uses the player's preview physics scene.
            // Keep a second floor there as well as the default-scene query floor.
            var previewGround = UnityEngine.Object.Instantiate(ground);
            previewGround.name = "FormParryRegressionPreviewGround";
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(previewGround, scene);
            Physics.SyncTransforms();
            Check(player.IsGround(), "Regression floor did not satisfy the real grounded test.");
            Check(Mathf.Approximately(player.FormChangeCooldown, 0.5f), "Default shared cooldown must be 0.5 seconds.");
            var fixture = new Fixture(player, sourceSet);

            int checkedPairs = 0;
            for (int from = 0; from < 5; from++)
            for (int to = 0; to < 5; to++)
            {
                if (from == to) continue;
                fixture.Reset(from);
                var oldForm = fixture.Forms[from];
                var target = fixture.Forms[to];
                Select(player.Input, to + 1);
                Press(player.Input, 10); // Form and attack pressed together before success.
                fixture.Frame();
                var parry = player.StateMachine.CurrentState as PlayerParryState;
                string pair = (from + 1) + " -> " + (to + 1);
                Check(parry != null && oldForm.ParryBegins == 1, "No old-form parry: " + pair);
                Check(fixture.Manager.CurrentForm == oldForm && target.ParryBegins == 0 && player.CanChangeForm,
                    "Form/cooldown changed before a blocked hit: " + pair);
                Check(!parry.CanCounter, "Attack accepted before first hit: " + pair);

                // Later input cannot replace the choice captured on parry entry.
                Select(player.Input, ((to + 1) % 5) + 1, true);
                fixture.Manager.Update();
                Field(oldForm, typeof(BaseForm), "_skillCooldownTimer", 3.25f);
                Field(target, typeof(BaseForm), "_ultimateCooldownTimer", 9.5f);
                int sequenceStart = fixture.Log.Count;
                float gaugeBefore = player.StatManager.CurrentUltimateGauge;
                var hit = player.TakeDamage(10f, source: fixture.Source);
                Check(hit.Outcome == DamageOutcome.Parried && fixture.Manager.CurrentForm == target,
                    "First blocked hit did not select the captured form: " + pair);
                Check(player.StateMachine.CurrentState == parry && parry.ParriedHitCount == 1
                    && parry.IsCounterWindowActive, "First hit re-entered/lost parry state: " + pair);
                Check(fixture.FormChanges == 1 && oldForm.ParryEnds == 1 && target.ParryBegins == 1,
                    "First-hit lifecycle count: " + pair);
                Check(fixture.Log.Count >= sequenceStart + 4
                    && fixture.Log[sequenceStart] == oldForm.Name + ".EndParry"
                    && fixture.Log[sequenceStart + 1] == oldForm.Name + ".Unequip"
                    && fixture.Log[sequenceStart + 2] == target.Name + ".Equip"
                    && fixture.Log[sequenceStart + 3] == target.Name + ".BeginParry",
                    "Old/new parry lifecycle order: " + pair);
                Check(Mathf.Approximately(oldForm.SkillCooldownRemaining, 3.25f)
                    && Mathf.Approximately(target.UltimateCooldownRemaining, 9.5f), "Form cooldown instance lost: " + pair);
                Check(Mathf.Approximately(Cooldown(player), 0.5f) && !player.CanChangeForm
                    && player.StatManager.CurrentUltimateGauge > gaugeBefore, "Success cooldown/reward missing: " + pair);
                Check(!player.Input.AttackBuffer.IsActive && !parry.CanCounter, "Pre-success attack leaked: " + pair);
                player.Input.AttackBuffer.Set(0.5f); // Same held press, even if another producer fills the buffer.
                Check(!parry.CanCounter, "Held/simultaneous attack became a fresh press: " + pair);
                player.Input.AttackBuffer.Consume();

                Cooldown(player, 0.125f);
                Press(player.Input, 11, false);
                player.TakeDamage(10f, source: fixture.Source);
                player.TakeDamage(10f, source: new object());
                Check(parry.ParriedHitCount == 3 && fixture.FormChanges == 1
                    && target.ParryBegins == 1 && Mathf.Approximately(Cooldown(player), 0.125f),
                    "Follow-up/multi-enemy hit restarted form lifecycle/cooldown: " + pair);
                Check(parry.CanCounter, "Fresh post-success attack rejected: " + pair);
                Set(player, "PendingHitReaction", PlayerHitReaction.Knockback);
                var transitions = (Dictionary<Type, List<Transition>>)typeof(StateMachine).GetField("_transitions", Hidden).GetValue(player.StateMachine);
                var counterTransition = transitions[typeof(PlayerParryState)].Find(t => t.To is PlayerCounterState);
                Check(counterTransition != null && !counterTransition.Condition(), "Counter ignored active knockback: " + pair);
                player.ConsumeHitReaction();
                Select(player.Input, 0);
                player.StateMachine.Update();
                Check(player.StateMachine.CurrentState is PlayerCounterState && target.CounterBegins == 1
                    && !player.Input.AttackBuffer.IsActive && Mathf.Approximately(Cooldown(player), 0.125f),
                    "Changed-form counter or success cooldown exit failed: " + pair);
                int targetUpdates = target.CounterUpdates;
                // A state owns all three lifecycle calls even if another system changes CurrentForm.
                fixture.Manager.ChangeForm(oldForm);
                player.StateMachine.Update();
                player.StateMachine.ChangeState(new PlayerIdleState(player));
                Check(target.CounterUpdates > targetUpdates && target.CounterEnds == 1
                    && oldForm.CounterUpdates == 0 && oldForm.CounterEnds == 0, "Counter owner changed mid-action: " + pair);
                checkedPairs++;
            }

            fixture.Reset(0);
            Select(player.Input, 2); fixture.Frame();
            var sourceLess = (PlayerParryState)player.StateMachine.CurrentState;
            player.TakeDamage(1f); Cooldown(player, 0.17f);
            Select(player.Input, 5, true);
            player.TakeDamage(1f); player.TakeDamage(1f);
            Check(sourceLess.ParriedHitCount == 3 && fixture.Manager.CurrentForm == fixture.Forms[1]
                && fixture.FormChanges == 1 && fixture.Forms[1].ParryBegins == 1
                && Mathf.Approximately(Cooldown(player), 0.17f), "Null attack source repeated the first-success transition.");
            Field(sourceLess, typeof(PlayerParryState), "_timer", 2f); sourceLess.Update();
            Check(!sourceLess.IsParryWindowActive && sourceLess.IsCounterWindowActive && !sourceLess.IsComplete,
                "Source-less success fell back into first-hit failure.");
            Field(sourceLess, typeof(PlayerParryState), "_timer", 0.49f);
            player.Input.AttackBuffer.Consume(); sourceLess.Update();
            // Explicitly cross the 0.5-second grace limit independently of Edit Mode delta time.
            Field(sourceLess, typeof(PlayerParryState), "_timer", 0.51f); sourceLess.Update();
            Check(sourceLess.IsComplete && !sourceLess.IsCounterWindowActive, "Counter grace did not expire.");
            Cooldown(player, 0f); Select(player.Input, 5, true); fixture.Frame();
            Check(fixture.Manager.CurrentForm == fixture.Forms[1] && player.CanChangeForm,
                "Parry-exit-frame form input leaked into locomotion after cooldown expired.");

            fixture.Reset(0);
            Select(player.Input, 2); fixture.Frame(); player.TakeDamage(1f, source: fixture.Source);
            var tracked = (PlayerParryState)player.StateMachine.CurrentState;
            Field(tracked, typeof(PlayerParryState), "_timer", 2f); tracked.Update();
            Check(tracked.IsParryWindowActive, "An unfinished first-hit action lost its multi-hit parry window.");
            fixture.Source.Finish();
            Field(tracked, typeof(PlayerParryState), "_timer", 2f); tracked.Update();
            Check(tracked.IsParryWindowActive, "The locked source's active telegraph stopped extending the parry window.");
            fixture.Sources.Clear(); fixture.Sources.Add(new object());
            Field(tracked, typeof(PlayerParryState), "_timer", 0.02f); tracked.Update();
            Check(!tracked.IsParryWindowActive && tracked.IsCounterWindowActive,
                "An unrelated enemy telegraph prolonged the completed first-hit action.");

            fixture.Reset(0);
            Select(player.Input, 2); fixture.Frame();
            var failed = (PlayerParryState)player.StateMachine.CurrentState;
            Check(player.CanChangeForm, "Parry attempt starts cooldown before result.");
            Field(failed, typeof(PlayerParryState), "_timer", 1.1f); failed.Update();
            Select(player.Input, 0); fixture.Frame();
            Check(fixture.Manager.CurrentForm == fixture.Forms[0] && fixture.FormChanges == 0
                && Mathf.Approximately(Cooldown(player), 0.5f) && fixture.Forms[0].ParryEnds == 1,
                "Failed parry changed form or omitted failure-end cooldown.");
            Select(player.Input, 3, true); fixture.Frame();
            Check(fixture.Manager.CurrentForm == fixture.Forms[0] && !player.CanParry,
                "Failed parry cooldown did not block numeric/Q changes and another parry.");
            Cooldown(player, 0f); Select(player.Input, 0); fixture.Frame();
            Check(fixture.Manager.CurrentForm == fixture.Forms[0], "Cooldown-period input was queued.");

            fixture.Reset(0);
            Select(player.Input, 2); fixture.Frame(); Select(player.Input, 0);
            player.TakeDamage(1f, poiseDamage: 0f, staggerResistLevel: StaggerResistLevel.HIGH, isParryable: false);
            fixture.Frame();
            Check(player.StateMachine.CurrentState is PlayerHitState && fixture.Manager.CurrentForm == fixture.Forms[0]
                && Mathf.Approximately(Cooldown(player), 0.5f), "Hit interruption did not preserve old form/start failure cooldown.");

            VerifyCooldownAndSelection(fixture);
            VerifyPriority(fixture);
            VerifyKeyMapping(player);

            fixture.Reset(0);
            Select(player.Input, 2); fixture.Frame(); player.TakeDamage(1f, source: fixture.Source);
            Select(player.Input, 0); Press(player.Input, 100, false); fixture.Frame();
            var counter = (PlayerCounterState)player.StateMachine.CurrentState;
            fixture.Forms[1].CounterComplete = true; counter.Update();
            Cooldown(player, 0f); Select(player.Input, 5, true); fixture.Frame();
            Check(fixture.Manager.CurrentForm == fixture.Forms[1] && player.CanChangeForm,
                "Counter-exit-frame form input leaked into locomotion after cooldown expired.");

            fixture.Reset(0);
            sourceSet.Clear(); Select(player.Input, 0);
            earthData = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<EarthFormActionDataSO>(
                "Assets/_Project/Scripts/FormActions/EarthFormAction.asset"));
            Check(earthData != null, "Earth action data required for the just-guard regression.");
            earthData.HitSound = null; earthData.HitVFX = null;
            var earth = new EarthForm(earthData); fixture.Manager.ChangeForm(earth);
            player.StateMachine.ChangeState(new PlayerAttackState(player));
            Field(earth, typeof(BaseForm), "_timer", 0.2f);
            Cooldown(player, 0.25f);
            float hpBeforeGuard = player.StatManager.CurrentHp;
            var guardResult = player.TakeDamage(10f, power: -player.transform.forward);
            Check(guardResult.Outcome == DamageOutcome.Blocked && earth.GuardCounterReady
                && player.StatManager.CurrentHp == hpBeforeGuard, "Earth front just-guard no longer blocks.");
            player.StateMachine.Update();
            Check(player.StateMachine.CurrentState is PlayerCounterState && fixture.Manager.CurrentForm == earth
                && !earth.GuardCounterReady && Mathf.Approximately(Cooldown(player), 0.25f),
                "Earth separate guard counter changed form, consumed shared cooldown, or required fresh attack.");

            fixture.Reset(0);
            Select(player.Input, 2); Field(player.StatManager, typeof(PlayerStatManager), "_currentHp", 0f);
            fixture.Frame();
            Check(player.StateMachine.CurrentState is PlayerDieState && fixture.Forms[0].ParryBegins == 0,
                "Parry overrode lethal/death transition.");

            return "PASS: " + checkedPairs + " form pairs (old-form entry, success-only captured target, lifecycle order, retained form cooldowns, fresh counter input, pinned counter owner); null-source/multi-enemy hits; first-hit failure and hit interruption cooldown; numeric/Q shared cooldown and no queue; grounded/telegraph/current-form restrictions; idle/run/attack and hit/death priority; exit-frame input consumption; real 1-5 mapping; Earth just-guard counter.";
        }
        finally
        {
            if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
            EditorSceneManager.ClosePreviewScene(scene);
            if (earthData != null) UnityEngine.Object.DestroyImmediate(earthData);
            PlayerUIManager.Instance = previousUI;
            sourceSet.Clear(); foreach (var source in previousSources) sourceSet.Add(source);
            typeof(ParryEventBus).GetProperty("CurrentAttackSource").SetValue(null, previousAttackSource);
            firstEvent.SetValue(null, previousFirst);
            lastEvent.SetValue(null, previousLast);
        }
    }

    private static void VerifyCooldownAndSelection(Fixture fixture)
    {
        var player = fixture.Player;
        fixture.Reset(0); fixture.Sources.Clear();
        Check(player.CanChangeForm, "Initial equip consumed shared cooldown.");
        Select(player.Input, 1); fixture.Frame();
        Check(player.CanChangeForm && fixture.FormChanges == 0, "Selecting current form consumed cooldown.");
        fixture.Sources.Add(fixture.Source);
        Check(!player.HasFormParryRequest, "Selecting current form entered parry.");
        Select(player.Input, 0, true);
        Check(fixture.Manager.GetRequestedForm() == null && !player.HasFormParryRequest, "Q requested a parry.");
        fixture.Frame();
        Check(fixture.Manager.CurrentForm == fixture.Forms[1] && Mathf.Approximately(Cooldown(player), 0.5f),
            "Q did not perform a normal next-form change/start shared cooldown.");
        Select(player.Input, 3, true); fixture.Frame();
        Check(fixture.Manager.CurrentForm == fixture.Forms[1] && !player.CanParry, "Q cooldown did not block numeric parry/change.");
        Cooldown(player, 0f); fixture.Sources.Clear(); Select(player.Input, 4); fixture.Frame();
        Check(fixture.Manager.CurrentForm == fixture.Forms[3] && Mathf.Approximately(Cooldown(player), 0.5f),
            "Numeric normal change did not consume shared cooldown.");
        Select(player.Input, 0, true); fixture.Frame();
        Check(fixture.Manager.CurrentForm == fixture.Forms[3], "Numeric cooldown did not block Q.");
        float beforeTick = Cooldown(player);
        typeof(PlayerController).GetMethod("UpdateFormChangeCooldown", Hidden).Invoke(player, null);
        Check(Mathf.Abs(Cooldown(player) - Mathf.Max(0f, beforeTick - Time.deltaTime)) < 0.0001f,
            "Shared cooldown is not updated by elapsed game time.");

        fixture.Reset(0); Select(player.Input, 2);
        fixture.Sources.Clear(); Check(!player.CanParry, "Parry allowed without an enemy telegraph.");
        fixture.Sources.Add(fixture.Source);
        Vector3 grounded = player.transform.position;
        player.transform.position += Vector3.up * 20f; Physics.SyncTransforms();
        Check(!player.IsGround() && !player.CanParry, "Parry allowed while airborne.");
        player.transform.position = grounded; Physics.SyncTransforms();
        Check(player.CanParry, "Grounded different-form telegraph selection did not allow parry.");
        Select(player.Input, 0);
        Check(!player.HasFormParryRequest && fixture.Manager.GetRequestedForm() == null,
            "Parry can be requested without a numeric form key.");
        Check(typeof(PlayerInputHandler).GetField("ParryBuffer") == null,
            "Legacy F parry buffer remains connected to the input contract.");
    }

    private static void VerifyPriority(Fixture fixture)
    {
        var player = fixture.Player;
        for (int state = 0; state < 3; state++)
        {
            fixture.Reset(0);
            if (state == 1) player.StateMachine.ChangeState(new PlayerRunState(player));
            if (state == 2) player.StateMachine.ChangeState(new PlayerAttackState(player));
            Set(player.Input, "MoveInput", state == 0 ? Vector2.right : Vector2.zero);
            Select(player.Input, 2); Press(player.Input, 200, false);
            player.Input.JumpBuffer.Set(); player.Input.DashBuffer.Set();
            player.Input.SkillBuffer.Set(); player.Input.UltimateBuffer.Set();
            fixture.Frame();
            Check(player.StateMachine.CurrentState is PlayerParryState,
                "Parry did not precede regular transitions from idle/run/attack: " + state);
        }
        fixture.Reset(0); Select(player.Input, 2);
        Set(player, "PendingHitReaction", PlayerHitReaction.Hit); fixture.Frame();
        Check(player.StateMachine.CurrentState is PlayerHitState && fixture.Forms[0].ParryBegins == 0,
            "Parry overrode a pending hit reaction.");
    }

    private static void VerifyKeyMapping(PlayerController player)
    {
        Select(player.Input, 0); Cooldown(player, 0f);
        var manager = new FormManagerSetup(player).Build(null, null, null, null, null);
        Check(manager.CurrentForm is WaterForm && player.CanChangeForm, "Default Water equip consumed cooldown.");
        var expected = new[] { typeof(FireForm), typeof(WaterForm), typeof(WoodForm), typeof(IronForm), typeof(EarthForm) };
        for (int i = 0; i < expected.Length; i++)
        {
            Select(player.Input, i + 1);
            Check(manager.GetRequestedForm()?.GetType() == expected[i], "Incorrect production numeric mapping for key " + (i + 1));
        }
        manager.CleanupTransientEffects(); Select(player.Input, 0);
    }

    private sealed class Fixture
    {
        public readonly PlayerController Player;
        public readonly HashSet<object> Sources;
        public readonly List<string> Log = new List<string>();
        public readonly FormParryRegressionAction Source = new FormParryRegressionAction();
        private readonly Vector3 _groundedPosition;
        public FormParryRegressionForm[] Forms;
        public FormManager Manager;
        public int FormChanges;
        public Fixture(PlayerController player, HashSet<object> sources)
        {
            Player = player; Sources = sources; _groundedPosition = player.transform.position;
        }
        private void GroundStationaryFixture()
        {
            Player.transform.position = _groundedPosition;
            Player.VerticalVelocity = 0f;
            Physics.SyncTransforms();
        }
        public void Reset(int initial)
        {
            Player.Controller.enabled = true; Player.Input.enabled = true; Player.Movement.enabled = true;
            Player.ConsumeHitReaction(); Player.StopParryKnockback(); GroundStationaryFixture();
            Player.Input.AttackBuffer.Consume(); Player.Input.JumpBuffer.Consume(); Player.Input.DashBuffer.Consume();
            Player.Input.SkillBuffer.Consume(); Player.Input.UltimateBuffer.Consume();
            Select(Player.Input, 0); Set(Player.Input, "MoveInput", Vector2.zero);
            Set(Player.Input, "AttackPressId", 0u); Set(Player.Input, "IsAttackHeld", false);
            Cooldown(Player, 0f); Field(Player.StatManager, typeof(PlayerStatManager), "_currentHp", 1000f);
            Player.StatManager.ConsumeAllUltimateGauge(); Source.ResetStatus(); Sources.Clear(); Sources.Add(Source);
            Forms = new FormParryRegressionForm[] {
                new FormParryRegressionFire(Log), new FormParryRegressionWater(Log), new FormParryRegressionWood(Log),
                new FormParryRegressionIron(Log), new FormParryRegressionEarth(Log) };
            Manager = new FormManager(Player); Set(Player, "FormManager", Manager);
            Manager.CanTransition = () => Player.StateMachine.CurrentState is PlayerIdleState || Player.StateMachine.CurrentState is PlayerRunState;
            for (int i = 0; i < Forms.Length; i++)
            {
                int key = i + 1;
                Manager.AddTransition(Forms[i], () => (bool)typeof(PlayerInputHandler).GetProperty("IsForm" + key + "Pressed").GetValue(Player.Input));
                Manager.AddFastTransition(Forms[i], Forms[(i + 1) % Forms.Length], () => Player.Input.IsFastFormPressed);
            }
            Manager.ChangeForm(Forms[initial]);
            Set(Player, "StateMachine", new PlayerStateMachineSetup(Player).Build());
            FormChanges = 0; Manager.OnFormChanged += form => FormChanges++; Log.Clear();
        }
        public void Frame()
        {
            // This suite tests a stationary grounded combatant. Edit Mode deltaTime
            // varies with editor activity; locomotion updates must not accumulate
            // displacement between independent input/state assertions.
            GroundStationaryFixture();
            Player.StateMachine.Update();
            Manager.Update();
            GroundStationaryFixture();
        }
    }
}

public class FormParryRegressionAction : EnemyAction
{
    public FormParryRegressionAction() : base(null, null) { }
    public void Finish() => IsFinished = true;
    public void ResetStatus() => IsFinished = false;
}

public abstract class FormParryRegressionForm : BaseForm
{
    private readonly List<string> _log;
    public readonly string Name;
    public int ParryBegins, ParryEnds, CounterBegins, CounterUpdates, CounterEnds;
    public bool CounterComplete;
    protected FormParryRegressionForm(string name, List<string> log) : base(null) { Name = name; _log = log; }
    public override void Equip(PlayerController player) { base.Equip(player); _log.Add(Name + ".Equip"); }
    public override void Unequip(PlayerController player) => _log.Add(Name + ".Unequip");
    public override void BeginParry() { ParryBegins++; _log.Add(Name + ".BeginParry"); }
    public override void EndParry() { ParryEnds++; _log.Add(Name + ".EndParry"); }
    public override void BeginCounter() { CounterBegins++; _log.Add(Name + ".BeginCounter"); }
    public override void UpdateCounter(out bool complete) { CounterUpdates++; complete = CounterComplete; }
    public override void EndCounter() { CounterEnds++; _log.Add(Name + ".EndCounter"); }
    public override void BeginAttack() { }
    public override void UpdateAttack(out bool complete) => complete = false;
    public override void EndAttack() { }
}
public sealed class FormParryRegressionFire : FormParryRegressionForm
{ public FormParryRegressionFire(List<string> log) : base("Fire", log) { } }
public sealed class FormParryRegressionWater : FormParryRegressionForm
{ public FormParryRegressionWater(List<string> log) : base("Water", log) { } }
public sealed class FormParryRegressionWood : FormParryRegressionForm
{ public FormParryRegressionWood(List<string> log) : base("Wood", log) { } }
public sealed class FormParryRegressionIron : FormParryRegressionForm
{ public FormParryRegressionIron(List<string> log) : base("Iron", log) { } }
public sealed class FormParryRegressionEarth : FormParryRegressionForm
{ public FormParryRegressionEarth(List<string> log) : base("Earth", log) { } }
