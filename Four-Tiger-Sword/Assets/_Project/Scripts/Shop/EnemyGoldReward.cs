using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One confirmed death pays once; disabling, clearing or destroying an enemy never pays.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Enemy))]
public sealed class EnemyGoldReward : MonoBehaviour
{
    private Enemy _enemy;
    private PlayerWallet _wallet;
    private PlayerItemRuntime _items;
    private readonly List<ItemDefinition> _itemRewards = new List<ItemDefinition>();
    private int _amount;
    private bool _initialized, _subscribed, _paid;
    public bool IsInitialized => _initialized;
    public int BaseGold => _amount;

    public void Initialize(PlayerWallet wallet, int amount)
        => Initialize(wallet, amount, null);

    public void Initialize(PlayerWallet wallet, int amount, PlayerItemRuntime items)
    {
        if (wallet == null) throw new ArgumentNullException(nameof(wallet));
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Unsubscribe();
        _wallet = wallet;
        _amount = amount;
        _items = items;
        _itemRewards.Clear();
        _enemy = GetComponent<Enemy>();
        _initialized = true;
        Subscribe();
    }

    /// <summary>Snapshots a boss reward without adding the legacy stage gold a second time.</summary>
    public void Initialize(PlayerWallet wallet, PlayerItemRuntime items, BossRewardGroupSO group)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        if (!group.Validate(out string error)) throw new ArgumentException(error, nameof(group));
        if (group.items.Count > 0 && (items == null || items.Catalog == null || !items.IsInitialized))
            throw new ArgumentException("Boss item rewards require the initialized run item inventory.", nameof(items));
        foreach (var definition in group.items)
            if (items.Catalog.Find(definition.itemId) != definition)
                throw new ArgumentException("Boss reward item is not in the run's item catalog: " + definition.itemId, nameof(group));
        Initialize(wallet, group.gold);
        _items = items;
        _itemRewards.AddRange(group.items);
    }

    private void Subscribe()
    {
        if (!_initialized || _paid || _subscribed || !isActiveAndEnabled || _enemy == null) return;
        _enemy.OnDied += OnDied;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (_subscribed && _enemy != null) _enemy.OnDied -= OnDied;
        _subscribed = false;
    }

    private void OnDied()
    {
        if (_paid || !_initialized || !isActiveAndEnabled) return;
        _paid = true;
        Unsubscribe();
        var wallet = _wallet;
        var items = _items;
        int amount = _amount;
        var rewards = _itemRewards.ToArray();
        var death = _enemy != null ? _enemy.DeathContext : default;
        int engravingBonus = death.Player != null && death.Player.Engravings != null
            ? death.Player.Engravings.BonusKillGold(_enemy, death, _amount) : 0;
        if (items != null && death.Player != null && death.Player.ItemRuntime == items)
        {
            var context = new ItemKillContext(_enemy.gameObject.GetInstanceID(), _enemy.Rank != EnemyRank.Normal,
                death.Family, death.CanTrigger, amount, death.PlayerHp, death.PlayerMaxHp, death.CombatEpoch, death.WasInCombat);
            int bonus = items.ProcessKill(context);
            amount = (int)Math.Min(int.MaxValue, (long)amount + bonus);
        }
        amount = (int)Math.Min(int.MaxValue, (long)amount + engravingBonus);
        if (wallet != null) wallet.AddGold(amount);
        if (items == null || !items.IsInitialized || items.Catalog == null) return;
        // Ownership is checked again at death: acquiring an item after spawn skips it without compensation.
        foreach (var definition in rewards)
        {
            if (definition == null || items.Catalog.Find(definition.itemId) != definition) continue;
            try { items.TryAddItem(definition.itemId); }
            catch (Exception error) { Debug.LogWarning("[Boss Reward] Item grant failed: " + error.Message, this); }
        }
    }

    private void OnEnable() => Subscribe();
    private void OnDisable() => Unsubscribe();
    private void OnDestroy() => Unsubscribe();
}
