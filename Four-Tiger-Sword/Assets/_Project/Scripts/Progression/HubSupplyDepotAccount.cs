using System;
using System.Collections.Generic;
using BoundaryZero.SupplyDepot;
using UnityEngine;

/// <summary>Connects the hub depot to the shared upgrade balance and the next run's item catalog.</summary>
[DisallowMultipleComponent]
public sealed class HubSupplyDepotAccount : MonoBehaviour, ISupplyDepotAccount
{
    [SerializeField] private ItemCatalog itemCatalog;
    [SerializeField] private SupplyDepotCatalog depotCatalog;

    private PlayerUpgradeService _service;
    private HashSet<string> _depotItemIds;
    private string _catalogError;

    public bool IsReady => _service?.IsReady == true && _depotItemIds != null && _catalogError == null;
    public int PermanentCurrency => IsReady ? _service.Balance : 0;
    public SupplyDepotReceipt CurrentReceipt => _service?.CurrentSupplyDepotReceipt;
    public string LastError { get; private set; }
    public event Action Changed;
    public event Action StatusChanged;

    public void Configure(ItemCatalog items, SupplyDepotCatalog depot)
    {
        itemCatalog = items;
        depotCatalog = depot;
        if (Application.isPlaying && isActiveAndEnabled) RefreshAccount();
    }

    public void RefreshAccount()
    {
        var service = PlayerUpgradeService.Instance;
        if (!ReferenceEquals(_service, service))
        {
            if (_service != null) _service.Changed -= OnServiceChanged;
            _service = service;
            _service.Changed += OnServiceChanged;
        }

        _catalogError = ValidateCatalogs();
        if (!_service.IsReady && _service.TryReload(out _)) return; // Successful recovery publishes Changed.
        OnServiceChanged();
    }

    public bool TryPurchase(string itemId, int cost, out SupplyDepotReceipt receipt)
    {
        receipt = null;
        if (!IsReady)
        {
            SetError(LastError ?? "보급고를 사용할 수 없습니다.");
            return false;
        }
        if (depotCatalog == null || cost != depotCatalog.price || !_depotItemIds.Contains(itemId) || !CanGrant(itemId))
        {
            SetError("보급품 또는 가격 정보가 유효하지 않습니다.");
            return false;
        }

        bool purchased = _service.TryPurchaseSupplyDepot(itemId, cost, out receipt, out string error);
        SetError(error);
        return purchased;
    }

    public void ResetForNextRun()
    {
        var receipt = CurrentReceipt;
        if (receipt != null && !_service.TryClearSupplyDepotReceipt(receipt.transactionId, out string error)) SetError(error);
    }

    private void OnEnable() => RefreshAccount();

    private void OnDisable()
    {
        if (_service != null) _service.Changed -= OnServiceChanged;
        _service = null;
    }

    private void OnServiceChanged()
    {
        var receipt = CurrentReceipt;
        string error = _catalogError ?? (_service?.IsReady == true ? null
            : _service?.LastError ?? "강화 데이터를 불러오지 못했습니다.");
        if (error == null && receipt != null && !_depotItemIds.Contains(receipt.itemId))
            error = "저장된 보급품을 찾을 수 없습니다.";
        SetError(error);
        Notify(Changed);
    }

    private string ValidateCatalogs()
    {
        _depotItemIds = null;
        if (itemCatalog == null || depotCatalog == null || depotCatalog.price < 0 || depotCatalog.items == null
            || depotCatalog.items.Count == 0)
            return "보급고 설정을 확인해 주세요.";

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in depotCatalog.items)
            if (item == null || string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id) || !CanGrant(item.id))
                return "보급품 목록이 탐사 아이템과 일치하지 않습니다.";

        _depotItemIds = ids;
        return null;
    }

    private bool CanGrant(string id)
    {
        var item = itemCatalog != null ? itemCatalog.Find(id) : null;
        return item != null && string.Equals(item.projectId, itemCatalog.projectId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(item.candidateKey) && item.effects != null
            && Array.TrueForAll(item.effects, effect => effect != null && effect.IsValid())
            && (item.boundaryEffects == null || Array.TrueForAll(item.boundaryEffects, effect => effect != null && effect.IsValid()));
    }

    private void SetError(string error)
    {
        if (LastError == error) return;
        LastError = error;
        Notify(StatusChanged);
    }

    private void Notify(Action observers)
    {
        if (observers == null) return;
        foreach (Action observer in observers.GetInvocationList())
        {
            try { observer(); }
            catch (Exception failure) { Debug.LogWarning("[Supply Depot] Observer failed: " + failure, this); }
        }
    }
}
