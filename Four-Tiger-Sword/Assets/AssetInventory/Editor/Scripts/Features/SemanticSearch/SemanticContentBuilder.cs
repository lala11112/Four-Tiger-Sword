using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AssetInventory
{
    internal readonly struct SemanticContent
    {
        public readonly string StableKey;
        public readonly string Text;
        public readonly string Hash;
        public readonly string SourcePreview;

        public SemanticContent(string stableKey, string text, string hash, string sourcePreview)
        {
            StableKey = stableKey;
            Text = text;
            Hash = hash;
            SourcePreview = sourcePreview;
        }
    }

    internal static class SemanticContentBuilder
    {
        public const string AssetCollection = "assets";
        public const string CodeCollection = "code";
        private const int MaxMetadataValueLength = 220;
        private const int MaxMetadataTextLength = 1000;

        public static SemanticContent BuildAssetContent(AssetInfo info, SemanticContentInputs inputs = null)
        {
            inputs ??= SemanticContentInputs.Empty;

            string stableKey = BuildAssetStableKey(info);
            StringBuilder sb = new StringBuilder(512);

            Append(sb, "Name", info.FileName);
            Append(sb, "Name Terms", NormalizeTerms(info.FileName));
            Append(sb, "Path", info.Path);
            Append(sb, "Path Terms", NormalizeTerms(info.Path));
            Append(sb, "Type", DescribeType(info.Type));
            Append(sb, "Caption", info.AICaption);
            Append(sb, "File Tags", inputs.GetFileTags(info.Id));
            Append(sb, "File Metadata", inputs.GetFileMetadata(info.Id));
            Append(sb, "Package Tags", inputs.GetPackageTags(info.AssetId));
            Append(sb, "Package Metadata", inputs.GetPackageMetadata(info.AssetId));
            Append(sb, "Package Context", BuildPackageContext(info));

            if (info.Width > 0 || info.Height > 0) Append(sb, "Dimensions", $"{info.Width} x {info.Height}");
            if (info.Length > 0f) Append(sb, "Length", $"{info.Length:0.##} seconds");
            if (info.Hue >= 0f) Append(sb, "Color Hue", $"{info.Hue:0.#}");

            string text = sb.ToString().Trim();
            string hash = SemanticVectorUtils.HashText(text);
            return new SemanticContent(stableKey, text, hash, Truncate(text, 500));
        }

        internal static SemanticContentInputs LoadInputs()
        {
            return LoadInputs(null, null);
        }

        internal static SemanticContentInputs LoadInputs(IReadOnlyCollection<int> packageIds, IReadOnlyCollection<int> assetFileIds)
        {
            List<TagInfo> tags = LoadTagInputs(packageIds, assetFileIds);
            List<MetadataInfo> metadata = LoadMetadataInputs(packageIds, assetFileIds);

            Dictionary<int, string> packageTags = tags
                .Where(t => t.TagTarget == TagAssignment.Target.Package)
                .GroupBy(t => t.TargetId)
                .ToDictionary(g => g.Key, g => JoinDistinct(g.Select(t => t.Name)));
            Dictionary<int, string> fileTags = tags
                .Where(t => t.TagTarget == TagAssignment.Target.Asset)
                .GroupBy(t => t.TargetId)
                .ToDictionary(g => g.Key, g => JoinDistinct(g.Select(t => t.Name)));
            Dictionary<int, string> fileMetadata = metadata
                .Where(m => m.MetadataTarget == MetadataAssignment.Target.Asset)
                .GroupBy(m => m.TargetId)
                .ToDictionary(g => g.Key, g => JoinMetadata(g));
            Dictionary<int, string> packageMetadata = metadata
                .Where(m => m.MetadataTarget == MetadataAssignment.Target.Package)
                .GroupBy(m => m.TargetId)
                .ToDictionary(g => g.Key, g => JoinMetadata(g));

            return new SemanticContentInputs(packageTags, fileTags, packageMetadata, fileMetadata);
        }

        private static List<TagInfo> LoadTagInputs(IReadOnlyCollection<int> packageIds, IReadOnlyCollection<int> assetFileIds)
        {
            List<object> args = new List<object>();
            string filter = BuildTargetFilter("TagAssignment", "TagTarget", packageIds, assetFileIds, args);
            if (filter == null) return new List<TagInfo>();

            string where = string.IsNullOrEmpty(filter) ? string.Empty : $" where {filter}";
            return DBAdapter.DB.Query<TagInfo>(
                "SELECT *, TagAssignment.Id as Id from TagAssignment inner join Tag on Tag.Id = TagAssignment.TagId" +
                where +
                " order by TagTarget, TargetId, Tag.Name",
                args.ToArray());
        }

        private static List<MetadataInfo> LoadMetadataInputs(IReadOnlyCollection<int> packageIds, IReadOnlyCollection<int> assetFileIds)
        {
            List<object> args = new List<object>();
            string filter = BuildTargetFilter("MetadataAssignment", "MetadataTarget", packageIds, assetFileIds, args);
            if (filter == null) return new List<MetadataInfo>();

            string where = string.IsNullOrEmpty(filter) ? string.Empty : $" where {filter}";
            return DBAdapter.DB.Query<MetadataInfo>(
                "SELECT *, MetadataAssignment.Id as Id, MetadataDefinition.Id as DefinitionId from MetadataAssignment inner join MetadataDefinition on MetadataDefinition.Id = MetadataAssignment.MetadataId" +
                where +
                " order by MetadataTarget, TargetId, MetadataAssignment.Id",
                args.ToArray());
        }

        private static string BuildTargetFilter(string tableName, string targetColumn, IReadOnlyCollection<int> packageIds, IReadOnlyCollection<int> assetFileIds, List<object> args)
        {
            if (packageIds == null && assetFileIds == null) return string.Empty;

            List<string> filters = new List<string>();
            AppendTargetFilter(filters, args, tableName, targetColumn, (int)TagAssignment.Target.Package, packageIds);
            AppendTargetFilter(filters, args, tableName, targetColumn, (int)TagAssignment.Target.Asset, assetFileIds);
            return filters.Count == 0 ? null : string.Join(" or ", filters);
        }

        private static void AppendTargetFilter(List<string> filters, List<object> args, string tableName, string targetColumn, int target, IReadOnlyCollection<int> targetIds)
        {
            if (targetIds == null || targetIds.Count == 0) return;

            List<int> ids = targetIds.Distinct().ToList();
            if (ids.Count == 0) return;

            args.Add(target);
            args.AddRange(ids.Cast<object>());
            filters.Add($"({tableName}.{targetColumn}=? and {tableName}.TargetId in ({string.Join(",", ids.Select(_ => "?"))}))");
        }

        public static string BuildAssetStableKey(AssetInfo info)
        {
            string identity = !string.IsNullOrWhiteSpace(info.Guid) ? info.Guid : NormalizePath(info.Path);
            return $"asset:{info.AssetId}:{identity}";
        }

        private static void Append(StringBuilder sb, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            sb.Append(label);
            sb.Append(": ");
            sb.Append(value.Trim());
            sb.Append('\n');
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').Trim();
        }

        private static string BuildPackageContext(AssetInfo info)
        {
            StringBuilder sb = new StringBuilder(128);
            AppendPackageContext(sb, info.DisplayName);
            AppendPackageContext(sb, info.DisplayCategory ?? info.SafeCategory);
            AppendPackageContext(sb, info.DisplayPublisher ?? info.SafePublisher);
            return sb.ToString();
        }

        private static string JoinDistinct(IEnumerable<string> values)
        {
            if (values == null) return string.Empty;
            return string.Join("; ", values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
        }

        private static string JoinMetadata(IEnumerable<MetadataInfo> metadata)
        {
            if (metadata == null) return string.Empty;

            StringBuilder sb = new StringBuilder(256);
            foreach (MetadataInfo info in metadata)
            {
                string value = FormatMetadata(info);
                if (string.IsNullOrWhiteSpace(value)) continue;

                if (sb.Length > 0) sb.Append("; ");
                sb.Append(value);
                if (sb.Length >= MaxMetadataTextLength) break;
            }

            return Truncate(sb.ToString(), MaxMetadataTextLength);
        }

        private static string FormatMetadata(MetadataInfo info)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.Name)) return string.Empty;
            if (info.Name == MetadataDefinition.FIELD_HIDE || info.Name == MetadataDefinition.FIELD_MAX_BACKUPS) return string.Empty;

            string value = GetMetadataValue(info);
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return $"{info.Name.Trim()}: {Truncate(value.Trim(), MaxMetadataValueLength)}";
        }

        private static string GetMetadataValue(MetadataInfo info)
        {
            switch (info.Type)
            {
                case MetadataDefinition.DataType.Boolean:
                    return info.BoolValue ? "yes" : string.Empty;
                case MetadataDefinition.DataType.Number:
                    return info.IntValue != 0 ? info.IntValue.ToString(CultureInfo.InvariantCulture) : string.Empty;
                case MetadataDefinition.DataType.DecimalNumber:
                    return Math.Abs(info.FloatValue) > 0.0001f ? info.FloatValue.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
                case MetadataDefinition.DataType.Date:
                case MetadataDefinition.DataType.DateTime:
                    return info.DateTimeValue != default ? info.DateTimeValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;
                default:
                    return info.StringValue;
            }
        }

        private static void AppendPackageContext(StringBuilder sb, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (sb.Length > 0) sb.Append("; ");
            sb.Append(value.Trim());
        }

        private static string DescribeType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return string.Empty;

            string normalized = type.Trim().TrimStart('.').ToLowerInvariant();
            switch (normalized)
            {
                case "wav":
                case "mp3":
                case "ogg":
                case "aif":
                case "aiff":
                case "flac":
                case "m4a":
                    return normalized + " audio sound";
                case "prefab":
                    return "prefab visual asset";
                case "fbx":
                case "obj":
                case "dae":
                case "blend":
                case "3ds":
                    return normalized + " 3d model";
                case "png":
                case "jpg":
                case "jpeg":
                case "tga":
                case "tif":
                case "tiff":
                case "bmp":
                case "psd":
                case "exr":
                    return normalized + " image texture";
                case "mat":
                    return "material";
                case "anim":
                    return "animation";
                default:
                    return normalized;
            }
        }

        private static string NormalizeTerms(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            StringBuilder sb = new StringBuilder(value.Length);
            char previous = '\0';
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (sb.Length > 0 && char.IsUpper(c) && char.IsLower(previous)) sb.Append(' ');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (sb.Length > 0 && sb[sb.Length - 1] != ' ')
                {
                    sb.Append(' ');
                }

                previous = c;
            }

            return sb.ToString().Trim();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
            return value.Substring(0, maxLength);
        }
    }

