using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using InventoryView = BoundaryZero.Inventory.InventoryView;
using Object = UnityEngine.Object;

/// <summary>Exercises the shipped inventory view and the current run's presentation binding.</summary>
public static class InventoryIntegrationRegression
{
    [MenuItem("Tools/Studio Baekho/BoundaryZERO/Run Inventory Presentation Regression")]
    private static void Menu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run the prefab fixtures in Edit Mode.");
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Inventory presentation: " + message); checks++; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoundaryInventorySetup.PrefabPath);
        Check(prefab != null, "The authored prefab is missing.");
        GameObject root = Object.Instantiate(prefab);
        try
        {
            var view = root.GetComponent<InventoryView>();
            Check(view != null && view.Catalog != null, "The prefab is not bound to its presentation catalog.");
            var data = view.Catalog.ReadData();
            Check(data.items.Length == 59 && data.synergies.Length == 11, "The shipped content count changed.");
            var ids = new List<string>();
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in data.items)
            { Check(item != null && distinct.Add(item.id), "A shipped item ID is absent or duplicated."); ids.Add(item.id); }
            view.SetOwnedItems(null);
            Check(view.OwnedItemCount == 0 && !view.IsOpen, "An unowned/demo item appeared at initialization.");
            view.SetOwnedItems(new[] { "I16", "I16", "missing", null });
            Check(view.OwnedItemCount == 1, "Duplicates or unknown IDs became owned slots.");
            view.SetOwnedItems(ids);
            Check(view.OwnedItemCount == 59, "The view dropped a real catalog ID.");
            var states = view.GetSynergyStates();
            Check(states.Count == 11, "The view omitted an inactive/active synergy state.");
            foreach (var state in states)
                Check(state.IsActive && state.ActiveTier == 6 && state.DisplayCount == 6,
                    "The full catalog did not produce the highest exclusive synergy tier: " + state.Id);
            int opens = 0, closes = 0;
            view.VisibilityChanged += open => { if (open) opens++; else closes++; };
            view.Open(); view.Open();
            Check(view.IsOpen && opens == 1, "Repeated open published duplicate events.");
            view.Close(); view.Close();
            Check(!view.IsOpen && closes == 1 && view.OwnedItemCount == 59, "Close erased ownership or published duplicate events.");
            view.SetOwnedItems(null);
            foreach (var state in view.GetSynergyStates())
                Check(state.ActiveTier == 0 && state.Count == 0, "Removing items retained a synergy.");
            foreach (var scroll in root.GetComponentsInChildren<UnityEngine.UI.ScrollRect>(true))
                Check(scroll.content != null && scroll.viewport != null && scroll.vertical,
                    "A shipped scroll view lost its content/viewport binding.");
            SharedRuntimeDisplay(root, view, Check);
            return $"PASS: inventory presentation ({checks} checks).";
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void SharedRuntimeDisplay(GameObject root, InventoryView view, Action<bool, string> check)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var created = new List<Object>();
        var source = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Generated/BoundaryInventory/ItemCatalog.asset");
        check(source != null, "Import the authoritative gameplay catalog before testing its shared display binding.");
        var original = view.Catalog.ReadData();
        bool cursorVisible = Cursor.visible;
        var cursorLock = Cursor.lockState;
        string fallbackDescription = null;
        foreach (var item in original.items) if (item.id == "I01") fallbackDescription = item.description;
        PlayerInventoryPresenter presenter = null;
        try
        {
            var catalog = Object.Instantiate(source); created.Add(catalog);
            var item = Object.Instantiate(catalog.Find("I01")); created.Add(item);
            item.displayName = "Runtime display name";
            item.description = "Runtime description independent of fallback JSON";
            item.effectText = "Runtime effect independent of fallback JSON";
            for (int i = 0; i < catalog.items.Length; i++)
                if (catalog.items[i].itemId == item.itemId) catalog.items[i] = item;
            ItemSynergyDefinition set = null;
            foreach (var candidate in catalog.synergies) if (candidate.id == "S03") set = candidate;
            check(set != null, "The runtime fixture has no S03 synergy.");
            set.displayName = "Runtime synergy name";
            set.description = "Runtime synergy rule";
            foreach (var tier in set.tiers) if (tier.count == 2) tier.text = "Runtime two-set effect";

            var playerHost = new GameObject("Inventory display player"); created.Add(playerHost); playerHost.SetActive(false);
            var player = playerHost.AddComponent<PlayerController>();
            var items = playerHost.GetComponent<PlayerItemRuntime>() ?? playerHost.AddComponent<PlayerItemRuntime>();
            items.Catalog = catalog;
            var runHost = new GameObject("Inventory display run"); created.Add(runHost); runHost.SetActive(false);
            var run = runHost.AddComponent<RunManager>(); run.player = player;
            presenter = root.AddComponent<PlayerInventoryPresenter>();
            presenter.Configure(run, view);
            var dataField = typeof(InventoryView).GetField("data", hidden);
            var bound = (BoundaryZero.Inventory.InventoryData)dataField.GetValue(view);
            presenter.SyncOwnedItems();
            check(ReferenceEquals(bound, dataField.GetValue(view)), "Unchanged runtime catalog rebuilt display data.");

            // Swapping an empty inventory's catalog does not publish an ownership event; Bind must still detect it.
            var replacement = Object.Instantiate(catalog); created.Add(replacement);
            var changedItem = Object.Instantiate(item); created.Add(changedItem);
            changedItem.description = "Replacement runtime description";
            changedItem.effectText = "Replacement runtime effect";
            for (int i = 0; i < replacement.items.Length; i++)
                if (replacement.items[i].itemId == changedItem.itemId) replacement.items[i] = changedItem;
            items.Catalog = replacement;
            typeof(PlayerInventoryPresenter).GetMethod("Bind", hidden).Invoke(presenter, null);
            check(!ReferenceEquals(bound, dataField.GetValue(view)), "A replaced runtime catalog retained old display copy.");
            presenter.Configure(null, null);
            Object.DestroyImmediate(presenter); presenter = null;

            string partner = null;
            foreach (var member in set.members) if (member != "I01") { partner = member; break; }
            view.SetOwnedItems(new[] { "I01", partner });
            view.Open();
            BoundaryZero.Inventory.InventoryHoverTarget target = null;
            foreach (var candidate in root.GetComponentsInChildren<BoundaryZero.Inventory.InventoryHoverTarget>(true))
                if (candidate.name == "Item_I01") { target = candidate; break; }
            check(target != null, "The authored I01 hover target is missing.");
            typeof(InventoryView).GetMethod("ShowTooltip", hidden).Invoke(view, new object[] { target, "I01", false });
            var tooltip = (TMPro.TMP_Text)typeof(InventoryView).GetField("tooltipText", hidden).GetValue(view);
            string shopText = ItemDisplayText.Description(changedItem, replacement);
            foreach (var expected in new[] { changedItem.description, changedItem.effectText, "Runtime synergy name", "Runtime two-set effect" })
                check(tooltip.text.Contains(expected) && shopText.Contains(expected),
                    "Inventory and shop did not share runtime display copy: " + expected);
            bool matchedTier = false;
            foreach (var state in view.GetSynergyStates())
                if (state.Id == "S03") matchedTier = state.Count == 2 && state.ActiveTier == 2 && state.ActiveEffect == "Runtime two-set effect";
            check(matchedTier, "Runtime tier text/count was replaced by fallback JSON.");
            check(tooltip.text.Contains(changedItem.displayName) && tooltip.text.Contains("Runtime synergy name"),
                "Runtime item/synergy names were not displayed.");
            view.Close();
            view.SetDisplayData(null);
            var restored = (BoundaryZero.Inventory.InventoryData)dataField.GetValue(view);
            bool fallbackRestored = false;
            foreach (var definition in restored.items)
                if (definition.id == "I01") fallbackRestored = definition.description == fallbackDescription;
            check(fallbackRestored, "Clearing the host override did not restore standalone package display data.");
            check(source.Find("I01").description != changedItem.description,
                "The shared display fixture modified the authoritative source asset.");
        }
        finally
        {
            if (presenter != null) { presenter.Configure(null, null); Object.DestroyImmediate(presenter); }
            view.Close();
            view.SetDisplayData(null);
            view.SetOwnedItems(null);
            for (int i = created.Count - 1; i >= 0; i--) if (created[i] != null) Object.DestroyImmediate(created[i]);
            Cursor.visible = cursorVisible;
            Cursor.lockState = cursorLock;
        }
    }

    public static string RunPlayMode()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run the live binding checks in Play Mode.");
        var presenter = Object.FindFirstObjectByType<PlayerInventoryPresenter>();
        if (presenter == null || presenter.Run == null || presenter.Run.State != RunState.Playing)
            throw new InvalidOperationException("Wait for the bound Game Scene run to finish preparation.");
        var run = presenter.Run;
        var player = run.player;
        var items = player.GetComponent<PlayerItemRuntime>();
        if (run.IsInventoryOpen || run.IsShopOpen || run.IsMapOpen)
            throw new InvalidOperationException("Close existing menus before running the live checks.");
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Live inventory: " + message); checks++; }
        float scale = Time.timeScale;
        try
        {
            presenter.SyncOwnedItems();
            Check(presenter.View.OwnedItemCount == items.OwnedItems.Count, "The view differs from real ownership.");
            var visibleStates = presenter.View.GetSynergyStates();
            Check(visibleStates.Count == items.SynergyStates.Count, "Presentation and runtime synergy catalogs differ.");
            foreach (var actual in items.SynergyStates)
            {
                bool matched = false;
                foreach (var displayed in visibleStates)
                    if (displayed.Id == actual.Definition.id)
                    {
                        matched = true;
                        Check(displayed.Count == actual.OwnedCount
                            && displayed.ActiveTier == (actual.ActiveTier?.count ?? 0),
                            "The displayed synergy count/tier differs from authoritative runtime: " + displayed.Id);
                        break;
                    }
                Check(matched, "The view omitted a runtime synergy: " + actual.Definition.id);
            }
            Check(presenter.View.GetComponent<Canvas>().sortingOrder == 170, "The inventory uses the wrong UI layer.");
            Check(presenter.Open() && run.IsInventorySessionCurrent && player.IsInventoryPaused,
                "The presenter did not acquire the run modal.");
            Check(Mathf.Approximately(Time.timeScale, 0f) && Cursor.visible && Cursor.lockState == CursorLockMode.None,
                "Opening did not pause world time and release the pointer.");
            Check(!run.TryOpenShop(presenter) && !run.TryOpenMap(presenter), "Another modal opened over the inventory.");
            presenter.View.Close();
            presenter.View.Close(); presenter.Close();
            Check(!run.IsInventoryOpen && !player.IsInventoryPaused, "The authored close event did not release the modal.");
            Check(Mathf.Approximately(Time.timeScale, scale), "Close changed the previous world speed.");
            Check(presenter.Open(), "The presenter could not reopen.");
            presenter.enabled = false;
            Check(!run.IsInventoryOpen && !player.IsInventoryPaused && !presenter.View.IsOpen,
                "Disabling the presenter stranded a menu or pause.");
            return $"PASS: live inventory binding ({checks} checks).";
        }
        finally
        {
            presenter.Close();
            presenter.enabled = true;
        }
    }
}
