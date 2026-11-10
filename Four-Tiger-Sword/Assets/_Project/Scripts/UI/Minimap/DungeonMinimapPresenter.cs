using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Connects the run-owned stage to a presentation-only exploration map.</summary>
[DisallowMultipleComponent]
public sealed class DungeonMinimapPresenter : MonoBehaviour
{
    [SerializeField] private RunManager run;
    [SerializeField] private DungeonMinimapView view;
    [SerializeField] private DungeonMinimapView fullMapView;

    private readonly DungeonMinimapModel _model = new DungeonMinimapModel();
    private readonly List<Vector2> _enemyPositions = new List<Vector2>(16);
    private readonly Dictionary<DungeonRewardChest, ChestMarkers> _chestMarkers = new();
    private readonly List<DungeonRewardChest> _expiredChests = new();
    private float _nextChestRefresh;
    private DungeonRewardNotice _rewardNotice;

    private struct ChestMarkers
    {
        public DungeonMinimapIconGraphic Minimap;
        public DungeonMinimapIconGraphic FullMap;
    }

    private DungeonRoomEncounter _enemyRoom;
    private int _enemyRoomId = -1;
    private RunManager _subscribedRun;
    private StageManager _subscribedStage;
    private DungeonMinimapView _subscribedFullMapView;
    private DungeonFastTravelService _travel;
    private BaekhoDungeonResult _result;
    private bool _tracking;
    private bool _mapReady;
    private bool _isFullMapOpen;
    private bool _closingFullMap;
    private int _closeRevision;
    private int _labelChapter = -1, _labelStage = -1;
    private string _stageLabel;

    public DungeonMinimapModel Model => _model;
    public DungeonMinimapView View => view;
    public DungeonMinimapView FullMapView => fullMapView;
    public bool IsFullMapOpen => _isFullMapOpen;
    public RunManager Run => run;

    public void Configure(RunManager targetRun, DungeonMinimapView targetView, DungeonMinimapView targetFullMapView = null)
    {
        CloseFullMap();
        Unsubscribe();
        ClearChestMarkers();
        if (view != null) { view.SetVisible(false); view.Clear(); }
        if (fullMapView != null) { fullMapView.SetVisible(false); fullMapView.Clear(); }
        run = targetRun;
        view = targetView;
        fullMapView = targetFullMapView != targetView ? targetFullMapView : null;
        _result = null;
        _model.Reset(null);
        _travel = run != null ? new DungeonFastTravelService(run, _model) : null;
        _tracking = false;
        HideMaps();
        if (isActiveAndEnabled) Subscribe();
    }

    public void ToggleFullMap()
    {
        if (_closingFullMap) return;
        if (_isFullMapOpen) { RequestCloseFullMap(); return; }
        if (!CanShowFullMap() || !run.TryOpenMap(this)) return;
        _isFullMapOpen = true;
        fullMapView.ShowTravelMessage(null);
        RefreshVisibility();
    }

    public void CloseFullMap()
    {
        ++_closeRevision;
        _closingFullMap = false;
        _isFullMapOpen = false;
        if (run != null && run.IsMapOpen) run.CloseMap();
        RefreshVisibility();
    }

    /// <summary>Normal user dismissal keeps gameplay paused through the visual exit.</summary>
    public void RequestCloseFullMap()
    {
        if (!_isFullMapOpen || _closingFullMap) return;
        if (!CanShowFullMap() || run == null || !run.IsMapSessionCurrent)
        { CloseFullMap(); return; }
        _closingFullMap = true;
        int revision = ++_closeRevision;
        fullMapView.PlayClose(() =>
        {
            if (revision == _closeRevision && _closingFullMap) CloseFullMap();
        });
    }

    private bool CanShowFullMap()
        => isActiveAndEnabled && _mapReady && fullMapView != null && fullMapView.isActiveAndEnabled
            && run != null && run.isActiveAndEnabled && run.State == RunState.Playing && !run.IsShopOpen
            && run.player != null && !run.player.IsStageTransitioning && !run.player.IsFinisherSelecting
            && run.stageManager != null && ReferenceEquals(_result, run.stageManager.Result);

    private void RefreshVisibility()
    {
        if (_isFullMapOpen && (!CanShowFullMap() || !run.IsMapOpen))
        {
            CloseFullMap();
            return;
        }
        if (view != null) view.SetVisible(_mapReady && !_isFullMapOpen);
        if (fullMapView != null) fullMapView.SetVisible(_mapReady && _isFullMapOpen);
    }

