using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Resolves safe return poses within one generated stage.</summary>
public sealed class DungeonFallRecovery
{
    private const float StableGroundTime = .2f;
    private const float SampleInterval = .1f;
    private const float FootProbeHeight = .25f;
    private const float FootProbeDistance = .45f;
    private static readonly float[] SearchRadii = { .75f, 1.5f, 3f };
    private static readonly Vector3[] SearchDirections =
    {
        Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
        new(.7071f, 0f, .7071f), new(-.7071f, 0f, .7071f),
        new(.7071f, 0f, -.7071f), new(-.7071f, 0f, -.7071f)
    };

    private sealed class RoomSpace
    {
        public readonly BaekhoMapModule Module;
        public readonly HashSet<Transform> Ground;
        public Pose? Fallback;

        public RoomSpace(BaekhoMapModule module)
        {
            Module = module;
            Ground = DungeonFastTravelLanding.GroundRoots(module);
            // Legacy modules can still identify their floor by the Ground layer and hierarchy.
            if (Ground.Count == 0) Ground.Add(module.transform);
        }
    }

    private readonly struct Sample
    {
        public readonly RoomSpace Room;
        public readonly Pose Pose;

        public Sample(RoomSpace room, Pose pose) { Room = room; Pose = pose; }
    }

    private readonly StageManager _stage;
    private readonly PlayerController _player;
    private readonly Dictionary<BaekhoMapModule, RoomSpace> _rooms = new();
    private readonly HashSet<Collider> _deadZones = new();
    private readonly Collider[] _overlaps = new Collider[128];
    private readonly Sample[] _history = new Sample[8];
    private readonly int _groundMask = LayerMask.GetMask("Ground");
    private Collider[] _deadZoneOverlaps = new Collider[16];
    private RaycastHit[] _deadZoneHits = new RaycastHit[16];
    private BaekhoDungeonResult _result;
    private PhysicsScene _physics;
    private float _minimumWorldY, _groundedTime, _sampleWait;
    private int _nextSample, _sampleCount, _deadZoneMask;
    private bool _prepared, _hasDeadZoneMotion;
    private Vector3 _previousCapsuleLower, _previousCapsuleUpper;
    private float _previousCapsuleRadius;
    private RoomSpace _startRoom, _lastGroundRoom, _lockedRoom;
    private Pose _stageSpawn;

    public DungeonFallRecovery(StageManager stage, PlayerController player)
    { _stage = stage; _player = player; }

    public string LastError { get; private set; }
    public bool IsValid => _prepared && _stage != null && _stage.IsReady && _player != null
        && _player.Controller != null && IsCurrentResult();

    public bool TryPrepare(out string reason)
    {
        _prepared = false;
        _result = _stage != null ? _stage.Result : null;
        _rooms.Clear();
        _deadZones.Clear();
        _deadZoneMask = 0;
        _startRoom = null;
        ResetHistory();
        reason = null;
        if (_player == null || _player.Controller == null || !IsCurrentResult() || _groundMask == 0)
        { reason = "Fall recovery needs the generated stage, a player capsule, and the Ground layer."; return false; }

        _physics = _result.root.scene.GetPhysicsScene();
        if (!_physics.IsValid())
        { reason = "Fall recovery could not locate the stage's physics scene."; return false; }

        _minimumWorldY = float.PositiveInfinity;
        foreach (var module in _result.root.GetComponentsInChildren<BaekhoMapModule>())
        {
            var bounds = module.localBounds;
            var center = module.transform.TransformPoint(bounds.center);
            var extent = bounds.extents;
            float minY = center.y
                - Mathf.Abs(module.transform.TransformVector(Vector3.right * extent.x).y)
                - Mathf.Abs(module.transform.TransformVector(Vector3.up * extent.y).y)
                - Mathf.Abs(module.transform.TransformVector(Vector3.forward * extent.z).y);
            if (!KnockbackMotion.IsFinite(minY) || extent.x <= 0f || extent.y < 0f || extent.z <= 0f)
            { reason = $"Fall recovery found invalid bounds in {module.name}."; return false; }
            _minimumWorldY = Mathf.Min(_minimumWorldY, minY);
            _rooms.Add(module, new RoomSpace(module));
        }
        if (_result.startRoom == null || !_rooms.TryGetValue(_result.startRoom, out _startRoom))
        { reason = "Fall recovery could not locate the start room."; return false; }

        RegisterDeadZones();
        Physics.SyncTransforms();
        foreach (var module in _result.rooms)
        {
            if (module == null || !_rooms.TryGetValue(module, out var room))
            { reason = "Fall recovery found a room outside the generated stage."; return false; }
            var anchors = module.GetComponent<StageMapAnchors>();
            var point = anchors != null ? anchors.teleportPoint : null;
            if ((DungeonFastTravelLanding.ValidateAnchor(module, point, out _)
                    && TryFindNear(room, new Pose(point.position, point.rotation), out var pose))
                || TryFindRoomFloor(room, out pose))
                room.Fallback = pose;
            else
            {
                var encounter = module.GetComponent<DungeonRoomEncounter>();
                if (encounter != null && (encounter.State == DungeonEncounterState.Waiting || encounter.IsCombatActive))
                { reason = $"Fall recovery could not find a safe position in combat room {module.name}."; return false; }
            }
        }

        var spawn = new Pose(_stage.PlayerSpawnPosition, _stage.PlayerSpawnRotation);
        if (!TryFindNear(_startRoom, spawn, out _stageSpawn))
        {
            if (!_startRoom.Fallback.HasValue)
            { reason = "Fall recovery could not find a safe position in the start room."; return false; }
            _stageSpawn = _startRoom.Fallback.Value;
        }
        _prepared = true;
        return true;
    }

