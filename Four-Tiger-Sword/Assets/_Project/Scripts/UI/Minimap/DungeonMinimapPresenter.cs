using StudioBaekho;
using UnityEngine;

/// <summary>Connects the run-owned stage to a presentation-only exploration map.</summary>
[DisallowMultipleComponent]
public sealed class DungeonMinimapPresenter : MonoBehaviour
{
    [SerializeField] private RunManager run;
    [SerializeField] private DungeonMinimapView view;

    private readonly DungeonMinimapModel _model = new DungeonMinimapModel();
    private RunManager _subscribedRun;
    private StageManager _subscribedStage;
    private BaekhoDungeonResult _result;
    private bool _tracking;

    public DungeonMinimapModel Model => _model;
    public DungeonMinimapView View => view;
    public RunManager Run => run;

    public void Configure(RunManager targetRun, DungeonMinimapView targetView)
    {
        Unsubscribe();
        if (view != null) { view.SetVisible(false); view.Clear(); }
        run = targetRun;
        view = targetView;
        _result = null;
        _model.Reset(null);
        _tracking = false;
        if (view != null) view.SetVisible(false);
        if (isActiveAndEnabled) Subscribe();
    }

    private void OnEnable()
    {
        _tracking = false;
        _model.ResetMotionHistory();
        if (view != null) view.SetVisible(false);
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        _tracking = false;
        _model.ResetMotionHistory();
        if (view != null) view.SetVisible(false);
    }

    private void Subscribe()
    {
        var stage = run != null ? run.stageManager : null;
        if (_subscribedRun == run && _subscribedStage == stage) return;
        Unsubscribe();
        _subscribedRun = run;
        _subscribedStage = stage;
        if (_subscribedRun != null) _subscribedRun.StateChanged += OnRunStateChanged;
        if (_subscribedStage != null) _subscribedStage.OnStageStart += OnStageStarted;
    }

    private void Unsubscribe()
    {
        if (_subscribedRun != null) _subscribedRun.StateChanged -= OnRunStateChanged;
        if (_subscribedStage != null) _subscribedStage.OnStageStart -= OnStageStarted;
        _subscribedRun = null;
        _subscribedStage = null;
    }

    private void OnRunStateChanged(RunState state)
    {
        if (state == RunState.Playing) return;
        _tracking = false;
        _model.ResetMotionHistory();
        if (view != null) view.SetVisible(false);
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
    }

    private void UpdateMap()
    {
        var stage = run != null ? run.stageManager : null;
        var player = run != null ? run.player : null;
        var result = stage != null ? stage.Result : null;
        if (view == null) return;
        if (run == null || run.State != RunState.Playing || stage == null || !stage.IsReady
            || player == null || !player.isActiveAndEnabled || player.IsStageTransitioning
            || result == null || !result.success || result.root == null || result.plan == null
            || stage.Generator == null || stage.Generator.CurrentRoot != result.root)
        {
            view.SetVisible(false);
            _tracking = false;
            _model.ResetMotionHistory();
            return;
        }

        if (!ReferenceEquals(_result, result))
        {
            _result = result;
            _model.Reset(result.plan);
            view.Build(_model);
            AddMarker(result.shopRoom, result.plan.shopMapId, DungeonMinimapIconGraphic.Symbol.Shop);
            AddMarker(result.endRoom, result.plan.endMapId, DungeonMinimapIconGraphic.Symbol.Exit);
            _tracking = false;
        }

        Transform root = result.root.transform;
        Vector3 position = root.InverseTransformPoint(player.transform.position);
        Vector3 forward = root.InverseTransformDirection(player.transform.forward);
        var point = new Vector2(position.x, position.z);
        if (_model.UpdatePosition(point, _tracking)) view.RefreshExploration(_model);
        view.SetPose(point, new Vector2(forward.x, forward.z));
        view.SetVisible(true);
        _tracking = true;
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
    }
}
