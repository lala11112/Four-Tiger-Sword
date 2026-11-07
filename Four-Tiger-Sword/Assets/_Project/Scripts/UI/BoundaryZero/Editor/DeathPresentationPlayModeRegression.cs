using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>Exercises lethal damage in the authored Game Scene with all reward writes isolated in memory.</summary>
public static class DeathPresentationPlayModeRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly int Die = Animator.StringToHash("Die");
    private static string status = "Not started.";
    private static bool running;
    private static int checks;
    private static DateTime deadline;

    public static string Status() => status;

    public static string Start(bool failSave = false, bool cancel = false, string captureDirectory = null,
        bool returnToHub = false)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter the authored Game Scene in Play Mode first.");
        if (running) return status;
        if (cancel && (failSave || returnToHub))
            throw new ArgumentException("Run cancellation separately from save recovery or returning to the Hub.");
        if (captureDirectory != null)
        {
            captureDirectory = Path.GetFullPath(captureDirectory);
            if (!Directory.Exists(captureDirectory)) throw new DirectoryNotFoundException(captureDirectory);
        }
        var run = UnityEngine.Object.FindFirstObjectByType<RunManager>();
        var overlay = UnityEngine.Object.FindFirstObjectByType<BoundaryZeroRunOverlay>();
        if (run == null || run.State != RunState.Playing || run.player == null || run.stageManager == null
            || !run.stageManager.IsReady || run.resultUI == null || overlay == null || overlay.ModalRoot.activeInHierarchy
            || run.IsShopOpen || run.IsMapOpen || run.IsUpgradeSaveBlocked || run.HasPendingUpgradeRewards
            || run.player.IsInRoomCombat || run.player.IsStageTransitioning || run.player.IsGameplayModalPaused
            || run.player.IsFinisherSelecting || run.player.StatManager.CurrentHp <= 0f)
            throw new InvalidOperationException("Use a healthy idle run outside combat, with its first stage fully ready and no pending rewards.");
        running = true;
        checks = 0;
        deadline = DateTime.UtcNow.AddSeconds(returnToHub ? 60 : 30);
        status = "RUNNING: applying lethal damage with an isolated progression account.";
        _ = Run(run, overlay, failSave, cancel, captureDirectory, returnToHub);
        return status;
    }

    private static async Task Run(RunManager run, BoundaryZeroRunOverlay overlay, bool failSave, bool cancel,
        string captureDirectory, bool returnToHub)
    {
        IDisposable accountScope = null;
        var runServiceField = typeof(RunManager).GetField("_upgradeService", Hidden);
        PlayerUpgradeService previousRunService = null;
        bool replacedRunService = false;
        bool appliedLethalDamage = false;
        try
        {
            var original = PlayerUpgradeService.Instance;
            Check(original.IsReady && original.Settings != null, "The progression account is unavailable.");
            var profile = (PlayerUpgradeProfile)typeof(PlayerUpgradeService).GetField("_profile", Hidden).GetValue(original);
            var store = new MemoryStore(profile) { RejectWrites = failSave };
            var isolated = new PlayerUpgradeService(original.Settings, store);
            accountScope = PlayerUpgradeService.OverrideForTesting(isolated);
            previousRunService = (PlayerUpgradeService)runServiceField.GetValue(run);
            runServiceField.SetValue(run, isolated);
            replacedRunService = true;

            var player = run.player;
            var result = run.resultUI;
            int balance = isolated.Balance;
            int expectedReward = Math.Min(isolated.Settings.deathRewardCap,
                run.ClearedCombatRoomCount * isolated.Settings.deathRewardPerRoom);
            appliedLethalDamage = true;
            player.StatManager.TakeDamage(float.MaxValue, ElementType.ELEMENT_NONE, false);
            await Wait(() => run.State == RunState.Lost, "Lethal damage did not terminate the run.");
            var fade = result.transform.Find("DeathFadeCanvas")?.GetComponent<CanvasGroup>();
            Check(run.IsDeathPresentationPlaying && !overlay.ModalRoot.activeInHierarchy
                && fade != null && fade.isActiveAndEnabled && Mathf.Approximately(fade.alpha, 0f),
                "The result or black screen appeared as soon as the player died.");
            Check(player.IsStageTransitioning && !player.Input.enabled && !player.Movement.enabled
                && !player.Controller.enabled && !run.stageManager.IsReady, "Gameplay remained active during the death sequence.");
            Check(store.Attempts == 1 && (failSave
                ? run.IsUpgradeSaveBlocked && run.HasPendingUpgradeRewards && store.Writes == 0 && isolated.Balance == balance
                : !run.IsUpgradeSaveBlocked && !run.HasPendingUpgradeRewards && store.Writes == 1 && isolated.Balance == balance + expectedReward),
                "The terminal run did not immediately attempt to settle its isolated death reward.");
            if (failSave) Check(Mathf.Approximately(Time.timeScale, 0f), "The failed save did not hold its world pause.");

            status = "RUNNING: observing the authored Die animation before the screen fade.";
            await Wait(() => IsDie(player.Animator) || result.IsDeathFadePlaying || overlay.ModalRoot.activeInHierarchy,
                "The authored death animation never entered its Die state.");
            Check(IsDie(player.Animator) && player.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f
                && !result.IsDeathFadePlaying && !overlay.ModalRoot.activeInHierarchy,
                "The animation did not play visibly before the fade and result.");

            if (cancel)
            {
                status = "RUNNING: disabling the run during the death animation and checking cancellation.";
                run.enabled = false;
                double checkAt = Time.realtimeSinceStartupAsDouble + 3d;
                await Wait(() => Time.realtimeSinceStartupAsDouble >= checkAt, "The player loop stopped during cancellation.");
                Check(!run.IsDeathPresentationPlaying && !result.IsDeathFadePlaying && !result.DeathFadeCompleted
                    && !fade.gameObject.activeInHierarchy && !overlay.ModalRoot.activeInHierarchy,
                    "A cancelled death sequence left a stale fade or opened a stale result.");
            }
            else
            {
                if (captureDirectory != null)
                {
                    await Wait(() => player.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= .5f
                        || result.IsDeathFadePlaying || overlay.ModalRoot.activeInHierarchy,
                        "The animation did not progress to the capture point.");
                    if (!result.IsDeathFadePlaying && !overlay.ModalRoot.activeInHierarchy)
                        ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory, "death-animation.png"));
                }
                await Wait(() => result.IsDeathFadePlaying || overlay.ModalRoot.activeInHierarchy,
                    "The death animation did not lead to a screen fade.");
                Check(result.IsDeathFadePlaying && !overlay.ModalRoot.activeInHierarchy && IsDie(player.Animator)
                    && player.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f,
                    "The screen did not start fading after the death animation finished and before the result.");
                if (captureDirectory != null)
                {
                    await Wait(() => fade.alpha >= .45f || overlay.ModalRoot.activeInHierarchy,
                        "The fade did not progress to the capture point.");
                    if (fade.alpha < 1f && !overlay.ModalRoot.activeInHierarchy)
                        ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory, "death-fade.png"));
                }
                await Wait(() => overlay.ModalRoot.activeInHierarchy, "The result did not appear after the fade.");
                Check(!run.IsDeathPresentationPlaying && !result.IsDeathFadePlaying && result.DeathFadeCompleted
                    && fade.isActiveAndEnabled && Mathf.Approximately(fade.alpha, 1f),
                    "The result appeared before the world had fully faded to black.");

                if (failSave)
                {
                    status = "RUNNING: retrying the deferred save error after the fade.";
                    Check(overlay.Title.text == "보상 저장 실패" && overlay.ActionButton.IsInteractable(),
                        "The blocked death reward did not show its retry action after fading.");
                    if (returnToHub)
                    {
                        run.ReturnToHub();
                        Check(!run.IsReturningToHub && !HubReturnTransition.IsActive && run.HasPendingUpgradeRewards,
                            "A return to the Hub bypassed the failed-save guard.");
                    }
                    store.RejectWrites = false;
                    overlay.ActionButton.onClick.Invoke();
                    Check(!run.IsUpgradeSaveBlocked && !run.HasPendingUpgradeRewards && store.Attempts == 2 && store.Writes == 1
                        && run.DeathUpgradeRewardTotal == expectedReward && isolated.Balance == balance + expectedReward
                        && Time.timeScale > .99f, "Retry did not settle the death reward once and release the world pause.");
                }

                await Wait(() => overlay.ActionButton.IsInteractable(), "The defeat result action did not finish revealing.");
                Check(overlay.Title.text == "탐사 종료" && result.DeathFadeCompleted && store.Writes == 1
                    && overlay.ResultSummaryRoot.activeInHierarchy
                    && overlay.ActionButton.GetComponentInChildren<TMPro.TMP_Text>(true).text == "거점으로 돌아가기",
                    "The final defeat UI or its saved reward was missing.");
                if (captureDirectory != null)
                {
                    int captureFrame = Time.frameCount;
                    ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory, "death-result.png"));
                    if (returnToHub)
                        await Wait(() => Time.frameCount > captureFrame, "The result capture frame did not finish.");
                }
                if (returnToHub)
                {
                    await HubReturnPlayModeRegression.Verify(run, overlay, Wait, Check, phase => status = phase,
                        captureDirectory);
                    Check(ReferenceEquals(PlayerUpgradeService.Instance, isolated) && isolated.Balance == balance + expectedReward
                        && store.Writes == 1, "The Hub load changed the isolated reward or paid it again.");
                }
            }

            string scenario = returnToHub ? "defeat, Hub load failure/retry, arrival fade and input restoration"
                : cancel ? "death cancellation" : failSave ? "death and save retry" : "death animation, fade and defeat result";
            status = $"PASSED: {checks} checks; {scenario}; progression writes isolated in memory.";
        }
        catch (Exception error)
        {
            status = "FAILED: " + error.Message;
            Debug.LogException(error);
        }
        finally
        {
            if (run != null && replacedRunService)
            {
                // Never let a receipt from a failed acceptance escape into the real save.
                if (run.HasPendingUpgradeRewards)
                    ((IList)typeof(RunManager).GetField("_pendingUpgradeRewards", Hidden).GetValue(run)).Clear();
                if (appliedLethalDamage && run.State != RunState.Lost && run.player != null) run.player.enabled = false;
                runServiceField.SetValue(run, previousRunService);
            }
            accountScope?.Dispose();
            running = false;
        }
    }

    private static bool IsDie(Animator animator) => animator != null && animator.isActiveAndEnabled
        && animator.layerCount > 0 && animator.GetCurrentAnimatorStateInfo(0).shortNameHash == Die;

    private static async Task Wait(Func<bool> condition, string failure)
    {
        while (!condition())
        {
            if (!Application.isPlaying || DateTime.UtcNow >= deadline) throw new InvalidOperationException(failure);
            var completion = new TaskCompletionSource<bool>();
            void Next() { EditorApplication.update -= Next; completion.SetResult(true); }
            EditorApplication.update += Next;
            await completion.Task;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }

    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        private PlayerUpgradeProfile profile;
        public bool RejectWrites;
        public int Attempts;
        public int Writes;

        public MemoryStore(PlayerUpgradeProfile source) => profile = source.Copy();

        public bool TryLoad(out PlayerUpgradeProfile value, out string error)
        { value = profile.Copy(); error = null; return true; }

        public bool TrySave(PlayerUpgradeProfile value, out string error)
        {
            Attempts++;
            if (RejectWrites) { error = "Expected death presentation save failure."; return false; }
            profile = value.Copy();
            Writes++;
            error = null;
            return true;
        }
    }
}
