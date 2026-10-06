using System;
using SQLite;

namespace AssetInventory
{
    /// <summary>Persistent link between a tag and either a package or indexed file.</summary>
    [Serializable]
    public class TagAssignment
    {
        public enum Target
        {
            Package = 0,
            Asset = 1
        }

        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] public int TagId { get; set; }
        [Indexed] public Target TagTarget { get; set; }
        [Indexed] public int TargetId { get; set; }

        public TagAssignment()
        {
        }

        /// <summary>Creates a tag link for the supplied tag, target kind, and package or file identity.</summary>
        public TagAssignment(int tagId, Target tagTarget, int targetId)
        {
            TagId = tagId;
            TagTarget = tagTarget;
            TargetId = targetId;
        }

        public override string ToString()
        {
            return $"Tag Assignment '{TagTarget}' ({TagId})";
        }
    }
}
