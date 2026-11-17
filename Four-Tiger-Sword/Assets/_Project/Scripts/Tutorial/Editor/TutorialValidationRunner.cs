#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Project-local validation mailbox. Only explicit requests under Temp can run these fixed checks.</summary>
[InitializeOnLoad]
public static class TutorialValidationRunner
{
    [Serializable] private sealed class Request { public string id; public string action; }
    private static double _nextPoll;
    private static readonly string Folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/TutorialValidation"));
    static TutorialValidationRunner()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += mode =>
        {
            if (mode == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Tutorial.ValidationBackground", false))
                Application.runInBackground = true;
            if (mode == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("Tutorial.ValidationBackground", false))
            {
                Application.runInBackground = SessionState.GetBool("Tutorial.ValidationOldBackground", false);
                SessionState.EraseBool("Tutorial.ValidationBackground");
            }
        };
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < _nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        _nextPoll = EditorApplication.timeSinceStartup + .5;
        string path = Path.Combine(Folder, "request.json");
        if (!File.Exists(path)) return;
        Request request;
        try { request = JsonUtility.FromJson<Request>(File.ReadAllText(path)); }
        catch { return; }
        if (request == null || string.IsNullOrEmpty(request.id) || request.id == SessionState.GetString("Tutorial.ValidationRequest", "")) return;
        SessionState.SetString("Tutorial.ValidationRequest", request.id);
        try
        {
            object result;
            switch (request.action)
            {
                case "refresh": AssetDatabase.Refresh(); result = "Asset refresh requested"; break;
                case "setup": result = TutorialSetup.Setup(); break;
                case "static":
                case "campaign":
                    if (EditorApplication.isPlaying) throw new InvalidOperationException("Run static regressions in Edit Mode.");
                    var checks = new List<object>();
                    var suite = request.action == "campaign" ? new Func<string>[] {
                        DungeonStageEncounterRegression.Run, RoomCombatEncounterRegression.Run, AirAttackLandingRegression.Run,
                        MapModalRegression.Run, ShopModalRegression.Run, RunItemChoiceModalRegression.Run,
                        PlayerInputModeRegression.Run, SupplyDepotEconomyRegression.Run }
                        : new Func<string>[] { FixedLayoutTests, TutorialCombatRegression.Run, PlayerUpgradeRegression.Run,
                        EngravingProgressionRegression.Run, RunItemChoiceRegression.Run, ShopTabsRegression.Run,
                        ShopTradeRegression.Run, ShopEconomyRegression.Run, DungeonMinimapModelRegression.Run, RunUpgradeRewardRegression.Run };
                    foreach (var test in suite)
                    {
                        try { checks.Add(new { name = test.Method.DeclaringType.Name, success = true, result = test() }); }
                        catch (Exception failure) { checks.Add(new { name = test.Method.DeclaringType.Name, success = false, result = failure.ToString() }); }
                    }
                    result = checks;
                    break;
                case "play": PrepareBackground(); result = TutorialPlayModeRegression.Start(); break;
                case "hub": PrepareBackground(); result = TutorialHubPlayModeRegression.Start(); break;
                case "slots": result = SaveSlotRegression.Run(); break;
                case "slot-play": PrepareBackground(); result = SaveSlotPlayModeRegression.Start(); break;
                case "capture":
                    Directory.CreateDirectory(Folder);
                    ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "tutorial.png"));
                    result = "Screenshot requested";
                    break;
                case "status":
                    var director = UnityEngine.Object.FindFirstObjectByType<TutorialDirector>();
                    result = new { project = Application.dataPath, scene = SceneManager.GetActiveScene().path,
                        playing = EditorApplication.isPlaying, frames = Time.frameCount,
                        test = TutorialPlayModeRegression.Status(), hubTest = TutorialHubPlayModeRegression.Status(),
                        slotTest = SaveSlotPlayModeRegression.Status(),
                        step = director != null ? director.Step.ToString() : null };
                    break;
                default: throw new InvalidOperationException("Unknown tutorial validation action.");
            }
            Write(request.id, true, result);
        }
        catch (Exception error) { Write(request.id, false, error.ToString()); Debug.LogException(error); }
    }

    private static void PrepareBackground()
    {
        SessionState.SetBool("Tutorial.ValidationOldBackground", Application.runInBackground);
        SessionState.SetBool("Tutorial.ValidationBackground", true);
        Application.runInBackground = true;
    }

    private static string FixedLayoutTests()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "StudioBaekho.Maps.Tests")
            ?? Assembly.Load("StudioBaekho.Maps.Tests");
        var type = assembly.GetType("StudioBaekho.Tests.BaekhoFixedDungeonLayoutTests", true);
        foreach (var test in new[] { ("FixedPathUsesShopAsPassageWithStableRoomIds", (object)0),
            ("FixedPathUsesShopAsPassageWithStableRoomIds", (object)431), ("ShopCannotReuseTheEntranceAsItsExit", (object)null),
            ("MisalignedSocketsDoNotProduceInvalidCorridorGeometry", (object)null) })
        {
            var fixture = Activator.CreateInstance(type);
            try
            {
                type.GetMethod("SetUp").Invoke(fixture, null);
                type.GetMethod(test.Item1).Invoke(fixture, test.Item2 != null ? new[] { test.Item2 } : null);
            }
            finally { type.GetMethod("TearDown").Invoke(fixture, null); }
        }
        return "PASS: four fixed-layout NUnit cases";
    }

    private static void Write(string id, bool success, object result)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "response.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new { id, success, result }));
    }
}
#endif
