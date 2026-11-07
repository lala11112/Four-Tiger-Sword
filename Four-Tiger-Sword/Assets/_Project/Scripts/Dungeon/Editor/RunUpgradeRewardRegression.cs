using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// <summary>Temporary actors and an injected memory store never read or change the player's real progression.</summary>
public static class RunUpgradeRewardRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [MenuItem("Tools/Studio Baekho/Upgrades/Run Reward Regression")]
    private static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run upgrade rewards regression in Edit Mode.");
        _checks = 0;
        bool cursorVisible = Cursor.visible;
        var cursorLock = Cursor.lockState;
        try
        {
            RoomRetryMaxima();
            StageAndBossPayOnce();
            DeathRewardsAndCap();
            FailedClearResumesPendingExit();
            FailedDeathKeepsPendingReward();
            ObserverReentryCannotEvadeSettlement();
            GenuineCombatClearOnly();
            return $"Upgrade run rewards: {_checks} checks passed (stage/boss receipts, death cap, generation maxima, "
                + "failed-save retry, pending exit, navigation guards and confirmed combat deaths).";
        }
        finally { Cursor.visible = cursorVisible; Cursor.lockState = cursorLock; }
    }

    private static void RoomRetryMaxima()
    {
        using var f = new Fixture();
        f.BeginRooms(0, 0);
        Check(f.ClearRoom(1) && !f.ClearRoom(1), "A room paid twice in one generation.");
        f.ClearRoom(2);
        Check(f.Run.ClearedCombatRoomCount == 2, "Distinct rooms were not counted.");
        f.BeginRooms(0, 0);
        f.ClearRoom(8);
        Check(f.Run.ClearedCombatRoomCount == 2, "A smaller retry reduced or added to its stage maximum.");
        f.ClearRoom(9); f.ClearRoom(10);
        Check(f.Run.ClearedCombatRoomCount == 3, "A better retry did not replace its stage maximum.");
        f.BeginRooms(0, 1); f.ClearRoom(1); f.ClearRoom(2);
        Check(f.Run.ClearedCombatRoomCount == 5, "Room IDs from another stage collided.");
        f.BeginRooms(0, 0); f.ClearRoom(77);
        Check(f.Run.ClearedCombatRoomCount == 5, "Returning to a stage added repeated progress.");
    }

    private static void StageAndBossPayOnce()
    {
        using var f = new Fixture();
        Call(f.Run, "OnUpgradeStageClear"); Call(f.Run, "OnUpgradeStageClear");
        Check(f.Service.Balance == 10 && f.Run.StageUpgradeRewardTotal == 10 && f.Store.Writes == 1,
            "Normal-stage clear did not pay exactly ten once.");
        f.SelectStage(1);
        Call(f.Run, "OnUpgradeStageClear"); Call(f.Run, "OnUpgradeStageClear");
        Check(f.Service.Balance == 40 && f.Run.StageUpgradeRewardTotal == 40 && f.Store.Writes == 2,
            "Boss-stage clear did not pay exactly thirty once.");
        f.SelectStage(0); Call(f.Run, "OnUpgradeStageClear");
        Check(f.Service.Balance == 40, "Regenerating a cleared stage paid again.");
        Call(f.Run, "EndRun", true);
        Check(f.Run.State == RunState.Won && f.Run.DeathUpgradeRewardTotal == 0 && f.Service.Balance == 40,
            "Victory paid a death reward.");
    }

    private static void DeathRewardsAndCap()
    {
        using (var f = new Fixture())
        {
            f.BeginRooms(0, 0); f.ClearRoom(1); f.ClearRoom(2);
            f.BeginRooms(0, 1); f.ClearRoom(1);
            Call(f.Run, "EndRun", false); Call(f.Run, "EndRun", false);
            Check(f.Run.State == RunState.Lost && f.Service.Balance == 6 && f.Run.DeathUpgradeRewardTotal == 6,
                "Death did not pay two per completed combat room exactly once across the run.");
            Check(f.Store.Writes == 1, "Repeated terminal calls saved the same death twice.");
        }
        using (var f = new Fixture())
        {
            f.BeginRooms(0, 0);
            for (int i = 0; i < 7; i++) f.ClearRoom(i);
            Call(f.Run, "EndRun", false);
            Check(f.Service.Balance == 10 && f.Run.DeathUpgradeRewardTotal == 10, "Death reward exceeded its ten cap.");
        }
        using (var f = new Fixture())
        {
            Call(f.Run, "EndRun", false);
            Check(f.Service.Balance == 0 && f.Run.DeathUpgradeRewardTotal == 0, "Immediate death granted currency.");
        }
    }

    private static void FailedClearResumesPendingExit()
    {
        using var f = new Fixture();
        f.SelectStage(1);
        f.Store.FailWrites = true;
        Call(f.Run, "OnUpgradeStageClear");
        Check(f.Run.IsUpgradeSaveBlocked && f.Run.HasPendingUpgradeRewards && f.Service.Balance == 0
            && f.Run.StageUpgradeRewardTotal == 0, "Failed save lost its pending reward or published uncommitted currency.");
        f.Run.ReturnToHub(); f.Run.ReturnToTitle();
        Check(f.Run.IsUpgradeSaveBlocked && f.Run.HasPendingUpgradeRewards, "Navigation evaded pending currency.");
        // MarkClear and ExitRequested are synchronous: remember the already accepted exit while saving is blocked.
        Call(f.Run, "Advance");
        f.Run.TryRetryPendingRewards();
        Check(f.Run.IsUpgradeSaveBlocked && f.Service.Balance == 0, "A repeated failed write resumed the run.");
        f.Store.FailWrites = false;
        f.Run.TryRetryPendingRewards();
        Check(!f.Run.IsUpgradeSaveBlocked && !f.Run.HasPendingUpgradeRewards && f.Run.State == RunState.Won
            && f.Service.Balance == 30 && f.Run.StageUpgradeRewardTotal == 30,
            "Successful retry did not settle once and complete the previously requested final exit.");
        f.Run.TryRetryPendingRewards();
        Check(f.Service.Balance == 30 && f.Store.Writes == 1, "Retrying a settled reward paid twice.");
    }

    private static void FailedDeathKeepsPendingReward()
    {
        using var f = new Fixture();
        f.BeginRooms(0, 0); f.ClearRoom(1);
        f.Store.FailWrites = true;
        Call(f.Run, "EndRun", false);
        f.Run.RestartRun();
        Check(f.Run.State == RunState.Lost && f.Run.IsUpgradeSaveBlocked && f.Run.HasPendingUpgradeRewards
            && f.Service.Balance == 0, "Death or direct restart discarded a failed reward.");
        f.Store.FailWrites = false;
        f.Run.TryRetryPendingRewards();
        Check(f.Run.State == RunState.Lost && !f.Run.IsUpgradeSaveBlocked && !f.Run.HasPendingUpgradeRewards
            && f.Service.Balance == 2 && f.Run.DeathUpgradeRewardTotal == 2, "Death retry did not settle exactly once.");
        Call(f.Run, "EndRun", false);
        Check(f.Service.Balance == 2 && f.Store.Writes == 1, "Settled death was queued again.");
    }

    private static void GenuineCombatClearOnly()
    {
        using var f = new Fixture();
        var room = f.Room(out var first);
        var second = f.Member(room);
        first.Enemy.TakeDamage(100f, poiseDamage: 0f);
        Object.DestroyImmediate(first.gameObject);
        Check(!room.WasCombatClearedByDefeat, "A partially defeated combat room earned credit.");
        second.Enemy.TakeDamage(100f, poiseDamage: 0f);
        Check(room.WasCombatClearedByDefeat, "Genuine death history was lost when an earlier corpse was destroyed.");
        room.NotifyEnemyRemoved(second);
        Check(room.WasCombatClearedByDefeat, "A repeated removal corrupted a confirmed clear.");

        var removed = f.Room(out var removedMember);
        removed.NotifyEnemyRemoved(removedMember);
        Check(removed.State == DungeonEncounterState.Cleared && !removed.WasCombatClearedByDefeat,
            "Despawn cleanup earned combat credit.");
        var zeroHp = f.Room(out var zeroMember);
        zeroMember.Enemy.EnemyStat.CurrentHp = 0f;
        zeroHp.NotifyEnemyRemoved(zeroMember);
        Check(!zeroHp.WasCombatClearedByDefeat, "Zero HP without Enemy.OnDied earned combat credit.");
        var suspended = f.Room(out var suspendedMember);
        suspended.SuspendEncounter();
        suspended.NotifyEnemyRemoved(suspendedMember);
        Check(suspended.State == DungeonEncounterState.Fighting && !suspended.WasCombatClearedByDefeat,
            "Suspension or shutdown earned combat credit.");
        var empty = f.Own(new GameObject("Empty room")).AddComponent<DungeonRoomEncounter>();
        Property(empty, "State", DungeonEncounterState.Cleared);
        Property(empty, "WasEntered", true);
        Check(!empty.WasCombatClearedByDefeat, "An entered empty room earned combat credit.");
    }

    private static void ObserverReentryCannotEvadeSettlement()
    {
        using var f = new Fixture();
        f.BeginRooms(0, 0); f.ClearRoom(1);
        int balanceCallbacks = 0, terminalCallbacks = 0;
        bool balanceCallbackBlocked = true;
        f.Service.Changed += () =>
        {
            balanceCallbacks++;
            balanceCallbackBlocked &= !(bool)Call(f.Run, "CanLeaveUpgradeRewards");
        };
        f.Run.StateChanged += state =>
        {
            if (state != RunState.Lost) return;
            terminalCallbacks++;
            Check(f.Service.Balance == 2 && f.Run.DeathUpgradeRewardTotal == 2 && !f.Run.HasPendingUpgradeRewards,
                "Terminal observers ran before the death reward was settled.");
            Check(!(bool)Call(f.Run, "CanLeaveUpgradeRewards"),
                "Terminal observers could interrupt the enclosing result settlement.");
            Call(f.Run, "EndRun", false);
        };
        Call(f.Run, "EndRun", false);
        Check(balanceCallbackBlocked && balanceCallbacks == 1 && terminalCallbacks == 1 && f.Service.Balance == 2,
            "Reentrant terminal callbacks caused duplicate settlement.");
        Check((bool)Call(f.Run, "CanLeaveUpgradeRewards"), "Completed settlement kept ordinary navigation blocked.");
    }

    private static object Field(object owner, string name) => owner.GetType().GetField(name, Hidden).GetValue(owner);
    private static void Field(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
    private static void Property(object owner, string name, object value) => owner.GetType().GetProperty(name).SetValue(owner, value);
    private static object Call(object owner, string name, params object[] args)
        => owner.GetType().GetMethod(name, Hidden | BindingFlags.Public).Invoke(owner, args);
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException("Upgrade run rewards: " + message); }

    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        public bool FailWrites;
        public int Writes;
        private PlayerUpgradeProfile _saved = PlayerUpgradeProfile.New();
        public bool TryLoad(out PlayerUpgradeProfile profile, out string error)
        { profile = _saved.Copy(); error = null; return true; }
        public bool TrySave(PlayerUpgradeProfile profile, out string error)
        {
            error = FailWrites ? "Expected regression save failure." : null;
            if (FailWrites) return false;
            _saved = profile.Copy(); Writes++; return true;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly List<Object> _owned = new();
        public readonly RunManager Run;
        public readonly StageManager Stage;
        public readonly MemoryStore Store;
        public readonly PlayerUpgradeService Service;

        public Fixture()
        {
            var root = Own(new GameObject("Upgrade reward regression")); root.SetActive(false);
            Stage = root.AddComponent<StageManager>();
            Run = root.AddComponent<RunManager>(); Run.stageManager = Stage;
            var data = Own(ScriptableObject.CreateInstance<PlayerStatData>());
            data.baseHP = 100f; data.baseATK = data.baseDEF = 10f;
            var settings = Own(ScriptableObject.CreateInstance<PlayerUpgradeSettingsSO>());
            settings.basePlayerStats = data;
            Store = new MemoryStore(); Service = new PlayerUpgradeService(settings, Store);
            Field(Run, "_upgradeService", Service);
            Run.definition = Own(ScriptableObject.CreateInstance<RunDefinitionSO>());
            Run.definition.chapters = new[] { new ChapterDefinition
            { stages = new[] { new StageDefinition(), new StageDefinition() } } };
            Property(Run, "State", RunState.Playing);
            Property(Run, "CurrentChapterIndex", 0);
            SelectStage(0);
        }

        public void SelectStage(int index)
        {
            Property(Run, "CurrentStageIndex", index);
            Property(Stage, "IsReady", true); Property(Stage, "IsCleared", true);
        }
        public void BeginRooms(int chapter, int stage) => Call(Field(Run, "_upgradeRoomLedger"), "BeginGeneration", chapter, stage);
        public bool ClearRoom(int id) => (bool)Call(Field(Run, "_upgradeRoomLedger"), "RecordClearedRoom", id);
        public T Own<T>(T value) where T : Object { value.hideFlags = HideFlags.HideAndDontSave; _owned.Add(value); return value; }

        public DungeonRoomEncounter Room(out DungeonEncounterMember member)
        {
            var room = Own(new GameObject("Confirmed room fixture")).AddComponent<DungeonRoomEncounter>();
            Property(room, "State", DungeonEncounterState.Fighting); Property(room, "WasEntered", true);
            member = Member(room);
            return room;
        }

        public DungeonEncounterMember Member(DungeonRoomEncounter room)
        {
            var actor = new GameObject("Encounter member") { hideFlags = HideFlags.HideAndDontSave };
            actor.SetActive(false); actor.transform.SetParent(room.transform, false);
            actor.AddComponent<NavMeshAgent>().enabled = false;
            var enemy = actor.AddComponent<Enemy>();
            enemy.EnemyStat = actor.GetComponent<EnemyStat>(); enemy.EnemyStat.CurrentHp = 10f;
            actor.GetComponent<PoiseHandler>().ResetPoise();
            actor.AddComponent<CapsuleCollider>();
            var member = actor.AddComponent<DungeonEncounterMember>(); member.Initialize(room);
            ((List<DungeonEncounterMember>)Field(room, "_members")).Add(member);
            ((HashSet<DungeonEncounterMember>)Field(room, "_alive")).Add(member);
            actor.SetActive(true); enemy.enabled = true;
            return member;
        }

        public void Dispose()
        {
            Call(Run, "CleanupUpgradeRewards");
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
        }
    }
}
