using System;
using SQLite;

namespace AssetInventory
{
    public sealed class SemanticItem
    {
        public enum ItemStatus
        {
            Ready = 0,
            Dirty = 1,
            Deleted = 2,
            Error = 3
        }

        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] public int ProfileId { get; set; }
        [Indexed] public string Collection { get; set; }
        [Indexed] public string StableKey { get; set; }
        [Indexed] public int AssetFileId { get; set; }
        [Indexed] public int AssetId { get; set; }
        public string Guid { get; set; }
        public string ChunkKey { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        [Indexed] public string ContentHash { get; set; }
        [Indexed] public ItemStatus Status { get; set; }
        public int LastSeenGeneration { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string ErrorMessage { get; set; }
        public string SourcePreview { get; set; }
    }
}