    private void HideMaps()
    {
        if (_rewardNotice != null) _rewardNotice.Hide();
        _stageLabel = null;
        if (view != null) view.SetStageLabel(string.Empty);
        _enemyRoom = null;
        _enemyRoomId = -1;
        _enemyPositions.Clear();
        if (view != null) view.SetEnemyPositions(null);
        _mapReady = false;
        CloseFullMap();
        if (view != null) view.SetVisible(false);
        if (fullMapView != null) fullMapView.SetVisible(false);
    }

    private void OnEnable()
    {
        _tracking = false;
        _model.ResetMotionHistory();
        HideMaps();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        _tracking = false;
        _model.ResetMotionHistory();
        HideMaps();
    }

    private void OnDestroy()
    {
        if (_rewardNotice != null) Destroy(_rewardNotice.gameObject);
    }

    private void Subscribe()
    {
        var stage = run != null ? run.stageManager : null;
        if (_subscribedRun == run && _subscribedStage == stage && _subscribedFullMapView == fullMapView) return;
        if (_subscribedRun != null && _subscribedRun.IsMapOpen) _subscribedRun.CloseMap();
        bool changedRun = _subscribedRun != run;
        Unsubscribe();
        _subscribedRun = run;
        _subscribedStage = stage;
        _subscribedFullMapView = fullMapView;
        if (_travel == null || changedRun)
        {
            _travel = run != null ? new DungeonFastTravelService(run, _model) : null;
            _travel?.Bind(_result);
        }
        if (_subscribedRun != null)
        {
            _subscribedRun.StateChanged += OnRunStateChanged;
            _subscribedRun.ShopOpenChanged += OnShopOpenChanged;
            _subscribedRun.MapOpenChanged += OnMapOpenChanged;
        }
        if (_subscribedStage != null) _subscribedStage.OnStageStart += OnStageStarted;
        if (_subscribedFullMapView != null)
        {
            _subscribedFullMapView.RoomClicked += OnRoomClicked;
            _subscribedFullMapView.Disabled += OnFullMapDisabled;
            _subscribedFullMapView.CloseRequested += RequestCloseFullMap;
            _subscribedFullMapView.SetTravelAvailability(GetTravelBlockReason);
        }
    }

    private void Unsubscribe()
    {
        if (_subscribedRun != null)
        {
            _subscribedRun.StateChanged -= OnRunStateChanged;
            _subscribedRun.ShopOpenChanged -= OnShopOpenChanged;
            _subscribedRun.MapOpenChanged -= OnMapOpenChanged;
        }
        if (_subscribedStage != null) _subscribedStage.OnStageStart -= OnStageStarted;
        if (_subscribedFullMapView != null)
        {
            _subscribedFullMapView.RoomClicked -= OnRoomClicked;
            _subscribedFullMapView.Disabled -= OnFullMapDisabled;
            _subscribedFullMapView.CloseRequested -= RequestCloseFullMap;
            _subscribedFullMapView.SetTravelAvailability(null);
        }
        _subscribedRun = null;
        _subscribedStage = null;
        _subscribedFullMapView = null;
    }

    private void OnRunStateChanged(RunState state)
    {
        if (state == RunState.Playing) return;
        _tracking = false;
        _model.ResetMotionHistory();
        HideMaps();
    }

    private void OnShopOpenChanged(bool open)
    {
        if (open) CloseFullMap();
    }

    private void OnMapOpenChanged(bool open)
    {
        if (!open)
        {
            ++_closeRevision;
            _closingFullMap = false;
        }
        _isFullMapOpen = open;
        RefreshVisibility();
    }

    private void OnFullMapDisabled() => CloseFullMap();

    private string GetTravelBlockReason(int roomId)
    {
        if (_travel == null) return "지도를 준비 중입니다.";
        return _travel.CanTravel(roomId, out string reason) ? null : reason;
    }

    private void OnRoomClicked(int roomId)
    {
        if (!_isFullMapOpen || _closingFullMap || fullMapView == null || fullMapView.IsAnimating
            || _travel == null || !CanShowFullMap()) return;
        if (!_travel.TryTravel(roomId, _result, out string reason))
        {
            fullMapView.ShowTravelMessage(reason);
            return;
        }
        // The service sampled the destination without revealing the intervening path.
        _tracking = false;
        view.RefreshExploration(_model);
        fullMapView.RefreshExploration(_model);
        UpdateMap();
        RequestCloseFullMap();
    }

