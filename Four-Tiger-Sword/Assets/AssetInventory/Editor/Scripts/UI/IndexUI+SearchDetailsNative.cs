using ImpossibleRobert.Common;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;
using Image = UnityEngine.UIElements.Image;
using Label = UnityEngine.UIElements.Label;
using Object = UnityEngine.Object;
using PopupStringField = UnityEngine.UIElements.PopupField<string>;
using ScrollView = UnityEngine.UIElements.ScrollView;
using Toggle = UnityEngine.UIElements.Toggle;
using VisualElement = UnityEngine.UIElements.VisualElement;

namespace AssetInventory
{
    public partial class IndexUI
    {
        private const string SearchInspectorClass = "ai-search-inspector";
        private const string SearchInspectorSettingsActiveClass = "ai-search-inspector-settings-active";
        private const string SearchDetailPreviewClass = "ai-search-detail-preview";
        private const string SearchDetailActionsClass = "ai-search-detail-actions";
        private const string SearchDetailAudioBlockClass = "ai-search-detail-audio-block";
        private const string SearchDetailAudioControlsClass = "ai-search-detail-audio-controls";
        private const string SearchDetailAudioTransportClass = "ai-search-detail-audio-transport";
        private const string SearchDetailAudioOptionsClass = "ai-search-detail-audio-options";
        private const string SearchDetailAudioButtonClass = "ai-search-detail-audio-button";
        private const string SearchDetailAudioScrubberClass = "ai-search-detail-audio-scrubber";
        private const string SearchDetailComparisonClass = "ai-search-detail-comparison";
        private const string SearchDetailComparisonOperatorClass = "ai-search-detail-comparison-operator";
        private const string SearchDetailCompactFieldClass = "ai-search-detail-compact-field";
        private const string SearchDetailInlineValueClass = "ai-search-detail-inline-value";
        private const string SearchDetailInlineActionClass = "ai-search-detail-inline-action";
        private const string SearchDownloadActionIcon = "icon dropdown";
        private const string SearchScopeGroupClass = "ai-search-scope-group";
        private const string SearchScopeFileClass = "ai-search-scope-file";
        private const string SearchScopePackageClass = "ai-search-scope-package";
        private const string SearchScopeHeaderClass = "ai-search-scope-header";
        private const string SearchScopeIconClass = "ai-search-scope-icon";
        private const string SearchScopeKindClass = "ai-search-scope-kind";
        private const string SearchScopeBodyClass = "ai-search-scope-body";

        private CommonTabbedPane _nativeSearchInspectorPane;
        private Button _nativeSearchInspectorSettingsButton;
        private ScrollView _nativeSearchInspectorScroll;
        private int _nativeSearchInspectorScrollTab = int.MinValue;
        private int _nativeSearchInspectorContentStateHash = int.MinValue;
#if !AUDIO_TOOL_NOAUDIO
        private Slider _nativeSearchAudioScrubber;
#endif

        private void PositionNativeSearchResult(VisualElement element)
        {
            if (element == null) return;

            element.style.position = UnityEngine.UIElements.Position.Absolute;
            element.style.left = 0f;
            element.style.top = 0f;
            element.style.bottom = 0f;
            element.style.right = 0f;
        }

        private CommonTabbedPane CreateNativeSearchInspectorPane()
        {
            CommonTabbedPane pane = AssetInventoryUITK.CreateTabbedInspectorPane();
            pane.AddToClassList(PackagesInspectorClass);
            pane.AddToClassList(SearchInspectorClass);
            pane.SetTabs(GetNativeSearchInspectorTabs(), GetNativeSearchInspectorTabIndex(), SelectNativeSearchInspectorTab);

            pane.Trailing.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("search.actions.settings", () =>
            {
                _nativeSearchInspectorSettingsButton = AssetInventoryUITK.CreateIconButton(
                    "Manage View",
                    "Settings",
                    SelectNativeSearchInspectorSettings);
                _nativeSearchInspectorSettingsButton.AddToClassList(PackagesInspectorHeaderButtonClass);
                return _nativeSearchInspectorSettingsButton;
            }, inlineControls: true, onVisibilityChanged: RebuildNativeSearchBody));

            RebuildNativeSearchInspectorContent();
            pane.schedule.Execute(RefreshNativeSearchInspector).Every(350);
            return pane;
        }

        private string[] GetNativeSearchInspectorTabs()
        {
            int activeFilterCount = GetActiveSearchFilterCount();
            string filtersLabel = activeFilterCount > 0 ? $"Filters ({activeFilterCount:N0})" : "Filters";
            return new[]
            {
                "Details",
                filtersLabel
            };
        }

        private int GetNativeSearchInspectorTabIndex()
        {
            return _searchInspectorTab >= 0 && _searchInspectorTab <= 1 ? _searchInspectorTab : -1;
        }

        private void SelectNativeSearchInspectorTab(int index)
        {
            if (_searchInspectorTab == index) return;

            _searchInspectorTab = index;
            ScheduleNativeSearchInspectorRebuild();
        }

        private void SelectNativeSearchInspectorSettings()
        {
            if (_searchInspectorTab == -1) return;

            _searchInspectorTab = -1;
            ScheduleNativeSearchInspectorRebuild();
        }

        private void RefreshNativeSearchInspector()
        {
            if (_nativeSearchInspectorPane == null) return;

            _nativeSearchInspectorPane.SetTabs(
                GetNativeSearchInspectorTabs(),
                GetNativeSearchInspectorTabIndex(),
                SelectNativeSearchInspectorTab);
            _nativeSearchInspectorSettingsButton?.EnableInClassList(
                SearchInspectorSettingsActiveClass,
                _searchInspectorTab == -1);

            int stateHash = GetNativeSearchInspectorContentStateHash();
            if (_nativeSearchInspectorContentStateHash != stateHash)
            {
                RebuildNativeSearchInspectorContent();
            }
            RefreshNativeSearchAudioScrubber();
        }

        private int GetNativeSearchInspectorContentStateHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + _searchInspectorTab;
                hash = hash * 31 + (_selectedEntry?.Id ?? 0);
                hash = hash * 31 + SGrid.selectionCount;
                hash = hash * 31 + Tagging.TagHash;
                hash = hash * 31 + (ShowAdvanced() ? 1 : 0);
                hash = hash * 31 + (IsSearchFilterActive() ? 1 : 0);
                hash = hash * 31 + GetNativeSearchFilterStateHash();
                hash = hash * 31 + (_blockingInProgress ? 1 : 0);
                hash = hash * 31 + _selectedPackageTag;
                hash = hash * 31 + _selectedFileTag;
                hash = hash * 31 + _selectedAsset;
                hash = hash * 31 + _selectedPublisher;
                hash = hash * 31 + _selectedCategory;
                hash = hash * 31 + _selectedImageType;
                hash = hash * 31 + _selectedPriceOption;
                hash = hash * 31 + _selectedColorOption;
                hash = hash * 31 + _selectedPackageTypes;
                hash = hash * 31 + _selectedPackageSRPs;
                hash = hash * 31 + _selectedHiddenFilter;
                hash = hash * 31 + (_searchWidth?.GetHashCode() ?? 0);
                hash = hash * 31 + (_searchHeight?.GetHashCode() ?? 0);
                hash = hash * 31 + (_searchLength?.GetHashCode() ?? 0);
                hash = hash * 31 + (_searchVertexCount?.GetHashCode() ?? 0);
                hash = hash * 31 + (_searchSize?.GetHashCode() ?? 0);
                hash = hash * 31 + AI.Config.searchField;
                hash = hash * 31 + AI.Config.sortField;
                hash = hash * 31 + AI.Config.maxResults;
                hash = hash * 31 + AI.Config.previewVisibility;
                hash = hash * 31 + (AI.Config.showPreviews ? 1 : 0);
                hash = hash * 31 + (AI.Config.autoPlayAudio ? 1 : 0);
                hash = hash * 31 + (AI.Config.loopAudio ? 1 : 0);
                hash = hash * 31 + AI.Config.tileText;
                hash = hash * 31 + AI.Config.searchListRowHeight;
                hash = hash * 31 + (AI.Config.packageBackupFeatureEnabled ? 1 : 0);
                hash = hash * 31 + (AI.Config.assetManagerFeatureEnabled ? 1 : 0);
                hash = hash * 31 + (AI.Config.aiCaptionsFeatureEnabled ? 1 : 0);
                hash = hash * 31 + (AI.Config.semanticSearchFeatureEnabled ? 1 : 0);
                hash = hash * 31 + (AI.Config.codeSearchFeatureEnabled ? 1 : 0);

