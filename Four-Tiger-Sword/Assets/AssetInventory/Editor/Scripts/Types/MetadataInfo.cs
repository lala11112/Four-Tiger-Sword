using System;

namespace AssetInventory
{
    /// <summary>Joined metadata assignment and definition data, including the typed value and its package or file target.</summary>
    [Serializable]
    // used to contain results of join calls
    public sealed class MetadataInfo : MetadataAssignment
    {
        public int DefinitionId { get; set; }
        public string Name { get; set; }
        public MetadataDefinition.DataType Type { get; set; }
        public string ValueList { get; set; }
        public bool RestrictAssetGroup { get; set; }
        public AI.AssetGroup ApplicableGroup { get; set; }
        public bool RestrictAssetSource { get; set; }
        public Asset.Source ApplicableSource { get; set; }

        /// <summary>Creates a persistent metadata assignment from this resolved definition and target value.</summary>
        public MetadataAssignment ToAssignment()
        {
            // TODO: reflection?
            MetadataAssignment result = new MetadataAssignment
            {
                Id = Id,
                StringValue = StringValue,
                IntValue = IntValue,
                FloatValue = FloatValue,
                BoolValue = BoolValue,
                DateTimeValue = DateTimeValue,
                MetadataId = DefinitionId,
                MetadataTarget = MetadataTarget,
                TargetId = TargetId
            };

            return result;
        }

        public override string ToString()
        {
            return $"Metadata Info '{Name}' ('{MetadataTarget}', {TargetId})";
        }
    }
}
