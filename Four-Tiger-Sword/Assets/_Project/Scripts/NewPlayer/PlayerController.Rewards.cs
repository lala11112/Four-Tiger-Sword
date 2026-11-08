public partial class PlayerController
{
    public bool IsRewardPaused { get; private set; }
    internal void SetRewardPaused(bool paused)
    {
        IsRewardPaused = paused;
        if (Input != null) Input.SetRewardInputBlocked(paused);
    }
}