                AssetInfo info = _selectedEntry;
                if (info != null)
                {
                    AssetInfo packageRoot = info.GetRoot();
                    hash = hash * 31 + (int)info.DependencyState;
                    hash = hash * 31 + (info.InProject ? 1 : 0);
                    hash = hash * 31 + (info.Hidden ? 1 : 0);
                    hash = hash * 31 + (info.AssetTags?.Count ?? 0);
                    hash = hash * 31 + (info.PackageTags?.Count ?? 0);
                    hash = hash * 31 + (info.PackageMetadata?.Count ?? 0);
                    hash = hash * 31 + (info.Dependencies?.Count ?? 0);
                    hash = hash * 31 + (info.AICaption?.GetHashCode() ?? 0);
                    hash = hash * 31 + (info.Backup ? 1 : 0);
                    hash = hash * 31 + (info.UseAI ? 1 : 0);
                    hash = hash * 31 + (info.IsSemanticIndexEnabled ? 1 : 0);
                    hash = hash * 31 + (info.IsCodeIndexEnabled ? 1 : 0);
                    hash = hash * 31 + (info.NoIndex ? 1 : 0);
                    hash = hash * 31 + (info.KeepExtracted ? 1 : 0);
                    hash = hash * 31 + (info.Exclude ? 1 : 0);
                    hash = hash * 31 + (packageRoot?.PreviewTexture != null ? packageRoot.PreviewTexture.GetStableId() : 0);
                    hash = hash * 31 + (_previewEditor != null ? _previewEditor.GetStableId() : 0);
                    hash = hash * 31 + (HasNativeSearchEditorPreview() ? 1 : 0);
#if !AUDIO_TOOL_NOAUDIO
                    hash = hash * 31 + (AudioTool.AudioManager.IsPlaying() ? 1 : 0);
#endif
                }
                return hash;
            }
        }

        private void RebuildNativeSearchInspectorContent()
        {
            if (_nativeSearchInspectorPane == null) return;

            CaptureNativeSearchInspectorScroll();
            _nativeSearchInspectorPane.Body.Clear();
            _nativeSearchTileDetailPopup = null;
#if !AUDIO_TOOL_NOAUDIO
            _nativeSearchAudioScrubber = null;
#endif
            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(PackagesInspectorScrollClass);
            VisualElement content = new VisualElement();
            content.AddToClassList(PackagesInspectorContentClass);
            content.AddToClassList(PackagesDetailRootClass);
            scroll.Add(content);
            _nativeSearchInspectorPane.Body.Add(scroll);
            _nativeSearchInspectorScroll = scroll;
            _nativeSearchInspectorScrollTab = _searchInspectorTab;

            switch (_searchInspectorTab)
            {
                case -1:
                    content.Add(CreateNativeSearchSettings());
                    break;
                case 1:
                    content.Add(CreateNativeSearchFilters(SearchFilterViewHost.Inspector));
                    break;
                default:
                    content.Add(CreateNativeSearchDetails());
                    break;
            }

            if (_searchInspectorTab == 0 && !ShowAdvanced() && AI.Config.showHints)
            {
                Label hint = AssetInventoryUITK.CreateMutedLabel("Use the eye icon in the upper-right toolbar to show advanced options.");
                hint.AddToClassList(InspectorWrappedHintClass);
                hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                content.Add(hint);
            }
            AssetInventoryUITK.HideEmptySections(content);
            _nativeSearchInspectorContentStateHash = GetNativeSearchInspectorContentStateHash();
            _nativeScrollViewState.Restore(GetNativeSearchInspectorScrollKey(_searchInspectorTab), scroll);
        }

        private void CaptureNativeSearchInspectorScroll()
        {
            if (_nativeSearchInspectorScroll == null || _nativeSearchInspectorScrollTab == int.MinValue) return;

            _nativeScrollViewState.Capture(
                GetNativeSearchInspectorScrollKey(_nativeSearchInspectorScrollTab),
                _nativeSearchInspectorScroll);
        }

        private static string GetNativeSearchInspectorScrollKey(int tab)
        {
            return "search-inspector:" + tab;
        }

        private VisualElement CreateNativeSearchDetails()
        {
            if (SGrid.selectionCount > 1)
            {
                return CreateNativeSearchBulkDetails(SGrid.selectionItems);
            }
            if (_selectedEntry == null || string.IsNullOrWhiteSpace(_selectedEntry.SafeName))
            {
                return AssetInventoryUITK.CreateHelpBox("Select an asset for details.");
            }

            AssetInfo info = _selectedEntry;
            VisualElement root = new VisualElement();
            VisualElement fileContent = new VisualElement();
            fileContent.Add(CreateNativeSearchFileOverview(info));

            if (HasNativeSearchEditorPreview())
            {
                fileContent.Add(CreateNativeSearchPreview(info));
            }

            VisualElement dependencies = CreateNativeSearchDependencies(info);
            if (dependencies != null) fileContent.Add(CreateNativeSearchKeyedBlock("asset.dependencies", () => dependencies));

            fileContent.Add(CreateNativeSearchActions(info));
            if (!string.IsNullOrWhiteSpace(info.AICaption))
            {
                fileContent.Add(CreateNativeSearchCaption(info));
            }

            if (!info.IsVirtual)
            {
                fileContent.Add(CreateNativeSearchKeyedBlock("asset.actions.tag", () => CreateNativeSearchTags(new List<AssetInfo> {info}, info.AssetTags, null)));
            }

            Texture fileIcon = _staticPreviews.TryGetValue(info.Type ?? string.Empty, out string fileIconName)
                ? EditorGUIUtility.IconContent(fileIconName).image
                : EditorGUIUtility.IconContent("DefaultAsset Icon").image;
            root.Add(CreateNativeSearchScopeGroup(
                "Selected File",
                fileIcon,
                SearchScopeFileClass,
                fileContent));

            if (info.AssetId > 0)
            {
                VisualElement packageContent = CreateNativePackageData(info);
                AssetInfo packageRoot = info.GetRoot();
                if (packageRoot?.PreviewTexture != null)
                {
                    packageContent.Add(CreateNativeSearchKeyedBlock(
                        "package.icon",
                        () => CreateNativePackagePreview(packageRoot)));
                }
                else if (info.AssetSource == Asset.Source.RegistryPackage && !string.IsNullOrWhiteSpace(info.Description))
                {
                    packageContent.Add(CreateNativeSearchKeyedBlock(
                        "package.description",
                        () => CreateNativePackageText(info.Description)));
                }
                else
                {
                    Texture fallback = ResolveNativePackagePreviewTexture(packageRoot);
                    if (fallback != null)
                    {
                        packageContent.Add(CreateNativeSearchKeyedBlock(
                            "package.icon",
                            () => CreateNativePackagePreview(packageRoot, fallback)));
                    }
                }
                Texture packageIcon = packageRoot?.GetFallbackIcon() ?? EditorGUIUtility.IconContent("Package Manager").image;
                root.Add(CreateNativeSearchScopeGroup(
                    "Containing Package",
                    packageIcon,
                    SearchScopePackageClass,
                    packageContent));
            }
            return root;
        }

        private static VisualElement CreateNativeSearchScopeGroup(
            string kind,
            Texture icon,
            string modifierClass,
            VisualElement content)
        {
            VisualElement group = new VisualElement();
            group.AddToClassList(SearchScopeGroupClass);
            group.AddToClassList(modifierClass);

            VisualElement header = new VisualElement();
            header.AddToClassList(SearchScopeHeaderClass);
            if (icon != null)
            {
                Image image = new Image
                {
                    image = icon,
                    scaleMode = ScaleMode.ScaleToFit
                };
                image.AddToClassList(SearchScopeIconClass);
                header.Add(image);
            }
            header.Add(AssetInventoryUITK.CreateLabel(kind.ToUpperInvariant(), SearchScopeKindClass));
            group.Add(header);

            VisualElement body = new VisualElement();
            body.AddToClassList(SearchScopeBodyClass);
            if (content != null) body.Add(content);
            group.Add(body);
            return group;
        }

        private VisualElement CreateNativeSearchFileOverview(AssetInfo info)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("File");
            section.AddToClassList(PackagesDetailSectionClass);
            string fullPath = info.GetPath(true);
            string name = Path.GetFileName(fullPath);
            if (info.AssetSource == Asset.Source.AssetManager)
            {
                AddNativeSearchDetailRow(section, null, "Name", name, fullPath, () => AI.OpenURL(info.GetAMAssetUrl()), true);
            }
            else
            {
                AddNativeSearchDetailRow(section, null, "Name", name, $"Internal Id: {info.Id:N0}\nPreview State: {info.PreviewState}\nGuid: {info.Guid}\n\n{fullPath}", null, true);
            }
            if (info.AssetSource == Asset.Source.Directory)
            {
                AddNativeSearchDetailRow(section, "asset.location", "Location", Path.GetDirectoryName(fullPath), fullPath);
            }
            AddNativeSearchDetailRow(section, "asset.status", "Status", info.FileStatus);
            AddNativeSearchDetailRow(section, "asset.size", "Size", EditorUtility.FormatBytes(info.Size));
            if (info.Width > 0)
            {
                AddNativeSearchDetailRow(section, "asset.dimensions", "Dimensions", $"{info.Width:N0} x {info.Height:N0} pixels");
            }
            AddNativeSearchFormatRows(section, info);
            if (ShowAdvanced() || info.InProject)
            {
                AddNativeSearchDetailRow(section, null, "In Project", info.InProject ? "Yes" : "No", alwaysShow: true);
            }
            return section;
        }

        private void AddNativeSearchFormatRows(VisualElement section, AssetInfo info)
        {
            if (info.Type == "fbx")
            {
                if (info.Length > 0)
                {
                    Button animations = AssetInventoryUITK.CreateIconButton("Show animations", "d_animationvisibilitytoggleon", () =>
                    {
                        AnimationsUI window = AnimationsUI.ShowWindow();
                        window.Init(info);
                    });
                    VisualElement row = CreateNativeSearchInlineActionRow(
                        "Animations",
                        ((int)info.Length).ToString("N0"),
                        "Animation clips found in this FBX file.",
                        animations);
                    section.Add(CreateNativeSearchKeyedBlock("asset.animations", () => row));
                }
                if (!string.IsNullOrWhiteSpace(info.FileData))
                {
                    try
                    {
                        FBXData data = JsonConvert.DeserializeObject<FBXData>(info.FileData);
                        if (data != null)
                        {
                            if (data.meshCount > 0) AddNativeSearchDetailRow(section, "asset.meshes", "Meshes", data.meshCount.ToString("N0"));
                            if (data.materialCount > 0) AddNativeSearchDetailRow(section, "asset.materials", "Materials", data.materialCount.ToString("N0"));
                            if (data.vertexCount > 0) AddNativeSearchDetailRow(section, "asset.vertices", "Vertices", data.vertexCount.ToString("N0"));
                            if (data.triangleCount > 0) AddNativeSearchDetailRow(section, "asset.triangles", "Triangles", data.triangleCount.ToString("N0"));
                            if (data.boneCount > 0) AddNativeSearchDetailRow(section, "asset.bones", "Bones", data.boneCount.ToString("N0"));
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }
            }
            else if (info.Length > 0)
            {
                AddNativeSearchDetailRow(section, "asset.length", "Length", StringUtils.FormatDuration(info.Length));
            }
        }

        private bool HasNativeSearchEditorPreview()
        {
            return !_isCleaningUp && _previewEditor != null
                && HasNativeSearchPreviewContext(_previewEditor.target)
                && _previewEditor.HasPreviewGUI();
        }

        internal static bool HasNativeSearchPreviewContext(Object target)
        {
            if (target == null) return false;

            // Unity advertises a preview for standalone clips even though there is no host object to render.
            if (!(target is AnimationClip)) return true;

            string assetPath = AssetDatabase.GetAssetPath(target);
            return UnityEditor.AssetImporter.GetAtPath(assetPath) is ModelImporter;
        }

        private VisualElement CreateNativeSearchPreview(AssetInfo info)
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.AddToClassList(PackagesDetailSectionClass);
            VisualElement preview = CommonEditorPreviewBridge.Create(
                _previewEditor,
                "ai-search-editor-preview");
            preview.AddToClassList(SearchDetailPreviewClass);
            preview.style.display = AI.Config.showPreviews ? DisplayStyle.Flex : DisplayStyle.None;
            Foldout foldout = AssetInventoryUITK.CreateFoldout("Preview", AI.Config.showPreviews, value =>
            {
                AI.Config.showPreviews = value;
                AI.SaveConfig();
                preview.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            }, $"Show Unity's interactive preview for {info.FileName}.");
            foldout.Add(preview);
            section.Add(foldout);
            return section;
        }

        private VisualElement CreateNativeSearchDependencies(AssetInfo info)
        {
            bool show = info.InProject || (!info.IsVirtual && (info.IsDownloaded || info.IsMaterialized)
                && (info.AssetSource == Asset.Source.AssetManager || DependencyAnalysis.NeedsScan(info.Type)));
            if (!show) return null;

            VisualElement section = AssetInventoryUITK.CreateSection("Dependencies");
            section.AddToClassList(PackagesDetailSectionClass);
            VisualElement actions = new VisualElement();
            actions.AddToClassList(PackagesDetailActionsClass);

            if (info.InProject)
            {
                AddNativeSearchDetailRow(section, null, "Found", info.DependencyState == AssetInfo.DependencyStateOptions.Done
                    ? (info.Dependencies?.Count ?? 0).ToString("N0")
                    : "On demand", alwaysShow: true);
                actions.Add(AssetInventoryUITK.CreateSecondaryButton("Show Dependencies...", () => ShowProjectDependencies(info)));
                actions.Add(AssetInventoryUITK.CreateSecondaryButton("Where Used...", () => ShowWhereUsed(info)));
            }
            else
            {
                Button showResults = null;
                if ((info.DependencyState == AssetInfo.DependencyStateOptions.Partial ||
                        info.DependencyState == AssetInfo.DependencyStateOptions.NotPossible) &&
                    CanShowDependencyTree(info))
                {
                    showResults = AssetInventoryUITK.CreateIconButton(
                        "Show dependency results",
                        "d_animationvisibilitytoggleon",
                        () => ShowNativeDependencyTree(info));
                }
                else if (info.DependencyState == AssetInfo.DependencyStateOptions.Done && CanShowDependencyTree(info))
                {
                    showResults = AssetInventoryUITK.CreateIconButton(
                        "Show dependency files",
                        "d_animationvisibilitytoggleon",
                        () => ShowNativeDependencyTree(info));
                }
                AddNativeSearchDependencyFilesRow(section, info, showResults);

                switch (info.DependencyState)
                {
                    case AssetInfo.DependencyStateOptions.Unknown:
                        actions.Add(AssetInventoryUITK.CreatePrimaryButton("Calculate", () => _ = CalculateDependencies(info)));
                        break;
                    case AssetInfo.DependencyStateOptions.Calculating:
                        actions.Add(AssetInventoryUITK.CreateSecondaryButton("Cancel", () => CancelDependencyCalculation(info)));
                        break;
                    case AssetInfo.DependencyStateOptions.Partial:
                    case AssetInfo.DependencyStateOptions.NotPossible:
                        actions.Add(AssetInventoryUITK.CreateSecondaryButton("Retry", () => _ = CalculateDependencies(info)));
                        break;
                }

                PopupStringField scripts = CreateNativeSearchPopup(_scriptImportOptions, AI.Config.scriptImportMode, value =>
                {
                    AI.Config.scriptImportMode = value;
                    AI.SaveConfig();
                    _files?.ForEach(file => file.DependencyState = AssetInfo.DependencyStateOptions.Unknown);
                    CalcDependenciesOnDemand(_selectedEntry);
                    ScheduleNativeSearchInspectorRebuild();
                });
                section.Add(CreateNativePackageDetailFormBuilder().CreateRow("Script Import", scripts.tooltip, scripts));
            }
            if (actions.childCount > 0) section.Add(actions);
            return section;
        }

        private static void AddNativeSearchDependencyFilesRow(VisualElement section, AssetInfo info, Button showResults)
        {
            section.Add(CreateNativeSearchInlineActionRow(
                "Files",
                GetNativeDependencyStatus(info),
                "Dependency files found for this asset.",
                showResults));
        }

        private static VisualElement CreateNativeSearchInlineActionRow(string label, string valueText, string tooltip, Button action)
        {
            VisualElement row = new VisualElement
            {
                tooltip = tooltip
            };
            row.AddToClassList(PackagesDetailRowClass);
            row.Add(AssetInventoryUITK.CreateLabel(label, PackagesDetailLabelClass));

            VisualElement value = new VisualElement();
            value.AddToClassList(SearchDetailInlineValueClass);
            Label status = AssetInventoryUITK.CreateCopyLabel(valueText);
            status.AddToClassList(PackagesDetailValueClass);
            value.Add(status);

            if (action != null)
            {
                action.AddToClassList(SearchDetailInlineActionClass);
                value.Add(action);
            }
            row.Add(value);
            return row;
        }

        private static string GetNativeDependencyStatus(AssetInfo info)
        {
            switch (info.DependencyState)
            {
                case AssetInfo.DependencyStateOptions.Unknown:
                    return "Not calculated";
                case AssetInfo.DependencyStateOptions.Calculating:
                    return "Calculating...";
                case AssetInfo.DependencyStateOptions.Done:
                case AssetInfo.DependencyStateOptions.Partial:
                    return FormatDependencyCount(info, AI.ShowAdvanced());
                case AssetInfo.DependencyStateOptions.NotPossible:
                    return IsIncompleteDependencyResult(info) ? FormatDependencyCount(info, AI.ShowAdvanced()) : "Cannot determine (binary)";
                case AssetInfo.DependencyStateOptions.Failed:
                    return "Failed to determine";
                default:
                    return info.DependencyState.ToString();
            }
        }

        private void ShowNativeDependencyTree(AssetInfo info)
        {
            DependenciesUI window = DependenciesUI.ShowWindow();
            window.Init(info, OpenAssetFileInSearch);
        }

        private VisualElement CreateNativeSearchCaption(AssetInfo info)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("AI Caption");
            section.AddToClassList(PackagesDetailSectionClass);
            section.Add(AssetInventoryUITK.CreateCopyLabel(info.AICaption));

            VisualElement actions = new VisualElement();
            actions.AddToClassList(PackagesDetailActionsClass);
            actions.AddToClassList(SearchDetailActionsClass);
            AddNativeSearchCaptionActions(actions, info, _blockingInProgress);
            if (actions.childCount > 0) section.Add(actions);
            return section;
        }

        private VisualElement CreateNativeSearchActions(AssetInfo info)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Actions");
            section.AddToClassList(PackagesDetailSectionClass);
            VisualElement actions = new VisualElement();
            actions.AddToClassList(PackagesDetailActionsClass);
            actions.AddToClassList(SearchDetailActionsClass);
            section.Add(actions);

            bool downloadable = IsDownloadable(info);
            bool available = info.IsDownloaded || info.IsMaterialized || downloadable;
            bool busy = _blockingInProgress;
            bool canImport = available && !string.IsNullOrEmpty(_importFolder);

            AssetInfo subPackage = ResolveSearchSubPackage(info);
            if (subPackage != null)
            {
                AddNativePackageAction(actions, "package.actions.opensubpackage", "Jump into Sub-Package",
                    () => OpenInSearch(subPackage, true, false), !busy,
                    tooltip: "Filter Search to the indexed contents of this sub-package.");
            }

            if (available && !info.InProject && string.IsNullOrEmpty(_importFolder))
            {
                section.Add(AssetInventoryUITK.CreateHelpBox("Select a folder in the Project view to enable import actions.", MessageType.Info));
            }
            if (canImport && AssetUtils.CanAddToScene(info.FileName))
            {
                AddNativePackageAction(actions, "asset.actions.addtoscene", "Add to Scene", () => _ = PerformCopyTo(info, _importFolder, false, true), !busy,
                    primary: true, tooltip: GetNativeSearchDownloadActionTooltip("Add to Scene", downloadable), trailingIconName: downloadable ? SearchDownloadActionIcon : null);
            }
            if (canImport && !info.InProject)
            {
                AddNativePackageAction(actions, "asset.actions.import", "Import", () => _ = CopyToAsync(info, _importFolder, true, AI.Config.scriptImportMode), !busy,
                    primary: true, alwaysShow: true, tooltip: GetNativeSearchDownloadActionTooltip("Import", downloadable), trailingIconName: downloadable ? SearchDownloadActionIcon : null);
            }
            if (canImport && ShowAdvanced())
            {
                string importFileText = info.InProject ? "Reimport File" : "Import File Only";
                AddNativePackageAction(actions, "asset.actions.importfile", importFileText,
                    () => _ = CopyToAsync(info, _importFolder, false, 0, true, false, info.InProject), !busy,
                    tooltip: GetNativeSearchDownloadActionTooltip(importFileText, downloadable), trailingIconName: downloadable ? SearchDownloadActionIcon : null);
            }

