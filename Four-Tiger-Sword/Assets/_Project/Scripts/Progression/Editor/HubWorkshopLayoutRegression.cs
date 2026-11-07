using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks the rendered workshop geometry at the current Game view size with isolated accounts.</summary>
public static class HubWorkshopLayoutRegression
{
    private const string ResultKey = "HubWorkshop.LayoutRegression";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const float Epsilon = .6f;
    private static bool _running;
    private static int _checks;
    private static string _phase;
    private static DateTime _deadline;
    private static TaskCompletionSource<bool> _nextTick;

    public static string Start()
    {
        RequireOpenView();
        if (_running) throw new InvalidOperationException("Workshop layout verification is already running.");
        _checks = 0;
        _running = true;
        _deadline = DateTime.UtcNow.AddSeconds(20);
        EditorApplication.update += Tick;
        _ = Run();
        return "Workshop layout scenarios started at " + Screen.width + "x" + Screen.height
            + ". Poll HubWorkshopLayoutRegression.Status().";
    }

    public static string Status() => SessionState.GetString(ResultKey, "NOT STARTED");

    /// <summary>Checks the currently displayed state without rebinding its account or changing selection.</summary>
    public static string VerifyCurrent()
    {
        if (_running) throw new InvalidOperationException("Wait for the running layout verification first.");
        _checks = 0;
        _phase = "current displayed state";
        VerifyGeometry(RequireOpenView());
        return "PASS: " + _checks + " geometry/pointer checks at " + Screen.width + "x" + Screen.height + ".";
    }

    private static HubWorkshopView RequireOpenView()
    {
        if (!Application.isPlaying || SceneManager.GetActiveScene().name != "Hub Scene")
            throw new InvalidOperationException("Run in the Hub Scene during Play Mode.");
        var view = Object.FindFirstObjectByType<HubWorkshopView>();
        if (view == null || !view.IsOpen) throw new InvalidOperationException("Open the workshop before verifying its layout.");
        return view;
    }

    private static void Tick()
    {
        var pending = _nextTick;
        _nextTick = null;
        pending?.TrySetResult(true);
    }

    private static Task NextTick()
    {
        _nextTick = new TaskCompletionSource<bool>();
        return _nextTick.Task;
    }

