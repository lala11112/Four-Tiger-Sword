using System;
using SQLite;

namespace AssetInventory
{
    public sealed class SemanticProfile
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] public string Provider { get; set; }
        [Indexed] public string Model { get; set; }
        public int Dimension { get; set; }
        public string Distance { get; set; }
        public string Encoding { get; set; }
        [Indexed] public string Collection { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