#if !AUDIO_TOOL_NOAUDIO
            if (AI.IsFileType(info.Path, AI.AssetGroup.Audio))
            {
                VisualElement audioControls = new VisualElement();
                audioControls.AddToClassList(SearchDetailAudioControlsClass);
                VisualElement transport = new VisualElement();
                transport.AddToClassList(SearchDetailAudioTransportClass);
                audioControls.Add(transport);
                Button play = AssetInventoryUITK.CreateIconButton("Play audio preview", "d_PlayButton", () => PlayAudio(info));
                play.AddToClassList(SearchDetailAudioButtonClass);
                transport.Add(play);
                Button stop = AssetInventoryUITK.CreateIconButton("Stop audio preview", "d_PreMatQuad", AudioTool.AudioManager.StopAudio);
                stop.AddToClassList(SearchDetailAudioButtonClass);
                stop.SetEnabled(AudioTool.AudioManager.IsPlaying());
                transport.Add(stop);

                if (AudioTool.AudioManager.IsPlaying() && AudioTool.AudioManager.CurrentClip != null)
                {
                    AudioClip clip = AudioTool.AudioManager.CurrentClip;
                    _nativeSearchAudioScrubber = new Slider(0f, clip.length);
                    _nativeSearchAudioScrubber.AddToClassList(SearchDetailAudioScrubberClass);
                    _nativeSearchAudioScrubber.SetValueWithoutNotify(AudioTool.AudioManager.GetCurrentPosition());
                    _nativeSearchAudioScrubber.RegisterValueChangedCallback(evt =>
                    {
                        int sample = Mathf.RoundToInt(clip.samples * evt.newValue / clip.length);
                        AudioTool.AudioManager.PlayClip(clip, sample, false);
                    });
                    transport.Add(_nativeSearchAudioScrubber);
                }

                VisualElement options = new VisualElement();
                options.AddToClassList(SearchDetailAudioOptionsClass);
                audioControls.Add(options);
                Toggle autoPlay = new Toggle {text = "Auto-Play", value = AI.Config.autoPlayAudio};
                autoPlay.RegisterValueChangedCallback(evt =>
                {
                    AI.Config.autoPlayAudio = evt.newValue;
                    AI.SaveConfig();
                    if (evt.newValue) PlayAudio(info);
                    ScheduleNativeSearchInspectorRebuild();
                });
                options.Add(autoPlay);
                Toggle loop = new Toggle {text = "Loop", value = AI.Config.loopAudio};
                loop.RegisterValueChangedCallback(evt =>
                {
                    AI.Config.loopAudio = evt.newValue;
                    AI.SaveConfig();
                });
                options.Add(loop);

                VisualElement audioBlock = CreateNativeSearchKeyedBlock("asset.actions.audiopreview", () => audioControls);
                audioBlock.AddToClassList(SearchDetailAudioBlockClass);
                actions.Add(audioBlock);
                if (!string.IsNullOrEmpty(_importFolder))
                {
                    AddNativePackageAction(actions, "asset.actions.audioedit", "Edit Audio...", () => OpenAudioEditor(info, _importFolder), !busy);
                }
            }
