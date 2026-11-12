using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Exercises production damage, item ordering and run state in an isolated preview scene.</summary>
public static class PlayerEngravingCombatRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [MenuItem("Tools/Studio Baekho/Progression/Run Engraving Combat Regression")]
    private static void RunFromMenu() => Run();

    public static string Run()
    {
        _checks = 0;
        StaticSnapshotAndItems();
        DirectHitsAndNestedCommit();
        CriticalAndKillEffects();
        HealingAndCappedShields();
        IncomingAndRevival();
        FirstbloodUsesOriginalGold();
        RealDodgeAndCounterExpiry();
        BossShieldsAndPhaseIdentity();
        ReachGeometryAndTimerPause();
        DisablePreservesPermanentHealth();
        string result = $"Engraving combat: 10 scenarios passed; {_checks} checks.";
        Debug.Log(result); return result;
    }

    private static void StaticSnapshotAndItems()
    {
        using var f = new Fixture();
        f.Stats.SetPermanentUpgradeRatios(.1f, .2f, .1f);
        f.Own("atk hp crit severe speed footwork reach");
        Near(f.Stats.MaxHp, 1200f, "HP adds to workshop basis");
        Near(f.Stats.GetStat(StatType.ST_ATK), 128f, "Attack adds to workshop basis");
        Near(f.Stats.GetStat(StatType.ST_CRT), 5f, "Critical points");
        Near(f.Stats.GetStat(StatType.ST_CRTD), 170f, "Critical damage points");
        Near(f.Stats.GetStat(StatType.ST_ATK_SPD), 1.06f, "Global attack speed");
        Near(f.Player.EffectiveRunSpeed, f.Player.RunSpeed * 1.08f, "Authored movement basis");
        Near(f.Runtime.NormalReachMultiplier, 1.15f, "Normal reach");
        f.Item("I56");
        Near(f.Stats.GetStat(StatType.ST_ATK), 128f * 1.06f, "Item attack scales permanent basis");
        f.Settings.Find("atk").amount = .9f;
        Near(f.Runtime.Snapshot.Amount("atk"), .08f, "Run snapshot survives asset change");
        f.Runtime.ClearStageTransient();
        Near(f.Stats.GetStat(StatType.ST_ATK), 128f * 1.06f, "Stage cleanup preserves frozen permanent stats");
    }

    private static void DirectHitsAndNestedCommit()
    {
        using var f = new Fixture();
        f.Own("opener combo counter finisher pity bossdmg backstab");
        var enemy = f.Enemy(1000);
        f.Runtime.NotifySuccessfulDodge();
        bool called = false; DamageResult nested = default;
        enemy.OnDamaged += (_, _, _) =>
        {
            if (called) return; called = true;
            nested = DamageManager.Apply(f.Hit(100), enemy, enemy.gameObject);
        };
        Near(DamageManager.Apply(f.Hit(100), enemy, enemy.gameObject).HealthDamage, 145f, "First contact combines opener and counter");
        Near(nested.HealthDamage, 105f, "Nested contact cannot reuse first-hit or counter");
        Near(DamageManager.Apply(f.Hit(100), enemy, enemy.gameObject).HealthDamage, 110f, "Third same-target hit");
        Near(DamageManager.Apply(f.Hit(100), enemy, enemy.gameObject).HealthDamage, 115f, "Combo cap");
        f.Runtime.Tick(3f);
        Near(DamageManager.Apply(f.Hit(100), enemy, enemy.gameObject).HealthDamage, 100f, "Combo expires at exact duration");
        var other = f.Enemy(1000);
        other.enabled = false;
        Check(!DamageManager.Apply(f.Hit(100), other, other.gameObject).Applied, "Rejected contact");
        other.enabled = true;
        Near(DamageManager.Apply(f.Hit(100), other, other.gameObject).HealthDamage, 125f, "Rejected contact did not consume opener");
        Near(DamageManager.Apply(f.Hit(100, direct: false), other, other.gameObject).HealthDamage, 100f, "Additional damage does not gain conditional direct bonuses");
        var low = f.Enemy(1000); low.EnemyStat.CurrentHp = 300f;
        Near(DamageManager.Apply(f.Hit(100), low, low.gameObject).HealthDamage, 115f, "Finisher threshold is inclusive");
        var boss = f.Enemy(1000, EnemyRank.Boss);
        Near(DamageManager.Apply(f.Hit(100), boss, boss.gameObject).HealthDamage, 135f, "Boss plus opener add");
        var elite = f.Enemy(1000, EnemyRank.Elite);
        Near(DamageManager.Apply(f.Hit(100), elite, elite.gameObject).HealthDamage, 125f, "Elite does not gain boss-only bonus");
        var rear = f.Enemy(1000); rear.transform.position = f.Player.transform.position + Vector3.forward;
        Near(DamageManager.Apply(f.Hit(100), rear, rear.gameObject).HealthDamage, 145f, "Rear attack plus opener");
        var front = f.Enemy(1000); front.transform.position = f.Player.transform.position + Vector3.back;
        Near(DamageManager.Apply(f.Hit(100), front, front.gameObject).HealthDamage, 125f, "Front attack does not gain backstab");
        var reset = f.Enemy(1000); reset.EnemyStat.CurrentHp = 900;
        Near(DamageManager.Apply(f.Hit(100), reset, reset.gameObject).HealthDamage, 100f, "Target change starts combo at zero");
        Near(DamageManager.Apply(f.Hit(100), front, front.gameObject).HealthDamage, 100f, "Returning to prior target starts a fresh combo");
    }

    private static void CriticalAndKillEffects()
    {
        using var f = new Fixture();
        f.Own("pity critheal heal pursuit haste footwork escape");
        var target = f.Enemy(10000);
        for (int i = 0; i < 15; i++) f.Runtime.CommitOutgoing(f.Hit(1, trueDamage: false), target, false);
        Near(f.Runtime.CriticalPity, .20f, "Pity cap");
        f.Runtime.CommitOutgoing(f.Hit(1), target, false);
        Near(f.Runtime.CriticalPity, .20f, "Fixed damage does not change pity");
        f.Stats.TakeDamage(100, ElementType.ELEMENT_NONE, false);
        var killed = f.Enemy(10);
        var critical = f.Hit(100, trueDamage: false, crit: 1f);
        Check(DamageManager.Apply(critical, killed, killed.gameObject).Killed, "Critical final death");
        Near(f.Runtime.CriticalPity, 0f, "Actual critical resets pity");
        Near(f.Stats.CurrentHp, 923f, "Critical direct kill heals with recovery multiplier");
        Near(f.Runtime.MoveMultiplier, 1.23f, "Kill movement bonus");
        Near(f.Stats.GetStat(StatType.ST_ATK_SPD), 1.10f, "Kill attack-speed bonus");
        f.Runtime.CommitIncoming(target, 1, 0);
        Near(f.Runtime.MoveMultiplier, 1.23f, "Hit and kill movement bonuses do not stack");
        f.Runtime.Tick(3f);
        Near(f.Runtime.MoveMultiplier, 1.08f, "Timed movement expires");
        Near(f.Stats.GetStat(StatType.ST_ATK_SPD), 1f, "Timed attack-speed expires");
        var additionalKill = f.Enemy(10);
        DamageManager.Apply(f.Hit(100, direct: false, trueDamage: false, crit: 1f), additionalKill, additionalKill.gameObject);
        Near(f.Stats.CurrentHp, 923f, "Additional critical kill does not proc heal");
        f.Runtime.NotifyRoomCleared();
        Near(f.Stats.CurrentHp, 923f, "Unowned room recovery");
        f.Own("roomheal heal"); f.Stats.TakeDamage(100, ElementType.ELEMENT_NONE, false);
        f.Runtime.NotifyRoomCleared();
        Near(f.Stats.CurrentHp, 934.5f, "Room recovery uses ordinary healing multiplier");
    }

    private static void HealingAndCappedShields()
    {
        using var f = new Fixture();
        f.Own("heal shield"); f.Item("I33");
        f.Stats.TakeDamage(100, ElementType.ELEMENT_NONE, false);
        f.Stats.Heal(100);
        Near(f.Stats.CurrentHp, 1000f, "Healing caps HP");
        Near(f.Stats.Shield, 8.25f, "Overheal gain receives item shield multiplier once");
        f.Stats.Heal(10);
        Near(f.Stats.Shield, 14.575f, "Existing shield is not amplified again");
        f.Stats.Heal(10000);
        Near(f.Stats.Shield, 100f, "Final shield cap after amplification");
        f.Item("I01");
        f.Stats.Heal(10000);
        Near(f.Stats.Shield, 105f, "Shield cap follows increased maximum HP");
        f.Items.TryRemoveItem("I01");
        Near(f.Stats.Shield, 100f, "Selling HP item clamps only engraving shield");
        f.Runtime.ClearStageTransient();
        Near(f.Stats.Shield, 0f, "Stage boundary clears temporary engraving shield");
    }

    private static void IncomingAndRevival()
    {
        using (var f = new Fixture())
        {
            f.EnableIncoming(); f.Own("armor bossguard guard crowdguard");
            f.Item("I77"); f.Items.SetCombatState(true);
            var normal = f.Enemy(1000);
            Near(f.Player.TakeDamage(100, isParryable: false, source: normal).HealthDamage, 83.6f, "Item and engraving reduction multiply");
            var boss = f.Enemy(1000, EnemyRank.Boss);
            Near(f.Player.TakeDamage(100, isParryable: false, source: boss).HealthDamage, 47.5f, "First boss hit reduction");
            Near(f.Player.TakeDamage(100, isParryable: false, source: boss).HealthDamage, 95f, "Boss guard consumed once");
            f.Stats.TakeDamage(f.Stats.CurrentHp - 300f, ElementType.ELEMENT_NONE, false);
            var third = f.Enemy(1000);
            Physics.SyncTransforms();
            Near(f.Player.TakeDamage(100, isParryable: false, source: normal).HealthDamage, 68.4f, "Low HP and three distinct nearby enemies");
        }
        using (var f = new Fixture())
        {
            f.EnableIncoming(); f.Own("revive heal shield armor");
            f.Item("I31", "I50", "I77", "I78"); f.Items.SetCombatState(true);
            var attacker = f.Enemy(1000);
            bool observed = false; DamageResult nested = default;
            f.Stats.OnDamageTaken += (_, _, _) =>
            {
                if (observed) return; observed = true;
                Check(f.Runtime.ReviveSpent && f.Runtime.IsInvulnerable, "Revive state committed before damage observers");
                Near(f.Stats.CurrentHp, 300f, "Observers never see HP zero");
                nested = f.Player.TakeDamage(10000, isParryable: false, source: attacker);
            };
            var revived = f.Player.TakeDamage(10000, isParryable: false, source: attacker);
            Check(revived.Revived && !revived.Killed && !nested.Applied, "Single revival and immediate immunity");
            Near(revived.HealthDamage, 1000f, "Lethal hit records actual pre-revival loss");
            Near(f.Stats.CurrentHp, 300f, "Revive bypasses recovery multiplier");
            Near(f.Stats.Shield, 100f, "Post-hit item shields activate after original lethal hit");
            f.Runtime.Tick(2f);
            var dead = f.Player.TakeDamage(10000, isParryable: false, source: attacker);
            Check(dead.Killed && !dead.Revived && f.Runtime.ReviveSpent, "Second lethal hit cannot revive");
        }
    }

    private static void FirstbloodUsesOriginalGold()
    {
        using var f = new Fixture();
        f.Own("firstblood"); f.Item("I89");
        var enemy = f.Enemy(10);
        enemy.gameObject.AddComponent<EnemyGoldReward>().Initialize(f.Wallet, 5, f.Items);
        DamageManager.Apply(f.Hit(100), enemy, enemy.gameObject);
        Check(f.Wallet.Gold == 7, "Gold is base5 + item1 + engraving20% of original5");
        for (int i = 0; i < 5; i++)
        {
            enemy = f.Enemy(10);
            enemy.gameObject.AddComponent<EnemyGoldReward>().Initialize(f.Wallet, 1, f.Items);
            DamageManager.Apply(f.Hit(100), enemy, enemy.gameObject);
        }
        Check(f.Wallet.Gold == 18, "Independent engraving fraction carries to an integer");
        Near((float)f.Runtime.GoldFractionRemainder, 0f, "Fraction boundary exact");
        enemy = f.Enemy(10, EnemyRank.Elite);
        enemy.gameObject.AddComponent<EnemyGoldReward>().Initialize(f.Wallet, 5, f.Items);
        DamageManager.Apply(f.Hit(100), enemy, enemy.gameObject);
        Check(f.Wallet.Gold == 24, "Elite does not receive normal-kill engraving gold");
        enemy = f.Enemy(10);
        enemy.gameObject.AddComponent<EnemyGoldReward>().Initialize(f.Wallet, 5, f.Items);
        DamageManager.Apply(f.Hit(100, direct: false), enemy, enemy.gameObject);
        Check(f.Wallet.Gold == 30, "Player-owned additional kill receives engraving gold without triggering item gold");
        enemy = f.Enemy(10);
        enemy.gameObject.AddComponent<EnemyGoldReward>().Initialize(f.Wallet, 5, f.Items);
        enemy.TakeDamage(100);
        Check(f.Wallet.Gold == 35, "Unattributed environmental kill receives no player engraving bonus");
        f.Runtime.ClearStageTransient();
        Check(!f.Runtime.ReviveSpent && f.Wallet.Gold == 35, "Stage cleanup does not reset wallet");
    }

    private static void RealDodgeAndCounterExpiry()
    {
        using var f = new Fixture(); f.Own("counter");
        var attacker = f.Enemy(10000);
        var action = new ProbeEnemyAction(attacker);
        void Dash()
        {
            typeof(PlayerController).GetField("_dashCooldownTimer", Hidden).SetValue(f.Player, 0f);
            f.Player.StateMachine.ChangeState(new NeutralState());
            f.Player.StateMachine.ChangeState(new PlayerDashState(f.Player));
        }
        Dash();
        Check(!f.Player.TakeDamage(1, source: attacker).Applied, "Dodge still avoids a non-action contact");
        f.Player.StateMachine.ChangeState(new NeutralState());
        Near(DamageManager.Apply(f.Hit(100), attacker, attacker.gameObject).HealthDamage, 100f,
            "A dash without an avoided enemy action does not charge counter");
        Dash();
        Check(!f.Player.TakeDamage(1, source: action).Applied, "Real enemy attack is avoided");
        f.Player.StateMachine.ChangeState(new NeutralState());
        Near(DamageManager.Apply(f.Hit(100), attacker, attacker.gameObject).HealthDamage, 120f,
            "Real dodge empowers next confirmed direct hit");
        Near(DamageManager.Apply(f.Hit(100), attacker, attacker.gameObject).HealthDamage, 100f,
            "Counter consumes exactly once");
        Dash(); f.Player.TakeDamage(1, source: action); f.Runtime.Tick(4f);
        f.Player.TakeDamage(1, source: action); // A second avoided contact during the same dash cannot refresh readiness.
        f.Runtime.Tick(1f); f.Player.StateMachine.ChangeState(new NeutralState());
        Near(DamageManager.Apply(f.Hit(100), attacker, attacker.gameObject).HealthDamage, 100f,
            "Counter expires at five seconds and one dash cannot refresh it twice");
    }

    private static void BossShieldsAndPhaseIdentity()
    {
        using (var f = new Fixture())
        {
            f.Own("opener counter"); var boss = f.Boss();
            var shield = boss.GetComponent<BossShield>(); shield.SetAbsorption(1000f);
            f.Runtime.NotifySuccessfulDodge();
            Near(DamageManager.Apply(f.Hit(100), boss, boss.gameObject).ShieldDamage, 145f,
                "First shield-only contact consumes opener and counter");
            Near(DamageManager.Apply(f.Hit(100), boss, boss.gameObject).ShieldDamage, 100f,
                "Still-full boss HP cannot repeat first contact bonuses");
            shield.SetAbsorption(0f);
            Near(DamageManager.Apply(f.Hit(20), boss, boss.gameObject).HealthDamage, 20f,
                "Removing shield does not restore opener");
        }
        using (var f = new Fixture())
        {
            f.EnableIncoming(); f.Own("bossguard"); var boss = f.Boss();
            Near(f.Player.TakeDamage(100, isParryable: false, source: boss).HealthDamage, 50f, "Boss actor first hit");
            var phaseHit = DamageManager.Apply(f.Hit(100), boss, boss.gameObject);
            Check(!phaseHit.Killed && boss.IsTransitioning, "Phase exhaustion is not a final death");
            typeof(Boss).GetMethod("TickPhaseTransition", Hidden).Invoke(boss, new object[] { 3f });
            Check(boss.PhaseIndex == 1 && !boss.IsTransitioning, "Boss reached its second health bar");
            Near(f.Player.TakeDamage(100, isParryable: false, source: boss).HealthDamage, 100f,
                "Phase change does not restore boss guard");
            var elite = f.Enemy(1000, EnemyRank.Elite);
            Near(f.Player.TakeDamage(100, isParryable: false, source: elite).HealthDamage, 100f,
                "Elite does not consume or benefit from boss guard");
            var newBoss = f.Boss();
            Near(f.Player.TakeDamage(100, isParryable: false, source: newBoss).HealthDamage, 50f,
                "A different boss owns a separate first-hit receipt");
        }
    }

    private static void ReachGeometryAndTimerPause()
    {
        using (var f = new Fixture())
        {
            var enemy = f.Enemy(1000); enemy.transform.position = f.Player.transform.position + Vector3.right * 1.1f;
            enemy.GetComponent<BoxCollider>().size = Vector3.one * .05f;
            var form = f.RangeForm(); Physics.SyncTransforms();
            foreach (HitBoxShape shape in Enum.GetValues(typeof(HitBoxShape)))
            {
                var step = new WeaponActionData { HitBoxShape = shape, HitBoxRadius = 1f, HitBoxSize = Vector3.one, HitBoxHeight = 2f };
                f.Own("");
                Check(!form.Hits(step, enemy, false), "Baseline " + shape + " misses outside target");
                f.Own("reach");
                Check(form.Hits(step, enemy, false), "Reach expands actual " + shape + " query");
                Check(!form.Hits(step, enemy, true), "Reach preserves authored skill " + shape + " area");
                Near(step.HitBoxRadius, 1f, "Shared hit radius is immutable");
                Check(step.HitBoxSize == Vector3.one && step.HitBoxHeight == 2f, "Shared hit shape remains immutable");
            }
        }
        using (var f = new Fixture())
        {
            f.EnableIncoming(); f.Own("footwork pursuit escape haste");
            var killed = f.Enemy(10); DamageManager.Apply(f.Hit(100), killed, killed.gameObject);
            var attacker = f.Enemy(1000); f.Player.TakeDamage(1, isParryable: false, source: attacker);
            Near(f.Player.EffectiveRunSpeed, f.Player.RunSpeed * 1.23f, "Movement uses strongest temporary bonus");
            Set(f.Player, nameof(PlayerController.IsStageTransitioning), true);
            f.Runtime.Tick(10f);
            Near(f.Stats.GetStat(StatType.ST_ATK_SPD), 1.1f, "Stage transition freezes haste timer");
            Near(f.Runtime.MoveMultiplier, 1.23f, "Stage transition freezes move timers");
            Set(f.Player, nameof(PlayerController.IsStageTransitioning), false);
            Set(f.Player, nameof(PlayerController.IsShopPaused), true);
            f.Runtime.Tick(10f);
            Near(f.Stats.GetStat(StatType.ST_ATK_SPD), 1.1f, "Modal pause freezes haste timer");
            Set(f.Player, nameof(PlayerController.IsShopPaused), false);
            f.Runtime.Tick(2f);
            Near(f.Runtime.MoveMultiplier, 1.23f, "Longer kill move remains after hit move expires");
            f.Runtime.Tick(1f);
            Near(f.Runtime.MoveMultiplier, 1.08f, "Movement returns to permanent speed at exact expiry");
            Near(f.Stats.GetStat(StatType.ST_ATK_SPD), 1f, "Haste ends at exact expiry");
            killed = f.Enemy(10); DamageManager.Apply(f.Hit(100), killed, killed.gameObject);
            f.Runtime.ClearStageTransient();
            Near(f.Runtime.MoveMultiplier, 1.08f, "Stage cleanup removes temporary movement");
            Near(f.Stats.GetStat(StatType.ST_ATK_SPD), 1f, "Stage cleanup removes temporary attack speed");
        }
    }

    private static void DisablePreservesPermanentHealth()
    {
        using var f = new Fixture();
        f.Own("hp atk crit severe speed haste pursuit revive firstblood");
        var snapshot = f.Runtime.Snapshot;
        var enemy = f.Enemy(10);
        enemy.gameObject.AddComponent<EnemyGoldReward>().Initialize(f.Wallet, 1, f.Items);
        DamageManager.Apply(f.Hit(100), enemy, enemy.gameObject);
        Near(f.Stats.GetStat(StatType.ST_ATK_SPD), 1.16f, "Fixture has a temporary haste contribution");
        f.Runtime.enabled = false;
        // Edit-mode fixtures explicitly run the lifecycle hooks that Unity runs for gameplay objects.
        typeof(PlayerEngravingRuntime).GetMethod("OnDisable", Hidden).Invoke(f.Runtime, null);
        Near(f.Stats.MaxHp, 1100f, "Disable retains permanent maximum HP");
        Near(f.Stats.CurrentHp, 1100f, "Disable does not clamp full HP");
        Near(f.Stats.GetStat(StatType.ST_ATK_SPD), 1.06f, "Disable clears haste while retaining permanent speed");
        f.Runtime.enabled = true;
        typeof(PlayerEngravingRuntime).GetMethod("OnEnable", Hidden).Invoke(f.Runtime, null);
        Near(f.Stats.CurrentHp, 1100f, "Enable preserves full HP without refill");
        f.Stats.TakeDamage(250f, ElementType.ELEMENT_NONE, false);
        Near(f.Stats.CurrentHp, 850f, "Fixture has partial HP");
        f.Player.gameObject.SetActive(false);
        typeof(PlayerEngravingRuntime).GetMethod("OnDisable", Hidden).Invoke(f.Runtime, null);
        Near(f.Stats.MaxHp, 1100f, "Disabled GameObject retains snapshot basis");
        Near(f.Stats.CurrentHp, 850f, "Disabled GameObject retains partial HP");
        f.Player.gameObject.SetActive(true);
        typeof(PlayerEngravingRuntime).GetMethod("OnEnable", Hidden).Invoke(f.Runtime, null);
        Near(f.Stats.CurrentHp, 850f, "Re-enable does not heal partial HP");
        Near(f.Stats.GetStat(StatType.ST_ATK), 108f, "Permanent attack survives lifecycle");
        Near(f.Stats.GetStat(StatType.ST_CRT), 5f, "Permanent critical points survive lifecycle");
        Check(ReferenceEquals(snapshot, f.Runtime.Snapshot) && f.Runtime.Snapshot.Has("revive"),
            "Disable never replaces the frozen snapshot");
        Near((float)f.Runtime.GoldFractionRemainder, .2f, "Disable preserves run-owned gold carry");
        Check(f.Stats.TakeDamage(5000f, ElementType.ELEMENT_NONE, false).Revived, "Revival remains available after lifecycle");
        f.Runtime.enabled = false;
        typeof(PlayerEngravingRuntime).GetMethod("OnDisable", Hidden).Invoke(f.Runtime, null);
        f.Runtime.enabled = true;
        typeof(PlayerEngravingRuntime).GetMethod("OnEnable", Hidden).Invoke(f.Runtime, null);
        Check(f.Runtime.ReviveSpent && !f.Runtime.IsInvulnerable, "Disable clears temporary immunity without replenishing revive");
        Near(f.Stats.CurrentHp, 330f, "Re-enable preserves post-revival HP");
    }

    // Only source identity is exercised; this action never enters or reads authored skill data.
    private sealed class ProbeEnemyAction : EnemyAction { public ProbeEnemyAction(Enemy enemy) : base(null, enemy) { } }
    private sealed class NeutralState : IPlayerState { public void Enter() { } public void Update() { } public void Exit() { } }
    private sealed class RangeProbeForm : BaseForm
    {
        public RangeProbeForm(WeaponActionDataSO data, PlayerController player) : base(data) { Equip(player); }
        public bool Hits(WeaponActionData step, Enemy target, bool skill)
        {
            _currentAction = skill ? ActionType.Skill : ActionType.Attack;
            int count = QueryOverlap(step, _playerController.transform.position);
            for (int i = 0; i < count; i++) if (_overlapBuffer[i] != null && _overlapBuffer[i].GetComponentInParent<Enemy>() == target) return true;
            return false;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Scene _previousActive = SceneManager.GetActiveScene();
        // Production uses Physics queries against the default physics world. Preview-scene colliders
        // do not participate in that world, so use a disposable additive scene for geometry assertions.
        private readonly Scene _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        private readonly List<Object> _objects = new();
        private readonly CombatSettings _previous = CombatSettings.Active;
        private readonly UnityEngine.Random.State _random = UnityEngine.Random.state;
        public readonly PlayerController Player;
        public readonly PlayerStatManager Stats;
        public readonly PlayerItemRuntime Items;
        public readonly PlayerEngravingRuntime Runtime;
        public readonly PlayerWallet Wallet;
        public readonly EngravingSettingsSO Settings;
        public Fixture()
        {
            var go = Make("Engraving regression player", false);
            Player = go.AddComponent<PlayerController>(); Stats = go.GetComponent<PlayerStatManager>();
            var data = Asset<PlayerStatData>(); data.baseHP = 1000f; data.baseATK = 100f; data.baseCRT = 0f; data.baseElmAtk = 0f;
            Stats.Initialize(data);
            Set(Player, nameof(PlayerController.StatManager), Stats);
            Set(Player, nameof(PlayerController.FormManager), new FormManager(Player));
            Set(Player, nameof(PlayerController.StateMachine), new StateMachine());
            Set(Player, nameof(PlayerController.Input), go.GetComponent<PlayerInputHandler>());
            Items = go.AddComponent<PlayerItemRuntime>();
            var catalog = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Generated/BoundaryInventory/ItemCatalog.asset"));
            _objects.Add(catalog); catalog.synergies = Array.Empty<ItemSynergyDefinition>();
            // Keep the combat ordering fixture's original effects when production item rules change.
            catalog.items = (ItemDefinition[])catalog.items.Clone();
            foreach (string id in new[] { "I50", "I56", "I77", "I89" })
            {
                int index = Array.FindIndex(catalog.items, item => item != null && item.itemId == id);
                var definition = Object.Instantiate(catalog.items[index]);
                _objects.Add(definition);
                definition.boundaryEffects = BoundaryInventoryContent.Effects(id);
                catalog.items[index] = definition;
            }
            Items.Catalog = catalog; Items.Initialize(Stats, () => ElementType.ELEMENT_NONE);
            Set(Player, nameof(PlayerController.ItemRuntime), Items);
            Runtime = PlayerEngravingRuntime.Ensure(Player);
            // Preview objects do not run the gameplay Awake lifecycle.
            typeof(PlayerEngravingRuntime).GetMethod("References", Hidden).Invoke(Runtime, null);
            Wallet = go.AddComponent<PlayerWallet>(); Wallet.Initialize(0);
            Settings = Asset<EngravingSettingsSO>();
            var table = Asset<ElementEffectiveManager>();
            foreach (ElementType a in Enum.GetValues(typeof(ElementType)))
                foreach (ElementType b in Enum.GetValues(typeof(ElementType)))
                    table.elementEffectiveList.Add(new ElementEffective { AttackType = a, DefenseType = b, multiplier = 1f });
            table.Initialize();
            var combat = Make("Neutral engraving table", false).AddComponent<CombatSettings>();
            typeof(CombatSettings).GetField("_elementTable", Hidden).SetValue(combat, table);
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, combat);
            // Effects use enabled components, while gameplay Update is never run in this edit-mode fixture.
            go.SetActive(true);
        }
        public void Own(string ids) => Runtime.InitializeRun(new EngravingSnapshot(Settings, ids.Split(' ')));
        public void Item(params string[] ids) { foreach (string id in ids) Check(Items.TryAddItem(id), "Cannot own " + id); }
        public void EnableIncoming() => Player.HitStaggerResistance = StaggerResistLevel.SUPER_ARMOR;
        public RangeProbeForm RangeForm() => new RangeProbeForm(Asset<FireFormActionDataSO>(), Player);
        private T Asset<T>() where T : ScriptableObject
        { var asset = ScriptableObject.CreateInstance<T>(); _objects.Add(asset); return asset; }
        private GameObject Make(string name, bool active)
        {
            var go = new GameObject(name); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, _scene);
            go.transform.position = new Vector3(10000f, 5000f, 10000f);
            _objects.Add(go); go.SetActive(active); return go;
        }
        public Enemy Enemy(int hp, EnemyRank rank = EnemyRank.Normal)
        {
            var go = Make("Engraving regression enemy", false); go.layer = LayerMask.NameToLayer("Enemy");
            var enemy = go.AddComponent<Enemy>(); enemy.EnemyStat = go.GetComponent<EnemyStat>();
            var data = Asset<EnemyData>(); data.maxHp = hp; data.baseDef = 0f;
            typeof(EnemyStat).GetField("baseData", Hidden).SetValue(enemy.EnemyStat, data);
            typeof(EnemyStat).GetMethod("InitializeFromData", Hidden).Invoke(enemy.EnemyStat, null);
            typeof(Enemy).GetField("enemyRank", Hidden).SetValue(enemy, rank);
            go.AddComponent<BoxCollider>(); go.GetComponent<PoiseHandler>().ResetPoise(); go.SetActive(true); return enemy;
        }
        public ConfiguredBoss Boss()
        {
            var data = Asset<BossData>(); data.maxHp = 100; data.baseDef = 0f;
            data.phaseDownSeconds = 1f; data.phaseHoldSeconds = .5f; data.phaseReviveSeconds = 1.5f;
            var first = Asset<BossPhaseData>(); first.phaseMaxHp = 60f;
            var second = Asset<BossPhaseData>(); second.phaseMaxHp = 40f;
            var go = Make("Engraving regression phased boss", false);
            go.AddComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
            var boss = go.AddComponent<ConfiguredBoss>(); boss.EnemyStat = go.GetComponent<EnemyStat>();
            typeof(Boss).GetField("BossData", Hidden).SetValue(boss, data);
            typeof(Boss).GetField("PhaseDatas", Hidden).SetValue(boss, new List<BossPhaseData> { first, second });
            typeof(EnemyStat).GetField("baseData", Hidden).SetValue(boss.EnemyStat, data);
            typeof(EnemyStat).GetMethod("InitializeFromData", Hidden).Invoke(boss.EnemyStat, null);
            boss.StaggerResistLevel = StaggerResistLevel.SUPER_ARMOR;
            go.SetActive(true); typeof(Boss).GetMethod("CreateBrain", Hidden).Invoke(boss, null); return boss;
        }
        public HitInfo Hit(float amount, bool direct = true, bool trueDamage = true, float crit = 0f)
            => new HitInfo(amount, ElementType.ELEMENT_NONE, crit, 1.5f, source: Player, trueDamage: trueDamage,
                category: direct ? PlayerAttackCategory.Normal : PlayerAttackCategory.Secondary,
                hasSourceDamageSnapshot: true, sourceDamageMultiplier: 1f, isDirectPlayerHit: direct,
                origin: direct ? DamageOrigin.AuthoredAction : DamageOrigin.AdditionalEffect);
        public void Dispose()
        {
            for (int i = _objects.Count - 1; i >= 0; i--) if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, _previous);
            if (_previousActive.IsValid() && _previousActive.isLoaded) SceneManager.SetActiveScene(_previousActive);
            EditorSceneManager.CloseScene(_scene, true); UnityEngine.Random.state = _random;
        }
    }
    private static void Set(object target, string property, object value) => target.GetType().GetProperty(property).SetValue(target, value);
    private static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .001f, message + ": " + actual + " != " + expected);
    private static void Check(bool value, string message) { _checks++; if (!value) throw new InvalidOperationException(message); }
}
