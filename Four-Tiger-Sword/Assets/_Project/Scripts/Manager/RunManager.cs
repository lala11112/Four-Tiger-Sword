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
    private CombatHitStopService.WorldScaleLease _stageIntroPause;
    private Coroutine _deathPresentation;
    private uint _deathPresentationVersion;
    public bool IsDeathPresentationPlaying { get; private set; }

    private void Awake()
    {
        if (stageManager == null) stageManager = GetComponent<StageManager>();
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (resultUI == null) resultUI = GetComponent<StageResultUI>() ?? gameObject.AddComponent<StageResultUI>();
        resultUI.Bind(this);
        RunRewardFeedPresenter.Ensure(this, player);
    }

    private IEnumerator Start()
    {
        if (stageManager == null || player == null) { ShowError("Run requires StageManager and PlayerController."); yield break; }
        stageManager.ExitRequested += Advance;
        stageManager.Failed += OnStageFailure;
        stageManager.OnStageClear += OnUpgradeStageClear;
        player.Died += Lose;
        SuspendPlayerAndCloseShop();
        // All Awake calls have completed; cover the new scene before its first rendered frame.
        resultUI.ShowLoading("Stage 1-1");
        // Subscribe before yielding: a first-frame death must terminate the run.
        yield return null;
        if (State == RunState.Lost || player.StatManager.CurrentHp <= 0) { Lose(); yield break; }
        RunSeed = randomizeSeed ? Guid.NewGuid().GetHashCode() : seed;
        _pendingChapter = _pendingStage = 0;
        _hasPending = true;
        if (!EnsureUpgradeProfile()) yield break;
        BeginPending();
    }

    private void BeginPending()
    {
        if (_transition != null || !_hasPending || State == RunState.Won || State == RunState.Lost) return;
        if (!CanLeaveUpgradeRewards()) return;
        UnbindUpgradeRooms();
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
        if (State != RunState.Preparing) { CancelStagePresentation(); yield break; }
        if (!stageManager.IsReady) { ShowError(stageManager.Error ?? "Stage preparation failed."); yield break; }
        if (player.StatManager.CurrentHp <= 0) { Lose(); yield break; }
        CurrentChapterIndex = _pendingChapter;
        CurrentStageIndex = _pendingStage;
        CurrentStageSeed = stageSeed;
        LastError = null;
        // Keep loading visible while placing the camera and evaluating the player's Idle pose.
        _stageIntroPause = CombatHitStopService.AcquireWorldScale(this, 0f);
        string placementError = null;
        try
        {
            player.PlaceStageTransition(stageManager.PlayerSpawnPosition, stageManager.PlayerSpawnRotation);
            if (!player.PrepareStageIdleForReveal()) placementError = "Player Idle animation is unavailable.";
        }
        catch (Exception error) { placementError = error.Message; }
        if (placementError != null) { ShowError("Stage spawn failed: " + placementError); yield break; }
        yield return null;
        if (!isActiveAndEnabled || State != RunState.Preparing) { CancelStagePresentation(); yield break; }
        if (player.StatManager.CurrentHp <= 0f) { Lose(); yield break; }
        resultUI.BeginStageIntro(CurrentChapterIndex + 1, CurrentStageIndex + 1);
        yield return resultUI.PlayStageIntro();
        if (!isActiveAndEnabled || State != RunState.Preparing) { CancelStagePresentation(); yield break; }
        if (!stageManager.IsReady || !string.IsNullOrEmpty(stageManager.Encounters?.Error))
        { ShowError(stageManager.Error ?? stageManager.Encounters?.Error ?? "Stage preparation was interrupted."); yield break; }
        if (player.StatManager.CurrentHp <= 0f) { Lose(); yield break; }
        if (!resultUI.StageIntroCompleted) { ShowError("Stage introduction was interrupted."); yield break; }
        _transition = null;
        _hasPending = false;
        try
        {
            player.ResumeStageTransition();
            SetState(RunState.Playing);
            BindUpgradeRooms();
            resultUI.ShowStage(ProgressLabel);
            stageManager.NotifyStarted();
        }
        finally { ReleaseStageIntroPause(); }
        Debug.Log($"[Run] Stage {CurrentChapterIndex + 1}-{CurrentStageIndex + 1}, run seed {RunSeed}, stage seed {stageSeed}, layout seed {layoutSeed}, rooms {stageManager.MapCount}.", this);
    }

    private void Advance()
    {
        if (_grantingUpgradeRewards || _endingUpgradeRun) return;
        if (IsUpgradeSaveBlocked) { _advanceAfterUpgradeSave = true; return; }
        if (State != RunState.Playing || _transition != null) return;
        if (!CanLeaveUpgradeRewards()) { _advanceAfterUpgradeSave = true; return; }
        if (!definition.TryGetNext(CurrentChapterIndex, CurrentStageIndex, out _pendingChapter, out _pendingStage))
        { EndRun(true); return; }
        _attempt = 0;
        _hasPending = true;
        BeginPending();
    }

    public void RetryPendingStage()
    {
        if (IsUpgradeSaveBlocked) { TryRetryPendingRewards(); return; }
        if (State != RunState.Error || !_hasPending || _transition != null) return;
        _attempt++;
        BeginPending();
    }

    private void OnStageFailure(string error)
    {
        if (State != RunState.Playing && !(State == RunState.Preparing && _stageIntroPause != null)) return;
        _pendingChapter = CurrentChapterIndex; _pendingStage = CurrentStageIndex;
        _hasPending = true; _attempt = 0;
        ShowError(error);
    }

    private void ShowError(string error)
    {
        CancelStagePresentation();
        UnbindUpgradeRooms();
        LastError = error;
        SuspendPlayerAndCloseShop();
        stageManager?.Suspend();
        SetState(RunState.Error);
        resultUI?.ShowError(error);
    }

    private void Lose() { if (State != RunState.Won && State != RunState.Lost) EndRun(false); }

    private void EndRun(bool won)
    {
        if (State == RunState.Won || State == RunState.Lost || _endingUpgradeRun) return;
        _endingUpgradeRun = true;
        try
        {
            CancelStagePresentation();
            IsDeathPresentationPlaying = !won && Application.isPlaying && isActiveAndEnabled
                && resultUI != null && resultUI.isActiveAndEnabled;
            _hasPending = false;
            UnbindUpgradeRooms();
            SuspendPlayerAndCloseShop();
            stageManager?.Cancel();
            if (IsDeathPresentationPlaying)
            {
                resultUI.BeginDeathPresentation();
                player?.BeginDeathPresentation();
            }
            // Commit terminal state before settlement, but notify observers only after its reward was queued/saved.
            State = won ? RunState.Won : RunState.Lost;
            bool saved = SettleUpgradeRunEnd(won);
            SetState(State);
            if (IsDeathPresentationPlaying)
                _deathPresentation = StartCoroutine(PresentDeathResult(_deathPresentationVersion));
            else if (saved) resultUI?.ShowRunResult(won);
        }
        finally { _endingUpgradeRun = false; }
    }

    private IEnumerator PresentDeathResult(uint version)
    {
        // Finish the synchronous death notification/settlement before observing animation frames.
        yield return null;
        if (player != null) yield return player.WaitForDeathAnimation();
        player?.CancelDeathPresentation();
        if (!DeathPresentationIsCurrent(version)) yield break;
        yield return resultUI.FadeOutForDeath();
        if (!DeathPresentationIsCurrent(version)) yield break;
        _deathPresentation = null;
        IsDeathPresentationPlaying = false;
        if (!resultUI.DeathFadeCompleted) yield break;
        if (IsUpgradeSaveBlocked) resultUI.ShowRewardSaveError(LastError);
        else resultUI.ShowRunResult(false);
    }

    private bool DeathPresentationIsCurrent(uint version) => version == _deathPresentationVersion
        && IsDeathPresentationPlaying && isActiveAndEnabled && State == RunState.Lost && resultUI != null;

    private void CancelDeathPresentation()
    {
        ++_deathPresentationVersion;
        if (_deathPresentation != null) StopCoroutine(_deathPresentation);
        _deathPresentation = null;
        IsDeathPresentationPlaying = false;
        player?.CancelDeathPresentation();
        resultUI?.CancelDeathFade();
    }

    private void SetState(RunState state)
    {
        if (state != RunState.Playing) CleanupMapOnShutdown();
        State = state;
        RefreshRunCursor();
        StateChanged?.Invoke(state);
    }

    public void RestartRun()
    {
        if (State != RunState.Won && State != RunState.Lost) return;
        if (!CanLeaveUpgradeRewards()) return;
        CancelStagePresentation();
        CloseMap();
        CloseShop();
        CombatHitStopService.SetExternalWorldTimeScale(1f);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ReturnToTitle()
    {
        if (!CanLeaveUpgradeRewards()) return;
        CancelStagePresentation();
        SuspendPlayerAndCloseShop();
        CombatHitStopService.SetExternalWorldTimeScale(1f);
        SceneManager.LoadScene("TitleScene");
    }

    private void ReleaseStageIntroPause()
    {
        _stageIntroPause?.Dispose();
        _stageIntroPause = null;
    }

    private void CancelStagePresentation()
    {
        CancelDeathPresentation();
        player?.CancelStageIdlePresentation();
        if (_transition != null) StopCoroutine(_transition);
        _transition = null;
        resultUI?.CancelStageIntro();
        ReleaseStageIntroPause();
    }

    private void OnDestroy()
    {
        CancelStagePresentation();
        CleanupUpgradeRewards();
        CleanupMapOnShutdown();
        CleanupShopOnShutdown();
        if (stageManager != null) { stageManager.ExitRequested -= Advance; stageManager.Failed -= OnStageFailure; stageManager.OnStageClear -= OnUpgradeStageClear; }
        if (player != null) player.Died -= Lose;
    }
}
