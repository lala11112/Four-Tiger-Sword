using System;
using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;

/// <summary>Only committed, current-lesson actions unlock the next tutorial objective.</summary>
[DefaultExecutionOrder(-20), DisallowMultipleComponent]
public sealed class TutorialDirector : MonoBehaviour
{
    public TutorialDefinitionSO definition;
    public TutorialStep Step { get; private set; }
    public event Action<TutorialStep> StepChanged;
    public TutorialView View => _view;
    private RunManager _run;
    private StageShopService _shop;
    private TutorialCombatReporter _combat;
    private TutorialView _view;
    private RunItemChoiceService _choices;
    private readonly Dictionary<int, BaekhoMapModule> _rooms = new();
    private readonly Dictionary<int, DungeonRoomEncounter> _encounters = new();
    private Enemy _trainingTarget;
    private Transform _movementTarget;
    private GameObject _generation;
    private bool _ready, _shopKitGranted, _airHit, _transitioning;
    private float _movedDistance;
    private Vector3 _previousPosition;
    private DungeonMinimapPresenter _map;
    private string _trainingReceipt;
    internal void RegisterTrainingReceipt(string receipt) => _trainingReceipt = receipt;

    private void Awake()
    {
        _run = GetComponent<RunManager>();
        if (definition == null) definition = Resources.Load<TutorialDefinitionSO>("TutorialDefinition");
        if (_run == null || definition == null) { enabled = false; return; }
        if (definition.runDefinition != null) _run.definition = definition.runDefinition;
        _shop = GetComponent<StageShopService>();
    }

    private void OnEnable()
    {
        if (_run == null || definition == null) return;
        _run.stageManager.OnStageStart += OnStageStarted;
        _run.StateChanged += OnRunState;
        _run.MapOpenChanged += OnMapOpened;
        _run.FastTravelCompleted += OnFastTravel;
        if (_shop != null) _shop.TransactionCommitted += OnTransaction;
    }

    private void OnStageStarted()
    {
        if (!_run.IsTutorial) return;
        _rooms.Clear(); _encounters.Clear();
        var result = _run.stageManager.Result;
        _generation = result.root;
        foreach (var room in result.rooms)
        {
            int id = room.GetComponent<BaekhoGeneratedRoom>().instanceId;
            _rooms[id] = room;
            _encounters[id] = room.GetComponent<DungeonRoomEncounter>();
        }
        foreach (int id in new[] { definition.startRoomId, definition.trainingRoomId, definition.shopRoomId, definition.freeCombatRoomId, definition.exitRoomId })
            if (!_rooms.ContainsKey(id)) { _run.ReportTutorialFailure("튜토리얼 방 구성이 올바르지 않습니다."); return; }
        if (_combat != null) _combat.ActionCompleted -= OnCombatAction;
        _combat = _run.player.GetComponent<TutorialCombatReporter>() ?? _run.player.gameObject.AddComponent<TutorialCombatReporter>();
        _combat.ActionCompleted += OnCombatAction;
        if (_choices != null) _choices.RewardClaimed -= OnRewardClaimed;
        _choices = _run.ItemChoices;
        if (_choices != null) _choices.RewardClaimed += OnRewardClaimed;
        _view = TutorialView.Ensure(this);
        _view.SetSkipAction(_run.SkipTutorial);
        _map = FindFirstObjectByType<DungeonMinimapPresenter>();
        _shop?.ConfigureTutorial(TutorialShopPhase.Complete);
        if (_movementTarget != null) Destroy(_movementTarget.gameObject);
        _movementTarget = new GameObject("TutorialMovementTarget").transform;
        _movementTarget.SetParent(_rooms[definition.startRoomId].transform, false);
        _movementTarget.localPosition = definition.movementTargetLocal;
        _previousPosition = _run.player.transform.position;
        _movedDistance = 0f;
        _ready = true;
        bool restoring = TutorialSession.RestoreCheckpoint && TutorialSession.PendingCheckpoint != null;
        if (restoring)
        {
            _shopKitGranted = true;
            _combat.SetTrainingProtection(false, null, false);
            SetStep(TutorialStep.FreeCombat);
            _encounters[definition.freeCombatRoomId].BeginEncounter();
            TutorialSession.RestoreCheckpoint = false;
        }
        else
        {
            TutorialSession.PendingCheckpoint = null;
            _shopKitGranted = false;
            _trainingTarget = null;
            foreach (var member in _encounters[definition.trainingRoomId].Members)
                if (member.Enemy != null) { _trainingTarget = member.Enemy; break; }
            if (_trainingTarget == null) { _run.ReportTutorialFailure("연습 적을 준비하지 못했습니다."); return; }
            _trainingTarget.CombatLabPassive = true;
            _combat.SetTrainingProtection(true, _trainingTarget, true);
            SetStep(TutorialStep.Move);
        }
    }

