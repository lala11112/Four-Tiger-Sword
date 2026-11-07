using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Verifies authored run artwork, readable mode labels and persisted modal blur dependencies.</summary>
public static class BoundaryZeroOverlayRegression
{
    private static int _checks;

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run overlay regression in Edit Mode.");
        _checks = 0;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/BoundaryZeroRunOverlay.prefab");
        Check(prefab != null, "The authored run overlay is missing.");
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
            var overlay = root.GetComponent<BoundaryZeroRunOverlay>();
            BoundaryZeroResultSetup.Validate(overlay);
            root.transform.localScale = Vector3.one;
            overlay.ModalRoot.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Rect(root, "Modal/ReferenceFrame/ResultArtwork", new Vector2(120, -270), new Vector2(1120, 806));
            Rect(root, "Modal/ReferenceFrame/ResultArtwork/ClearGate", new Vector2(95, -166), new Vector2(839, 573));
            Rect(root, "Modal/ReferenceFrame/ResultArtwork/DeathTalisman", new Vector2(344, -78), new Vector2(419, 682));

            overlay.ShowLoading();
            Check(root.GetComponent<Canvas>().sortingOrder == 200, "Loading must cover open gameplay modals.");
            VisibleTitle(overlay.Title, "구역 준비 중");
            Check(overlay.Title.rectTransform.rect.height >= 60, "The loading title clips the existing Noto font line height.");
            var loading = root.transform.Find("Modal/ReferenceFrame/Loading");
            Check(loading != null && loading.gameObject.activeSelf, "Loading mode did not enable its authored layers.");
            Check(loading.Find("ProgressTrack/Activity") != null, "Indeterminate loading activity is missing.");
            foreach (var image in loading.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                if (image.name.StartsWith("Channel", StringComparison.Ordinal))
                    Check(Mathf.Approximately(image.fillAmount, 0), "An unknown loading percentage was fabricated.");
            overlay.ShowResult(true);
            Check(root.GetComponent<Canvas>().sortingOrder == 200, "The run result must cover open gameplay modals.");
            VisibleTitle(overlay.Title, "탐사 완료");
            Check(root.transform.Find("Modal/ReferenceFrame/ResultArtwork/ClearGate").gameObject.activeSelf,
                "The clear result did not enable its artwork.");
            overlay.ShowResult(false);
            VisibleTitle(overlay.Title, "탐사 종료");
            Check(root.transform.Find("Modal/ReferenceFrame/ResultArtwork/DeathTalisman").gameObject.activeSelf,
                "The death result did not enable its artwork.");
            overlay.ShowError();
            VisibleTitle(overlay.Title, "구역 준비 실패");
            overlay.Hide();
            Check(!overlay.ModalRoot.activeSelf, "Hiding the run result left the modal visible.");
            Check(root.GetComponent<Canvas>().sortingOrder == 100, "The playing-stage label would render sharply above captured shop/map backgrounds.");
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
        }
        Backdrop("Assets/_Game/Shop/UI/TemporaryShop.prefab");
        Backdrop("Assets/_Project/Prefabs/UI/DungeonFullMap.prefab");
        return "BoundaryZERO overlays: " + _checks + " checks passed (crop offsets, readable loading/results, indeterminate activity, persisted blur bindings).";
    }

    private static void Rect(GameObject root, string path, Vector2 position, Vector2 size)
    {
        var rect = root.transform.Find(path) as RectTransform;
        Check(rect != null && (rect.anchoredPosition - position).sqrMagnitude < .01f
            && (rect.sizeDelta - size).sqrMagnitude < .01f, "Artwork crop restoration differs from the guide: " + path);
    }

    private static void VisibleTitle(TMP_Text label, string text)
    {
        label.text = text;
        label.ForceMeshUpdate(true, true);
        Check(label.preferredHeight <= label.rectTransform.rect.height + .1f, "A title does not fit the existing Noto font: " + text);
        int visible = 0;
        for (int i = 0; i < label.textInfo.characterCount; i++)
            if (label.textInfo.characterInfo[i].isVisible) visible++;
        Check(visible > 0, "A mode title has no visible glyphs: " + text);
    }

    private static void Backdrop(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Check(prefab != null, "A modal prefab is missing: " + path);
        var backdrop = prefab.GetComponent<BoundaryZeroBackdrop>();
        Check(backdrop != null && backdrop.HasBindings, "The saved modal backdrop lost its sharp-content CanvasGroup: " + path);
        var serialized = new SerializedObject(backdrop);
        var content = serialized.FindProperty("modalContent").objectReferenceValue as CanvasGroup;
        var image = serialized.FindProperty("background").objectReferenceValue as UnityEngine.UI.RawImage;
        Check(content != null && image != null && !image.transform.IsChildOf(content.transform),
            "The captured background is inside the group hidden during capture: " + path);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}
