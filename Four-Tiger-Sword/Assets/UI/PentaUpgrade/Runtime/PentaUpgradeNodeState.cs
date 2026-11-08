namespace Penta.Upgrade
{
    /// <summary>Gameplay data supplied by the host; this UI assembly owns no progression account.</summary>
    public enum PentaUpgradeAvailability { Locked, Available, Unaffordable, Owned }

    public sealed class PentaUpgradeNodeState
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string CostLabel;
        public string StatusText;
        public PentaUpgradeAvailability Availability;
        public bool CanPurchase;
    }
}
