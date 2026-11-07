using System;

/// <summary>Only permanent progression is stored here; run gold and items have their own lifetime.</summary>
[Serializable]
public sealed class PlayerUpgradeProfile
{
    public const int CurrentVersion = 1;
    public int version;
    public int currency;
    public int healthLevel;
    public int attackLevel;
    public int defenseLevel;

    public static PlayerUpgradeProfile New() => new PlayerUpgradeProfile { version = CurrentVersion };
    public PlayerUpgradeProfile Copy() => new PlayerUpgradeProfile
    {
        version = version, currency = currency, healthLevel = healthLevel,
        attackLevel = attackLevel, defenseLevel = defenseLevel
    };

    public bool Validate(out string error)
    {
        error = version != CurrentVersion ? "지원하지 않는 강화 저장 데이터 버전입니다."
            : currency < 0 || healthLevel < 0 || attackLevel < 0 || defenseLevel < 0
                ? "강화 저장 데이터의 잔액 또는 단계가 유효하지 않습니다." : null;
        return error == null;
    }
}

/// <summary>Implementations must not report a successful write before the profile is committed.</summary>
public interface IPlayerUpgradeStore
{
    bool TryLoad(out PlayerUpgradeProfile profile, out string error);
    bool TrySave(PlayerUpgradeProfile profile, out string error);
}
