using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetInventory
{
    internal sealed class TagSelectionRow
    {
        public Tag Tag { get; }
        public int Depth { get; }
        public string FullPath { get; }
        public bool MatchesFilter { get; }
        public bool IsContextOnly { get; }
        public bool IsSelectable { get; }

        public TagSelectionRow(Tag tag, int depth, string fullPath, bool matchesFilter, bool isContextOnly, bool isSelectable)
        {
            Tag = tag;
            Depth = depth;
            FullPath = fullPath;
            MatchesFilter = matchesFilter;
            IsContextOnly = isContextOnly;
            IsSelectable = isSelectable;
        }
    }

    internal sealed class TagSelectionKeyboardState
    {
        public int? ActiveTagId { get; private set; }

        public void Clear()
        {
            ActiveTagId = null;
        }

        public bool Move(IReadOnlyList<TagSelectionRow> rows, int direction)
        {
            if (rows == null || rows.Count == 0 || direction == 0)
            {
                Clear();
                return false;
            }

            int firstSelectableIndex = -1;
            int lastSelectableIndex = -1;
            int activeIndex = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                TagSelectionRow row = rows[i];
                if (row == null || !row.IsSelectable) continue;

                if (firstSelectableIndex < 0) firstSelectableIndex = i;
                lastSelectableIndex = i;
                if (ActiveTagId.HasValue && row.Tag.Id == ActiveTagId.Value) activeIndex = i;
            }

            if (firstSelectableIndex < 0)
            {
                Clear();
                return false;
            }

            int targetIndex = activeIndex;
            if (activeIndex < 0)
            {
                targetIndex = direction > 0 ? firstSelectableIndex : lastSelectableIndex;
            }
            else
            {
                int step = Math.Sign(direction);
                for (int i = activeIndex + step; i >= 0 && i < rows.Count; i += step)
                {
                    if (!rows[i].IsSelectable) continue;

                    targetIndex = i;
                    break;
                }
            }

            ActiveTagId = rows[targetIndex].Tag.Id;
            return true;
        }

        public void RetainIfSelectable(IReadOnlyList<TagSelectionRow> rows)
        {
            if (!TryGetActiveRow(rows, out _)) Clear();
        }

        public bool TryGetActiveRow(IReadOnlyList<TagSelectionRow> rows, out TagSelectionRow activeRow)
        {
            activeRow = null;
            if (!ActiveTagId.HasValue || rows == null) return false;

            for (int i = 0; i < rows.Count; i++)
            {
                TagSelectionRow row = rows[i];
                if (row == null || !row.IsSelectable || row.Tag.Id != ActiveTagId.Value) continue;

                activeRow = row;
                return true;
            }

            return false;
        }
    }

    internal static class TagSelectionRows
    {
        private sealed class Node
        {
            public Tag Tag;
            public int Depth;
            public string FullPath;
            public bool MatchesFilter;
            public readonly List<Node> Children = new List<Node>();
        }

        public static IEnumerable<TagSelectionRow> Build(IReadOnlyList<Tag> tags, string filter, ISet<int> assignedTagIds)
        {
            if (tags == null || tags.Count == 0) yield break;

            string normalizedFilter = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
            HashSet<int> assignedIds = assignedTagIds != null ? new HashSet<int>(assignedTagIds) : new HashSet<int>();

            List<Node> roots = BuildTree(tags);
            foreach (Node root in roots)
            {
                foreach (TagSelectionRow row in BuildRows(root, normalizedFilter, assignedIds))
                {
                    yield return row;
                }
            }
        }

        private static List<Node> BuildTree(IReadOnlyList<Tag> tags)
        {
            Dictionary<int, Node> nodes = tags
                .Where(tag => tag != null)
                .GroupBy(tag => tag.Id)
                .Select(group => group.First())
                .ToDictionary(tag => tag.Id, tag => new Node { Tag = tag });

            foreach (Node node in nodes.Values)
            {
                if (node.Tag.ParentId.HasValue && nodes.TryGetValue(node.Tag.ParentId.Value, out Node parent))
                {
                    parent.Children.Add(node);
                }
            }

            List<Node> roots = nodes.Values
                .Where(node => !node.Tag.ParentId.HasValue || !nodes.ContainsKey(node.Tag.ParentId.Value))
                .OrderBy(node => node.Tag.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (Node root in roots)
            {
                PopulateTreeMetadata(root, 0, root.Tag.Name);
            }

            return roots;
        }

        private static void PopulateTreeMetadata(Node node, int depth, string fullPath)
        {
            node.Depth = depth;
            node.FullPath = fullPath;
            node.Children.Sort((left, right) => string.Compare(left.Tag.Name, right.Tag.Name, StringComparison.OrdinalIgnoreCase));

            foreach (Node child in node.Children)
            {
                PopulateTreeMetadata(child, depth + 1, $"{fullPath} / {child.Tag.Name}");
            }
        }

        private static IEnumerable<TagSelectionRow> BuildRows(Node node, string filter, HashSet<int> assignedIds)
        {
            bool hasFilter = !string.IsNullOrWhiteSpace(filter);
            node.MatchesFilter = !hasFilter || Matches(node, filter);

            List<TagSelectionRow> childRows = new List<TagSelectionRow>();
            foreach (Node child in node.Children)
            {
                childRows.AddRange(BuildRows(child, filter, assignedIds));
            }

            bool isAssigned = assignedIds.Contains(node.Tag.Id);
            bool hasVisibleChildren = childRows.Count > 0;
            bool shouldShow = hasFilter
                ? (!isAssigned && node.MatchesFilter) || hasVisibleChildren
                : !isAssigned;

            if (!shouldShow)
            {
                foreach (TagSelectionRow childRow in childRows)
                {
                    yield return childRow;
                }
                yield break;
            }

            bool isContextOnly = hasFilter && (!node.MatchesFilter || isAssigned);
            bool isSelectable = !isAssigned && !isContextOnly;

            yield return new TagSelectionRow(node.Tag, node.Depth, node.FullPath, node.MatchesFilter, isContextOnly, isSelectable);

            foreach (TagSelectionRow childRow in childRows)
            {
                yield return childRow;
            }
        }

        private static bool Matches(Node node, string filter)
        {
            return node.Tag.Name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || node.FullPath?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
