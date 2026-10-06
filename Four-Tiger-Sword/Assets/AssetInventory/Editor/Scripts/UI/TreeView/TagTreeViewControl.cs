namespace AssetInventory
{
    internal static class TagTreeViewControl
    {
        internal static bool CanAssignColor(Tag tag)
        {
            return tag != null;
        }

        internal static bool CanAssignHotkey(Tag tag)
        {
            return tag != null;
        }

        internal static bool CanRenameTag(Tag tag)
        {
            return tag != null;
        }

        internal static bool CanDeleteTag(Tag tag)
        {
            return CanRenameTag(tag);
        }
    }
}
