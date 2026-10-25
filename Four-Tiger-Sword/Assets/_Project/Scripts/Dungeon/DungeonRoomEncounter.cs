using System;
using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

public enum DungeonEncounterState { Preparing, Waiting, Fighting, Cleared, Failed }

[DisallowMultipleComponent]
public sealed class DungeonRoomEncounter : MonoBehaviour
{
    public DungeonEncounterState State { get; private set; } = DungeonEncounterState.Preparing;
    public int AliveCount => _alive.Count;
    public bool IsCombatActive => isActiveAndEnabled && !_disposed && !_suspended
        && State == DungeonEncounterState.Fighting && _alive.Count > 0;
    public int SpentBudget { get; private set; }
    public int RemainingBudget { get; private set; }
    public string Error { get; private set; }
    public bool WasEntered { get; private set; }
    public bool BossDefeated { get; private set; }
    public IReadOnlyList<DungeonEncounterMember> Members => _members;
    public IReadOnlyList<DungeonEncounterGate> Gates => _gates;
    public event Action<DungeonRoomEncounter> Cleared;
    private readonly List<DungeonEncounterMember> _members = new();
    private readonly HashSet<DungeonEncounterMember> _alive = new();
    private readonly List<DungeonEncounterGate> _gates = new();
    private readonly List<DungeonEncounterMember> _removed = new();
    private DungeonEncounterDirector _director;
    private NavMeshData _ownedNavigation;
    private GameObject _enemyRoot;
    private Vector3 _previousCenter;
    private bool _hasPrevious;
    private bool _disposed;
    private bool _suspended;
    private bool _isBoss;
    private DungeonEncounterMember _bossMember;

