using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    internal static class SearchTreeViewControl
    {
        public enum Columns
        {
            FileName,
            Path,
            Type,
            Size,
            Package
        }

        internal static VisualElement CreateNativeRetainedCell(int sourceColumnIndex)
        {
            CommonMultiColumnTreeCell cell = new CommonMultiColumnTreeCell();
            bool showIcon = (Columns)sourceColumnIndex == Columns.FileName;
            cell.Icon.scaleMode = ScaleMode.ScaleToFit;
            cell.Icon.style.display = showIcon ? DisplayStyle.Flex : DisplayStyle.None;
            if (showIcon) cell.Icon.AddToClassList("ai-native-tree-preview");
            cell.Action.style.display = DisplayStyle.None;
            cell.Accessory.style.display = DisplayStyle.None;
            return cell;
        }

        internal static void BindNativeRetainedCell(VisualElement element, AssetInfo info, int sourceColumnIndex, IndexUI indexUI)
        {
            if (!(element is CommonMultiColumnTreeCell cell)) return;

            Columns column = (Columns)sourceColumnIndex;
            string text = column == Columns.FileName ? info?.FileName : GetCellText(info, column);
            cell.Label.text = text ?? string.Empty;
            cell.Label.tooltip = text ?? string.Empty;

            if (column != Columns.FileName)
            {
                cell.Icon.style.display = DisplayStyle.None;
                return;
            }

            float iconSize = Mathf.Max(12f, AI.Config.searchListRowHeight - 4f);
            cell.Icon.style.width = iconSize;
            cell.Icon.style.height = iconSize;
            cell.Icon.image = info == null
                ? null
                : indexUI?.GetFilePreview(info.Id) ?? info.GetFallbackIcon();
            cell.Icon.tooltip = info?.FileName ?? string.Empty;
            cell.Icon.style.display = cell.Icon.image == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        internal static void UnbindNativeRetainedCell(VisualElement element, AssetInfo info, int sourceColumnIndex)
        {
            if (element is CommonMultiColumnTreeCell cell) cell.ResetContent();
        }

        private static string GetCellText(AssetInfo info, Columns column)
        {
            if (info == null) return string.Empty;

            switch (column)
            {
                case Columns.Path:
                    return info.ShortPath;

                case Columns.Type:
                    return info.Type;

                case Columns.Size:
                    return info.Size > 0 ? EditorUtility.FormatBytes(info.Size) : string.Empty;

                case Columns.Package:
                    return info.SafeName;

                default:
                    return string.Empty;
            }
        }

        public static CommonMultiColumnState CreateDefaultMultiColumnState()
        {
            int[] defaultVisibleColumns = new[]
            {
                (int)Columns.FileName,
                (int)Columns.Path,
                (int)Columns.Type,
                (int)Columns.Size,
                (int)Columns.Package
            };

            CommonMultiColumnColumn[] columns =
            {
                new CommonMultiColumnColumn("File Name", 250f, 100f, optional: false, stretchable: true),
                new CommonMultiColumnColumn("Path", 200f, 60f),
                new CommonMultiColumnColumn("Type", 80f, 40f),
                new CommonMultiColumnColumn("Size", 70f, 40f),
                new CommonMultiColumnColumn("Package", 150f, 60f, sortable: false)
            };

            return new CommonMultiColumnState(columns, defaultVisibleColumns);
        }

        internal static int GetSortField(int sourceColumnIndex)
        {
            switch ((Columns)sourceColumnIndex)
            {
                case Columns.FileName: return 3;
                case Columns.Path: return 2;
                case Columns.Type: return 5;
                case Columns.Size: return 4;
                default: return -1;
            }
        }

        internal static int GetSourceColumnIndex(int sortField)
        {
            switch (sortField)
            {
                case 2: return (int)Columns.Path;
                case 3: return (int)Columns.FileName;
                case 4: return (int)Columns.Size;
                case 5: return (int)Columns.Type;
                default: return -1;
            }
        }
    }
}
