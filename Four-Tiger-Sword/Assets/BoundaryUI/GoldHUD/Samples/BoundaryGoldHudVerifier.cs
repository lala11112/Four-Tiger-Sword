using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using BoundaryZeroUI;
using UnityEngine;
using UnityEngine.UI;

namespace ArpgHud.PC
{
    /// <summary>Opt-in player verification; never runs in an ordinary game session.</summary>
    public sealed class BoundaryGoldHudVerifier : MonoBehaviour
    {
        [Serializable] sealed class Report { public bool success; public string unityVersion; public string[] passed, errors, screenshots; }
        readonly List<string> passed = new List<string>(), errors = new List<string>(), screenshots = new List<string>();
        ReferenceHud hud;
        BoundaryUIController controller;
        PentaGoldBalanceView balance;
        PentaGoldRewardFeed feed;
        PentaGoldRewardRow[] rows;
        string output;
        bool finished;
        float originalTimeScale;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--boundary-gold-verify") >= 0)
                new GameObject("Gold HUD Verification").AddComponent<BoundaryGoldHudVerifier>();
        }

        void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--boundary-gold-output");
            output = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "GoldHUDVerification");
            Directory.CreateDirectory(output);
            originalTimeScale = Time.timeScale;
            Application.logMessageReceived += Log;
            StartCoroutine(Watchdog());
            StartCoroutine(Verify());
        }

        void Log(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert ||
                (type == LogType.Warning && message.IndexOf("font", StringComparison.OrdinalIgnoreCase) >= 0)) errors.Add(message);
        }
        void Check(bool condition, string label) { (condition ? passed : errors).Add(label); }
        IEnumerator Watchdog() { yield return new WaitForSecondsRealtime(60); errors.Add("Verification timeout"); Finish(); }
        void ResetFeed(float merge = .4f, float hold = 2f, float fade = .3f) { feed.Configure(rows, 10f, merge, hold, fade); }

        IEnumerator Verify()
        {
            yield return new WaitForSecondsRealtime(.3f);
            controller = FindFirstObjectByType<BoundaryUIController>();
            if (controller) { controller.ShowScreen("hud"); controller.SetHudData("1층 · 3 / 5", "", .76f, "구역 탐색", new[] { "E", "R" }); }
            hud = FindFirstObjectByType<ReferenceHud>();
            balance = FindFirstObjectByType<PentaGoldBalanceView>();
            feed = FindFirstObjectByType<PentaGoldRewardFeed>();
            if (!hud || !balance || !feed) { errors.Add("Gold HUD components missing"); Finish(); yield break; }
            foreach (var component in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (component.GetType().Name == "BoundaryGoldHudDemo") component.enabled = false;
            rows = feed.GetComponentsInChildren<PentaGoldRewardRow>(true);
            if (rows.Length < 3) { errors.Add("Three configured reward rows required"); Finish(); yield break; }
            Check(balance.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget) && feed.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), "Gold graphics do not intercept input");
            Check(feed.GetComponentsInChildren<CanvasGroup>(true).All(g => !g.blocksRaycasts && !g.interactable), "Reward groups do not intercept input");
            Check(ReferenceHud.FormScale == 1.5f && ReferenceHud.MapScale == 1.65f, "Accepted form and minimap scale retained");
            Check(hud.AbilityImages.Length == 3 && !hud.AbilityImages[0] && hud.AbilityImages[1] && hud.AbilityImages[2], "Only original E/R ability indices exist");
            Check(hud.GetComponentsInChildren<RectTransform>(true).Count(r => r.name.StartsWith("ElementDiamond")) == 5 && !hud.GetComponentsInChildren<RectTransform>(true).Any(r => r.name == "SkillMount0"), "Five original forms and no Q mount");
            Check(hud.GetComponentsInChildren<RectTransform>(true).Any(r => r.name == "Seal") && hud.GetComponentsInChildren<RectTransform>(true).Any(r => r.name == "LegacyBladeFrame"), "White crest frame and red seal retained");

            yield return VerifyMotion();

            ResetFeed();
            balance.SetGold(1250);
            Check(balance.DisplayedGold == 1250 && feed.ActiveRowCount == 0, "Balance update creates no reward entry");
            Check(balance.GetComponentInChildren<TMP_Text>(true).text == "골드 1,250", "Balance renders exact comma formatting");
            balance.SetGold(0);
            Check(balance.GetComponentInChildren<TMP_Text>(true).text == "골드 0", "Zero balance renders correctly");
            balance.SetGold(1250); balance.SetGold(1220);
            Check(balance.DisplayedGold == 1220 && feed.ActiveRowCount == 0 && balance.GetComponentInChildren<TMP_Text>(true).text == "골드 1,220", "Spending updates balance without a gain log");
            balance.SetGold(-1);
            Check(balance.DisplayedGold == 0, "Negative balance clamps to zero");
            feed.ShowGoldGain(0); feed.ShowGoldGain(-10);
            Check(feed.ActiveRowCount == 0, "Nonpositive rewards ignored");
            feed.ShowGoldGain(20); feed.ShowGoldGain(10);
            Check(feed.ActiveRowCount == 1 && feed.GetRow(0).Amount == 30, "Immediate gains merge");
            yield return new WaitForSecondsRealtime(.25f);
            feed.ShowGoldGain(10);
            Check(feed.ActiveRowCount == 1 && feed.GetRow(0).Amount == 40, "Gain within first window merges");
            yield return new WaitForSecondsRealtime(.25f);
            feed.ShowGoldGain(5);
            Check(feed.ActiveRowCount == 2 && feed.GetRow(0).Amount == 40 && feed.GetRow(1).Amount == 5, "Merge deadline stays fixed to first gain");

            ResetFeed(0);
            foreach (int amount in new[] { 11, 22, 33, 44 }) feed.ShowGoldGain(amount);
            Check(feed.ActiveRowCount == 3 && feed.GetRow(0).Amount == 22 && feed.GetRow(2).Amount == 44, "Fourth entry evicts oldest and preserves ordering");
            ResetFeed(.4f, .35f, .3f);
            feed.ShowGoldGain(20);
            yield return new WaitForSecondsRealtime(.2f);
            feed.ShowGoldGain(10);
            yield return new WaitForSecondsRealtime(.2f);
            Check(feed.ActiveRowCount == 1 && feed.GetRow(0).Alpha > .99f, "Merged gain restarts hold time");
            yield return new WaitForSecondsRealtime(.25f);
            Check(feed.ActiveRowCount == 1 && feed.GetRow(0).Alpha > 0 && feed.GetRow(0).Alpha < 1, "Entry fades after hold");
            yield return new WaitForSecondsRealtime(.25f);
            Check(feed.ActiveRowCount == 0 && rows.All(r => !r.Visible), "Expired entry disappears");
            ResetFeed(.4f, .1f, .1f);
            Time.timeScale = 0;
            feed.ShowGoldGain(20);
            yield return new WaitForSecondsRealtime(.3f);
            Check(feed.ActiveRowCount == 0, "Rewards expire with timeScale zero");
            Time.timeScale = originalTimeScale;
            ResetFeed(); feed.ShowGoldGain(20); feed.enabled = false; feed.ShowGoldGain(30);
            Check(feed.ActiveRowCount == 0 && rows.All(r => !r.Visible), "Disabling component clears and ignores gains");
            feed.enabled = true;
            Check(feed.ActiveRowCount == 0, "Re-enabled component stays empty");
            feed.ShowGoldGain(20); feed.gameObject.SetActive(false); feed.ShowGoldGain(30);
            Check(feed.ActiveRowCount == 0, "Inactive object clears and ignores gains");
            feed.gameObject.SetActive(true);
            Check(feed.ActiveRowCount == 0, "Reactivated object stays empty");
            balance.SetGold(1234);
            controller.Refresh(); yield return null;
            Check(balance.DisplayedGold == 1234 && balance.GetComponentInChildren<TMP_Text>(true).text == "골드 1,234", "Controller refresh preserves directly supplied balance");
            feed.ShowGoldGain(20);
            controller.ShowScreen("main"); yield return null;
            Check(feed.ActiveRowCount == 0, "Disabling entire HUD clears reward entries");
            controller.ShowScreen("hud"); yield return null;
            Check(balance.DisplayedGold == 1234 && balance.GetComponentInChildren<TMP_Text>(true).text == "골드 1,234", "Whole HUD re-enable preserves directly supplied balance");
            Check(feed.ActiveRowCount == 0 && rows.All(r => !r.Visible), "Whole HUD re-enable never replays cleared entries");
            ResetFeed(); feed.ShowGoldGain(int.MaxValue); feed.ShowGoldGain(int.MaxValue);
            Check(feed.ActiveRowCount == 1 && feed.GetRow(0).Amount == 4294967294L, "Merged reward exceeds int maximum without overflow");

            controller.SetHudData("1층 · 3 / 5", "", .76f, "구역 탐색", new[] { "E", "R" });
            yield return Capture("01-gold-2560x1440", 2560, 1440, false);
            yield return Capture("02-gold-1920x1080", 1920, 1080, false);
            yield return Capture("03-gold-1280x720", 1280, 720, false);
            yield return Capture("04-gold-2560x1080", 2560, 1080, false);
            yield return Capture("05-gold-large-values", 2560, 1440, true);
            Finish();
        }

        IEnumerator VerifyMotion()
        {
            ResetFeed(0, 2f, .3f);
            Time.timeScale = 0;
            feed.ShowGoldGain(11);
            var first = feed.GetRow(0);
            float entranceStartY = first.Rect.anchoredPosition.y;
            float entranceStartAlpha = first.Alpha;
            yield return new WaitForSecondsRealtime(.08f);
            float entranceMiddleY = first.Rect.anchoredPosition.y;
            float entranceMiddleAlpha = first.Alpha;
            yield return new WaitForSecondsRealtime(.25f);
            float settledY = first.Rect.anchoredPosition.y;
            Check(entranceStartY < entranceMiddleY && entranceMiddleY < settledY &&
                entranceStartAlpha < entranceMiddleAlpha && entranceMiddleAlpha < .99f && first.Alpha > .99f,
                "Entrance fades and rises across frames while timeScale is zero");
            Time.timeScale = originalTimeScale;

            feed.ShowGoldGain(22);
            float slideStartY = first.Rect.anchoredPosition.y;
            Check(Mathf.Abs(slideStartY - settledY) < 1f, "Existing row does not snap upward on a new reward");
            yield return ObserveRowSeparation(.1f, "Normal insertion keeps visible rows separated during motion");
            float slideMiddleY = first.Rect.anchoredPosition.y;
            yield return ObserveRowSeparation(.25f, "Normal insertion stays separated through settling");
            Check(slideMiddleY > slideStartY && first.Rect.anchoredPosition.y > slideMiddleY &&
                feed.ActiveRowCount == 2 && first.Amount == 11 && feed.GetRow(1).Amount == 22,
                "Existing row slides upward across frames without changing reward semantics");

            feed.ShowGoldGain(33);
            yield return new WaitForSecondsRealtime(.35f);
            feed.ShowGoldGain(44);
            var reused = feed.GetRow(2);
            Check(reused == first && reused.Amount == 44 && reused.Alpha <= .01f &&
                reused.Rect.anchoredPosition.y < feed.GetRow(1).Rect.anchoredPosition.y,
                "Fourth reward reuses oldest row with fresh entrance position and alpha");
            yield return new WaitForSecondsRealtime(.35f);
            Check(reused.Alpha > .99f && Mathf.Abs(reused.Rect.anchoredPosition.y - settledY) < 1f,
                "Reused row reaches its own settled visual state");

            ResetFeed(0, 2f, .3f);
            feed.ShowGoldGain(11); feed.ShowGoldGain(22); feed.ShowGoldGain(33);
            yield return ObserveRowSeparation(.4f, "Same-frame rewards keep visible rows separated throughout entrance");

            ResetFeed(0, .4f, .3f);
            feed.ShowGoldGain(55);
            var exiting = feed.GetRow(0);
            yield return new WaitForSecondsRealtime(.34f);
            float exitStartY = exiting.Rect.anchoredPosition.y;
            yield return new WaitForSecondsRealtime(.18f);
            float exitMiddleY = exiting.Rect.anchoredPosition.y;
            float exitMiddleAlpha = exiting.Alpha;
            yield return new WaitForSecondsRealtime(.08f);
            Check(exitMiddleY > exitStartY && exiting.Rect.anchoredPosition.y > exitMiddleY &&
                exitMiddleAlpha > 0f && exitMiddleAlpha < 1f && exiting.Alpha < exitMiddleAlpha,
                "Exit rises upward while fading across frames");
            yield return new WaitForSecondsRealtime(.2f);
            Check(feed.ActiveRowCount == 0 && !exiting.Visible, "Animated exit releases the row");
            ResetFeed();
        }

        IEnumerator ObserveRowSeparation(float duration, string label)
        {
            bool separated = true;
            double until = Time.unscaledTimeAsDouble + duration;
            var firstCorners = new Vector3[4];
            var secondCorners = new Vector3[4];
            do
            {
                for (int i = 0; i < feed.ActiveRowCount; i++)
                    for (int j = i + 1; j < feed.ActiveRowCount; j++)
                    {
                        var older = feed.GetRow(i); var newer = feed.GetRow(j);
                        if (older.Alpha <= .01f || newer.Alpha <= .01f) continue;
                        older.Rect.GetWorldCorners(firstCorners);
                        newer.Rect.GetWorldCorners(secondCorners);
                        if (firstCorners[0].y < secondCorners[1].y - .01f) separated = false;
                    }
                yield return null;
            } while (Time.unscaledTimeAsDouble < until);
            Check(separated, label);
        }

        Rect Bounds(RectTransform rect, Camera camera)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = min;
            foreach (var corner in corners) { Vector2 p = RectTransformUtility.WorldToScreenPoint(camera, corner); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        void CheckLayout(Camera camera, int width, int height, string prefix)
        {
            var nodes = hud.GetComponentsInChildren<RectTransform>(true);
            var goldRect = balance.transform as RectTransform;
            var health = nodes.FirstOrDefault(r => r.name == "HealthFill");
            var map = hud.MinimapContent;
            var skills = nodes.FirstOrDefault(r => r.name == "Skills");
            var forms = nodes.FirstOrDefault(r => r.name == "FiveForms");
            foreach (var original in new[] { forms, skills }) {
                var rect = original ? Bounds(original, camera) : new Rect();
                Check(original && rect.xMin >= -1 && rect.xMax <= width + 1 && rect.yMin >= -1 && rect.yMax <= height + 1, prefix + " original bottom group fits: " + (original ? original.name : "missing"));
            }
            var shortcuts = hud.transform.Find("Shortcuts");
            float goldFormGap = goldRect && forms ? Bounds(goldRect, camera).yMin - Bounds(forms, camera).yMax : -1f;
            Check(goldRect && forms && goldFormGap > 0 && goldFormGap <= Bounds((RectTransform)hud.transform, camera).height * 32f / 1440f, prefix + " gold above five forms with a small positive gap");
            if (goldRect) {
                var gold = Bounds(goldRect, camera);
                Check(gold.xMin >= 0 && gold.xMax <= width && gold.yMin >= 0 && gold.yMax <= height, prefix + " balance in screen bounds");
                // Crest is confined to the left of x150, ending at y244 in the accepted QHD frame.
                var frameBounds = Bounds((RectTransform)hud.transform, camera);
                var crest = new Rect(frameBounds.xMin, frameBounds.yMax - frameBounds.height * 244f / 1440f, frameBounds.width * 150f / 2560f, frameBounds.height * 244f / 1440f);
                Check(!gold.Overlaps(crest), prefix + " balance avoids original crest and seal");
                Check(!hud.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text == "데이터" || t.text == "1층 · 3 / 5"), prefix + " removed data label and value stay absent");
            }
            var visible = Enumerable.Range(0, feed.ActiveRowCount).Select(i => Bounds(feed.GetRow(i).Rect, camera)).ToArray();
            foreach (var rect in visible) {
                Check(rect.xMin >= 0 && rect.xMax <= width + 1 && rect.yMin >= 0 && rect.yMax <= height + 1 && rect.center.x > width * .65f && rect.center.y > height * .2f && rect.center.y < height * .8f, prefix + " reward in right-middle screen bounds");
                Check(map && rect.yMax < Bounds(map, camera).yMin && skills && rect.yMin > Bounds(skills, camera).yMax, prefix + " reward below minimap and above E/R");
                foreach (var other in new[] { health, map, skills, forms, nodes.FirstOrDefault(r => r.name == "ShopInteract"), nodes.FirstOrDefault(r => r.name == "Crosshair") })
                    if (other && other.gameObject.activeInHierarchy) Check(!rect.Overlaps(Bounds(other, camera)), prefix + " reward avoids " + other.name);
                if (shortcuts) foreach (Transform shortcut in shortcuts)
                    if (shortcut.gameObject.activeInHierarchy && shortcut is RectTransform shortcutRect) Check(!rect.Overlaps(Bounds(shortcutRect, camera)), prefix + " reward avoids shortcut " + shortcut.name);
            }
            for (int i = 0; i < visible.Length; i++)
                for (int j = i + 1; j < visible.Length; j++) Check(!visible[i].Overlaps(visible[j]), prefix + " reward rows do not overlap");
            foreach (var label in balance.GetComponentsInChildren<TMP_Text>(false).Concat(feed.GetComponentsInChildren<TMP_Text>(false))) {
                label.ForceMeshUpdate(); Check(!label.isTextOverflowing, prefix + " gold text fits: " + label.name);
            }
        }

        IEnumerator Capture(string name, int width, int height, bool large)
        {
            ResetFeed(0, 10, .3f);
            balance.SetGold(large ? int.MaxValue : 1250);
            Check(balance.GetComponentInChildren<TMP_Text>(true).text == (large ? "골드 2,147,483,647" : "골드 1,250"), name + " exact balance text");
            feed.ShowGoldGain(20); feed.ShowGoldGain(50); feed.ShowGoldGain(100);
            if (large)
            {
                ResetFeed(.4f, 10, .3f); feed.ShowGoldGain(int.MaxValue); feed.ShowGoldGain(int.MaxValue);
                yield return new WaitForSecondsRealtime(.45f); feed.ShowGoldGain(50);
                yield return new WaitForSecondsRealtime(.45f); feed.ShowGoldGain(100);
                Check(feed.GetRow(0).GetComponentInChildren<TMP_Text>(true).text == "골드 +4,294,967,294", name + " merged large reward exact text");
            }
            yield return new WaitForSecondsRealtime(.4f);
            var canvas = hud.GetComponentInParent<Canvas>(); var camera = canvas && canvas.worldCamera ? canvas.worldCamera : FindFirstObjectByType<Camera>();
            if (!camera || !canvas) { errors.Add("Capture camera or HUD canvas missing"); yield break; }
            var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; float oldDistance = canvas.planeDistance;
            var oldTarget = camera.targetTexture; var oldColor = camera.backgroundColor; var oldActive = RenderTexture.active;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D texture = null;
            try
            {
                camera.targetTexture = target; camera.backgroundColor = new Color(.075f, .082f, .095f, 1);
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); yield return null; Canvas.ForceUpdateCanvases();
                CheckLayout(camera, width, height, name); camera.Render();
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                Check(texture.GetPixels32().Count(p => p.r > 100 || p.g > 100 || p.b > 100) > 500, name + " contains rendered UI");
                File.WriteAllBytes(Path.Combine(output, name + ".png"), texture.EncodeToPNG()); screenshots.Add(name + ".png");
            }
            finally
            {
                RenderTexture.active = oldActive; camera.targetTexture = oldTarget; camera.backgroundColor = oldColor;
                canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldDistance;
                if (texture != null) Destroy(texture); target.Release(); Destroy(target);
            }
        }

        void Finish()
        {
            if (finished) return;
            finished = true; Time.timeScale = originalTimeScale; Application.logMessageReceived -= Log;
            File.WriteAllText(Path.Combine(output, "runtime-validation.json"), JsonUtility.ToJson(new Report { success = errors.Count == 0, unityVersion = Application.unityVersion, passed = passed.ToArray(), errors = errors.Distinct().ToArray(), screenshots = screenshots.ToArray() }, true));
            Application.Quit(errors.Count == 0 ? 0 : 2);
        }
    }
}
