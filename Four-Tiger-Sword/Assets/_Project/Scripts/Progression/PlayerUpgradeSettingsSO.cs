using UnityEngine;

[CreateAssetMenu(fileName = "PlayerUpgradeSettings", menuName = "Data/Player Upgrade Settings")]
public sealed class PlayerUpgradeSettingsSO : ScriptableObject
{
    public string currencyName = "강화석";
    [Min(1)] public int maxLevel = 20;
    [Min(0f)] public float healthPerLevel = .05f;
    [Min(0f)] public float attackPerLevel = .05f;
    [Min(0f)] public float defensePerLevel = .05f;
    [Min(1)] public int initialCost = 10;
    [Min(0)] public int costPerLevel = 5;
    [Min(0)] public int stageClearReward = 10;
    [Min(0)] public int bossClearReward = 30;
    [Min(0)] public int deathRewardPerRoom = 2;
    [Min(0)] public int deathRewardCap = 10;
    [Min(1)] public int goldPerUpgradeStone = 10;
    [Tooltip("실제 플레이어에 연결된 기본 능력치 에셋을 지정합니다.")]
    public PlayerStatData basePlayerStats;

    public bool Validate(out string error)
    {
        error = null;
        if (basePlayerStats == null) error = "강화 설정에 플레이어 기본 능력치가 연결되지 않았습니다.";
        else if (string.IsNullOrWhiteSpace(currencyName)) error = "강화 재화 이름이 비어 있습니다.";
        else if (maxLevel < 1 || initialCost < 1 || costPerLevel < 0
            || (long)initialCost + (long)(maxLevel - 1) * costPerLevel > int.MaxValue)
            error = "강화 최대 단계와 비용 설정을 확인해 주세요.";
        else if (!ValidRatio(healthPerLevel) || !ValidRatio(attackPerLevel) || !ValidRatio(defensePerLevel)
            || !ValidRatio(healthPerLevel * maxLevel) || !ValidRatio(attackPerLevel * maxLevel)
            || !ValidRatio(defensePerLevel * maxLevel)) error = "강화 증가율은 유효한 양수 또는 0이어야 합니다.";
        else if (!ValidBase(basePlayerStats.baseHP) || !ValidBase(basePlayerStats.baseATK)
            || !ValidBase(basePlayerStats.baseDEF)) error = "강화에 사용되는 기본 능력치가 유효하지 않습니다.";
        else if (stageClearReward < 0 || bossClearReward < 0 || deathRewardPerRoom < 0 || deathRewardCap < 0 || goldPerUpgradeStone < 1)
            error = "강화 재화 보상은 음수일 수 없습니다.";
        return error == null;
    }

    private static bool ValidRatio(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    private static bool ValidBase(float value) => ValidRatio(value) && value > 0f;
}
