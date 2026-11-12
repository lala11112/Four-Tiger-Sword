using System;
using System.Collections.Generic;

/// <summary>Permanent progression and the paid entitlement for the next run.</summary>
[Serializable]
public sealed class PlayerUpgradeProfile
{
    public const int CurrentVersion = 2;
    public int version;
    public int currency;
    public int healthLevel;
    public int attackLevel;
    public int defenseLevel;
    public string[] unlockedNodeIds = Array.Empty<string>();
    public string supplyDepotItemId;
    public string supplyDepotTransactionId;

    public static PlayerUpgradeProfile New() => new PlayerUpgradeProfile { version = CurrentVersion };
    public PlayerUpgradeProfile Copy() => new PlayerUpgradeProfile
    {
        version = version, currency = currency, healthLevel = healthLevel,
        attackLevel = attackLevel, defenseLevel = defenseLevel,
        unlockedNodeIds = unlockedNodeIds != null ? (string[])unlockedNodeIds.Clone() : Array.Empty<string>(),
        supplyDepotItemId = supplyDepotItemId, supplyDepotTransactionId = supplyDepotTransactionId
    };

    // Migrate retired purchases together with their original costs. The next normal
    // atomic save writes both the filtered IDs and the balance, so a reload cannot pay twice.
    internal bool TryMigrateRetiredEngravings(out PlayerUpgradeProfile migrated)
    {
        migrated = this;
        var ids = unlockedNodeIds ?? Array.Empty<string>();
        bool heirloom = Array.IndexOf(ids, "heirloom") >= 0;
        bool starterSynergy = Array.IndexOf(ids, "starterelement") >= 0;
        if (!heirloom) return !starterSynergy;

        migrated = Copy();
        migrated.unlockedNodeIds = Array.FindAll(ids, id => id != "heirloom" && id != "starterelement");
        long balance = (long)currency + (starterSynergy ? 30 : 10);
        migrated.currency = (int)Math.Min(int.MaxValue, balance);
        return true;
    }

    public bool Validate(out string error)
    {
        error = version != 1 && version != CurrentVersion ? "지원하지 않는 강화 저장 데이터 버전입니다."
            : currency < 0 || healthLevel < 0 || attackLevel < 0 || defenseLevel < 0
                ? "강화 저장 데이터의 잔액 또는 단계가 유효하지 않습니다." : null;
        if (error != null) return false;
        if (version == 2 && unlockedNodeIds == null) { error = "각인 저장 목록이 없습니다."; return false; }
        bool hasDepotReceipt = !string.IsNullOrEmpty(supplyDepotItemId) || !string.IsNullOrEmpty(supplyDepotTransactionId);
        if (hasDepotReceipt && (string.IsNullOrWhiteSpace(supplyDepotItemId) || string.IsNullOrWhiteSpace(supplyDepotTransactionId)))
        { error = "보급고 구매 기록이 유효하지 않습니다."; return false; }
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in unlockedNodeIds ?? Array.Empty<string>())
            if (string.IsNullOrWhiteSpace(id) || id == "origin" || !unique.Add(id))
            { error = "각인 저장 목록이 중복되거나 유효하지 않습니다."; return false; }
        return true;
    }
}

/// <summary>Implementations must not report a successful write before the profile is committed.</summary>
public interface IPlayerUpgradeStore
{
    bool TryLoad(out PlayerUpgradeProfile profile, out string error);
    bool TrySave(PlayerUpgradeProfile profile, out string error);
}
