using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>One persistent progression account shared by the Hub and every newly created run.</summary>
public sealed class PlayerUpgradeService
{
    private static PlayerUpgradeService _instance;
    public static PlayerUpgradeService Instance => _instance ??= new PlayerUpgradeService(
        Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings"),
        new PlayerUpgradeFileStore(Path.Combine(Application.persistentDataPath, "player-upgrades.json")));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => _instance = null;

#if UNITY_EDITOR
    /// <summary>Scope the shared account to an isolated store during Play Mode integration verification.</summary>
    public static IDisposable OverrideForTesting(PlayerUpgradeService service)
    {
        if (service == null) throw new ArgumentNullException(nameof(service));
        var scope = new TestOverride(_instance, service);
        _instance = service;
        return scope;
    }

    private sealed class TestOverride : IDisposable
    {
        private readonly PlayerUpgradeService _previous, _replacement;
        private bool _disposed;
        public TestOverride(PlayerUpgradeService previous, PlayerUpgradeService replacement)
        { _previous = previous; _replacement = replacement; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(_instance, _replacement)) _instance = _previous;
        }
    }
#endif

    public PlayerUpgradeSettingsSO Settings { get; }
    public int Balance => _profile?.currency ?? 0;
    public bool IsReady { get; private set; }
    public string LastError { get; private set; }
    public event Action Changed;

    private readonly IPlayerUpgradeStore _store;
    private readonly HashSet<string> _rewardIds = new(StringComparer.Ordinal);
    private PlayerUpgradeProfile _profile;
    private bool _transactionInProgress;

    /// <summary>Explicit dependencies let regressions use memory or temporary files, never the real save.</summary>
    public PlayerUpgradeService(PlayerUpgradeSettingsSO settings, IPlayerUpgradeStore store)
    {
        Settings = settings;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        TryReload(out _);
    }

    public int GetLevel(StatType stat) => _profile == null ? 0 : stat switch
    {
        StatType.ST_HP => _profile.healthLevel,
        StatType.ST_ATK => _profile.attackLevel,
        StatType.ST_DEF => _profile.defenseLevel,
        _ => 0
    };

    public float GetBonusRatio(StatType stat) => Settings == null ? 0f : GetLevel(stat) * (stat switch
    {
        StatType.ST_HP => Settings.healthPerLevel,
        StatType.ST_ATK => Settings.attackPerLevel,
        StatType.ST_DEF => Settings.defensePerLevel,
        _ => 0f
    });

    /// <summary>Zero denotes an unavailable or completed upgrade.</summary>
    public int GetNextCost(StatType stat) => !IsReady || !Supports(stat) || GetLevel(stat) >= Settings.maxLevel
        ? 0 : Settings.initialCost + GetLevel(stat) * Settings.costPerLevel;

    public float GetBaseValue(StatType stat) => Settings?.basePlayerStats == null ? 0f : stat switch
    {
        StatType.ST_HP => Settings.basePlayerStats.baseHP,
        StatType.ST_ATK => Settings.basePlayerStats.baseATK,
        StatType.ST_DEF => Settings.basePlayerStats.baseDEF,
        _ => 0f
    };

    public bool TryReload(out string error)
    {
        error = null;
        if (_transactionInProgress) { error = "다른 강화 처리가 진행 중입니다."; return false; }
        // Reload is a recovery operation. A healthy in-memory account is not replaced mid-run.
        if (IsReady) return true;
        _transactionInProgress = true;
        try
        {
            if (Settings == null) return Reject("PlayerUpgradeSettings 강화 설정을 찾을 수 없습니다.", out error);
            if (!Settings.Validate(out error)) { LastError = error; return false; }
            if (!_store.TryLoad(out var loaded, out error)) { LastError = error; return false; }
            if (loaded == null || !loaded.Validate(out error))
                return Reject(error ?? "강화 저장 데이터가 없습니다.", out error);
            if (loaded.healthLevel > Settings.maxLevel || loaded.attackLevel > Settings.maxLevel
                || loaded.defenseLevel > Settings.maxLevel)
                return Reject("저장된 강화 단계가 설정된 최대 단계를 초과합니다. 기존 파일은 보존됩니다.", out error);
            _profile = loaded.Copy();
            IsReady = true;
            LastError = null;
            NotifyChanged();
            return true;
        }
        catch (Exception failure) { return Reject("강화 데이터를 불러오지 못했습니다. " + failure.Message, out error); }
        finally { _transactionInProgress = false; }
    }

    public bool TryUpgrade(StatType stat, out string error)
    {
        error = null;
        if (!CanMutate(out error)) return false;
        if (!Supports(stat)) return Reject("강화할 수 없는 능력치입니다.", out error);
        if (GetLevel(stat) >= Settings.maxLevel) return Reject("최대 강화 단계입니다.", out error);
        int cost = GetNextCost(stat);
        if (Balance < cost) return Reject(Settings.currencyName + "이(가) 부족합니다.", out error);
        var next = _profile.Copy();
        next.currency -= cost;
        if (stat == StatType.ST_HP) next.healthLevel++;
        else if (stat == StatType.ST_ATK) next.attackLevel++;
        else next.defenseLevel++;
        return Commit(next, null, out error);
    }

    /// <summary>Repeated successful reward IDs are no-ops. Failed writes leave the ID retryable.</summary>
    public bool TryGrantReward(string uniqueId, int amount, out string error)
    {
        error = null;
        if (!CanMutate(out error)) return false;
        if (string.IsNullOrWhiteSpace(uniqueId) || amount < 0)
            return Reject("강화 재화 보상 정보가 유효하지 않습니다.", out error);
        if (_rewardIds.Contains(uniqueId)) return true;
        if (amount > int.MaxValue - Balance) return Reject("강화 재화 보유 한도를 초과합니다.", out error);
        var next = _profile.Copy();
        next.currency += amount;
        return Commit(next, uniqueId, out error);
    }

    private bool CanMutate(out string error)
    {
        error = null;
        if (_transactionInProgress) { error = "다른 강화 처리가 진행 중입니다."; return false; }
        if (!IsReady) return Reject(LastError ?? "강화 데이터를 불러오지 못했습니다.", out error);
        if (!Settings.Validate(out error)) { LastError = error; return false; }
        return true;
    }

    private bool Commit(PlayerUpgradeProfile next, string rewardId, out string error)
    {
        error = null;
        _transactionInProgress = true;
        try
        {
            if (!_store.TrySave(next.Copy(), out error))
                return Reject(error ?? "강화 데이터를 저장하지 못했습니다.", out error);
            _profile = next;
            if (rewardId != null) _rewardIds.Add(rewardId);
            LastError = null;
            NotifyChanged();
            return true;
        }
        catch (Exception failure) { return Reject("강화 데이터를 저장하지 못했습니다. " + failure.Message, out error); }
        finally { _transactionInProgress = false; }
    }

    private bool Reject(string message, out string error) { LastError = error = message; return false; }
    private static bool Supports(StatType stat) => stat == StatType.ST_HP || stat == StatType.ST_ATK || stat == StatType.ST_DEF;

    private void NotifyChanged()
    {
        if (Changed == null) return;
        foreach (Action callback in Changed.GetInvocationList())
        {
            try { callback(); }
            catch (Exception failure) { Debug.LogWarning("[Upgrades] Observer failed: " + failure); }
        }
    }
}
