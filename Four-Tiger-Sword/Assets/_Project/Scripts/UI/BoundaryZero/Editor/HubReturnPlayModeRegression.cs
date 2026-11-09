using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Follows the authored result action into the world Hub while the death fixture isolates saves.</summary>
public static class HubReturnPlayModeRegression
{
    private const string HubScene = "Hub Scene";
    private static readonly FieldInfo LoaderOverride = typeof(HubReturnTransition).GetField(
        "SceneLoaderOverride", BindingFlags.Static | BindingFlags.NonPublic);

    public static string Start(bool failSave = false, string captureDirectory = null)
        => DeathPresentationPlayModeRegression.Start(failSave, false, captureDirectory, true);

    public static string Status() => DeathPresentationPlayModeRegression.Status();

    internal static async Task Verify(RunManager run, BoundaryZeroRunOverlay overlay,
        Func<Func<bool>, string, Task> wait, Action<bool, string> check, Action<string> phase, string captureDirectory)
    {
        if (LoaderOverride == null) throw new InvalidOperationException("The Hub scene loader override is missing.");
        var previousLoader = LoaderOverride.GetValue(null);
        try
        {
            phase("RUNNING: checking recovery from unavailable and throwing Hub loads.");
            await VerifyFailure(run, overlay, _ => null, wait, check);
            await VerifyFailure(run, overlay, _ => throw new InvalidOperationException("Expected test loader failure."),
                wait, check);
            phase("RUNNING: checking a real Hub load and fade-in.");
            await VerifySuccessfulReturn(run, overlay, wait, check, captureDirectory);
        }
        finally
        {
            LoaderOverride.SetValue(null, previousLoader);
        }
    }

    private static async Task VerifyFailure(RunManager run, BoundaryZeroRunOverlay overlay,
        Func<string, AsyncOperation> failure, Func<Func<bool>, string, Task> wait, Action<bool, string> check)
    {
        int calls = 0;
        int requestedFrame = -1;
        SetLoader(scene =>
        {
            calls++;
            requestedFrame = Time.frameCount;
            return failure(scene);
        });
        var summary = overlay.ResultSummaryRoot;
        int clickFrame = Time.frameCount;
        overlay.ActionButton.onClick.Invoke();
        run.ReturnToHub();
        var transition = Object.FindFirstObjectByType<HubReturnTransition>();
        check(run.IsReturningToHub && HubReturnTransition.IsActive && calls == 0
            && transition != null && transition.IsLoadingVisible && transition.LoadingHeading.text == "이동 중..."
            && !overlay.ActionButton.IsInteractable(), "The loading presentation or source input guard did not start immediately.");
        await wait(() => !HubReturnTransition.IsActive && overlay.ActionButton.IsInteractable(),
            "A failed Hub load did not return control to the result screen.");
        check(calls == 1 && requestedFrame > clickFrame && !run.IsReturningToHub
            && SceneManager.GetActiveScene().name == "Game Scene" && run.State == RunState.Lost
            && overlay.ModalRoot.activeInHierarchy && summary.activeInHierarchy
            && run.resultUI.DeathFadeCompleted, "Load failure discarded the result or made more than one request.");
        var error = overlay.ActionButton.transform.Find("HubReturnError")?.GetComponent<TMP_Text>();
        check(error != null && error.isActiveAndEnabled && error.text.Contains("다시 시도"),
            "The restored result did not tell the player how to retry the Hub load.");
        await wait(() => Object.FindFirstObjectByType<HubReturnTransition>() == null,
            "The failed load left its temporary Canvas alive.");
    }