#if UNITY_6000_7_OR_NEWER
    // Empty is immutable semantic-input metadata shared by every indexing request.
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal sealed partial class SemanticContentInputs
    {
        public static readonly SemanticContentInputs Empty = new SemanticContentInputs(
            new Dictionary<int, string>(),
            new Dictionary<int, string>(),
            new Dictionary<int, string>(),
            new Dictionary<int, string>());

        private readonly Dictionary<int, string> _packageTags;
        private readonly Dictionary<int, string> _fileTags;
        private readonly Dictionary<int, string> _packageMetadata;
        private readonly Dictionary<int, string> _fileMetadata;

        public SemanticContentInputs(Dictionary<int, string> packageTags, Dictionary<int, string> fileTags, Dictionary<int, string> packageMetadata, Dictionary<int, string> fileMetadata = null)
        {
            _packageTags = packageTags ?? new Dictionary<int, string>();
            _fileTags = fileTags ?? new Dictionary<int, string>();
            _packageMetadata = packageMetadata ?? new Dictionary<int, string>();
            _fileMetadata = fileMetadata ?? new Dictionary<int, string>();
        }

        public string GetPackageTags(int assetId)
        {
            return _packageTags.TryGetValue(assetId, out string value) ? value : string.Empty;
        }

        public string GetFileTags(int assetFileId)
        {
            return _fileTags.TryGetValue(assetFileId, out string value) ? value : string.Empty;
        }

        public string GetFileMetadata(int assetFileId)
        {
            return _fileMetadata.TryGetValue(assetFileId, out string value) ? value : string.Empty;
        }

        public string GetPackageMetadata(int assetId)
        {
            return _packageMetadata.TryGetValue(assetId, out string value) ? value : string.Empty;
        }
    }
}
