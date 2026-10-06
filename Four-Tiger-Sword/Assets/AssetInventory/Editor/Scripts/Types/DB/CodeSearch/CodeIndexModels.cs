using System;
using SQLite;

namespace AssetInventory
{
    public sealed class CodeDocument
    {
        public enum SourceKindType
        {
            Project = 0,
            IndexedPackage = 1
        }

        public enum DocumentStatus
        {
            Ready = 0,
            Deleted = 1,
            Error = 2
        }

        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] public string StableKey { get; set; }
        [Indexed] public SourceKindType SourceKind { get; set; }
        [Indexed] public int AssetId { get; set; }
        [Indexed] public int AssetFileId { get; set; }
        [Indexed] public string Guid { get; set; }
        [Indexed] public string Extension { get; set; }
        [Indexed] public DocumentStatus Status { get; set; }
        public string Path { get; set; }
        public string FileName { get; set; }
        public string PhysicalPath { get; set; }
        public string PackageName { get; set; }
        public string Language { get; set; }
        public long Size { get; set; }
        public long LastWriteTicks { get; set; }
        public string ContentHash { get; set; }
        public int LastSeenGeneration { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class CodeChunk
    {
        public enum ChunkStatus
        {
            Ready = 0,
            Deleted = 1,
            Error = 2
        }

        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] public int DocumentId { get; set; }
        [Indexed] public string StableKey { get; set; }
        [Indexed] public string ChunkKey { get; set; }
        [Indexed] public string ContentHash { get; set; }
        [Indexed] public ChunkStatus Status { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string Symbol { get; set; }
        public string Content { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class CodeIndexProperty
    {
        [PrimaryKey] public string Name { get; set; }
        public string Value { get; set; }

        public CodeIndexProperty()
        {
        }

        public CodeIndexProperty(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    public sealed class CodeEmbeddingProfile
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] public string Provider { get; set; }
        [Indexed] public string Model { get; set; }
        public int Dimension { get; set; }
        public string Distance { get; set; }
        public string Encoding { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class CodeChunkVector
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] public int CodeChunkId { get; set; }
        [Indexed] public int ProfileId { get; set; }
        public byte[] VectorBlob { get; set; }
    }
}