    public bool HasEnteredDeadZone()
    {
        if (_deadZoneMask == 0 || !IsValid || !_player.Controller.enabled
            || !DungeonFastTravelLanding.TryGetCapsuleSegment(_player.Controller, PlayerPose(),
                out var lower, out var upper, out float radius))
        {
            _hasDeadZoneMotion = false;
            return false;
        }

        // The endpoint overlap also covers the first frame after spawning or warping.
        bool entered = OverlapsDeadZone(lower, upper, radius) || (_hasDeadZoneMotion
            && SweepsDeadZone((lower - _previousCapsuleLower + upper - _previousCapsuleUpper) * .5f));
        _previousCapsuleLower = lower;
        _previousCapsuleUpper = upper;
        _previousCapsuleRadius = radius;
        _hasDeadZoneMotion = true;
        return entered;
    }

    public bool IsBelowVoid(float margin)
    {
        return IsValid && KnockbackMotion.IsFinite(margin)
            && DungeonFastTravelLanding.TryGetCapsuleBottom(_player.Controller, PlayerPose(), out var bottom)
            && bottom.y < _minimumWorldY - Mathf.Max(0f, margin);
    }

    public void TickSafePosition(float deltaTime)
    {
        if (!IsValid || !_player.Controller.enabled || deltaTime <= 0f || !KnockbackMotion.IsFinite(deltaTime)) return;
        var pose = PlayerPose();
        if (_player.VerticalVelocity > 0f
            || !DungeonFastTravelLanding.TryGetCapsuleBottom(_player.Controller, pose, out var bottom)
            || !TryProbeFloor(bottom, FootProbeHeight, FootProbeDistance, out var floor))
        {
            _groundedTime = _sampleWait = 0f;
            return;
        }

        var module = floor.collider.GetComponentInParent<BaekhoMapModule>();
        RoomSpace room = null;
        if (module != null && (!_rooms.TryGetValue(module, out room) || !OwnsFloor(room, floor.collider)))
        {
            _groundedTime = _sampleWait = 0f;
            return;
        }
        if (room != _lastGroundRoom) _groundedTime = _sampleWait = 0f;
        _lastGroundRoom = room;
        _groundedTime += deltaTime;
        _sampleWait -= deltaTime;
        if (_groundedTime < StableGroundTime || _sampleWait > 0f) return;
        _sampleWait = SampleInterval;

        pose.position = DungeonFastTravelLanding.PlayerRootAtFloor(floor.point, pose.rotation, _player.Controller);
        if (!ValidatePose(room, pose, out _)) return;
        int last = (_nextSample + _history.Length - 1) % _history.Length;
        if (_sampleCount > 0 && _history[last].Room == room
            && (_history[last].Pose.position - pose.position).sqrMagnitude < .0625f)
            _history[last] = new Sample(room, pose);
        else
        {
            _history[_nextSample] = new Sample(room, pose);
            _nextSample = (_nextSample + 1) % _history.Length;
            _sampleCount = Mathf.Min(_sampleCount + 1, _history.Length);
        }
    }

