using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Runs EnemyBrainRegression end to end in an empty scene (so live game code cannot unload the
/// fixture), then restores the open scenes. The result goes to Temp/EnemyBrainRegression.result.json.
/// Triggered from the menu or by creating Temp/EnemyBrainRegression.request.
/// </summary>
[InitializeOnLoad]
public static class EnemyBrainRegressionRunner
{
    private const string RequestPath = "Temp/EnemyBrainRegression.request";
    private const string ResultPath = "Temp/EnemyBrainRegression.result.json";
    private const string StageKey = "EnemyBrainRegressionRunner.Stage";
    // A file survives domain reloads and editor restarts, so an interrupted run still restores the scenes.
    private const string ScenesPath = "Temp/EnemyBrainRegression.scenes.json";
    private static double _startAt;
    private static bool _started;

    [Serializable] private sealed class Failure { public string status; }
    [Serializable] private sealed class SavedScene { public string path; public bool isLoaded, isActive, isSubScene; }
    [Serializable] private sealed class SavedScenes { public SavedScene[] scenes; }

    static EnemyBrainRegressionRunner()
    {
        // Restore before handling a new request, which would otherwise save the temporary empty scene.
        EditorApplication.delayCall += RestoreScenes;
        EditorApplication.delayCall += Resume;
        // A request can land while the Editor is compiling; keep checking for it once a second.
        EditorApplication.update += PollRequest;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) Resume();
            else if (state == PlayModeStateChange.EnteredEditMode) OnEditMode();
        };
    }

    private static double _nextPoll;

    private static void PollRequest()
    {
        if (EditorApplication.timeSinceStartup < _nextPoll) return;
        _nextPoll = EditorApplication.timeSinceStartup + 1d;
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
        // The post-Play restore can be skipped while the Editor is still leaving Play Mode; retry it here.
        if (File.Exists(ScenesPath)) { RestoreScenes(); return; }
        if (File.Exists(RequestPath)) Resume();
    }

    [MenuItem("Tools/Regression/Run Enemy Brain Regression")]
    public static void Run()
    {
        File.WriteAllText(RequestPath, DateTime.Now.ToString("O"));
        Resume();
    }

    private static void Resume()
    {
        string stage = SessionState.GetString(StageKey, "");
        if (!Application.isPlaying)
        {
            if (stage == "running") { Finish(Fail("Play Mode exited before the regression finished")); return; }
            if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            // Import edits made outside the Editor first; an unfocused Editor does not auto-refresh.
            // If that starts a compile, the request stays and runs after the domain reload.
            AssetDatabase.Refresh();
            if (EditorApplication.isCompiling) return;
            File.Delete(RequestPath);
            if (File.Exists(ResultPath)) File.Delete(ResultPath);
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                {
                    WriteResult(Fail("open scenes have unsaved changes; save them and run again"));
                    return;
                }
            var current = EditorSceneManager.GetSceneManagerSetup();
            var saved = new SavedScene[current.Length];
            for (int i = 0; i < current.Length; i++)
                saved[i] = new SavedScene { path = current[i].path, isLoaded = current[i].isLoaded,
                    isActive = current[i].isActive, isSubScene = current[i].isSubScene };
            File.WriteAllText(ScenesPath, JsonUtility.ToJson(new SavedScenes { scenes = saved }));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetString(StageKey, "entering");
            EditorApplication.EnterPlaymode();
            return;
        }
        if (stage != "entering") return;
        SessionState.SetString(StageKey, "running");
        _started = false;
        _startAt = EditorApplication.timeSinceStartup + .5d;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (!Application.isPlaying)
        {
            EditorApplication.update -= Tick;
            if (SessionState.GetString(StageKey, "") == "running")
                Finish(Fail("Play Mode exited before the regression finished"));
            return;
        }
        if (!_started)
        {
            if (EditorApplication.timeSinceStartup < _startAt || EditorApplication.isCompiling) return;
            _started = true;
            try { EnemyBrainRegression.StartPlayMode(); }
            catch (Exception exception)
            {
                EditorApplication.update -= Tick;
                Finish(Fail("could not start: " + exception.Message));
                return;
            }
        }
        string status = EnemyBrainRegression.PlayModeStatus();
        if (status.Contains("\"running\"")) return;
        EditorApplication.update -= Tick;
        Finish(status);
    }

    private static string Fail(string message) => JsonUtility.ToJson(new Failure { status = "failed: " + message });

    private static void Finish(string json)
    {
        SessionState.EraseString(StageKey);
        WriteResult(json);
        if (Application.isPlaying) EditorApplication.ExitPlaymode();
        else OnEditMode();
    }

    private static void WriteResult(string json)
    {
        File.WriteAllText(ResultPath, json);
        Debug.Log("[EnemyBrainRegressionRunner] " + json);
    }

    // Unity reloads its pre-Play backup scene after EnteredEditMode, so restore on a later editor tick.
    private static void OnEditMode() => EditorApplication.delayCall += RestoreScenes;

    private static void RestoreScenes()
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode
            || SessionState.GetString(StageKey, "") != "" || !File.Exists(ScenesPath)) return;
        string saved = File.ReadAllText(ScenesPath);
        File.Delete(ScenesPath);
        var scenes = JsonUtility.FromJson<SavedScenes>(saved)?.scenes;
        if (scenes == null || scenes.Length == 0) return;
        var setup = new SceneSetup[scenes.Length];
        for (int i = 0; i < scenes.Length; i++)
            setup[i] = new SceneSetup { path = scenes[i].path, isLoaded = scenes[i].isLoaded,
                isActive = scenes[i].isActive, isSubScene = scenes[i].isSubScene };
        EditorSceneManager.RestoreSceneManagerSetup(setup);
    }
}
