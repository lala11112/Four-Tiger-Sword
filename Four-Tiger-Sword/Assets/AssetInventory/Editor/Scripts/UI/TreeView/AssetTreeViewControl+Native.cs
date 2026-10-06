using System;
using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    internal sealed partial class AssetTreeViewControl
    {
        private enum CellKind
        {
            Empty,
            Text,
            Checkmark,
            Name,
            Tags,
            Media,
            Version,
            ManualVersion,
            Link
        }

        private struct CellPresentation
        {
            public CellKind Kind;
            public string Text;
            public string Link;
            public Texture Image;
            public IList<TagInfo> Tags;
            public List<AssetMedia> Media;
            public bool Wrap;
            public bool Bold;
            public bool Centered;
            public bool Muted;
            public bool UpdateAvailable;
            public bool IndexingRequired;
            public Color UpdateColor;
            public string Tooltip;
        }

        private sealed class NativeCell : CommonMultiColumnTreeCell
        {
            public AssetInfo BoundInfo;
            public CellPresentation Presentation;
        }

        internal VisualElement CreateNativeCell(int sourceColumnIndex, int metadataDefinitionId)
        {
            NativeCell cell = new NativeCell();
            cell.Icon.scaleMode = ScaleMode.ScaleToFit;
            cell.Icon.AddToClassList("ai-native-tree-icon");
            cell.Action.clicked += () => HandleNativeCellAction(cell);
            return cell;
        }

        internal void BindNativeCell(
            VisualElement element,
            AssetInfo info,
            int sourceColumnIndex,
            int metadataDefinitionId,
            bool mediaColumnVisible)
        {
            if (!(element is NativeCell cell)) return;

            CellPresentation presentation = GetCellPresentation(info, (Columns)sourceColumnIndex, metadataDefinitionId);
            cell.BoundInfo = info;
            cell.Presentation = presentation;

            cell.Icon.style.display = DisplayStyle.None;
            cell.Icon.tooltip = string.Empty;
            cell.Label.style.display = DisplayStyle.None;
            cell.Label.text = string.Empty;
            cell.Label.tooltip = string.Empty;
            cell.Action.style.display = DisplayStyle.None;
            cell.Accessory.style.display = DisplayStyle.None;
            cell.EnableInClassList("ai-native-tree-name-cell", presentation.Kind == CellKind.Name);
            cell.EnableInClassList("ai-native-tree-version-cell", presentation.Kind == CellKind.Version);
            cell.EnableInClassList("ai-native-tree-media-row", mediaColumnVisible);
            cell.Label.EnableInClassList("ai-native-tree-label-wrap", presentation.Wrap || presentation.Kind == CellKind.Name && mediaColumnVisible);
            cell.Label.EnableInClassList("ai-native-tree-label-bold", presentation.Bold);
            cell.Label.EnableInClassList("ai-native-tree-label-centered", presentation.Centered);
            cell.Label.EnableInClassList("ai-native-tree-label-muted", presentation.Muted);
            cell.Icon.EnableInClassList("ai-native-tree-icon-centered", presentation.Kind == CellKind.Checkmark);

            switch (presentation.Kind)
            {
                case CellKind.Text:
                    BindNativeLabel(cell, presentation.Text, presentation.Tooltip);
                    break;

                case CellKind.Checkmark:
                    BindNativeIcon(cell, GetCheckmarkIcon(), 16f, "Indexed");
                    break;

                case CellKind.Name:
                    float iconSize = mediaColumnVisible ? TOGGLE_WIDTH * 3f - 3f : Mathf.Max(13f, AI.Config.rowHeight - 4f);
                    BindNativeIcon(cell, presentation.Image, iconSize, presentation.Text);
                    BindNativeLabel(cell, presentation.Text);
                    break;

                case CellKind.Tags:
                    if (AI.Config.showColoredPackageTreeTags)
                    {
                        BindNativeTags(cell, presentation.Tags);
                    }
                    else
                    {
                        BindNativeLabel(cell, presentation.Tags == null
                            ? string.Empty
                            : string.Join(", ", presentation.Tags.Select(tag => tag.Name)));
                    }
                    break;

                case CellKind.Media:
                    BindNativeMedia(cell, info, presentation.Media);
                    break;

                case CellKind.Version:
                    BindNativeLabel(cell, presentation.Text);
                    BindNativeVersionStatus(cell, presentation);
                    break;

                case CellKind.ManualVersion:
                    cell.Action.text = "enter manually";
                    cell.Action.tooltip = "Enter a package version manually";
                    cell.Action.style.display = DisplayStyle.Flex;
                    cell.Action.EnableInClassList("ai-link-button", true);
                    break;

                case CellKind.Link:
                    cell.Action.text = presentation.Text ?? string.Empty;
                    cell.Action.tooltip = presentation.Link ?? string.Empty;
                    cell.Action.style.display = DisplayStyle.Flex;
                    cell.Action.EnableInClassList("ai-link-button", true);
                    break;
            }
        }

        internal void UnbindNativeCell(VisualElement element, AssetInfo info, int sourceColumnIndex)
        {
            if (!(element is NativeCell cell)) return;

            if ((Columns)sourceColumnIndex == Columns.Media) info?.DisposeMedia();
            cell.BoundInfo = null;
            cell.Presentation = default;
            cell.Icon.image = null;
            cell.Label.text = string.Empty;
            cell.Label.tooltip = string.Empty;
            cell.ResetContent();
        }

        private static void BindNativeLabel(NativeCell cell, string text, string tooltip = null)
        {
            string value = text ?? string.Empty;
            cell.Label.text = value;
            cell.Label.tooltip = tooltip ?? value;
            cell.Label.style.display = DisplayStyle.Flex;
        }

        private static void BindNativeIcon(NativeCell cell, Texture image, float size, string tooltip)
        {
            cell.Icon.image = image;
            cell.Icon.tooltip = tooltip ?? string.Empty;
            cell.Icon.style.width = size;
            cell.Icon.style.height = size;
            cell.Icon.style.display = image == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static void BindNativeVersionStatus(NativeCell cell, CellPresentation presentation)
        {
            int signature = (presentation.UpdateAvailable ? 1 : 0) | (presentation.IndexingRequired ? 2 : 0);
            signature = signature * 31 + presentation.UpdateColor.GetHashCode();
            if (cell.ContentStateHash != signature)
            {
                cell.Accessory.Clear();
                if (presentation.UpdateAvailable)
                {
                    Image update = CreateNativeStatusIcon(
                        CommonUIStyles.IconContent("preAudioLoopOff", "Update-Available", "|Update Available").image,
                        "Update available");
                    update.tintColor = presentation.UpdateColor;
                    cell.Accessory.Add(update);
                }
                if (presentation.IndexingRequired)
                {
                    Image indexing = CreateNativeStatusIcon(
                        CommonUIStyles.IconContent("d_Refresh", "d_Refresh", "|Indexing Required").image,
                        "Indexing required");
                    indexing.tintColor = Color.gray;
                    cell.Accessory.Add(indexing);
                }
                cell.ContentStateHash = signature;
            }

            cell.Accessory.style.display = cell.Accessory.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static Image CreateNativeStatusIcon(Texture texture, string tooltip)
        {
            Image image = new Image
            {
                image = texture,
                scaleMode = ScaleMode.ScaleToFit,
                tooltip = tooltip
            };
            image.AddToClassList("ai-native-tree-status-icon");
            return image;
        }

        private static void BindNativeTags(NativeCell cell, IList<TagInfo> tags)
        {
            int signature = 17;
            if (tags != null)
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    TagInfo tag = tags[i];
                    signature = signature * 31 + (tag?.Name?.GetHashCode() ?? 0);
                    signature = signature * 31 + (tag?.Color?.GetHashCode() ?? 0);
                }
            }

            if (cell.ContentStateHash != signature)
            {
                cell.Accessory.Clear();
                if (tags != null)
                {
                    for (int i = 0; i < tags.Count; i++)
                    {
                        TagInfo tag = tags[i];
                        if (tag == null) continue;

                        VisualElement pill = new VisualElement {tooltip = tag.Name};
                        pill.AddToClassList("ai-native-tree-tag");
                        Label label = new Label(tag.Name) {tooltip = tag.Name};
                        label.AddToClassList("ai-native-tree-tag__label");
                        ApplyNativeTagColor(label, tag.Color);
                        pill.Add(label);
                        cell.Accessory.Add(pill);
                    }
                }
                cell.ContentStateHash = signature;
            }

            cell.Accessory.style.display = cell.Accessory.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void ApplyNativeTagColor(VisualElement label, string htmlColor)
        {
            if (label == null || string.IsNullOrWhiteSpace(htmlColor)) return;
            if (!ColorUtility.TryParseHtmlString(htmlColor, out Color color)) return;

            label.style.backgroundColor = color;
            label.style.color = CommonUITK.GetReadableTextColor(color);
        }

        private static void BindNativeMedia(NativeCell cell, AssetInfo info, List<AssetMedia> media)
        {
            int signature = info?.TreeId ?? 0;
            signature = signature * 31 + AI.Config.rowHeightMedia.GetHashCode();
            signature = signature * 31 + AI.Config.mediaYFillRatio.GetHashCode();
            signature = signature * 31 + AI.Config.mediaXSpacing.GetHashCode();
            signature = signature * 31 + (AI.Config.mediaSameWidth ? 1 : 0);
            signature = signature * 31 + (AI.Config.mediaMaintainAspect ? 1 : 0);
            if (media != null)
            {
                for (int i = 0; i < media.Count; i++)
                {
                    AssetMedia item = media[i];
                    Texture texture = item?.ThumbnailTexture ?? item?.Texture;
                    signature = signature * 31 + (texture == null ? 0 : texture.GetStableId());
                }
            }

            if (cell.ContentStateHash != signature)
            {
                cell.Accessory.Clear();
                if (media != null)
                {
                    float imageHeight = AI.Config.rowHeightMedia * (AI.Config.mediaYFillRatio / 100f);
                    int rendered = 0;
                    for (int i = 0; i < media.Count && rendered < 12; i++)
                    {
                        AssetMedia item = media[i];
                        if (item == null || item.Type != "screenshot") continue;

                        Texture texture = item.ThumbnailTexture ?? item.Texture;
                        if (texture == null) continue;

                        float imageWidth = AI.Config.mediaSameWidth || texture.height <= 0
                            ? imageHeight
                            : imageHeight / texture.height * texture.width;
                        Image image = new Image
                        {
                            image = texture,
                            scaleMode = AI.Config.mediaMaintainAspect ? ScaleMode.ScaleToFit : ScaleMode.StretchToFill
                        };
                        image.AddToClassList("ai-native-tree-media-image");
                        image.style.width = imageWidth;
                        image.style.height = imageHeight;
                        image.style.marginRight = AI.Config.mediaXSpacing;
                        cell.Accessory.Add(image);
                        rendered++;
                    }
                }
                cell.ContentStateHash = signature;
            }

            if ((media == null || media.Count == 0) && info != null && !info.IsMediaLoading()) MediaManager.Load(info);
            cell.Accessory.style.display = cell.Accessory.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void HandleNativeCellAction(NativeCell cell)
        {
            AssetInfo info = cell.BoundInfo;
            if (info == null) return;

            if (cell.Presentation.Kind == CellKind.Link)
            {
                AI.OpenURL(cell.Presentation.Link);
                return;
            }
            if (cell.Presentation.Kind != CellKind.ManualVersion) return;

            EditorWindow owner = _owner != null ? _owner : EditorWindow.focusedWindow;
            if (owner == null) return;

            NameWindow.ShowAsDropDown(
                CommonUITK.ToScreenDropdownAnchor(owner, cell.Action),
                string.Empty,
                newVersion => AI.SetVersion(info, newVersion));
        }

        private CellPresentation GetCellPresentation(AssetInfo info, Columns column, int metadataDefinitionId)
        {
            if (info == null || info.AssetId <= 0 && column != Columns.Name) return default;

            switch (column)
            {
                case Columns.Name:
                    return new CellPresentation
                    {
                        Kind = CellKind.Name,
                        Text = info.TreeName,
                        Image = info.AssetId > 0
                            ? info.PreviewTexture != null ? info.PreviewTexture : info.GetFallbackIcon()
                            : CommonUIStyles.IconContent("Folder Icon", "d_Folder Icon").image
                    };
                case Columns.Tags:
                    return info.PackageTags != null && info.PackageTags.Count > 0
                        ? new CellPresentation {Kind = CellKind.Tags, Tags = info.PackageTags}
                        : default;
                case Columns.Version:
                    return GetVersionPresentation(info);
                case Columns.Media:
                    return new CellPresentation {Kind = CellKind.Media, Media = info.Media};
                case Columns.AICaptions:
                    return GetCheckmarkPresentation(info.UseAI);
                case Columns.Backup:
                    return GetCheckmarkPresentation(info.Backup);
                case Columns.NoIndex:
                    return GetCheckmarkPresentation(info.NoIndex);
                case Columns.SemanticIndex:
                    return GetCheckmarkPresentation(info.IsSemanticIndexEnabled);
                case Columns.CodeIndex:
                    return GetCheckmarkPresentation(info.IsCodeIndexEnabled);
                case Columns.BIRP:
                    return GetCheckmarkPresentation(info.BIRPCompatible);
                case Columns.Deprecated:
                    return GetCheckmarkPresentation(info.IsDeprecated);
                case Columns.Downloaded:
                    return GetCheckmarkPresentation(info.IsDownloaded);
                case Columns.Exclude:
                    return GetCheckmarkPresentation(info.Exclude);
                case Columns.Extract:
                    return GetCheckmarkPresentation(info.KeepExtracted);
                case Columns.HDRP:
                    return GetCheckmarkPresentation(info.HDRPCompatible);
                case Columns.Indexed:
                    return GetPackageIndexColumnPresentation(info);
                case Columns.Materialized:
                    return GetCheckmarkPresentation(info.IsMaterialized);
                case Columns.Outdated:
                    return GetCheckmarkPresentation(info.CurrentSubState == Asset.SubState.Outdated);
                case Columns.Update:
                    return GetCheckmarkPresentation(info.IsUpdateAvailable());
                case Columns.URP:
                    return GetCheckmarkPresentation(info.URPCompatible);
                case Columns.BackupCount:
                    int? backupCount = _backupCountProvider?.Invoke(info);
                    return GetTextPresentation(backupCount.HasValue ? backupCount.Value.ToString() : "-");
                case Columns.Rules:
                    return GetTextPresentation(GetPackageRuleSummary(info));
                case Columns.Category:
                    return GetTextPresentation(info.GetDisplayCategory());
                case Columns.FileCount:
                    return GetTextPresentation(info.FileCount > 0 ? info.FileCount.ToString() : string.Empty);
                case Columns.ForeignId:
                    return GetTextPresentation(info.ForeignId > 0 ? info.ForeignId.ToString() : string.Empty);
                case Columns.InternalState:
                    return GetTextPresentation(StringUtils.CamelCaseToWords(info.CurrentState.ToString()));
                case Columns.License:
                    return GetTextPresentation(string.IsNullOrWhiteSpace(info.License) ? "-default-" : info.License);
                case Columns.Location:
                    return GetTextPresentation(info.GetLocation(true));
                case Columns.ModifiedDate:
                    return GetDatePresentation(info.ModifiedDate);
                case Columns.ModifiedDateRelative:
                    return GetRelativeDatePresentation(info.ModifiedDate);
                case Columns.Popularity:
                    return GetTextPresentation($"{info.GetRoot().Hotness:N1}");
                case Columns.Price:
                    return GetTextPresentation(info.GetPriceText());
                case Columns.Publisher:
                    return GetTextPresentation(info.GetDisplayPublisher());
                case Columns.PurchaseDate:
                    return GetDatePresentation(info.GetPurchaseDate());
                case Columns.PurchaseDateRelative:
                    return GetRelativeDatePresentation(info.GetPurchaseDate());
                case Columns.Rating:
                    return GetTextPresentation($"{info.AssetRating:N1}");
                case Columns.RatingCount:
                    return GetTextPresentation(info.GetRoot().RatingCount.ToString());
                case Columns.ReleaseDate:
                    return GetDatePresentation(info.FirstRelease);
                case Columns.ReleaseDateRelative:
                    return GetRelativeDatePresentation(info.FirstRelease);
                case Columns.Size:
                    return GetTextPresentation(info.PackageSize > 0 ? EditorUtility.FormatBytes(info.PackageSize) : string.Empty);
                case Columns.Source:
                    return GetTextPresentation(StringUtils.CamelCaseToWords(info.AssetSource.ToString()));
                case Columns.State:
                    return GetTextPresentation(info.OfficialState.ToString());
                case Columns.UnityVersions:
                    return GetTextPresentation(info.SupportedUnityVersions);
                case Columns.UpdateDate:
                    return GetDatePresentation(info.LastRelease);
                case Columns.UpdateDateRelative:
                    return GetRelativeDatePresentation(info.LastRelease);
                default:
                    return GetMetadataPresentation(info, column, metadataDefinitionId);
            }
        }

        private CellPresentation GetVersionPresentation(AssetInfo info)
        {
            if ((info.AssetSource == Asset.Source.Archive || info.AssetSource == Asset.Source.CustomPackage) &&
                string.IsNullOrWhiteSpace(info.GetVersion()))
            {
                return AI.ShowAdvanced()
                    ? new CellPresentation {Kind = CellKind.ManualVersion}
                    : default;
            }

            List<AssetInfo> assets = _treeModel?.GetData() as List<AssetInfo>;
            bool updateAvailable = assets != null && info.IsUpdateAvailable(assets);
            bool indexingRequired = (info.CurrentState == Asset.State.New || info.CurrentState == Asset.State.InProcess)
                && info.IsDownloaded
                && (info.AssetSource != Asset.Source.RegistryPackage || AI.Actions.IndexPackageCache);
            return new CellPresentation
            {
                Kind = CellKind.Version,
                Text = info.GetVersion(),
                Bold = info.Origin != null || info.AssetSource == Asset.Source.RegistryPackage,
                UpdateAvailable = updateAvailable,
                IndexingRequired = indexingRequired,
                UpdateColor = info.AssetSource == Asset.Source.CustomPackage ? Color.gray : Color.white
            };
        }

        private static CellPresentation GetPackageIndexColumnPresentation(AssetInfo info)
        {
            switch (GetPackageIndexColumnState(info))
            {
                case PackageIndexColumnState.NoIndex:
                    return new CellPresentation
                    {
                        Kind = CellKind.Text,
                        Text = NoIndexColumnGlyph,
                        Tooltip = NoIndexColumnTooltip,
                        Centered = true,
                        Muted = true
                    };
                case PackageIndexColumnState.Indexed:
                    return GetCheckmarkPresentation(true);
                default:
                    return default;
            }
        }

        private CellPresentation GetMetadataPresentation(AssetInfo info, Columns column, int metadataDefinitionId)
        {
            int metaId = metadataDefinitionId;
            if (metaId < 0) return default;
            MetadataInfo metadata = info.PackageMetadata?.FirstOrDefault(value => value.DefinitionId == metaId);
            if (metadata == null) return default;

            switch (metadata.Type)
            {
                case MetadataDefinition.DataType.Boolean:
                    return GetCheckmarkPresentation(metadata.BoolValue);
                case MetadataDefinition.DataType.Text:
                case MetadataDefinition.DataType.SingleSelect:
                    return GetTextPresentation(metadata.StringValue);
                case MetadataDefinition.DataType.BigText:
                    CellPresentation bigText = GetTextPresentation(metadata.StringValue);
                    bigText.Wrap = true;
                    return bigText;
                case MetadataDefinition.DataType.Number:
                    return GetTextPresentation(metadata.IntValue.ToString());
                case MetadataDefinition.DataType.DecimalNumber:
                    return GetTextPresentation($"{metadata.FloatValue:N1}");
                case MetadataDefinition.DataType.Url:
                    return new CellPresentation
                    {
                        Kind = CellKind.Link,
                        Text = metadata.StringValue?.Replace("https://", string.Empty).Replace("www.", string.Empty),
                        Link = metadata.StringValue
                    };
                case MetadataDefinition.DataType.Date:
                    return GetTextPresentation(metadata.DateTimeValue.ToShortDateString());
                default:
                    return default;
            }
        }

        private static CellPresentation GetCheckmarkPresentation(bool value)
        {
            return value ? new CellPresentation {Kind = CellKind.Checkmark} : default;
        }

        private static CellPresentation GetTextPresentation(string value)
        {
            return string.IsNullOrEmpty(value)
                ? default
                : new CellPresentation {Kind = CellKind.Text, Text = value};
        }

        private static CellPresentation GetDatePresentation(DateTime? value)
        {
            return value.HasValue && value.Value.Year > 1
                ? GetTextPresentation(value.Value.ToShortDateString())
                : default;
        }

        private static CellPresentation GetDatePresentation(DateTime value)
        {
            return value.Year > 1 ? GetTextPresentation(value.ToShortDateString()) : default;
        }

        private static CellPresentation GetRelativeDatePresentation(DateTime? value)
        {
            return value.HasValue && value.Value.Year > 1
                ? GetTextPresentation(StringUtils.GetRelativeTimeDifference(value.Value))
                : default;
        }

        private static CellPresentation GetRelativeDatePresentation(DateTime value)
        {
            return value.Year > 1
                ? GetTextPresentation(StringUtils.GetRelativeTimeDifference(value))
                : default;
        }

        private static Texture GetCheckmarkIcon()
        {
            return CommonUIStyles.IconContent("Valid", "d_Valid", "|Indexed").image;
        }

    }
}
