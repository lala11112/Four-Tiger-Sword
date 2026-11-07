using System;
using UnityEngine;

/// <summary>Attach only to the gameplay player. Regression-created players never implicitly open a save file.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(PlayerStatManager))]
public sealed class PlayerUpgradeBinding : MonoBehaviour
{
    private PlayerUpgradeService _service;
    private PlayerStatManager _stats;
    private bool _subscribed;
    private bool _pendingHealthRefill;

    internal void BindForSpawn()
    {
        if (_service != null || !Application.isPlaying) return;
        Bind(PlayerUpgradeService.Instance, true);
    }

    public void Bind(PlayerUpgradeService service, bool refillHealth = false)
    {
        Unsubscribe();
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _stats = GetComponent<PlayerStatManager>();
        _pendingHealthRefill |= refillHealth;
        Apply();
        if (isActiveAndEnabled) Subscribe();
    }

    private void OnEnable()
    {
        BindForSpawn();
        Subscribe();
        Apply();
    }

    private void Subscribe()
    {
        if (_service == null || _subscribed) return;
        _service.Changed += OnChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (_service != null && _subscribed) _service.Changed -= OnChanged;
        _subscribed = false;
    }

    private void OnChanged() => Apply();
    private void Apply()
    {
        if (_service?.IsReady != true || _stats == null) return;
        _stats.SetPermanentUpgradeRatios(_service.GetBonusRatio(StatType.ST_HP),
            _service.GetBonusRatio(StatType.ST_ATK), _service.GetBonusRatio(StatType.ST_DEF), _pendingHealthRefill);
        _pendingHealthRefill = false;
    }

    private void OnDisable() => Unsubscribe();
    private void OnDestroy() => Unsubscribe();
}