    private static async Task Run()
    {
        HubWorkshopView view = null;
        PlayerUpgradeService previousService = null;
        StatType previousSelection = default;
        float previousScroll = 1f;
        PlayerUpgradeSettingsSO maximumSettings = null;
        bool captured = false;
        string result = null;
        try
        {
            view = RequireOpenView();
            previousService = (PlayerUpgradeService)typeof(HubWorkshopView).GetField("_service", Hidden).GetValue(view);
            previousSelection = view.SelectedStat;
            previousScroll = view.UI.statusScroll != null ? view.UI.statusScroll.verticalNormalizedPosition : 1f;
            captured = true;
            var settings = previousService?.Settings ?? PlayerUpgradeService.Instance.Settings;
            Check(settings != null && settings.Validate(out _), "Installed upgrade settings are invalid.");
            var stats = new[] { StatType.ST_HP, StatType.ST_ATK, StatType.ST_DEF };

            Phase("all three normal categories");
            var funded = PlayerUpgradeProfile.New();
            funded.currency = (int)Math.Min(int.MaxValue, (long)settings.initialCost * 3);
            var purchaseStore = new MemoryStore(funded);
            var fundedService = new PlayerUpgradeService(settings, purchaseStore);
            view.Bind(fundedService);
            foreach (var stat in stats)
            {
                Phase("normal " + stat);
                view.Select(stat);
                await Frames(2);
                VerifyGeometry(view);
                Check(view.UI.purchaseButton.IsInteractable(), "A funded normal category cannot be purchased.");
            }

            Phase("successful purchase feedback");
            view.Select(StatType.ST_HP);
            int expectedCost = fundedService.GetNextCost(StatType.ST_HP);
            view.UI.purchaseButton.onClick.Invoke();
            await Frames(2);
            Check(fundedService.GetLevel(StatType.ST_HP) == 1 && purchaseStore.Writes == 1
                && fundedService.Balance == funded.currency - expectedCost, "The isolated purchase did not commit once.");
            VerifyGeometry(view);

            var capped = PlayerUpgradeProfile.New();
            capped.currency = int.MaxValue;
            capped.healthLevel = capped.attackLevel = capped.defenseLevel = settings.maxLevel;
            view.Bind(new PlayerUpgradeService(settings, new MemoryStore(capped)));
            foreach (var stat in stats)
            {
                Phase("maximum level " + stat);
                view.Select(stat);
                await Frames(2);
                VerifyGeometry(view);
                Check(!view.UI.purchaseButton.IsInteractable(), "A capped category permits another purchase.");
            }

            view.Bind(new PlayerUpgradeService(settings, new MemoryStore(PlayerUpgradeProfile.New())));
            foreach (var stat in stats)
            {
                Phase("insufficient currency " + stat);
                view.Select(stat);
                await Frames(2);
                VerifyGeometry(view);
                Check(!view.UI.purchaseButton.IsInteractable(), "An empty account permits a purchase.");
            }

            Phase("largest supported currency and cost labels");
            maximumSettings = Object.Instantiate(settings);
            maximumSettings.hideFlags = HideFlags.DontSave;
            maximumSettings.initialCost = int.MaxValue;
            maximumSettings.costPerLevel = 0;
            Check(maximumSettings.Validate(out _), "The maximum-cost layout fixture is not a valid configuration.");
            var maximumBalance = PlayerUpgradeProfile.New();
            maximumBalance.currency = int.MaxValue;
            view.Bind(new PlayerUpgradeService(maximumSettings, new MemoryStore(maximumBalance)));
            view.Select(StatType.ST_HP);
            await Frames(2);
            VerifyGeometry(view);
            Check(view.UI.balance.text.Contains(int.MaxValue.ToString("N0"))
                && view.UI.cost.text.Contains(int.MaxValue.ToString("N0")), "Maximum integer amounts were not displayed in full.");

            Phase("long wrapped save error and masked scrolling");
            var failingProfile = PlayerUpgradeProfile.New();
            failingProfile.currency = int.MaxValue;
            var failingStore = new MemoryStore(failingProfile)
            {
                Error = string.Concat(Enumerable.Repeat("저장에 실패했습니다. 저장 위치의 권한과 남은 공간을 확인한 뒤 다시 시도해 주세요. 기존 강화 단계와 보유 재화는 안전하게 유지됩니다. ", 8))
            };
            var failing = new PlayerUpgradeService(settings, failingStore);
            view.Bind(failing);
            view.Select(StatType.ST_HP);
            view.UI.purchaseButton.onClick.Invoke();
            await Frames(2);
            VerifyGeometry(view);
            var scroll = view.UI.statusScroll;
            Check(view.UI.status.textInfo.lineCount >= 3 && !view.UI.status.text.Contains("\n"),
                "The long save error did not wrap into at least three lines.");
            Check(failing.GetLevel(StatType.ST_HP) == 0 && failing.Balance == int.MaxValue && failingStore.Writes == 0,
                "The failed-write fixture changed the isolated account.");
            Check(scroll.content.rect.height > scroll.viewport.rect.height + 1f,
                "The long error did not expand the scroll content beyond its viewport.");
            Check(scroll.verticalNormalizedPosition > .99f, "A new long error did not reset to its first line.");
            Vector2 contentBefore = scroll.content.anchoredPosition;
            var pointer = PointerAt(scroll.viewport);
            pointer.scrollDelta = new Vector2(0f, -3f);
            var hits = Hits(pointer);
            Check(hits.Count > 0 && (hits[0].gameObject.transform == scroll.viewport
                || hits[0].gameObject.transform.IsChildOf(scroll.viewport)), "The status viewport cannot receive pointer scrolling.");
            var handled = ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.scrollHandler);
            Check(handled != null, "The status pointer event did not reach a ScrollRect handler.");
            await Frames(2);
            Check(scroll.verticalNormalizedPosition < .99f
                && Vector2.Distance(contentBefore, scroll.content.anchoredPosition) > .5f,
                "A real scroll event did not move the status content.");
            scroll.verticalNormalizedPosition = 0f;
            await Frames(2);
            Check(scroll.verticalNormalizedPosition < .01f, "The complete save error cannot be scrolled to its final line.");
            VerifyGeometry(view);
            result = "PASS: " + _checks + " checks at " + Screen.width + "x" + Screen.height
                + "; all categories, cap/empty/max-int/success/error states, glyph bounds, 64-unit safe inset, independent content and real pointer/scroll targets; memory stores only.";
        }
        catch (Exception failure) { result = "FAIL during " + _phase + ": " + failure; }
        finally
        {
            if (captured && view != null)
            {
                try
                {
                    view.Bind(previousService);
                    view.Select(previousSelection);
                    // Consume the normal pending reset before restoring the viewer's prior scroll position.
                    typeof(HubWorkshopView).GetMethod("LateUpdate", Hidden)?.Invoke(view, null);
                    if (view.UI.statusScroll != null) view.UI.statusScroll.verticalNormalizedPosition = previousScroll;
                }
                catch (Exception cleanup) { result += " Cleanup: " + cleanup.Message; }
            }
            if (maximumSettings != null) Object.DestroyImmediate(maximumSettings);
            SessionState.SetString(ResultKey, result ?? "FAIL: layout verification ended without a result.");
            EditorApplication.update -= Tick;
            _nextTick = null;
            _running = false;
        }
    }

    private static void VerifyGeometry(HubWorkshopView view)
    {
        Check(view.IsOpen, "The workshop closed during geometry verification.");
        var fit = view.UI.panel.GetComponentInChildren<BoundaryZeroWindowFit>(true);
        Check(fit != null, "The modal window has no responsive fit component.");
        fit.Refresh();
        var window = (RectTransform)fit.transform;
        Canvas.ForceUpdateCanvases();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(window);
        Canvas.ForceUpdateCanvases();
        var safe = window.rect;
        safe.xMin += 64f;
        safe.xMax -= 64f;
        safe.yMin += 64f;
        safe.yMax -= 64f;
        var scroll = view.UI.statusScroll;
        Check(scroll != null && scroll.viewport != null && scroll.content != null && scroll.vertical && !scroll.horizontal,
            "The status area does not have a vertical ScrollRect with viewport and content.");
        Check(scroll.viewport.GetComponent<UnityEngine.UI.Mask>()?.isActiveAndEnabled == true
            || scroll.viewport.GetComponent<UnityEngine.UI.RectMask2D>()?.isActiveAndEnabled == true,
            "The overflowing status text has no active viewport mask.");
        Check(view.UI.status.rectTransform.IsChildOf(scroll.content)
            && view.UI.status is TextMeshProUGUI statusGraphic && statusGraphic.maskable,
            "The status text is outside the masked scroll content or ignores its mask.");

        var elements = new List<Element>();
        foreach (var label in window.GetComponentsInChildren<TMP_Text>())
        {
            if (!label.isActiveAndEnabled) continue;
            label.ForceMeshUpdate();
            Check(label.textInfo.characterInfo.Take(label.textInfo.characterCount).Any(character => character.isVisible),
                "Label has no visible glyphs: " + Path(label.transform, window));
            var glyphs = GlyphBounds(label);
            Check(Contains(label.rectTransform.rect, glyphs), "Glyphs exceed their TMP rect: "
                + Path(label.transform, window) + "; rect=" + label.rectTransform.rect + "; glyphs=" + glyphs);
            if (label.rectTransform.IsChildOf(scroll.content))
            {
                var viewportGlyphs = ConvertRect(glyphs, label.rectTransform, scroll.viewport);
                Check(viewportGlyphs.xMin >= scroll.viewport.rect.xMin - Epsilon
                    && viewportGlyphs.xMax <= scroll.viewport.rect.xMax + Epsilon,
                    "Scrollable status glyphs overflow the viewport horizontally: " + Path(label.transform, window)
                        + "; glyphs=" + viewportGlyphs + "; viewport=" + scroll.viewport.rect);
                continue;
            }
            Check(!label.isTextTruncated, "Label is truncated: " + Path(label.transform, window));
            Check(label.textInfo.lineCount > 1 || label.rectTransform.rect.height + Epsilon >= label.fontSize * 1.5f,
                "Single-line TMP rect is shorter than 1.5 times its font size: " + Path(label.transform, window)
                    + "; height=" + label.rectTransform.rect.height + "; font=" + label.fontSize);
            var assigned = InWindow(label.rectTransform, window);
            Check(Contains(safe, assigned), "Label rect leaves the 64-unit window inset: "
                + Path(label.transform, window) + "; rect=" + assigned + "; safe=" + safe);
            elements.Add(new Element(label.transform, assigned));
        }
        foreach (var button in window.GetComponentsInChildren<UnityEngine.UI.Button>())
        {
            if (!button.isActiveAndEnabled) continue;
            var rect = InWindow((RectTransform)button.transform, window);
            Check(Contains(safe, rect), "Button leaves the 64-unit window inset: " + Path(button.transform, window) + "; rect=" + rect);
            elements.Add(new Element(button.transform, rect));
            CheckButtonPointer(button);
        }
        var viewportRect = InWindow(scroll.viewport, window);
        Check(Contains(safe, viewportRect), "The status viewport leaves the 64-unit window inset.");
        elements.Add(new Element(scroll.viewport, viewportRect));
        for (int a = 0; a < elements.Count; a++)
            for (int b = a + 1; b < elements.Count; b++)
            {
                var left = elements[a];
                var right = elements[b];
                if (left.Transform.IsChildOf(right.Transform) || right.Transform.IsChildOf(left.Transform)) continue;
                float overlapX = Mathf.Min(left.Bounds.xMax, right.Bounds.xMax) - Mathf.Max(left.Bounds.xMin, right.Bounds.xMin);
                float overlapY = Mathf.Min(left.Bounds.yMax, right.Bounds.yMax) - Mathf.Max(left.Bounds.yMin, right.Bounds.yMin);
                Check(overlapX <= Epsilon || overlapY <= Epsilon, "Independent content overlaps: "
                    + Path(left.Transform, window) + " " + left.Bounds + " and " + Path(right.Transform, window) + " " + right.Bounds);
            }
    }

    private readonly struct Element
    {
        public readonly Transform Transform;
        public readonly Rect Bounds;
        public Element(Transform transform, Rect bounds) { Transform = transform; Bounds = bounds; }
    }

    private static Rect GlyphBounds(TMP_Text label)
    {
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < label.textInfo.characterCount; i++)
        {
            var character = label.textInfo.characterInfo[i];
            if (!character.isVisible) continue;
            min = Vector2.Min(min, character.bottomLeft);
            min = Vector2.Min(min, character.topLeft);
            max = Vector2.Max(max, character.bottomRight);
            max = Vector2.Max(max, character.topRight);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static Rect InWindow(RectTransform rect, RectTransform window) => ConvertRect(rect.rect, rect, window);
    private static Rect ConvertRect(Rect rect, Transform source, Transform destination)
    {
        var corners = new[] { new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMin, rect.yMax),
            new Vector3(rect.xMax, rect.yMin), new Vector3(rect.xMax, rect.yMax) };
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in corners)
        {
            Vector2 point = destination.InverseTransformPoint(source.TransformPoint(corner));
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - Epsilon
        && inner.xMax <= outer.xMax + Epsilon && inner.yMin >= outer.yMin - Epsilon && inner.yMax <= outer.yMax + Epsilon;
    private static string Path(Transform transform, Transform root)
        => transform == root ? root.name : transform.parent == null ? transform.name : Path(transform.parent, root) + "/" + transform.name;
    private static PointerEventData PointerAt(RectTransform rect)
    {
        Check(EventSystem.current != null, "The workshop has no EventSystem for pointer verification.");
        return new PointerEventData(EventSystem.current)
        { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
    }
    private static List<RaycastResult> Hits(PointerEventData pointer)
    {
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        return hits;
    }
    private static void CheckButtonPointer(UnityEngine.UI.Button button)
    {
        // Disabled purchase controls must still occupy their reserved geometry; pointer gating is tested when enabled.
        if (!button.IsInteractable()) return;
        var hits = Hits(PointerAt((RectTransform)button.transform));
        Check(hits.Count > 0 && (hits[0].gameObject == button.gameObject
            || hits[0].gameObject.transform.IsChildOf(button.transform)), "Button center is blocked: " + button.name
            + "; first hit=" + (hits.Count > 0 ? hits[0].gameObject.name : "none"));
    }
    private static void Phase(string phase) { _phase = phase; SessionState.SetString(ResultKey, "RUNNING: " + phase); }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(_phase + ": " + message);
        _checks++;
    }
    private static async Task Frames(int count)
    {
        int until = Time.frameCount + count;
        while (Time.frameCount < until)
        {
            if (!Application.isPlaying || DateTime.UtcNow >= _deadline) throw new InvalidOperationException("Game frames stopped during " + _phase);
            await NextTick();
        }
    }
    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        private PlayerUpgradeProfile _profile;
        public string Error;
        public int Writes;
        public MemoryStore(PlayerUpgradeProfile profile) => _profile = profile.Copy();
        public bool TryLoad(out PlayerUpgradeProfile profile, out string error)
        { profile = _profile.Copy(); error = null; return true; }
        public bool TrySave(PlayerUpgradeProfile profile, out string error)
        {
            error = Error;
            if (error != null) return false;
            _profile = profile.Copy();
            Writes++;
            return true;
        }
    }
}