    private void Update()
    {
        if (!_ready || _run == null || _run.State != RunState.Playing || _generation == null) return;
        var player = _run.player;
        Vector3 position = player.transform.position;
        if (Step == TutorialStep.Move)
        {
            Vector3 delta = position - _previousPosition; delta.y = 0f;
            if (!player.IsGameplayModalPaused && player.StateMachine?.CurrentState is PlayerRunState
                && delta.sqrMagnitude < 4f) _movedDistance += delta.magnitude;
            Vector3 distance = position - _movementTarget.position; distance.y = 0f;
            if (_movedDistance >= 2f && distance.sqrMagnitude < 2.25f) SetStep(TutorialStep.EnterTraining);
        }
        _previousPosition = position;
        if (Step == TutorialStep.EnterTraining && _encounters[definition.trainingRoomId].State == DungeonEncounterState.Fighting)
            SetStep(TutorialStep.Attack);
        if (Step == TutorialStep.DefeatTraining && _encounters[definition.trainingRoomId].WasCombatClearedByDefeat)
            SetStep(TutorialStep.ClaimChest);
        if (Step == TutorialStep.ReachShop && IsInside(definition.shopRoomId, position)) PrepareShopLesson();
        if (Step == TutorialStep.EnterFreeCombat && !_run.IsShopOpen && !_run.IsRewardOpen)
        {
            if (TutorialSession.PendingCheckpoint == null) CaptureCheckpoint();
            if (_encounters[definition.freeCombatRoomId].State == DungeonEncounterState.Fighting)
            {
                player.StatManager.Heal(player.StatManager.MaxHp);
                _combat.SetTrainingProtection(false, null, false);
                SetStep(TutorialStep.FreeCombat);
            }
        }
        if (Step == TutorialStep.FreeCombat && _encounters[definition.freeCombatRoomId].WasCombatClearedByDefeat)
            SetStep(TutorialStep.ReachExit);
        RefreshTarget();
    }

    private bool IsInside(int roomId, Vector3 world)
    {
        var room = _rooms[roomId];
        var local = room.transform.InverseTransformPoint(world);
        var bounds = room.localBounds;
        return local.x >= bounds.min.x && local.x <= bounds.max.x && local.z >= bounds.min.z && local.z <= bounds.max.z;
    }

    private void PrepareShopLesson()
    {
        var items = _run.player.GetComponent<PlayerItemRuntime>();
        var wallet = _shop.Wallet;
        if (!_shopKitGranted)
        {
            if (!items.Contains("I05") && !items.TryAddItem("I05")) return;
            if (wallet.Gold < 40 && !wallet.AddGold(40 - wallet.Gold)) return;
            _shopKitGranted = true;
        }
        SetStep(TutorialStep.Buy);
    }

    private void CaptureCheckpoint()
    {
        var ids = new List<string>();
        foreach (var item in _run.player.GetComponent<PlayerItemRuntime>().OwnedItems) ids.Add(item.itemId);
        TutorialSession.PendingCheckpoint = new TutorialCheckpoint { itemIds = ids.ToArray(), gold = _shop.Wallet.Gold };
    }

    private void SetStep(TutorialStep step)
    {
        if (_transitioning) return;
        _transitioning = true;
        try
        {
            Step = step;
            var copy = definition.GetStep(step);
            _view.SetGoal(copy.title, copy.body, copy.keys);
            switch (step)
            {
                case TutorialStep.Attack: _combat.ResetLessonResources(TutorialCombatAction.GroundHit); break;
                case TutorialStep.Dodge: _combat.ResetLessonResources(TutorialCombatAction.Dash); break;
                case TutorialStep.Skill: _combat.ResetLessonResources(TutorialCombatAction.Skill); break;
                case TutorialStep.AirAttack: _airHit = false; _combat.ResetLessonResources(TutorialCombatAction.AirHit); break;
                case TutorialStep.Ultimate: _combat.ResetLessonResources(TutorialCombatAction.Ultimate); break;
                case TutorialStep.DefeatTraining: _combat.SetTrainingProtection(true, _trainingTarget, false); break;
                case TutorialStep.Buy: _shop.ConfigureTutorial(TutorialShopPhase.Buy); break;
                case TutorialStep.Sell: _shop.ConfigureTutorial(TutorialShopPhase.Sell); break;
                case TutorialStep.Fuse: _shop.ConfigureTutorial(TutorialShopPhase.Fuse); break;
                case TutorialStep.EnterFreeCombat: _shop.ConfigureTutorial(TutorialShopPhase.Complete); break;
            }
            RefreshGates();
            RefreshTarget();
            StepChanged?.Invoke(step);
        }
        finally { _transitioning = false; }
    }

