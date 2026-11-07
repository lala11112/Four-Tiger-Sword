using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Fits the real result prefab against three virtual viewports without changing the Game view.</summary>
public static class ResultSummaryLayoutRegression
{
    private const float Epsilon = 1f;

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run result layout verification in Edit Mode.");
        const string prefabPath = "Assets/_Project/Prefabs/UI/BoundaryZeroRunOverlay.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Check(prefab != null, "The authored result overlay is missing.");

        var preview = EditorSceneManager.NewPreviewScene();
        GameObject instance = null;
        try
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
            instance.transform.localScale = Vector3.one;
            var overlay = instance.GetComponent<BoundaryZeroRunOverlay>();
            var canvas = instance.GetComponent<Canvas>();
            var scaler = instance.GetComponent<UnityEngine.UI.CanvasScaler>();
            Check(overlay != null && canvas != null && scaler != null, "The result prefab is missing its Canvas setup.");
            Check(scaler.uiScaleMode == UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize
                && scaler.screenMatchMode == UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight,
                "The virtual viewport must use the same scaler policy as the result prefab.");

            var viewport = (RectTransform)instance.transform;
            var fit = overlay.ModalRoot.GetComponentInChildren<BoundaryZeroWindowFit>(true);
            Check(fit != null, "The result overlay is missing its responsive frame.");
            // Give the real layout a measurable viewport in Canvas units. A World Space Canvas
            // lets the fixture supply its size without changing the Editor's Game view.
            canvas.renderMode = RenderMode.WorldSpace;
            scaler.enabled = false;
            overlay.ModalRoot.SetActive(true);
            var nominal = new RunResultSummary("1-2  |  지하 연구소", 125, 20, 4, "강화석", 1_420);
            var stress = new RunResultSummary("12-34  |  경계선 외곽 지역 심층 조사와 생존자 수색 작전",
                9_876_543_210L, 123_456_789, 12_345, "강화석", int.MaxValue);

