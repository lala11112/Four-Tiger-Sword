using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Drives the title slot picker widgets and scene routing with a private save directory.</summary>
[InitializeOnLoad]
public static class SaveSlotPlayModeRegression
{
    private const string TitlePath = "Assets/_Project/Scenes/TitleScene.unity";
    private const string ArmedKey = "SaveSlot.PlayModeRegression.Armed";
    private const string StatusKey = "SaveSlot.PlayModeRegression.Status";
    private const string StartSceneKey = "SaveSlot.PlayModeRegression.StartScene";
    private const string RootKey = "SaveSlot.PlayModeRegression.Root";
    private static IDisposable _slotScope;

    static SaveSlotPlayModeRegression() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    [MenuItem("Tools/Studio Baekho/Progression/Run Save Slot Play Mode Regression")]
    private static void StartFromMenu() => Debug.Log(Start());

    public static string Start()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Run the slot picker regression from Edit Mode.");
        var title = AssetDatabase.LoadAssetAtPath<SceneAsset>(TitlePath);
        Check(title != null && AssetDatabase.LoadAssetAtPath<SceneAsset>(TutorialSetup.ScenePath) != null,
            "The authored title and tutorial scenes must be installed.");
        SessionState.SetString(StartSceneKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) ?? string.Empty);
        SessionState.SetBool(ArmedKey, true);
        Phase("opening the title with an isolated save directory");
        EditorSceneManager.playModeStartScene = title;
        EditorApplication.EnterPlaymode();
        return Status();
    }

    public static string Status() => SessionState.GetString(StatusKey, "NOT STARTED");

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(ArmedKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode) _ = Verify();
        if (change != PlayModeStateChange.EnteredEditMode) return;
        _slotScope?.Dispose();
        _slotScope = null;
        SaveSlotRegression.DeleteTemporaryRoot(SessionState.GetString(RootKey, string.Empty));
        SessionState.EraseString(RootKey);
        var start = SessionState.GetString(StartSceneKey, string.Empty);
        EditorSceneManager.playModeStartScene = start.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        SessionState.EraseString(StartSceneKey);
        SessionState.EraseBool(ArmedKey);
        if (Status().StartsWith("RUNNING", StringComparison.Ordinal))
            SessionState.SetString(StatusKey, "FAIL: slot picker regression was interrupted.");
    }

    private static async Task Verify()
    {
        FieldInfo loaderField = null;
        object originalLoader = null;
        try
        {
            Check(SceneManager.GetActiveScene().path == TitlePath, "Play Mode did not start from the title.");
            string root = SaveSlotRegression.CreateTemporaryRoot();
            SessionState.SetString(RootKey, root);
            var settings = Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings");
            var slots = ReopenSlots(root, settings);
            Check(slots.TryRefresh(out string error) && slots.Slots.Count == 0, "The isolated catalog was not empty: " + error);

            Phase("rendering five empty slots and a name field");
            var title = await WaitForTitle();
            Check(title.ContinueButton != null && title.ContinueButton.gameObject.activeInHierarchy
                && !Ready(title.ContinueButton), "Continue was missing or enabled with no last-played slot.");
            await Capture("slots-00-title");
            var picker = await OpenPicker(title);
            for (int number = 1; number <= 5; number++)
                Check(picker.GetPrimaryButton(number)?.IsInteractable() == true
                    && (picker.GetDeleteButton(number) == null || !picker.GetDeleteButton(number).gameObject.activeInHierarchy),
                    "An empty row was missing or offered deletion.");
            await Capture("slots-01-empty");
            Click(picker.GetPrimaryButton(1));
            await Wait(() => picker.NameInput != null && picker.NameInput.gameObject.activeInHierarchy
                && Ready(picker.ConfirmButton), "Clicking an empty row did not show the name field.");
            picker.NameInput.text = "첫 훈련";
            await Capture("slots-02-name");

            Phase("preserving a new slot after its first tutorial load fails");
            loaderField = typeof(TitleManager).GetField("TutorialSceneLoaderOverride", BindingFlags.Static | BindingFlags.NonPublic);
            Check(loaderField != null, "The test-only tutorial loading seam is missing.");
            originalLoader = loaderField.GetValue(null);
            int rejectedLoads = 0;
            loaderField.SetValue(null, new Func<string, AsyncOperation>(_ => { rejectedLoads++; return null; }));
            Click(picker.ConfirmButton);
            await Wait(() => rejectedLoads == 1 && !title.IsLoading && title.SlotView.IsOpen,
                "A failed tutorial load did not return to the picker.");
            var first = slots.Slots.Single();
            Check(first.Number == 1 && first.Name == "첫 훈련" && first.IsReadable && slots.SelectedSlotId == first.Id
                && slots.SelectedAccount?.TutorialStatus == TutorialProgressStatus.InMap,
                "Creation was lost or detached when the tutorial could not load.");
            Check(new SaveSlotService(root, slots.SelectedAccount.Settings).TryRefresh(out error)
                && File.Exists(SaveSlotRegression.ProfilePath(root, first)),
                "The failed launch did not leave the expected on-disk slot: " + error);
            loaderField.SetValue(null, originalLoader);
            picker = title.SlotView;
            await Wait(() => Ready(picker.GetPrimaryButton(1)), "The saved row could not retry the scene load.");
            Click(picker.GetPrimaryButton(1));
            var run = await WaitForTutorial();
            Check(slots.Slots.Count == 1 && slots.SelectedSlotId == first.Id,
                "Retry created a new slot instead of loading the existing save.");
            run.ReturnToTitle();

            Phase("verifying five occupied rows and cancel/confirm deletion");
            title = await WaitForTitle();
            Check(slots.TryCreate(2, "기존 거점", out var existing, out error)
                && slots.TrySelect(existing.Id, out var existingAccount, out error)
                && existingAccount.TryGrantReward("test-hub-balance", 46, out error)
                && existingAccount.TrySkipTutorial(out error), "Could not prepare existing hub slot: " + error);
            for (int number = 3; number <= 5; number++)
                Check(slots.TryCreate(number, null, out _, out error), "Could not fill catalog for the UI: " + error);
            var fifth = slots.Slots.Single(slot => slot.Number == 5);
            picker = await OpenPicker(title);
            Check(Enumerable.Range(1, 5).All(number => Ready(picker.GetPrimaryButton(number))
                && Ready(picker.GetDeleteButton(number))) && slots.Slots.Count == 5,
                "The full picker did not show all five occupied rows.");
            await Capture("slots-03-full");
            Click(picker.GetDeleteButton(5));
            await Wait(() => Ready(picker.ConfirmButton) && Ready(picker.CancelButton), "Deletion did not require an explicit confirmation.");
            await Capture("slots-04-delete");
            Click(picker.CancelButton);
            await Wait(() => !Ready(picker.ConfirmButton) && Ready(picker.GetDeleteButton(5)), "Cancelling deletion did not return to the slot list.");
            Check(slots.Slots.Any(slot => slot.Id == fifth.Id) && File.Exists(SaveSlotRegression.ProfilePath(root, fifth)),
                "Cancelling deletion removed the slot or its file.");
            Click(picker.GetDeleteButton(5));
            await Wait(() => Ready(picker.ConfirmButton), "Deletion confirmation did not reopen.");
            Click(picker.ConfirmButton);
            await Wait(() => slots.Slots.Count == 4 && Ready(picker.GetPrimaryButton(5))
                && (picker.GetDeleteButton(5) == null || !picker.GetDeleteButton(5).gameObject.activeInHierarchy),
                "Confirming deletion did not return row five to the empty state.");
            Check(!slots.TrySelect(fifth.Id, out _, out _), "A confirmed deletion left its saved ID selectable.");

            Phase("resuming the in-map slot and then loading the existing hub slot");
            Click(picker.GetPrimaryButton(1));
            run = await WaitForTutorial();
            Check(slots.SelectedSlotId == first.Id && slots.SelectedAccount?.TutorialStatus == TutorialProgressStatus.InMap
                && run.GetComponent<TutorialDirector>()?.Step == TutorialStep.Move,
                "The in-map slot did not return to tutorial start.");
            run.ReturnToTitle();

            Phase("continuing the last tutorial slot after reopening the save catalog");
            slots = ReopenSlots(root, settings);
            CheckLastPlayed(slots, first.Id);
            title = await WaitForTitle();
            await Wait(() => Ready(title.ContinueButton), "Continue was disabled for the saved tutorial slot.");
            await Capture("slots-05-title-continue");
            ClickContinue(title);
            run = await WaitForTutorial();
            Check(slots.SelectedSlotId == first.Id && slots.SelectedAccount?.TutorialStatus == TutorialProgressStatus.InMap
                && run.GetComponent<TutorialDirector>()?.Step == TutorialStep.Move,
                "Continue did not resume the persisted tutorial slot.");
            run.ReturnToTitle();

            Phase("loading the existing hub slot and continuing it after reopening the catalog");
            title = await WaitForTitle();
            picker = await OpenPicker(title);
            Click(picker.GetPrimaryButton(2));
            await WaitForHub("The existing skipped slot did not load the hub.");
            CheckHubAccount(slots, existing.Id);
            slots = ReopenSlots(root, settings);
            CheckLastPlayed(slots, existing.Id);
            title = await ReloadTitle();
            await Wait(() => Ready(title.ContinueButton), "Continue was disabled for the saved hub slot.");
            ClickContinue(title);
            await WaitForHub("Continue did not load the last played hub slot.");
            CheckHubAccount(slots, existing.Id);

            Phase("deleting the last played slot without falling back to another save");
            title = await ReloadTitle();
            picker = await OpenPicker(title);
            Click(picker.GetDeleteButton(2));
            await Wait(() => Ready(picker.ConfirmButton), "The last-played slot did not show deletion confirmation.");
            Click(picker.ConfirmButton);
            await Wait(() => slots.Slots.Count == 3 && Ready(picker.BackButton), "The last-played slot was not deleted.");
            Click(picker.BackButton);
            await Wait(() => !picker.IsOpen && Ready(StartButton(title)), "The title did not return from the picker.");
            if (Ready(title.ContinueButton))
            {
                Click(title.ContinueButton);
                await Wait(() => title.SlotView?.IsOpen == true && !title.IsLoading,
                    "A stale Continue button did not stay on the title and show the picker.");
                var errorLabel = title.SlotView.transform.Find("SlotList/Error")?.GetComponent<TMPro.TMP_Text>();
                Check(slots.SelectedAccount == null && !string.IsNullOrWhiteSpace(errorLabel?.text),
                    "A stale Continue button chose a different save or hid the load error.");
            }
            slots = ReopenSlots(root, settings);
            Check((!slots.TryGetLastPlayedSlot(out var removed, out _) || removed == null)
                && slots.TryRefresh(out error) && slots.Slots.Count == 3,
                "After reopening, a deleted last-played slot remained resumable or other slots were lost.");
            title = await ReloadTitle();
            Check(title.ContinueButton != null && !Ready(title.ContinueButton),
                "Continue was still enabled after reopening the title with the last-played slot deleted.");

            const string result = "PASS: actual slot picker creation, load-failure retry, capacity, deletion, and persisted direct Continue into tutorial and hub.";
            SessionState.SetString(StatusKey, result);
            Debug.Log(result);
        }
        catch (Exception failure)
        {
            SessionState.SetString(StatusKey, "FAIL: " + failure.Message);
            Debug.LogException(failure);
        }
        finally
        {
            if (loaderField != null) loaderField.SetValue(null, originalLoader);
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }
    }

    private static SaveSlotService ReopenSlots(string root, PlayerUpgradeSettingsSO settings)
    {
        _slotScope?.Dispose();
        var service = new SaveSlotService(root, settings);
        _slotScope = SaveSlotService.OverrideForTesting(service);
        TutorialSession.Reset();
        return service;
    }

    private static void CheckLastPlayed(SaveSlotService slots, string expectedId)
    {
        Check(slots.SelectedAccount == null && string.IsNullOrEmpty(slots.SelectedSlotId),
            "Reopening the test catalog retained a live account.");
        Check(slots.TryGetLastPlayedSlot(out var last, out var error) && last?.Id == expectedId,
            "The last-played slot identity did not survive reopening the catalog: " + error);
    }

    private static void ClickContinue(TitleManager title)
    {
        Click(title.ContinueButton);
        Check(title == null || title.SlotView?.IsOpen != true, "Continue opened the slot picker before loading the last save.");
    }

    private static Task WaitForHub(string error) => Wait(() => SceneManager.GetActiveScene().name == "Hub Scene"
        && !HubReturnTransition.IsActive && Object.FindFirstObjectByType<HubWorldController>() != null, error, 45f);

    private static void CheckHubAccount(SaveSlotService slots, string expectedId)
        => Check(slots.SelectedSlotId == expectedId && ReferenceEquals(slots.SelectedAccount, PlayerUpgradeService.Instance)
            && PlayerUpgradeService.Instance.Balance == 46 && PlayerUpgradeService.Instance.TutorialStatus == TutorialProgressStatus.Skipped,
            "The hub did not keep the expected account and progress.");

    private static async Task<TitleManager> ReloadTitle()
    {
        var operation = SceneManager.LoadSceneAsync("TitleScene");
        await Wait(() => operation?.isDone == true, "Could not reload the title scene for the Continue test.", 45f);
        return await WaitForTitle();
    }

    private static async Task<TitleManager> WaitForTitle()
    {
        TitleManager title = null;
        await Wait(() =>
        {
            title = Object.FindFirstObjectByType<TitleManager>();
            return SceneManager.GetActiveScene().path == TitlePath && title != null && !title.IsLoading && Ready(StartButton(title));
        }, "Title controls did not become ready.", 45f);
        return title;
    }

    private static async Task<SaveSlotView> OpenPicker(TitleManager title)
    {
        Click(StartButton(title));
        await Wait(() => title.SlotView != null && title.SlotView.IsOpen && Ready(title.SlotView.GetPrimaryButton(1)),
            "Start did not open a usable slot picker.");
        await Awaitable.NextFrameAsync();
        VerifyPickerScale(title.SlotView);
        return title.SlotView;
    }

    private static void VerifyPickerScale(SaveSlotView picker)
    {
        Canvas.ForceUpdateCanvases();
        var shade = picker.transform.Find("Shade") as RectTransform;
        var primary = picker.GetPrimaryButton(1)?.transform as RectTransform;
        Check(shade != null && primary != null, "The slot picker is missing its shade or first row button.");
        Vector2 shadePixels = ScreenSize(shade);
        Vector2 primaryPixels = ScreenSize(primary);
        Check(shadePixels.x >= Screen.width * .95f && shadePixels.y >= Screen.height * .95f,
            $"The slot shade covers only {shadePixels.x:F0} × {shadePixels.y:F0} px of {Screen.width} × {Screen.height}.");
        Check(primaryPixels.x >= Screen.width * .02f && primaryPixels.y >= Screen.height * .015f,
            $"The first slot button is too small at {primaryPixels.x:F1} × {primaryPixels.y:F1} px.");
    }

    private static Vector2 ScreenSize(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 minimum = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
        Vector2 maximum = minimum;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, corners[i]);
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }
        return maximum - minimum;
    }

    private static async Task<RunManager> WaitForTutorial()
    {
        RunManager run = null;
        await Wait(() =>
        {
            run = Object.FindFirstObjectByType<RunManager>();
            if (run == null || !run.IsTutorial) return false;
            Check(run.State != RunState.Error, "The real tutorial stage failed to prepare: " + run.LastError);
            return run.State == RunState.Playing && run.stageManager.IsReady;
        }, "The selected save did not open the tutorial.", 120f);
        return run;
    }

    private static Button StartButton(TitleManager title)
        => (Button)typeof(TitleManager).GetField("startButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(title);

    private static bool Ready(Button button) => button != null && button.isActiveAndEnabled && button.IsInteractable();

    private static void Click(Button button)
    {
        Check(Ready(button), "Expected an enabled slot-picker button.");
        button.onClick.Invoke();
    }

    private static async Task Capture(string file)
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/TutorialValidation"));
        Directory.CreateDirectory(folder);
        await Awaitable.NextFrameAsync();
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, file + ".png"));
        await Awaitable.NextFrameAsync();
    }

    private static void Phase(string text) => SessionState.SetString(StatusKey, "RUNNING: " + text);

    private static async Task Wait(Func<bool> predicate, string error, float seconds = 12f)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + seconds;
        while (!predicate())
        {
            Check(EditorApplication.isPlaying && Time.realtimeSinceStartupAsDouble < deadline, error);
            await Awaitable.NextFrameAsync();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
