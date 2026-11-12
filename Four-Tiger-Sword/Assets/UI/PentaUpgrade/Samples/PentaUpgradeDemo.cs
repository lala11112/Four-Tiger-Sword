using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Penta.Upgrade.Samples
{
    /// <summary>Sample reopening control and opt-in player verification; no gameplay mutations.</summary>
    public sealed class PentaUpgradeDemo : MonoBehaviour
    {
        public PentaUpgradeView View;
        [Serializable] sealed class Report
        {
            public bool success;
            public string unityVersion;
            public string[] passed, errors, screenshots;
        }
        readonly List<string> passed = new List<string>();
        readonly List<string> errors = new List<string>();
        readonly List<string> screenshots = new List<string>();
        string output;
        bool verifying, finished;

        void Start()
        {
            if (!View) View = FindFirstObjectByType<PentaUpgradeView>();
            var args = Environment.GetCommandLineArgs();
            verifying = Array.IndexOf(args, "--penta-upgrade-verify") >= 0;
            if (!verifying) return;
            int index = Array.IndexOf(args, "--penta-upgrade-output");
            output = Path.GetFullPath(index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "PentaUpgradeVerification"));
            Directory.CreateDirectory(output);
            Application.logMessageReceived += Log;
            StartCoroutine(Watchdog());
            StartCoroutine(Guarded(Verify()));
        }
        void Update()
        {
            #if ENABLE_LEGACY_INPUT_MANAGER
            if (!verifying && View && !View.IsOpen && Input.GetKeyDown(KeyCode.E)) View.Open();
            #endif
        }
        void Log(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || (message.Contains("font") && message.Contains("was not found"))) errors.Add(message);
        }
        void Check(bool value, string message) { (value ? passed : errors).Add(message); }
        IEnumerator Guarded(IEnumerator work)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(work);
            while (stack.Count > 0 && !finished)
            {
                object current = null; bool moved = false;
                try { moved = stack.Peek().MoveNext(); if (moved) current = stack.Peek().Current; }
                catch (Exception ex) { errors.Add(ex.ToString()); break; }
                if (!moved) { stack.Pop(); continue; }
                if (current is IEnumerator nested) stack.Push(nested); else yield return current;
            }
            Finish();
        }
        IEnumerator Watchdog()
        {
            yield return new WaitForSecondsRealtime(100);
            if (!finished) { errors.Add("Verification timed out"); Finish(); }
        }
        IEnumerator Verify()
        {
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            yield return new WaitForSecondsRealtime(3f);
            yield return null; yield return null;
            Check(View != null, "Upgrade view exists");
            if (!View) yield break;
            View.Open(); View.HomeView();
            yield return null;
            Canvas.ForceUpdateCanvases();
            Check(View.IsOpen, "View opens");
            Check(!View.GetComponentInChildren<Canvas>(true), "Panel uses the existing parent Canvas");
            Check(!View.GetComponentInChildren<EventSystem>(true), "Panel contains no EventSystem");
            Check(!View.GetComponentInChildren<GraphicRaycaster>(true), "Panel contains no duplicate raycaster");
            Check(View.GetComponentInParent<Canvas>() != null, "Panel is parented under the host Canvas");
            Check(PentaUpgradeCatalog.Nodes.Count == 38, "Catalog has free origin and 37 upgrades");
            int connections = 0;
            foreach (var node in PentaUpgradeCatalog.Nodes)
            {
                connections += node.ParentIds.Count;
                Check(node.ParentIds.Count == (node.Id == "origin" ? 0 : 1), "Single-parent tree: " + node.Id);
            }
            Check(connections == 37, "Tree has 37 connections");
            var graphLinks = View.GetComponentsInChildren<PentaUpgradeLinks>(true);
            Check(graphLinks.Length == 1 && graphLinks[0].ConnectionCount == 37, "UI graph draws 37 connections");
            int nodeControls = 0;
            foreach (var node in PentaUpgradeCatalog.Nodes)
                if (View.GetNodeButton(node.Id)) nodeControls++;
            Check(nodeControls == 38, "UI graph exposes all 38 node controls");
            foreach (var transform in View.GetComponentsInChildren<Transform>(true))
                Check(transform.name != "ChildrenHeading" && transform.name != "Child0" && transform.name != "Child1"
                    && transform.name != "Child2" && transform.name != "Child3", "No successor navigation: " + transform.name);
            foreach (var text in View.GetComponentsInChildren<TMP_Text>(true))
                Check(!text.text.Contains("이어지는 각인"), "No successor heading: " + text.name);
            Check(View.SelectedId == "origin", "Default selection is the origin");
            Check(Mathf.Abs(View.Zoom - 1f) < .02f, "Home uses readable normal scale");
            Check(Mathf.Abs(View.NodeContent.rect.width - 1178f) < 1f
                && Mathf.Abs(View.NodeContent.rect.height - 5276f) < 1f, "Vertical tree content is 1178 by 5276");
            Check(View.NodeContent.rect.width * View.Zoom <= View.Viewport.rect.width + 1f,
                "Entire tree width fits viewport at normal zoom");
            CheckHorizontalNodeBounds();
            CheckDraftStatus();
            var homeVisible = VisibleNodes();
            Check(homeVisible.Contains("origin") && homeVisible.Count > 0 && homeVisible.Count <= 8,
                "Home shows the origin and only a few nodes: " + homeVisible.Count);
            yield return Capture("01-origin", 1920, 1080);
            foreach (var node in PentaUpgradeCatalog.Nodes)
            {
                var button = View.GetNodeButton(node.Id);
                Check(button != null, "Node control exists: " + node.Id);
                if (!button) continue;
                View.SelectAndFocus(node.Id);
                yield return null;
                Canvas.ForceUpdateCanvases();
                Hit(node.Id);
                button.onClick.Invoke();
                Check(View.SelectedId == node.Id, "Node click selects: " + node.Id);
            }
            View.SelectAndFocus("revive");
            yield return Capture("02-revive", 1920, 1080);
            View.SelectAndFocus("finisher");
            yield return Capture("03-deeper-finisher", 1920, 1080);
            yield return Capture("04-1280x720", 1280, 720, true);
            yield return Capture("05-2560x1080", 2560, 1080, true);
            View.Close(); Check(!View.IsOpen, "Close hides upgrade view");
            View.Open(); Check(View.IsOpen, "Reopen restores upgrade view");
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.3f);
            View.HomeView();
            yield return null;
            Canvas.ForceUpdateCanvases();
            Vector2 homePosition = View.NodeContent.anchoredPosition;
            var navigation = View.Viewport.GetComponentInParent<PentaUpgradeViewport>();
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, View.Viewport.TransformPoint(View.Viewport.rect.center)),
                scrollDelta = new Vector2(0, 1),
                button = PointerEventData.InputButton.Left
            };
            navigation.OnScroll(pointer);
            Check(View.Zoom > 1f, "Mouse wheel event zooms graph");
            View.SetZoom(1.2f);
            View.FocusSelected();
            Check(Mathf.Abs(View.Zoom - 1.2f) < .02f, "Zoom uses actual scale 1.2");
            yield return Capture("06-zoomed", 1920, 1080);
            View.HomeView();
            yield return null;
            Check(Mathf.Abs(View.Zoom - 1f) < .02f && Vector2.Distance(View.NodeContent.anchoredPosition, homePosition) < 1f,
                "Home restores normal scale and start position");
            Canvas.ForceUpdateCanvases();
            var beforeVisible = VisibleNodes();
            View.Select("guard");
            Vector2 beforeDrag = View.NodeContent.anchoredPosition;
            navigation.OnInitializePotentialDrag(pointer);
            navigation.OnBeginDrag(pointer);
            // Screen Y increases upward: moving the content up reveals deeper descendants.
            pointer.position += new Vector2(0, 800);
            pointer.delta = new Vector2(0, 800);
            navigation.OnDrag(pointer);
            navigation.OnEndDrag(pointer);
            navigation.StopMovement();
            yield return null;
            Canvas.ForceUpdateCanvases();
            var afterVisible = VisibleNodes();
            Check(Vector2.Distance(beforeDrag, View.NodeContent.anchoredPosition) > 100f, "Pointer drag pans graph at normal scale");
            Check(!beforeVisible.SetEquals(afterVisible) && afterVisible.Count > 0,
                "Normal-scale drag changes the visible nodes");
            Check(View.NodeContent.anchoredPosition.y > beforeDrag.y + 100f,
                "Upward drag advances down the vertical spine");
            Check(Mathf.Abs(View.Zoom - 1f) < .02f, "Drag preserves normal scale");
            Check(Mathf.Abs(View.NodeContent.anchoredPosition.x - beforeDrag.x) < 1f,
                "Vertical drag preserves horizontal centering");
            CheckHorizontalNodeBounds();
            yield return Capture("07-dragged", 1920, 1080);
            string bottomId = null;
            float lowestY = float.PositiveInfinity;
            foreach (var node in PentaUpgradeCatalog.Nodes)
            {
                var button = View.GetNodeButton(node.Id);
                float y = View.NodeContent.InverseTransformPoint(button.transform.position).y;
                if (y < lowestY) { lowestY = y; bottomId = node.Id; }
            }
            Check(bottomId != null, "Bottom spine node exists");
            if (bottomId != null)
            {
                View.SelectAndFocus(bottomId);
                yield return null;
                Canvas.ForceUpdateCanvases();
                Check(VisibleNodes().Contains(bottomId), "Bottom spine focus reaches deepest geometry");
                Hit(bottomId);
                Check(Mathf.Abs(View.NodeContent.anchoredPosition.x - homePosition.x) < 1f,
                    "Bottom focus preserves horizontal centering at normal zoom");
                CheckHorizontalNodeBounds();
                CheckDraftStatus();
                yield return Capture("10-bottom-spine", 1920, 1080);
            }
            yield return VerifyHostCanvas();
            foreach (var transform in View.GetComponentsInChildren<Transform>(true))
                foreach (var component in transform.GetComponents<Component>())
                    if (component == null) errors.Add("Missing script: " + transform.name);
            Check(View.Viewport && View.NodeContent, "Graph viewport and content references exist");
        }

        void CheckHorizontalNodeBounds()
        {
            var corners = new Vector3[4];
            foreach (var node in PentaUpgradeCatalog.Nodes)
            {
                var button = View.GetNodeButton(node.Id);
                if (!button) continue;
                ((RectTransform)button.transform).GetWorldCorners(corners);
                float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
                foreach (var corner in corners)
                {
                    float x = View.Viewport.InverseTransformPoint(corner).x;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                }
                Check(minX >= View.Viewport.rect.xMin - 1f && maxX <= View.Viewport.rect.xMax + 1f,
                    "Normal-scale node width stays inside viewport: " + node.Id);
            }
        }
        void CheckDraftStatus()
        {
            bool family = false, heading = false, subtitle = false, notice = false;
            foreach (var text in View.GetComponentsInChildren<TMP_Text>(true))
            {
                family |= text.name == "Family" && text.text.Contains("내용 미확정");
                heading |= text.name == "DescriptionHeading" && text.text == "효과 초안";
                subtitle |= text.name == "Subtitle" && text.text == "세로 줄기형 / 노드 내용 미확정";
                notice |= text.text == "이름과 효과는 검토 중입니다";
            }
            Check(family && heading && subtitle && notice, "Selected node and panel explicitly show unconfirmed draft content");
        }

        IEnumerator VerifyHostCanvas()
        {
            var host = View.GetComponentInParent<Canvas>();
            var scaler = host.GetComponent<CanvasScaler>();
            var originalReference = scaler.referenceResolution;
            var originalMode = scaler.uiScaleMode;
            var originalMatch = scaler.screenMatchMode;
            var originalFactor = scaler.scaleFactor;
            scaler.referenceResolution = new Vector2(2560, 1440);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            yield return null;
            Canvas.ForceUpdateCanvases();
            View.SelectAndFocus("revive");
            yield return null;
            Canvas.ForceUpdateCanvases();
            CheckPanelBounds("2560 reference");
            Hit("revive", "2560 reference after focus");
            Check(scaler.referenceResolution == new Vector2(2560, 1440), "Panel preserves host Canvas reference resolution");
            yield return Capture("08-existing-canvas-2560-reference", 1920, 1080, true);

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1;
            yield return Capture("09-existing-canvas-pixel-size", 1280, 720, true);
            CheckPanelBounds("Constant Pixel Size");
            Hit("revive", "Constant Pixel Size");

            // Camera mode uses the same supplied Canvas and its existing raycaster.
            var cameraObject = new GameObject("Panel Camera Verification", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = 1 << 5;
            camera.backgroundColor = new Color32(7, 13, 23, 255);
            host.worldCamera = camera;
            host.renderMode = RenderMode.ScreenSpaceCamera;
            host.planeDistance = 10;
            yield return null;
            Canvas.ForceUpdateCanvases();
            View.SelectAndFocus("revive");
            yield return null;
            Canvas.ForceUpdateCanvases();
            Hit("revive", "Screen Space Camera after focus");
            CheckPanelBounds("Screen Space Camera");

            View.gameObject.SetActive(false);
            yield return null;
            Check(!View.IsOpen, "Disabling panel closes it");
            View.gameObject.SetActive(true);
            yield return null;
            Check(View.IsOpen, "Enabling panel reopens it");
            Hit("revive", "Reenabled host panel");

            host.renderMode = RenderMode.ScreenSpaceOverlay;
            host.worldCamera = null;
            Destroy(cameraObject);
            scaler.referenceResolution = originalReference;
            scaler.screenMatchMode = originalMatch;
            scaler.uiScaleMode = originalMode;
            scaler.scaleFactor = originalFactor;
        }

        void CheckPanelBounds(string context)
        {
            var fit = View.GetComponentInChildren<PentaUpgradePanelFit>(true);
            Check(fit && fit.Panel, context + ": adaptive panel frame exists");
            if (!fit || !fit.Panel) return;
            var container = (RectTransform)fit.transform;
            var corners = new Vector3[4];
            fit.Panel.GetWorldCorners(corners);
            bool contained = true;
            foreach (var corner in corners)
            {
                Vector2 local = container.InverseTransformPoint(corner);
                contained &= container.rect.Contains(local);
            }
            Check(contained, context + ": panel frame stays inside existing Canvas");
            Check(Mathf.Abs(fit.Panel.localScale.x - fit.Panel.localScale.y) < .001f,
                context + ": panel keeps its aspect ratio");
        }
        HashSet<string> VisibleNodes()
        {
            var visible = new HashSet<string>();
            var corners = new Vector3[4];
            foreach (var node in PentaUpgradeCatalog.Nodes)
            {
                var button = View.GetNodeButton(node.Id);
                if (!button) continue;
                ((RectTransform)button.transform).GetWorldCorners(corners);
                Vector2 min = View.Viewport.InverseTransformPoint(corners[0]);
                Vector2 max = View.Viewport.InverseTransformPoint(corners[2]);
                if (View.Viewport.rect.Overlaps(Rect.MinMaxRect(min.x, min.y, max.x, max.y))) visible.Add(node.Id);
            }
            return visible;
        }
        void Hit(string id, string context = null)
        {
            string label = id + (string.IsNullOrEmpty(context) ? "" : " (" + context + ")");
            var button = View.GetNodeButton(id);
            if (!button || !EventSystem.current) { Check(false, "Raycast prerequisites: " + label); return; }
            var rect = (RectTransform)button.transform;
            var canvas = button.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
            bool found = false;
            foreach (var hit in hits)
            {
                if (!(hit.module is GraphicRaycaster)) continue;
                found = hit.gameObject == button.gameObject || hit.gameObject.transform.IsChildOf(button.transform);
                break;
            }
            Check(found, "GraphicRaycaster reaches node center: " + label);
        }
        IEnumerator Capture(string name, int width, int height, bool focusSelected = false)
        {
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.3f);
            if (focusSelected) View.FocusSelected();
            yield return null;
            Canvas.ForceUpdateCanvases();
            foreach (var text in View.GetComponentsInChildren<TMP_Text>(false))
            {
                text.ForceMeshUpdate();
                Check(!text.isTextOverflowing, name + " text fits: " + text.name);
            }
            yield return new WaitForEndOfFrame();
            string file = Path.Combine(output, name + ".png");
            if (File.Exists(file)) File.Delete(file);
            ScreenCapture.CaptureScreenshot(file);
            float deadline = Time.realtimeSinceStartup + 10;
            while ((!File.Exists(file) || new FileInfo(file).Length == 0) && Time.realtimeSinceStartup < deadline) yield return null;
            Check(File.Exists(file) && new FileInfo(file).Length > 0, "Screenshot saved: " + name);
            screenshots.Add(file);
        }
        void Finish()
        {
            if (finished) return;
            finished = true; Application.logMessageReceived -= Log;
            var report = new Report { success = errors.Count == 0, unityVersion = Application.unityVersion, passed = passed.ToArray(), errors = errors.ToArray(), screenshots = screenshots.ToArray() };
            File.WriteAllText(Path.Combine(output, "runtime-validation.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.success ? 0 : 1);
        }
        void OnDestroy() { Application.logMessageReceived -= Log; }
    }
}
