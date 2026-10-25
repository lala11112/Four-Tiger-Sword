using System;
using UnityEngine;

/// <summary>One confirmed death pays once; disabling, clearing or destroying an enemy never pays.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Enemy))]
public sealed class EnemyGoldReward : MonoBehaviour
{
    private Enemy _enemy;
    private PlayerWallet _wallet;
    private int _amount;
    private bool _initialized, _subscribed, _paid;

    public void Initialize(PlayerWallet wallet, int amount)
    {
        if (wallet == null) throw new ArgumentNullException(nameof(wallet));
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Unsubscribe();
        _wallet = wallet;
        _amount = amount;
        _enemy = GetComponent<Enemy>();
        _initialized = true;
        Subscribe();
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
        if (_wallet != null) _wallet.AddGold(_amount);
    }

    private void OnEnable() => Subscribe();
    private void OnDisable() => Unsubscribe();
    private void OnDestroy() => Unsubscribe();
}
