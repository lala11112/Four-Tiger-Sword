using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;
using Image = UnityEngine.UIElements.Image;
using Label = UnityEngine.UIElements.Label;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using ScrollView = UnityEngine.UIElements.ScrollView;
using Toggle = UnityEngine.UIElements.Toggle;
using VisualElement = UnityEngine.UIElements.VisualElement;

namespace AssetInventory
{
    public partial class IndexUI
    {
        private const string PackagesDetailRootClass = "ai-package-detail-root";
        private const string PackagesDetailSectionClass = "ai-package-detail-section";
        private const string PackagesDetailRowClass = "ai-package-detail-row";
        private const string PackagesDetailLabelClass = "ai-package-detail-label";
        private const string PackagesDetailValueClass = "ai-package-detail-value";
        private const string PackagesDetailControlClass = "ai-package-detail-control";
        private const string PackagesDetailLinkClass = "ai-package-detail-link";
        private const string PackagesDetailRatingClass = "ai-package-detail-rating";
        private const string PackagesDetailRatingStarClass = "ai-package-detail-rating-star";
        private const string PackagesDetailRatingCountClass = "ai-package-detail-rating-count";
        private const string PackagesDetailActionsClass = "ai-package-detail-actions";
        private const string PackagesDetailActionGridClass = "ai-package-detail-action-grid";
        private const string PackagesDetailActionItemClass = "ai-package-detail-action-item";
        private const string PackagesDetailWideActionClass = "ai-package-detail-action-wide";
        private const string PackagesDetailPrimaryActionClass = "ai-package-detail-action-primary";
        private const string PackagesDetailDestructiveActionClass = "ai-package-detail-action-destructive";
        private const string PackagesDetailActionWithIconClass = "ai-package-detail-action-with-icon";
        private const string PackagesDetailActionTextClass = "ai-package-detail-action-text";
        private const string PackagesDetailActionIconClass = "ai-package-detail-action-icon";
        private const string PackagesDetailCompactActionClass = "ai-package-detail-compact-action";
        private const string PackagesDetailStatusControlClass = "ai-package-detail-status-control";

        private sealed class NativeBulkDownloadStatusElements
        {
            public VisualElement DownloadingRow;
            public Label DownloadingLabel;
            public Label DownloadingValue;
            public VisualElement RemainingRow;
            public Label RemainingLabel;
            public Label RemainingValue;
            public VisualElement PausedRow;
            public Label PausedLabel;
            public Label PausedValue;
        }
        private const string PackagesDetailStatusActionClass = "ai-package-detail-status-action";
        private const string PackagesDetailToggleClass = "ai-package-detail-toggle";
        private const string PackagesDetailPreviewContainerClass = "ai-package-detail-preview-container";
        private const string PackagesDetailPreviewClass = "ai-package-detail-preview";
        private const string PackagesDetailRichTabsClass = "ai-package-detail-rich-tabs";
        private const string PackagesDetailRichBodyClass = "ai-package-detail-rich-body";
        private const string PackagesDetailTextClass = "ai-package-detail-text";
        private const string PackagesDetailInlineLinkHoverClass = "ai-package-detail-inline-link-hover";
        private const string PackagesDetailMediaClass = "ai-package-detail-media";
        private const string PackagesDetailMediaMainClass = "ai-package-detail-media-main";
        private const string PackagesDetailMediaMainImageClass = "ai-package-detail-media-main-image";
        private const string PackagesDetailMediaStatusClass = "ai-package-detail-media-status";
        private const string PackagesDetailMediaNavigationClass = "ai-package-detail-media-navigation";
        private const string PackagesDetailMediaPreviousClass = "ai-package-detail-media-previous";
        private const string PackagesDetailMediaNextClass = "ai-package-detail-media-next";
        private const string PackagesDetailMediaCounterClass = "ai-package-detail-media-counter";
        private const string PackagesDetailMediaThumbsClass = "ai-package-detail-media-thumbs";
        private const string PackagesDetailMediaThumbClass = "ai-package-detail-media-thumb";
        private const string PackagesDetailMediaThumbSelectedClass = "ai-package-detail-media-thumb-selected";
        private const string PackagesDetailDependencyColumnsClass = "ai-package-detail-dependency-columns";
        private const string PackagesDetailDependencyColumnClass = "ai-package-detail-dependency-column";
        private const string PackagesDetailTagsClass = "ai-package-detail-tags";
        private const string PackagesDetailTagClass = "ai-package-detail-tag";
        private const string PackagesDetailMetadataControlClass = "ai-package-detail-metadata-control";
        private const string PackagesDetailMetadataRemoveClass = "ai-package-detail-metadata-remove";
        private const string PackagesDetailMetadataActionsClass = "ai-package-detail-metadata-actions";
        private const string PackagesDetailBulkChoiceClass = "ai-package-detail-bulk-choice";
        private const string PackagesDetailBulkCommandsClass = "ai-package-detail-bulk-commands";
        private const string PackagesDetailVersionFieldClass = "ai-package-detail-version-field";
        private const string PackagesDetailUpdateStrategyClass = "ai-package-detail-update-strategy";

        private ScrollView CreateNativePackageDetailsInspector()
        {
            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(PackagesInspectorScrollClass);

            VisualElement content = new VisualElement();
            content.AddToClassList(PackagesInspectorContentClass);
            content.AddToClassList(PackagesDetailRootClass);
            scroll.Add(content);

            if (_selectedTreeAsset != null)
            {
                content.Add(CreateNativeSinglePackageDetails(_selectedTreeAsset));
            }
            else if (_selectedTreeAssets != null && _selectedTreeAssets.Count > 0)
            {
                content.Add(CreateNativeBulkPackageDetails(
                    _selectedTreeAssets,
                    _assetTreeSubPackageCount,
                    _assetBulkTags,
                    _assetTreeSelectionSize,
                    _assetTreeSelectionTotalCosts,
                    _assetTreeSelectionStoreCosts));
            }
            else
            {
                content.Add(AssetInventoryUITK.CreateHelpBox("Select one or more packages to see details."));
            }

            if (!ShowAdvanced() && AI.Config.showHints)
            {
                Label hint = AssetInventoryUITK.CreateMutedLabel("Use the eye icon in the upper-right toolbar to show advanced options.");
                hint.AddToClassList(InspectorWrappedHintClass);
                hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                content.Add(hint);
            }
            AssetInventoryUITK.HideEmptySections(content);
            return scroll;
        }

        private int GetNativePackageDetailsStateHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (AI.Config.expandPackageDetails ? 1 : 0);
                hash = hash * 31 + (AI.Config.alwaysShowPackageDetails ? 1 : 0);
                hash = hash * 31 + (AI.Config.projectDetailTabs ? 1 : 0);
                hash = hash * 31 + _packageDetailsTab;
                hash = hash * 31 + _selectedMedia;
                hash = hash * 31 + AI.Config.mediaHeight;
                hash = hash * 31 + AI.Config.mediaThumbnailWidth;
                hash = hash * 31 + AI.Config.mediaThumbnailHeight;
                hash = hash * 31 + (AI.Config.packageBackupFeatureEnabled ? 1 : 0);
                hash = hash * 31 + (AI.Config.assetManagerFeatureEnabled ? 1 : 0);
                hash = hash * 31 + (AI.Config.aiCaptionsFeatureEnabled ? 1 : 0);
                hash = hash * 31 + (AI.Config.semanticSearchFeatureEnabled ? 1 : 0);
                hash = hash * 31 + (AI.Config.codeSearchFeatureEnabled ? 1 : 0);
                hash = hash * 31 + (_metadataEditMode ? 1 : 0);
                hash = hash * 31 + (AI.Actions.ActionsInProgress ? 1 : 0);
                hash = hash * 31 + Tagging.TagHash;

