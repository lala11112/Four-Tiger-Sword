using System;

namespace AssetInventory
{
    /// <summary>Identifier and relationship type pair used for links in parsed Unity package headers.</summary>
    [Serializable]
    public sealed class AssetHeaderIdType
    {
        public string id;
        public string type;

        public override string ToString()
        {
            return $"Asset Header Type ({id}, {type})";
        }
    }
}
