using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real damage/death/transaction paths, isolated from the open game scene.</summary>
public static class BoundaryCombatIntegrationRegression
{
    private static int _checks;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Use Edit Mode for isolated combat integration checks.");
        _checks = 0;
        GoldRolls();
        VerifyIncomingReentrancy();
        VerifyShieldCallbackReentrancy();
        VerifyPhaseCallbackReentrancy();
        VerifyOutOfCombatRewards();
        using (var f = new Fixture())
        {
            f.Own("I95", "I72");
            foreach (var category in new[] { PlayerAttackCategory.Normal, PlayerAttackCategory.Heavy, PlayerAttackCategory.Counter,
                PlayerAttackCategory.Skill, PlayerAttackCategory.Ultimate, PlayerAttackCategory.SwitchAttack, PlayerAttackCategory.Secondary })
            {
                var enemy = f.Enemy(1000f);
                var result = DamageManager.Apply(f.Hit(100f, category), enemy, enemy.gameObject);
                float expected = ItemDamageContext.Family(category) == BoundaryAttackFamily.Normal ? 107f
                    : ItemDamageContext.Family(category) == BoundaryAttackFamily.Skill ? 105f : 100f;
                Near(result.HealthDamage, expected, "Attack family " + category);
            }
            f.Own("I43");
            var threshold = f.Enemy(1000f);
            threshold.EnemyStat.CurrentHp = 300f;
            Near(DamageManager.Apply(f.Hit(100f), threshold, threshold.gameObject).HealthDamage, 106f, "Inclusive target HP boundary");
            threshold.EnemyStat.CurrentHp = 301f;
            Near(DamageManager.Apply(f.Hit(100f), threshold, threshold.gameObject).HealthDamage, 100f, "Above target HP boundary");
            f.Own("I61", "I64");
            var armored = f.Enemy(1000f, defense: 100f);
            Near(DamageManager.Apply(f.Hit(100f, trueDamage: false), armored, armored.gameObject).HealthDamage,
                10000f / 181f, "Combined positive defense penetration");
            f.Own("I71");
            var first = f.Enemy(1000f); var second = f.Enemy(1000f);
            Near(DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 101), first, first.gameObject).HealthDamage, 107f, "First cast hit");
            Near(DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 101), second, second.gameObject).HealthDamage, 100f, "Other target in same cast");
            Near(DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 102), first, first.gameObject).HealthDamage, 107f, "Next cast first hit");
            VerifyRejectedAndNestedHits(f);
            f.Own("I89", "I90", "I91", "I94");
            f.Items.SetCombatState(true);
            var final = f.Enemy(10f);
            // Mimic room's earlier OnDied subscription before reward binding.
            final.OnDied += () => f.Items.SetCombatState(false);
            var reward = final.gameObject.AddComponent<EnemyGoldReward>();
            reward.Initialize(f.Wallet, 10, f.Items);
            DamageManager.Apply(f.Hit(100f), final, final.gameObject);
            Check(f.Wallet.Gold == 17, "Final-enemy context lost +1/+2/+3/+1 bonuses");
            ((Action)typeof(Enemy).GetField("OnDied", Hidden).GetValue(final))?.Invoke();
            Check(f.Wallet.Gold == 17, "Duplicate death paid again");
            var extra = f.Enemy(10f);
            extra.gameObject.AddComponent<EnemyGoldReward>().Initialize(f.Wallet, 10, f.Items);
            DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Secondary), extra, extra.gameObject);
            Check(f.Wallet.Gold == 27, "Additional damage incorrectly triggered kill bonuses");
            f.Own("I89");
            var eleven = f.Enemy(10f);
            eleven.gameObject.AddComponent<EnemyGoldReward>().Initialize(f.Wallet, 11, f.Items);
            DamageManager.Apply(f.Hit(100f), eleven, eleven.gameObject);
            Check(f.Wallet.Gold == 38, "I89 incorrectly affects base gold 11");
            f.Own("I01", "I13");
            var settings = f.Asset<EconomySettingsSO>();
            var transactions = new ShopTransactions(f.Items, f.Wallet, f.Stats, settings, 312);
            Check(transactions.TrySell("I01") == ShopSaleResult.Success && !f.Items.Contains("I01"), "Selling real new item failed");
            Near(f.Stats.MaxHp, 1000f, "Selling leaves resource bonus");
            f.Own("I13", "I41");
            Check(transactions.TryFuse("I13", "I41", out var fused) == ShopFusionResult.Success
                && fused != null && f.Items.OwnedItems.Count == 1, "New catalog fusion failed");
            VerifyBossPhaseReward(f);
        }
        return $"PASS: Boundary combat integration ({_checks} checks: damage, cast identity/reentrancy, boss phases, final death ordering, gold, sale and fusion).";
    }

    private static void VerifyRejectedAndNestedHits(Fixture f)
    {
        f.Own("I71");
        var rejected = f.Enemy(1000f);
        rejected.enabled = false;
        Check(!DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 201), rejected, rejected.gameObject).Applied,
            "An inactive receiver accepted the first skill contact");
        rejected.enabled = true;
        Near(DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 201), rejected, rejected.gameObject).HealthDamage,
            107f, "A rejected contact consumed the first-hit bonus");
        Near(DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 201), rejected, rejected.gameObject).HealthDamage,
            100f, "An accepted cast received the first-hit bonus twice");

        var outer = f.Enemy(1000f);
        var inner = f.Enemy(1000f);
        var later = f.Enemy(1000f);
        DamageResult nested = default;
        int nestedCalls = 0;
        var prior = ItemDamageContext.Current;
        outer.OnDamaged += (_, _, _) =>
        {
            nestedCalls++;
            nested = DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 301), inner, inner.gameObject);
        };
        Near(DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 301), outer, outer.gameObject).HealthDamage,
            107f, "Outer same-cast first hit");
        Check(nestedCalls == 1 && nested.Applied, "The synchronous nested hit fixture did not run exactly once");
        Near(nested.HealthDamage, 100f, "A nested hit claimed the same cast's first-hit bonus");
        Near(DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 301), later, later.gameObject).HealthDamage,
            100f, "A later contact claimed the nested cast's first-hit bonus");
        Check(SameContext(prior, ItemDamageContext.Current), "A completed nested damage stack leaked its caller context");

        outer = f.Enemy(1000f);
        inner = f.Enemy(1000f);
        bool outerSeen = false, innerSeen = false, outerRestored = false;
        inner.OnDamaged += (_, _, _) => innerSeen = ItemDamageContext.Current.Player == f.Player
            && ItemDamageContext.Current.Family == BoundaryAttackFamily.Normal;
        outer.OnDamaged += (_, _, _) =>
        {
            var caller = ItemDamageContext.Current;
            outerSeen = caller.Player == f.Player && caller.Family == BoundaryAttackFamily.Skill;
            DamageManager.Apply(f.Hit(1f, PlayerAttackCategory.Normal), inner, inner.gameObject);
            outerRestored = SameContext(caller, ItemDamageContext.Current);
        };
        DamageManager.Apply(f.Hit(1f, PlayerAttackCategory.Skill, cast: 302), outer, outer.gameObject);
        Check(outerSeen && innerSeen && outerRestored && SameContext(prior, ItemDamageContext.Current),
            "Nested OnDamaged dispatch did not restore skill/normal attribution at every scope boundary");
    }

    private static bool SameContext(ItemDeathContext a, ItemDeathContext b)
        => a.Player == b.Player && a.Family == b.Family && a.CanTrigger == b.CanTrigger && a.WasInCombat == b.WasInCombat
            && a.PlayerHp == b.PlayerHp && a.PlayerMaxHp == b.PlayerMaxHp && a.CombatEpoch == b.CombatEpoch;

    private static void VerifyOutOfCombatRewards()
    {
        using var f = new Fixture();
        f.Own("I91", "I94");
        var outside = f.Enemy(10f);
        outside.gameObject.AddComponent<EnemyGoldReward>().Initialize(f.Wallet, 10, f.Items);
        DamageManager.Apply(f.Hit(100f), outside, outside.gameObject);
        Check(!outside.DeathContext.WasInCombat && f.Wallet.Gold == 11,
            "Before the first room, I91 must stay inactive while I94 still rewards normal kills");

        f.Items.SetCombatState(true);
        var final = f.Enemy(10f);
        final.OnDied += () => f.Items.SetCombatState(false);
        final.gameObject.AddComponent<EnemyGoldReward>().Initialize(f.Wallet, 10, f.Items);
        DamageManager.Apply(f.Hit(100f), final, final.gameObject);
        Check(final.DeathContext.WasInCombat && !f.Items.IsInCombat && f.Wallet.Gold == 25,
            "The final active-combat kill lost I91 after its room-close callback");

        outside = f.Enemy(10f);
        outside.gameObject.AddComponent<EnemyGoldReward>().Initialize(f.Wallet, 10, f.Items);
        DamageManager.Apply(f.Hit(100f), outside, outside.gameObject);
        Check(!outside.DeathContext.WasInCombat && f.Wallet.Gold == 36,
            "A later outside kill gained a combat-first bonus or lost I94");

        f.Items.SetCombatState(true);
        f.Items.SetCombatState(false);
        outside = f.Enemy(10f);
        outside.gameObject.AddComponent<EnemyGoldReward>().Initialize(f.Wallet, 10, f.Items);
        DamageManager.Apply(f.Hit(100f), outside, outside.gameObject);
        Check(f.Wallet.Gold == 47,
            "An unclaimed prior combat epoch incorrectly awarded I91 to an outside kill");
    }

    private static void VerifyIncomingReentrancy()
    {
        using (var f = new Fixture())
        {
            f.Own("I31", "I77", "I78");
            f.EnableIncoming();
            f.Items.SetCombatState(true);
            var attacker = f.Enemy(1000f);
            bool dispatched = false;
            float shieldAtObserver = -1f;
            DamageResult nested = default;
            f.Stats.OnDamageTaken += (_, _, _) =>
            {
                if (dispatched) return;
                dispatched = true;
                shieldAtObserver = f.Stats.Shield;
                nested = f.Player.TakeDamage(100f, isParryable: false, poiseDamage: 0f, source: attacker);
            };
            var outer = f.Player.TakeDamage(250f, isParryable: false, poiseDamage: 0f, source: attacker);
            Check(dispatched && nested.Applied, "Incoming observer did not synchronously dispatch the second hit");
            Near(outer.HealthDamage, 220f, "The original hit uses the first incoming reduction once");
            Near(outer.ShieldDamage, 0f, "The original hit cannot be protected by its newly triggered shield");
            Near(shieldAtObserver, 40f, "New shield must exist before incoming observers");
            Near(nested.ShieldDamage, 40f, "The nested hit consumes the post-hit shield");
            Near(nested.HealthDamage, 52f, "Nested hit uses I78 reduction and cannot reuse I77");
            Near(f.Stats.CurrentHp, 728f, "Nested incoming damage committed exactly once");
        }
        using (var f = new Fixture())
        {
            f.Own("I31", "I50", "I78");
            f.EnableIncoming();
            f.Items.SetCombatState(true);
            var attacker = f.Enemy(1000f);
            float shieldAtObserver = -1f;
            f.Stats.OnDamageTaken += (_, _, _) => shieldAtObserver = f.Stats.Shield;
            var lethal = f.Player.TakeDamage(2000f, isParryable: false, poiseDamage: 0f, source: attacker);
            Check(lethal.Killed, "Lethal original damage did not report death");
            Near(lethal.HealthDamage, 1000f, "Lethal original damage is not retroactively prevented");
            Near(f.Stats.CurrentHp, 0f, "Lethal original damage cannot trigger healing");
            Near(f.Stats.Shield, 0f, "Lethal original damage cannot grant post-death shields");
            Near(shieldAtObserver, 0f, "Observers must not see a post-death shield");
        }
    }

    private static void VerifyShieldCallbackReentrancy()
    {
        foreach (bool breakShield in new[] { false, true })
        {
            using var f = new Fixture();
            f.Own("I71");
            var boss = f.Boss();
            var shield = boss.GetComponent<BossShield>();
            shield.SetAbsorption(breakShield ? 107f : 1000f);
            var inner = f.Enemy(1000f);
            bool dispatched = false;
            DamageResult nested = default;
            void Dispatch()
            {
                if (dispatched) return;
                dispatched = true;
                nested = DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 401), inner, inner.gameObject);
            }
            if (breakShield) shield.OnShieldBroken += Dispatch;
            else shield.OnShieldChanged += _ => Dispatch();
            var outer = DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 401), boss, boss.gameObject);
            Check(dispatched && nested.Applied, "Shield observer did not synchronously dispatch the second hit");
            Near(outer.ShieldDamage, 107f, "Outer shield contact receives first cast bonus");
            Near(outer.HealthDamage, 0f, "Fully absorbed first contact does not damage boss HP");
            Near(nested.HealthDamage, 100f, "Shield callback cannot reuse the first cast bonus");
            Near(DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 401), inner, inner.gameObject).HealthDamage,
                100f, "Shield contact's cast remains committed after callback returns");
        }
    }

    private static void VerifyBossPhaseReward(Fixture f)
    {
        f.Own("I92");
        f.Items.SetCombatState(true);
        var boss = f.Boss();
        var rewards = f.Asset<BossRewardGroupSO>(); rewards.gold = 150;
        var reward = boss.gameObject.AddComponent<EnemyGoldReward>();
        reward.Initialize(f.Wallet, f.Items, rewards);
        int before = f.Wallet.Gold;
        var depletion = DamageManager.Apply(f.Hit(1000f), boss, boss.gameObject);
        Check(depletion.Applied && !depletion.Killed && boss.IsTransitioning && !boss.IsDead,
            "The first boss health bar was treated as a final death");
        Check(f.Wallet.Gold == before && reward.BaseGold == 150, "Nonfinal boss phase paid gold or changed the base reward");
        Check(!DamageManager.Apply(f.Hit(1000f), boss, boss.gameObject).Applied && f.Wallet.Gold == before,
            "A same-frame invulnerable phase hit paid a boss reward");
        typeof(Boss).GetMethod("BeginPhaseTransition", Hidden).Invoke(boss, null);
        typeof(Boss).GetMethod("TickPhaseTransition", Hidden).Invoke(boss, new object[] { 3f });
        Check(!boss.IsTransitioning && boss.PhaseIndex == 1, "The boss fixture did not revive into its final phase");
        var death = DamageManager.Apply(f.Hit(1000f), boss, boss.gameObject);
        Check(death.Killed && boss.IsDead && f.Wallet.Gold == before + 154,
            "Final boss death did not pay base150 plus the elite/boss bonus exactly once");
        DamageManager.Apply(f.Hit(1000f), boss, boss.gameObject);
        Check(f.Wallet.Gold == before + 154, "A dead boss paid a second reward");
    }

    private static void VerifyPhaseCallbackReentrancy()
    {
        using var f = new Fixture();
        f.Own("I71");
        var boss = f.Boss();
        var inner = f.Enemy(1000f);
        int depleted = 0;
        DamageResult nested = default;
        boss.OnPhaseDepleted += _ =>
        {
            depleted++;
            nested = DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 501), inner, inner.gameObject);
        };
        var outer = DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 501), boss, boss.gameObject);
        Check(outer.Applied && !outer.Killed && boss.IsTransitioning && depleted == 1,
            "Phase depletion must remain a single nonlethal phase boundary");
        Check(nested.Applied, "Phase observer did not synchronously dispatch a nested hit");
        Near(nested.HealthDamage, 100f, "Phase-depleted callback cannot reuse the first cast bonus");
        Near(DamageManager.Apply(f.Hit(100f, PlayerAttackCategory.Skill, cast: 501), inner, inner.gameObject).HealthDamage,
            100f, "Phase-depleting first contact remains committed after the observer returns");
    }

    private static void GoldRolls()
    {
        var random = UnityEngine.Random.state;
        var values = new HashSet<int>();
        for (int seed = 0; seed < 256; seed++)
        {
            int value = EnemyGoldRoll.Roll(seed, 3, 1, 2, 5, 15);
            Check(value >= 5 && value <= 15 && value == EnemyGoldRoll.Roll(seed, 3, 1, 2, 5, 15), "Gold roll range/replay");
            values.Add(value);
        }
        Check(values.Count == 11 && values.Contains(5) && values.Contains(15), "Gold roll endpoints not covered");
        Check(random.Equals(UnityEngine.Random.state), "Gold rolls altered Unity RNG");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Scene _scene = EditorSceneManager.NewPreviewScene();
        private readonly List<Object> _objects = new();
        private readonly CombatSettings _previous = CombatSettings.Active;
        private readonly UnityEngine.Random.State _random = UnityEngine.Random.state;
        public readonly PlayerController Player;
        public readonly PlayerStatManager Stats;
        public readonly PlayerItemRuntime Items;
        public readonly PlayerWallet Wallet;
        private readonly ItemCatalog _catalog;
        public Fixture()
        {
            var go = Make("Boundary integration player", false);
            Player = go.AddComponent<PlayerController>();
            Stats = go.GetComponent<PlayerStatManager>();
            var data = Asset<PlayerStatData>(); data.baseHP = 1000f; data.baseATK = 100f; data.baseCRT = 0f; data.baseElmAtk = 0f;
            Stats.Initialize(data);
            Set(Player, nameof(PlayerController.StatManager), Stats);
            Set(Player, nameof(PlayerController.FormManager), new FormManager(Player));
            Set(Player, nameof(PlayerController.StateMachine), new StateMachine());
            Items = go.AddComponent<PlayerItemRuntime>();
            _catalog = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Generated/BoundaryInventory/ItemCatalog.asset"));
            _objects.Add(_catalog); _catalog.synergies = Array.Empty<ItemSynergyDefinition>();
            // This ordering regression still exercises the previous rules, including retired kill effects.
            var legacyDefinitions = new List<ItemDefinition>(BoundaryInventoryContent.Grades.Count);
            foreach (var entry in BoundaryInventoryContent.Grades)
            {
                string id = entry.Key;
                var source = _catalog.Find(id) ?? AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    $"Assets/_Project/Generated/BoundaryInventory/Definitions/{id}.asset");
                var definition = source != null
                    ? Object.Instantiate(source)
                    : ScriptableObject.CreateInstance<ItemDefinition>();
                _objects.Add(definition);
                definition.projectId = _catalog.projectId;
                definition.itemId = id;
                definition.candidateKey = id;
                if (source == null) definition.displayName = id;
                definition.grade = entry.Value;
                definition.purchasePrice = 20 << (entry.Value - 1);
                definition.boundaryEffects = BoundaryInventoryContent.Effects(id);
                legacyDefinitions.Add(definition);
            }
            _catalog.items = legacyDefinitions.ToArray();
            Items.Catalog = _catalog; Items.Initialize(Stats, () => ElementType.ELEMENT_NONE);
            Set(Player, nameof(PlayerController.ItemRuntime), Items);
            Wallet = go.AddComponent<PlayerWallet>(); Wallet.Initialize(0);
            var table = Asset<ElementEffectiveManager>();
            foreach (ElementType a in Enum.GetValues(typeof(ElementType)))
                foreach (ElementType b in Enum.GetValues(typeof(ElementType)))
                    table.elementEffectiveList.Add(new ElementEffective { AttackType = a, DefenseType = b, multiplier = 1f });
            table.Initialize();
            var settings = Make("Neutral table", false).AddComponent<CombatSettings>();
            typeof(CombatSettings).GetField("_elementTable", Hidden).SetValue(settings, table);
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, settings);
        }
        public T Asset<T>() where T : ScriptableObject
        { var asset = ScriptableObject.CreateInstance<T>(); _objects.Add(asset); return asset; }
        private GameObject Make(string name, bool active = true)
        {
            var go = new GameObject(name); go.SetActive(false);
            SceneManager.MoveGameObjectToScene(go, _scene); _objects.Add(go); go.SetActive(active); return go;
        }
        public Enemy Enemy(float hp, float defense = 0f)
        {
            var go = Make("Boundary integration enemy", false);
            var enemy = go.AddComponent<Enemy>(); enemy.EnemyStat = go.GetComponent<EnemyStat>();
            var data = Asset<EnemyData>(); data.maxHp = Mathf.RoundToInt(hp); data.baseDef = defense;
            typeof(EnemyStat).GetField("baseData", Hidden).SetValue(enemy.EnemyStat, data);
            typeof(EnemyStat).GetMethod("InitializeFromData", Hidden).Invoke(enemy.EnemyStat, null);
            go.GetComponent<PoiseHandler>().ResetPoise(); go.SetActive(true); return enemy;
        }
        public ConfiguredBoss Boss()
        {
            var data = Asset<BossData>(); data.maxHp = 100; data.baseDef = 0f;
            data.phaseDownSeconds = 1f; data.phaseHoldSeconds = .5f; data.phaseReviveSeconds = 1.5f;
            var first = Asset<BossPhaseData>(); first.phaseMaxHp = 60f;
            var second = Asset<BossPhaseData>(); second.phaseMaxHp = 40f;
            var go = Make("Boundary integration phased boss", false);
            go.AddComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
            var boss = go.AddComponent<ConfiguredBoss>();
            boss.EnemyStat = go.GetComponent<EnemyStat>();
            typeof(Boss).GetField("BossData", Hidden).SetValue(boss, data);
            typeof(Boss).GetField("PhaseDatas", Hidden).SetValue(boss, new List<BossPhaseData> { first, second });
            typeof(EnemyStat).GetField("baseData", Hidden).SetValue(boss.EnemyStat, data);
            typeof(EnemyStat).GetMethod("InitializeFromData", Hidden).Invoke(boss.EnemyStat, null);
            boss.StaggerResistLevel = StaggerResistLevel.SUPER_ARMOR;
            go.SetActive(true);
            typeof(Boss).GetMethod("CreateBrain", Hidden).Invoke(boss, null);
            return boss;
        }
        public HitInfo Hit(float amount, PlayerAttackCategory category = PlayerAttackCategory.Normal, long cast = 0, bool trueDamage = true)
            => new HitInfo(amount, ElementType.ELEMENT_NONE, 0f, 1.5f, source: Player, trueDamage: trueDamage,
                category: category, hasSourceDamageSnapshot: true, sourceDamageMultiplier: 1f, castId: cast);
        public void Own(params string[] ids)
        {
            var remove = new List<ItemDefinition>(Items.OwnedItems);
            foreach (var item in remove) Items.TryRemoveItem(item.itemId);
            foreach (string id in ids) Check(Items.TryAddItem(id), "Cannot own " + id);
        }
        public void EnableIncoming()
        {
            Player.HitStaggerResistance = StaggerResistLevel.SUPER_ARMOR;
            Player.gameObject.SetActive(true);
        }
        public void Dispose()
        {
            for (int i = _objects.Count - 1; i >= 0; i--) if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, _previous);
            EditorSceneManager.ClosePreviewScene(_scene); UnityEngine.Random.state = _random;
        }
    }
    private static void Set(object target, string property, object value)
        => target.GetType().GetProperty(property).SetValue(target, value);
    private static void Near(float actual, float expected, string message)
        => Check(Mathf.Abs(actual - expected) < 0.001f, message + ": " + actual + " != " + expected);
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
}
