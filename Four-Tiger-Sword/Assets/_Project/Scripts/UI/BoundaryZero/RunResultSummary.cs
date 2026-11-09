/// <summary>The run information and settled rewards shown on the result screen.</summary>
public readonly struct RunResultSummary
{
    public RunResultSummary(string progressLabel, long stageReward, int deathReward, int clearedRooms,
        string currencyName, int? balance, int goldBeforeConversion = 0, int goldConversionReward = 0)
    {
        ProgressLabel = progressLabel;
        StageReward = stageReward;
        DeathReward = deathReward;
        ClearedRooms = clearedRooms;
        CurrencyName = currencyName;
        Balance = balance;
        GoldBeforeConversion = goldBeforeConversion;
        GoldConversionReward = goldConversionReward;
    }

    public string ProgressLabel { get; }
    public long StageReward { get; }
    public int DeathReward { get; }
    public int ClearedRooms { get; }
    public string CurrencyName { get; }
    public int? Balance { get; }
    public int GoldBeforeConversion { get; }
    public int GoldConversionReward { get; }
}
