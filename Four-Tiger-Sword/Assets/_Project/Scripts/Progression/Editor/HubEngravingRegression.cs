using System;
using System.Collections.Generic;
using System.Linq;
using Penta.Upgrade;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Isolated workshop integration acceptance; never reads or changes the player's save.</summary>
public static class HubEngravingRegression
{
    [MenuItem("Tools/Studio Baekho/Progression/Verify Engraving Workshop")]
    private static void Menu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run isolated prefab verification in Edit Mode.");
        var root = PrefabUtility.LoadPrefabContents(HubWorkshopSetup.PrefabPath);
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        try
        {
            var view = root.GetComponent<HubWorkshopView>();
            var tree = view.EngravingView;
            Check(tree != null && tree.NodeCount == 40, "The workshop must expose all 40 delivered nodes.");
            Check(tree.GetComponentInChildren<PentaUpgradeLinks>(true).ConnectionCount == 39, "The delivered 39 connections changed.");
            Check(root.GetComponent<Canvas>() != null && root.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null,
                "The workshop requires its host canvas and raycaster.");
            Check(view.Entries.Length == 3, "Keep all three basic upgrades.");
            var settings = Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings");
            Check(settings != null && settings.Validate(out _), "Installed basic settings are invalid.");
            var profile = PlayerUpgradeProfile.New(); profile.currency = 5000;
            var store = new MemoryStore(profile);
            var service = new PlayerUpgradeService(settings, store);
            view.Bind(service);
            view.Open();
            Check(view.SelectedPage == HubWorkshopView.WorkshopPage.Basic && view.BasicWindow.activeSelf,
                "The default workshop page must be Basic.");
            view.ShowEngravingPage();
            Check(tree.IsOpen && tree.gameObject.activeSelf && !view.BasicWindow.activeSelf,
                "The engraving tab must replace the basic page inside the same modal.");
            Check(tree.GetNodeState("origin").Availability == PentaUpgradeAvailability.Owned, "The free origin must be unlocked.");
            Check(tree.GetNodeState("crit").Availability == PentaUpgradeAvailability.Locked, "A child needs its parent.");
            foreach (var concept in PentaUpgradeCatalog.Nodes)
            {
                var button = tree.GetNodeButton(concept.Id);
                Check(button != null && button.targetGraphic != null && button.targetGraphic.raycastTarget,
                    "A node has no selectable pointer graphic: " + concept.Id);
                button.onClick.Invoke();
                Check(tree.SelectedId == concept.Id, "Node selection was not wired: " + concept.Id);
                if (concept.Id == "origin") continue;
                int before = service.Balance;
                int cost = service.GetNodePurchaseState(concept.Id).Cost;
                Check(tree.PurchaseButton.interactable, "A funded, eligible node is not purchasable: " + concept.Id);
                tree.PurchaseButton.onClick.Invoke();
                Check(service.IsNodeUnlocked(concept.Id) && service.Balance == before - cost,
                    "Purchase did not atomically charge and unlock: " + concept.Id);
                Check(tree.GetNodeState(concept.Id).Availability == PentaUpgradeAvailability.Owned,
                    "A purchased node was not refreshed: " + concept.Id);
                tree.PurchaseButton.onClick.Invoke();
                Check(service.Balance == before - cost, "A completed node charged twice: " + concept.Id);
            }
            tree.SetZoom(.85f); tree.SelectAndFocus("fusion");
            Vector2 position = tree.NodeContent.anchoredPosition;
            view.ShowBasicPage(); view.ShowEngravingPage();
            Check(tree.SelectedId == "fusion" && Mathf.Approximately(tree.Zoom, .85f)
                && Vector2.Distance(tree.NodeContent.anchoredPosition, position) < .1f,
                "Tab switches must retain tree selection, zoom and scroll.");
            view.Close(); view.Open();
            Check(view.SelectedPage == HubWorkshopView.WorkshopPage.Engravings && tree.SelectedId == "fusion",
                "Reopening must remember the last tab and selection within this Hub session.");
            Check(Vector2.Distance(tree.NodeContent.anchoredPosition, position) < .1f, "Reopening reset tree scroll.");
            view.ShowBasicPage();
            int level = service.GetLevel(view.SelectedStat);
            view.UI.purchaseButton.onClick.Invoke();
            Check(service.GetLevel(view.SelectedStat) == level + 1, "Basic upgrades stopped working alongside the tree.");
            var empty = new PlayerUpgradeService(settings, new MemoryStore(PlayerUpgradeProfile.New()));
            view.Bind(empty); view.ShowEngravingPage(); tree.Select("atk");
            Check(tree.GetNodeState("atk").Availability == PentaUpgradeAvailability.Unaffordable
                && !tree.PurchaseButton.interactable, "An unaffordable node must display its state and block purchase.");
            var failingProfile = PlayerUpgradeProfile.New(); failingProfile.currency = 100;
            var failingStore = new MemoryStore(failingProfile) { RejectSaves = true };
            var failing = new PlayerUpgradeService(settings, failingStore);
            view.Bind(failing); tree.Select("atk"); tree.PurchaseButton.onClick.Invoke();
            Check(!failing.IsNodeUnlocked("atk") && failing.Balance == 100 && !string.IsNullOrEmpty(tree.PurchaseStatus.text),
                "Save failure must leave the node and balance unchanged and show a message.");
            Check(tree.DescriptionScroll != null && tree.DescriptionScroll.viewport != null
                && tree.DescriptionScroll.content != null, "Numeric descriptions need a configured scroll area.");
            view.Close();
            return "PASS: " + checks + " engraving UI checks, all 40 selections/39 purchases, basic/tree tabs, state refresh, remembered viewport and save failure.";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>Call after opening the tree in Play Mode to verify every focused node receives real UI raycasts.</summary>
    public static string VerifyCurrentPointers()
    {
        var view = UnityEngine.Object.FindFirstObjectByType<HubWorkshopView>();
        if (!Application.isPlaying || view == null || !view.IsInteractionReady || EventSystem.current == null)
            throw new InvalidOperationException("Open the workshop in Play Mode before checking tree pointer targets.");
        view.ShowEngravingPage();
        var tree = view.EngravingView;
        string selected = tree.SelectedId; float zoom = tree.Zoom; Vector2 position = tree.NodeContent.anchoredPosition;
        try
        {
            tree.SetZoom(1f);
            foreach (var node in PentaUpgradeCatalog.Nodes)
            {
                tree.SelectAndFocus(node.Id); Canvas.ForceUpdateCanvases();
                var button = tree.GetNodeButton(node.Id);
                var rect = (RectTransform)button.transform;
                var pointer = new PointerEventData(EventSystem.current)
                    { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
                var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                if (!hits.Any() || !(hits[0].gameObject == button.gameObject || hits[0].gameObject.transform.IsChildOf(button.transform)))
                    throw new InvalidOperationException("Focused node did not receive the top pointer raycast: " + node.Id);
            }
            return "PASS: all 40 focused nodes receive pointer raycasts at " + Screen.width + "x" + Screen.height + ".";
        }
        finally { tree.Select(selected); tree.SetZoom(zoom); tree.NodeContent.anchoredPosition = position; }
    }

    public static string VerifyCurrentRewardView()
    {
        var view = UnityEngine.Object.FindFirstObjectByType<RunItemChoiceView>();
        if (!Application.isPlaying || view == null || !view.IsOpen || EventSystem.current == null)
            throw new InvalidOperationException("Open an item choice in Play Mode before verifying its layout.");
        Canvas.ForceUpdateCanvases();
        int buttons = 0, scrolls = 0;
        foreach (var scroll in view.GetComponentsInChildren<UnityEngine.UI.ScrollRect>())
        {
            if (scroll.viewport == null || scroll.content == null || !scroll.vertical || scroll.horizontal
                || scroll.viewport.GetComponent<UnityEngine.UI.RectMask2D>() == null)
                throw new InvalidOperationException("An item choice description has no configured vertical masked scroll.");
            var label = scroll.content.GetComponent<TMPro.TMP_Text>();
            if (label == null || label.font == null || !label.font.HasCharacters(label.text, out uint[] _, true, true))
                throw new InvalidOperationException("An item choice description has a missing Korean glyph.");
            scrolls++;
        }
        foreach (var button in view.GetComponentsInChildren<UnityEngine.UI.Button>())
        {
            if (!button.interactable) continue;
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current)
                { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count == 0 || !(hits[0].gameObject == button.gameObject || hits[0].gameObject.transform.IsChildOf(button.transform)))
                throw new InvalidOperationException("An item choice button cannot receive pointer input: " + button.name);
            buttons++;
        }
        if (view.VisibleCardCount > 4 || (view.VisibleCardCount > 0 && scrolls != view.VisibleCardCount))
            throw new InvalidOperationException("The item choice card count or description scroll count is invalid.");
        return "PASS: item choice has " + view.VisibleCardCount + " cards, " + scrolls + " masked descriptions and "
            + buttons + " selectable pointer targets at " + Screen.width + "x" + Screen.height + ".";
    }

    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        private PlayerUpgradeProfile _profile;
        public bool RejectSaves;
        public MemoryStore(PlayerUpgradeProfile profile) => _profile = profile.Copy();
        public bool TryLoad(out PlayerUpgradeProfile profile, out string error)
        { profile = _profile.Copy(); error = null; return true; }
        public bool TrySave(PlayerUpgradeProfile profile, out string error)
        {
            error = RejectSaves ? "검증용 저장 실패입니다." : null;
            if (RejectSaves) return false;
            _profile = profile.Copy(); return true;
        }
    }
}
