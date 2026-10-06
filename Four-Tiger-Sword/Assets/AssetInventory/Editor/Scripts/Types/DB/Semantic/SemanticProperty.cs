using SQLite;

namespace AssetInventory
{
    public sealed class SemanticProperty
    {
        [PrimaryKey] public string Name { get; set; }
        public string Value { get; set; }

        public SemanticProperty()
        {
        }

        public SemanticProperty(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }
}
