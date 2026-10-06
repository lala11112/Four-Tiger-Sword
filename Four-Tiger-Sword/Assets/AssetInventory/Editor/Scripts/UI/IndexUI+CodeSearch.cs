using ImpossibleRobert.Common;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public partial class IndexUI
    {
        private string _codeSearchPhrase = string.Empty;
        private CodeSearch.Result _codeSearchResult;
        private int _activeSavedCodeSearchId = -1;
        private int _codeSearchPage = 1;
        private List<SavedCodeSearch> _codeSearches;
        private bool _codeSearchesLoaded;
        private const int CodeSearchFilesPerPage = 25;
        private const string CodeRootClass = "ai-code-root";
        private const string CodeControlsClass = "ai-code-controls";
        private const string CodeSearchRowClass = "ai-code-search-row";
        private const string CodeSearchActionWrapperClass = "ai-code-search-action-wrapper";
        private const string CodeSearchActionGroupClass = "ai-code-search-action-group";
        private const string CodeSearchLabelClass = "ai-code-search-label";
        private const string CodeSearchFieldClass = "ai-code-search-field";
        private const string CodeSearchSaveClass = "ai-code-search-save";
        private const string CodeSearchGoClass = "ai-code-search-go";
        private const string CodeEmptyStateClass = "ai-code-empty";
        private const string CodeResultsClass = "ai-code-results";
        private const string CodeResultMessageClass = "ai-code-result-message";
        private const string CodeFileCardClass = "ai-code-file-card";
        private const string CodeFileHeaderClass = "ai-code-file-header";
        private const string CodeFileIconClass = "ai-code-file-icon";
        private const string CodeFileTextClass = "ai-code-file-text";
        private const string CodeFilePathClass = "ai-code-file-path";
        private const string CodeFileMetaClass = "ai-code-file-meta";
        private const string CodeSmallButtonClass = "ai-code-small-button";
        private const string CodeMatchCardClass = "ai-code-match-card";
        private const string CodeMatchHeaderClass = "ai-code-match-header";
        private const string CodeMatchTitleClass = "ai-code-match-title";
        private const string CodeMatchActionsClass = "ai-code-match-actions";
        private const string CodeSnippetClass = "ai-code-snippet";
        private ToolbarSearchField _nativeCodeSearchField;
        private VisualElement _nativeCodeSavedSearches;
        private CommonEmptyState _nativeCodeEmptyState;
        private ScrollView _nativeCodeResults;
        private CommonPaginationControl _nativeCodePager;
        private Label _nativeCodeFooterSummary;
        private VisualElement _nativeCodeScopeBar;
        private CodeSearch.Result _nativeCodeRenderedResult;
        private int _nativeCodeRenderedPage;
        private bool _nativeCodeResultsDirty = true;
        private bool _nativeCodeSavedSearchesDirty = true;
        private bool _nativeCodeSavedSearchesShowAdvanced;
        private int _nativeCodeAdvancedVisibilityStateHash;
        private string _nativeCodeEmptyStateMessage;

        private List<SavedCodeSearch> CodeSearches
        {
            get
            {
                if (_codeSearches == null || !_codeSearchesLoaded)
                {
                    _codeSearches = DBAdapter.DB.Table<SavedCodeSearch>().ToList();
                    _codeSearchesLoaded = true;
                }
                return _codeSearches;
            }
        }

        private void RefreshNativeCodeSearchBody()
        {
            if (_nativeCodeBody == null) return;

            if (_nativeCodeBody.childCount == 0 ||
                AssetInventoryUITK.AdvancedVisibilityStateChanged(ref _nativeCodeAdvancedVisibilityStateHash))
            {
                RebuildNativeCodeSearchBody();
            }

            RefreshNativeCodeSearchState();
        }

        private void RebuildNativeCodeSearchBody()
        {
            if (_nativeCodeBody == null) return;

            _nativeScrollViewState.Capture("code-results", _nativeCodeResults);
            _nativeCodeBody.Clear();
            _nativeCodeBody.AddToClassList(CodeRootClass);

            _nativeCodeSavedSearches = AssetInventoryUITK.CreateSavedSearchStrip();
            _nativeCodeBody.Add(_nativeCodeSavedSearches);

            VisualElement controls = AssetInventoryUITK.CreateSection();
            controls.AddToClassList(CodeControlsClass);
            VisualElement row = new VisualElement();
            row.AddToClassList(CodeSearchRowClass);
            row.AddToClassList(AssetInventoryUITK.CompactSearchToolbarClass);
            _nativeCodeSearchField = null;
            VisualElement searchBlock = AssetInventoryUITK.CreateAdvancedVisibilityBlock("code.actions.search", () =>
            {
                VisualElement group = new VisualElement();
                group.AddToClassList(CodeSearchActionGroupClass);

                Label label = new Label("Search");
                label.AddToClassList(CodeSearchLabelClass);
                group.Add(label);

                _nativeCodeSearchField = new ToolbarSearchField
                {
                    value = _codeSearchPhrase ?? string.Empty,
                    tooltip = "Search indexed code"
                };
                _nativeCodeSearchField.AddToClassList(CodeSearchFieldClass);
                _nativeCodeSearchField.RegisterValueChangedCallback(evt =>
                {
                    _codeSearchPhrase = evt.newValue ?? string.Empty;
                    _activeSavedCodeSearchId = -1;
                    _codeSearchPage = 1;
                    RefreshNativeCodeSearchState();
                });
                _nativeCodeSearchField.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;

                    ExecuteCodeSearch(false);
                    evt.StopPropagation();
                });
                group.Add(_nativeCodeSearchField);

                Button go = AssetInventoryUITK.CreatePrimaryButton("Go", () => ExecuteCodeSearch(false));
                go.tooltip = "Search the code index.";
                go.AddToClassList(CodeSearchGoClass);
                group.Add(go);

                return group;
            }, onVisibilityChanged: RebuildNativeCodeSearchBody);
            searchBlock.AddToClassList(CodeSearchActionWrapperClass);
            row.Add(searchBlock);

            row.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("code.actions.savedsearches", () =>
            {
                Button save = AssetInventoryUITK.CreateIconButton(
                    "Save current code search",
                    "d_saveas",
                    SaveCurrentCodeSearch);
                save.AddToClassList(CodeSearchSaveClass);
                return save;
            }, onVisibilityChanged: RebuildNativeCodeSearchBody));
            controls.Add(row);
            _nativeCodeBody.Add(controls);

            _nativeCodeEmptyState = AssetInventoryUITK.CreateEmptyState(null);
            _nativeCodeEmptyState.AddToClassList(CodeEmptyStateClass);
            _nativeCodeBody.Add(_nativeCodeEmptyState);

            _nativeCodeResults = new ScrollView(ScrollViewMode.Vertical);
            _nativeCodeResults.AddToClassList(CodeResultsClass);
            _nativeCodeBody.Add(_nativeCodeResults);
            _nativeScrollViewState.Restore("code-results", _nativeCodeResults);

            CommonUITK.ThreeZoneLayout footer = AssetInventoryUITK.CreateNavigationFooterLayout();
            VisualElement centerGroup = footer.Center;
            _nativeCodeFooterSummary = new Label();
            _nativeCodeFooterSummary.AddToClassList(AssetInventoryUITK.NavigationFooterSummaryClass);
            centerGroup.Add(_nativeCodeFooterSummary);

            _nativeCodePager = AssetInventoryUITK.CreatePaginationControl(this);
            centerGroup.Add(_nativeCodePager);

            _nativeCodeScopeBar = null;
            centerGroup.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("code.actions.scope", () =>
            {
                _nativeCodeScopeBar = CreateNativeSearchScopeControl(() =>
                {
                    if (_codeSearchResult != null)
                    {
                        ExecuteCodeSearch(false);
                    }
                    else
                    {
                        InvalidateNativeCodeSearchBody();
                    }
                });
                return _nativeCodeScopeBar;
            }, onVisibilityChanged: RebuildNativeCodeSearchBody));
            _nativeCodeBody.Add(footer.Root);

            _nativeCodeSavedSearchesDirty = true;
            _nativeCodeSavedSearchesShowAdvanced = ShowAdvanced();
            _nativeCodeAdvancedVisibilityStateHash = AssetInventoryUITK.GetAdvancedVisibilityStateHash();
            _nativeCodeEmptyStateMessage = null;
        }

        private void RefreshNativeCodeSearchState()
        {
            if (_nativeCodeBody == null || _nativeCodeBody.childCount == 0) return;

            string phrase = _codeSearchPhrase ?? string.Empty;
            if (_nativeCodeSearchField != null && _nativeCodeSearchField.value != phrase)
            {
                _nativeCodeSearchField.SetValueWithoutNotify(phrase);
            }

            RefreshNativeCodeSavedSearches();
            RefreshNativeCodeEmptyState();
            RefreshNativeCodeFooterSummary();
            RefreshNativeCodePager();
            RefreshNativeCodeScopeBar();

            bool showResults = _codeSearchResult != null;
            if (_nativeCodeEmptyState != null)
            {
                _nativeCodeEmptyState.style.display = showResults ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (_nativeCodeResults != null)
            {
                _nativeCodeResults.style.display = showResults ? DisplayStyle.Flex : DisplayStyle.None;
                if (showResults)
                {
                    RefreshNativeCodeResults();
                }
            }
        }

        private void RefreshNativeCodeSavedSearches()
        {
            if (_nativeCodeSavedSearches == null) return;

            int searchCount = CodeSearches.Count;
            bool showAdvanced = ShowAdvanced();
            if (_nativeCodeSavedSearchesDirty ||
                _nativeCodeSavedSearches.childCount != searchCount ||
                _nativeCodeSavedSearchesShowAdvanced != showAdvanced)
            {
                RebuildNativeCodeSavedSearches();
                return;
            }

            for (int i = 0; i < searchCount; i++)
            {
                VisualElement group = _nativeCodeSavedSearches.ElementAt(i);
                Button button = AssetInventoryUITK.FindSavedSearchPill(group);
                if (button != null)
                {
                    AssetInventoryUITK.SetSavedSearchActive(button, CodeSearches[i].Id == _activeSavedCodeSearchId);
                }
            }
        }

        private void RebuildNativeCodeSavedSearches()
        {
            if (_nativeCodeSavedSearches == null) return;

            _nativeCodeSavedSearches.Clear();
            _nativeCodeSavedSearches.style.display = CodeSearches.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _nativeCodeSavedSearchesShowAdvanced = ShowAdvanced();
            foreach (SavedCodeSearch search in CodeSearches)
            {
                _nativeCodeSavedSearches.Add(CreateNativeSavedCodeSearchPillGroup(search, _nativeCodeSavedSearchesShowAdvanced));
            }
            _nativeCodeSavedSearchesDirty = false;
        }

        private VisualElement CreateNativeSavedCodeSearchPillGroup(SavedCodeSearch search, bool hasMenu)
        {
            return AssetInventoryUITK.CreateSavedSearchPillGroup(
                GetNativeSavedCodeSearchLabel(search),
                search.SearchPhrase ?? string.Empty,
                search.Icon,
                search.Color,
                search.Id == _activeSavedCodeSearchId,
                hasMenu,
                () => SelectNativeSavedCodeSearch(search),
                anchor => ShowNativeSavedCodeSearchMenu(search, anchor),
                search);
        }

        private static string GetNativeSavedCodeSearchLabel(SavedCodeSearch search)
        {
            if (!string.IsNullOrWhiteSpace(search.Name)) return search.Name;
            if (!string.IsNullOrWhiteSpace(search.SearchPhrase)) return search.SearchPhrase;
            return "Code Search";
        }

        private void SelectNativeSavedCodeSearch(SavedCodeSearch search)
        {
            if (_activeSavedCodeSearchId == search.Id)
            {
                _activeSavedCodeSearchId = -1;
                RefreshNativeCodeSearchState();
                return;
            }

            LoadCodeSearch(search);
        }

        private void ShowNativeSavedCodeSearchMenu(SavedCodeSearch search, VisualElement anchor)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Edit"), false, () =>
            {
                SavedCodeSearchUI savedSearchUI = SavedCodeSearchUI.ShowWindow();
                savedSearchUI.Init(search, OnNativeSavedCodeSearchEdited);
            });
            menu.AddItem(new GUIContent("Override with Current Search"), false, () => OverrideCodeSearch(search));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete"), false, () =>
            {
                if (!EditorUtility.DisplayDialog("Confirm", $"Do you really want to delete the saved code search '{search.Name}'?", "Yes", "No")) return;

                DBAdapter.DB.Delete(search);
                CodeSearches.Remove(search);
                if (_activeSavedCodeSearchId == search.Id) _activeSavedCodeSearchId = -1;
                _nativeCodeSavedSearchesDirty = true;
                InvalidateNativeCodeSearchBody();
            });
            CommonUITK.ShowGenericMenu(menu, anchor);
        }

        private void OnNativeSavedCodeSearchEdited(SavedCodeSearch search)
        {
            _nativeCodeSavedSearchesDirty = true;
            RefreshNativeCodeSearchState();
        }

        private void RefreshNativeCodeEmptyState()
        {
            if (_nativeCodeEmptyState == null || _codeSearchResult != null) return;

            bool indexExists = CodeIndexService.Exists();
            string message = indexExists
                ? "Search indexed code by name or symbol. Optional filters include path:, ext:, package:, and source:."
                : "Build the code index first. It is optional and can be enabled from Settings when you need source search.";
            if (_nativeCodeEmptyStateMessage == message && _nativeCodeEmptyState.childCount > 0) return;

            Button settings = indexExists
                ? null
                : AssetInventoryUITK.CreatePrimaryButton("Open Code Index Settings", () => SelectUITKTab(AssetInventoryTab.Settings));
            _nativeCodeEmptyState.SetContent(indexExists ? "Search indexed code" : "Code index is not enabled", message, actions: new[] {settings});
            _nativeCodeEmptyStateMessage = message;
        }

        private void RefreshNativeCodeFooterSummary()
        {
            if (_nativeCodeFooterSummary == null) return;

            if (_codeSearchResult == null)
            {
                _nativeCodeFooterSummary.text = string.Empty;
                _nativeCodeFooterSummary.style.display = DisplayStyle.None;
                return;
            }

            _nativeCodeFooterSummary.text = $"{_codeSearchResult.DocumentCount:N0} files, {_codeSearchResult.ResultCount:N0} matches";
            _nativeCodeFooterSummary.style.display = DisplayStyle.Flex;
        }

        private void RefreshNativeCodeResults()
        {
            if (_nativeCodeResults == null) return;
            if (!_nativeCodeResultsDirty
                && ReferenceEquals(_nativeCodeRenderedResult, _codeSearchResult)
                && _nativeCodeRenderedPage == _codeSearchPage)
            {
                return;
            }

            _nativeCodeResults.Clear();
            _nativeCodeRenderedResult = _codeSearchResult;
            _nativeCodeRenderedPage = _codeSearchPage;
            _nativeCodeResultsDirty = false;

            if (_codeSearchResult == null) return;

            if (!string.IsNullOrEmpty(_codeSearchResult.Error))
            {
                VisualElement message = AssetInventoryUITK.CreateHelpBox(_codeSearchResult.Error, MessageType.Warning);
                message.AddToClassList(CodeResultMessageClass);
                _nativeCodeResults.Add(message);
            }
            else if (!_codeSearchResult.IndexExists)
            {
                VisualElement message = AssetInventoryUITK.CreateHelpBox(
                    "The code search index has not been created yet. Build it from the Settings tab when needed.",
                    MessageType.Info);
                message.AddToClassList(CodeResultMessageClass);
                _nativeCodeResults.Add(message);
                return;
            }

            if (_codeSearchResult.Files.Count == 0)
            {
                VisualElement message = AssetInventoryUITK.CreateHelpBox("No code matches found.", MessageType.Info);
                message.AddToClassList(CodeResultMessageClass);
                _nativeCodeResults.Add(message);
                return;
            }

            foreach (CodeSearch.CodeSearchFileResult file in _codeSearchResult.Files)
            {
                _nativeCodeResults.Add(CreateNativeCodeSearchFile(file));
            }
        }

        private VisualElement CreateNativeCodeSearchFile(CodeSearch.CodeSearchFileResult file)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList(CodeFileCardClass);

            VisualElement header = new VisualElement();
            header.AddToClassList(CodeFileHeaderClass);

            Image icon = new Image
            {
                image = GetCodeSearchFileIcon(file),
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList(CodeFileIconClass);
            header.Add(icon);

            VisualElement fileText = new VisualElement();
            fileText.AddToClassList(CodeFileTextClass);

            Label path = new Label(file.Path ?? string.Empty);
            path.tooltip = file.Path ?? string.Empty;
            path.AddToClassList(CodeFilePathClass);
            fileText.Add(path);

            string context = file.SourceKind == CodeDocument.SourceKindType.Project ? "Project" : file.PackageName;
            string fileContext = $"{context}  |  {file.Language}";
            Label meta = new Label(fileContext);
            meta.tooltip = fileContext;
            meta.AddToClassList(CodeFileMetaClass);
            fileText.Add(meta);
            header.Add(fileText);

            Button copyPath = AssetInventoryUITK.CreateSecondaryButton("Copy Path", () => EditorGUIUtility.systemCopyBuffer = file.Path);
            copyPath.tooltip = "Copy the indexed path.";
            copyPath.AddToClassList(CodeSmallButtonClass);
            header.Add(copyPath);

            card.Add(header);

            foreach (CodeSearch.CodeSearchMatch match in file.Matches)
            {
                card.Add(CreateNativeCodeSearchMatch(file, match));
            }

            return card;
        }

        private Texture GetCodeSearchFileIcon(CodeSearch.CodeSearchFileResult file)
        {
            string iconName = _staticPreviews.TryGetValue(file.Extension ?? string.Empty, out string previewIcon)
                ? previewIcon
                : "TextScriptImporter Icon";
            return EditorGUIUtility.IconContent(iconName).image;
        }

        private VisualElement CreateNativeCodeSearchMatch(CodeSearch.CodeSearchFileResult file, CodeSearch.CodeSearchMatch match)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList(CodeMatchCardClass);

            VisualElement header = new VisualElement();
            header.AddToClassList(CodeMatchHeaderClass);

            string title = string.IsNullOrWhiteSpace(match.Symbol)
                ? $"Lines {match.StartLine:N0}-{match.EndLine:N0}"
                : $"{match.Symbol}  |  Lines {match.StartLine:N0}-{match.EndLine:N0}";
            Label titleLabel = new Label(title);
            titleLabel.tooltip = title;
            titleLabel.AddToClassList(CodeMatchTitleClass);
            header.Add(titleLabel);

            VisualElement actions = new VisualElement();
            actions.AddToClassList(CodeMatchActionsClass);
            Button open = AssetInventoryUITK.CreateSecondaryButton("Open", () => OpenCodeSearchMatch(file, match.StartLine));
            open.tooltip = "Open this file at the first matching line.";
            open.AddToClassList(CodeSmallButtonClass);
            actions.Add(open);
            Button copySnippet = AssetInventoryUITK.CreateSecondaryButton("Copy Snippet", () => EditorGUIUtility.systemCopyBuffer = match.Snippet);
            copySnippet.tooltip = "Copy the displayed snippet.";
            copySnippet.AddToClassList(CodeSmallButtonClass);
            actions.Add(copySnippet);
            Button copyChunk = AssetInventoryUITK.CreateSecondaryButton("Copy Chunk", () => EditorGUIUtility.systemCopyBuffer = match.Content);
            copyChunk.tooltip = "Copy the full indexed chunk.";
            copyChunk.AddToClassList(CodeSmallButtonClass);
            actions.Add(copyChunk);
            header.Add(actions);

            card.Add(header);

            Label snippet = new Label(match.Snippet ?? string.Empty);
            snippet.AddToClassList(CodeSnippetClass);
            card.Add(snippet);

            return card;
        }

        private void RefreshNativeCodePager()
        {
            if (_nativeCodePager == null) return;
            int pageCount = GetCodeSearchPageCount();
            string tooltip = _codeSearchResult == null
                ? string.Empty
                : $"{_codeSearchResult.DocumentCount:N0} files, {_codeSearchResult.ResultCount:N0} matches";
            _nativeCodePager.SetState(
                _codeSearchPage,
                pageCount,
                tooltip,
                SetCodeSearchPage,
                _codeSearchResult != null);
        }

        private void RefreshNativeCodeScopeBar()
        {
            if (_nativeCodeScopeBar == null) return;

            RefreshNativeSearchScopeControl(_nativeCodeScopeBar);
        }

        private void InvalidateNativeCodeSearchBody()
        {
            if (_nativeCodeBody == null || !IsNativeCodeShellActive()) return;

            _nativeCodeResultsDirty = true;
            RefreshNativeCodeSearchState();
            _nativeCodeResults?.MarkDirtyRepaint();
            Repaint();
        }

        private void ExecuteCodeSearch(bool keepPage = false)
        {
            if (!keepPage) _codeSearchPage = 1;

            int pageSize = GetCodeSearchPageSize();
            CodeSearch.Options options = new CodeSearch.Options
            {
                SearchPhrase = _codeSearchPhrase,
                Scope = GetConfiguredSearchScope(),
                CurrentPage = Mathf.Max(1, _codeSearchPage),
                MaxFiles = pageSize,
                MaxMatchesPerFile = 5
            };
            _codeSearchResult = CodeSearch.Execute(options);
            int pageCount = GetCodeSearchPageCount();
            if (_codeSearchResult.IndexExists && string.IsNullOrEmpty(_codeSearchResult.Error) && pageCount > 0 && _codeSearchPage > pageCount)
            {
                _codeSearchPage = pageCount;
                options.CurrentPage = _codeSearchPage;
                _codeSearchResult = CodeSearch.Execute(options);
            }
            _nativeCodeResultsDirty = true;
            InvalidateNativeCodeSearchBody();
        }

        private int GetCodeSearchPageSize()
        {
            return CodeSearchFilesPerPage;
        }

        private int GetCodeSearchPageCount()
        {
            if (_codeSearchResult == null) return 0;
            return AssetUtils.GetPageCount(_codeSearchResult.DocumentCount, GetCodeSearchPageSize());
        }

        private void SetCodeSearchPage(int page)
        {
            int pageCount = Mathf.Max(1, GetCodeSearchPageCount());
            page = Mathf.Clamp(page, 1, pageCount);
            if (page == _codeSearchPage) return;

            _codeSearchPage = page;
            ExecuteCodeSearch(true);
        }

        private void LoadCodeSearch(SavedCodeSearch search)
        {
            _codeSearchPhrase = search.SearchPhrase;
            _activeSavedCodeSearchId = search.Id;
            ExecuteCodeSearch(false);
        }

        private void PopulateSavedCodeSearchFromCurrentState(SavedCodeSearch search)
        {
            search.SearchPhrase = _codeSearchPhrase;
        }

        private void SaveCurrentCodeSearch()
        {
            SavedCodeSearch search = new SavedCodeSearch
            {
                Name = string.IsNullOrWhiteSpace(_codeSearchPhrase) ? "Code Search" : _codeSearchPhrase,
                Color = "4F81BD"
            };
            PopulateSavedCodeSearchFromCurrentState(search);
            DBAdapter.DB.Insert(search);
            CodeSearches.Add(search);
            _activeSavedCodeSearchId = search.Id;

            SavedCodeSearchUI savedSearchUI = SavedCodeSearchUI.ShowWindow();
            savedSearchUI.Init(search, OnNativeSavedCodeSearchEdited);
            _nativeCodeSavedSearchesDirty = true;
            InvalidateNativeCodeSearchBody();
        }

        private void OverrideCodeSearch(SavedCodeSearch search)
        {
            PopulateSavedCodeSearchFromCurrentState(search);
            DBAdapter.DB.Update(search);
            _activeSavedCodeSearchId = search.Id;
            _nativeCodeSavedSearchesDirty = true;
            InvalidateNativeCodeSearchBody();
        }

        private static void OpenCodeSearchMatch(CodeSearch.CodeSearchFileResult file, int line)
        {
            string path = ResolveCodeSearchPath(file);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                EditorUtility.DisplayDialog("Open Code", "The indexed source file could not be found on disk. Updating the code search index may repair the path.", "OK");
                return;
            }

            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(path, Mathf.Max(1, line));
        }

        private static string ResolveCodeSearchPath(CodeSearch.CodeSearchFileResult file)
        {
            if (!string.IsNullOrWhiteSpace(file.PhysicalPath) && File.Exists(file.PhysicalPath)) return file.PhysicalPath;
            if (file.SourceKind == CodeDocument.SourceKindType.Project && !string.IsNullOrWhiteSpace(file.Path))
            {
                string projectPath = Path.GetFullPath(file.Path);
                if (File.Exists(projectPath)) return projectPath;
            }
            return file.PhysicalPath;
        }
    }
}
