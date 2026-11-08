using System.Collections.Generic;
using BoundaryZero.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Synchronizes the run inventory with its presentation and owns menu input only.</summary>
[DisallowMultipleComponent]
public sealed class PlayerInventoryPresenter : MonoBehaviour
{
    [SerializeField] private RunManager run;
    [SerializeField] private InventoryView view;
    private RunManager _subscribedRun;
    private PlayerItemRuntime _items;
    private InventoryView _subscribedView;
    private ItemCatalog _displayCatalog;
    private bool _displayDataBound;
    private readonly List<string> _ownedIds = new();
    private bool _syncingVisibility;

    public RunManager Run => run;
    public InventoryView View => view;
    public bool IsOpen => run != null && run.IsInventoryOpen && view != null && view.IsOpen;

    public void Configure(RunManager targetRun, InventoryView targetView)
    {
        Close();
        Unsubscribe();
        run = targetRun;
        view = targetView;
        if (isActiveAndEnabled) Bind();
    }

    private void OnEnable() => Bind();

    private void OnDisable()
    {
        Close();
        Unsubscribe();
    }

    private void Bind()
    {
        var items = run != null && run.player != null ? run.player.GetComponent<PlayerItemRuntime>() : null;
        if (_subscribedRun == run && _items == items && _subscribedView == view)
        { SyncDisplayData(); return; }
        if (_subscribedRun != null) _subscribedRun.CloseInventory();
        Unsubscribe();
        _subscribedRun = run;
        _items = items;
        _subscribedView = view;
        if (_subscribedRun != null) _subscribedRun.InventoryOpenChanged += OnInventoryOpenChanged;
        if (_items != null) _items.OnOwnedItemsChanged += SyncOwnedItems;
        if (_subscribedView != null) _subscribedView.VisibilityChanged += OnViewVisibilityChanged;
        SyncOwnedItems();
        SetViewVisible(run != null && run.IsInventoryOpen);
    }

    private void Unsubscribe()
    {
        if (_subscribedRun != null) _subscribedRun.InventoryOpenChanged -= OnInventoryOpenChanged;
        if (_items != null) _items.OnOwnedItemsChanged -= SyncOwnedItems;
        if (_subscribedView != null) _subscribedView.VisibilityChanged -= OnViewVisibilityChanged;
        _subscribedRun = null;
        _subscribedView = null;
        _items = null;
        _displayCatalog = null;
        _displayDataBound = false;
    }

    private void SyncDisplayData()
    {
        if (view == null) return;
        var catalog = _items != null ? _items.Catalog : null;
        if (_displayDataBound && _displayCatalog == catalog) return;
        _displayCatalog = catalog;
        _displayDataBound = true;
        view.SetDisplayData(catalog != null ? ItemDisplayText.ToInventoryData(catalog) : null);
    }

    public void SyncOwnedItems()
    {
        SyncDisplayData();
        _ownedIds.Clear();
        if (_items != null)
            foreach (var item in _items.OwnedItems)
                if (item != null && !string.IsNullOrEmpty(item.itemId)) _ownedIds.Add(item.itemId);
        if (view != null) view.SetOwnedItems(_ownedIds);
    }

    public bool Open()
    {
        Bind();
        if (view == null || !view.isActiveAndEnabled || _items == null || !_items.IsInitialized
            || run == null || !run.TryOpenInventory(this)) return false;
        SyncOwnedItems();
        SetViewVisible(true);
        return IsOpen;
    }

    public void Toggle()
    {
        if (run != null && run.IsInventoryOpen) Close();
        else Open();
    }

    public void Close()
    {
        if (run != null) run.CloseInventory();
        SetViewVisible(false);
    }

    private void OnInventoryOpenChanged(bool open)
    {
        if (open) SyncOwnedItems();
        SetViewVisible(open);
    }

    private void SetViewVisible(bool open)
    {
        if (view == null) return;
        _syncingVisibility = true;
        try { if (open) view.Open(); else view.Close(); }
        finally { _syncingVisibility = false; }
    }

    private void OnViewVisibilityChanged(bool open)
    {
        if (_syncingVisibility) return;
        if (!open) { if (run != null) run.CloseInventory(); return; }
        if (run == null || !run.TryOpenInventory(this)) SetViewVisible(false);
    }

    private void LateUpdate()
    {
        Bind();
        if (run != null && run.IsInventoryOpen
            && (view == null || !view.isActiveAndEnabled || !view.gameObject.activeInHierarchy || !run.IsInventorySessionCurrent))
            Close();
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (run != null && run.IsInventoryOpen && keyboard.escapeKey.wasPressedThisFrame) Close();
        else if (keyboard.iKey.wasPressedThisFrame) Toggle();
    }
}