                AssetInfo info = _selectedTreeAsset;
                if (info != null)
                {
                    hash = hash * 31 + info.AssetId;
                    hash = hash * 31 + (info.Backup ? 1 : 0);
                    hash = hash * 31 + (info.UseAI ? 1 : 0);
                    hash = hash * 31 + (info.IsSemanticIndexEnabled ? 1 : 0);
                    hash = hash * 31 + (info.IsCodeIndexEnabled ? 1 : 0);
                    hash = hash * 31 + (info.NoIndex ? 1 : 0);
                    hash = hash * 31 + (info.ParentInfo?.NoIndex == true ? 1 : 0);
                    hash = hash * 31 + (info.KeepExtracted ? 1 : 0);
                    hash = hash * 31 + (info.Exclude ? 1 : 0);
                    hash = hash * 31 + (info.IsDownloaded ? 1 : 0);
                    hash = hash * 31 + (info.IsIndexed ? 1 : 0);
                    hash = hash * 31 + info.FileCount;
                    hash = hash * 31 + (info.PackageTags?.Count ?? 0);
                    hash = hash * 31 + (info.PackageMetadata?.Count ?? 0);
                    hash = hash * 31 + (info.PreviewTexture != null ? info.PreviewTexture.GetStableId() : 0);
                    if (info.Media != null)
                    {
                        hash = hash * 31 + info.Media.Count;
                        for (int i = 0; i < info.Media.Count; i++)
                        {
                            AssetMedia media = info.Media[i];
                            hash = hash * 31 + (media.ThumbnailTexture != null ? media.ThumbnailTexture.GetStableId() : 0);
                            hash = hash * 31 + (media.Texture != null ? media.Texture.GetStableId() : 0);
                            hash = hash * 31 + (media.IsDownloading ? 1 : 0);
                            hash = hash * 31 + (media.DownloadFailed ? 1 : 0);
                        }
                    }
                    AssetDownloadState state = info.PackageDownloader?.GetState();
                    if (state != null)
                    {
                        hash = hash * 31 + (int)state.state;
                        hash = hash * 31 + Mathf.RoundToInt(state.progress * 100f);
                    }
                }
                else
                {
                    hash = hash * 31 + (_selectedTreeAssets?.Count ?? 0);
                    hash = hash * 31 + _assetBulkTags.Count;
                    hash = hash * 31 + AI.GetObserver().PrioInitializationDone.GetHashCode();
                    BulkPackageDownloadSummary summary = CalculateBulkPackageDownloadSummary(_selectedTreeAssets, _assets);
                    hash = hash * 31 + summary.NotDownloaded;
                    hash = hash * 31 + summary.UpdateAvailable;
                    hash = hash * 31 + summary.PackageUpdateAvailable;
                    hash = hash * 31 + summary.UpdateAvailableButCustom;
                }
                return hash;
            }
        }

        private VisualElement CreateNativeSinglePackageDetails(AssetInfo info)
        {
            VisualElement root = new VisualElement();
            if (info.AssetId == 0)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox(
                    "This asset has no package association anymore. Use the maintenance wizard to clean up such orphaned files.",
                    MessageType.Error));
                return root;
            }

            bool showExpanded = (AI.Config.expandPackageDetails || AI.Config.alwaysShowPackageDetails) && AI.Config.tab == 1;
            List<string> richSections = GetNativePackageRichSections(info, showExpanded);
            List<string> order = AI.Config.GetSection("package").sections;
            for (int i = 0; i < order.Count; i++)
            {
                string sectionKey = order[i];
                VisualElement section = CreateNativePackageOrderedSection(info, sectionKey, showExpanded, richSections);
                if (section == null) continue;

                root.Add(AssetInventoryUITK.CreateOrderedSection(
                    "package",
                    sectionKey,
                    section,
                    ScheduleNativePackageDetailsRebuild));
            }

            if (!showExpanded)
            {
                AssetInfo packageRoot = info.GetRoot();
                if (packageRoot.PreviewTexture != null)
                {
                    root.Add(CreateNativePackageKeyedBlock("package.icon", () => CreateNativePackagePreview(packageRoot)));
                }
                else if (info.AssetSource == Asset.Source.RegistryPackage && !string.IsNullOrWhiteSpace(info.Description))
                {
                    root.Add(CreateNativePackageKeyedBlock("package.description", () => CreateNativePackageText(info.Description)));
                }
            }
            return root;
        }

        private VisualElement CreateNativePackageOrderedSection(AssetInfo info, string sectionKey, bool showExpanded, List<string> richSections)
        {
            switch (sectionKey.ToLowerInvariant())
            {
                case "packagedata":
                    return CreateNativePackageData(info);
                case "tabbeddetails":
                    return showExpanded && AI.Config.projectDetailTabs && richSections.Count > 0
                        ? CreateNativePackageTabbedDetails(info, richSections)
                        : null;
                case "media":
                    return showExpanded && !AI.Config.projectDetailTabs && richSections.Contains("Media")
                        ? CreateNativePackageKeyedBlock("package.media", () => CreateNativePackageMediaSection(info))
                        : null;
                case "description":
                    return showExpanded && !AI.Config.projectDetailTabs && richSections.Contains("About")
                        ? CreateNativePackageKeyedBlock("package.description", () => CreateNativePackageTextSection("Description", info.Description))
                        : null;
                case "releasenotes":
                    return showExpanded && !AI.Config.projectDetailTabs && richSections.Contains("Release Notes")
                        ? CreateNativePackageKeyedBlock("package.releasenotes", () => CreateNativePackageTextSection("Release Notes", info.ReleaseNotes))
                        : null;
                case "dependencies":
                    return showExpanded && !AI.Config.projectDetailTabs && richSections.Contains("Dependencies")
                        ? CreateNativePackageKeyedBlock("package.dependencies", () => CreateNativePackageDependenciesSection(info))
                        : null;
                default:
                    return null;
            }
        }

        private List<string> GetNativePackageRichSections(AssetInfo info, bool showExpanded)
        {
            List<string> sections = new List<string>();
            if (!showExpanded) return sections;

            if ((info.Media != null && info.Media.Count > 0) || info.GetRoot().ForeignId > 0) sections.Add("Media");
            if (!string.IsNullOrWhiteSpace(info.Description)) sections.Add("About");
            if (!string.IsNullOrWhiteSpace(info.ReleaseNotes)) sections.Add("Release Notes");
            if (info.AssetSource == Asset.Source.RegistryPackage || info.GetPackageDependencies() != null || info.GetPackageUsageDependencies(_assets) != null)
            {
                sections.Add("Dependencies");
            }
            return sections;
        }

        private VisualElement CreateNativePackageData(AssetInfo info)
        {
            VisualElement root = new VisualElement();
            root.Add(CreateNativePackageOverview(info));

            VisualElement indexing = CreateNativePackageIndexingSection(info);
            if (indexing.childCount > 1) root.Add(indexing);

            VisualElement metadata = CreateNativePackageMetadataSection(info);
            if (metadata != null) root.Add(metadata);

            AddNativePackageHints(root, info);
            root.Add(CreateNativePackageActionsSection(info));

            VisualElement tags = CreateNativePackageTagsSection(new List<AssetInfo> {info}, info.PackageTags, null);
            if (tags != null) root.Add(CreateNativePackageKeyedBlock("package.actions.tag", () => tags));
            return root;
        }

        private VisualElement CreateNativePackageOverview(AssetInfo info)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Package");
            section.AddToClassList(PackagesDetailSectionClass);
            AssetInfo root = info.GetRoot();

            if (AI.Config.expandPackageDetails && AI.Config.tab == 1 && info.PreviewTexture != null)
            {
                section.Add(CreateNativePackageKeyedBlock("package.topicon", () => CreateNativePackagePreview(info)));
            }

            if (info.AssetSource == Asset.Source.AssetManager)
            {
                AddNativePackageLinkRow(section, "package.organization", "Organization", info.OriginalLocation, info.GetAMOrganizationUrl());
                AddNativePackageLinkRow(section, "package.project", "Project", info.ToAsset().GetRootAsset().DisplayName, info.GetAMProjectUrl());
                if (info.ParentId > 0) AddNativePackageLinkRow(section, "package.collection", "Collection", info.GetDisplayName(), info.GetAMCollectionUrl());
            }
            else
            {
                AddNativePackageDetailRow(section, null, "Name", info.GetDisplayName(), info.Location);
            }

            AddNativePackageVersionRows(section, info);
            if (!string.IsNullOrWhiteSpace(info.License))
            {
                if (!string.IsNullOrWhiteSpace(info.LicenseLocation))
                {
                    AddNativePackageLinkRow(section, "package.license", "License", info.License, info.LicenseLocation);
                }
                else
                {
                    AddNativePackageDetailRow(section, "package.license", "License", info.License);
                }
            }
            if (!string.IsNullOrWhiteSpace(info.GetDisplayPublisher()))
            {
                if (info.PublisherId > 0)
                {
                    AddNativePackageLinkRow(section, "package.publisher", "Publisher", info.GetDisplayPublisher(), info.GetPublisherLink(), true);
                }
                else
                {
                    AddNativePackageDetailRow(section, "package.publisher", "Publisher", info.GetDisplayPublisher());
                }
            }
            AddNativePackageDetailRow(section, "package.category", "Category", info.GetDisplayCategory());
            if (info.PackageSize > 0) AddNativePackageDetailRow(section, "package.size", "Size", EditorUtility.FormatBytes(info.PackageSize));
            if (!string.IsNullOrWhiteSpace(info.SupportedUnityVersions))
            {
                string unityTooltip = info.IsCurrentUnitySupported() ? null : "Package is potentially incompatible with the current Unity version.";
                AddNativePackageDetailRow(section, "package.unityversions", "Unity", info.SupportedUnityVersions, unityTooltip);
            }
            string srps = (info.BIRPCompatible ? "BIRP " : string.Empty) + (info.URPCompatible ? "URP " : string.Empty) + (info.HDRPCompatible ? "HDRP" : string.Empty);
            AddNativePackageDetailRow(section, "package.srps", "SRPs", srps.Trim());
            if (info.FirstRelease.Year > 1) AddNativePackageDetailRow(section, "package.releasedate", "Released", info.FirstRelease.ToString("ddd, MMM d yyyy"));
            if (info.GetPurchaseDate().Year > 1) AddNativePackageDetailRow(section, "package.purchasedate", "Purchased", info.GetPurchaseDate().ToString("ddd, MMM d yyyy"));
            if (info.LastRelease.Year > 1)
            {
                string release = info.LastRelease.ToString("ddd, MMM d yyyy") + (!string.IsNullOrEmpty(info.LatestVersion) ? $" ({info.LatestVersion})" : string.Empty);
                AddNativePackageDetailRow(section, "package.lastupdate", "Last Update", release, info.LastUpdate.Year > 1 ? info.LastUpdate.ToString("ddd, MMM d yyyy") : null);
            }
            else
            {
                AddNativePackageDetailRow(section, "package.latestversion", "Latest Version", info.LatestVersion);
            }
            if (info.AssetSource != Asset.Source.CurrentProject)
            {
                AddNativePackageDetailRow(section, "package.price", "Price", info.GetPrice() > 0 ? info.GetPriceText() : "Free");
            }
            if (info.AssetRating > 0)
            {
                AddNativePackageRatingRow(section, info);
            }
            if ((ShowAdvanced() || AI.Config.tab == 1) && info.AssetSource != Asset.Source.CurrentProject)
            {
                bool alwaysShow = info.AssetSource == Asset.Source.Directory || info.AssetSource == Asset.Source.AssetManager || info.AssetSource == Asset.Source.Archive;
                AddNativePackageDetailRow(section, "package.indexedfiles", "Indexed Files", $"{info.FileCount:N0}", null, alwaysShow);
            }
            if (info.HasChildInfos)
            {
                string childLabel = info.AssetSource == Asset.Source.AssetManager ? "Collections" : "Sub-Packages";
                string childValue = $"{info.ChildInfoCount:N0}" + (info.CurrentState == Asset.State.SubInProcess ? " (reindexing pending)" : string.Empty);
                AddNativePackageDetailRow(section, "package.childcount", childLabel, childValue);
            }

            string source = FormatNativePackageSource(info);
            string sourceTooltip = $"IDs: Asset ({info.AssetId}), Foreign ({info.ForeignId}), Upload ({info.UploadId})\n\nLocation: {info.GetLocation(false)}\n\nResolved Location: {info.GetLocation(true)}\n\nCurrent State: {info.CurrentState}";
            if (root.AssetSource == Asset.Source.Synty)
            {
                AddNativePackageDetailRow(section, "package.source", "Source", source, sourceTooltip);
                if (root.ForeignId > 0)
                {
                    AddNativePackageLinkRow(section, "package.assetstorelink", "Asset Link", "Unity Asset Store", root.GetAssetStoreLink(), true);
                }
            }
            else if (root.ForeignId > 0 && (root.AssetSource == Asset.Source.AssetStorePackage || root.AssetSource == Asset.Source.RegistryPackage))
            {
                AddNativePackageLinkRow(section, "package.source", "Source", source, root.GetItemLink(), true, sourceTooltip);
            }
            else
            {
                AddNativePackageDetailRow(section, "package.source", "Source", source, sourceTooltip);
            }
            if (info.AssetSource != Asset.Source.Synty && info.AssetSource != Asset.Source.AssetStorePackage && info.AssetSource != Asset.Source.RegistryPackage && info.ForeignId > 0)
            {
                AddNativePackageLinkRow(section, "package.sourcelink", "Asset Link", "Asset Store", info.GetAssetStoreLink(), true);
            }
            return section;
        }

        private void AddNativePackageVersionRows(VisualElement section, AssetInfo info)
        {
            if (info.AssetSource == Asset.Source.RegistryPackage)
            {
                AddNativePackageDetailRow(section, "package.id", "Id", info.SafeName, info.SafeName);
                if (info.PackageSource == PackageSource.Local)
                {
                    string version = info.GetVersion(true);
                    AddNativePackageDetailRow(section, "package.version", "Version", string.IsNullOrWhiteSpace(version) ? "-none-" : version);
                    return;
                }

                VisualElement versionField = null;
                string versionText = AssetStore.IsInstalled(info) ? info.InstalledPackageVersion() : "Not installed, select version";
                versionField = CreateNativePackageDropdownField(versionText, "Select the package version to install.", () =>
                {
                    VersionSelectionUI.ShowDropdown(this, versionField, info, version => InstallPackage(info, version));
                });
                VisualElement versionControls = new VisualElement();
                versionControls.AddToClassList(PackagesDetailActionsClass);
                versionControls.Add(versionField);
                if (AssetStore.IsInstalled(info))
                {
                    string changelog = info.GetChangeLogURL(info.InstalledPackageVersion());
                    if (!string.IsNullOrWhiteSpace(changelog))
                    {
                        Button changelogButton = AssetInventoryUITK.CreateIconButton("Open changelog", "d_UnityEditor.ConsoleWindow", () => AI.OpenURL(changelog));
                        changelogButton.AddToClassList(PackagesDetailCompactActionClass);
                        versionControls.Add(changelogButton);
                    }
                }
                AddNativePackageControlRow(section, "package.version", "Version", versionControls);

                EnumField strategy = new EnumField(info.UpdateStrategy);
                strategy.AddToClassList(PackagesDetailUpdateStrategyClass);
                strategy.RegisterValueChangedCallback(evt =>
                {
                    if (!(evt.newValue is Asset.Strategy value)) return;
                    info.UpdateStrategy = value;
                    AI.SetAssetUpdateStrategy(info, value);
                    _requireAssetTreeRebuild = true;
                    ScheduleNativePackageDetailsRebuild();
                });
                AddNativePackageControlRow(section, "package.updatestrategy", "Updates", strategy);
                return;
            }

            int backupKey = AssetBackup.GetBackupKey(info);
            if ((info.AssetSource != Asset.Source.AssetStorePackage && info.AssetSource != Asset.Source.CustomPackage && info.AssetSource != Asset.Source.Synty) || backupKey == 0)
            {
                AddNativePackageDetailRow(section, "package.version", "Version", info.GetVersion(true));
                return;
            }

            if (_cachedBackupState != null && _cachedBackupState.TryGetValue(backupKey, out List<BackupInfo> versions) && versions != null && versions.Count > 0)
            {
                string currentVersion = info.GetVersion(true);
                string displayVersion = !string.IsNullOrWhiteSpace(info.ForcedUnityPackageVersion) ? info.ForcedUnityPackageVersion : currentVersion;
                if (versions.Count > 1)
                {
                    VisualElement versionField = null;
                    versionField = CreateNativePackageDropdownField(string.IsNullOrWhiteSpace(displayVersion) ? "Select version" : displayVersion, "Select a backup version.", () =>
                    {
                        UnityPackageVersionSelectionUI.ShowDropdown(
                            this,
                            versionField,
                            info,
                            _cachedBackupState,
                            version =>
                            {
                                info.ForcedUnityPackageVersion = version;
                                _requireAssetTreeRebuild = true;
                                ScheduleNativePackageDetailsRebuild();
                            });
                    });
                    AddNativePackageControlRow(section, "package.version", "Version", versionField);
                }
                else
                {
                    AddNativePackageDetailRow(section, "package.version", "Version", displayVersion ?? "-none-");
                }
            }
            else
            {
                AddNativePackageDetailRow(section, "package.version", "Version", info.GetVersion(true));
            }
        }

        private VisualElement CreateNativePackageIndexingSection(AssetInfo info)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Indexing");
            section.AddToClassList(PackagesDetailSectionClass);
            CommonFormBuilder form = CreateNativePackageDetailFormBuilder(PackagesDetailToggleClass);

            bool indexingActionRunning = AI.Actions != null && AI.Actions.IsPackageIndexingActionRunning(info);
            PackageIndexingStatus status = PackageIndexingPolicy.GetStatus(info, indexingActionRunning);
            AddNativePackageIndexingStatusRow(section, info, status);

            if (AI.Actions.PackageBackupsEnabled && info.AssetSource != Asset.Source.RegistryPackage && info.AssetSource != Asset.Source.CurrentProject && info.ParentId == 0)
            {
                AddNativePackageToggle(section, form, "package.backup", "Backup", "Create backups for this package whenever the package backup action runs.", info.Backup, value =>
                {
                    info.Backup = value;
                    AI.SetAssetBackup(info, value);
                });
            }
            if (AI.Actions.AICaptionsEnabled && info.AssetSource != Asset.Source.CurrentProject)
            {
                AddNativePackageToggle(section, form, "package.aiusage", "AI Captions", "Create AI captions for this package whenever the caption action runs.", info.UseAI, value =>
                {
                    info.UseAI = value;
                    AI.SetAssetAIUse(info, value);
                });
            }
            if (AI.Actions.SemanticSearchEnabled && info.AssetSource != Asset.Source.CurrentProject)
            {
                AddNativePackageToggle(section, form, "package.semanticindex", "Semantic Index", "Include this package when updating the semantic asset index.", info.IsSemanticIndexEnabled, value => AI.SetAssetSemanticIndexUse(info, value));
            }
            if (AI.Actions.CodeSearchEnabled && info.AssetSource != Asset.Source.CurrentProject)
            {
                AddNativePackageToggle(section, form, "package.codeindex", "Code Index", "Include this package when updating the code search index.", info.IsCodeIndexEnabled, value => AI.SetAssetCodeIndexUse(info, value));
            }
            if (info.AssetSource != Asset.Source.CurrentProject)
            {
                if (PackageIndexingPolicy.IsInheritedNoIndex(info))
                {
                    AddNativePackageDetailRow(
                        section,
                        "package.noindex",
                        "Do Not Index",
                        $"Controlled by {info.ParentInfo.GetDisplayName()}",
                        "Sub-packages inherit Do Not Index from their parent package.");
                }
                else
                {
                    AddNativePackageToggle(section, form, "package.noindex", "Do Not Index", "Skip this package in future indexing runs. Existing indexed content is kept.", info.NoIndex, value =>
                    {
                        info.NoIndex = value;
                        AI.SetAssetNoIndex(info, value);
                        _requireAssetTreeRebuild = true;
                    });
                }
            }
            if (info.AssetSource == Asset.Source.CustomPackage || info.AssetSource == Asset.Source.Archive || info.AssetSource == Asset.Source.AssetStorePackage || info.AssetSource == Asset.Source.Synty)
            {
                AddNativePackageToggle(section, form, "package.extract", "Keep Cached", "Keep this package extracted to minimize access delays.", info.KeepExtracted, value =>
                {
                    info.KeepExtracted = value;
                    AI.SetAssetExtraction(info, value);
                });
            }
            if (info.AssetSource != Asset.Source.CurrentProject)
            {
                AddNativePackageToggle(section, form, "package.exclude", "Exclude", "Exclude this package and its existing results from package and search views. This is separate from future indexing participation and can be reversed with the Excluded maintenance filter.", info.Exclude, value =>
                {
                    info.Exclude = value;
                    AI.SetAssetExclusion(info, value);
                    _requireLookupUpdate = ChangeImpact.Write;
                    _requireSearchUpdate = true;
                    _requireAssetTreeRebuild = true;
                });
            }
            return section;
        }

        private void AddNativePackageIndexingStatusRow(VisualElement section, AssetInfo info, PackageIndexingStatus status)
        {
            string statusText = FormatNativePackageIndexingStatus(status);
            string statusTooltip = GetNativePackageIndexingStatusTooltip(status);
            bool showIndexNow = (status == PackageIndexingStatus.NeedsIndexing || status == PackageIndexingStatus.Incomplete)
                && info.ParentId <= 0
                && info.AssetSource != Asset.Source.CurrentProject
                && (info.AssetSource != Asset.Source.AssetManager || AI.Actions.AssetManagerEnabled)
                && info.SafeName != Asset.NONE;
            if (!showIndexNow)
            {
                AddNativePackageDetailRow(section, "package.indexingstatus", "Status", statusText, statusTooltip);
                return;
            }

            VisualElement control = new VisualElement
            {
                tooltip = statusTooltip
            };
            control.AddToClassList(PackagesDetailStatusControlClass);

            Label value = AssetInventoryUITK.CreateCopyLabel(statusText);
            value.AddToClassList(PackagesDetailValueClass);
            control.Add(value);

            Button indexNow = AssetInventoryUITK.CreateButton("Index Now", () => IncludeAndIndexPackagesNow(new[] {info}));
            indexNow.tooltip = "Index this package now. It is already included in future indexing.";
            indexNow.AddToClassList(PackagesDetailStatusActionClass);
            indexNow.SetEnabled(!AI.Actions.ActionsInProgress && CanIndexPackageNow(info));
            control.Add(indexNow);

            AddNativePackageControlRow(section, "package.indexingstatus", "Status", control);
        }

        private void AddNativePackageToggle(
            VisualElement section,
            CommonFormBuilder form,
            string key,
            string label,
            string tooltip,
            bool value,
            Action<bool> onChange)
        {
            VisualElement row = form.CreateToggleRow(label, value, changed =>
            {
                onChange?.Invoke(changed);
                ScheduleNativePackageDetailsRebuild();
            }, tooltip);
            section.Add(CreateNativePackageKeyedBlock(key, () => row));
        }

        private VisualElement CreateNativePackageMetadataSection(AssetInfo info)
        {
            List<MetadataInfo> entries = info.PackageMetadata?
                .Where(meta => (!meta.RestrictAssetSource || info.AssetSource == meta.ApplicableSource) && meta.Name != MetadataDefinition.FIELD_HIDE)
                .ToList() ?? new List<MetadataInfo>();
            if (entries.Count == 0 && !_metadataEditMode) return CreateNativePackageMetadataActions(info, null);

            VisualElement section = AssetInventoryUITK.CreateSection("Metadata");
            section.AddToClassList(PackagesDetailSectionClass);
            CommonFormBuilder form = CreateNativePackageDetailFormBuilder();
            foreach (MetadataInfo meta in entries)
            {
                VisualElement row = CreateNativePackageMetadataRow(info, meta, form);
                section.Add(CreateNativePackageKeyedBlock($"package.metadata.{meta.Id}", () => row));
            }
            section.Add(CreateNativePackageMetadataActions(info, section));
            return section;
        }

        private VisualElement CreateNativePackageMetadataRow(AssetInfo info, MetadataInfo meta, CommonFormBuilder form)
        {
            if (!_metadataEditMode)
            {
                if (meta.Type == MetadataDefinition.DataType.Boolean)
                {
                    return form.CreateToggleRow(meta.Name, meta.BoolValue, value =>
                    {
                        meta.BoolValue = value;
                        DBAdapter.DB.Update(meta.ToAssignment());
                    });
                }
                string value = FormatNativePackageMetadataValue(meta);
                if (meta.Type == MetadataDefinition.DataType.Url && !string.IsNullOrWhiteSpace(meta.StringValue))
                {
                    VisualElement linkRow = CreateNativePackageDetailRow(meta.Name, value, meta.StringValue, () => AI.OpenURL(meta.StringValue));
                    return linkRow;
                }
                return CreateNativePackageDetailRow(meta.Name, value);
            }

            VisualElement control;
            switch (meta.Type)
            {
                case MetadataDefinition.DataType.Boolean:
                    control = form.CreateToggle(meta.BoolValue, value =>
                    {
                        meta.BoolValue = value;
                        DBAdapter.DB.Update(meta.ToAssignment());
                    });
                    break;
                case MetadataDefinition.DataType.Text:
                case MetadataDefinition.DataType.Url:
                    control = form.CreateTextField(meta.StringValue, value =>
                    {
                        meta.StringValue = value;
                        DBAdapter.DB.Update(meta.ToAssignment());
                    }, isDelayed: true);
                    break;
                case MetadataDefinition.DataType.BigText:
                    TextField bigText = form.CreateTextField(meta.StringValue, value =>
                    {
                        meta.StringValue = value;
                        DBAdapter.DB.Update(meta.ToAssignment());
                    }, isDelayed: true);
                    bigText.multiline = true;
                    control = bigText;
                    break;
                case MetadataDefinition.DataType.Number:
                    control = form.CreateIntegerField(meta.IntValue, value =>
                    {
                        meta.IntValue = value;
                        DBAdapter.DB.Update(meta.ToAssignment());
                    });
                    break;
                case MetadataDefinition.DataType.DecimalNumber:
                    control = form.CreateFloatField(meta.FloatValue, value =>
                    {
                        meta.FloatValue = value;
                        DBAdapter.DB.Update(meta.ToAssignment());
                    });
                    break;
                case MetadataDefinition.DataType.Date:
                case MetadataDefinition.DataType.DateTime:
                    control = form.CreateTextField(meta.DateTimeValue.ToString("o"), value =>
                    {
                        if (!DateTime.TryParse(value, out DateTime parsed)) return;
                        meta.DateTimeValue = parsed;
                        DBAdapter.DB.Update(meta.ToAssignment());
                    }, isDelayed: true);
                    break;
                case MetadataDefinition.DataType.SingleSelect:
                    List<string> values = new List<string> {"-none-"};
                    if (!string.IsNullOrWhiteSpace(meta.ValueList)) values.AddRange(meta.ValueList.Split(',').Select(value => value.Trim()));
                    string selected = string.IsNullOrWhiteSpace(meta.StringValue) || !values.Contains(meta.StringValue) ? values[0] : meta.StringValue;
                    UnityEngine.UIElements.PopupField<string> popup = new UnityEngine.UIElements.PopupField<string>(values, selected);
                    popup.RegisterValueChangedCallback(evt =>
                    {
                        meta.StringValue = evt.newValue == "-none-" ? null : evt.newValue;
                        DBAdapter.DB.Update(meta.ToAssignment());
                    });
                    control = popup;
                    break;
                case MetadataDefinition.DataType.List:
                    control = AssetInventoryUITK.CreateStringListControl(this, meta.StringValue, ";", value =>
                    {
                        meta.StringValue = value;
                        DBAdapter.DB.Update(meta.ToAssignment());
                    }, meta.Name, $"Edit {meta.Name}");
                    break;
                default:
                    control = form.CreateTextField(meta.StringValue, null, isReadOnly: true);
                    break;
            }

            Button remove = AssetInventoryUITK.CreateIconButton("Remove metadata", "TreeEditor.Trash", () =>
            {
                Metadata.RemoveAssignment(info, meta);
                ScheduleNativePackageDetailsRebuild();
            });
            control.AddToClassList(PackagesDetailMetadataControlClass);
            remove.AddToClassList(PackagesDetailMetadataRemoveClass);
            VisualElement row = form.CreateRow(meta.Name, null, control, remove);
            return row;
        }

        private VisualElement CreateNativePackageMetadataActions(AssetInfo info, VisualElement section)
        {
            VisualElement actions = new VisualElement();
            actions.AddToClassList(PackagesDetailMetadataActionsClass);
            Button add = null;
            add = AssetInventoryUITK.CreateSecondaryButton("Add Metadata...", () =>
            {
                MetadataSelectionUI.ShowDropdown(
                    this,
                    add,
                    MetadataAssignment.Target.Package,
                    new List<AssetInfo> {info},
                    () =>
                    {
                        _metadataEditMode = true;
                        ScheduleNativePackageDetailsRebuild();
                    });
            });
            actions.Add(add);
            if (info.PackageMetadata != null && info.PackageMetadata.Count > 0)
            {
                Button edit = AssetInventoryUITK.CreateSecondaryButton(_metadataEditMode ? "Done" : "Edit Metadata", () =>
                {
                    _metadataEditMode = !_metadataEditMode;
                    ScheduleNativePackageDetailsRebuild();
                });
                actions.Add(edit);
            }

            VisualElement block = CreateNativePackageKeyedBlock("package.metadata", () => actions, _metadataEditMode);
            if (section == null)
            {
                VisualElement standalone = AssetInventoryUITK.CreateSection("Metadata");
                standalone.AddToClassList(PackagesDetailSectionClass);
                standalone.Add(block);
                return standalone;
            }
            return block;
        }

        private static string FormatNativePackageMetadataValue(MetadataInfo meta)
        {
            switch (meta.Type)
            {
                case MetadataDefinition.DataType.Boolean:
                    return meta.BoolValue ? "Yes" : "No";
                case MetadataDefinition.DataType.Number:
                    return meta.IntValue.ToString();
                case MetadataDefinition.DataType.DecimalNumber:
                    return $"{meta.FloatValue:N1}";
                case MetadataDefinition.DataType.Date:
                    return meta.DateTimeValue.ToShortDateString();
                case MetadataDefinition.DataType.DateTime:
                    return meta.DateTimeValue.ToString("g");
                default:
                    return meta.StringValue;
            }
        }

        private CommonTabbedPane CreateNativePackageTabbedDetails(AssetInfo info, List<string> sections)
        {
            if (_packageDetailsTab < 0 || _packageDetailsTab >= sections.Count) _packageDetailsTab = 0;
            CommonTabbedPane pane = AssetInventoryUITK.CreateTabbedInspectorPane();
            pane.AddToClassList(PackagesDetailRichTabsClass);
            pane.SetTabs(sections, _packageDetailsTab, index =>
            {
                _packageDetailsTab = index;
                ScheduleNativePackageDetailsRebuild();
            });

            VisualElement body = CreateNativePackageRichSection(info, sections[_packageDetailsTab]);
            body.AddToClassList(PackagesDetailRichBodyClass);
            pane.Body.Add(body);
            return pane;
        }

        private VisualElement CreateNativePackageRichSection(AssetInfo info, string section)
        {
            switch (section)
            {
                case "Media":
                    return CreateNativePackageKeyedBlock("package.media", () => CreateNativePackageMediaSection(info));
                case "About":
                    return CreateNativePackageKeyedBlock("package.description", () => CreateNativePackageTextSection("Description", info.Description));
                case "Release Notes":
                    return CreateNativePackageKeyedBlock("package.releasenotes", () => CreateNativePackageTextSection("Release Notes", info.ReleaseNotes));
                case "Dependencies":
                    return CreateNativePackageKeyedBlock("package.dependencies", () => CreateNativePackageDependenciesSection(info));
                default:
                    return new VisualElement();
            }
        }

        private VisualElement CreateNativePackageTextSection(string title, string text)
        {
            VisualElement section = AssetInventoryUITK.CreateSection(title);
            section.AddToClassList(PackagesDetailSectionClass);
            section.Add(CreateNativePackageText(text));
            return section;
        }

        private VisualElement CreateNativePackageText(string rawText)
        {
            VisualElement root = new VisualElement();
            TextWithLinks parsed = StringUtils.ToLabelWithLinks(rawText);
            if (!string.IsNullOrWhiteSpace(parsed.Text))
            {
                Label text;
                if (parsed.HasLinks)
                {
                    Color linkColor = EditorGUIUtility.isProSkin
                        ? new Color32(91, 169, 255, 255)
                        : new Color32(13, 94, 168, 255);
                    text = new CommonInlineLinkLabel(parsed, AI.OpenURL, linkColor, PackagesDetailInlineLinkHoverClass);
                    text.AddToClassList("ai-section-copy");
                }
                else
                {
                    text = AssetInventoryUITK.CreateCopyLabel(parsed.Text);
                }
                text.AddToClassList(PackagesDetailTextClass);
                root.Add(text);
            }
            return root;
        }

        private VisualElement CreateNativePackageMediaSection(AssetInfo info)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Media");
            section.AddToClassList(PackagesDetailSectionClass);
            section.AddToClassList(PackagesDetailMediaClass);
            if (info.Media == null || info.Media.Count == 0)
            {
                section.Add(AssetInventoryUITK.CreateHelpBox("No gallery metadata is available for this package."));
                if (info.GetRoot().ForeignId > 0)
                {
                    Button refresh = AssetInventoryUITK.CreateSecondaryButton(
                        "Refresh Metadata",
                        () => _ = RefreshNativePackageMetadataAsync(info));
                    refresh.tooltip = "Fetch current package details and gallery metadata from the Asset Store.";
                    refresh.SetEnabled(!AI.Actions.ActionsInProgress);
                    section.Add(refresh);
                }
                return section;
            }

            List<int> imageIndices = GetPackageImageMediaIndices(info.Media);
            _selectedMedia = NormalizePackageImageMediaIndex(info.Media, _selectedMedia);
            AssetMedia selected = _selectedMedia >= 0 ? info.Media[_selectedMedia] : null;
            if (selected != null && selected.Texture == null && !selected.IsDownloading && !selected.DownloadFailed)
            {
                LoadNativePackageMedia(info, selected);
            }

            VisualElement stage = new VisualElement
            {
                focusable = imageIndices.Count > 0,
                tooltip = "Use the left and right arrow keys to browse package images."
            };
            stage.AddToClassList(PackagesDetailMediaMainClass);
            int galleryHeight = Mathf.Clamp(AI.Config.mediaHeight, 120, 720);
            stage.style.height = galleryHeight;
            stage.style.minHeight = galleryHeight;
            stage.style.maxHeight = galleryHeight;
            stage.RegisterCallback<GeometryChangedEvent>(evt =>
                ApplyNativePackageMediaStageHeight(stage, selected, evt.newRect.width, galleryHeight));

            Image main = new Image
            {
                image = selected?.Texture,
                scaleMode = ScaleMode.ScaleToFit,
                tooltip = selected?.Texture == null ? "Loading media..." : "Open media"
            };
            main.AddToClassList(PackagesDetailMediaMainImageClass);
            main.RegisterCallback<ClickEvent>(_ =>
            {
                if (selected?.Texture == null) return;
                string path = ResolveNativePackageMediaPath(info, selected);
                if (!string.IsNullOrWhiteSpace(path)) Task.Run(() => Process.Start(path));
            });
            stage.Add(main);

            if (selected == null)
            {
                Label status = AssetInventoryUITK.CreateMutedLabel("This package has no inline gallery images.");
                status.AddToClassList(PackagesDetailMediaStatusClass);
                stage.Add(status);
            }
            else if (selected.DownloadFailed)
            {
                VisualElement status = new VisualElement();
                status.AddToClassList(PackagesDetailMediaStatusClass);
                status.Add(AssetInventoryUITK.CreateMutedLabel("The image could not be downloaded."));
                Button retry = AssetInventoryUITK.CreateSecondaryButton("Retry", () => RetryNativePackageMedia(info, selected));
                retry.tooltip = "Try downloading this gallery image again.";
                status.Add(retry);
                stage.Add(status);
            }
            else if (selected.Texture == null)
            {
                Label status = AssetInventoryUITK.CreateMutedLabel("Loading image...");
                status.AddToClassList(PackagesDetailMediaStatusClass);
                stage.Add(status);
            }

            int previousIndex = GetAdjacentPackageImageMediaIndex(info.Media, _selectedMedia, -1);
            int nextIndex = GetAdjacentPackageImageMediaIndex(info.Media, _selectedMedia, 1);
            Button previous = CreateNativePackageMediaNavigationButton(
                "\u2039",
                "Previous gallery image",
                PackagesDetailMediaPreviousClass,
                previousIndex,
                info);
            Button next = CreateNativePackageMediaNavigationButton(
                "\u203a",
                "Next gallery image",
                PackagesDetailMediaNextClass,
                nextIndex,
                info);
            stage.Add(previous);
            stage.Add(next);

            int selectedImagePosition = imageIndices.IndexOf(_selectedMedia);
            Label counter = new Label(selectedImagePosition >= 0 ? $"{selectedImagePosition + 1} / {imageIndices.Count}" : $"0 / {imageIndices.Count}");
            counter.AddToClassList(PackagesDetailMediaCounterClass);
            stage.Add(counter);
            stage.RegisterCallback<KeyDownEvent>(evt =>
            {
                int targetIndex = evt.keyCode == KeyCode.LeftArrow
                    ? previousIndex
                    : evt.keyCode == KeyCode.RightArrow
                        ? nextIndex
                        : -1;
                if (targetIndex < 0) return;

                SelectNativePackageMedia(info, info.Media[targetIndex], targetIndex);
                evt.StopPropagation();
            });
            if (_focusNativePackageMediaAfterRebuild)
            {
                _focusNativePackageMediaAfterRebuild = false;
                stage.schedule.Execute(stage.Focus).ExecuteLater(0);
            }
            section.Add(stage);

            ScrollView thumbnails = new ScrollView(ScrollViewMode.Horizontal);
            thumbnails.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            thumbnails.AddToClassList(PackagesDetailMediaThumbsClass);
            int thumbnailWidth = Mathf.Clamp(AI.Config.mediaThumbnailWidth, 48, 320);
            int thumbnailHeight = Mathf.Clamp(AI.Config.mediaThumbnailHeight, 40, 240);
            int thumbnailStripHeight = thumbnailHeight + 14;
            thumbnails.style.height = thumbnailStripHeight;
            thumbnails.style.minHeight = thumbnailStripHeight;
            _nativePackageMediaScroll = thumbnails;
            _nativePackageMediaScrollAssetId = info.AssetId;
            Button selectedThumbnail = null;
            for (int i = 0; i < info.Media.Count; i++)
            {
                int index = i;
                AssetMedia media = info.Media[index];
                Button thumbnail = new Button(() => SelectNativePackageMedia(info, media, index));
                thumbnail.AddToClassList(PackagesDetailMediaThumbClass);
                thumbnail.EnableInClassList(PackagesDetailMediaThumbSelectedClass, index == _selectedMedia);
                thumbnail.style.width = thumbnailWidth;
                thumbnail.style.minWidth = thumbnailWidth;
                thumbnail.style.height = thumbnailHeight;
                thumbnail.style.minHeight = thumbnailHeight;
                Texture2D texture = media.ThumbnailTexture != null ? media.ThumbnailTexture : media.Texture;
                thumbnail.Add(new Image {image = texture, scaleMode = ScaleMode.ScaleToFit});
                thumbnail.tooltip = IsExternalPackageMedia(media)
                    ? "Open this media item in your browser."
                    : "Show this media item in the preview.";
                thumbnails.Add(thumbnail);
                if (index == _selectedMedia) selectedThumbnail = thumbnail;
            }
            section.Add(thumbnails);
            _nativeScrollViewState.Restore(GetNativePackageMediaScrollKey(info.AssetId), thumbnails);
            if (selectedThumbnail != null)
            {
                thumbnails.schedule.Execute(() => thumbnails.ScrollTo(selectedThumbnail)).ExecuteLater(0);
            }
            return section;
        }

        private static void ApplyNativePackageMediaStageHeight(
            VisualElement stage,
            AssetMedia media,
            float availableWidth,
            float maximumHeight)
        {
            if (stage == null || media == null) return;

            Texture2D texture = media.Texture != null ? media.Texture : media.ThumbnailTexture;
            int sourceWidth = texture != null ? texture.width : media.Width;
            int sourceHeight = texture != null ? texture.height : media.Height;
            float targetHeight = CalculatePackageMediaStageHeight(sourceWidth, sourceHeight, availableWidth, maximumHeight);
            if (targetHeight <= 0f || Mathf.Abs(stage.resolvedStyle.height - targetHeight) <= 0.5f) return;

            stage.style.height = targetHeight;
            stage.style.minHeight = targetHeight;
            stage.style.maxHeight = targetHeight;
        }

        internal static float CalculatePackageMediaStageHeight(
            int sourceWidth,
            int sourceHeight,
            float availableWidth,
            float maximumHeight)
        {
            if (sourceWidth <= 0
                || sourceHeight <= 0
                || float.IsNaN(availableWidth)
                || float.IsInfinity(availableWidth)
                || availableWidth <= 0f
                || float.IsNaN(maximumHeight)
                || float.IsInfinity(maximumHeight)
                || maximumHeight <= 0f)
            {
                return 0f;
            }

            float aspectHeight = availableWidth * sourceHeight / sourceWidth;
            return Mathf.Min(aspectHeight, maximumHeight);
        }

        private async void LoadNativePackageMedia(AssetInfo info, AssetMedia media)
        {
            await MediaManager.LoadFullMediaOnDemand(info, media);
            ScheduleNativePackageDetailsRebuild();
        }

        private void RetryNativePackageMedia(AssetInfo info, AssetMedia media)
        {
            media.DownloadFailed = false;
            LoadNativePackageMedia(info, media);
            ScheduleNativePackageDetailsRebuild();
        }

        private void SelectNativePackageMedia(AssetInfo info, AssetMedia media, int index)
        {
            if (IsExternalPackageMedia(media))
            {
                AI.OpenURL(media.GetUrl());
                return;
            }
            _selectedMedia = index;
            _focusNativePackageMediaAfterRebuild = true;
            if (media.Texture == null && !media.IsDownloading && !media.DownloadFailed) LoadNativePackageMedia(info, media);
            ScheduleNativePackageDetailsRebuild();
        }

        private Button CreateNativePackageMediaNavigationButton(
            string text,
            string tooltip,
            string positionClass,
            int targetIndex,
            AssetInfo info)
        {
            Button button = AssetInventoryUITK.CreateSecondaryButton(text, () =>
            {
                if (targetIndex >= 0) SelectNativePackageMedia(info, info.Media[targetIndex], targetIndex);
            });
            button.tooltip = tooltip;
            button.AddToClassList(PackagesDetailMediaNavigationClass);
            button.AddToClassList(positionClass);
            button.SetEnabled(targetIndex >= 0);
            return button;
        }

        private static string ResolveNativePackageMediaPath(AssetInfo info, AssetMedia media)
        {
            if (info == null || media == null) return null;

            string path = info.ToAsset().GetMediaFile(media, Paths.GetPreviewFolder());
            if (!string.IsNullOrWhiteSpace(path)) return path;
            if (!string.IsNullOrWhiteSpace(media.Url) && File.Exists(media.Url)) return media.Url;
            return null;
        }

        internal static bool IsExternalPackageMedia(AssetMedia media)
        {
            if (media == null) return false;

            return media.Type == "youtube"
                || media.Type == "vimeo"
                || media.Type == "sketchfab"
                || media.Type == "soundcloud"
                || media.Type == "mixcloud"
                || media.Type == "attachment_video"
                || media.Type == "attachment_audio";
        }

        internal static List<int> GetPackageImageMediaIndices(IReadOnlyList<AssetMedia> media)
        {
            List<int> result = new List<int>();
            if (media == null) return result;

            for (int i = 0; i < media.Count; i++)
            {
                AssetMedia item = media[i];
                if (item != null && !IsExternalPackageMedia(item)) result.Add(i);
            }
            return result;
        }

        internal static int NormalizePackageImageMediaIndex(IReadOnlyList<AssetMedia> media, int selectedIndex)
        {
            List<int> imageIndices = GetPackageImageMediaIndices(media);
            if (imageIndices.Contains(selectedIndex)) return selectedIndex;
            return imageIndices.Count > 0 ? imageIndices[0] : -1;
        }

        internal static int GetAdjacentPackageImageMediaIndex(IReadOnlyList<AssetMedia> media, int selectedIndex, int direction)
        {
            if (direction == 0) return -1;

            List<int> imageIndices = GetPackageImageMediaIndices(media);
            int position = imageIndices.IndexOf(selectedIndex);
            if (position < 0) return -1;

            int adjacentPosition = position + Math.Sign(direction);
            return adjacentPosition >= 0 && adjacentPosition < imageIndices.Count
                ? imageIndices[adjacentPosition]
                : -1;
        }

        private VisualElement CreateNativePackageDependenciesSection(AssetInfo info)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Dependencies");
            section.AddToClassList(PackagesDetailSectionClass);
            VisualElement columns = new VisualElement();
            columns.AddToClassList(PackagesDetailDependencyColumnsClass);

            if (info.AssetSource == Asset.Source.RegistryPackage)
            {
                PackageInfo packageInfo = AssetStore.GetPackageInfo(info);
                if (!AssetStore.IsMetadataAvailable())
                {
                    section.Add(AssetInventoryUITK.CreateHelpBox("Loading data..."));
                    return section;
                }
                if (packageInfo == null || packageInfo.dependencies == null)
                {
                    section.Add(AssetInventoryUITK.CreateHelpBox("Could not find matching package metadata.", MessageType.Warning));
                    return section;
                }

                VisualElement uses = CreateNativeDependencyColumn("Is Using");
                if (packageInfo.dependencies.Length == 0) uses.Add(AssetInventoryUITK.CreateMutedLabel("-none-"));
                foreach (DependencyInfo dependency in packageInfo.dependencies.OrderBy(value => value.name))
                {
                    AssetInfo dependencyPackage = _assets.FirstOrDefault(asset => asset.SafeName == dependency.name);
                    uses.Add(CreateNativeDependencyButton(dependencyPackage, $"{dependency.name} - {dependency.version}"));
                }
                columns.Add(uses);

                if (_usedByCacheAssetId != info.AssetId)
                {
                    _usedByCacheAssetId = info.AssetId;
                    _usedByCache = AssetStore.GetPackages().Values
                        .Where(package => package.dependencies != null && package.dependencies.Any(dependency => dependency.name == info.SafeName))
                        .OrderBy(package => package.displayName)
                        .ToList();
                }
                VisualElement usedBy = CreateNativeDependencyColumn("Used By");
                if (_usedByCache == null || _usedByCache.Count == 0) usedBy.Add(AssetInventoryUITK.CreateMutedLabel("-none-"));
                if (_usedByCache != null)
                {
                    foreach (PackageInfo dependency in _usedByCache)
                    {
                        AssetInfo dependencyPackage = _assets.FirstOrDefault(asset => asset.SafeName == dependency.name);
                        usedBy.Add(CreateNativeDependencyButton(dependencyPackage, $"{dependency.displayName} - {dependency.version}"));
                    }
                }
                columns.Add(usedBy);
            }
            else
            {
                List<Dependency> dependencies = info.GetPackageDependencies();
                if (dependencies != null)
                {
                    section.Add(AssetInventoryUITK.CreateHelpBox("These items might be required to use the package, but can also be optional."));
                    VisualElement uses = CreateNativeDependencyColumn("Is Using");
                    foreach (Dependency dependency in dependencies.OrderBy(value => value.name).ThenBy(value => value.location))
                    {
                        AssetInfo dependencyPackage = _assets.FirstOrDefault(asset => asset.ForeignId == dependency.id)
                            ?? _assets.FirstOrDefault(asset => asset.SafeName == dependency.location);
                        string text = dependencyPackage != null
                            ? dependencyPackage.GetDisplayName()
                            : (string.IsNullOrWhiteSpace(dependency.name) ? dependency.location : dependency.name);
                        uses.Add(dependencyPackage != null
                            ? CreateNativeDependencyButton(dependencyPackage, text)
                            : CreateNativeExternalDependencyButton(text, dependency.location));
                    }
                    columns.Add(uses);
                }
            }

            List<AssetInfo> usageDependencies = info.GetPackageUsageDependencies(_assets);
            if (usageDependencies != null)
            {
                VisualElement usedBy = CreateNativeDependencyColumn("Used By Asset Store Packages");
                foreach (AssetInfo package in usageDependencies.OrderBy(value => value.GetDisplayName()))
                {
                    usedBy.Add(CreateNativeDependencyButton(package, package.GetDisplayName()));
                }
                columns.Add(usedBy);
            }
            if (columns.childCount == 0) columns.Add(AssetInventoryUITK.CreateMutedLabel("-none-"));
            section.Add(columns);
            return section;
        }

        private VisualElement CreateNativeDependencyColumn(string title)
        {
            VisualElement column = new VisualElement();
            column.AddToClassList(PackagesDetailDependencyColumnClass);
            column.Add(AssetInventoryUITK.CreateSectionTitle(title));
            return column;
        }

        private Button CreateNativeDependencyButton(AssetInfo package, string text)
        {
            Button button = AssetInventoryUITK.CreateSecondaryButton(text, () =>
            {
                if (package != null) OpenInPackageView(package);
            });
            button.SetEnabled(package != null);
            return button;
        }

        private static Button CreateNativeExternalDependencyButton(string text, string url)
        {
            Button button = AssetInventoryUITK.CreateSecondaryButton(text + "*", () => AI.OpenURL(url));
            button.SetEnabled(!string.IsNullOrWhiteSpace(url));
            return button;
        }

        private VisualElement CreateNativePackagePreview(AssetInfo info, Texture previewTexture = null)
        {
            previewTexture ??= info?.PreviewTexture;
            VisualElement container = new VisualElement();
            container.AddToClassList(PackagesDetailPreviewContainerClass);
            Image image = new Image
            {
                image = previewTexture,
                scaleMode = ScaleMode.ScaleToFit,
                tooltip = info?.GetDisplayName()
            };
            image.AddToClassList(PackagesDetailPreviewClass);
            container.Add(image);

            ApplyNaturalPackagePreviewSize(image, previewTexture, float.PositiveInfinity);
            container.RegisterCallback<GeometryChangedEvent>(evt =>
                ApplyNaturalPackagePreviewSize(image, previewTexture, evt.newRect.width));
            return container;
        }

        internal static Texture ResolveNativePackagePreviewTexture(AssetInfo info)
        {
            if (info == null) return null;

            return info.PreviewTexture
                ?? info.GetFallbackIcon()
                ?? EditorGUIUtility.IconContent("Package Manager").image;
        }

        private static void ApplyNaturalPackagePreviewSize(Image image, Texture texture, float availableWidth)
        {
            if (image == null || texture == null) return;

            Vector2 size = CalculateNaturalPackagePreviewSize(texture.width, texture.height, availableWidth);
            if (size == Vector2.zero) return;

            image.style.width = size.x;
            image.style.height = size.y;
        }

        internal static Vector2 CalculateNaturalPackagePreviewSize(int sourceWidth, int sourceHeight, float availableWidth)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0 || float.IsNaN(availableWidth) || availableWidth <= 0f)
            {
                return Vector2.zero;
            }

            float scale = float.IsPositiveInfinity(availableWidth)
                ? 1f
                : Mathf.Min(1f, availableWidth / sourceWidth);
            return new Vector2(sourceWidth * scale, sourceHeight * scale);
        }

        private void AddNativePackageDetailRow(
            VisualElement section,
            string key,
            string label,
            string value,
            string tooltip = null,
            bool alwaysShow = false)
        {
            if (section == null || string.IsNullOrWhiteSpace(value)) return;
            VisualElement row = CreateNativePackageDetailRow(label, value, tooltip);
            if (string.IsNullOrWhiteSpace(key)) section.Add(row);
            else section.Add(CreateNativePackageKeyedBlock(key, () => row, alwaysShow));
        }

        private void AddNativePackageLinkRow(
            VisualElement section,
            string key,
            string label,
            string value,
            string url,
            bool assetStoreUrl = false,
            string tooltip = null)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            VisualElement row = CreateNativePackageDetailRow(label, value, tooltip ?? url, () =>
            {
                if (assetStoreUrl) AI.OpenStoreURL(url);
                else AI.OpenURL(url);
            });
            section.Add(CreateNativePackageKeyedBlock(key, () => row));
        }

        private void AddNativePackageControlRow(VisualElement section, string key, string label, VisualElement control)
        {
            CommonFormBuilder form = CreateNativePackageDetailFormBuilder();
            VisualElement row = form.CreateRow(label, control?.tooltip, control);
            section.Add(CreateNativePackageKeyedBlock(key, () => row));
        }

        private static CommonFormBuilder CreateNativePackageDetailFormBuilder(string toggleClass = null)
        {
            return AssetInventoryUITK.CreateFormBuilder(
                rowClass: PackagesDetailRowClass,
                labelClass: PackagesDetailLabelClass,
                controlClass: PackagesDetailControlClass,
                toggleClass: toggleClass);
        }

        private static VisualElement CreateNativePackageDropdownField(string text, string tooltip, Action showDropdown)
        {
            string safeTooltip = tooltip ?? string.Empty;
            VisualElement field = new VisualElement
            {
                name = text ?? string.Empty,
                tooltip = safeTooltip
            };
            CommonUITK.AddClasses(
                field,
                BaseField<string>.ussClassName,
                BaseField<string>.noLabelVariantUssClassName,
                BasePopupField<string, string>.ussClassName,
                PopupField<string>.ussClassName,
                PackagesDetailVersionFieldClass);

            VisualElement input = new VisualElement
            {
                focusable = true,
                tabIndex = 0,
                tooltip = safeTooltip
            };
            CommonUITK.AddClasses(
                input,
                BaseField<string>.inputUssClassName,
                BasePopupField<string, string>.inputUssClassName,
                PopupField<string>.inputUssClassName);
            input.AddManipulator(new Clickable(showDropdown));
            input.RegisterCallback<PointerDownEvent>(_ => input.Focus());
            input.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter && evt.keyCode != KeyCode.Space) return;

                showDropdown?.Invoke();
                evt.StopPropagation();
            });

            Label value = AssetInventoryUITK.CreateLabel(text ?? string.Empty);
            value.AddToClassList(BasePopupField<string, string>.textUssClassName);
            value.pickingMode = PickingMode.Ignore;
            input.Add(value);

            VisualElement arrow = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };
            arrow.AddToClassList(BasePopupField<string, string>.arrowUssClassName);
            input.Add(arrow);
            field.Add(input);
            return field;
        }

        private void AddNativePackageRatingRow(VisualElement section, AssetInfo info)
        {
            int filledStarCount = Mathf.Clamp(Mathf.RoundToInt(info.AssetRating), 0, 5);
            string ratingText = $"{info.AssetRating:N1} out of 5 from {info.RatingCount:N0} ratings";
            VisualElement row = new VisualElement
            {
                tooltip = $"Rating given by Asset Store users: {ratingText}. Hot value {info.Hotness}."
            };
            row.AddToClassList(PackagesDetailRowClass);
            row.Add(AssetInventoryUITK.CreateLabel("Rating", PackagesDetailLabelClass));

            VisualElement rating = new VisualElement
            {
                name = ratingText,
                pickingMode = PickingMode.Ignore
            };
            rating.AddToClassList(PackagesDetailValueClass);
            rating.AddToClassList(PackagesDetailRatingClass);

            Color gold = new Color(0.992f, 0.694f, 0.004f);
            for (int i = 0; i < 5; i++)
            {
                bool filled = i < filledStarCount;
                Label star = new Label(filled ? "\u2605" : "\u2606")
                {
                    pickingMode = PickingMode.Ignore
                };
                star.style.color = gold;
                star.AddToClassList(PackagesDetailRatingStarClass);
                rating.Add(star);
            }

            Label count = AssetInventoryUITK.CreateLabel($"({info.RatingCount:N0} ratings)", PackagesDetailRatingCountClass);
            count.pickingMode = PickingMode.Ignore;
            rating.Add(count);
            row.Add(rating);
            section.Add(CreateNativePackageKeyedBlock("package.rating", () => row));
        }

        private VisualElement CreateNativePackageDetailRow(string label, string value, string tooltip = null, Action click = null)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(PackagesDetailRowClass);
            row.tooltip = tooltip ?? value ?? string.Empty;
            Label labelElement = AssetInventoryUITK.CreateLabel(label, PackagesDetailLabelClass);
            row.Add(labelElement);
            if (click == null)
            {
                Label valueElement = AssetInventoryUITK.CreateCopyLabel(value ?? string.Empty);
                valueElement.AddToClassList(PackagesDetailValueClass);
                row.Add(valueElement);
            }
            else
            {
                Button link = AssetInventoryUITK.CreateButton(value ?? string.Empty, click);
                link.AddToClassList(PackagesDetailValueClass);
                link.AddToClassList(PackagesDetailLinkClass);
                row.Add(link);
            }
            return row;
        }

        private VisualElement CreateNativePackageKeyedBlock(string key, Func<VisualElement> content, bool alwaysShow = false)
        {
            return AssetInventoryUITK.CreateAdvancedVisibilityBlock(
                key,
                content,
                alwaysShow,
                onVisibilityChanged: ScheduleNativePackageDetailsRebuild);
        }

        private void AddNativePackageHints(VisualElement root, AssetInfo info)
        {
            if (info.SafeName == Asset.NONE)
            {
                AddNativePackageHint(root, "package.hints.noname", "This is an automatically created package for indexed media files that are not associated with another package.", MessageType.Info);
            }
            if (info.ParentInfo != null)
            {
                AddNativePackageHint(root, "package.hints.subpackage", $"This is a sub-package inside '{info.ParentInfo.GetDisplayName()}'.", MessageType.Info);
            }
            if (PackageIndexingPolicy.IsInheritedNoIndex(info))
            {
                AddNativePackageHint(root, "package.hints.inheritednoindex", $"Future indexing is disabled by parent package '{info.ParentInfo.GetDisplayName()}'. Change the parent package to include this sub-package.", MessageType.Info);
            }
            if (info.IsDeprecated) AddNativePackageHint(root, "package.hints.deprecation", "This asset is deprecated.", MessageType.Warning);
            if (info.IsAbandoned) AddNativePackageHint(root, "package.hints.abandoned", "This asset is no longer available for download.", MessageType.Error);
#if !USE_ASSET_MANAGER || !USE_CLOUD_IDENTITY
            if (info.AssetSource == Asset.Source.AssetManager && AI.Actions.AssetManagerEnabled)
            {
                AddNativePackageHint(root, "package.hints.noassetmanager", "This package links to Unity Asset Manager, but its SDK is not installed. No actions are available.", MessageType.Info);
            }
#endif
            if (info.CurrentSubState == Asset.SubState.Outdated)
            {
                AssetDownloader.State? state = info.PackageDownloader?.GetState().state;
                if (state == AssetDownloader.State.Downloaded || state == AssetDownloader.State.UpdateAvailable)
                {
                    AddNativePackageHint(root, "package.hints.outdated", "This asset is outdated in the cache. Delete it from the database and file system when it is no longer needed.", MessageType.Info);
                }
            }
        }

        private void AddNativePackageHint(VisualElement root, string key, string text, MessageType type)
        {
            root.Add(CreateNativePackageKeyedBlock(key, () => AssetInventoryUITK.CreateHelpBox(text, type)));
        }

        internal VisualElement CreateNativePackageActionsSection(AssetInfo info)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Actions");
            section.AddToClassList(PackagesDetailSectionClass);
            VisualElement actions = new VisualElement();
            actions.AddToClassList(PackagesDetailActionsClass);
            actions.AddToClassList(PackagesDetailActionGridClass);
            section.Add(actions);

            bool busy = AI.Actions.ActionsInProgress;
            bool primaryUsed = false;
            bool showDelete = info.CurrentSubState == Asset.SubState.Outdated;

            if (info.AssetSource == Asset.Source.RegistryPackage)
            {
                AddNativeRegistryPackageActions(actions, info, busy, ref primaryUsed);
            }
            else if (info.AssetSource != Asset.Source.AssetManager && info.SafeName != Asset.NONE
                && (info.IsDownloaded || info.AssetSource == Asset.Source.AssetStorePackage)
                && !info.IsAbandoned)
            {
                AddNativeImportedPackageActions(actions, info, busy, ref primaryUsed);
            }

            AddNativeIndexingParticipationActions(actions, info, busy, ref primaryUsed);

            if (info.ForeignId > 0 || info.AssetSource == Asset.Source.RegistryPackage)
            {
                AddNativePackageAction(actions, "package.actions.openinpackagemanager", "Package Manager...", () => AssetStore.OpenInPackageManager(info));
            }
            if (AI.Config.tab != 1 && info.AssetId > 0)
            {
                AddNativePackageAction(actions, "package.actions.packageview", "Show in Package View", () => OpenInPackageView(info));
            }
            if (AI.Config.tab > 0 && info.IsIndexed && info.FileCount > 0)
            {
                AddNativePackageAction(actions, "package.actions.openinsearch", "Open in Search", () => OpenInSearch(info), primary: !primaryUsed);
                primaryUsed = true;
            }