            foreach (var resolution in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(3440, 1440) })
            {
                float widthRatio = resolution.x / scaler.referenceResolution.x;
                float heightRatio = resolution.y / scaler.referenceResolution.y;
                float scale = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(widthRatio, 2f), Mathf.Log(heightRatio, 2f),
                    scaler.matchWidthOrHeight));
                viewport.sizeDelta = new Vector2(resolution.x, resolution.y) / scale;
                fit.Refresh();
                Verify(overlay, viewport, nominal, false, resolution);
                Verify(overlay, viewport, stress, true, resolution);
            }

            overlay.ShowLoading();
            Check(!overlay.ResultSummaryRoot.activeSelf && overlay.Details.gameObject.activeSelf
                && Mathf.Approximately(overlay.Details.fontSize, 27f), "Result rows or enlarged text leaked into loading mode.");
            overlay.ShowError();
            Check(!overlay.ResultSummaryRoot.activeSelf && overlay.Details.gameObject.activeSelf
                && Mathf.Approximately(overlay.Details.fontSize, 27f), "Result rows or enlarged text leaked into error mode.");
            return "PASS: result glyph bounds, distinct rows, and action fit at 1280x720, 1920x1080 and 3440x1440; both results, large values, long chapter, loading/error reset.";
        }
        finally
        {
            if (instance != null) Object.DestroyImmediate(instance);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void Verify(BoundaryZeroRunOverlay overlay, RectTransform viewport, RunResultSummary summary,
        bool won, Vector2Int resolution)
    {
        string context = $"{resolution.x}x{resolution.y}, {(won ? "clear / large values" : "defeat")}: ";
        overlay.ShowResult(won);
        overlay.SetResultSummary(summary);
        overlay.Title.text = won ? "탐사 완료" : "탐사 종료";
        var button = overlay.ActionButton.GetComponentInChildren<TMP_Text>(true);
        button.text = "거점으로 돌아가기";
        Canvas.ForceUpdateCanvases();
        var rows = overlay.ResultSummaryRoot;
        Check(rows != null && rows.activeInHierarchy && !overlay.Details.gameObject.activeSelf,
            context + "The separate result summary did not replace the old details label.");
        var rowLabels = new List<TMP_Text>(rows.GetComponentsInChildren<TMP_Text>());
        Check(rowLabels.Count >= 8, context + "The reward names, values or helper row are missing.");
        Check(TextAt(rows, "Progress").text == summary.ProgressLabel
            && TextAt(rows, "StageRewardValue").text == Format(summary.StageReward)
            && TextAt(rows, "DeathRewardValue").text == Format(summary.DeathReward)
            && TextAt(rows, "BalanceValue").text == Format(summary.Balance.GetValueOrDefault())
            && TextAt(rows, "ClearedRooms").text.Contains(Format(summary.ClearedRooms)),
            context + "The rows did not render the supplied progress and full reward values.");
        Check(NominalSize(TextAt(rows, "StageRewardLabel")) == 48f
            && NominalSize(TextAt(rows, "StageRewardValue")) == 56f
            && NominalSize(TextAt(rows, "ClearedRooms")) == 34f && NominalSize(button) == 42f,
            context + "The requested result typography sizes changed.");

        var summaryBounds = RectIn((RectTransform)rows.transform, viewport);
        var buttonBounds = RectIn((RectTransform)overlay.ActionButton.transform, viewport);
        var titleBounds = RectIn(overlay.Title.rectTransform, viewport);
        Check(Contains(viewport.rect, summaryBounds) && Contains(viewport.rect, buttonBounds),
            context + "The summary or action escaped the viewport.");
        Check(summaryBounds.yMin - buttonBounds.yMax > Epsilon && titleBounds.yMin - summaryBounds.yMax > Epsilon,
            context + "The summary touches or overlaps the title or return action.");
        Check(Contains(buttonBounds, GlyphsIn(button, viewport, context)), context + "The return label escaped its button.");

        var glyphBounds = new List<(string Name, Rect Bounds)>(rowLabels.Count);
        foreach (var label in rowLabels)
        {
            var bounds = GlyphsIn(label, viewport, context);
            Check(Contains(summaryBounds, bounds) && Contains(RectIn(label.rectTransform, viewport), bounds),
                context + label.name + " glyphs escaped their row.");
            foreach (var earlier in glyphBounds)
                Check(!Overlaps(earlier.Bounds, bounds), context + earlier.Name + " overlaps " + label.name + ".");
            glyphBounds.Add((label.name, bounds));
        }
    }

    private static TMP_Text TextAt(GameObject root, string child)
    {
        var label = root.transform.Find(child)?.GetComponent<TMP_Text>();
        Check(label != null, "Missing result label " + child + ".");
        return label;
    }

    private static float NominalSize(TMP_Text label) => label.enableAutoSizing ? label.fontSizeMax : label.fontSize;

    private static string Format(long amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

    private static Rect GlyphsIn(TMP_Text label, RectTransform space, string context)
    {
        label.ForceMeshUpdate(true, true);
        Check(!label.isTextOverflowing && !label.isTextTruncated, context + label.name + " text was clipped or truncated.");
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        int visible = 0;
        for (int i = 0; i < label.textInfo.characterCount; i++)
        {
            var character = label.textInfo.characterInfo[i];
            if (!character.isVisible) continue;
            Include(space.InverseTransformPoint(label.transform.TransformPoint(character.bottomLeft)), ref min, ref max);
            Include(space.InverseTransformPoint(label.transform.TransformPoint(character.topRight)), ref min, ref max);
            visible++;
        }
        Check(visible > 0, context + label.name + " has no visible glyphs.");
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static Rect RectIn(RectTransform rect, RectTransform space)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in corners) Include(space.InverseTransformPoint(corner), ref min, ref max);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void Include(Vector2 point, ref Vector2 min, ref Vector2 max)
    {
        min = Vector2.Min(min, point);
        max = Vector2.Max(max, point);
    }

    private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - Epsilon
        && inner.xMax <= outer.xMax + Epsilon && inner.yMin >= outer.yMin - Epsilon && inner.yMax <= outer.yMax + Epsilon;

    private static bool Overlaps(Rect a, Rect b) => Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > Epsilon
        && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > Epsilon;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Result layout: " + message);
    }
}
