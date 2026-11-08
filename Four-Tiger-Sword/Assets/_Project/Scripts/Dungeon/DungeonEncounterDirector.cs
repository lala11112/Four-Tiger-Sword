using System;
using System.Collections;
using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(BaekhoDungeonGenerator))]
public sealed class DungeonEncounterDirector : MonoBehaviour
{
    public DungeonEnemyCatalog catalog;
    public GameObject doorPrefab;
    [Min(0.1f)] public float spawnRadius = 3f;
    [Min(1), Tooltip("Desired cost per wave. Individual enemy prices can exceed this target.")]
    public int targetCostPerWave = 20;
    [Min(1), Tooltip("Maximum enemies allocated to one wave.")]
    public int maxEnemiesPerWave = 6;
    [SerializeField] private CharacterController player;
    public CharacterController Player => player;
    public bool IsReady { get; private set; }
    public string Error
    {
        get
        {
            var errors = new List<string>();
            if (!string.IsNullOrEmpty(_preparationError)) errors.Add(_preparationError);
            foreach (var room in _rooms)
                if (room != null && room.State == DungeonEncounterState.Failed)
                    errors.Add(room.name + ": " + room.Error);
            return errors.Count == 0 ? null : string.Join("\n", errors);
        }
    }
    public IReadOnlyList<DungeonRoomEncounter> Rooms => _rooms;
    public event Action<DungeonEncounterDirector> Ready;
    private readonly List<DungeonRoomEncounter> _rooms = new();
    private BaekhoDungeonGenerator _generator;
    private GameObject _root;
    private Coroutine _preparation;
    private float _nextPlayerSearch;
    private GameObject _bossPrefab;
    private string _preparationError;
    private bool _suspended;
    private PlayerController _boundPlayer;

    /// <summary>Only live encounters belonging to this director's current dungeon count as combat.</summary>
    public bool IsPlayerInCombat(PlayerController target)
    {
        if (target == null || target != _boundPlayer || !target.isActiveAndEnabled
            || player == null || !player.enabled || player.gameObject != target.gameObject
            || !isActiveAndEnabled || !IsReady || _suspended || _generator == null
            || _generator.IsGenerating || _root == null || !_root.activeInHierarchy
            || _root != _generator.CurrentRoot || _generator.LastResult?.success != true
            || _generator.LastResult.root != _root || !string.IsNullOrEmpty(_preparationError)) return false;
        bool fighting = false;
        foreach (var room in _rooms)
        {
            if (room == null) continue;
            if (room.State == DungeonEncounterState.Failed) return false;
            if (room.IsCombatActive && room.transform.IsChildOf(_root.transform)) fighting = true;
        }
        return fighting;
    }

    /// <summary>Set before generating a stage; null selects ordinary encounters only.</summary>
    public void ConfigureStage(GameObject bossPrefab) => _bossPrefab = bossPrefab;

    /// <summary>A warp must not be interpreted as a swept crossing of unrelated room entrances.</summary>
    public void ResetPlayerMotionHistory()
    {
        foreach (var room in _rooms) if (room != null) room.ResetPlayerMotionHistory();
    }

    public void SuspendEncounters()
    {
        _suspended = true;
        RefreshItemCombatState();
        foreach (var room in _rooms) if (room != null) room.SuspendEncounter();
    }

    private void OnEnable()
    {
        _generator = GetComponent<BaekhoDungeonGenerator>();
        _generator.Completed += OnGenerated;
        FindPlayer();
        var result = _generator.LastResult;
        if (result != null && result.success && result.root != null && result.root == _generator.CurrentRoot) OnGenerated(result);
    }

    private void OnGenerated(BaekhoDungeonResult result)
    {
        if (!result.success || result.root == null || (result.root == _root && IsReady)) return;
        if (_preparation != null) StopCoroutine(_preparation);
        _root = result.root;
        IsReady = false;
        _preparationError = null;
        _suspended = false;
        _rooms.Clear();
        _preparation = StartCoroutine(Prepare(result));
    }

