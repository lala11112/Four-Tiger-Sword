using System;
using SQLite;

namespace AssetInventory
{
    /// <summary>Search facade for querying saved code records with structured options and result metadata.</summary>
    [Serializable]
    public sealed class SavedCodeSearch
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        public string Name { get; set; }
        public string Icon { get; set; }
        public string Color { get; set; }

        public string SearchPhrase { get; set; }
        public string ExtensionFilter { get; set; }
        public string PathFilter { get; set; }
        public string SymbolFilter { get; set; }
    }
}
