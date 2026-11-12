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
        && State == DungeonEncounterState.Fighting && (_alive.Count > 0 || HasMoreWaves);
    public int WaveCount => _waves.Count;
    public int CurrentWaveNumber { get; private set; }
    public string WaveSummary { get; private set; } = string.Empty;
    public int SpentBudget { get; private set; }
    public int RemainingBudget { get; private set; }
    public string Error { get; private set; }
    public bool WasEntered { get; private set; }
    public bool BossDefeated { get; private set; }
    /// <summary>All waves died during entered combat; despawning never earns rewards.</summary>
    public bool WasCombatClearedByDefeat => State == DungeonEncounterState.Cleared && WasEntered
        && _members.Count > 0 && _confirmedDefeats == _members.Count;
    public IReadOnlyList<DungeonEncounterMember> Members => _members;
    public IReadOnlyList<DungeonEncounterGate> Gates => _gates;
    public EnemyCombatGroup CombatGroup { get; private set; }
    public event Action<DungeonRoomEncounter> Cleared;
    public event Action<DungeonEncounterMember> MemberSpawned;
    private const float ReinforcementDelay = 1.5f;
    private const float SpawnRetryDelay = 0.25f;
    private const float SpawnRetryTimeout = 10f;
    private readonly List<DungeonEncounterMember> _members = new();
    private readonly HashSet<DungeonEncounterMember> _alive = new();
    private readonly List<DungeonEncounterGate> _gates = new();
    private readonly List<DungeonEncounterMember> _removed = new();
    private readonly List<List<GameObject>> _waves = new();
    private DungeonEncounterDirector _director;
    private BaekhoMapModule _module;
    private NavMeshData _ownedNavigation;
    private GameObject _enemyRoot;
    private Vector3 _previousCenter;
    private bool _hasPrevious;
    private bool _disposed;
    private bool _suspended;
    private bool _isBoss;
    private DungeonEncounterMember _bossMember;
    private int _confirmedDefeats;
    private int _seed;
    private float _waveDelayRemaining = -1f;
    private float _reinforcementElapsed;
    private bool HasMoreWaves => CurrentWaveNumber < _waves.Count;

    public void Initialize(DungeonEncounterDirector director, BaekhoMapModule module,
        Dictionary<int, float> usedSockets, int seed, GameObject bossPrefab = null, int bossExitSocketIndex = -1)
    {
        if (_director != null) return;
        _director = director;
        _module = module;
        _seed = seed;
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
            if (_isBoss)
            {
                var anchors = module.GetComponent<StageMapAnchors>();
                if (bossPrefab == null) throw new InvalidOperationException("The boss stage has no boss prefab.");
                if (anchors == null || anchors.bossSpawn == null)
                    throw new InvalidOperationException("The boss room needs StageMapAnchors.bossSpawn.");
                if (!usedSockets.ContainsKey(bossExitSocketIndex))
                    throw new InvalidOperationException("The boss room has no connection to its end room.");
                _waves.Add(new List<GameObject> { bossPrefab });
                WaveSummary = $"1차 [{bossPrefab.name} ×1]";
            }
            bool hasMarkers = false;
            foreach (var marker in module.GetComponentsInChildren<BaekhoGameplayMarker>(true))
                if (marker.kind == "enemy") { hasMarkers = true; break; }
            if (module.encounterBudget < 0) throw new InvalidOperationException("Encounter budget cannot be negative.");
            if (!_isBoss && hasMarkers && module.encounterBudget > 0)
            {
                if (director.catalog == null) throw new InvalidOperationException("Dungeon enemy catalog is missing.");
                int[] costs = director.catalog.GetValidatedCosts();
                var plan = BaekhoEncounterPlanner.Plan(module.encounterBudget, costs, seed);
                SpentBudget = plan.Spent;
                RemainingBudget = plan.Remaining;
                if (plan.BalanceRelaxed)
                    Debug.LogWarning($"[Dungeon Encounter] {name}: budget {module.encounterBudget} cannot support a balanced mix; composition constraints relaxed.", this);
                var waves = BaekhoEncounterPlanner.SplitIntoWaves(plan.Counts, costs,
                    director.targetCostPerWave, director.maxEnemiesPerWave, seed);
                var summaries = new List<string>();
                for (int waveIndex = 0; waveIndex < waves.Count; waveIndex++)
                {
                    var wave = waves[waveIndex];
                    var prefabs = new List<GameObject>();
                    var roster = new List<string>();
                    for (int i = 0; i < wave.Counts.Length; i++)
                    {
                        if (wave.Counts[i] == 0) continue;
                        var prefab = director.catalog.enemies[i].prefab;
                        roster.Add(prefab.name + " ×" + wave.Counts[i]);
                        for (int n = 0; n < wave.Counts[i]; n++) prefabs.Add(prefab);
                    }
                    _waves.Add(prefabs);
                    summaries.Add($"{waveIndex + 1}차 [{string.Join(", ", roster)}] · 코스트 {wave.Cost}");
                }
                WaveSummary = string.Join("\n", summaries);
            }
            if (_waves.Count > 0 && !DungeonEncounterPlacement.TryPrepareNavigation(module, out _ownedNavigation, out var navError))
                throw new InvalidOperationException(navError);
            // Navigation is prepared before adding dynamic gate geometry.
            CreateGates(module, usedSockets, director.doorPrefab, _isBoss ? bossExitSocketIndex : -1);
            if (_waves.Count == 0) { State = DungeonEncounterState.Cleared; return; }
            EnsureCombatGroup();
            _enemyRoot = new GameObject("EncounterEnemies");
            _enemyRoot.transform.SetParent(transform, false);
            if (!TryPlanNextWave(out var placements, out var placementError))
                throw new InvalidOperationException(placementError);
            SpawnWave(placements, !_isBoss);
            if (_disposed || State == DungeonEncounterState.Failed) return;
            State = DungeonEncounterState.Waiting;
            Debug.Log($"[Dungeon Encounter] {name}: {WaveCount} waves, budget {SpentBudget}/{module.encounterBudget} (remaining {RemainingBudget}).\n{WaveSummary}", this);
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

    private void EnsureCombatGroup()
    {
        if (_isBoss || CombatGroup != null) return;
        CombatGroup = GetComponent<EnemyCombatGroup>() ?? gameObject.AddComponent<EnemyCombatGroup>();
        CombatGroup.Initialize(_director.normalEnemyAI);
    }

    private bool TryPlanNextWave(out List<DungeonEnemyPlacement> placements, out string error)
    {
        if (_isBoss)
        {
            placements = null;
            var anchors = _module.GetComponent<StageMapAnchors>();
            if (!DungeonEncounterPlacement.TryPlanBoss(_module, _waves[CurrentWaveNumber][0],
                anchors.bossSpawn, out var placement, out error)) return false;
            placements = new List<DungeonEnemyPlacement> { placement };
            return true;
        }
        int waveSeed = unchecked(_seed + CurrentWaveNumber * 7919);
        return DungeonEncounterPlacement.TryPlan(_module, _waves[CurrentWaveNumber], waveSeed,
            _director.spawnRadius, out placements, out error);
    }

    private void SpawnWave(List<DungeonEnemyPlacement> placements, bool patrolOnly)
    {
        var waveRoot = new GameObject("Wave_" + (CurrentWaveNumber + 1));
        waveRoot.SetActive(false);
        waveRoot.transform.SetParent(_enemyRoot.transform, false);
        var newMembers = new List<DungeonEncounterMember>();
        int rewardRoomId = _module.GetComponent<BaekhoGeneratedRoom>().instanceId;
        foreach (var placement in placements)
        {
            var clone = Instantiate(placement.prefab, placement.position, placement.rotation, waveRoot.transform);
            var member = clone.AddComponent<DungeonEncounterMember>();
            member.SetRewardIdentity(rewardRoomId, CurrentWaveNumber, newMembers.Count);
            _members.Add(member);
            member.Initialize(this);
            _alive.Add(member);
            newMembers.Add(member);
            if (_isBoss) _bossMember = member;
        }
        // Run EnemyStat.Awake with AI and hit colliders still disabled.
        waveRoot.SetActive(true);
        foreach (var member in newMembers)
        {
            var stats = member.GetComponent<EnemyStat>();
            if (member.IsDead || stats == null || !(stats.CurrentHp > 0f) || float.IsInfinity(stats.CurrentHp))
                throw new InvalidOperationException($"{member.name} has no positive starting HP.");
        }
        CurrentWaveNumber++;
        _waveDelayRemaining = -1f;
        _reinforcementElapsed = 0f;
        foreach (var member in newMembers)
        {
            if (_disposed || _suspended || State == DungeonEncounterState.Failed) return;
            MemberSpawned?.Invoke(member);
        }
        foreach (var member in newMembers)
        {
            if (_disposed || _suspended || State == DungeonEncounterState.Failed) return;
            if (_isBoss) continue;
            if (patrolOnly)
            {
                if (!member.ActivatePatrol(out var patrolError)) throw new InvalidOperationException(patrolError);
                continue;
            }
            member.Enemy.SetEncounterPatrolOnly(false);
            if (!member.Activate(out var error)) throw new InvalidOperationException(error);
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
        if (_alive.Count == 0 && !HasMoreWaves) { Complete(); return false; }
        foreach (var member in _alive)
            if (!member.CanActivate(out var error)) { Fail(error); return false; }
        EnsureCombatGroup();
        foreach (var gate in _gates) gate.Lock();
        WasEntered = true;
        State = DungeonEncounterState.Fighting;
        _director.RefreshItemCombatState();
        foreach (var member in _alive)
        {
            member.Enemy.SetEncounterPatrolOnly(false);
            if (!member.Activate(out var error)) { Fail(error); return false; }
        }
        HandleWaveEnded();
        return true;
    }

    public void NotifyEnemyRemoved(DungeonEncounterMember member)
    {
        if (_disposed || _suspended || !_alive.Remove(member)) return;
        if (member != null && member.HasDied && WasEntered && State == DungeonEncounterState.Fighting)
            _confirmedDefeats++;
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
        HandleWaveEnded();
    }

    private void HandleWaveEnded()
    {
        if (_alive.Count > 0 || (State != DungeonEncounterState.Waiting && State != DungeonEncounterState.Fighting)) return;
        if (!HasMoreWaves) { Complete(); return; }
        if (State == DungeonEncounterState.Fighting && _waveDelayRemaining < 0f)
        {
            _waveDelayRemaining = ReinforcementDelay;
            _reinforcementElapsed = 0f;
        }
    }

    private void UpdateReinforcements()
    {
        if (State != DungeonEncounterState.Fighting || _alive.Count > 0 || !HasMoreWaves) return;
        if (Time.deltaTime <= 0f) return;
        _waveDelayRemaining -= Time.deltaTime;
        _reinforcementElapsed += Time.deltaTime;
        if (_waveDelayRemaining > 0f) return;
        // A moving player or a corpse can temporarily occupy every authored spawn marker.
        if (!TryPlanNextWave(out var placements, out var error))
        {
            if (_reinforcementElapsed >= ReinforcementDelay + SpawnRetryTimeout) { Fail(error); return; }
            _waveDelayRemaining = SpawnRetryDelay;
            return;
        }
        try { SpawnWave(placements, false); }
        catch (Exception exception) { Fail(exception.Message); }
    }

    /// <summary>Stop combat during transition/end without treating frozen members as defeated.</summary>
    public void SuspendEncounter()
    {
        if (_disposed || _suspended) return;
        _suspended = true;
        _waveDelayRemaining = -1f;
        _hasPrevious = false;
        foreach (var member in _members) if (member != null) member.Freeze();
    }

    /// <summary>Forget only entrance movement; retain visitation, combat and cleared state.</summary>
    public void ResetPlayerMotionHistory() => _hasPrevious = false;

    private void Update()
    {
        if (_disposed || _suspended || State == DungeonEncounterState.Preparing || State == DungeonEncounterState.Failed) return;
        if (State == DungeonEncounterState.Waiting || State == DungeonEncounterState.Fighting)
        {
            PruneRemoved();
            if (State == DungeonEncounterState.Failed) return;
            HandleWaveEnded();
            UpdateReinforcements();
            if (State == DungeonEncounterState.Failed) return;
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
        _director?.RefreshItemCombatState();
        _waveDelayRemaining = -1f;
        foreach (var gate in _gates) if (gate != null) gate.Open();
        Cleared?.Invoke(this);
    }

    private void Fail(string error)
    {
        State = DungeonEncounterState.Failed;
        _director?.RefreshItemCombatState();
        _waveDelayRemaining = -1f;
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
        _director?.RefreshItemCombatState();
        _waveDelayRemaining = -1f;
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