    public void Initialize(DungeonEncounterDirector director, BaekhoMapModule module,
        Dictionary<int, float> usedSockets, int seed, GameObject bossPrefab = null, int bossExitSocketIndex = -1)
    {
        if (_director != null) return;
        _director = director;
        RemainingBudget = module.encounterBudget;
        var identity = module.GetComponent<BaekhoGeneratedRoom>();
        _isBoss = identity != null && (identity.role & BaekhoRoomRole.Boss) != 0;
        try
        {
            // Authored enemy markers or a copied encounter budget must never turn a shop into combat.
            if (identity != null && (identity.role & BaekhoRoomRole.Shop) != 0)
            {
                _isBoss = false;
                RemainingBudget = 0;
                CreateGates(module, usedSockets, director.doorPrefab, -1);
                foreach (var gate in _gates) gate.Open(true);
                State = DungeonEncounterState.Cleared;
                return;
            }
            var prefabs = new List<GameObject>();
            StageMapAnchors anchors = null;
            if (_isBoss)
            {
                anchors = module.GetComponent<StageMapAnchors>();
                if (bossPrefab == null) throw new InvalidOperationException("The boss stage has no boss prefab.");
                if (anchors == null || anchors.bossSpawn == null)
                    throw new InvalidOperationException("The boss room needs StageMapAnchors.bossSpawn.");
                if (!usedSockets.ContainsKey(bossExitSocketIndex))
                    throw new InvalidOperationException("The boss room has no connection to its end room.");
                prefabs.Add(bossPrefab);
            }
            bool hasMarkers = false;
            foreach (var marker in module.GetComponentsInChildren<BaekhoGameplayMarker>(true))
                if (marker.kind == "enemy") { hasMarkers = true; break; }
            if (module.encounterBudget < 0) throw new InvalidOperationException("Encounter budget cannot be negative.");
            if (!_isBoss && hasMarkers && module.encounterBudget > 0)
            {
                if (director.catalog == null) throw new InvalidOperationException("Dungeon enemy catalog is missing.");
                var plan = BaekhoEncounterPlanner.Plan(module.encounterBudget, director.catalog.GetValidatedCosts(), seed);
                SpentBudget = plan.Spent;
                RemainingBudget = plan.Remaining;
                if (plan.BalanceRelaxed)
                    Debug.LogWarning($"[Dungeon Encounter] {name}: budget {module.encounterBudget} cannot support a balanced mix; composition constraints relaxed.", this);
                for (int i = 0; i < plan.Counts.Length; i++)
                    for (int n = 0; n < plan.Counts[i]; n++) prefabs.Add(director.catalog.enemies[i].prefab);
            }
            if (prefabs.Count > 0 && !DungeonEncounterPlacement.TryPrepareNavigation(module, out _ownedNavigation, out var navError))
                throw new InvalidOperationException(navError);
            // Navigation is prepared before adding dynamic gate geometry.
            CreateGates(module, usedSockets, director.doorPrefab, _isBoss ? bossExitSocketIndex : -1);
            if (prefabs.Count == 0) { State = DungeonEncounterState.Cleared; return; }
            Physics.SyncTransforms();
            List<DungeonEnemyPlacement> placements;
            if (_isBoss)
            {
                if (!DungeonEncounterPlacement.TryPlanBoss(module, bossPrefab, anchors.bossSpawn, out var placement, out var bossError))
                    throw new InvalidOperationException(bossError);
                placements = new List<DungeonEnemyPlacement> { placement };
            }
            else if (!DungeonEncounterPlacement.TryPlan(module, prefabs, seed, director.spawnRadius, out placements, out var placementError))
                throw new InvalidOperationException(placementError);
            _enemyRoot = new GameObject("EncounterEnemies");
            _enemyRoot.SetActive(false);
            _enemyRoot.transform.SetParent(transform, false);
            foreach (var placement in placements)
            {
                var clone = Instantiate(placement.prefab, placement.position, placement.rotation, _enemyRoot.transform);
                var member = clone.AddComponent<DungeonEncounterMember>();
                _members.Add(member);
                member.Initialize(this);
                _alive.Add(member);
                if (_isBoss) _bossMember = member;
            }
            _enemyRoot.SetActive(true); // EnemyStat.Awake runs; Enemy.Start waits for room entry.
            foreach (var member in _members)
                if (member.IsDead) throw new InvalidOperationException($"{member.name} has no positive starting HP.");
            State = DungeonEncounterState.Waiting;
            Debug.Log($"[Dungeon Encounter] {name}: {_members.Count} monsters, budget {SpentBudget}/{module.encounterBudget} (remaining {RemainingBudget}).", this);
        }
        catch (Exception exception)
        {
            // An authored door must not strand the player even when earlier setup failed.
            foreach (int index in usedSockets.Keys)
            {
                var authored = module.connections[index].runtimeDoor;
                if (authored == null) continue;
                authored.SetActive(true);
                authored.GetComponentInChildren<LabDoor.SlidingDoor>(true)?.SetOpenImmediate(true);
            }
            Fail(exception.Message);
        }
    }

    private void CreateGates(BaekhoMapModule module, Dictionary<int, float> sockets, GameObject prefab,
        int lockedSocketIndex)
    {
        foreach (var pair in sockets)
        {
            var socket = module.connections[pair.Key];
            var gateObject = new GameObject("EncounterGate_" + socket.id);
            gateObject.transform.SetParent(transform, false);
            var gate = gateObject.AddComponent<DungeonEncounterGate>();
            _gates.Add(gate);
            gate.Initialize(socket, Mathf.Min(socket.width, pair.Value), prefab);
            // Keep the route to the end room closed even before the boss encounter begins.
            if (pair.Key == lockedSocketIndex) gate.Lock();
        }
    }

    public bool BeginEncounter()
    {
        if (_disposed || _suspended || State != DungeonEncounterState.Waiting) return false;
        PruneRemoved();
        if (State != DungeonEncounterState.Waiting) return false;
        if (_alive.Count == 0) { Complete(); return false; }
        foreach (var member in _alive)
            if (!member.CanActivate(out var error)) { Fail(error); return false; }
        foreach (var gate in _gates) gate.Lock();
        WasEntered = true;
        State = DungeonEncounterState.Fighting;
        foreach (var member in _alive)
            if (!member.Activate(out var error)) { Fail(error); return false; }
        return true;
    }

