public partial class PlayerController
{
    public bool IsInventoryPaused { get; private set; }

    internal void SetInventoryPaused(bool paused)
    {
        IsInventoryPaused = paused;
        if (Input != null) Input.SetInventoryInputBlocked(paused);
    }
}
