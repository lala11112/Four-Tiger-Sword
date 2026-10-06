using System;
using SQLite;

namespace AssetInventory
{
    /// <summary>Search facade for querying workspace records with structured options and result metadata.</summary>
    [Serializable]
    public sealed class WorkspaceSearch
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] public int WorkspaceId { get; set; }
        [Indexed] public int SavedSearchId { get; set; }
        public int OrderIdx { get; set; }

        public WorkspaceSearch()
        {
        }

        public override string ToString()
        {
            return $"Workspace Search Assignment '{SavedSearchId} -> {WorkspaceId}'";
        }
    }
}
