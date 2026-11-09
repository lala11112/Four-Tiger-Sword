using System;
using System.Collections;
using StudioBaekho;
using UnityEngine;

/// <summary>Owns one generated stage. RunManager alone chooses and commits progression.</summary>
[DisallowMultipleComponent]
public class StageManager : MonoBehaviour
{
    [SerializeField] private BaekhoDungeonGenerator generator;
    [SerializeField] private DungeonEncounterDirector encounters;
    public BaekhoDungeonGenerator Generator => generator;
    public DungeonEncounterDirector Encounters => encounters;
    public BaekhoDungeonResult Result { get; private set; }
    public bool IsReady { get; private set; }
    public bool IsCleared { get; private set; }
    public string Error { get; private set; }
    public int MapCount => Result?.rooms.Count ?? 0;
    public StageExit Exit { get; private set; }
    public Vector3 PlayerSpawnPosition { get; private set; }
    public Quaternion PlayerSpawnRotation { get; private set; }
    public event Action OnStageClear;
    public event Action OnStageStart;
    public event Action ExitRequested;
    public event Action<string> Failed;
    private BaekhoDungeonSettings _runtimeSettings;
    private DungeonRoomEncounter _endEncounter, _bossEncounter;
    private PlayerController _player;
    private bool _isBossStage, _exitRequested;
    private int _generation;

    private void Awake()
    {
        if (generator == null) generator = FindFirstObjectByType<BaekhoDungeonGenerator>();
        if (encounters == null && generator != null) encounters = generator.GetComponent<DungeonEncounterDirector>();
        if (generator != null) generator.generateOnStart = false;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
    }

    public void Configure(BaekhoDungeonGenerator source, DungeonEncounterDirector director)
    { generator = source; encounters = director; generator.generateOnStart = false; }

    public IEnumerator Prepare(StageDefinition definition, bool bossStage, int stageSeed, int layoutSeed, PlayerController player)
    {
        int generation = ++_generation;
        Suspend();
        Error = null;
        IsCleared = false;
        _exitRequested = false;
        _player = player;
        _isBossStage = bossStage;
        _endEncounter = _bossEncounter = null;
        if (generator == null || encounters == null || player == null)
        { SetError("Stage requires a generator, encounter director and player."); yield break; }
        if (definition == null || !definition.Validate(bossStage, out _))
        { SetError(definition == null ? "Stage Definition is missing." : GetValidationError(definition, bossStage)); yield break; }
        if (_runtimeSettings != null) Destroy(_runtimeSettings);
        _runtimeSettings = definition.CreateRuntimeSettings(stageSeed, bossStage);
        generator.settings = _runtimeSettings;
        encounters.catalog = definition.enemyCatalog;
        encounters.ConfigureStage(bossStage ? definition.bossPrefab : null);
        yield return generator.Generate(layoutSeed);
        if (generation != _generation) yield break;
        var result = generator.LastResult;
        if (result == null || !result.success || result.root != generator.CurrentRoot)
        { SetError(result?.error ?? "Dungeon generation failed."); yield break; }
        Result = result;
        float deadline = Time.realtimeSinceStartup + 120f;
        while (!encounters.IsReady)
        {
            if (generation != _generation) yield break;
            if (result.root == null || generator.CurrentRoot != result.root || Time.realtimeSinceStartup > deadline)
            { SetError("Encounter preparation did not complete."); yield break; }
            yield return null;
        }
        if (!string.IsNullOrEmpty(encounters.Error)) { SetError(encounters.Error); yield break; }
        var start = result.startRoom != null ? result.startRoom.GetComponent<StageMapAnchors>() : null;
        var end = result.endRoom != null ? result.endRoom.GetComponent<StageMapAnchors>() : null;
        if (start == null || start.playerSpawn == null || end == null || end.exitPoint == null)
        { SetError("Generated start or exit anchor is missing."); yield break; }
        PlayerSpawnPosition = start.playerSpawn.position;
        PlayerSpawnRotation = start.playerSpawn.rotation;
        _endEncounter = result.endRoom.GetComponent<DungeonRoomEncounter>();
        _bossEncounter = result.bossRoom != null ? result.bossRoom.GetComponent<DungeonRoomEncounter>() : null;
        if (_endEncounter == null || (bossStage && _bossEncounter == null))
        { SetError("Required stage encounter is missing."); yield break; }
        var shop = GetComponent<StageShopService>();
        if (_runtimeSettings.shopRoom != null && shop == null)
        { SetError("This stage requires StageShopService."); yield break; }
        if (shop != null)
        {
            try { shop.PrepareStage(stageSeed, result); }
            catch (Exception exception) { SetError("Shop preparation failed: " + exception.Message); }
            if (Error != null) yield break;
        }
        GameObject exitObject = null;
        try
        {
            exitObject = new GameObject("StageExit");
            exitObject.transform.SetParent(result.endRoom.transform, false);
            exitObject.transform.SetPositionAndRotation(end.exitPoint.position, end.exitPoint.rotation);
            Exit = exitObject.AddComponent<StageExit>();
            Exit.Initialize(this, player, end.exitSize, generation);
        }
        catch (Exception exception)
        {
            if (exitObject != null) Destroy(exitObject);
            Exit = null;
            SetError("Stage exit preparation failed: " + exception.Message);
        }
        if (Error != null) yield break;
        IsReady = true;
    }