#endif

            if (info.InProject && !AI.Config.pingSelected)
            {
                AddNativePackageAction(actions, "asset.actions.ping", "Ping", () => PingAsset(info));
            }
            AddNativePackageAction(actions, "asset.actions.open", "Open", () => Open(info), !busy,
                tooltip: GetNativeSearchDownloadActionTooltip("Open", downloadable), trailingIconName: downloadable ? SearchDownloadActionIcon : null);
            string openLocationText = Application.platform == RuntimePlatform.OSXEditor ? "Show in Finder" : "Show in Explorer";
            AddNativePackageAction(actions, "asset.actions.openexplorer",
                openLocationText,
                () => OpenExplorer(info), !busy,
                tooltip: GetNativeSearchDownloadActionTooltip(openLocationText, downloadable), trailingIconName: downloadable ? SearchDownloadActionIcon : null);

            if (!info.IsVirtual && PreviewManager.IsPreviewable(info.FileName, true, info)
                && (ShowAdvanced() || info.PreviewState == AssetFile.PreviewOptions.Error || !info.HasPreview()))
            {
                AddNativePackageAction(actions, "asset.actions.recreatepreview", "Recreate Preview", () => RecreatePreviews(new List<AssetInfo> {info}), !busy);
            }
            if (string.IsNullOrWhiteSpace(info.AICaption))
            {
                AddNativeSearchCaptionActions(actions, info, busy);
            }