    private void OnCombatAction(TutorialCombatAction action)
    {
        if (!_ready || _run.State != RunState.Playing || !_encounters[definition.trainingRoomId].IsCombatActive) return;
        if (Step == TutorialStep.Attack && action == TutorialCombatAction.GroundHit) SetStep(TutorialStep.Dodge);
        else if (Step == TutorialStep.Dodge && action == TutorialCombatAction.Dash) SetStep(TutorialStep.Skill);
        else if (Step == TutorialStep.Skill && action == TutorialCombatAction.Skill) SetStep(TutorialStep.AirAttack);
        else if (Step == TutorialStep.AirAttack && action == TutorialCombatAction.AirHit)
        { _airHit = true; _combat.ResetLessonResources(TutorialCombatAction.AirLanded); }
        else if (Step == TutorialStep.AirAttack && _airHit && action == TutorialCombatAction.AirLanded) SetStep(TutorialStep.Ultimate);
        else if (Step == TutorialStep.Ultimate && action == TutorialCombatAction.Ultimate) SetStep(TutorialStep.DefeatTraining);
    }

    private void OnRewardClaimed(string receipt, string itemId)
    {
        if (Step != TutorialStep.ClaimChest || itemId != "I01" || !_ready) return;
        if (_trainingReceipt == receipt) SetStep(TutorialStep.OpenMap);
    }

    private void OnMapOpened(bool open)
    {
        if (_ready && open && Step == TutorialStep.OpenMap) SetStep(TutorialStep.TravelToStart);
    }

    private void OnFastTravel(int roomId)
    {
        if (_ready && Step == TutorialStep.TravelToStart && roomId == definition.startRoomId) SetStep(TutorialStep.ReachShop);
        _previousPosition = _run.player.transform.position;
    }

    private void OnTransaction(TutorialShopPhase phase, string itemId)
    {
        if (!_ready || _run.State != RunState.Playing) return;
        if (Step == TutorialStep.Buy && phase == TutorialShopPhase.Buy && itemId == "I04") SetStep(TutorialStep.Sell);
        else if (Step == TutorialStep.Sell && phase == TutorialShopPhase.Sell) SetStep(TutorialStep.Fuse);
        else if (Step == TutorialStep.Fuse && phase == TutorialShopPhase.Fuse) SetStep(TutorialStep.EnterFreeCombat);
    }

    private void RefreshGates()
    {
        foreach (var link in _run.stageManager.Result.plan.links)
        {
            bool blocked = link.fromMapId == definition.startRoomId && Step < TutorialStep.EnterTraining
                || link.fromMapId == definition.trainingRoomId && Step < TutorialStep.ReachShop
                || link.fromMapId == definition.shopRoomId && Step < TutorialStep.EnterFreeCombat
                || link.fromMapId == definition.freeCombatRoomId && Step < TutorialStep.ReachExit;
            _encounters[link.fromMapId].GetGate(link.fromSocketIndex)?.SetTutorialBlocked(blocked);
        }
    }

    private void RefreshTarget()
    {
        Transform world = null;
        RectTransform ui = null;
        var target = definition.ResolveHighlightTarget(Step);
        switch (target)
        {
            case TutorialHighlightTarget.Movement:
                world = _movementTarget;
                break;
            case TutorialHighlightTarget.TrainingEnemy:
                world = _trainingTarget != null ? _trainingTarget.transform : null;
                break;
            case TutorialHighlightTarget.TrainingChest:
                world = _rooms[definition.trainingRoomId].GetComponentInChildren<DungeonRewardChest>()?.transform;
                break;
            case TutorialHighlightTarget.StartRoomOnMap when _run.IsMapOpen:
                ui = _map?.FullMapView?.GetRoomTarget(definition.startRoomId);
                break;
            case TutorialHighlightTarget.Shop:
            case TutorialHighlightTarget.FreeCombatRoom:
                if (_run.IsShopOpen) ui = _shop.TutorialTarget;
                else world = target == TutorialHighlightTarget.FreeCombatRoom ? _rooms[definition.freeCombatRoomId].transform : _shop.ShopPoint;
                break;
            case TutorialHighlightTarget.ExitPortal:
                world = _run.stageManager.Exit != null ? _run.stageManager.Exit.transform : null;
                break;
        }
        _view.SetWorldTarget(world);
        _view.SetUITarget(ui);
    }

    public void ShowSaveFailure(string error)
    {
        _view ??= TutorialView.Ensure(this);
        _view.SetGoal("튜토리얼 안내", error ?? "다시 시도해 주세요.", string.Empty);
        _view.ShowContinue("다시 시도", _run.SkipTutorial);
    }

    private void OnRunState(RunState state)
    {
        if (state == RunState.Playing) return;
        _ready = false;
        if (_combat != null) _combat.SetTrainingProtection(false, null, false);
        _view?.Hide();
    }

    private void OnDisable()
    {
        _ready = false;
        if (_run != null)
        {
            _run.stageManager.OnStageStart -= OnStageStarted;
            _run.StateChanged -= OnRunState;
            _run.MapOpenChanged -= OnMapOpened;
            _run.FastTravelCompleted -= OnFastTravel;
        }
        if (_shop != null) _shop.TransactionCommitted -= OnTransaction;
        if (_choices != null) _choices.RewardClaimed -= OnRewardClaimed;
        if (_combat != null) { _combat.ActionCompleted -= OnCombatAction; _combat.SetTrainingProtection(false, null, false); }
        _view?.Hide();
    }
}
