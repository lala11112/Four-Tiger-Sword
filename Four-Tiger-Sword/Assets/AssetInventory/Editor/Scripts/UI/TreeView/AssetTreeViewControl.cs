using System;
using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;

namespace AssetInventory
{
    internal sealed partial class AssetTreeViewControl
    {
        private const float TOGGLE_WIDTH = 20f;

        public enum Columns
        {
            Name,
            Tags,
            Version,
            Indexed,
            Downloaded,
            ModifiedDate,
            ModifiedDateRelative,
            Publisher,
            Category,
            UnityVersions,
            BIRP,
            URP,
            HDRP,
            License,
            Price,
            ReleaseDate,
            ReleaseDateRelative,
            PurchaseDate,
            PurchaseDateRelative,
            UpdateDate,
            UpdateDateRelative,
            State,
            Source,
            Location,
            Size,
            FileCount,
            Rating,
            RatingCount,
            Update,
            Backup,
            Extract,
            Exclude,
            AICaptions,
            Deprecated,
            Outdated,
            Popularity,
            Materialized,
            ForeignId,
            InternalState,
            Media,
            SemanticIndex,
            CodeIndex,
            Rules,
            BackupCount,
            NoIndex
        }

        internal enum PackageIndexColumnState
        {
            NotIndexed,
            NoIndex,
            Indexed
        }

        internal const string NoIndexColumnGlyph = "\u2014";
        internal const string NoIndexColumnTooltip = "Not included in future indexing. Existing indexed content is retained.";

        private readonly TreeModel<AssetInfo> _treeModel;
        private readonly Func<AssetInfo, int?> _backupCountProvider;
        private readonly EditorWindow _owner;

        public AssetTreeViewControl(
            TreeModel<AssetInfo> treeModel,
            Func<AssetInfo, int?> backupCountProvider = null,
            EditorWindow owner = null)
        {
            _treeModel = treeModel;
            _backupCountProvider = backupCountProvider;
            _owner = owner;
        }

        public static CommonMultiColumnState CreateDefaultMultiColumnState()
        {
            Dictionary<int, CommonMultiColumnColumn> columns = new Dictionary<int, CommonMultiColumnColumn>();
            columns[(int)Columns.Name] = new CommonMultiColumnColumn(
                "Name",
                340f,
                180f,
                800f,
                optional: false,
                stretchable: true);
            columns[(int)Columns.License] = GetTextColumn("License");
            columns[(int)Columns.Tags] = GetTextColumn("Tags", 160, minWidth: 100f);
            columns[(int)Columns.Version] = GetTextColumn("Version", 110, minWidth: 80f);
            columns[(int)Columns.Media] = GetTextColumn("Media", 200);
            columns[(int)Columns.Indexed] = GetCheckmarkColumn("Indexed");
            columns[(int)Columns.FileCount] = GetTextColumn("Indexed Files");
            columns[(int)Columns.Downloaded] = GetCheckmarkColumn("Downloaded");
            columns[(int)Columns.ModifiedDate] = GetTextColumn("Modified Date");
            columns[(int)Columns.ModifiedDateRelative] = GetTextColumn("Modified Date Rel");
            columns[(int)Columns.Publisher] = GetTextColumn("Publisher");
            columns[(int)Columns.Category] = GetTextColumn("Category");
            columns[(int)Columns.UnityVersions] = GetTextColumn("Unity Versions");
            columns[(int)Columns.BIRP] = GetCheckmarkColumn("BIRP");
            columns[(int)Columns.URP] = GetCheckmarkColumn("URP");
            columns[(int)Columns.HDRP] = GetCheckmarkColumn("HDRP");
            columns[(int)Columns.Price] = GetTextColumn("Price");
            columns[(int)Columns.State] = GetTextColumn("State");
            columns[(int)Columns.Source] = GetTextColumn("Source");
            columns[(int)Columns.Size] = GetTextColumn("Size");
            columns[(int)Columns.Rating] = GetTextColumn("Rating");
            columns[(int)Columns.RatingCount] = GetTextColumn("#Reviews");
            columns[(int)Columns.ReleaseDate] = GetTextColumn("Release Date");
            columns[(int)Columns.ReleaseDateRelative] = GetTextColumn("Release Date Rel");
            columns[(int)Columns.PurchaseDate] = GetTextColumn("Purchase Date");
            columns[(int)Columns.PurchaseDateRelative] = GetTextColumn("Purchase Date Rel");
            columns[(int)Columns.UpdateDate] = GetTextColumn("Update Date");
            columns[(int)Columns.UpdateDateRelative] = GetTextColumn("Update Date Rel");
            columns[(int)Columns.Location] = GetTextColumn("Location");
            columns[(int)Columns.Update] = GetCheckmarkColumn("Update");
            columns[(int)Columns.Backup] = GetCheckmarkColumn("Backup");
            columns[(int)Columns.Extract] = GetCheckmarkColumn("Extract");
            columns[(int)Columns.Exclude] = GetCheckmarkColumn("Exclude");
            columns[(int)Columns.AICaptions] = GetCheckmarkColumn("AI Captions");
            columns[(int)Columns.Popularity] = GetTextColumn("Popularity");
            columns[(int)Columns.Deprecated] = GetCheckmarkColumn("Deprecated");
            columns[(int)Columns.Outdated] = GetCheckmarkColumn("Outdated");
            columns[(int)Columns.Materialized] = GetCheckmarkColumn("Cached");
            columns[(int)Columns.ForeignId] = GetTextColumn("Foreign Id");
            columns[(int)Columns.InternalState] = GetTextColumn("Processing");
            columns[(int)Columns.SemanticIndex] = GetCheckmarkColumn("Semantic Index");
            columns[(int)Columns.CodeIndex] = GetCheckmarkColumn("Code Index");
            columns[(int)Columns.Rules] = GetTextColumn("Rules", 80, minWidth: 60f);
            columns[(int)Columns.BackupCount] = GetTextColumn("#Backups", 70, minWidth: 60f);
            columns[(int)Columns.NoIndex] = GetCheckmarkColumn("Future Indexing Off");

            List<MetadataDefinition> metadataDefs = Metadata.LoadDefinitions();
            if (metadataDefs.Any())
            {
                const int offset = 100;
                columns[offset] = GetTextColumn(string.Empty);
                foreach (MetadataDefinition definition in metadataDefs)
                {
                    columns[offset + definition.Id] = definition.Type == MetadataDefinition.DataType.Boolean
                        ? GetCheckmarkColumn(definition.Name, definition.Id)
                        : GetTextColumn(definition.Name, 150, definition.Id);
                }
            }

            return new CommonMultiColumnState(columns.OrderBy(column => column.Key).Select(column => column.Value).ToArray());
        }

