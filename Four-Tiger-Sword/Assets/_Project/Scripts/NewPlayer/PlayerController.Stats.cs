public partial class PlayerController
{
    public bool IsStatsOpen { get; private set; }

    internal void SetStatsOpen(bool open) => IsStatsOpen = open;
}
