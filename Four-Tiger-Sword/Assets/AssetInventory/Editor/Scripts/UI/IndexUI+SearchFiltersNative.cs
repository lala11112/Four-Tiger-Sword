using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;
using PopupStringField = UnityEngine.UIElements.PopupField<string>;
using TextField = UnityEngine.UIElements.TextField;
using Toggle = UnityEngine.UIElements.Toggle;
using VisualElement = UnityEngine.UIElements.VisualElement;

namespace AssetInventory
{
    public partial class IndexUI
    {
        private const string SearchSettingsIconButtonClass = "ai-search-settings-icon-button";

        private enum SearchFilterViewHost
        {
            External,
            Inspector,
            Sidebar
        }

        private VisualElement CreateNativeSearchFilters(SearchFilterViewHost host)
        {
            VisualElement root = new VisualElement();
            bool projectOnly = SearchScopeModel.IsProjectOnly(GetConfiguredSearchScope());
            bool inMemory = _inMemoryMode != InMemoryModeState.None;
            if (projectOnly)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox(
                    "Package and store metadata filters are not available in Project scope.",
                    MessageType.Info));
            }
            if (inMemory)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox(
                    "Filters are locked while High-Speed Mode is active.",
                    MessageType.Info));
            }

            VisualElement metadata = AssetInventoryUITK.CreateSection("Package Metadata");
            metadata.AddToClassList(PackagesDetailSectionClass);
            CommonFormBuilder form = AssetInventoryUITK.CreateFormBuilder();
            metadata.Add(form.CreateRow("Package Tag", null,
                AssetInventoryUITK.CreateSearchablePopupField(this, _tagPopupItems, _selectedPackageTag, value =>
                {
                    _selectedPackageTag = value;
                    CommitNativeSearchFilterChange(host);
                }, AI.Config.colorTagFilterClosedField)));
            metadata.Add(form.CreateRow("File Tag", null,
                AssetInventoryUITK.CreateSearchablePopupField(this, _tagPopupItems, _selectedFileTag, value =>
                {
                    _selectedFileTag = value;
                    CommitNativeSearchFilterChange(host);
                }, AI.Config.colorTagFilterClosedField)));
            metadata.Add(form.CreateRow("Package", null,
                AssetInventoryUITK.CreateSearchablePopupField(this, _assetNames, _selectedAsset, value =>
                {
                    _selectedAsset = value;
                    CommitNativeSearchFilterChange(host);
                })));
            metadata.Add(form.CreateRow("Publisher", null,
                AssetInventoryUITK.CreateSearchablePopupField(this, _publisherNames, _selectedPublisher, value =>
                {
                    _selectedPublisher = value;
                    CommitNativeSearchFilterChange(host);
                })));
            metadata.Add(form.CreateRow("Category", null,
                AssetInventoryUITK.CreateSearchablePopupField(this, _categoryNames, _selectedCategory, value =>
                {
                    _selectedCategory = value;
                    CommitNativeSearchFilterChange(host);
                })));
            metadata.SetEnabled(!projectOnly && !inMemory);
            root.Add(metadata);

            VisualElement properties = AssetInventoryUITK.CreateSection("File Properties");
            properties.AddToClassList(PackagesDetailSectionClass);
            if (IsFilterApplicable("ImageType"))
            {
                properties.Add(form.CreateRow("Image Type", null,
                    CreateNativeSearchPopup(_imageTypeOptions, _selectedImageType, value =>
                    {
                        _selectedImageType = value;
                        CommitNativeSearchFilterChange(host);
                    })));
            }
            if (IsFilterApplicable("Width"))
            {
                properties.Add(CreateNativeSearchComparisonRow("Width", _checkMaxWidth, _searchWidth, "pixels", (maximum, value) =>
                {
                    _checkMaxWidth = maximum;
                    _searchWidth = value;
                }, host));
            }
            if (IsFilterApplicable("Height"))
            {
                properties.Add(CreateNativeSearchComparisonRow("Height", _checkMaxHeight, _searchHeight, "pixels", (maximum, value) =>
                {
                    _checkMaxHeight = maximum;
                    _searchHeight = value;
                }, host));
            }
            if (IsFilterApplicable("Length"))
            {
                string rawType = GetRawSearchType();
                bool fbxContext = rawType == "Models" || rawType == "Animations" || rawType == "Models/fbx";
                properties.Add(CreateNativeSearchComparisonRow(fbxContext ? "Animations" : "Length", _checkMaxLength, _searchLength, fbxContext ? null : "sec", (maximum, value) =>
                {
                    _checkMaxLength = maximum;
                    _searchLength = value;
                }, host));
            }
            if (IsFilterApplicable("VertexCount"))
            {
                properties.Add(CreateNativeSearchComparisonRow("Vertices", _checkMaxVertexCount, _searchVertexCount, null, (maximum, value) =>
                {
                    _checkMaxVertexCount = maximum;
                    _searchVertexCount = value;
                }, host, "Number of vertices in the model."));
            }
            properties.Add(CreateNativeSearchComparisonRow("File Size", _checkMaxSize, _searchSize, "kb", (maximum, value) =>
            {
                _checkMaxSize = maximum;
                _searchSize = value;
            }, host, "File size in kilobytes."));
            properties.SetEnabled(!inMemory);
            root.Add(properties);

            VisualElement store = AssetInventoryUITK.CreateSection("Store & Visibility");
            store.AddToClassList(PackagesDetailSectionClass);
            VisualElement price = new VisualElement();
            price.AddToClassList(SearchDetailComparisonClass);
            price.Add(CreateNativeSearchPopup(_priceOptions, _selectedPriceOption, value =>
            {
                _selectedPriceOption = value;
                CommitNativeSearchFilterChange(host, true);
            }));
            if (_selectedPriceOption == 4 || _selectedPriceOption == 5)
            {
                FloatField value = new FloatField
                {
                    value = _searchPrice,
                    isDelayed = true
                };
                value.AddToClassList(SearchDetailCompactFieldClass);
                value.RegisterValueChangedCallback(evt =>
                {
                    _searchPrice = evt.newValue;
                    CommitNativeSearchFilterChange(host);
                });
                price.Add(value);
                price.Add(new Label(AI.Config.currency == 0 ? "EUR" : AI.Config.currency == 1 ? "USD" : "CNY"));
            }
            store.Add(form.CreateRow("Price", null, price));
            if (AI.Actions.ExtractColors)
            {
                VisualElement color = new VisualElement();
                color.AddToClassList(SearchDetailComparisonClass);
                color.Add(CreateNativeSearchPopup(_colorOptions, _selectedColorOption, value =>
                {
                    _selectedColorOption = value;
                    CommitNativeSearchFilterChange(host, true);
                }));
                if (_selectedColorOption > 0)
                {
                    ColorField field = new ColorField {value = _selectedColor};
                    field.RegisterValueChangedCallback(evt =>
                    {
                        _selectedColor = evt.newValue;
                        CommitNativeSearchFilterChange(host);
                    });
                    color.Add(field);
                }
                store.Add(form.CreateRow("Color", null, color));
            }
            store.Add(form.CreateRow("Packages", null,
                CreateNativeSearchPopup(_packageListingOptions, _selectedPackageTypes, value =>
                {
                    _selectedPackageTypes = value;
                    CommitNativeSearchFilterChange(host);
                })));
            store.Add(form.CreateRow("SRPs", null,
                CreateNativeSearchPopup(_srpOptions, _selectedPackageSRPs, value =>
                {
                    _selectedPackageSRPs = value;
                    CommitNativeSearchFilterChange(host);
                })));
            store.Add(form.CreateRow("Hidden Files", null,
                CreateNativeSearchPopup(_hiddenFilterOptions, _selectedHiddenFilter, value =>
                {
                    _selectedHiddenFilter = value;
                    CommitNativeSearchFilterChange(host);
                })));
            store.SetEnabled(!projectOnly && !inMemory);
            root.Add(store);

            Button reset = AssetInventoryUITK.CreatePrimaryButton("Reset Filters", () => ResetNativeSearchFilters(host));
            reset.tooltip = "Clear all filters and restore the default search.";
            reset.SetEnabled(IsSearchFilterActive() && !inMemory);
            root.Add(reset);
            return root;
        }

        private VisualElement CreateNativeSearchComparisonRow(
            string label,
            bool maximum,
            string value,
            string suffix,
            Action<bool, string> onChanged,
            SearchFilterViewHost host,
            string tooltip = null)
        {
            VisualElement controls = new VisualElement();
            controls.AddToClassList(SearchDetailComparisonClass);
            Button operation = null;
            operation = AssetInventoryUITK.CreateSecondaryButton(maximum ? "<=" : ">=", () =>
            {
                maximum = !maximum;
                onChanged(maximum, value);
                operation.text = maximum ? "<=" : ">=";
                operation.tooltip = GetNativeSearchComparisonTooltip(label, maximum);
                CommitNativeSearchFilterChange(host);
            });
            operation.tooltip = GetNativeSearchComparisonTooltip(label, maximum);
            operation.AddToClassList(SearchDetailComparisonOperatorClass);
            controls.Add(operation);

            TextField field = new TextField
            {
                value = value ?? string.Empty,
                isDelayed = true,
                tooltip = tooltip
            };
            field.AddToClassList(SearchDetailCompactFieldClass);
            field.RegisterValueChangedCallback(evt =>
            {
                value = evt.newValue;
                onChanged(maximum, value);
                CommitNativeSearchFilterChange(host);
            });
            controls.Add(field);
            if (!string.IsNullOrWhiteSpace(suffix)) controls.Add(new Label(suffix));
            return AssetInventoryUITK.CreateFormBuilder().CreateRow(label, tooltip, controls);
        }

        private static string GetNativeSearchComparisonTooltip(string label, bool maximum)
        {
            return maximum
                ? $"Match {label.ToLowerInvariant()} at or below this value. Click for minimum."
                : $"Match {label.ToLowerInvariant()} at or above this value. Click for maximum.";
        }

        private VisualElement CreateNativeSearchSettings()
        {
            VisualElement root = new VisualElement();
            CommonFormBuilder form = AssetInventoryUITK.CreateFormBuilder();

            VisualElement matches = AssetInventoryUITK.CreateSection("Search Matches");
            matches.AddToClassList(PackagesDetailSectionClass);
            matches.Add(form.CreateRow("Search In", "Field used for plain searches.",
                CreateNativeSearchPopup(_searchFields, AI.Config.searchField, value =>
                {
                    AI.Config.searchField = value;
                    CommitNativeSearchSetting(true);
                })));
            matches.Add(form.CreateToggleRow("Package Name", AI.Config.searchPackageNames, value =>
            {
                AI.Config.searchPackageNames = value;
                CommitNativeSearchSetting(true);
            }, "Search package names as well."));
            if (AI.Actions.AICaptionsEnabled)
            {
                matches.Add(form.CreateToggleRow("AI Captions", AI.Config.searchAICaptions, value =>
                {
                    AI.Config.searchAICaptions = value;
                    CommitNativeSearchSetting(true);
                }, "Search AI captions as well."));
            }
            if (ShouldShowSemanticSearchToggle(AI.Actions.SemanticSearchEnabled))
            {
                matches.Add(form.CreateToggleRow("Semantic Search", AI.Config.enableSemanticSearch, value =>
                {
                    AI.Config.enableSemanticSearch = value;
                    CommitNativeSearchSetting(true);
                }, "Use the local semantic index to find assets by meaning."));
            }
            if (ShowAdvanced())
            {
                VisualElement customTypes = AssetInventoryUITK.CreateStringListControl(
                    this,
                    AI.Config.customSearchTypeExtensions,
                    ";",
                    value =>
                    {
                        SetCustomSearchTypeExtensions(value);
                        ScheduleNativeSearchInspectorRebuild();
                    },
                    "Custom File Types",
                    "File extensions pinned in the type dropdown.",
                    editButtonClass: SearchSettingsIconButtonClass);
                matches.Add(form.CreateRow("Custom Types", null, customTypes));
            }
            root.Add(matches);

            VisualElement results = AssetInventoryUITK.CreateSection("Results & Sorting");
            results.AddToClassList(PackagesDetailSectionClass);
            VisualElement sort = new VisualElement();
            sort.AddToClassList(SearchDetailComparisonClass);
            sort.Add(CreateNativeSearchPopup(_sortFields, AI.Config.sortField, value =>
            {
                if (searchColumnState == null) EnsureSearchColumnState();
                AI.Config.sortField = value;
                int sourceColumnIndex = SearchTreeViewControl.GetSourceColumnIndex(value);
                AssetInventoryColumnLayoutCoordinator.UpdateSort(
                    AssetInventoryTableLayoutKind.Search,
                    null,
                    searchColumnState,
                    AssetInventoryColumnLayoutCoordinator.GetSearchColumnKey,
                    sourceColumnIndex,
                    AI.Config.sortDescending);
                CommitNativeSearchSetting(true);
            }));
            Button direction = AssetInventoryUITK.CreateIconButton(
                AI.Config.sortDescending ? "Descending" : "Ascending",
                AI.Config.sortDescending ? "d_scrollup" : "d_scrolldown",
                () =>
                {
                    if (searchColumnState == null) EnsureSearchColumnState();
                    AI.Config.sortDescending = !AI.Config.sortDescending;
                    int sourceColumnIndex = SearchTreeViewControl.GetSourceColumnIndex(AI.Config.sortField);
                    AssetInventoryColumnLayoutCoordinator.UpdateSort(
                        AssetInventoryTableLayoutKind.Search,
                        null,
                        searchColumnState,
                        AssetInventoryColumnLayoutCoordinator.GetSearchColumnKey,
                        sourceColumnIndex,
                        AI.Config.sortDescending);
                    CommitNativeSearchSetting(true, true);
                });
            direction.AddToClassList(SearchSettingsIconButtonClass);
            sort.Add(direction);
            results.Add(form.CreateRow("Sort by", "Choose the order used for search results.", sort));
            results.Add(form.CreateRow("Maximum", "Limit the number of results returned by a search.",
                CreateNativeSearchPopup(_resultSizes, AI.Config.maxResults, value =>
                {
                    AI.Config.maxResults = value;
                    CommitNativeSearchSetting(true);
                })));
            if (ShowAdvanced())
            {
                results.Add(form.CreateIntegerRow("In-Memory", AI.Config.maxInMemoryResults, value =>
                {
                    AI.Config.maxInMemoryResults = Mathf.Max(1, value);
                    CommitNativeSearchSetting(true);
                }, tooltip: "Maximum result count in High-Speed Mode."));
                results.Add(form.CreateIntegerRow("In Project", AI.Config.maxProjectSearchResults, value =>
                {
                    AI.Config.maxProjectSearchResults = value;
                    CommitNativeSearchSetting(true);
                }, tooltip: "Set to zero or less for no project result limit."));
                results.Add(form.CreateToggleRow("Show Index Scope", AI.Config.showIndexSearchScope, value =>
                {
                    AI.Config.showIndexSearchScope = value;
                    CommitNativeSearchSetting(true);
                    RebuildNativeSearchBody();
                }, "Expose the Index-only search scope."));
            }
            root.Add(results);
            root.Add(CreateNativeSearchViewSettings(form));

            VisualElement defaults = AssetInventoryUITK.CreateSection("Default Filters");
            defaults.AddToClassList(PackagesDetailSectionClass);
            defaults.Add(form.CreateToggleRow("Hide Extensions", AI.Config.excludeExtensions, value =>
            {
                AI.Config.excludeExtensions = value;
                CommitNativeSearchSetting(true, true);
            }, "Hide configured file extensions when searching all types."));
            if (AI.Config.excludeExtensions)
            {
                VisualElement extensions = AssetInventoryUITK.CreateStringListControl(
                    this,
                    AI.Config.excludedExtensions,
                    ";",
                    value =>
                    {
                        AI.Config.excludedExtensions = value;
                        CommitNativeSearchSetting(true);
                    },
                    "Hidden Extensions",
                    "File extensions hidden from search results.",
                    editButtonClass: SearchSettingsIconButtonClass);
                defaults.Add(form.CreateRow("Extensions", null, extensions));
            }
            defaults.Add(form.CreateRow("Previews", "Show all files, only files with previews, or only files without previews.",
                CreateNativeSearchPopup(_previewOptions, AI.Config.previewVisibility, value =>
                {
                    AI.Config.previewVisibility = value;
                    CommitNativeSearchSetting(true);
                })));
            if (ShowAdvanced())
            {
                defaults.Add(form.CreateToggleRow("Sub-Packages", AI.Config.searchSubPackages, value =>
                {
                    AI.Config.searchSubPackages = value;
                    CommitNativeSearchSetting(true);
                }, "Search sub-packages when filtering by package."));
                defaults.Add(form.CreateToggleRow("Exclude Wrong SRPs", AI.Config.excludeIncompatibleSRPs, value =>
                {
                    AI.Config.excludeIncompatibleSRPs = value;
                    CommitNativeSearchSetting(true);
                }, "Exclude packages that do not match the active render pipeline."));
            }
            root.Add(defaults);

            VisualElement behavior = AssetInventoryUITK.CreateSection("Behavior");
            behavior.AddToClassList(PackagesDetailSectionClass);
            behavior.Add(form.CreateToggleRow("Search While Typing", AI.Config.searchAutomatically, value =>
            {
                AI.Config.searchAutomatically = value;
                CommitNativeSearchSetting(false);
            }, "Run searches automatically after typing pauses."));
            behavior.Add(form.CreateToggleRow("Search Without Input", AI.Config.searchWithoutInput, value =>
            {
                AI.Config.searchWithoutInput = value;
                CommitNativeSearchSetting(true);
            }, "Allow an empty search to return all matching files."));
            behavior.Add(form.CreateToggleRow("Auto-Play Audio", AI.Config.autoPlayAudio, value =>
            {
                AI.Config.autoPlayAudio = value;
                CommitNativeSearchSetting(false);
            }, "Automatically play audio when an audio result is selected."));
            behavior.Add(form.CreateRow("Dependencies", "Choose whether dependency information is calculated when a result is selected.",
                CreateNativeSearchPopup(_dependencyOptions, AI.Config.autoCalculateDependencies, value =>
                {
                    AI.Config.autoCalculateDependencies = value;
                    CommitNativeSearchSetting(true);
                })));
            root.Add(behavior);

            VisualElement actions = AssetInventoryUITK.CreateSection("Selection & Actions");
            actions.AddToClassList(PackagesDetailSectionClass);
            actions.Add(form.CreateToggleRow("Ping Selected", AI.Config.pingSelected, value =>
            {
                AI.Config.pingSelected = value;
                CommitNativeSearchSetting(false);
            }, "Highlight a selected project file in Unity's Project window."));
            actions.Add(form.CreateToggleRow("Ping Imported", AI.Config.pingImported, value =>
            {
                AI.Config.pingImported = value;
                CommitNativeSearchSetting(false);
            }, "Highlight a file in Unity's Project window after importing it."));
            actions.Add(form.CreateRow("Double-Click", "Choose what happens when a search result is double-clicked.",
                CreateNativeSearchPopup(_doubleClickOptions, AI.Config.doubleClickAction, value =>
                {
                    AI.Config.doubleClickAction = value;
                    CommitNativeSearchSetting(false);
                })));
            actions.Add(form.CreateRow("Alt + Double", "Choose the alternate double-click action while Alt is held.",
                CreateNativeSearchPopup(_doubleClickOptions, AI.Config.doubleClickAltAction, value =>
                {
                    AI.Config.doubleClickAltAction = value;
                    CommitNativeSearchSetting(false);
                })));
            if (ShowAdvanced())
            {
                actions.Add(form.CreateToggleRow("Disable Drag & Drop", AI.Config.disableDragDrop, value =>
                {
                    AI.Config.disableDragDrop = value;
                    CommitNativeSearchSetting(false);
                }));
            }
            root.Add(actions);

            return root;
        }

        private VisualElement CreateNativeSearchViewSettings(CommonFormBuilder form)
        {
            VisualElement view = AssetInventoryUITK.CreateSection("View & Display");
            view.AddToClassList(PackagesDetailSectionClass);
            _nativeSearchTileDetailPopup = CreateNativeSearchPopup(
                SearchGridDetailOptions,
                (int)GetNativeSearchGridDisplayMode(),
                SetNativeSearchGridDetail);
            view.Add(form.CreateRow(
                "Tile Detail",
                "Choose the overall information density and matching tile-size preset.",
                _nativeSearchTileDetailPopup));
            view.Add(form.CreateRow("Tile Text", null,
                CreateNativeSearchPopup(_tileTitle, AI.Config.tileText, value =>
                {
                    AI.Config.tileText = value;
                    CommitNativeSearchSetting(true);
                })));
            if (!ShowAdvanced()) return view;

            view.Add(form.CreateToggleRow("Group Lists", AI.Config.groupLists, value =>
            {
                AI.Config.groupLists = value;
                AI.SaveConfig();
                ReloadLookups();
                ScheduleNativeSearchInspectorRebuild();
            }));
            view.Add(form.CreateToggleRow("Show Workspaces", AI.Config.alwaysShowWorkspaces, value =>
            {
                AI.Config.alwaysShowWorkspaces = value;
                CommitNativeSearchSetting(false);
            }));

            Slider ratio = new Slider(0.3f, 3f) {value = AI.Config.searchTileAspectRatio};
            ratio.RegisterValueChangedCallback(evt =>
            {
                AI.Config.searchTileAspectRatio = evt.newValue;
                _lastTileSizeChange = DateTime.Now;
                CommitNativeSearchSetting(false);
                RefreshNativeSearchGridView();
            });
            view.Add(form.CreateRow("Tile Aspect", "Adjusts tile height.", ratio));

            SliderInt margin = new SliderInt(-3, 30) {value = AI.Config.tileMargin};
            margin.RegisterValueChangedCallback(evt =>
            {
                AI.Config.tileMargin = evt.newValue;
                _lastTileSizeChange = DateTime.Now;
                CommitNativeSearchSetting(false);
                RefreshNativeSearchGridView();
            });
            view.Add(form.CreateRow("Tile Margins", null, margin));
            view.Add(form.CreateIntegerRow("Corner Radius", AI.Config.tileCornerRadius, value =>
            {
                AI.Config.tileCornerRadius = Mathf.Max(0, value);
                CommitNativeSearchSetting(true);
            }, "pixels"));
            view.Add(form.CreateToggleRow("Play All Animations", AI.Config.playVisibleSearchAnimations, value =>
            {
                AI.Config.playVisibleSearchAnimations = value;
                AI.SaveConfig();
                if (value) TriggerVisibleAnimationsUpdate();
                else DisposeAllVisibleAnimations(true);
                ScheduleNativeSearchInspectorRebuild();
            }));
            if (AI.Config.playVisibleSearchAnimations)
            {
                view.Add(form.CreateIntegerRow("Max Animations", AI.Config.maxVisibleSearchAnimations, value =>
                {
                    AI.Config.maxVisibleSearchAnimations = Mathf.Max(1, value);
                    CommitNativeSearchSetting(false);
                }));
            }
            SliderInt rowHeight = new SliderInt(16, 256) {value = AI.Config.searchListRowHeight};
            rowHeight.RegisterValueChangedCallback(evt =>
            {
                AI.Config.searchListRowHeight = evt.newValue;
                _nativeSearchTreeAdapter?.RefreshRowHeight();
                _nativeSearchTreeAdapter?.RepaintCells();
                CommitNativeSearchSetting(false);
            });
            view.Add(form.CreateRow("List Row Height", null, rowHeight));
            return view;
        }

        private PopupStringField CreateNativeSearchPopup(string[] options, int selectedIndex, Action<int> onChanged)
        {
            List<string> items = options?.ToList() ?? new List<string>();
            if (items.Count == 0) items.Add(string.Empty);
            PopupStringField popup = new PopupStringField(items, Mathf.Clamp(selectedIndex, 0, items.Count - 1));
            popup.RegisterValueChangedCallback(evt =>
            {
                int index = items.IndexOf(evt.newValue);
                if (index >= 0) onChanged?.Invoke(index);
            });
            return popup;
        }

        private int GetNativeSearchFilterStateHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + _selectedPackageTag;
                hash = hash * 31 + _selectedFileTag;
                hash = hash * 31 + _selectedAsset;
                hash = hash * 31 + _selectedPublisher;
                hash = hash * 31 + _selectedCategory;
                hash = hash * 31 + _selectedImageType;
                hash = hash * 31 + _selectedPriceOption;
                hash = hash * 31 + _searchPrice.GetHashCode();
                hash = hash * 31 + _selectedColorOption;
                hash = hash * 31 + _selectedColor.GetHashCode();
                hash = hash * 31 + _selectedPackageTypes;
                hash = hash * 31 + _selectedPackageSRPs;
                hash = hash * 31 + _selectedHiddenFilter;
                hash = hash * 31 + (_searchWidth?.GetHashCode() ?? 0);
                hash = hash * 31 + (_checkMaxWidth ? 1 : 0);
                hash = hash * 31 + (_searchHeight?.GetHashCode() ?? 0);
                hash = hash * 31 + (_checkMaxHeight ? 1 : 0);
                hash = hash * 31 + (_searchLength?.GetHashCode() ?? 0);
                hash = hash * 31 + (_checkMaxLength ? 1 : 0);
                hash = hash * 31 + (_searchVertexCount?.GetHashCode() ?? 0);
                hash = hash * 31 + (_checkMaxVertexCount ? 1 : 0);
                hash = hash * 31 + (_searchSize?.GetHashCode() ?? 0);
                hash = hash * 31 + (_checkMaxSize ? 1 : 0);
                hash = hash * 31 + (GetRawSearchType()?.GetHashCode() ?? 0);
                hash = hash * 31 + (int)GetConfiguredSearchScope();
                hash = hash * 31 + (int)_inMemoryMode;
                hash = hash * 31 + Tagging.TagHash;
                hash = hash * 31 + (AI.Actions.ExtractColors ? 1 : 0);
                hash = hash * 31 + (AI.Config.aiCaptionsFeatureEnabled ? 1 : 0);
                hash = hash * 31 + (AI.Config.semanticSearchFeatureEnabled ? 1 : 0);
                hash = AddNativeSearchHeaderOptionsHash(hash, _assetNames);
                hash = AddNativeSearchHeaderOptionsHash(hash, _publisherNames);
                hash = AddNativeSearchHeaderOptionsHash(hash, _categoryNames);
                hash = AddNativeSearchHeaderOptionsHash(hash, _imageTypeOptions);
                return hash;
            }
        }

        private void CommitNativeSearchFilterChange(SearchFilterViewHost source, bool rebuildContent = false)
        {
            _activeSavedSearchId = -1;
            _nativeSearchSavedSearchesDirty = true;
            _requireSearchUpdate = true;
            _keepSearchResultPage = false;
            _curPage = 1;
            RefreshNativeSearchFilterChip();
            _nativeSearchInspectorPane?.SetTabs(GetNativeSearchInspectorTabs(), GetNativeSearchInspectorTabIndex(), SelectNativeSearchInspectorTab);
            SynchronizeNativeSearchFilterViews(source, rebuildContent);
        }

        private void SynchronizeNativeSearchFilterViews(SearchFilterViewHost source, bool rebuildSource)
        {
            if (_searchInspectorTab == 1)
            {
                if (rebuildSource || source != SearchFilterViewHost.Inspector)
                {
                    ScheduleNativeSearchInspectorRebuild();
                }
                else
                {
                    _nativeSearchInspectorContentStateHash = GetNativeSearchInspectorContentStateHash();
                }
            }

            if (IsNativeSearchFilterSidebarMode())
            {
                if (rebuildSource || source != SearchFilterViewHost.Sidebar)
                {
                    ScheduleNativeSearchSidebarFiltersRebuild();
                }
                else
                {
                    _nativeSearchSidebarFiltersStateHash = GetNativeSearchFilterStateHash();
                }
            }
        }

        private void ResetNativeSearchFilters()
        {
            ResetNativeSearchFilters(SearchFilterViewHost.External);
        }

        private void ResetNativeSearchFilters(SearchFilterViewHost source)
        {
            ResetSearch(true, false);
            _requireSearchUpdate = true;
            _keepSearchResultPage = false;
            _curPage = 1;
            _nativeSearchSavedSearchesDirty = true;
            RefreshNativeSearchFilterChip();
            SynchronizeNativeSearchFilterViews(source, true);
        }

        private void CommitNativeSearchSetting(bool refreshResults, bool rebuildContent = false)
        {
            AI.SaveConfig();
            SyncNativeSearchSortIndicator();
            if (refreshResults)
            {
                _requireSearchUpdate = true;
                _keepSearchResultPage = false;
                _curPage = 1;
            }
            if (rebuildContent) ScheduleNativeSearchInspectorRebuild();
        }
    }
}