        internal static string GetPackageRuleSummary(AssetInfo info)
        {
            if (!TryGetPackageRuleSet(info, out HideRuleSet ruleSet)) return "-";

            string mode = ruleSet.Mode == HideRuleMode.Include ? "Include" : "Hide";
            return $"{mode}: {ruleSet.Rules.Count}";
        }

        internal static int GetPackageRuleSortBucket(AssetInfo info)
        {
            if (!TryGetPackageRuleSet(info, out HideRuleSet ruleSet)) return 0;
            return ruleSet.Mode == HideRuleMode.Include ? 2 : 1;
        }

        internal static int GetPackageRuleCount(AssetInfo info)
        {
            return TryGetPackageRuleSet(info, out HideRuleSet ruleSet) ? ruleSet.Rules.Count : 0;
        }

        internal static PackageIndexColumnState GetPackageIndexColumnState(AssetInfo info)
        {
            if (info == null) return PackageIndexColumnState.NotIndexed;
            if (PackageIndexingPolicy.HasNoIndex(info)) return PackageIndexColumnState.NoIndex;
            return info.IsIndexed ? PackageIndexColumnState.Indexed : PackageIndexColumnState.NotIndexed;
        }

        internal static int GetPackageIndexColumnSortBucket(AssetInfo info)
        {
            return (int)GetPackageIndexColumnState(info);
        }

        private static bool TryGetPackageRuleSet(AssetInfo info, out HideRuleSet ruleSet)
        {
            ruleSet = null;
            if (info == null || info.AssetId <= 0) return false;

            MetadataInfo hideMetadata = info.PackageMetadata?.FirstOrDefault(metadata => metadata.Name == MetadataDefinition.FIELD_HIDE);
            if (hideMetadata == null || string.IsNullOrWhiteSpace(hideMetadata.StringValue)) return false;

            ruleSet = HideRuleSet.Parse(hideMetadata.StringValue);
            if (ruleSet.Mode != HideRuleMode.Hide || ruleSet.Rules.Count > 0) return true;

            ruleSet = null;
            return false;
        }

        private static CommonMultiColumnColumn GetCheckmarkColumn(string name, int metadataDefinitionId = 0)
        {
            return new CommonMultiColumnColumn(name, 48f, 36f, 64f, userData: metadataDefinitionId);
        }

        private static CommonMultiColumnColumn GetTextColumn(
            string name,
            int width = 140,
            int metadataDefinitionId = 0,
            float minWidth = 70f)
        {
            return new CommonMultiColumnColumn(name, width, minWidth, userData: metadataDefinitionId);
        }
    }
}