    private void OnStageStarted()
    {
        // RunManager has already teleported the player before publishing this event.
        _tracking = false;
        _model.ResetMotionHistory();
        UpdateMap();
    }

    private void LateUpdate()
    {
        Subscribe();
        UpdateMap();
        // Input remains available while the run owns a paused map session.
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.escapeKey.wasPressedThisFrame) RequestCloseFullMap();
        else if (keyboard.tabKey.wasPressedThisFrame) ToggleFullMap();
    }

    private void UpdateMap()
    {
        var stage = run != null ? run.stageManager : null;
        var player = run != null ? run.player : null;
        var result = stage != null ? stage.Result : null;
        if (view == null || run == null || !run.isActiveAndEnabled || run.State != RunState.Playing || stage == null || !stage.IsReady
            || player == null || !player.isActiveAndEnabled || player.IsStageTransitioning
            || result == null || !result.success || result.root == null || result.plan == null
            || stage.Generator == null || stage.Generator.CurrentRoot != result.root)
        {
            HideMaps();
            _tracking = false;
            _model.ResetMotionHistory();
            return;
        }

        if (!ReferenceEquals(_result, result))
        {
            CloseFullMap();
            _result = result;
            _enemyRoom = null;
            _enemyRoomId = -1;
            ClearChestMarkers();
            _model.Reset(result.plan);
            _travel ??= new DungeonFastTravelService(run, _model);
            _travel.Bind(result);
            view.Build(_model);
            _stageLabel = null;
            if (fullMapView != null) fullMapView.Build(_model);
            AddMarker(result.shopRoom, result.plan.shopMapId, DungeonMinimapIconGraphic.Symbol.Shop);
            AddMarker(result.endRoom, result.plan.endMapId, DungeonMinimapIconGraphic.Symbol.Exit);
            AddBossMarker(result.plan.bossMapId, result.bossRoom);
            _tracking = false;
        }

        Transform root = result.root.transform;
        Vector3 position = root.InverseTransformPoint(player.transform.position);
        Vector3 forward = root.InverseTransformDirection(player.transform.forward);
        var point = new Vector2(position.x, position.z);
        if (_model.UpdatePosition(point, _tracking))
        {
            view.RefreshExploration(_model);
            if (fullMapView != null) fullMapView.RefreshExploration(_model);
        }
        var direction = new Vector2(forward.x, forward.z);
        view.SetPose(point, direction);
        if (fullMapView != null) fullMapView.SetPose(point, direction);
        _mapReady = true;
        UpdateStageLabel();
        RefreshVisibility();
        UpdateEnemies(stage, root);
        UpdateChestMarkers(stage.Encounters, root);
        _tracking = true;
    }

    private void UpdateStageLabel()
    {
        if (_stageLabel == null || _labelChapter != run.CurrentChapterIndex || _labelStage != run.CurrentStageIndex)
        {
            _labelChapter = run.CurrentChapterIndex;
            _labelStage = run.CurrentStageIndex;
            _stageLabel = run.ProgressLabel;
        }
        view.SetStageLabel(_stageLabel);
    }

    private void UpdateEnemies(StageManager stage, Transform root)
    {
        _enemyPositions.Clear();
        int index = _model.CurrentAreaIndex;
        var director = stage.Encounters;
        if (!view.IsVisible || index < 0 || index >= _model.Areas.Count
            || _model.Areas[index].Kind != DungeonMinimapAreaKind.Room
            || director == null || !director.isActiveAndEnabled || !director.IsReady)
        {
            _enemyRoom = null;
            _enemyRoomId = -1;
            view.SetEnemyPositions(null);
            return;
        }

        int roomId = _model.Areas[index].RoomId;
        if (_enemyRoom == null || _enemyRoomId != roomId || !_enemyRoom.transform.IsChildOf(root))
        {
            _enemyRoom = null;
            _enemyRoomId = roomId;
            // Resolve only when the room changes; never search every Enemy in the scene.
            foreach (var room in director.Rooms)
            {
                if (room == null || !room.transform.IsChildOf(root)) continue;
                var identity = room.GetComponent<BaekhoGeneratedRoom>();
                if (identity == null || identity.instanceId != roomId) continue;
                _enemyRoom = room;
                break;
            }
        }

        if (_enemyRoom != null && _enemyRoom.isActiveAndEnabled
            && (_enemyRoom.State == DungeonEncounterState.Waiting || _enemyRoom.State == DungeonEncounterState.Fighting))
        {
            var members = _enemyRoom.Members;
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (member == null || !member.isActiveAndEnabled || member.HasDied || member.IsDead
                    || member.Enemy == null || !member.Enemy.gameObject.activeInHierarchy) continue;
                // Dormant AI is still a living enemy in this room. Its world height is irrelevant to the map.
                Vector3 local = root.InverseTransformPoint(member.Enemy.transform.position);
                if (float.IsNaN(local.x) || float.IsInfinity(local.x) || float.IsNaN(local.z) || float.IsInfinity(local.z)) continue;
                _enemyPositions.Add(new Vector2(local.x, local.z));
            }
        }
        view.SetEnemyPositions(_enemyPositions);
    }

    private void AddBossMarker(int roomId, BaekhoMapModule room)
    {
        if (room == null) return;
        int area = _model.GetRoomAreaIndex(roomId);
        if (area < 0) return;
        Vector2 center = _model.Areas[area].Center;
        view.AddMarker(area, center, DungeonMinimapIconGraphic.Symbol.Boss);
        if (fullMapView != null) fullMapView.AddMarker(area, center, DungeonMinimapIconGraphic.Symbol.Boss);
    }

    private void UpdateChestMarkers(DungeonEncounterDirector director, Transform root)
    {
        // The chest is an immediate child of its generated encounter room.
        // Only poll cleared rooms, so regular movement never scans the scene hierarchy.
        foreach (var entry in _chestMarkers)
        {
            if (entry.Key != null && entry.Key.IsAvailable) continue;
            if (view != null) view.RemoveMarker(entry.Value.Minimap);
            if (fullMapView != null) fullMapView.RemoveMarker(entry.Value.FullMap);
            _expiredChests.Add(entry.Key);
        }
        foreach (var chest in _expiredChests) _chestMarkers.Remove(chest);
        _expiredChests.Clear();

        if (director == null || !director.IsReady || Time.unscaledTime < _nextChestRefresh) return;
        _nextChestRefresh = Time.unscaledTime + .2f;
        foreach (var room in director.Rooms)
        {
            if (room == null || !room.WasCombatClearedByDefeat || !room.transform.IsChildOf(root)) continue;
            var chestObject = room.transform.Find("CombatRewardChest");
            if (chestObject == null || !chestObject.TryGetComponent<DungeonRewardChest>(out var chest)
                || !chest.IsAvailable || _chestMarkers.ContainsKey(chest)) continue;
            var identity = room.GetComponent<BaekhoGeneratedRoom>();
            if (identity == null) continue;
            int area = _model.GetRoomAreaIndex(identity.instanceId);
            if (area < 0) continue;
            Vector3 local = root.InverseTransformPoint(chest.transform.position);
            var position = new Vector2(local.x, local.z);
            _chestMarkers.Add(chest, new ChestMarkers
            {
                Minimap = view.AddTrackedMarker(area, position, DungeonMinimapIconGraphic.Symbol.RewardChest),
                FullMap = fullMapView != null
                    ? fullMapView.AddTrackedMarker(area, position, DungeonMinimapIconGraphic.Symbol.RewardChest) : null
            });
            if (_rewardNotice == null) _rewardNotice = DungeonRewardNotice.Create();
            _rewardNotice.Show();
        }
    }

    private void ClearChestMarkers()
    {
        foreach (var markers in _chestMarkers.Values)
        {
            if (view != null) view.RemoveMarker(markers.Minimap);
            if (fullMapView != null) fullMapView.RemoveMarker(markers.FullMap);
        }
        _chestMarkers.Clear();
        _expiredChests.Clear();
        _nextChestRefresh = 0f;
        if (_rewardNotice != null) _rewardNotice.Hide();
    }

    private void AddMarker(BaekhoMapModule room, int roomId, DungeonMinimapIconGraphic.Symbol symbol)
    {
        if (room == null) return;
        int area = _model.GetRoomAreaIndex(roomId);
        var anchors = room.GetComponent<StageMapAnchors>();
        if (area < 0 || anchors == null) return;
        Transform anchor = symbol == DungeonMinimapIconGraphic.Symbol.Shop ? anchors.shopPoint : anchors.exitPoint;
        if (anchor == null) return;
        Vector3 local = _result.root.transform.InverseTransformPoint(anchor.position);
        view.AddMarker(area, new Vector2(local.x, local.z), symbol);
        if (fullMapView != null) fullMapView.AddMarker(area, new Vector2(local.x, local.z), symbol);
    }
}