    private IEnumerator Prepare(BaekhoDungeonResult result)
    {
        Dictionary<int, Dictionary<int, float>> used = null;
        try
        {
            if (result.plan == null) throw new InvalidOperationException("The generated dungeon has no layout plan.");
            used = new Dictionary<int, Dictionary<int, float>>();
            foreach (var module in result.rooms)
            {
                if (module == null || module.GetComponent<BaekhoGeneratedRoom>() == null)
                    throw new InvalidOperationException("A generated room has no runtime identity.");
                used.Add(module.GetComponent<BaekhoGeneratedRoom>().instanceId, new Dictionary<int, float>());
            }
            foreach (var link in result.plan.links)
            {
                used[link.fromMapId][link.fromSocketIndex] = link.width;
                used[link.toMapId][link.toSocketIndex] = link.width;
            }
            if (_bossPrefab != null && result.bossRoom == null)
                throw new InvalidOperationException("The configured boss stage generated no boss room.");
        }
        catch (Exception exception)
        {
            _preparationError = exception.Message;
            used = null;
        }
        if (used != null) foreach (var module in result.rooms)
        {
            if (_root == null || _root != result.root || !result.root.activeInHierarchy) yield break;
            try
            {
                if (module == null) throw new InvalidOperationException("A room was removed during encounter preparation.");
                int id = module.GetComponent<BaekhoGeneratedRoom>().instanceId;
                var encounter = module.GetComponent<DungeonRoomEncounter>() ?? module.gameObject.AddComponent<DungeonRoomEncounter>();
                _rooms.Add(encounter);
                int seed = unchecked(result.seed * 397 ^ id * 7919);
                int bossExitSocket = -1;
                if (id == result.plan.bossMapId) foreach (var link in result.plan.links)
                {
                    if (link.fromMapId == id && link.toMapId == result.plan.endMapId) bossExitSocket = link.fromSocketIndex;
                    if (link.toMapId == id && link.fromMapId == result.plan.endMapId) bossExitSocket = link.toSocketIndex;
                }
                encounter.Initialize(this, module, used[id], seed, _bossPrefab, bossExitSocket);
                if (_suspended) encounter.SuspendEncounter();
            }
            catch (Exception exception)
            {
                string error = (module != null ? module.name : "Missing room") + ": " + exception.Message;
                _preparationError = string.IsNullOrEmpty(_preparationError) ? error : _preparationError + "\n" + error;
            }
            yield return null;
        }
        IsReady = true;
        _preparation = null;
        Ready?.Invoke(this);
    }

    private void Update()
    {
        RefreshItemCombatState();
        if ((player == null || _boundPlayer == null) && Time.unscaledTime >= _nextPlayerSearch) FindPlayer();
        if (_root != null && _root == _generator.CurrentRoot) return;
        if (_preparation != null) StopCoroutine(_preparation);
        _preparation = null;
        _root = null;
        IsReady = false;
        _preparationError = null;
        _rooms.Clear();
    }

    private void FindPlayer()
    {
        if (player == null)
        {
            var controller = FindFirstObjectByType<PlayerController>();
            if (controller != null) player = controller.GetComponent<CharacterController>();
        }
        var next = player != null ? player.GetComponent<PlayerController>() : null;
        if (_boundPlayer != next)
        {
            if (_boundPlayer != null) _boundPlayer.UnbindEncounterDirector(this);
            _boundPlayer = next;
        }
        if (_boundPlayer != null) _boundPlayer.BindEncounterDirector(this);
        _nextPlayerSearch = Time.unscaledTime + 1f;
    }

    private void OnDisable()
    {
        _boundPlayer?.ItemRuntime?.SetCombatState(false);
        if (_boundPlayer != null) _boundPlayer.UnbindEncounterDirector(this);
        _boundPlayer = null;
        if (_generator != null) _generator.Completed -= OnGenerated;
        if (_preparation != null) StopCoroutine(_preparation);
        _preparation = null;
    }

    internal void RefreshItemCombatState()
    {
        if (_boundPlayer != null)
            _boundPlayer.ItemRuntime?.SetCombatState(IsPlayerInCombat(_boundPlayer));
    }
}
