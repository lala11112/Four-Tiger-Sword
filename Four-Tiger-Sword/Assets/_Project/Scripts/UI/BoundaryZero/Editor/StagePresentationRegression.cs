using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Checks stage fades and banners, the death fade and the authored result presentation.</summary>
public static class StagePresentationRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    [MenuItem("Tools/Studio Baekho/UI/Run Stage Presentation Regression")]
    private static void Menu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run stage presentation regression in Edit Mode.");
        checks = 0;
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject fallback = null, authored = null;
        try
        {
            fallback = new GameObject("Stage presentation regression");
            SceneManager.MoveGameObjectToScene(fallback, preview);
            var ui = fallback.AddComponent<StageResultUI>();
            ui.BeginStageIntro(2, 3);
            var intro = fallback.transform.Find("StageIntroCanvas");
            Check(intro != null && intro.gameObject.activeSelf, "The stage banner was not created synchronously.");
            var group = intro.GetComponent<CanvasGroup>();
            Check(group.alpha == 1f && group.blocksRaycasts && !group.interactable,
                "The stage banner must block pointer input during placement.");
            var blocker = intro.GetComponent<UnityEngine.UI.Image>();
            Check(intro.GetComponent<Canvas>().sortingOrder > 200 && blocker != null
                && blocker.color.a == 0f && blocker.raycastTarget,
                "The full-screen pointer blocker must remain transparent.");
            var worldCover = intro.Find("WorldReveal")?.GetComponent<UnityEngine.UI.Image>();
            Check(worldCover != null && worldCover.color == Color.black && !worldCover.raycastTarget
                && worldCover.transform.GetSiblingIndex() == intro.childCount - 1,
                "The first frame must cover the world and banner in black while the root blocks pointer input.");
            var coverRect = worldCover.rectTransform;
            Check(coverRect.anchorMin == Vector2.zero && coverRect.anchorMax == Vector2.one
                && coverRect.offsetMin == Vector2.zero && coverRect.offsetMax == Vector2.zero,
                "The black stage cover must fill the entire screen.");
            var banner = intro.Find("StageBanner") as RectTransform;
            var band = banner != null ? banner.GetComponent<UnityEngine.UI.Image>() : null;
            Check(band != null && band.color.a > 0f && band.color.a < 1f && !band.raycastTarget
                && banner.anchorMin == new Vector2(0f, .5f) && banner.anchorMax == new Vector2(1f, .5f)
                && banner.anchoredPosition == Vector2.zero && banner.sizeDelta == new Vector2(0f, 435f),
                "The intro background must be a semitransparent horizontal band centered on screen.");
            var number = banner.Find("StageNumber")?.GetComponent<TMP_Text>();
            Check(number != null && number.text == "Stage 2 - 3" && number.alpha == 0f,
                "The initially hidden title must show the requested chapter and stage inside the band.");
            Check(number.rectTransform.anchorMin == Vector2.zero && number.rectTransform.anchorMax == Vector2.one
                && number.enableAutoSizing, "The stage title must fit within the banner as its width changes.");
            float duration = Field<float>(ui, "introNumberFadeIn") + Field<float>(ui, "introNumberHold")
                + Field<float>(ui, "introNumberFadeOut") + Field<float>(ui, "introWorldReveal");
            Check(Mathf.Abs(duration - 1.8f) < .001f, "The default intro duration changed.");
            var cancelled = ui.PlayStageIntro();
            Check(cancelled.MoveNext() && worldCover.color == Color.black,
                "The intro did not retain its first fully black frame.");
            var sampleIntro = typeof(StageResultUI).GetMethod("ApplyStageIntro", Hidden);
            sampleIntro.Invoke(ui, new object[] { .25f });
            Check(Mathf.Abs(worldCover.color.a - .5f) < .001f && number.alpha == 1f && group.alpha == 1f
                && group.blocksRaycasts, "Halfway through the reveal, only the black cover should be half opaque.");
            sampleIntro.Invoke(ui, new object[] { .5f });
            Check(worldCover.color.a == 0f && number.alpha == 1f && group.alpha == 1f && group.blocksRaycasts,
                "The world cover must clear at 0.5 seconds while the title remains visible and input stays blocked.");
            ui.ShowError("Expected regression error");
            Check(!cancelled.MoveNext() && !ui.IsStageIntroPlaying && !ui.StageIntroCompleted
                && !intro.gameObject.activeSelf && !group.blocksRaycasts && worldCover.color.a == 0f,
                "An error left the intro active or allowed its stale coroutine to complete.");
            ui.BeginStageIntro(10, 12);
            var interrupted = ui.PlayStageIntro();
            Check(interrupted.MoveNext() && worldCover.color == Color.black, "A new intro did not reset the black cover.");
            sampleIntro.Invoke(ui, new object[] { .25f });
            ui.CancelStageIntro();
            Check(!interrupted.MoveNext() && worldCover.color.a == 0f && !intro.gameObject.activeSelf
                && !group.blocksRaycasts && !ui.IsStageIntroPlaying && !ui.StageIntroCompleted,
                "Cancelling an active fade must remove the cover and release the blocker.");
            foreach (var field in new[] { "introNumberFadeIn", "introNumberHold", "introNumberFadeOut", "introWorldReveal" })
                typeof(StageResultUI).GetField(field, Hidden).SetValue(ui, 0f);
            ui.BeginStageIntro(10, 12);
            var instant = ui.PlayStageIntro();
            Check(instant.MoveNext() && worldCover.color == Color.black && !instant.MoveNext() && ui.StageIntroCompleted
                && !ui.IsStageIntroPlaying && !intro.gameObject.activeSelf && worldCover.color.a == 0f
                && !group.blocksRaycasts, "A zero-duration preset must hold one black frame before releasing the cover.");
            ui.ShowStage("10-12");
            Check(!ui.IsStageIntroPlaying && !intro.gameObject.activeSelf,
                "Returning to the live stage replayed the introduction.");
            DeathFade(ui);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/BoundaryZeroRunOverlay.prefab");
            Check(prefab != null, "The authored result overlay is missing.");
            authored = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
            var overlay = authored.GetComponent<BoundaryZeroRunOverlay>();
            overlay.ModalRoot.SetActive(false);
            UIButtonMotion.Install(overlay.ModalRoot);
            overlay.ShowResult(true);
            Vector2 titlePosition = overlay.Title.rectTransform.anchoredPosition;
            var actionRect = (RectTransform)overlay.ActionButton.transform;
            Vector2 actionCenter = VisualCenter(actionRect);
            overlay.PrepareResultReveal();
            overlay.ModalRoot.SetActive(true);
            // Edit Mode does not run the button's Awake when the modal first appears.
            var motion = overlay.ActionButton.GetComponent<UIButtonMotion>();
            typeof(UIButtonMotion).GetMethod("Awake", Hidden).Invoke(motion, null);
            Check(actionRect.pivot == new Vector2(.5f, .5f)
                && Vector2.Distance(VisualCenter(actionRect), actionCenter) < .01f,
                "Centering the action's pivot changed its visual position.");
            var art = authored.transform.Find("Modal/ReferenceFrame/ResultArtwork").GetComponent<CanvasGroup>();
            var title = overlay.Title.GetComponent<CanvasGroup>();
            var details = overlay.ResultSummaryRoot.GetComponent<CanvasGroup>();
            var action = overlay.ActionButton.GetComponent<CanvasGroup>();
            Check(art.alpha == 0f && title.alpha == 0f && details.alpha == 0f && action.alpha == 0f,
                "Result content can flash at full opacity before its reveal.");
            Sample(overlay, .12f);
            Check(art.alpha > title.alpha && title.alpha > 0f && details.alpha == 0f && action.alpha == 0f,
                "The artwork/title/details/actions result order changed.");
            Sample(overlay, .5f);
            Check(details.alpha == 1f && action.alpha > 0f && action.alpha < 1f,
                "Result actions did not reveal after the reward details.");
            Check(Vector2.Distance(VisualCenter(actionRect), actionCenter) < .01f,
                "Revealing the result moved the action away from its authored position.");
            overlay.CancelResultReveal();
            Check(art.alpha == 1f && title.alpha == 1f && details.alpha == 1f && action.alpha == 1f
                && overlay.Title.rectTransform.anchoredPosition == titlePosition && !overlay.ResultRevealCompleted,
                "Cancelling the result reveal left opacity, position or completion state behind.");
            Check(Vector2.Distance(VisualCenter(actionRect), actionCenter) < .01f,
                "Cancelling the result reveal moved the action away from its authored position.");
            overlay.ShowError();
            Check(overlay.Title.GetComponent<CanvasGroup>().alpha == 1f,
                "An interrupted result reveal hid a later error screen.");
            PlacementAndResume(preview);
        }
        finally
        {
            if (fallback != null) UnityEngine.Object.DestroyImmediate(fallback);
            if (authored != null) UnityEngine.Object.DestroyImmediate(authored);
            EditorSceneManager.ClosePreviewScene(preview);
        }
        return $"Stage presentation: {checks} checks passed (world reveal, banner, stage title, death fade, result order/reset).";
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);

    private static void DeathFade(StageResultUI ui)
    {
        ui.ShowRunResult(false);
        var panel = Field<GameObject>(ui, "resultPanel");
        ui.BeginDeathPresentation();
        var fadeRoot = ui.transform.Find("DeathFadeCanvas");
        Check(fadeRoot != null && fadeRoot.gameObject.activeSelf, "The death presentation did not create its fade canvas.");
        var group = fadeRoot.GetComponent<CanvasGroup>();
        var image = fadeRoot.GetComponent<UnityEngine.UI.Image>();
        Check(!panel.activeSelf && group.alpha == 0f && group.blocksRaycasts && !group.interactable
            && image.color == Color.black && image.raycastTarget && !ui.IsDeathFadePlaying && !ui.DeathFadeCompleted,
            "Waiting for the death animation must hide the result, keep the world visible and block UI input.");

        typeof(StageResultUI).GetField("deathFadeDuration", Hidden).SetValue(ui, 0f);
        var fade = ui.FadeOutForDeath();
        Check(fade.MoveNext() && group.alpha == 1f && ui.IsDeathFadePlaying && !ui.DeathFadeCompleted && !panel.activeSelf,
            "A zero-duration death fade must yield a fully black frame before the result can appear.");
        Check(!fade.MoveNext() && ui.DeathFadeCompleted && !ui.IsDeathFadePlaying && group.alpha == 1f && !panel.activeSelf,
            "Completing the fade must leave the black layer visible without opening the result itself.");
        ui.ShowRunResult(false);
        int fadeOrder = fadeRoot.GetComponent<Canvas>().sortingOrder;
        Check(panel.activeSelf && fadeRoot.gameObject.activeSelf && group.alpha == 1f
            && fadeOrder == 199 && fadeOrder < panel.GetComponentInParent<Canvas>().sortingOrder,
            "The result must appear above the retained black layer.");
        ui.HideResult();
        Check(!panel.activeSelf && !fadeRoot.gameObject.activeSelf && !group.blocksRaycasts && !ui.DeathFadeCompleted,
            "Hiding the result must release the retained death fade and its input blocker.");

        ui.BeginDeathPresentation();
        var stale = ui.FadeOutForDeath();
        Check(stale.MoveNext(), "The second death fade did not yield before completion.");
        ui.ShowLoading("Stage 1-1");
        Check(panel.activeSelf && !fadeRoot.gameObject.activeSelf && !group.blocksRaycasts
            && !ui.IsDeathFadePlaying && !ui.DeathFadeCompleted, "Loading must clear an interrupted death fade.");
        ui.BeginDeathPresentation();
        Check(!stale.MoveNext() && group.alpha == 0f && !ui.DeathFadeCompleted,
            "An interrupted fade must not mark a later death presentation complete.");
        ui.HideResult();
    }

    private static void PlacementAndResume(Scene scene)
    {
        var host = new GameObject("Frozen stage player regression");
        host.SetActive(false);
        SceneManager.MoveGameObjectToScene(host, scene);
        var data = ScriptableObject.CreateInstance<PlayerStatData>();
        try
        {
            data.baseHP = 100;
            var player = host.AddComponent<PlayerController>();
            host.AddComponent<FormEffectRunner>();
            var stats = host.GetComponent<PlayerStatManager>();
            stats.Initialize(data);
            var input = host.GetComponent<PlayerInputHandler>();
            var movement = host.GetComponent<PlayerMovement>();
            var controller = host.GetComponent<CharacterController>();
            Property(player, "StatManager", stats);
            Property(player, "Input", input);
            Property(player, "Movement", movement);
            Property(player, "Controller", controller);
            input.AttackBuffer.Set();
            float hp = stats.CurrentHp;
            player.BeginStageTransition();
            Check(player.IsStageTransitioning && !input.enabled && !movement.enabled && !controller.enabled
                && !input.AttackBuffer.IsActive, "Beginning a stage did not block retained player controls and buffered attacks.");
            var destination = new Vector3(23f, 4f, -18f);
            var rotation = Quaternion.Euler(0f, 135f, 0f);
            player.PlaceStageTransition(destination, rotation);
            Check(player.IsStageTransitioning && !input.enabled && !movement.enabled && !controller.enabled
                && host.transform.position == destination && Quaternion.Angle(host.transform.rotation, rotation) < .01f,
                "Placement resumed controls or failed to place the frozen player.");
            player.ResumeStageTransition();
            Check(!player.IsStageTransitioning && input.enabled && movement.enabled && controller.enabled
                && stats.CurrentHp == hp,
                "Stage reveal failed to restore controls without changing retained resources.");
            input.enabled = movement.enabled = false;
            player.BeginStageTransition();
            destination += Vector3.right;
            player.CompleteStageTransition(destination, Quaternion.identity);
            Check(!player.IsStageTransitioning && !input.enabled && !movement.enabled && controller.enabled
                && host.transform.position == destination,
                "The compatibility transition failed or re-enabled controls disabled by another owner.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(data);
        }
    }

    private static void Property(object target, string name, object value)
        => target.GetType().GetProperty(name).SetValue(target, value);

    private static Vector2 VisualCenter(RectTransform rect)
        => rect.parent.InverseTransformPoint(rect.TransformPoint(rect.rect.center));

    private static void Sample(BoundaryZeroRunOverlay overlay, float elapsed)
        => typeof(BoundaryZeroRunOverlay).GetMethod("ApplyResultReveal", Hidden).Invoke(overlay, new object[] { elapsed });
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Stage presentation regression: " + message);
        checks++;
    }
}
