using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StudioBaekho.CombatLab;
using UnityEditor;
using UnityEngine;

// Invoke explicitly in Play Mode. The marker is synthetic: this tests capture timing,
// not combat correctness. A session-level single-hit regression must test the real marker.
public static class CombatLabPrecisionCaptureRegression
{
    public static async Task<string> RunIncompleteWindows()
    {
        Require(Application.isPlaying, "Run capture checks in Play Mode.");
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "CombatLabResults",
            "precision-incomplete-check-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
        using (var missing = new CombatLabRecording())
        {
            missing.Start(Path.Combine(directory, "missing-hit"), true);
            await Task.Delay(350);
            missing.Stop();
            Require(missing.CapturedFrameCount > 0 && string.IsNullOrEmpty(missing.Error), "Missing-hit case needs actual render samples.");
            Require(missing.Capture.precisionStatus == "missing-hit" && missing.Capture.frames.Count == 0,
                "A recording with no confirmed hit must not claim a precision window.");
        }
        using (var shortWindow = new CombatLabRecording())
        {
            shortWindow.Start(Path.Combine(directory, "missing-before"), true);
            shortWindow.MarkImpact(new CombatEvent { kind = "confirmed-impact", eventId = "missing-before-marker",
                unityFrameCount = Time.frameCount, unityRealtimeUs = Time.realtimeSinceStartupAsDouble * 1000000d });
            await Task.Delay(850);
            shortWindow.Stop();
            Require(shortWindow.CapturedFrameCount > 0 && string.IsNullOrEmpty(shortWindow.Error), "Short-window case needs actual render samples.");
            Require(shortWindow.Capture.precisionStatus == "incomplete" && shortWindow.Capture.frames.Count > 0
                && shortWindow.Capture.frames.Count <= 48, "Absent pre-hit frames must be marked incomplete, not fabricated.");
            Require(shortWindow.Capture.frames.TrueForAll(frame => frame.relativeTimeSeconds >= 0
                && frame.kind == "captured" && File.Exists(Path.Combine(directory, frame.imagePath))),
                "Only actually captured post-hit PNGs may be retained.");
        }
        return directory + "\nActual missing-hit and missing-pre-hit capture cases passed.";
    }

    public static async Task<string> Run()
    {
        Require(Application.isPlaying, "Run precision capture regression in Play Mode.");
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "CombatLabResults",
            "precision-capture-check-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"), "samples"));
        float originalScale = Time.timeScale;
        var recording = new CombatLabRecording();
        try
        {
            recording.Start(path, true);
            double readyAt = Time.realtimeSinceStartupAsDouble;
            while ((recording.TimelineSeconds < 0.25f || recording.CapturedFrameCount < 2)
                && Time.realtimeSinceStartupAsDouble - readyAt < 5d) await Task.Delay(25);
            Require(recording.CapturedFrameCount >= 2, "Game View produced no warmup samples.");
            var impact = new CombatEvent { kind = "confirmed-impact", eventId = "precision-regression-hit",
                unityFrameCount = Time.frameCount, unityRealtimeUs = Time.realtimeSinceStartupAsDouble * 1000000d };
            recording.MarkImpact(impact);
            Time.timeScale = 0f;
            double stoppedAt = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - stoppedAt < 0.85d) await Task.Delay(25);
            recording.Stop();
            Require(string.IsNullOrEmpty(recording.Error), "Recorder error: " + recording.Error);
            var capture = recording.Capture;
            Require(capture.precisionStatus == "complete", "Expected a complete captured window, got " + capture.precisionStatus);
            Require(capture.hitEventId == impact.eventId && capture.hitRealtimeUs == impact.unityRealtimeUs,
                "Precise impact identity/time was changed.");
            Require(capture.frames.Count > 1 && capture.frames.Count <= 48, "Precision window must retain 1..48 actual samples.");
            Require(capture.frames.All(frame => frame.kind == "captured" && frame.relativeTimeSeconds >= -0.15001f
                && frame.relativeTimeSeconds <= 0.60001f && !string.IsNullOrEmpty(frame.imagePath)),
                "Window contains a terminal hold, a missing PNG path, or a sample outside the actual requested times.");
            string directory = Path.GetDirectoryName(recording.OutputPath);
            Require(capture.frames.All(frame => File.Exists(Path.Combine(directory, frame.imagePath))), "A retained sample PNG is missing.");
            string sidecarPath = Path.Combine(directory, capture.framesPath);
            Require(File.Exists(sidecarPath), "All-frame timestamp sidecar is missing.");
            var all = JsonUtility.FromJson<CaptureMetadata>(File.ReadAllText(sidecarPath));
            var actual = all.frames.Where(frame => frame.kind == "captured").ToArray();
            Require(actual.Length == recording.CapturedFrameCount, "Synthetic hold was counted as a real rendered sample.");
            Require(actual[0].mediaPtsUs == 0 && actual[0].captureElapsedUs >= 0,
                "First media PTS must be zero without rewriting the real capture elapsed time.");
            for (int i = 1; i < actual.Length; i++)
            {
                Require(actual[i].captureIndex == actual[i - 1].captureIndex + 1,
                    "Actual capture indices must advance one captured sample at a time.");
                Require(actual[i].mediaPtsUs > actual[i - 1].mediaPtsUs
                    && actual[i].unityRealtimeUs >= actual[i - 1].unityRealtimeUs,
                    "Actual timestamps must remain monotonic while game time is stopped.");
            }
            Require(all.frames.Where(frame => frame.kind == "terminalHold").All(frame => string.IsNullOrEmpty(frame.imagePath)),
                "The final encoded hold must never masquerade as a new precision image.");
            Require(actual.Count(frame => frame.unityRealtimeUs >= impact.unityRealtimeUs) >= 2,
                "Global timeScale=0 prevented actual frame capture.");
            return recording.OutputPath + "\n" + capture.frames.Count + " retained samples; " + actual.Length
                + " actual frames; median interval " + capture.medianIntervalUs + "us.";
        }
        finally { recording.Dispose(); Time.timeScale = originalScale; }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
