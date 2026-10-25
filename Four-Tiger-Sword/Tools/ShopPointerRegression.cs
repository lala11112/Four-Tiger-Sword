// Invoke Start in a fresh running Game Scene via Unity Pipeline run_script.
// Poll Shop.PointerRegression. Opens, clicks, scrolls and closes through real Input System events.
// No assets/scenes are saved. Test resources, stock and the player spawn are restored in finally.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class ShopPointerRegression
{
    public const string ResultKey = "Shop.PointerRegression";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool _running;
    private static int _checks;

    public static string Start()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode in Game Scene first.");
        if (_running) throw new InvalidOperationException("A pointer regression is already running.");
        var service = Object.FindFirstObjectByType<StageShopService>();
        if (service == null) throw new InvalidOperationException("StageShopService is not installed.");
        _checks = 0;
        _running = true;
        SessionState.SetString(ResultKey, "RUNNING");
        service.StartCoroutine(Drive(Execute(service)));
        return "Shop pointer regression started; poll " + ResultKey + ".";
    }

    private static IEnumerator Drive(IEnumerator task)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(task);
        Exception failure = null;
        try
        {
            while (stack.Count > 0)
            {
                bool moved = false;
                object value = null;
                try { moved = stack.Peek().MoveNext(); if (moved) value = stack.Peek().Current; }
                catch (Exception error) { failure = error; }
                if (failure != null) break;
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (value is IEnumerator nested) { stack.Push(nested); continue; }
                yield return value;
            }
        }
        finally
        {
            while (stack.Count > 0)
            {
                try { (stack.Pop() as IDisposable)?.Dispose(); }
                catch (Exception error) { failure = failure ?? error; }
            }
            _running = false;
            string result = failure == null
                ? $"PASS: shop pointer ({_checks} checks): virtual F, real mouse raycast/purchase, wheel/healing, Esc and service prompt lifecycle."
                : $"FAIL after {_checks} checks: {failure}";
            SessionState.SetString(ResultKey, result);
            if (failure == null) Debug.Log(result); else Debug.LogError(result);
        }
    }

    private static IEnumerator Execute(StageShopService shop)
    {
        Restore restore = null;
        Mouse mouse = null;
        Keyboard keyboard = null;
        var inputSettings = InputSystem.settings;
        var previousEditorInput = inputSettings.editorInputBehaviorInPlayMode;
        var previousBackgroundInput = inputSettings.backgroundBehavior;
        try
        {
            // The automation host is normally unfocused; route test devices to the player loop.
            // These values are restored below and are never saved to project settings.
            inputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            inputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var run = shop.GetComponent<RunManager>();
            Check(run != null, "Shop has no run manager.");
            yield return Wait(() => run.State == RunState.Playing || run.State == RunState.Error,
                "Run generation did not finish.", 180f);
            Check(run.State == RunState.Playing && run.stageManager.IsReady, "The run is not playing: " + run.LastError);
            var player = run.player;
            Check(player != null && player.ItemRuntime != null && player.Controller != null && player.Input != null,
                "The live player is not initialized.");
            Check(shop.isActiveAndEnabled && shop.CurrentStock != null && shop.ShopPoint != null && shop.Wallet != null,
                "The generated shop is unavailable.");
            Check(!run.IsShopOpen && Mathf.Approximately(Time.timeScale, 1f), "Start with the shop closed and ordinary gameplay running.");
            restore = Restore.Capture(run, shop);
            var stock = shop.CurrentStock;
            var available = stock.Offers.Where(offer => !offer.IsHealing && !offer.SoldOut && offer.Item != null
                && !player.ItemRuntime.Contains(offer.Item.itemId)).ToArray();
            Check(available.Length > 0, "The fixture needs at least one unowned item in stock.");
            var heal = stock.Offers.Single(offer => offer.IsHealing);
            Check(!heal.SoldOut, "Start with the stage's healing offer unpurchased.");
            int required = available.Max(offer => offer.Price) + heal.Price + 100;
            Check(shop.Wallet.Gold >= required || shop.Wallet.AddGold(required - shop.Wallet.Gold),
                "Could not fund the pointer purchase fixture.");

            var events = EventSystem.current;
            Check(events != null && events.isActiveAndEnabled, "There is no active EventSystem.");
            var module = events.currentInputModule as InputSystemUIInputModule;
            Check(module != null && module.isActiveAndEnabled, "The active UI module is not InputSystemUIInputModule.");
            Check(module.point != null && module.point.action.enabled && module.leftClick != null && module.leftClick.action.enabled
                && module.scrollWheel != null && module.scrollWheel.action.enabled,
                "The UI module does not have active point/click/scroll actions.");
            keyboard = InputSystem.AddDevice<Keyboard>("ShopPointerKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("ShopPointerMouse");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(-1000f, -1000f) });
            yield return TeleportNear(shop, player);
            yield return Frames(3);
            Check(shop.IsInRange, "The player did not reach the authored shop interaction radius.");
            var view = shop.GetComponentInChildren<ShopView>(true);
            Check(view != null, "The temporary ShopView was not instantiated.");
            var prompt = Field<GameObject>(view, "interactionPrompt");
            var panel = Field<GameObject>(view, "panel");
            Check(prompt.activeInHierarchy && !panel.activeInHierarchy, "The in-range closed-shop prompt is missing.");
            var canvas = view.GetComponent<Canvas>();
            Check(canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.GetComponent<GraphicRaycaster>() != null,
                "This pointer fixture expects the installed overlay canvas with a GraphicRaycaster.");

            yield return KeyPress(keyboard, Key.F);
            Check(run.IsShopOpen && player.IsShopPaused && panel.activeInHierarchy,
                "A real virtual F press did not open the shop.");
            Check(Mathf.Approximately(Time.timeScale, 0f) && Cursor.lockState == CursorLockMode.None && Cursor.visible,
                "Keyboard opening did not pause gameplay and release the pointer.");
            Check(!prompt.activeInHierarchy, "The interaction prompt remained visible over the open shop.");
            Canvas.ForceUpdateCanvases();
            var scroll = view.GetComponentInChildren<ScrollRect>(false);
            Check(scroll != null && scroll.vertical && scroll.viewport != null && scroll.content != null,
                "The visible shop has no vertical offer scroll view.");

            ShopOffer selected = null;
            Button itemButton = null;
            foreach (var row in view.GetComponentsInChildren<ShopOfferView>(false))
            {
                string id = Field<string>(row, "_id");
                var offer = available.FirstOrDefault(candidate => candidate.Id == id);
                if (offer == null) continue;
                var button = Field<Button>(row, "purchaseButton");
                Vector2 center = ScreenCenter(button.transform as RectTransform);
                if (!button.interactable || !RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport, center, null)) continue;
                selected = offer; itemButton = button; break;
            }
            Check(selected != null && itemButton != null, "No affordable item button is actually visible inside the viewport.");
            int goldBefore = shop.Wallet.Gold;
            uint attackPress = player.Input.AttackPressId;
            yield return Click(mouse, itemButton, events);
            Check(selected.SoldOut && player.ItemRuntime.Contains(selected.Item.itemId),
                "A real pointer press/release on the item button did not purchase its offer.");
            Check(shop.Wallet.Gold == goldBefore - selected.Price && !itemButton.interactable,
                "Pointer item purchase charged incorrectly or left its button enabled.");
            Check(player.Input.AttackPressId == attackPress && !player.Input.AttackBuffer.IsActive,
                "Clicking the shop also entered a gameplay attack.");
            Check(run.IsShopOpen && Mathf.Approximately(Time.timeScale, 0f), "Purchasing released the modal unexpectedly.");

            Check(scroll.content.rect.height > scroll.viewport.rect.height + 1f,
                "The fixture needs overflowing offers to verify actual wheel scrolling.");
            Vector2 viewportCenter = ScreenCenter(scroll.viewport);
            var scrollHits = Raycast(events, viewportCenter);
            Check(scrollHits.Count > 0 && scrollHits[0].gameObject.GetComponentInParent<ScrollRect>() == scroll,
                "The top pointer raycast over the viewport does not belong to this ScrollRect.");
            // First wheel to the top through real events, then verify a downward wheel changes content.
            yield return Wheel(mouse, viewportCenter, 1200f);
            float yBeforeScroll = scroll.content.anchoredPosition.y;
            yield return Wheel(mouse, viewportCenter, -1200f);
            Check(Mathf.Abs(scroll.content.anchoredPosition.y - yBeforeScroll) > 0.5f,
                "Real wheel events did not move the offer content while gameplay was paused.");
            var healRow = view.GetComponentsInChildren<ShopOfferView>(false).Single(row => Field<string>(row, "_id") == heal.Id);
            var healButton = Field<Button>(healRow, "purchaseButton");
            Vector2 healingCenter = ScreenCenter(healButton.transform as RectTransform);
            Check(RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport, healingCenter, null),
                "Scrolling did not bring the healing button inside the visible viewport.");
            float maxHp = player.StatManager.MaxHp;
            player.StatManager.Heal(maxHp);
            player.StatManager.ConsumeHealth(maxHp * 0.4f);
            float hpBefore = player.StatManager.CurrentHp;
            int goldBeforeHeal = shop.Wallet.Gold;
            Check(healButton.interactable, "The visible healing button did not become purchasable after HP loss.");
            yield return Click(mouse, healButton, events);
            Check(heal.SoldOut && shop.Wallet.Gold == goldBeforeHeal - heal.Price && !healButton.interactable,
                "A real pointer healing purchase did not charge/consume once.");
            Check(Mathf.Abs(player.StatManager.CurrentHp - Mathf.Min(maxHp, hpBefore + maxHp * shop.settings.healingFraction)) < 0.01f,
                "The pointer healing purchase restored the wrong HP amount.");

            yield return KeyPress(keyboard, Key.Escape);
            Check(!run.IsShopOpen && !player.IsShopPaused && !panel.activeInHierarchy,
                "A real virtual Escape press did not close the shop.");
            Check(Mathf.Approximately(Time.timeScale, 1f) && Cursor.lockState == CursorLockMode.Locked && !Cursor.visible,
                "Escape did not restore world time and the gameplay cursor.");
            Check(prompt.activeInHierarchy && shop.IsInRange, "Closing did not restore the in-range interaction prompt.");

            // Keep the player still and the cached in-range value unchanged across component lifecycle.
            shop.enabled = false;
            yield return Frames(2);
            Check(!view.gameObject.activeInHierarchy && !prompt.activeInHierarchy && !run.IsShopOpen,
                "Disabling the service did not hide its view and close its session.");
            shop.enabled = true;
            yield return Frames(2);
            Check(shop.IsInRange && view.gameObject.activeInHierarchy && prompt.activeInHierarchy && !panel.activeInHierarchy,
                "Re-enabling the service in the same interaction radius did not refresh the prompt.");
            yield return KeyPress(keyboard, Key.F);
            Check(run.IsShopOpen && panel.activeInHierarchy, "Virtual F did not work after re-enabling the service.");
            shop.enabled = false;
            yield return Frames(2);
            Check(!run.IsShopOpen && !player.IsShopPaused && Mathf.Approximately(Time.timeScale, 1f),
                "Disabling an open shop left its gameplay pause active.");
            shop.enabled = true;
            yield return Frames(2);
            Check(prompt.activeInHierarchy && !panel.activeInHierarchy && shop.IsInRange,
                "Re-enabling after an open-session shutdown did not restore the nearby prompt.");
        }
        finally
        {
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            try { restore?.Apply(); }
            finally
            {
                inputSettings.editorInputBehaviorInPlayMode = previousEditorInput;
                inputSettings.backgroundBehavior = previousBackgroundInput;
            }
        }
    }

    private static IEnumerator KeyPress(Keyboard keyboard, Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return Frames(2);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return Frames(2);
    }

    private static IEnumerator Click(Mouse mouse, Button button, EventSystem events)
    {
        Canvas.ForceUpdateCanvases();
        Vector2 center = ScreenCenter(button.transform as RectTransform);
        Check(center.x >= 0f && center.y >= 0f && center.x <= Screen.width && center.y <= Screen.height,
            "The selected button's center lies outside the actual game screen.");
        var hits = Raycast(events, center);
        Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button,
            "The top GraphicRaycaster hit does not belong to the selected purchase button.");
        InputSystem.QueueStateEvent(mouse, new MouseState { position = center });
        yield return Frames(2);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = center, buttons = 1 });
        yield return Frames(2);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = center });
        yield return Frames(2);
    }

    private static IEnumerator Wheel(Mouse mouse, Vector2 position, float delta)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, scroll = new Vector2(0f, delta) });
        yield return Frames(2);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
        yield return Frames(3);
        Canvas.ForceUpdateCanvases();
    }

    private static Vector2 ScreenCenter(RectTransform rect)
    {
        if (rect == null) throw new InvalidOperationException("The pointer target has no RectTransform.");
        return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
    }

    private static List<RaycastResult> Raycast(EventSystem events, Vector2 point)
    {
        var hits = new List<RaycastResult>();
        events.RaycastAll(new PointerEventData(events) { position = point }, hits);
        return hits;
    }

    private static IEnumerator TeleportNear(StageShopService shop, PlayerController player)
    {
        var point = shop.ShopPoint;
        Vector3 floor = point.position + point.forward * Mathf.Min(1.75f, shop.settings.interactionDistance * 0.65f);
        player.BeginStageTransition();
        yield return Frames(3);
        float halfHeight = player.Controller.height * Mathf.Abs(player.transform.lossyScale.y) * 0.5f;
        Vector3 center = floor + Vector3.up * (halfHeight + player.Controller.skinWidth + 0.05f);
        player.CompleteStageTransition(center - point.rotation * Vector3.Scale(player.Controller.center, player.transform.lossyScale),
            point.rotation);
    }

    private static IEnumerator Frames(int count)
    {
        int target = Time.frameCount + count;
        yield return Wait(() => Time.frameCount >= target, "The actual player loop stopped advancing.");
    }

    private static IEnumerator Wait(Func<bool> predicate, string error, float seconds = 20f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!predicate())
        {
            if (!Application.isPlaying || Time.realtimeSinceStartup > deadline) throw new InvalidOperationException(error);
            yield return null;
        }
    }

    private static T Field<T>(object owner, string name)
        => (T)owner.GetType().GetField(name, Hidden).GetValue(owner);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private sealed class Restore
    {
        private RunManager _run;
        private StageShopService _shop;
        private PlayerController _player;
        private int _gold;
        private float _hp, _timeScale;
        private string[] _owned;
        private ShopStock _stock;
        private bool[] _sold;
        private bool _serviceEnabled;

        public static Restore Capture(RunManager run, StageShopService shop) => new Restore
        {
            _run = run, _shop = shop, _player = run.player, _gold = shop.Wallet.Gold,
            _hp = run.player.StatManager.CurrentHp, _timeScale = Time.timeScale, _serviceEnabled = shop.enabled,
            _owned = run.player.ItemRuntime.OwnedItems.Select(item => item.itemId).ToArray(),
            _stock = shop.CurrentStock, _sold = shop.CurrentStock.Offers.Select(offer => offer.SoldOut).ToArray()
        };

        public void Apply()
        {
            if (_run == null || _shop == null || _player == null) return;
            _run.CloseShop();
            _shop.enabled = _serviceEnabled;
            var items = _player.ItemRuntime;
            foreach (var item in items.OwnedItems.ToArray()) if (!_owned.Contains(item.itemId)) items.TryRemoveItem(item.itemId);
            foreach (string id in _owned) if (!items.Contains(id)) items.TryAddItem(id);
            int current = _shop.Wallet.Gold;
            if (current > _gold) _shop.Wallet.TrySpend(current - _gold);
            else if (current < _gold) _shop.Wallet.AddGold(_gold - current);
            if (_player.StatManager.CurrentHp < _hp) _player.StatManager.Heal(_hp - _player.StatManager.CurrentHp);
            else if (_player.StatManager.CurrentHp > _hp) _player.StatManager.ConsumeHealth(_player.StatManager.CurrentHp - _hp);
            for (int i = 0; i < _stock.Offers.Count; i++)
                typeof(ShopOffer).GetField("<SoldOut>k__BackingField", Hidden).SetValue(_stock.Offers[i], _sold[i]);
            _player.Input.AttackBuffer.Consume(); _player.Input.DashBuffer.Consume(); _player.Input.JumpBuffer.Consume();
            _player.Input.SkillBuffer.Consume(); _player.Input.UltimateBuffer.Consume(); _player.Input.ConsumeFormInput();
            CombatHitStopService.SetExternalWorldTimeScale(_timeScale);
            if (_run.State == RunState.Playing && _run.stageManager.IsReady)
            {
                _player.BeginStageTransition();
                _player.CompleteStageTransition(_run.stageManager.PlayerSpawnPosition, _run.stageManager.PlayerSpawnRotation);
            }
        }
    }
}
