public partial class PlayerController
{
    /// <summary>A non-destructive modal pause: the active action and all run resources are retained.</summary>
    public bool IsShopPaused { get; private set; }
    public bool IsMapPaused { get; private set; }
    public bool IsPauseMenuPaused { get; private set; }
    public bool IsGameplayModalPaused => IsShopPaused || IsMapPaused || IsInventoryPaused || IsRewardPaused
        || IsPauseMenuPaused || IsFallRecovering;

    internal void SetShopPaused(bool paused)
    {
        IsShopPaused = paused;
        if (Input != null) Input.SetShopInputBlocked(paused);
    }

    internal void SetMapPaused(bool paused)
    {
        IsMapPaused = paused;
        if (Input != null) Input.SetMapInputBlocked(paused);
    }

    internal void SetPauseMenuPaused(bool paused)
    {
        IsPauseMenuPaused = paused;
        if (Input != null) Input.SetPauseMenuInputBlocked(paused);
    }
}
