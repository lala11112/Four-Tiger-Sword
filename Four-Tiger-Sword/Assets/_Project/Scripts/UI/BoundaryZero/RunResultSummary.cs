/// <summary>The run information and settled rewards shown on the result screen.</summary>
public readonly struct RunResultSummary
{
    public RunResultSummary(string progressLabel, long stageReward, int deathReward, int clearedRooms,
        string currencyName, int? balance)
    {
        ProgressLabel = progressLabel;
        StageReward = stageReward;
        DeathReward = deathReward;
        ClearedRooms = clearedRooms;
        CurrencyName = currencyName;
        Balance = balance;
    }

    public string ProgressLabel { get; }
    public long StageReward { get; }
    public int DeathReward { get; }
    public int ClearedRooms { get; }
    public string CurrencyName { get; }
    public int? Balance { get; }
}
