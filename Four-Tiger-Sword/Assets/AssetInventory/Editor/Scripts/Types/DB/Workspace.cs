using System;
using System.Collections.Generic;
using SQLite;

namespace AssetInventory
{
    /// <summary>Named collection of saved package and file searches that can be restored together.</summary>
    [Serializable]
    public sealed class Workspace
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] [Collation("NOCASE")] public string Name { get; set; }

        // runtime
        [Ignore] public List<WorkspaceSearch> Searches { get; set; }

        public Workspace()
        {
        }

        /// <summary>Creates a named workspace with an initially empty saved-search collection.</summary>
        public Workspace(string name)
        {
            Name = name;
        }

        /// <summary>Deserializes the package and file searches stored with this workspace; malformed entries are skipped.</summary>
        public List<WorkspaceSearch> LoadSearches()
        {
            Searches = DBAdapter.DB.Query<WorkspaceSearch>("SELECT * FROM WorkspaceSearch WHERE WorkspaceId = ? order by OrderIdx", Id);
            return Searches;
        }

        public override string ToString()
        {
            return $"Workspace '{Name}'";
        }
    }
}