    public void NotifyEnemyRemoved(DungeonEncounterMember member)
    {
        if (_disposed || _suspended || !_alive.Remove(member)) return;
        if (_isBoss)
        {
            if (member != null && member == _bossMember && member.HasDied && WasEntered &&
                State == DungeonEncounterState.Fighting) BossDefeated = true;
            else
            {
                Fail("The boss was removed without being defeated in its encounter.");
                return;
            }
        }
        if (_alive.Count == 0 && (State == DungeonEncounterState.Waiting || State == DungeonEncounterState.Fighting)) Complete();
    }

    /// <summary>Stop combat during transition/end without treating frozen members as defeated.</summary>
    public void SuspendEncounter()
    {
        if (_disposed || _suspended) return;
        _suspended = true;
        _hasPrevious = false;
        foreach (var member in _members) if (member != null) member.Freeze();
    }

    private void Update()
    {
        if (_disposed || _suspended || State == DungeonEncounterState.Preparing || State == DungeonEncounterState.Failed) return;
        if (State == DungeonEncounterState.Waiting || State == DungeonEncounterState.Fighting)
        {
            PruneRemoved();
            if (State == DungeonEncounterState.Failed) return;
            if (_alive.Count == 0) Complete();
        }
        if (State == DungeonEncounterState.Fighting || WasEntered) return;
        var player = _director != null ? _director.Player : null;
        if (player == null || !player.enabled || !player.gameObject.activeInHierarchy) { _hasPrevious = false; return; }
        var center = player.transform.TransformPoint(player.center);
        foreach (var gate in _gates)
        {
            if ((_hasPrevious && gate.HasEntered(player, _previousCenter, center)) ||
                (!_hasPrevious && gate.ContainsEntryInterior(player, center)))
            {
                WasEntered = true;
                if (State == DungeonEncounterState.Waiting) BeginEncounter();
                break;
            }
        }
        _previousCenter = center;
        _hasPrevious = true;
    }

    private void PruneRemoved()
    {
        _removed.Clear();
        foreach (var member in _alive)
            if (member == null || member.IsDead || !member.gameObject.activeInHierarchy) _removed.Add(member);
        foreach (var member in _removed)
        {
            NotifyEnemyRemoved(member);
            if (State == DungeonEncounterState.Failed) return;
            if (member == null) continue;
            if (!member.gameObject.activeInHierarchy) member.Freeze();
            member.Release();
        }
    }

    private void Complete()
    {
        if (State == DungeonEncounterState.Cleared || State == DungeonEncounterState.Failed || _disposed) return;
        if (_isBoss && !BossDefeated) { Fail("The boss encounter ended without a confirmed boss death."); return; }
        State = DungeonEncounterState.Cleared;
        foreach (var gate in _gates) if (gate != null) gate.Open();
        Cleared?.Invoke(this);
    }

    private void Fail(string error)
    {
        State = DungeonEncounterState.Failed;
        Error = error;
        foreach (var member in _members) if (member != null) { member.Freeze(); member.Release(); }
        _alive.Clear();
        if (_enemyRoot != null) _enemyRoot.SetActive(false);
        foreach (var gate in _gates) if (gate != null) gate.Open(true);
        Debug.LogError($"[Dungeon Encounter] {name}: {error}", this);
    }

    private void OnDisable()
    {
        if (_director == null || _disposed) return;
        _disposed = true;
        foreach (var member in _members) if (member != null) { member.Freeze(); member.Release(); }
        _alive.Clear();
        foreach (var gate in _gates) if (gate != null) gate.Open(true);
    }

    private void OnDestroy()
    {
        if (_ownedNavigation == null) return;
        var surface = GetComponent<NavMeshSurface>();
        if (surface != null && surface.navMeshData == _ownedNavigation) { surface.RemoveData(); surface.navMeshData = null; }
        if (Application.isPlaying) Destroy(_ownedNavigation); else DestroyImmediate(_ownedNavigation);
    }
}
