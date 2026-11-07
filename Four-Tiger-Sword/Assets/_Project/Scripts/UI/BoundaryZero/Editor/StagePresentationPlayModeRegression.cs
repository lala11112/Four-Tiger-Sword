using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>Regenerates only the current live stage and isolates every progression write in memory.</summary>
public static class StagePresentationPlayModeRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    private static string status = "Not started.";
    private static bool running;
    private static int checks;
    private static DateTime deadline;

    public static string Status() => status;

    public static string Start()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter the authored Game Scene in Play Mode first.");
        if (running) return status;
        var run = UnityEngine.Object.FindFirstObjectByType<RunManager>();
        if (run == null || run.State != RunState.Playing || run.player == null || run.stageManager == null
            || !run.stageManager.IsReady || run.resultUI == null || run.IsShopOpen || run.IsMapOpen
            || run.IsUpgradeSaveBlocked || run.HasPendingUpgradeRewards || run.player.IsInRoomCombat
            || run.player.IsStageTransitioning || run.player.IsGameplayModalPaused || run.player.IsFinisherSelecting)
            throw new InvalidOperationException("Use a healthy idle run outside combat, with its first stage fully ready and no pending rewards.");
        running = true;
        checks = 0;
        deadline = DateTime.UtcNow.AddSeconds(30);
        status = "RUNNING: regenerating the current stage and checking its introduction.";
        _ = Run(run);
        return status;
    }

    private static async Task Run(RunManager run)
    {
        IDisposable accountScope = null;
        PlayerUpgradeService previousRunService = null;
        bool replacedRunService = false;
        Action started = null;
        int starts = 0;
        float idleAtStart = -1f;
        try
        {
            var original = PlayerUpgradeService.Instance;
            Check(original.IsReady && original.Settings != null, "The existing progression account is unavailable.");
            var profile = (PlayerUpgradeProfile)typeof(PlayerUpgradeService).GetField("_profile", Hidden).GetValue(original);
            var store = new MemoryStore(profile);
            var isolated = new PlayerUpgradeService(original.Settings, store);
            accountScope = PlayerUpgradeService.OverrideForTesting(isolated);
            previousRunService = Field<PlayerUpgradeService>(run, "_upgradeService");
            Set(run, "_upgradeService", isolated);
            replacedRunService = true;
            int chapter = run.CurrentChapterIndex, stage = run.CurrentStageIndex, seed = run.CurrentStageSeed;
            var animator = run.player.Animator;
            var originalMode = animator.updateMode;
            var originalCulling = animator.cullingMode;
            var wallet = run.player.GetComponent<PlayerWallet>();
            int gold = wallet != null ? wallet.Gold : 0;
            int items = run.player.ItemRuntime != null ? run.player.ItemRuntime.OwnedItems.Count : 0;
            started = () =>
            {
                starts++;
                idleAtStart = IdleTime(animator);
            };
            run.stageManager.OnStageStart += started;

            RegenerateCurrent(run, chapter, stage);
            await WaitForIntro(run);
            status = "RUNNING: observing animated Idle during the locked 1.8-second stage intro.";
            double began = Time.realtimeSinceStartupAsDouble;
            float lastIdle = await ObserveIntro(run, () => starts == 0);
            Check(Time.realtimeSinceStartupAsDouble - began >= 1.6d, "The intro completed before its authored duration.");
            Playing(run, chapter, stage, seed, starts, 1);
            CheckRestoredIdle(run.player, originalMode, originalCulling, lastIdle, idleAtStart);

            status = "RUNNING: cancelling a second intro to Error, then retrying the same stage.";
            RegenerateCurrent(run, chapter, stage);
            await WaitForIntro(run);
            Covered(run);
            Invoke(run, "ShowError", "Expected stage-introduction cancellation regression.");
            Check(run.State == RunState.Error && !run.resultUI.IsStageIntroPlaying
                && Field<Coroutine>(run, "_transition") == null && Time.timeScale > .99f,
                "Cancelling to Error retained the stage coroutine, black screen or world pause.");
            Check(!run.player.IsStageIdleReady && !animator.enabled && animator.updateMode == originalMode
                && animator.cullingMode == originalCulling && run.player.IsStageTransitioning && !run.player.Input.enabled,
                "Cancelling the intro did not restore the animator settings and retain the gameplay lock.");
            int afterCancellation = Time.frameCount + 3;
            await Wait(() => Time.frameCount >= afterCancellation);
            Check(run.State == RunState.Error && starts == 1, "A stale introduction resumed gameplay after cancellation.");
            run.RetryPendingStage();
            await WaitForIntro(run);
            lastIdle = await ObserveIntro(run, () => starts == 1);
            Playing(run, chapter, stage, seed, starts, 2);
            CheckRestoredIdle(run.player, originalMode, originalCulling, lastIdle, idleAtStart);
            Check((wallet == null || wallet.Gold == gold)
                && (run.player.ItemRuntime == null || run.player.ItemRuntime.OwnedItems.Count == items),
                "Regeneration or presentation changed retained gold/items.");
            Check(store.Writes == 0 && !run.HasPendingUpgradeRewards,
                "The presentation harness unexpectedly granted or persisted progression rewards.");
            status = $"PASSED: {checks} checks; Idle prepared under loading and animated with world time paused; "
                + "two stage starts, one cancelled intro, gameplay and animator settings restored, no save writes.";
        }
        catch (Exception error)
        {
            status = "FAILED: " + error.Message;
            Debug.LogException(error);
        }
        finally
        {
            if (run != null && run.stageManager != null && started != null) run.stageManager.OnStageStart -= started;
            if (run != null && replacedRunService) Set(run, "_upgradeService", previousRunService);
            accountScope?.Dispose();
            running = false;
        }
    }

    private static void RegenerateCurrent(RunManager run, int chapter, int stage)
    {
        Set(run, "_pendingChapter", chapter);
        Set(run, "_pendingStage", stage);
        Set(run, "_attempt", 0);
        Set(run, "_hasPending", true);
        Invoke(run, "BeginPending");
    }

    private static async Task WaitForIntro(RunManager run)
    {
        bool readyUnderLoading = false;
        await Wait(() =>
        {
            if (run == null || run.resultUI.IsStageIntroPlaying || run.State == RunState.Error) return true;
            if (!readyUnderLoading && run.player.IsStageIdleReady)
            {
                Check(run.resultUI.PresentationOverlay.ModalRoot.activeInHierarchy && RevealIdleTime(run.player) >= 0f,
                    "Idle was marked ready without evaluating the state behind the loading screen.");
                Covered(run);
                readyUnderLoading = true;
            }
            return false;
        });
        Check(run != null && run.State == RunState.Preparing && run.resultUI.IsStageIntroPlaying,
            "The regenerated stage did not enter its intro: " + (run != null ? run.LastError : "run destroyed"));
        Check(readyUnderLoading && RevealIdleTime(run.player) > 0f,
            "The first stage reveal frame did not have an already-playing Idle animation.");
    }

    private static async Task<float> ObserveIntro(RunManager run, Func<bool> expectedStarts)
    {
        float firstIdle = RevealIdleTime(run.player);
        float lastIdle = firstIdle;
        while (run != null && run.resultUI.IsStageIntroPlaying)
        {
            Covered(run);
            Check(expectedStarts(), "OnStageStart was published before the introduction completed.");
            float currentIdle = RevealIdleTime(run.player);
            Check(currentIdle >= lastIdle, "The visible Idle state restarted or stopped during the stage intro.");
            lastIdle = currentIdle;
            await WaitOneTick();
        }
        Check(lastIdle > firstIdle + .01f, "Idle did not advance while world time was paused.");
        await Wait(() => run == null || run.State != RunState.Preparing);
        return lastIdle;
    }

    internal static float RevealIdleTime(PlayerController player)
    {
        var animator = player != null ? player.Animator : null;
        if (animator == null || !player.IsStageIdleReady || animator.updateMode != AnimatorUpdateMode.UnscaledTime
            || animator.cullingMode != AnimatorCullingMode.AlwaysAnimate || animator.applyRootMotion
            || !(player.StateMachine.CurrentState is PlayerIdleState)) return -1f;
        return IdleTime(animator);
    }

    private static float IdleTime(Animator animator)
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.layerCount == 0 || animator.IsInTransition(0)) return -1f;
        var state = animator.GetCurrentAnimatorStateInfo(0);
        return state.shortNameHash == IdleHash ? state.normalizedTime : -1f;
    }

    private static void CheckRestoredIdle(PlayerController player, AnimatorUpdateMode originalMode,
        AnimatorCullingMode originalCulling, float beforeRelease, float onStageStart)
    {
        Check(!player.IsStageIdleReady && player.Animator.isActiveAndEnabled && player.Animator.updateMode == originalMode
            && player.Animator.cullingMode == originalCulling && onStageStart >= beforeRelease,
            "Starting gameplay reset Idle or left the reveal-only animator settings enabled.");
    }

    private static void Covered(RunManager run)
    {
        var player = run.player;
        Check(run.State == RunState.Preparing && player.IsStageTransitioning
            && !player.Input.enabled && !player.Movement.enabled && !player.Controller.enabled
            && Mathf.Approximately(Time.timeScale, 0f), "Gameplay/input/world time escaped during the intro.");
        Check(Vector3.Distance(player.transform.position, run.stageManager.PlayerSpawnPosition) < .01f
            && Quaternion.Angle(player.transform.rotation, run.stageManager.PlayerSpawnRotation) < .1f,
            "The covered player was not placed at the new stage spawn.");
    }

    private static void Playing(RunManager run, int chapter, int stage, int seed, int starts, int expectedStarts)
    {
        Check(run != null && run.State == RunState.Playing && run.stageManager.IsReady && !run.player.IsStageTransitioning
            && run.player.Input.enabled && run.player.Movement.enabled && run.player.Controller.enabled
            && Time.timeScale > .99f && !run.resultUI.IsStageIntroPlaying && starts == expectedStarts,
            "The completed intro did not resume gameplay and publish exactly one stage start.");
        Check(run.CurrentChapterIndex == chapter && run.CurrentStageIndex == stage && run.CurrentStageSeed == seed,
            "The presentation harness advanced progression or changed the stable stage seed.");
    }

    private static async Task Wait(Func<bool> condition)
    {
        while (!condition()) await WaitOneTick();
    }

    private static Task WaitOneTick()
    {
        if (!Application.isPlaying || DateTime.UtcNow >= deadline)
            throw new InvalidOperationException("The stage presentation acceptance exceeded 30 seconds or left Play Mode.");
        var completion = new TaskCompletionSource<bool>();
        void Next() { EditorApplication.update -= Next; completion.SetResult(true); }
        EditorApplication.update += Next;
        return completion.Task;
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Hidden).Invoke(target, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }

    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        private PlayerUpgradeProfile profile;
        public int Writes;
        public MemoryStore(PlayerUpgradeProfile source) => profile = source.Copy();
        public bool TryLoad(out PlayerUpgradeProfile value, out string error)
        { value = profile.Copy(); error = null; return true; }
        public bool TrySave(PlayerUpgradeProfile value, out string error)
        { profile = value.Copy(); Writes++; error = null; return true; }
    }
}
