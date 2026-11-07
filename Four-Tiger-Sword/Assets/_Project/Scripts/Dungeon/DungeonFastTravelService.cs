using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;

/// <summary>Authoritative checks for a single current stage; views only request a room ID.</summary>
public sealed class DungeonFastTravelService
{
    private readonly RunManager _run;
    private readonly DungeonMinimapModel _model;
    private readonly Dictionary<int, BaekhoMapModule> _rooms = new();
    private BaekhoDungeonResult _result;
    private StageManager _stage;
    private PlayerController _player;
    private int _chapterIndex, _stageIndex;

    public DungeonFastTravelService(RunManager run, DungeonMinimapModel model) { _run = run; _model = model; }

    public void Bind(BaekhoDungeonResult result)
    {
        _rooms.Clear();
        _result = result;
        _stage = _run != null ? _run.stageManager : null;
        _player = _run != null ? _run.player : null;
        _chapterIndex = _run != null ? _run.CurrentChapterIndex : -1;
        _stageIndex = _run != null ? _run.CurrentStageIndex : -1;
        if (result == null) return;
        foreach (var room in result.rooms)
        {
            var identity = room != null ? room.GetComponent<BaekhoGeneratedRoom>() : null;
            if (identity != null && !_rooms.ContainsKey(identity.instanceId)) _rooms.Add(identity.instanceId, room);
        }
    }

    public bool CanTravel(int roomId, out string reason) => Resolve(roomId, out _, out _, out reason);

    public bool TryTravel(int roomId, BaekhoDungeonResult expectedResult, out string reason)
    {
        if (!ReferenceEquals(expectedResult, _result)) return Fail("지도가 변경되었습니다. 다시 열어 주세요.", out reason);
        if (!Resolve(roomId, out var position, out var rotation, out reason)) return false;
        if (!_player.TryFastTravel(position, rotation)) return Fail("현재 플레이어 상태에서는 이동할 수 없습니다.", out reason);
        _stage.Encounters.ResetPlayerMotionHistory();
        _model.ResetMotionHistory();
        var local = _result.root.transform.InverseTransformPoint(position);
        _model.UpdatePosition(new Vector2(local.x, local.z), false);
        return true;
    }

    private bool Resolve(int roomId, out Vector3 position, out Quaternion rotation, out string reason)
    {
        position = default;
        rotation = Quaternion.identity;
        reason = null;
        if (_run == null || !_run.isActiveAndEnabled || _run.State != RunState.Playing || !_run.IsMapSessionCurrent || _run.IsShopOpen
            || _stage == null || _stage != _run.stageManager || !_stage.IsReady || _player == null || _player != _run.player
            || !_player.isActiveAndEnabled || _player.IsStageTransitioning || _player.IsShopPaused || _player.IsFinisherSelecting
            || _player.Controller == null || !_player.Controller.enabled || _player.StatManager == null || _player.StatManager.CurrentHp <= 0f
            || _run.CurrentChapterIndex != _chapterIndex || _run.CurrentStageIndex != _stageIndex)
            return Fail("현재 상태에서는 순간이동할 수 없습니다.", out reason);
        if (_result == null || !_result.success || _result.plan == null || _result.root == null || !_result.root.activeInHierarchy
            || !ReferenceEquals(_stage.Result, _result) || _stage.Generator == null || _stage.Generator.IsGenerating
            || _stage.Generator.CurrentRoot != _result.root || !ReferenceEquals(_stage.Generator.LastResult, _result))
            return Fail("현재 스테이지와 지도가 일치하지 않습니다.", out reason);
        var director = _stage.Encounters;
        if (director == null || !director.isActiveAndEnabled || !director.IsReady || !string.IsNullOrEmpty(director.Error)
            || director.Player != _player.Controller)
            return Fail("방 상태를 확인할 수 없습니다.", out reason);
        foreach (var encounter in director.Rooms)
            if (encounter != null && encounter.IsCombatActive) return Fail("전투 중에는 이동할 수 없습니다.", out reason);
        if (_player.IsInRoomCombat) return Fail("전투 중에는 이동할 수 없습니다.", out reason);
        int index = _model != null ? _model.GetRoomAreaIndex(roomId) : -1;
        if (index < 0 || _model.Areas[index].Kind != DungeonMinimapAreaKind.Room || !_model.Areas[index].Visited)
            return Fail("방문한 방만 선택할 수 있습니다.", out reason);
        if (index == _model.CurrentAreaIndex) return Fail("현재 있는 방입니다.", out reason);
        if (!_rooms.TryGetValue(roomId, out var room) || room == null || !room.gameObject.activeInHierarchy
            || !room.transform.IsChildOf(_result.root.transform)) return Fail("이 방은 현재 스테이지에 없습니다.", out reason);
        var roomEncounter = room.GetComponent<DungeonRoomEncounter>();
        if (roomEncounter == null || !roomEncounter.isActiveAndEnabled || roomEncounter.State != DungeonEncounterState.Cleared
            || !Contains(director.Rooms, roomEncounter))
            return Fail("전투가 끝난 방으로만 이동할 수 있습니다.", out reason);
        var anchors = room.GetComponent<StageMapAnchors>();
        Physics.SyncTransforms();
        return DungeonFastTravelLanding.TryResolve(room, anchors != null ? anchors.teleportPoint : null,
            _player.Controller, out position, out rotation, out reason);
    }

    private static bool Contains(IReadOnlyList<DungeonRoomEncounter> rooms, DungeonRoomEncounter target)
    {
        foreach (var room in rooms) if (room == target) return true;
        return false;
    }
    private static bool Fail(string value, out string reason) { reason = value; return false; }
}
