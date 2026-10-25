using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum RunState { Preparing, Playing, Error, Won, Lost }

[DisallowMultipleComponent]
public sealed partial class RunManager : MonoBehaviour
{
    public RunDefinitionSO definition;
    public StageManager stageManager;
    public PlayerController player;
    public StageResultUI resultUI;
    public bool randomizeSeed = true;
    public int seed = 12345;
    public RunState State { get; private set; } = RunState.Preparing;
    public int CurrentChapterIndex { get; private set; } = -1;
    public int CurrentStageIndex { get; private set; } = -1;
    public int RunSeed { get; private set; }
    public int CurrentStageSeed { get; private set; }
    public string LastError { get; private set; }
    public string ProgressLabel => CurrentChapterIndex < 0 ? "Preparing run" : $"{CurrentChapterIndex + 1}-{CurrentStageIndex + 1}  |  {definition.chapters[CurrentChapterIndex].displayName}";
    public event Action<RunState> StateChanged;
    private int _pendingChapter, _pendingStage, _attempt;
    private bool _hasPending;
    private Coroutine _transition;

    private void Awake()
    {
        if (stageManager == null) stageManager = GetComponent<StageManager>();
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (resultUI == null) resultUI = GetComponent<StageResultUI>() ?? gameObject.AddComponent<StageResultUI>();
        resultUI.Bind(this);
    }

    private IEnumerator Start()
    {
        if (stageManager == null || player == null) { ShowError("Run requires StageManager and PlayerController."); yield break; }
        stageManager.ExitRequested += Advance;
        stageManager.Failed += OnStageFailure;
        player.Died += Lose;
        SuspendPlayerAndCloseShop();
        // Subscribe before yielding: a first-frame death must terminate the run.
        yield return null;
        if (State == RunState.Lost || player.StatManager.CurrentHp <= 0) { Lose(); yield break; }
        RunSeed = randomizeSeed ? Guid.NewGuid().GetHashCode() : seed;
        _pendingChapter = _pendingStage = 0;
        _hasPending = true;
        BeginPending();
    }

    private void BeginPending()
    {
        if (_transition != null || !_hasPending || State == RunState.Won || State == RunState.Lost) return;
        SuspendPlayerAndCloseShop();
        stageManager?.Suspend();
        if (definition == null || !definition.Validate(out _))
        {
            string error = "Run Definition is missing.";
            if (definition != null) definition.Validate(out error);
            ShowError(error); return;
        }
        SetState(RunState.Preparing);
        resultUI.ShowLoading($"Stage {_pendingChapter + 1}-{_pendingStage + 1}");
        _transition = StartCoroutine(PreparePending());
    }

    private IEnumerator PreparePending()
    {
        // Assign the handle even when validation fails synchronously.
        yield return null;
        var stages = definition.chapters[_pendingChapter].stages;
        int stageSeed = RunDefinitionSO.GetStageSeed(RunSeed, _pendingChapter, _pendingStage);
        int layoutSeed = _attempt == 0 ? stageSeed : RunDefinitionSO.GetStageSeed(stageSeed, _attempt, 0);
        yield return stageManager.Prepare(stages[_pendingStage], _pendingStage == stages.Length - 1, stageSeed, layoutSeed, player);
        _transition = null;
        if (State != RunState.Preparing) yield break;
        if (!stageManager.IsReady) { ShowError(stageManager.Error ?? "Stage preparation failed."); yield break; }
        if (player.StatManager.CurrentHp <= 0) { Lose(); yield break; }
        CurrentChapterIndex = _pendingChapter;
        CurrentStageIndex = _pendingStage;
        CurrentStageSeed = stageSeed;
        _hasPending = false;
        LastError = null;
        player.CompleteStageTransition(stageManager.PlayerSpawnPosition, stageManager.PlayerSpawnRotation);
        SetState(RunState.Playing);
        resultUI.ShowStage(ProgressLabel);
        stageManager.NotifyStarted();
        Debug.Log($"[Run] Stage {CurrentChapterIndex + 1}-{CurrentStageIndex + 1}, run seed {RunSeed}, stage seed {stageSeed}, layout seed {layoutSeed}, rooms {stageManager.MapCount}.", this);
    }

    private void Advance()
    {
        if (State != RunState.Playing || _transition != null) return;
        if (!definition.TryGetNext(CurrentChapterIndex, CurrentStageIndex, out _pendingChapter, out _pendingStage))
        { EndRun(true); return; }
        _attempt = 0;
        _hasPending = true;
        BeginPending();
    }

    public void RetryPendingStage()
    {
        if (State != RunState.Error || !_hasPending || _transition != null) return;
        _attempt++;
        BeginPending();
    }

    private void OnStageFailure(string error)
    {
        if (State != RunState.Playing) return;
        _pendingChapter = CurrentChapterIndex; _pendingStage = CurrentStageIndex;
        _hasPending = true; _attempt = 0;
        ShowError(error);
    }

    private void ShowError(string error)
    {
        LastError = error;
        SuspendPlayerAndCloseShop();
        stageManager?.Suspend();
        SetState(RunState.Error);
        resultUI?.ShowError(error);
    }

    private void Lose() { if (State != RunState.Won && State != RunState.Lost) EndRun(false); }

    private void EndRun(bool won)
    {
        if (_transition != null) StopCoroutine(_transition);
        _transition = null;
        _hasPending = false;
        SuspendPlayerAndCloseShop();
        stageManager?.Cancel();
        SetState(won ? RunState.Won : RunState.Lost);
        resultUI?.ShowRunResult(won);
    }

    private void SetState(RunState state)
    {
        State = state;
        RefreshRunCursor();
        StateChanged?.Invoke(state);
    }

    public void RestartRun()
    {
        if (State != RunState.Won && State != RunState.Lost) return;
        CloseShop();
        CombatHitStopService.SetExternalWorldTimeScale(1f);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ReturnToTitle()
    {
        SuspendPlayerAndCloseShop();
        CombatHitStopService.SetExternalWorldTimeScale(1f);
        SceneManager.LoadScene("TitleScene");
    }

    private void OnDestroy()
    {
        CleanupShopOnShutdown();
        if (stageManager != null) { stageManager.ExitRequested -= Advance; stageManager.Failed -= OnStageFailure; }
        if (player != null) player.Died -= Lose;
    }
}