    private static async Task VerifySuccessfulReturn(RunManager run, BoundaryZeroRunOverlay overlay,
        Func<Func<bool>, string, Task> wait, Action<bool, string> check, string captureDirectory)
    {
        int calls = 0;
        int requestedFrame = -1;
        string target = null;
        string renderedLoadingError = "The rendered loading text was not sampled.";
        HubReturnTransition transition = null;
        PlayerController arrival = null;
        HubWorldController world = null;
        CinemachineInputAxisController[] cameraInputs = null;
        AnimatorUpdateMode originalMode = default;
        AnimatorCullingMode originalCulling = default;
        bool coveredAtActivation = false, lockedAtActivation = false;
        void OnLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != HubScene) return;
            coveredAtActivation = transition != null && Mathf.Approximately(transition.CoverAlpha, 1f)
                && !transition.IsLoadingVisible;
            arrival = transition != null ? transition.ArrivingPlayer : null;
            world = Object.FindFirstObjectByType<HubWorldController>();
            cameraInputs = arrival != null ? CamerasTracking(arrival) : Array.Empty<CinemachineInputAxisController>();
            lockedAtActivation = world != null && world.IsModalOpen && Locked(arrival, cameraInputs);
            if (arrival != null && arrival.Animator != null)
            {
                originalMode = arrival.Animator.updateMode;
                originalCulling = arrival.Animator.cullingMode;
            }
        }

        SetLoader(scene =>
        {
            calls++;
            requestedFrame = Time.frameCount;
            target = scene;
            renderedLoadingError = LoadingLayoutError(transition, checkGlyphs: true);
            // The transition has subscribed by the time it requests the load.
            SceneManager.sceneLoaded += OnLoaded;
            return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
        });
        try
        {
            int clickFrame = Time.frameCount;
            overlay.ActionButton.onClick.Invoke();
            overlay.ActionButton.onClick.Invoke();
            run.ReturnToHub();
            transition = Object.FindFirstObjectByType<HubReturnTransition>();
            check(HubReturnTransition.IsActive && transition != null && transition.IsLoadingVisible && calls == 0
                && transition.LoadingHeading.text == "이동 중...", "The result action did not show loading before requesting the Hub.");
            check(transition.GetComponentInChildren<BossHealthHud>(true) == null
                && transition.GetComponentInChildren<BoundaryZeroRunOverlay>(true) == null,
                "The persistent loading Canvas retained unrelated result or boss HUD components.");
            string initialLoadingError = LoadingLayoutError(transition, checkGlyphs: false);
            Capture(captureDirectory, "hub-return-loading.png");

            await wait(() => SceneManager.GetActiveScene().name == HubScene,
                "The actual result action did not enter the authored Hub Scene.");
            check(calls == 1 && target == HubScene && requestedFrame > clickFrame,
                "The accepted return requested the wrong scene, started before a frame, or loaded twice.");
            check(initialLoadingError == null, "First loading frame layout: " + initialLoadingError);
            check(renderedLoadingError == null, "Rendered loading frame layout: " + renderedLoadingError);
            check(coveredAtActivation && lockedAtActivation,
                "The Hub was uncovered or controls were enabled at scene activation.");
            bool observedFade = false, lockedDuringFade = true;
            bool idlePreparedUnderCover = false, idleContinuedDuringFade = true;
            float firstIdle = -1f, lastIdle = -1f;
            await wait(() =>
            {
                if (transition == null || !HubReturnTransition.IsActive) return true;
                float idle = StagePresentationPlayModeRegression.RevealIdleTime(arrival);
                if (!transition.IsRevealingHub && Mathf.Approximately(transition.CoverAlpha, 1f))
                    idlePreparedUnderCover |= idle >= 0f;
                if (!transition.IsRevealingHub || transition.CoverAlpha <= 0f) return false;
                lockedDuringFade &= Locked(arrival, cameraInputs);
                if (transition.CoverAlpha < 1f)
                {
                    if (firstIdle < 0f) firstIdle = idle;
                    idleContinuedDuringFade &= idle > 0f && idle >= lastIdle;
                    lastIdle = idle;
                }
                if (!observedFade && transition.CoverAlpha < .95f)
                {
                    observedFade = true;
                    Capture(captureDirectory, "hub-return-fade-in.png");
                }
                return false;
            }, "The arrival overlay or input lock never completed.");
            check(observedFade && lockedDuringFade, "The Hub did not visibly fade in under a continuous control lock.");
            check(idlePreparedUnderCover && idleContinuedDuringFade && firstIdle > 0f && lastIdle > firstIdle + .01f,
                "The Hub began to fade in before Idle was evaluated, or the visible animation did not advance.");
            await wait(() => Object.FindFirstObjectByType<HubReturnTransition>() == null,
                "The completed transition left its temporary Canvas alive.");
            check(arrival != null && !arrival.IsStageTransitioning && arrival.Input.enabled && arrival.Movement.enabled
                && arrival.Controller.enabled && cameraInputs.Any(input => input != null && input.enabled)
                && world != null && !world.IsModalOpen && Object.FindFirstObjectByType<HubGateController>() != null
                && Time.timeScale > .99f, "The destination did not restore player, camera and world controls.");
            check(!arrival.IsStageIdleReady && arrival.Animator.isActiveAndEnabled
                && arrival.Animator.updateMode == originalMode && arrival.Animator.cullingMode == originalCulling,
                "The destination retained the reveal-only animator settings.");
            Capture(captureDirectory, "hub-return-arrived.png");
        }
        finally
        {
            SceneManager.sceneLoaded -= OnLoaded;
        }
    }

    private static string LoadingLayoutError(HubReturnTransition transition, bool checkGlyphs)
    {
        if (transition == null || !transition.IsLoadingVisible) return "The loading presentation is not active.";
        var details = transition.GetComponentsInChildren<TMP_Text>()
            .FirstOrDefault(label => label.name == "Details");
        var viewport = new Rect(-1f, -1f, Screen.width + 2f, Screen.height + 2f);
        var corners = new Vector3[4];
        foreach (var label in new[] { transition.LoadingHeading, details })
        {
            if (label == null || !label.isActiveAndEnabled || string.IsNullOrWhiteSpace(label.text))
                return "The loading heading or details are missing.";

            var transform = label.rectTransform;
            transform.GetWorldCorners(corners);
            Vector2 ToScreen(Vector3 world) => RectTransformUtility.WorldToScreenPoint(null, world);
            var bottomLeft = ToScreen(corners[0]);
            var topRight = ToScreen(corners[2]);
            if (topRight.x <= bottomLeft.x || topRight.y <= bottomLeft.y
                || corners.Any(corner => !viewport.Contains(ToScreen(corner))))
                return $"{label.name} has screen bounds {bottomLeft} to {topRight} at {Screen.width}x{Screen.height}.";

            if (!checkGlyphs) continue;
            int visible = 0;
            for (int i = 0; i < label.textInfo.characterCount; i++)
            {
                var character = label.textInfo.characterInfo[i];
                if (!character.isVisible) continue;
                visible++;
                if (!viewport.Contains(ToScreen(transform.TransformPoint(character.bottomLeft)))
                    || !viewport.Contains(ToScreen(transform.TransformPoint(character.topRight))))
                    return $"{label.name} has a glyph outside the loading viewport.";
            }
            if (visible == 0 || label.isTextTruncated)
                return $"{label.name} did not render its full loading text.";
        }
        return null;
    }

    private static CinemachineInputAxisController[] CamerasTracking(PlayerController player)
    {
        bool Tracks(Transform target) => target != null && (target == player.transform || target.IsChildOf(player.transform));
        return Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None)
            .Where(camera => camera.gameObject.scene == player.gameObject.scene
                && (Tracks(camera.Follow) || Tracks(camera.LookAt)))
            .SelectMany(camera => camera.GetComponents<CinemachineInputAxisController>())
            .ToArray();
    }

    private static bool Locked(PlayerController player, CinemachineInputAxisController[] cameraInputs)
        => player != null && player.IsStageTransitioning && !player.Input.enabled && !player.Movement.enabled
            && !player.Controller.enabled && cameraInputs != null && cameraInputs.Length != 0
            && cameraInputs.All(input => !input.enabled);

    private static void SetLoader(Func<string, AsyncOperation> loader) => LoaderOverride.SetValue(null, loader);

    private static void Capture(string directory, string name)
    {
        if (directory != null) ScreenCapture.CaptureScreenshot(Path.Combine(directory, name));
    }
}
