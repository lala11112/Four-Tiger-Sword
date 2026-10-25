using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using StudioBaekho.CombatLab;
using StudioBaekho.CombatLab.FourTigerSword;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit integration diagnostic only. Production Start still rejects unsaved scenes.
// This seeds the normal runner after its saved-scene gate, so Unity's own Play Mode
// snapshot preserves the currently open unsaved scene across all control/repeat runs.
// No scene is saved or opened. The report identifies this limited diagnostic mode.
public static class CombatLabRunnerPlaybackRegression
{
    [Serializable] private sealed class SeedState
    {
        public ExperimentRequest request;
        public string directory, phase = "between";
        public int candidateIndex = -1, repeatIndex;
        public float originalTimeScale, originalCaptureDeltaTime;
        public bool originalRunInBackground;
        public int originalCaptureFramerate;
    }
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    public static string Start(string sourceGuid = "ddfdcb1b3fb44274a8bbf37f6fe3a95b")
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || CombatLabRunner.IsRunning)
            throw new InvalidOperationException("Run this diagnostic from idle Edit Mode.");
        if (EditorSettings.enterPlayModeOptionsEnabled && EditorSettings.enterPlayModeOptions != EnterPlayModeOptions.None)
            throw new InvalidOperationException("Scene and domain reload must both remain enabled.");
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("An existing saved-path scene is required; current unsaved edits are preserved.");
        var dirtyScenes = new List<string>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var loaded = SceneManager.GetSceneAt(i);
            if (!loaded.IsValid() || string.IsNullOrEmpty(loaded.path))
                throw new InvalidOperationException("Every open scene must have an existing saved path. This diagnostic bypasses dirty-scene checks only.");
            if (loaded.isDirty) dirtyScenes.Add(loaded.path);
        }
        CombatCandidate Candidate(string id, float shake, float stop) => new CombatCandidate { id = id, label = id, origin = "manual",
            values = new List<ParameterValue> { new ParameterValue("step.0.camera.shake.translation.x", shake),
                new ParameterValue("step.0.camera.shake.rotation.z", .5f), new ParameterValue("step.0.camera.shake.duration", .12f),
                new ParameterValue("step.0.hitstop.duration", stop), new ParameterValue("step.0.hitstop.timeScale", 0),
                new ParameterValue("step.0.hitstop.scope", 3), new ParameterValue("step.0.hitstop.stacking", 2) } };
        var request = new ExperimentRequest { schemaVersion = 2, adapterId = FourTigerCombatAdapter.AdapterId,
            sourceGuid = sourceGuid, actionId = "normal-1", durationSeconds = 3, seed = 1701,
            recordVideo = true, precisionCapture = true, scenarioMode = "fixed", includeSourceControl = true, repeatCount = 3,
            candidates = new List<CombatCandidate> { Candidate("absolute-a", .05f, .06f), Candidate("absolute-b", .025f, .03f) } };
        CombatLab.Validate(request);
        string id = "runner-v2-diagnostic-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "CombatLabResults", id));
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Diagnostic output already exists: " + directory);
        Directory.CreateDirectory(directory);
        string sourcePath = AssetDatabase.GUIDToAssetPath(sourceGuid);
        var report = new ExperimentReport { schemaVersion = 2, request = request, runId = id,
            adapterId = request.adapterId, sourceGuid = sourceGuid, actionId = request.actionId,
            sourceDependencyHash = AssetDatabase.GetAssetDependencyHash(sourcePath).ToString(),
            scenePath = scene.path, unityVersion = Application.unityVersion, seed = request.seed, startedUtc = DateTime.UtcNow.ToString("O") };
        report.notes.Add("Explicit integration diagnostic: only the production Start saved-scene gate was bypassed. The original open scene was not saved. Unity's normal Play Mode snapshot restores its unsaved state between runs. The unchanged production runner executes original control plus two candidates repeated three times, with fresh scene/domain reload, seeding, preparation, recording and condition validation.");
        report.notes.Add("Dirty scenes at diagnostic entry: " + (dirtyScenes.Count == 0 ? "none" : string.Join(", ", dirtyScenes))
            + ". Scene dependency hashes describe saved assets; unsaved scene edits are retained by Unity's Play Mode snapshot rather than represented by those hashes.");
        File.WriteAllText(Path.Combine(directory, "request.json"), JsonUtility.ToJson(request, true));
        File.WriteAllText(Path.Combine(directory, "source-snapshot.json"), EditorJsonUtility.ToJson(AssetDatabase.LoadMainAssetAtPath(sourcePath), true));
        File.WriteAllText(Path.Combine(directory, "scene-dependency-hash.txt"), AssetDatabase.GetAssetDependencyHash(scene.path).ToString());
        var type = typeof(CombatLabRunner);
        type.GetMethod("SnapshotSourceContext", PrivateStatic).Invoke(null, new object[] { request, report, directory });
        var seed = new SeedState { request = request, directory = directory, originalTimeScale = Time.timeScale,
            originalRunInBackground = Application.runInBackground, originalCaptureFramerate = Time.captureFramerate,
            originalCaptureDeltaTime = Time.captureDeltaTime };
        var stateType = type.GetNestedType("RunState", BindingFlags.NonPublic);
        var reportField = type.GetField("report", PrivateStatic);
        var stateField = type.GetField("state", PrivateStatic);
        var saveReport = type.GetMethod("SaveReport", PrivateStatic);
        var enterNext = type.GetMethod("EnterNext", PrivateStatic);
        var finish = type.GetMethod("Finish", PrivateStatic);
        if (stateType == null || reportField == null || stateField == null || saveReport == null || enterNext == null || finish == null)
            throw new InvalidOperationException("Runner internals changed; update this explicit diagnostic before using it.");
        try
        {
            reportField.SetValue(null, report);
            stateField.SetValue(null, JsonUtility.FromJson(JsonUtility.ToJson(seed), stateType));
            SessionState.SetString("Baekho.CombatLab.LastReport", directory);
            saveReport.Invoke(null, null);
            enterNext.Invoke(null, null);
        }
        catch
        {
            // Preserve the runner until a queued Play transition can cancel normally.
            // If entry never began, Finish's finally clears both static and SessionState.
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) CombatLabRunner.Cancel();
                else finish.Invoke(null, null);
            }
            catch (Exception cleanupError) { Debug.LogException(cleanupError); }
            throw;
        }
        return directory;
    }
}
