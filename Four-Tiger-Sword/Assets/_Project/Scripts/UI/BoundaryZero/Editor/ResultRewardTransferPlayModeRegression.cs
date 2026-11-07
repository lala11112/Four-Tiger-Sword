using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Observes the real result coroutine on an unbound copy of the authored overlay.</summary>
public static class ResultRewardTransferPlayModeRegression
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/BoundaryZeroRunOverlay.prefab";
    private static string status = "Not started.";
    private static bool running;
    private static DateTime deadline;

    private sealed class Reveal
    {
        public bool Done;
    }

    public static string Status() => status;

    public static string Start(string captureDirectory = null)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before running the reward transfer regression.");
        if (running) return status;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException("The authored result overlay is missing.");
        if (captureDirectory != null)
        {
            captureDirectory = Path.GetFullPath(captureDirectory);
            if (!Directory.Exists(captureDirectory)) throw new DirectoryNotFoundException(captureDirectory);
        }
        running = true;
        deadline = DateTime.UtcNow.AddSeconds(45);
        status = "RUNNING: observing the authored reward transfer.";
        _ = Run(prefab, captureDirectory);
        return status;
    }

    private static async Task Run(GameObject prefab, string captureDirectory)
    {
        GameObject instance = null;
        try
        {
            instance = Object.Instantiate(prefab);
            instance.name = "Reward transfer regression overlay";
            instance.hideFlags = HideFlags.DontSave;
            instance.transform.localScale = Vector3.one;
            var overlay = instance.GetComponent<BoundaryZeroRunOverlay>();
            overlay.Title.text = "탐사 종료";
            overlay.ActionButton.GetComponentInChildren<TMP_Text>(true).text = "거점으로 돌아가기";
            var normal = Summary(125, 20, 1_420);
            using (var pause = CombatHitStopService.AcquireWorldScale(instance, 0f))
            {
                Check(pause.IsValid && Mathf.Approximately(Time.timeScale, 0f), "The fixture could not hold world time at zero.");
                await VerifyTransfer(overlay, normal, 1_275, "ordinary transfer at time scale zero", captureDirectory);
                Check(Mathf.Approximately(Time.timeScale, 0f), "The result changed the fixture's world pause.");
            }
            await VerifyTransfer(overlay, Summary(2_000_000_000, 100_000_000, int.MaxValue),
                47_483_647, "large valid rewards");
            await VerifySkipped(overlay, Summary(0, 0, 1_420), "zero rewards");
            await VerifySkipped(overlay, Summary(125, 20, null), "unavailable balance");
            await VerifySkipped(overlay, Summary(125, 20, 25), "balance smaller than rewards");
            await VerifySkipped(overlay, Summary(long.MaxValue, int.MaxValue, int.MaxValue), "overflowing reward sum");

            var interrupted = await Interrupt(overlay, normal, overlay.ShowLoading, "loading");
            await VerifyTransfer(overlay, Summary(84, 12, 900), 804, "fresh result after loading");
            Check(interrupted.Done, "The interrupted loading coroutine did not exit.");
            interrupted = await Interrupt(overlay, normal, overlay.ShowError, "error");
            await VerifyTransfer(overlay, normal, 1_275, "replayed result after error");
            Check(interrupted.Done, "The interrupted error coroutine did not exit.");
            status = "PASS: normal and large conserved transfers, four static edge cases, loading/error interruption, and fresh results. No progression account was used.";
        }
        catch (Exception failure)
        {
            status = "FAIL: " + failure.Message;
            Debug.LogException(failure);
        }
        finally
        {
            if (instance != null) Object.Destroy(instance);
            running = false;
        }
    }

    private static RunResultSummary Summary(long stage, int death, int? balance)
        => new RunResultSummary("1-2  |  지하 연구소", stage, death, 4, "강화석", balance);

    private static async Task VerifyTransfer(BoundaryZeroRunOverlay overlay, RunResultSummary summary, long openingBalance,
        string scenario, string captureDirectory = null)
    {
        status = "RUNNING: " + scenario;
        var initial = (Stage: summary.StageReward, Death: (long)summary.DeathReward, Balance: (long?)openingBalance);
        var target = (Stage: 0L, Death: 0L, Balance: (long?)summary.Balance);
        var reveal = Begin(overlay, summary, initial, scenario);
        var previous = initial;
        bool sawHold = false;
        bool sawPartialTransfer = false;
        bool capturedMidpoint = false;
        while (!reveal.Done)
        {
            var amount = Read(overlay);
            if (overlay.IsRewardTransferPlaying)
            {
                Check(sawHold && RowsVisible(overlay) && !overlay.ResultRevealCompleted,
                    scenario + ": the count started before the result could be read, or completed too early.");
                Check(amount.Stage >= 0 && amount.Stage <= previous.Stage
                    && amount.Death >= 0 && amount.Death <= previous.Death
                    && amount.Balance.HasValue && amount.Balance >= previous.Balance && amount.Balance <= summary.Balance,
                    scenario + ": a count reversed or exceeded its bounds.");
                Check(amount.Stage + amount.Death + amount.Balance == summary.Balance,
                    scenario + ": the transfer lost or duplicated currency mid-animation.");
                sawPartialTransfer |= amount.Stage > 0 && amount.Stage < initial.Stage
                    && amount.Death > 0 && amount.Death < initial.Death && amount.Balance > initial.Balance;
                if (!capturedMidpoint && sawPartialTransfer && amount.Stage <= initial.Stage / 2)
                {
                    capturedMidpoint = true;
                    Capture(captureDirectory, "reward-transfer-midpoint.png");
                }
            }
            else if (!overlay.ResultRevealCompleted)
            {
                Check(amount == initial, scenario + ": a count changed before the transfer phase.");
                if (!sawHold && RowsVisible(overlay))
                {
                    sawHold = true;
                    Capture(captureDirectory, "reward-transfer-before.png");
                }
            }
            previous = amount;
            await NextTick();
        }
        Check(sawPartialTransfer && overlay.ResultRevealCompleted && !overlay.IsRewardTransferPlaying && Read(overlay) == target,
            scenario + ": no intermediate transfer was visible or the final values were incorrect.");
        if (captureDirectory != null)
        {
            int capturedFrame = Time.frameCount;
            Capture(captureDirectory, "reward-transfer-after.png");
            while (Time.frameCount == capturedFrame) await NextTick();
        }
    }

    private static async Task VerifySkipped(BoundaryZeroRunOverlay overlay, RunResultSummary summary, string scenario)
    {
        status = "RUNNING: " + scenario;
        var expected = Snapshot(summary);
        var reveal = Begin(overlay, summary, expected, scenario);
        while (!reveal.Done)
        {
            Check(!overlay.IsRewardTransferPlaying && Read(overlay) == expected,
                scenario + ": an invalid or empty transfer changed the settled display.");
            await NextTick();
        }
        Check(overlay.ResultRevealCompleted && !overlay.IsRewardTransferPlaying && Read(overlay) == expected,
            scenario + ": the skipped transfer failed to retain the settled display.");
    }

    private static async Task<Reveal> Interrupt(BoundaryZeroRunOverlay overlay, RunResultSummary summary, Action changeMode, string mode)
    {
        status = "RUNNING: interrupting a transfer with " + mode;
        var reveal = Begin(overlay, summary, (summary.StageReward, summary.DeathReward, 1_275L), mode);
        while (!overlay.IsRewardTransferPlaying || Read(overlay).Stage == summary.StageReward)
        {
            Check(!reveal.Done, "No active transfer to interrupt with " + mode + ".");
            await NextTick();
        }
        changeMode();
        Check(!overlay.ResultSummaryRoot.activeSelf && overlay.Details.gameObject.activeSelf
            && !overlay.IsRewardTransferPlaying && !overlay.ResultRevealCompleted && Read(overlay) == Snapshot(summary),
            mode + ": interrupting the transfer left stale flags, counts or layout.");
        // The caller immediately starts another result while the cancelled iterator is still pending.
        return reveal;
    }

    private static Reveal Begin(BoundaryZeroRunOverlay overlay, RunResultSummary summary,
        (long Stage, long Death, long? Balance) prepared, string scenario)
    {
        overlay.ShowResult(false);
        overlay.SetResultSummary(summary);
        Check(Read(overlay) == Snapshot(summary), scenario + ": binding did not show the settled snapshot.");
        overlay.PrepareResultReveal();
        Check(!overlay.ResultRevealCompleted && !overlay.IsRewardTransferPlaying && Read(overlay) == prepared,
            scenario + ": preparing the reveal produced the wrong opening values.");
        overlay.ModalRoot.SetActive(true);
        var reveal = new Reveal();
        overlay.StartCoroutine(Observe(overlay.PlayResultReveal(), reveal));
        return reveal;
    }

    private static IEnumerator Observe(IEnumerator routine, Reveal reveal)
    {
        yield return routine;
        reveal.Done = true;
    }

    private static bool RowsVisible(BoundaryZeroRunOverlay overlay)
        => Opaque(overlay.ResultSummaryRoot) && Opaque(overlay.Title.gameObject) && Opaque(overlay.ActionButton.gameObject);

    private static bool Opaque(GameObject layer)
        => layer.TryGetComponent<CanvasGroup>(out var group) && Mathf.Approximately(group.alpha, 1f);

    private static (long Stage, long Death, long? Balance) Snapshot(RunResultSummary summary)
        => (summary.StageReward, summary.DeathReward, summary.Balance);

    private static (long Stage, long Death, long? Balance) Read(BoundaryZeroRunOverlay overlay)
    {
        var root = overlay.ResultSummaryRoot.transform;
        string Amount(string child) => root.Find(child).GetComponent<TMP_Text>().text;
        var balance = Amount("BalanceValue");
        return (Parse(Amount("StageRewardValue")), Parse(Amount("DeathRewardValue")),
            balance == "확인 필요" ? null : Parse(balance));
    }

    private static long Parse(string value)
        => long.Parse(value, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

    private static void Capture(string directory, string name)
    {
        if (directory != null) ScreenCapture.CaptureScreenshot(Path.Combine(directory, name));
    }

    private static Task NextTick()
    {
        if (!Application.isPlaying || DateTime.UtcNow >= deadline)
            throw new InvalidOperationException("Reward transfer verification timed out or left Play Mode.");
        var completion = new TaskCompletionSource<bool>();
        void Tick() { EditorApplication.update -= Tick; completion.SetResult(true); }
        EditorApplication.update += Tick;
        return completion.Task;
    }

    private static void Check(bool passed, string failure)
    {
        if (!passed) throw new InvalidOperationException(failure);
    }
}
