using SQLite;

namespace AssetInventory
{
    public sealed class SemanticVector
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] public int SemanticItemId { get; set; }
        public byte[] VectorBlob { get; set; }
    }
}