    public bool TryGetRecoveryPose(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        LastError = null;
        if (!IsValid) { LastError = "The generated stage changed during fall recovery."; return false; }
        // A door can lock after the first candidate check but before the covered-frame recheck.
        // Once a combat room is captured, retain it until this recovery has finished.
        _lockedRoom ??= FindCombatRoom();

        Physics.SyncTransforms();
        Pose pose;
        for (int offset = 1; offset <= _sampleCount; offset++)
        {
            var sample = _history[(_nextSample + _history.Length - offset) % _history.Length];
            if ((_lockedRoom == null || sample.Room == _lockedRoom) && ValidatePose(sample.Room, sample.Pose, out _))
            {
                position = sample.Pose.position;
                rotation = sample.Pose.rotation;
                return true;
            }
        }

        var room = _lockedRoom ?? _lastGroundRoom;
        if ((room?.Fallback is Pose fallback && TryFindNear(room, fallback, out pose))
            || (_lockedRoom == null && TryFindNear(_startRoom, _stageSpawn, out pose)))
        {
            position = pose.position;
            rotation = pose.rotation;
            return true;
        }

        LastError = _lockedRoom != null ? $"No clear fall recovery position in {_lockedRoom.Module.name}."
            : "No clear fall recovery position in the generated stage.";
        return false;
    }

    public void ResetHistory()
    {
        _nextSample = _sampleCount = 0;
        _groundedTime = _sampleWait = 0f;
        _lockedRoom = _lastGroundRoom = null;
        _hasDeadZoneMotion = false;
        LastError = null;
    }

    public void NotifyWarp()
    {
        if (!IsValid) return;
        ResetHistory();
        _stage.Encounters?.ResetPlayerMotionHistory();
        foreach (var map in Object.FindObjectsByType<DungeonMinimapPresenter>(FindObjectsSortMode.None))
            if (map.Run != null && map.Run.stageManager == _stage && map.Run.player == _player)
                map.Model.ResetMotionHistory();
    }

    internal void ReportFailure(string reason)
    {
        if (IsValid) _stage.ReportFallRecoveryFailure(reason ?? LastError);
    }

    private bool IsCurrentResult() => _result != null && _result.success && _result.root != null
        && _result.root.activeInHierarchy && _stage != null && ReferenceEquals(_stage.Result, _result)
        && _stage.Generator != null && !_stage.Generator.IsGenerating && _stage.Generator.CurrentRoot == _result.root;

    private Pose PlayerPose() => new(_player.transform.position, _player.transform.rotation);

