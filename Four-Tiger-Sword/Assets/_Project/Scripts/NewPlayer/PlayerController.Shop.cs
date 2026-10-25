public partial class PlayerController
{
    /// <summary>A non-destructive modal pause: the active action and all run resources are retained.</summary>
    public bool IsShopPaused { get; private set; }

    internal void SetShopPaused(bool paused)
    {
        IsShopPaused = paused;
        if (Input != null) Input.SetShopInputBlocked(paused);
    }
}