    internal void NotifyStarted() => OnStageStart?.Invoke();

    private static string GetValidationError(StageDefinition definition, bool bossStage)
    { definition.Validate(bossStage, out string error); return error; }

    public void Suspend()
    {
        IsReady = false;
        GetComponent<StageShopService>()?.SuspendStage();
        if (Exit != null) Exit.SetAvailable(false);
        encounters?.SuspendEncounters();
    }

    public void Cancel()
    {
        ++_generation;
        StopAllCoroutines();
        Suspend();
        if (generator != null && generator.IsGenerating) generator.ClearGenerated();
    }

    private void Update()
    {
        if (!IsReady) return;
        if (!string.IsNullOrEmpty(encounters.Error)) { SetError(encounters.Error); Failed?.Invoke(Error); return; }
        bool objectiveComplete = _endEncounter != null && _endEncounter.State == DungeonEncounterState.Cleared
            && (!_isBossStage || (_bossEncounter != null && _bossEncounter.BossDefeated && _bossEncounter.State == DungeonEncounterState.Cleared));
        Exit?.SetAvailable(objectiveComplete);
        // Empty end rooms are Cleared at initialization: arrival, not a missed event, completes them.
        if (objectiveComplete && _endEncounter.WasEntered) MarkClear();
    }

    public bool TryUseExit(PlayerController player, int generation)
    {
        if (!IsReady || _exitRequested || generation != _generation || player != _player || player.IsStageTransitioning || player.IsGameplayModalPaused
            || GetComponent<RunManager>()?.HasPendingItemChoice == true
            || Exit == null || !Exit.ContainsPlayer(player)
            || player.StatManager.CurrentHp <= 0 || _endEncounter == null || _endEncounter.State != DungeonEncounterState.Cleared
            || (_isBossStage && (_bossEncounter == null || !_bossEncounter.BossDefeated || _bossEncounter.State != DungeonEncounterState.Cleared))) return false;
        _exitRequested = true;
        MarkClear();
        ExitRequested?.Invoke();
        return true;
    }

    private void MarkClear()
    { if (!IsCleared) { IsCleared = true; OnStageClear?.Invoke(); } }

    private void SetError(string message)
    { Error = message; Suspend(); Debug.LogWarning("[Stage] " + message, this); }

    private void OnDestroy()
    { if (_runtimeSettings != null) Destroy(_runtimeSettings); }
}
