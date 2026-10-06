using System;

namespace AssetInventory
{
    /// <summary>Reference to a dependent indexed file by catalog identity, name, and source location.</summary>
    [Serializable]
    public sealed class Dependency
    {
        public string location;
        public int id;
        public string name;

        public Dependency()
        {
        }

        public override string ToString()
        {
            return $"Dependency '{name}' ({id}, '{location}')";
        }
    }
}