    private void RegisterDeadZones()
    {
        foreach (var asset in _result.root.GetComponentsInChildren<BaekhoMapAsset>(true))
        {
            if (asset.role != BaekhoMapAsset.AssetRole.Deadzone) continue;
            foreach (var collider in asset.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.isTrigger || collider.GetComponentInParent<BaekhoMapAsset>(true) != asset) continue;
                _deadZones.Add(collider);
                _deadZoneMask |= 1 << collider.gameObject.layer;
            }
        }
    }

    private bool IsActiveDeadZone(Collider collider) => collider != null && _deadZones.Contains(collider)
        && collider.enabled && collider.isTrigger && collider.gameObject.activeInHierarchy
        && collider.transform.IsChildOf(_result.root.transform);

    private bool OverlapsDeadZone(Vector3 lower, Vector3 upper, float radius)
    {
        while (true)
        {
            int count = _physics.OverlapCapsule(lower, upper, radius, _deadZoneOverlaps, _deadZoneMask,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
                if (IsActiveDeadZone(_deadZoneOverlaps[i])) return true;
            if (count < _deadZoneOverlaps.Length) return false;
            // The layer may also contain unrelated colliders. Requery so saturation cannot hide a deadzone.
            System.Array.Resize(ref _deadZoneOverlaps, _deadZoneOverlaps.Length * 2);
        }
    }

    private bool SweepsDeadZone(Vector3 displacement)
    {
        float distance = displacement.magnitude;
        if (distance <= .0001f || !KnockbackMotion.IsFinite(distance)) return false;
        var direction = displacement / distance;
        while (true)
        {
            int count = _physics.CapsuleCast(_previousCapsuleLower, _previousCapsuleUpper, _previousCapsuleRadius,
                direction, _deadZoneHits, distance, _deadZoneMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
                if (IsActiveDeadZone(_deadZoneHits[i].collider)) return true;
            if (count < _deadZoneHits.Length) return false;
            System.Array.Resize(ref _deadZoneHits, _deadZoneHits.Length * 2);
        }
    }

    private RoomSpace FindCombatRoom()
    {
        RoomSpace first = null;
        if (_stage.Encounters == null) return null;
        foreach (var encounter in _stage.Encounters.Rooms)
        {
            if (encounter == null || !encounter.IsCombatActive) continue;
            var module = encounter.GetComponent<BaekhoMapModule>();
            if (module == null || !_rooms.TryGetValue(module, out var room)) continue;
            if (room == _lastGroundRoom) return room;
            first ??= room;
        }
        return first;
    }

    private bool TryFindNear(RoomSpace room, Pose reference, out Pose pose)
    {
        pose = default;
        if (!DungeonFastTravelLanding.TryGetCapsuleBottom(_player.Controller, reference, out var bottom)) return false;
        if (TrySnap(room, bottom, reference.rotation, out pose)) return true;
        foreach (float radius in SearchRadii)
            foreach (var direction in SearchDirections)
                if (TrySnap(room, bottom + direction * radius, reference.rotation, out pose)) return true;
        return false;
    }

    private bool TrySnap(RoomSpace room, Vector3 bottom, Quaternion rotation, out Pose pose)
    {
        pose = default;
        return TryProbeFloor(bottom, .65f, 1.3f, out var floor) && OwnsFloor(room, floor.collider)
            && TryMakePose(room, floor.point, rotation, out pose);
    }

    private bool TryFindRoomFloor(RoomSpace room, out Pose pose)
    {
        pose = default;
        foreach (var ground in room.Ground)
        {
            foreach (var collider in ground.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || !IsGroundLayer(collider)) continue;
                var bounds = collider.bounds;
                var origin = new Vector3(bounds.center.x, bounds.max.y + .25f, bounds.center.z);
                float distance = bounds.size.y + .5f;
                if (TryColliderFloor(room, collider, origin, distance, out pose)) return true;
                foreach (var direction in SearchDirections)
                {
                    var offset = Vector3.Scale(direction, bounds.extents) * .5f;
                    if (TryColliderFloor(room, collider, origin + offset, distance, out pose)) return true;
                }
            }
        }
        return false;
    }

    private bool TryColliderFloor(RoomSpace room, Collider collider, Vector3 origin, float distance, out Pose pose)
    {
        pose = default;
        return collider.Raycast(new Ray(origin, Vector3.down), out var floor, distance)
            && TryMakePose(room, floor.point, room.Module.transform.rotation, out pose);
    }

    private bool TryMakePose(RoomSpace room, Vector3 floor, Quaternion rotation, out Pose pose)
    {
        pose = new Pose(DungeonFastTravelLanding.PlayerRootAtFloor(floor, rotation, _player.Controller), rotation);
        return ValidatePose(room, pose, out _);
    }

    private bool TryProbeFloor(Vector3 bottom, float height, float distance, out RaycastHit hit) =>
        _physics.Raycast(bottom + Vector3.up * height, Vector3.down, out hit, distance, _groundMask,
            QueryTriggerInteraction.Ignore) && hit.collider.transform.IsChildOf(_result.root.transform)
        && Vector3.Angle(hit.normal, Vector3.up) <= Mathf.Min(_player.Controller.slopeLimit, 45f);

    private bool IsGroundLayer(Collider collider) => (_groundMask & (1 << collider.gameObject.layer)) != 0;

    private static bool OwnsFloor(RoomSpace room, Collider collider) => room != null
        && DungeonFastTravelLanding.OwnsGround(collider.transform, room.Module.transform, room.Ground);

    private bool ValidatePose(RoomSpace room, Pose pose, out string reason)
    {
        if (!DungeonFastTravelLanding.ValidateRecoveryPose(_physics, room?.Module, room?.Ground, _result.root.transform,
            _player.Controller, pose, _overlaps, _groundMask, out reason)) return false;
        if (_deadZoneMask != 0 && DungeonFastTravelLanding.TryGetCapsuleSegment(_player.Controller, pose,
            out var lower, out var upper, out float radius) && OverlapsDeadZone(lower, upper, radius))
        { reason = "안전지점이 추락 구역과 겹칩니다."; return false; }
        return true;
    }
}