#if USE_ASSET_MANAGER && USE_CLOUD_IDENTITY
            if (info.SafeName != Asset.NONE && info.AssetSource == Asset.Source.AssetManager && AI.Actions.AssetManagerEnabled)
            {
                Button create = null;
                create = AddNativePackageAction(actions, "package.actions.createcollection", "Create Collection...", () =>
                {
                    NameWindow.ShowAsDropDown(
                        CommonUITK.ToScreenDropdownAnchor(this, create),
                        "New Collection",
                        name => CreateCollection(info, name));
                }, enabled: !CloudAssetManagement.IsBusy && !busy);
                if (info.ParentInfo != null)
                {
                    AddNativePackageAction(actions, "package.actions.deletecollection", "Delete Collection", () => DeleteCollection(info), enabled: !CloudAssetManagement.IsBusy && !busy, destructive: true);
                }
            }
#endif

            if (info.SafeName != Asset.NONE && info.ForeignId > 0)
            {
                AddNativePackageAction(actions, "package.actions.refreshmetadata", "Refresh Metadata", () => _ = AI.Actions.FetchAssetDetails(true, info.AssetId), enabled: !busy, tooltip: "Fetch current metadata from the Asset Store.");
            }
            if (info.IsIndexed && info.FileCount > 0)
            {
                AddNativePackageAction(actions, "package.actions.recreatepreviews", "Previews Wizard...", () =>
                {
                    PreviewWizardUI window = PreviewWizardUI.ShowWindow();
                    window.Init(new List<AssetInfo> {info}, _assets);
                });
            }

            if (!info.IsDownloaded)
            {
                if (info.ParentId <= 0 && (info.AssetSource == Asset.Source.CustomPackage || info.AssetSource == Asset.Source.Archive || info.AssetSource == Asset.Source.Directory))
                {
                    showDelete = true;
                    section.Add(AssetInventoryUITK.CreateHelpBox("This package no longer exists on the file system and was probably deleted.", MessageType.Error));
                }
                else if (!info.IsAbandoned)
                {
                    section.Add(CreateNativePackageDownload(info));
                }
            }

            if (info.AssetSource != Asset.Source.CurrentProject)
            {
                if (info.AssetSource != Asset.Source.RegistryPackage)
                {
                    AddNativePackageAction(actions, "package.actions.export", "Export Package...", () =>
                    {
                        ExportUI window = ExportUI.ShowWindow();
                        window.Init(_selectedTreeAssets, false, 0, assetColumnState.VisibleColumns);
                    });
                }
                if ((info.IsDownloaded || info.AssetSource == Asset.Source.AssetStorePackage)
                    && PackageIndexingPolicy.IsIndexingEnabled(info)
                    && PackageIndexingPolicy.HasIndexedContent(info))
                {
                    AddNativePackageAction(actions, "package.actions.reindexnow", "Reindex Package Now", () =>
                    {
                        ReindexPackageNow(info);
                        ScheduleNativePackageDetailsRebuild();
                    }, enabled: !busy);
                }
                if (info.IsIndexed && info.FileCount > 0)
                {
                    AddNativePackageAction(actions, "package.actions.hidecontent", "Hide Content...", () =>
                    {
                        HideContentUI window = HideContentUI.ShowWindow();
                        window.Init(info);
                    }, enabled: !busy, tooltip: "Manage which package files are hidden from search results.");
                }
            }

            if (AI.Config.tab == 0 && _selectedAsset == 0 && info.IsIndexed && info.FileCount > 0)
            {
                AddNativePackageAction(actions, "package.actions.filter", "Filter to This Package", () => OpenInSearch(info, true),
                    tooltip: "Show only files from this package in Search.");
            }

            if (ShouldShowPackageDataEditorAction(info))
            {
                VisualElement connectionActions = new VisualElement();
                connectionActions.AddToClassList(PackagesDetailActionsClass);
                connectionActions.AddToClassList(PackagesDetailActionGridClass);
                if (info.ForeignId <= 0)
                {
                    Button connect = null;
                    connect = AddNativePackageAction(connectionActions, null, "Connect to Store...", () =>
                    {
                        AssetConnectionUI.ShowDropdown(CommonUITK.ToScreenDropdownAnchor(this, connect), details => ConnectToAssetStore(info, details));
                    }, enabled: !busy, tooltip: "Connect this package to its Unity Asset Store listing.");
                }
                AddNativePackageAction(connectionActions, null, "Edit Data...", () => OpenPackageDataEditor(info), enabled: !busy,
                    tooltip: "Edit package metadata and its local location when supported.");
                actions.Add(CreateNativePackageKeyedBlock("package.actions.connecttoassetstore", () => connectionActions));
            }

            if (info.AssetSource != Asset.Source.CurrentProject)
            {
                if (info.ForeignId > 0 && (info.AssetSource == Asset.Source.CustomPackage || info.AssetSource == Asset.Source.Archive))
                {
                    AddNativePackageAction(actions, "package.actions.removeassetstoreconnection", "Remove Asset Store Connection", () =>
                    {
                        bool removeMetadata = EditorUtility.DisplayDialog("Remove Metadata", "Remove additional Asset Store metadata such as ratings and category?", "Remove", "Keep");
                        AI.DisconnectFromAssetStore(info, removeMetadata);
                        _requireAssetTreeRebuild = true;
                        ScheduleNativePackageDetailsRebuild();
                    }, enabled: !busy, wide: true);
                }
                if (AI.Config.tab > 0 && PackageIndexingPolicy.IsIndexingEnabled(info) && info.IsIndexed && info.FileCount > 0 && (info.IsDownloaded || info.AssetSource == Asset.Source.AssetStorePackage))
                {
                    AddNativePackageAction(actions, "package.actions.reindexnextrun", "Reindex Package on Next Run", () =>
                    {
                        Assets.ForgetPackage(info, true);
                        _requireLookupUpdate = ChangeImpact.Write;
                        _requireSearchUpdate = true;
                        _requireAssetTreeRebuild = true;
                        ScheduleNativePackageDetailsRebuild();
                    }, enabled: !busy, tooltip: "Mark the package for reindexing during the next Settings action run.", wide: true);
                }
                AddNativePackageAction(actions, "package.actions.delete", "Delete Package...", () =>
                {
                    PackageDeletionUI window = PackageDeletionUI.ShowWindow();
                    window.Init(info, () =>
                    {
                        _selectedTreeAsset = null;
                        _requireLookupUpdate = ChangeImpact.Write;
                        _requireAssetTreeRebuild = true;
                        ScheduleNativePackageDetailsRebuild();
                    });
                }, enabled: !busy, destructive: true, alwaysShow: showDelete, wide: true);
            }

            if (actions.childCount == 0)
            {
                section.Add(AssetInventoryUITK.CreateMutedLabel("No actions are available for this package."));
            }
            return section;
        }

        private static bool ShouldShowPackageDataEditorAction(AssetInfo info)
        {
            if (info == null || info.ParentId > 0 || info.AssetId <= 0) return false;
            if (info.AssetSource == Asset.Source.RegistryPackage || info.AssetSource == Asset.Source.Synty) return false;

            return info.ForeignId <= 0
                || info.AssetSource == Asset.Source.CustomPackage
                || info.AssetSource == Asset.Source.Archive;
        }

        private void OpenPackageDataEditor(AssetInfo info)
        {
            PackageUI window = PackageUI.ShowWindow();
            window.Init(info, _ =>
            {
                _requireAssetTreeRebuild = true;
                UpdateStatistics(true);
                ScheduleNativePackageDetailsRebuild();
            });
        }

        private void AddNativeIndexingParticipationActions(VisualElement actions, AssetInfo info, bool busy, ref bool primaryUsed)
        {
            if (info == null || info.ParentId > 0 || info.AssetSource == Asset.Source.CurrentProject || info.SafeName == Asset.NONE) return;
            bool assetManagerDisabled = info.AssetSource == Asset.Source.AssetManager && !AI.Actions.AssetManagerEnabled;

            if (info.Exclude)
            {
                AddNativePackageAction(actions, "package.actions.restorecatalog", "Include Again", () =>
                {
                    AI.SetAssetExclusion(info, false);
                    _requireLookupUpdate = ChangeImpact.Write;
                    _requireSearchUpdate = true;
                    _requireAssetTreeRebuild = true;
                    ScheduleNativePackageDetailsRebuild();
                }, enabled: !busy, alwaysShow: true, tooltip: "Include the package and its existing indexed content in package and search views again.");

                if (!assetManagerDisabled)
                {
                    AddNativePackageAction(actions, "package.actions.restoreandindex", "Include Again & Index Now", () => IncludeAndIndexPackagesNow(new[] {info}, true),
                        enabled: !busy && CanIndexPackageNow(info),
                        primary: !primaryUsed,
                        alwaysShow: true,
                        tooltip: "Include the package again, enable future indexing, and index it now.");
                    primaryUsed = true;
                }
                return;
            }

            bool requiresInclusion = PackageIndexingPolicy.HasNoIndex(info);
            if (!assetManagerDisabled && (requiresInclusion || PackageIndexingPolicy.NeedsIndexing(info)))
            {
                string caption = requiresInclusion ? "Include & Index Now" : "Index Now";
                string tooltip = requiresInclusion
                    ? "Include this package in future indexing and index its content now."
                    : "Index this package now. It is already included in future indexing.";
                AddNativePackageAction(actions, "package.actions.includeandindex", caption, () => IncludeAndIndexPackagesNow(new[] {info}),
                    enabled: !busy && CanIndexPackageNow(info),
                    primary: !primaryUsed,
                    alwaysShow: true,
                    tooltip: tooltip);
                primaryUsed = true;
            }

            if (requiresInclusion && PackageIndexingPolicy.HasIndexedContent(info))
            {
                AddNativePackageAction(actions, "package.actions.removeindexedcontent", "Remove Indexed Content...", () => RemoveIndexedContent(new[] {info}),
                    enabled: !busy,
                    destructive: true,
                    alwaysShow: true,
                    tooltip: "Remove searchable content and generated data while keeping the package record and source archive.");
            }

            AddNativePackageAction(actions, "package.actions.hidefromcatalog", "Exclude...", () =>
            {
                if (!EditorUtility.DisplayDialog("Exclude Package", $"Exclude '{info.GetDisplayName()}' and its existing search results?\n\nYou can find it again with the Excluded maintenance filter.", "Exclude", "Cancel")) return;
                AI.SetAssetExclusion(info, true);
                _requireLookupUpdate = ChangeImpact.Write;
                _requireSearchUpdate = true;
                _requireAssetTreeRebuild = true;
                ScheduleNativePackageDetailsRebuild();
            }, enabled: !busy, tooltip: "Exclude this package from package and search views. This can be reversed.");
        }

        private void AddNativeRegistryPackageActions(VisualElement actions, AssetInfo info, bool busy, ref bool primaryUsed)
        {
            if (info.IsIndirectPackageDependency())
            {
                actions.Add(CreateNativePackageKeyedBlock("package.hints.indirectdependency", () => AssetInventoryUITK.CreateHelpBox(
                    "This is an indirect dependency. Changing its version decouples it from the dependency lifecycle and can cause compatibility issues.")));
            }

            string installedVersion = info.InstalledPackageVersion();
            string targetVersion = info.TargetPackageVersion();
            if (installedVersion != null)
            {
                if (targetVersion != null && installedVersion != targetVersion)
                {
                    string command = new SemVer(installedVersion) > new SemVer(targetVersion) ? "Downgrade" : "Update";
                    AddNativePackageAction(actions, "package.actions.update", $"{command} to {targetVersion}", () =>
                    {
                        ImportUI window = ImportUI.ShowWindow();
                        window.Init(new List<AssetInfo> {info}, true);
                    }, enabled: !busy, primary: true);
                    string changelog = info.GetChangeLogURL(targetVersion);
                    if (!string.IsNullOrWhiteSpace(changelog))
                    {
                        AddNativePackageAction(actions, null, "Open Changelog", () => AI.OpenURL(changelog));
                    }
                    primaryUsed = true;
                }
                if (info.HasSamples())
                {
                    Button samples = null;
                    samples = AddNativePackageAction(actions, "package.actions.samples", "Add/Remove Samples...", () =>
                    {
                        SampleSelectionUI.ShowDropdown(CommonUITK.ToScreenDropdownAnchor(this, samples), info);
                    });
                }
                AddNativePackageAction(actions, "package.actions.remove", "Uninstall Package", () => UninstallNativeRegistryPackage(info), enabled: !busy, destructive: true);
            }
            else if (targetVersion != null)
            {
                AddNativePackageAction(actions, "package.actions.install", $"Install Version {targetVersion}", () =>
                {
                    ImportUI window = ImportUI.ShowWindow();
                    window.Init(new List<AssetInfo> {info}, true);
                }, enabled: !busy, primary: true);
                primaryUsed = true;
            }
            else if (info.PackageSource == PackageSource.Local)
            {
                AddNativePackageAction(actions, "package.actions.install", "Install (link locally)", () =>
                {
                    ImportUI window = ImportUI.ShowWindow();
                    window.Init(new List<AssetInfo> {info}, true);
                }, enabled: !busy, primary: true, tooltip: "Link this local package into the current project.");
                AddNativePackageAction(actions, "package.actions.openlocation", "Package Location", () => ShowInExplorer(info));
                primaryUsed = true;
            }
            else if (info.PackageSource == PackageSource.Git)
            {
                AddNativePackageAction(actions, "package.actions.install", "Install Indexed Version", () => InstallPackage(info, info.LatestVersion), enabled: !busy, primary: true, tooltip: info.LatestVersion);
                primaryUsed = true;
            }

            if (info.IsFeaturePackage())
            {
                List<AssetInfo> installed = info.GetInstalledFeaturePackageContent(_assets);
                if (installed.Count > 0)
                {
                    string packageLabel = installed.Count == 1 ? "Package" : "Packages";
                    AddNativePackageAction(actions, "package.actions.remove", $"Uninstall {installed.Count} {packageLabel}", () =>
                    {
                        RemovalUI window = RemovalUI.ShowWindow();
                        window.Init(installed);
                    }, enabled: !busy, destructive: true);
                }
            }
        }

        private static void UninstallNativeRegistryPackage(AssetInfo info)
        {
            PackageInfo packageInfo = AssetStore.GetPackageInfo(info);
            if (packageInfo == null) return;
            if (packageInfo.source == PackageSource.Embedded)
            {
                FileUtil.DeleteFileOrDirectory(packageInfo.resolvedPath);
                AssetDatabase.Refresh();
            }
            else
            {
                Client.Remove(packageInfo.name);
            }
            AssetStore.GatherProjectMetadata();
        }

        private void AddNativeImportedPackageActions(VisualElement actions, AssetInfo info, bool busy, ref bool primaryUsed)
        {
            bool downloadable = !info.IsDownloaded && IsOnDemandPackageSource(info) && HasAssetStoreDownloadMetadata(info);
            if (info.AssetSource != Asset.Source.Directory)
            {
                if (info.IsDownloaded && (info.IsUpdateAvailable(_assets) || info.WasOutdated || !info.IsDownloadedCompatible))
                {
                    actions.Add(CreateNativePackageDownload(info, true));
                }
                if (AssetStore.IsInstalled(info) || (_usedPackages != null && _usedPackages.ContainsKey(info.AssetId)))
                {
                    AddNativePackageAction(actions, "package.actions.remove", "Uninstall Package...", () =>
                    {
                        UninstallPackageUI.ShowWindow().Init(info, _usedPackages != null && _usedPackages.ContainsKey(info.AssetId) ? _usedPackages[info.AssetId] : null);
                    }, enabled: !busy, destructive: true);
                }
                else
                {
                    string caption = downloadable ? "Import Package..." : "Import Package...";
                    AddNativePackageAction(actions, "package.actions.import", caption, () =>
                    {
                        ImportUI window = ImportUI.ShowWindow();
                        window.Init(new List<AssetInfo> {info});
                    }, enabled: !busy, primary: true, tooltip: downloadable ? "Download the package, then open the import dialog." : "Open the import dialog.");
                    primaryUsed = true;
                    if (!downloadable) AddNativePackageAction(actions, "package.actions.openlocation", "Package Location", () => ShowInExplorer(info));
                }
            }
            else
            {
                string location = info.AssetSource == Asset.Source.Archive ? "Archive" : "Directory";
                AddNativePackageAction(actions, "package.actions.openlocation", $"Open {location} Location...", () => ShowInExplorer(info));
            }
        }

        private VisualElement CreateNativePackageDownload(AssetInfo info, bool updateMode = false)
        {
            VisualElement root = new VisualElement();
            root.AddToClassList(PackagesDetailActionItemClass);
            AssetInfo packageRoot = info.GetRoot();
            bool hasDownloadMetadata = HasAssetStoreDownloadMetadata(packageRoot);
            if (hasDownloadMetadata)
            {
                if (!updateMode)
                {
                    if (packageRoot.IsLocationUnmappedRelative()) root.Add(AssetInventoryUITK.CreateHelpBox("The package uses a relative location with no mapping for this system.", MessageType.Warning));
                    else if (string.IsNullOrWhiteSpace(packageRoot.DownloadedActual)) root.Add(AssetInventoryUITK.CreateHelpBox(packageRoot.PackageSize > 0 ? $"Not cached. Download {EditorUtility.FormatBytes(packageRoot.PackageSize)} to access its content." : "Not cached. Download the package to access its content.", MessageType.Warning));
                    else root.Add(AssetInventoryUITK.CreateHelpBox($"The cache contains version {packageRoot.DownloadedActual} of another listing. Download this package to replace it.", MessageType.Warning));
                }
                if (!packageRoot.IsDownloadedCompatible)
                {
                    string message = packageRoot.IsCurrentUnitySupported()
                        ? $"The cached package targets newer Unity {packageRoot.DownloadededUnityVersion} and may be incompatible. Download again to obtain a compatible version when available."
                        : $"The cached package targets newer Unity {packageRoot.DownloadededUnityVersion} and may be incompatible.";
                    root.Add(CreateNativePackageKeyedBlock("package.hints.incompatibledownload", () => AssetInventoryUITK.CreateHelpBox(message, MessageType.Warning)));
                }

                if (packageRoot.ParentId == 0 && packageRoot.PackageDownloader == null) AI.GetObserver().Attach(packageRoot);
                if (packageRoot.ParentId == 0 && packageRoot.PackageDownloader != null)
                {
                    AssetDownloadState state = packageRoot.PackageDownloader.GetState();
                    switch (state.state)
                    {
                        case AssetDownloader.State.Downloading:
                            root.AddToClassList(PackagesDetailWideActionClass);
                            root.Add(AssetInventoryUITK.CreateProgressBar(EditorUtility.FormatBytes(state.bytesDownloaded), state.progress));
                            VisualElement controls = new VisualElement();
                            controls.AddToClassList(PackagesDetailActionsClass);
                            controls.AddToClassList(PackagesDetailActionGridClass);
                            AddNativePackageAction(controls, null, "Pause", () =>
                            {
                                packageRoot.PackageDownloader.PauseDownload(false);
                                ScheduleNativePackageDetailsRebuild();
                            });
                            AddNativePackageAction(controls, null, "Abort", () =>
                            {
                                packageRoot.PackageDownloader.PauseDownload(true);
                                ScheduleNativePackageDetailsRebuild();
                            }, destructive: true);
                            root.Add(controls);
                            break;
                        case AssetDownloader.State.Unavailable:
                            if (packageRoot.PackageDownloader.IsDownloadSupported()) root.Add(AssetInventoryUITK.CreatePrimaryButton("Download", () => StartNativePackageDownload(packageRoot, false)));
                            break;
                        case AssetDownloader.State.Paused:
                            if (packageRoot.PackageDownloader.IsDownloadSupported()) root.Add(AssetInventoryUITK.CreatePrimaryButton("Resume Download", () => StartNativePackageDownload(packageRoot, false)));
                            break;
                        case AssetDownloader.State.UpdateAvailable:
                            if (packageRoot.PackageDownloader.IsDownloadSupported()) root.Add(AssetInventoryUITK.CreatePrimaryButton("Download Update", () => StartNativePackageDownload(packageRoot, true)));
                            break;
                        case AssetDownloader.State.Downloaded:
                            if (!packageRoot.IsDownloadedCompatible && packageRoot.IsCurrentUnitySupported() && packageRoot.PackageDownloader.IsDownloadSupported())
                            {
                                root.Add(AssetInventoryUITK.CreatePrimaryButton("Download", () => StartNativePackageDownload(packageRoot, false)));
                            }
                            break;
                    }
                }
            }
            else if (!updateMode)
            {
                if (info.IsLocationUnmappedRelative()) root.Add(AssetInventoryUITK.CreateHelpBox("The package uses a relative location with no mapping for this system.", MessageType.Warning));
                else if (info.AssetSource == Asset.Source.CustomPackage && !File.Exists(info.GetLocation(true))) root.Add(AssetInventoryUITK.CreateHelpBox("The custom package was deleted and is no longer available.", MessageType.Warning));
                else if (info.AssetSource == Asset.Source.Synty)
                {
                    root.Add(AssetInventoryUITK.CreateHelpBox("This package is not present in the local Synty Importer cache. Download it in the official importer, then run Index Synty Importer Cache.", MessageType.Info));
                }
                else
                {
                    root.Add(AssetInventoryUITK.CreateHelpBox("Metadata has not been collected yet. Update the Asset Store catalog to load current package information.", MessageType.Warning));
                    root.Add(AssetInventoryUITK.CreateSecondaryButton("Load Metadata", () => _ = RefreshNativePackageMetadataAsync(info)));
                }
            }
            else if (info.AssetSource == Asset.Source.CustomPackage)
            {
                root.AddToClassList(PackagesDetailWideActionClass);
                root.Add(CreateNativePackageKeyedBlock("package.hints.noautoupdate", () => AssetInventoryUITK.CreateHelpBox("Automatic update is unavailable for local custom packages.")));
            }
            return root;
        }

        private void StartNativePackageDownload(AssetInfo info, bool update)
        {
            if (info.PackageDownloader == null) AI.GetObserver().Attach(info);
            if (info.PackageDownloader == null || !info.PackageDownloader.IsDownloadSupported()) return;
            if (update)
            {
                info.WasOutdated = true;
                info.PackageDownloader.SetAsset(info);
            }
            info.PackageDownloader.Download(false);
            ScheduleNativePackageDetailsRebuild();
        }

        private static Task RefreshNativePackageMetadataAsync(AssetInfo info)
        {
            return AI.Actions.FetchAssetDetails(true, info?.AssetId ?? 0);
        }

        private Button AddNativePackageAction(
            VisualElement parent,
            string key,
            string text,
            Action click,
            bool enabled = true,
            bool primary = false,
            bool destructive = false,
            bool alwaysShow = false,
            string tooltip = null,
            string trailingIconName = null,
            bool wide = false)
        {
            Button button = destructive
                ? AssetInventoryUITK.CreateDestructiveButton(text, click)
                : primary
                    ? AssetInventoryUITK.CreatePrimaryButton(text, click)
                    : AssetInventoryUITK.CreateSecondaryButton(text, click);
            button.SetEnabled(enabled);
            button.tooltip = tooltip ?? text;
            Texture icon = string.IsNullOrWhiteSpace(trailingIconName)
                ? null
                : EditorGUIUtility.IconContent(trailingIconName).image;
            if (icon != null)
            {
                button.text = string.Empty;
                button.AddToClassList(PackagesDetailActionWithIconClass);
                button.Add(CommonUITK.CreateLabel(text, PackagesDetailActionTextClass));
                Image image = new Image
                {
                    image = icon,
                    scaleMode = ScaleMode.ScaleToFit
                };
                image.AddToClassList(PackagesDetailActionIconClass);
                button.Add(image);
            }
            if (primary) button.AddToClassList(PackagesDetailPrimaryActionClass);
            if (destructive) button.AddToClassList(PackagesDetailDestructiveActionClass);
            VisualElement actionItem;
            if (string.IsNullOrWhiteSpace(key))
            {
                if (parent.ClassListContains(PackagesDetailActionGridClass))
                {
                    actionItem = new VisualElement();
                    actionItem.Add(button);
                }
                else
                {
                    actionItem = button;
                }
            }
            else
            {
                actionItem = CreateNativePackageKeyedBlock(key, () => button, alwaysShow);
            }
            actionItem.AddToClassList(PackagesDetailActionItemClass);
            if (wide) actionItem.AddToClassList(PackagesDetailWideActionClass);
            parent.Add(actionItem);
            return button;
        }

        private VisualElement CreateNativePackageTagsSection(List<AssetInfo> infos, List<TagInfo> tags, Dictionary<string, Tuple<int, Color>> bulkTags)
        {
            if (infos == null || infos.Count == 0) return null;
            VisualElement section = AssetInventoryUITK.CreateSection("Tags");
            section.AddToClassList(PackagesDetailSectionClass);
            VisualElement tagList = new VisualElement();
            tagList.AddToClassList(PackagesDetailTagsClass);

            Button add = null;
            add = AssetInventoryUITK.CreateSecondaryButton("Add Tag...", () =>
            {
                TagSelectionUI.ShowDropdown(
                    this,
                    add,
                    TagAssignment.Target.Package,
                    infos,
                    () =>
                    {
                        _requireAssetTreeRebuild = true;
                        ScheduleNativePackageDetailsRebuild();
                    });
            });
            tagList.Add(add);

            if (bulkTags != null)
            {
                foreach (KeyValuePair<string, Tuple<int, Color>> entry in bulkTags.OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase))
                {
                    string tagName = entry.Key;
                    VisualElement pill = CreateNativePackageTagPill($"{tagName} ({entry.Value.Item1:N0})", entry.Value.Item2, tagName, () =>
                    {
                        Tagging.RemovePackageAssignments(infos, tagName, true);
                        _requireAssetTreeRebuild = true;
                        ScheduleNativePackageDetailsRebuild();
                    });
                    tagList.Add(pill);
                }
            }
            else if (tags != null)
            {
                foreach (TagInfo tag in tags.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
                {
                    TagInfo captured = tag;
                    VisualElement pill = CreateNativePackageTagPill(tag.Name, tag.GetColor(), tag.Name, () =>
                    {
                        Tagging.RemoveAssignment(infos[0], captured);
                        _requireAssetTreeRebuild = true;
                        ScheduleNativePackageDetailsRebuild();
                    });
                    tagList.Add(pill);
                }
            }
            section.Add(tagList);
            return section;
        }

        private VisualElement CreateNativePackageTagPill(string text, Color color, string tooltip, Action remove)
        {
            VisualElement pill = AssetInventoryUITK.CreateRemovablePill(
                text,
                "Remove tag " + tooltip,
                remove,
                PackagesDetailTagClass);
            pill.style.backgroundColor = new StyleColor(new Color(color.r, color.g, color.b, 0.25f));
            return pill;
        }

        private VisualElement CreateNativeBulkPackageDetails(
            List<AssetInfo> bulkAssets,
            long bulkSubAssetCount,
            Dictionary<string, Tuple<int, Color>> bulkTags,
            long size,
            float totalCosts,
            float storeCosts)
        {
            VisualElement root = new VisualElement();
            VisualElement summary = AssetInventoryUITK.CreateSection("Selection");
            summary.AddToClassList(PackagesDetailSectionClass);
            AddNativePackageDetailRow(summary, "package.bulk.count", "Selected Items", $"{bulkAssets.Count - bulkSubAssetCount:N0}");
            if (bulkSubAssetCount > 0) AddNativePackageDetailRow(summary, "package.bulk.childcount", "Sub-Packages", $"{bulkSubAssetCount:N0}");
            AddNativePackageDetailRow(summary, "package.bulk.size", "Size on Disk", EditorUtility.FormatBytes(size));
            if (totalCosts > 0) AddNativePackageDetailRow(summary, "package.bulk.price", "Total Price", bulkAssets[0].GetPriceText(totalCosts));
            if (storeCosts > 0 && totalCosts > storeCosts)
            {
                AddNativePackageDetailRow(summary, "package.bulk.storeprice", "Asset Store", bulkAssets[0].GetPriceText(storeCosts));
                AddNativePackageDetailRow(summary, "package.bulk.otherprice", "Other Sources", bulkAssets[0].GetPriceText(totalCosts - storeCosts));
            }
            int selectedRootCount = bulkAssets.Count(info => info != null && info.ParentId <= 0);
            int visibleRootCount = GetSelectableRootPackages().Count;
            if (visibleRootCount > selectedRootCount)
            {
                summary.Add(AssetInventoryUITK.CreateSecondaryButton($"Select All {visibleRootCount:N0} Results", SelectAllVisiblePackages));
            }
            root.Add(summary);

            VisualElement actions = AssetInventoryUITK.CreateSection("Bulk Actions");
            actions.AddToClassList(PackagesDetailSectionClass);
            UpdateObserver observer = AI.GetObserver();
            if (!observer.PrioInitializationDone)
            {
                int progress = Mathf.RoundToInt(observer.PrioInitializationProgress * 100f);
                actions.Add(AssetInventoryUITK.CreateHelpBox($"Gathering data (*): {progress}%"));
            }

            AddNativeBulkChoice(actions, "package.bulk.actions.extract", "Keep Cached", "Keep selected packages extracted in the cache.",
                () => bulkAssets.ForEach(info => AI.SetAssetExtraction(info, true)),
                () => bulkAssets.ForEach(info => AI.SetAssetExtraction(info, false)));
            if (AI.Actions.PackageBackupsEnabled)
            {
                AddNativeBulkChoice(actions, "package.bulk.actions.backup", "Backup", "Create backups for selected packages whenever the package backup action runs.",
                    () => { bulkAssets.ForEach(info => AI.SetAssetBackup(info, true, false)); AI.TriggerPackageRefresh(); },
                    () => { bulkAssets.ForEach(info => AI.SetAssetBackup(info, false, false)); AI.TriggerPackageRefresh(); });
            }
            AddNativeBulkIndexingChoices(actions, bulkAssets);
            if (AI.Actions.AICaptionsEnabled)
            {
                AddNativeBulkChoice(actions, "package.bulk.actions.aiusage", "AI Captions", "Create AI captions for selected packages whenever the caption action runs.",
                    () => { bulkAssets.ForEach(info => AI.SetAssetAIUse(info, true, false)); AI.TriggerPackageRefresh(); },
                    () => { bulkAssets.ForEach(info => AI.SetAssetAIUse(info, false, false)); AI.TriggerPackageRefresh(); });
            }
            if (AI.Actions.SemanticSearchEnabled)
            {
                AddNativeBulkChoice(actions, "package.bulk.actions.semanticindex", "Semantic Index", "Include selected packages when updating the semantic asset index.",
                    () => { bulkAssets.ForEach(info => AI.SetAssetSemanticIndexUse(info, true, false)); AI.TriggerPackageRefresh(); },
                    () => { bulkAssets.ForEach(info => AI.SetAssetSemanticIndexUse(info, false, false)); AI.TriggerPackageRefresh(); });
            }
            if (AI.Actions.CodeSearchEnabled)
            {
                AddNativeBulkChoice(actions, "package.bulk.actions.codeindex", "Code Index", "Include selected packages when updating the code search index.",
                    () => { bulkAssets.ForEach(info => AI.SetAssetCodeIndexUse(info, true, false)); AI.TriggerPackageRefresh(); },
                    () => { bulkAssets.ForEach(info => AI.SetAssetCodeIndexUse(info, false, false)); AI.TriggerPackageRefresh(); });
            }

            VisualElement commands = new VisualElement();
            commands.AddToClassList(PackagesDetailBulkCommandsClass);
            AddNativeBulkDownloadActions(commands, bulkAssets, observer);
            AddNativeBulkIndexingActions(commands, bulkAssets);
            AddNativeBulkCommandActions(commands, bulkAssets);
            actions.Add(commands);
            root.Add(actions);

            VisualElement tags = CreateNativePackageTagsSection(bulkAssets, null, bulkTags);
            if (tags != null) root.Add(CreateNativePackageKeyedBlock("package.bulk.actions.tag", () => tags));
            return root;
        }

        private void AddNativeBulkIndexingChoices(VisualElement parent, List<AssetInfo> assets)
        {
            List<AssetInfo> roots = GetNativeBulkPackageRoots(assets);
            if (roots.Count == 0) return;

            AddNativeBulkChoice(parent, "package.bulk.actions.disablefutureindexing", "Do Not Index", "Skip selected packages in future indexing runs. Existing indexed content is kept.",
                () => SetFutureIndexing(roots, false),
                () => SetFutureIndexing(roots, true));
            AddNativeBulkChoice(parent, "package.bulk.actions.hidefromcatalog", "Exclude", "Hide selected packages and their existing results from package and search views. Use None to include them again.",
                () => SetNativeBulkPackageExclusion(roots, true),
                () => SetNativeBulkPackageExclusion(roots, false));
        }

        private void AddNativeBulkIndexingActions(VisualElement parent, List<AssetInfo> assets)
        {
            List<AssetInfo> roots = GetNativeBulkPackageRoots(assets);
            if (roots.Count == 0) return;

            List<AssetInfo> indexTargets = roots.Where(CanIndexPackageNow).ToList();
            if (indexTargets.Count > 0)
            {
                bool hasExcluded = indexTargets.Any(info => info.Exclude);
                bool hasNoIndex = indexTargets.Any(PackageIndexingPolicy.HasNoIndex);
                string indexCaption = hasExcluded
                    ? "Include Again & Index Selected Now"
                    : hasNoIndex
                        ? "Include & Index Selected Now"
                        : "Index Selected Now";
                string indexTooltip = hasExcluded
                    ? "Include the selected packages again, enable future indexing, and index them now."
                    : hasNoIndex
                        ? "Enable future indexing for the selected packages and index them now."
                        : "Index only the selected packages now.";
                AddNativePackageAction(parent, "package.bulk.actions.includeandindex", indexCaption, () => IncludeAndIndexPackagesNow(indexTargets, hasExcluded),
                    enabled: !AI.Actions.ActionsInProgress,
                    primary: true,
                    alwaysShow: true,
                    tooltip: indexTooltip);
            }

            List<AssetInfo> cleanupTargets = roots
                .Where(info => PackageIndexingPolicy.HasNoIndex(info) && PackageIndexingPolicy.HasIndexedContent(info))
                .ToList();
            if (cleanupTargets.Count > 0)
            {
                AddNativePackageAction(parent, "package.bulk.actions.removeindexedcontent", "Remove Indexed Content...", () => RemoveIndexedContent(cleanupTargets),
                    enabled: !AI.Actions.ActionsInProgress,
                    destructive: true,
                    alwaysShow: true,
                    tooltip: "Remove searchable content and generated data while keeping package records and source archives.");
            }
        }

        private static List<AssetInfo> GetNativeBulkPackageRoots(IEnumerable<AssetInfo> assets)
        {
            return assets?
                .Where(info => info != null && info.ParentId <= 0 && info.AssetSource != Asset.Source.CurrentProject && info.SafeName != Asset.NONE)
                .GroupBy(info => info.AssetId)
                .Select(group => group.First())
                .ToList() ?? new List<AssetInfo>();
        }

        private void AddNativeBulkChoice(VisualElement parent, string key, string label, string tooltip, Action all, Action none)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(PackagesDetailRowClass);
            row.AddToClassList(PackagesDetailBulkChoiceClass);
            Label title = AssetInventoryUITK.CreateLabel(label, PackagesDetailLabelClass);
            title.tooltip = tooltip ?? label;
            row.Add(title);
            VisualElement controls = new VisualElement();
            controls.AddToClassList(PackagesDetailActionsClass);
            Button allButton = AssetInventoryUITK.CreateSecondaryButton("All", () => { all?.Invoke(); ScheduleNativePackageDetailsRebuild(); });
            allButton.tooltip = $"Set {label} for all selected packages.";
            controls.Add(allButton);
            Button noneButton = AssetInventoryUITK.CreateSecondaryButton("None", () => { none?.Invoke(); ScheduleNativePackageDetailsRebuild(); });
            noneButton.tooltip = $"Clear {label} for all selected packages.";
            controls.Add(noneButton);
            row.Add(controls);
            parent.Add(CreateNativePackageKeyedBlock(key, () => row));
        }

        private void SetNativeBulkPackageExclusion(List<AssetInfo> assets, bool excluded)
        {
            assets.Where(info => info != null && info.ParentId <= 0).ToList().ForEach(info => AI.SetAssetExclusion(info, excluded));
            _requireLookupUpdate = ChangeImpact.Write;
            _requireSearchUpdate = true;
            _requireAssetTreeRebuild = true;
        }

        private void AddNativeBulkDownloadActions(VisualElement parent, List<AssetInfo> assets, UpdateObserver observer)
        {
            BulkPackageDownloadSummary summary = CalculateBulkPackageDownloadSummary(assets, _assets);
            string initializing = observer.PrioInitializationDone ? string.Empty : "*";
            if (summary.NotDownloaded > 0)
            {
                AddNativePackageAction(parent, null, $"Download remaining {summary.NotDownloaded}", () =>
                {
                    List<AssetInfo> targets = assets.Where(asset => IsBulkAssetStoreDownloadTarget(asset, asset?.PackageDownloader?.GetState().state)).ToList();
                    StartBulkPackageDownloads(targets, false);
                    ScheduleNativePackageDetailsRebuild();
                });
            }
            if (summary.UpdateAvailable > 0)
            {
                AddNativePackageAction(parent, null, $"Download {summary.UpdateAvailable} update{(summary.UpdateAvailable == 1 ? string.Empty : "s")}", () =>
                {
                    List<AssetInfo> targets = assets.Where(asset => IsBulkAssetStoreUpdateTarget(asset, _assets, asset?.PackageDownloader?.GetState().state)).ToList();
                    StartBulkPackageDownloads(targets, true);
                    ScheduleNativePackageDetailsRebuild();
                }, primary: true);
            }
            if (summary.PackageUpdateAvailable > 0)
            {
                AddNativePackageAction(parent, "package.bulk.actions.import", $"Update {summary.PackageUpdateAvailable} registry packages", () =>
                {
                    ImportUI window = ImportUI.ShowWindow();
                    window.Init(assets.Where(asset => asset.AssetSource == Asset.Source.RegistryPackage && asset.IsUpdateAvailable()).ToList(), true);
                });
            }
            if (summary.UpdateAvailableButCustom > 0)
            {
                parent.Add(AssetInventoryUITK.CreateHelpBox($"{summary.UpdateAvailableButCustom}{initializing} updates cannot run because the assets are local custom packages."));
            }
            NativeBulkDownloadStatusElements status = CreateNativeBulkDownloadStatus(parent);
            RefreshNativeBulkDownloadStatus(status);
            parent.schedule.Execute(() => RefreshNativeBulkDownloadStatus(status)).Every(250);
        }

        private NativeBulkDownloadStatusElements CreateNativeBulkDownloadStatus(VisualElement parent)
        {
            NativeBulkDownloadStatusElements status = new NativeBulkDownloadStatusElements
            {
                DownloadingRow = CreateNativePackageDetailRow("Downloading", string.Empty),
                RemainingRow = CreateNativePackageDetailRow("Remaining", string.Empty),
                PausedRow = CreateNativePackageDetailRow("Paused", string.Empty)
            };
            status.DownloadingLabel = status.DownloadingRow.Q<Label>(className: PackagesDetailLabelClass);
            status.DownloadingValue = status.DownloadingRow.Q<Label>(className: PackagesDetailValueClass);
            status.RemainingLabel = status.RemainingRow.Q<Label>(className: PackagesDetailLabelClass);
            status.RemainingValue = status.RemainingRow.Q<Label>(className: PackagesDetailValueClass);
            status.PausedLabel = status.PausedRow.Q<Label>(className: PackagesDetailLabelClass);
            status.PausedValue = status.PausedRow.Q<Label>(className: PackagesDetailValueClass);
            parent.Add(status.DownloadingRow);
            parent.Add(status.RemainingRow);
            parent.Add(status.PausedRow);
            return status;
        }

        private void RefreshNativeBulkDownloadStatus(NativeBulkDownloadStatusElements status)
        {
            if (status == null) return;

            BulkPackageDownloadSummary summary = CalculateBulkPackageDownloadSummary(_selectedTreeAssets, _assets);
            string initializing = AI.GetObserver().PrioInitializationDone ? string.Empty : "*";
            SetNativeBulkDownloadStatusRow(
                status.DownloadingRow,
                status.DownloadingLabel,
                status.DownloadingValue,
                "Downloading" + initializing,
                $"{summary.Downloading:N0}",
                summary.Downloading > 0);
            SetNativeBulkDownloadStatusRow(
                status.RemainingRow,
                status.RemainingLabel,
                status.RemainingValue,
                "Remaining" + initializing,
                EditorUtility.FormatBytes(Math.Max(0, summary.RemainingBytes)),
                summary.Downloading > 0);
            SetNativeBulkDownloadStatusRow(
                status.PausedRow,
                status.PausedLabel,
                status.PausedValue,
                "Paused",
                $"{summary.Paused:N0}",
                summary.Paused > 0);
        }

        private static void SetNativeBulkDownloadStatusRow(
            VisualElement row,
            Label label,
            Label value,
            string labelText,
            string valueText,
            bool visible)
        {
            if (row == null || label == null || value == null) return;
            label.text = labelText;
            value.text = valueText;
            row.tooltip = valueText;
            row.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void AddNativeBulkCommandActions(VisualElement parent, List<AssetInfo> assets)
        {
            int packageCount = assets.Count(asset => asset.AssetSource == Asset.Source.RegistryPackage);
            int installedCount = assets.Count(asset => asset.AssetSource == Asset.Source.RegistryPackage && asset.InstalledPackageVersion() != null);
            string importCaption = packageCount == assets.Count ? "Install..." : packageCount > 0 ? "Import & Install..." : "Import...";
            if (assets.Count > installedCount)
            {
                AddNativePackageAction(parent, "package.bulk.actions.import", importCaption, () =>
                {
                    ImportUI window = ImportUI.ShowWindow();
                    window.Init(assets);
                }, primary: true);
            }
            if (installedCount > 0)
            {
                AddNativePackageAction(parent, "package.bulk.actions.uninstall", $"Uninstall {installedCount} Package{(installedCount == 1 ? string.Empty : "s")}", () =>
                {
                    if (!EditorUtility.DisplayDialog("Confirm", $"Uninstall {installedCount} packages?", "OK", "Cancel")) return;
                    RemovalUI window = RemovalUI.ShowWindow();
                    window.Init(assets.Where(asset => asset.AssetSource == Asset.Source.RegistryPackage && asset.InstalledPackageVersion() != null).ToList());
                }, destructive: true);
            }
            AddNativePackageAction(parent, "package.bulk.actions.openlocation", "Open Package Locations...", () => OpenNativeBulkPackageLocations(assets));
            AddNativePackageAction(parent, "package.bulk.actions.refreshmetadata", "Refresh Metadata", () => RefreshMetadataAsync(assets));
            AddNativePackageAction(parent, "package.bulk.actions.recreatepreviews", "Previews Wizard...", () =>
            {
                PreviewWizardUI window = PreviewWizardUI.ShowWindow();
                window.Init(assets, _assets);
            });
            AddNativePackageAction(parent, "package.bulk.actions.export", "Export Packages...", () =>
            {
                ExportUI window = ExportUI.ShowWindow();
                window.Init(assets, false, 0, assetColumnState.VisibleColumns);
            });
            AddNativePackageAction(parent, "package.bulk.actions.reindexnextrun", "Reindex Packages on Next Run", () =>
            {
                if (!EditorUtility.DisplayDialog("Confirm", $"Reindex {assets.Count} packages during the next action run?", "OK", "Cancel")) return;
                assets.ForEach(info => Assets.ForgetPackage(info, true));
                _requireLookupUpdate = ChangeImpact.Write;
                _requireSearchUpdate = true;
                _requireAssetTreeRebuild = true;
                ScheduleNativePackageDetailsRebuild();
            });
            AddNativePackageAction(parent, "package.bulk.actions.delete", "Delete Packages...", () =>
            {
                PackageDeletionUI window = PackageDeletionUI.ShowWindow(true);
                window.Init(assets, () =>
                {
                    _selectedTreeAsset = null;
                    _requireLookupUpdate = ChangeImpact.Write;
                    _requireAssetTreeRebuild = true;
                    _requireSearchUpdate = true;
                    ScheduleNativePackageDetailsRebuild();
                });
            }, destructive: true);
        }

        private static void OpenNativeBulkPackageLocations(List<AssetInfo> assets)
        {
            List<AssetInfo> roots = assets.Where(info => info.ParentId <= 0).ToList();
            if (roots.Count > AI.Config.massOpenWarnThreshold
                && !EditorUtility.DisplayDialog("Open Locations", $"Open {roots.Count} package locations? This can open many windows.", "Continue", "Cancel"))
            {
                return;
            }
            roots.ForEach(info => EditorUtility.RevealInFinder(info.GetLocation(true)));
        }

        private void ScheduleNativePackageDetailsRebuild()
        {
            _nativePackageInspectorContentStateHash = int.MinValue;
            _nativePackageInspectorPane?.schedule.Execute(RefreshNativePackageInspector).ExecuteLater(0);
        }

        private static string FormatNativePackageSource(AssetInfo info)
        {
            AssetInfo root = info.GetRoot();
            switch (root.AssetSource)
            {
                case Asset.Source.AssetStorePackage:
                    return "Asset Store";
                case Asset.Source.Synty:
                    return "Synty Importer";
                case Asset.Source.RegistryPackage:
                    if (root.ForeignId > 0) return "Asset Store";
                    if (info.IsFeaturePackage()) return "Unity Feature (Package Bundle)";
                    return $"{StringUtils.CamelCaseToWords(info.AssetSource.ToString())} ({info.PackageSource})";
                default:
                    return StringUtils.CamelCaseToWords(info.AssetSource.ToString());
            }
        }

        private static string FormatNativePackageIndexingStatus(PackageIndexingStatus status)
        {
            switch (status)
            {
                case PackageIndexingStatus.Excluded:
                    return "Excluded";
                case PackageIndexingStatus.Pausing:
                    return "Pausing";
                case PackageIndexingStatus.NotIncluded:
                    return "Not Included";
                case PackageIndexingStatus.IndexedWithoutFutureIndexing:
                    return "Indexed, Future Indexing Off";
                case PackageIndexingStatus.Indexing:
                    return "Indexing";
                case PackageIndexingStatus.Incomplete:
                    return "Indexing Incomplete";
                case PackageIndexingStatus.NeedsIndexing:
                    return "Needs Indexing";
                case PackageIndexingStatus.Indexed:
                    return "Indexed";
                default:
                    return status.ToString();
            }
        }

        private static string GetNativePackageIndexingStatusTooltip(PackageIndexingStatus status)
        {
            switch (status)
            {
                case PackageIndexingStatus.Excluded:
                    return "The package and any existing indexed content are excluded from package and search views.";
                case PackageIndexingStatus.Pausing:
                    return "Future indexing was disabled while this package was in progress. The current operation is stopping at a safe boundary.";
                case PackageIndexingStatus.NotIncluded:
                    return "The package is not included in future indexing and has no indexed content.";
                case PackageIndexingStatus.IndexedWithoutFutureIndexing:
                    return "Existing indexed content is retained, but future indexing runs skip this package.";
                case PackageIndexingStatus.Indexing:
                    return "The package is currently being indexed.";
                case PackageIndexingStatus.Incomplete:
                    return "Indexing started previously but did not finish. You can resume it now.";
                case PackageIndexingStatus.NeedsIndexing:
                    return "The package participates in indexing but does not have indexed content yet.";
                case PackageIndexingStatus.Indexed:
                    return "The package participates in future indexing and has indexed content.";
                default:
                    return string.Empty;
            }
        }
    }
}
