using System;
using SQLite;

namespace AssetInventory
{
    /// <summary>Persistent named Automator workflow with its ordered Asset Inventory action steps and last execution state.</summary>
    [Serializable]
    public sealed class CustomAction
    {
        public enum Mode
        {
            Manual = 0,
            AtInstallation = 1
        }

        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] [Collation("NOCASE")] public string Name { get; set; }
        public string Description { get; set; }
        public bool StopOnFailure { get; set; } = true;
        public Mode RunMode { get; set; }

        public CustomAction()
        {
        }

        /// <summary>Creates an empty saved Automator action with the supplied display name.</summary>
        public CustomAction(string name) : this()
        {
            Name = name;
        }

        public override string ToString()
        {
            return $"Custom Action '{Name}'";
        }
    }
}