#if USE_ASSET_MANAGER && USE_CLOUD_IDENTITY
            if (AI.Actions.AssetManagerEnabled)
            {
                if (info.AssetSource == Asset.Source.AssetManager)
                {
                    if (info.ParentInfo == null)
                    {
                        AddNativePackageAction(actions, "asset.actions.assetmanager", "Delete from Asset Manager Project", () =>
                            DeleteAssetsFromProject(new List<AssetInfo> {info}), !CloudAssetManagement.IsBusy && !busy, destructive: true);
                    }
                    else
                    {
                        AddNativePackageAction(actions, "asset.actions.assetmanager", "Remove from Asset Manager Collection", () =>
                            RemoveAssetsFromCollection(new List<AssetInfo> {info}), !CloudAssetManagement.IsBusy && !busy, destructive: true);
                    }
                }
                else
                {
                    Button upload = null;
                    upload = AddNativePackageAction(actions, "asset.actions.assetmanager", "Upload...", () =>
                    {
                        ProjectSelectionUI.ShowDropdown(CommonUITK.ToScreenDropdownAnchor(this, upload), _assets, project =>
                            AddAssetsToProject(project, new List<AssetInfo> {info}));
                    }, !CloudAssetManagement.IsBusy && !busy);
                }
            }
#endif
            if (info.InProject)
            {
                AddNativePackageAction(actions, "asset.actions.remove", "Remove from Project...", () =>
                    UninstallPackageUI.ShowWindow().Init(new List<AssetInfo> {info}), !busy, destructive: true);
            }
            if (!info.IsVirtual)
            {
                AddNativePackageAction(actions, "asset.actions.delete", info.Hidden ? "Unhide" : "Hide from Results", () =>
                {
                    Assets.SetFilesHidden(new List<int> {info.Id}, !info.Hidden);
                    _requireSearchUpdate = true;
                    ScheduleNativeSearchInspectorRebuild();
                }, !busy, destructive: !info.Hidden);
            }

            if (!info.IsMaterialized && !busy)
            {
                string hint = downloadable
                    ? "The package will be downloaded and extracted before this action."
                    : info.AssetSource == Asset.Source.AssetManager
                        ? $"{EditorUtility.FormatBytes(info.Size)} will be downloaded first."
                        : $"{EditorUtility.FormatBytes(info.GetRoot().PackageSize)} will be extracted first.";
                section.Add(CreateNativeSearchKeyedBlock("asset.actions.extraction", () => AssetInventoryUITK.CreateHelpBox(hint, MessageType.Info)));
            }
            if (busy)
            {
                VisualElement progress = AssetInventoryUITK.CreateHelpBox("Operation in progress...", MessageType.Info);
                if (_extraction != null && !_extraction.IsCancellationRequested)
                {
                    progress.Add(AssetInventoryUITK.CreateSecondaryButton("Cancel", () => _extraction.Cancel()));
                }
                section.Add(progress);
            }
            return section;
        }

        internal static AssetInfo ResolveSearchSubPackage(AssetInfo info)
        {
            if (info == null || info.Id <= 0 || (!info.IsUnityPackage() && !info.IsArchive())) return null;

            string childLocation = info.Location + Asset.SUB_PATH + info.Path;
            return info.FirstChildInfoOrDefault(child => child != null
                && child.Id > 0
                && string.Equals(child.Location, childLocation, StringComparison.Ordinal));
        }

        private void AddNativeSearchCaptionActions(VisualElement actions, AssetInfo info, bool busy)
        {
            if (!AI.Actions.AICaptionsEnabled || info.IsVirtual || (!ShowAdvanced() && !string.IsNullOrWhiteSpace(info.AICaption))) return;

            AddNativePackageAction(actions, "asset.actions.recreateaicaption",
                string.IsNullOrWhiteSpace(info.AICaption) ? "Create AI Caption" : "Recreate AI Caption",
                () => RecreateAICaptions(new List<AssetInfo> {info}), !busy);
            Button manual = null;
            manual = AddNativePackageAction(actions, "asset.actions.recreateaicaption", "Enter Caption...", () =>
            {
                NameWindow.ShowAsDropDown(
                    CommonUITK.ToScreenDropdownAnchor(this, manual),
                    info.AICaption,
                    value =>
                    {
                        if (value == info.AICaption) return;
                        AI.SetAICaption(info, value);
                        _requireSearchUpdate = true;
                        ScheduleNativeSearchInspectorRebuild();
                    });
            }, !busy);
            if (string.IsNullOrWhiteSpace(info.AICaption)) return;

            AddNativePackageAction(actions, "asset.actions.recreateaicaption", "Remove Caption", () =>
            {
                AI.SetAICaption(info, null);
                _requireSearchUpdate = true;
                ScheduleNativeSearchInspectorRebuild();
            }, !busy, destructive: true);
        }

        private static string GetNativeSearchDownloadActionTooltip(string action, bool downloadsFirst)
        {
            return downloadsFirst ? action + ". Downloads the required file first." : action;
        }

        private VisualElement CreateNativeSearchBulkDetails(List<AssetInfo> selection)
        {
            List<AssetInfo> items = selection?.Where(item => item != null).ToList() ?? new List<AssetInfo>();
            VisualElement root = new VisualElement();
            VisualElement summary = AssetInventoryUITK.CreateSection("Selection");
            summary.AddToClassList(PackagesDetailSectionClass);
            AddNativeSearchDetailRow(summary, "asset.bulk.count", "Selected", items.Count.ToString("N0"));
            AddNativeSearchDetailRow(summary, "asset.bulk.packages", "Packages", SGrid.selectionPackageCount.ToString("N0"));
            AddNativeSearchDetailRow(summary, "asset.bulk.size", "Size", EditorUtility.FormatBytes(SGrid.selectionSize));
            int inProject = items.Count(item => item.InProject);
            AddNativeSearchDetailRow(summary, "asset.bulk.inproject", "In Project", $"{inProject:N0}/{items.Count:N0}");
            root.Add(summary);

            VisualElement actionSection = AssetInventoryUITK.CreateSection("Actions");
            actionSection.AddToClassList(PackagesDetailSectionClass);
            VisualElement actions = new VisualElement();
            actions.AddToClassList(PackagesDetailActionsClass);
            actions.AddToClassList(SearchDetailActionsClass);
            actionSection.Add(actions);
            bool busy = _blockingInProgress;
            bool needsDownload = items.Any(IsDownloadable);

            if (!string.IsNullOrEmpty(_importFolder) && inProject < items.Count)
            {
                string importText = inProject > 0 ? $"Import {items.Count - inProject:N0} Remaining" : "Import Files";
                AddNativePackageAction(actions, "asset.bulk.actions.import", importText,
                    () => ImportBulkFiles(items), !busy, primary: true, alwaysShow: true,
                    tooltip: GetNativeSearchDownloadActionTooltip(importText, needsDownload), trailingIconName: needsDownload ? SearchDownloadActionIcon : null);
            }
            AddNativePackageAction(actions, "asset.bulk.actions.open", "Open Files", () => OpenNativeSearchBulk(items), !busy,
                tooltip: GetNativeSearchDownloadActionTooltip("Open Files", needsDownload), trailingIconName: needsDownload ? SearchDownloadActionIcon : null);
            string openLocationsText = Application.platform == RuntimePlatform.OSXEditor ? "Show in Finder" : "Show in Explorer";
            AddNativePackageAction(actions, "asset.bulk.actions.openexplorer",
                openLocationsText,
                () => OpenNativeSearchBulkLocations(items), !busy,
                tooltip: GetNativeSearchDownloadActionTooltip(openLocationsText, needsDownload), trailingIconName: needsDownload ? SearchDownloadActionIcon : null);
            AddNativePackageAction(actions, "asset.bulk.actions.recreatepreviews", "Recreate Previews", () => RecreatePreviews(items), !busy);
            if (AI.Actions.AICaptionsEnabled && (ShowAdvanced() || _assetFileAICaptionCount > 0))
            {
                AddNativePackageAction(actions, "asset.bulk.actions.recreateaicaptions",
                    _assetFileAICaptionCount == 0 ? "Create AI Captions" : "Recreate AI Captions",
                    () => RecreateAICaptions(items), !busy);
                if (_assetFileAICaptionCount > 0)
                {
                    AddNativePackageAction(actions, "asset.bulk.actions.recreateaicaptions", "Remove AI Captions", () =>
                    {
                        items.ForEach(info => AI.SetAICaption(info, null));
                        _requireSearchUpdate = true;
                        ScheduleNativeSearchInspectorRebuild();
                    }, !busy, destructive: true);
                }
            }
#if USE_ASSET_MANAGER && USE_CLOUD_IDENTITY
            if (AI.Actions.AssetManagerEnabled)
            {
                if (_assetFileAMProjectCount + _assetFileAMCollectionCount > 0)
                {
                    if (_assetFileAMProjectCount > 0)
                    {
                        AddNativePackageAction(actions, "asset.bulk.actions.assetmanager", "Delete from Asset Manager Project", () =>
                            DeleteAssetsFromProject(items), !CloudAssetManagement.IsBusy && !busy, destructive: true);
                    }
                    if (_assetFileAMCollectionCount > 0)
                    {
                        AddNativePackageAction(actions, "asset.bulk.actions.assetmanager", "Remove from Asset Manager Collection", () =>
                            RemoveAssetsFromCollection(items), !CloudAssetManagement.IsBusy && !busy, destructive: true);
                    }
                }
                else
                {
                    Button upload = null;
                    upload = AddNativePackageAction(actions, "asset.bulk.actions.assetmanager", "Upload...", () =>
                    {
                        ProjectSelectionUI.ShowDropdown(CommonUITK.ToScreenDropdownAnchor(this, upload), _assets, project =>
                            AddAssetsToProject(project, items));
                    }, !CloudAssetManagement.IsBusy && !busy);
                }
            }
#endif
            if (inProject > 0)
            {
                AddNativePackageAction(actions, "asset.bulk.actions.remove", $"Remove {inProject:N0} from Project...", () =>
                    UninstallPackageUI.ShowWindow().Init(items.Where(item => item.InProject).ToList()), !busy, destructive: true);
            }
            AddNativePackageAction(actions, "asset.bulk.actions.export", "Export Files...", () =>
            {
                ExportUI window = ExportUI.ShowWindow();
                window.Init(items, true, 2);
            }, !busy);
            if (items.Any(item => !item.Hidden))
            {
                AddNativePackageAction(actions, "asset.bulk.actions.delete", "Hide from Results", () => SetNativeSearchBulkHidden(items, true), !busy, destructive: true);
            }
            if (items.Any(item => item.Hidden))
            {
                AddNativePackageAction(actions, "asset.bulk.actions.delete", "Unhide", () => SetNativeSearchBulkHidden(items, false), !busy);
            }
            int calculating = items.Count(item => item.DependencyState == AssetInfo.DependencyStateOptions.Calculating);
            if (calculating > 0)
            {
                AddNativePackageAction(actions, "asset.bulk.actions.cancelcalculations", $"Cancel {calculating:N0} Dependency Calculations", () =>
                {
                    items.Where(item => item.DependencyState == AssetInfo.DependencyStateOptions.Calculating).ToList().ForEach(CancelDependencyCalculation);
                    ScheduleNativeSearchInspectorRebuild();
                });
            }
            root.Add(actionSection);

            List<AssetInfo> taggable = items.Where(item => !item.IsVirtual).ToList();
            if (taggable.Count > 0)
            {
                CalculateSearchBulkSelection();
                root.Add(CreateNativeSearchKeyedBlock("asset.bulk.actions.tag", () => CreateNativeSearchTags(taggable, null, _assetFileBulkTags)));
            }
            return root;
        }

        private void OpenNativeSearchBulk(List<AssetInfo> items)
        {
            if (items.Count > AI.Config.massOpenWarnThreshold
                && !EditorUtility.DisplayDialog("Open Files", $"You are about to open {items.Count:N0} files. Continue?", "Continue", "Cancel")) return;
            items.ForEach(Open);
        }

        private void OpenNativeSearchBulkLocations(List<AssetInfo> items)
        {
            if (items.Count > AI.Config.massOpenWarnThreshold
                && !EditorUtility.DisplayDialog("Show Files", $"You are about to open {items.Count:N0} locations. Continue?", "Continue", "Cancel")) return;
            items.ForEach(OpenExplorer);
        }

        private void SetNativeSearchBulkHidden(List<AssetInfo> items, bool hidden)
        {
            Assets.SetFilesHidden(items.Select(item => item.Id).ToList(), hidden);
            _requireSearchUpdate = true;
            ScheduleNativeSearchInspectorRebuild();
        }

        private VisualElement CreateNativeSearchTags(List<AssetInfo> infos, List<TagInfo> tags, Dictionary<string, Tuple<int, Color>> bulkTags)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Tags");
            section.AddToClassList(PackagesDetailSectionClass);
            VisualElement tagList = new VisualElement();
            tagList.AddToClassList(PackagesDetailTagsClass);
            section.Add(tagList);

            Button add = null;
            add = AssetInventoryUITK.CreateSecondaryButton("Add Tag...", () =>
            {
                TagSelectionUI.ShowDropdown(
                    this,
                    add,
                    TagAssignment.Target.Asset,
                    infos,
                    () =>
                    {
                        CalculateSearchBulkSelection();
                        _requireAssetTreeRebuild = true;
                        _requireSearchUpdate = true;
                        ScheduleNativeSearchInspectorRebuild();
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
                        Tagging.RemoveAssetAssignments(infos, tagName, true);
                        CalculateSearchBulkSelection();
                        _requireSearchUpdate = true;
                        ScheduleNativeSearchInspectorRebuild();
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
                        Tagging.RemoveAssignment(infos[0], captured, true, true);
                        _requireAssetTreeRebuild = true;
                        _requireSearchUpdate = true;
                        ScheduleNativeSearchInspectorRebuild();
                    });
                    tagList.Add(pill);
                }
            }
            return section;
        }

        private void AddNativeSearchDetailRow(
            VisualElement section,
            string key,
            string label,
            string value,
            string tooltip = null,
            Action click = null,
            bool alwaysShow = false)
        {
            if (section == null || string.IsNullOrWhiteSpace(value)) return;
            VisualElement row = CreateNativePackageDetailRow(label, value, tooltip, click);
            if (string.IsNullOrWhiteSpace(key)) section.Add(row);
            else section.Add(CreateNativeSearchKeyedBlock(key, () => row, alwaysShow));
        }

        private VisualElement CreateNativeSearchKeyedBlock(string key, Func<VisualElement> content, bool alwaysShow = false)
        {
            return AssetInventoryUITK.CreateAdvancedVisibilityBlock(
                key,
                content,
                alwaysShow,
                onVisibilityChanged: ScheduleNativeSearchInspectorRebuild);
        }

        private void ScheduleNativeSearchInspectorRebuild()
        {
            _nativeSearchInspectorContentStateHash = int.MinValue;
            _nativeSearchInspectorPane?.schedule.Execute(RefreshNativeSearchInspector).ExecuteLater(0);
        }

        private void RefreshNativeSearchAudioScrubber()
        {
#if !AUDIO_TOOL_NOAUDIO
            if (_nativeSearchAudioScrubber == null || !AudioTool.AudioManager.IsPlaying()) return;

            AudioClip clip = AudioTool.AudioManager.CurrentClip;
            if (clip == null) return;
            _nativeSearchAudioScrubber.highValue = clip.length;
            _nativeSearchAudioScrubber.SetValueWithoutNotify(AudioTool.AudioManager.GetCurrentPosition());
#endif
        }
    }
}
