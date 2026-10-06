using System;
using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UIElementsTreeView = UnityEngine.UIElements.TreeView;

namespace AssetInventory
{
    public class HideContentUI : BasicEditorUI
    {
        private static readonly string[] RuleModeOptions = {"Hide Rules", "Include Rules"};
        private const string HideRootClass = "ai-hide-content-root";
        private const string HideSubtitleClass = "ai-hide-content-subtitle";
        private const string HideModeBarClass = "ai-hide-content-mode-bar";
        private const string HideModeButtonClass = "ai-hide-content-mode-button";
        private const string HideModeButtonActiveClass = "ai-hide-content-mode-button-active";
        private const string HideRuleHelpClass = "ai-hide-content-rule-help";
        private const string HideEditorContainerClass = "ai-hide-content-editor";
        private const string HideFilesSectionClass = "ai-hide-content-files-section";
        private const string HideRulesSectionClass = "ai-hide-content-rules-section";
        private const string HideTreeClass = "ai-hide-content-tree";
        private const string HideTreeRowClass = "ai-hide-content-tree-row";
        private const string HideTreeToggleClass = "ai-hide-content-tree-toggle";
        private const string HideTreeIconClass = "ai-hide-content-tree-icon";
        private const string HideTreeLabelClass = "ai-hide-content-tree-label";
        private const string HideRuleHintClass = "ai-hide-content-rule-hint";
        private const string HideRuleScrollClass = "ai-hide-content-rule-scroll";
        private const string HideRuleRowClass = "ai-hide-content-rule-row";
        private const string HideRuleFieldClass = "ai-hide-content-rule-field";
        private const string HideRuleAddRowClass = "ai-hide-content-rule-add-row";
        private const string HideManualRuleClass = "ai-hide-content-manual-rule";

        private AssetInfo _info;
        private string _displayName;
        private bool _initialized;
        private bool _uitkActive;

        private TreeModel<FileTreeElement> _treeModel;
        private Dictionary<string, FileTreeElement> _pathToElementMap;
        private UIElementsTreeView _nativeTreeView;
        private VisualElement _nativeRulesList;
        private VisualElement _nativeManualRulesList;
        private Button _nativeAddRuleButton;

        private string _exclusionRules = "";
        private List<string> _rules = new List<string>();
        private List<string> _manualRules = new List<string>();
        private HideRuleMode _ruleMode = HideRuleMode.Hide;
        private string _newRule = "";

        public static HideContentUI ShowWindow()
        {
            HideContentUI window = GetWindow<HideContentUI>("Hide Package Content");
            window.minSize = new Vector2(700, 450);
            return window;
        }

        public void Init(AssetInfo info)
        {
            _info = info;
            _displayName = info.GetDisplayName();
            _initialized = false;
            BuildTree();
            BuildIfReady();
        }

        internal void InitForTests(AssetInfo info, List<string> rules)
        {
            InitForTests(info, HideRuleMode.Hide, rules);
        }

        internal void InitForTests(AssetInfo info, HideRuleMode ruleMode, List<string> rules)
        {
            _info = info;
            _displayName = info.GetDisplayName();
            _ruleMode = ruleMode;
            _rules = rules != null ? new List<string>(rules) : new List<string>();
            _manualRules = new List<string>();
            SyncRulesString();
        }

        internal void SaveExclusionRulesForTests()
        {
            SaveExclusionRules();
        }

        internal void InitSelectionForTests(AssetInfo info, HideRuleMode ruleMode, Dictionary<string, FileTreeElement> pathToElementMap)
        {
            InitSelectionForTests(info, ruleMode, pathToElementMap, null);
        }

        internal void InitSelectionForTests(AssetInfo info, HideRuleMode ruleMode, Dictionary<string, FileTreeElement> pathToElementMap, List<string> rules)
        {
            _info = info;
            _displayName = info.GetDisplayName();
            _ruleMode = ruleMode;
            SetStoredRules(rules);
            _pathToElementMap = pathToElementMap;
            _initialized = true;
            SyncRulesString();
        }

        internal void SwitchRuleModeForTests(HideRuleMode newMode)
        {
            SwitchRuleMode(newMode);
        }

        internal List<string> GetRulesForTests()
        {
            return new List<string>(_rules);
        }

        internal List<string> GetManualRulesForTests()
        {
            return new List<string>(_manualRules);
        }

        internal void RefreshRulesFromSelectionForTests()
        {
            RefreshRulesFromTreeSelection();
        }

        internal void ApplyPatternMarkingsForTests()
        {
            ApplyPatternMarkings();
        }

        internal void ApplyLoadedRulesForTests()
        {
            ApplyManualRuleSelection();
            ApplyPatternMarkings();
        }

        private void BuildTree()
        {
            List<AssetFile> files = DBAdapter.DB.Query<AssetFile>("SELECT * FROM AssetFile WHERE AssetId=?", _info.AssetId);
            if (files.Count == 0)
            {
                _initialized = true;
                return;
            }

            FileTreeBuilder.ModelResult result = FileTreeBuilder.BuildModel(files);
            _treeModel = result.Model;
            _pathToElementMap = result.PathToElementMap;

            // Load current hidden state: deselect hidden files
            HashSet<string> hiddenPaths = new HashSet<string>(
                files.Where(f => f.Hidden).Select(f => f.Path));

            foreach (KeyValuePair<string, FileTreeElement> kvp in _pathToElementMap)
            {
                if (hiddenPaths.Contains(kvp.Key))
                {
                    kvp.Value.IsSelected = false;
                }
            }

            // Load exclusion rules from metadata
            LoadExclusionRules();

            if (_ruleMode == HideRuleMode.Include)
            {
                SetSelectionToModeDefault();
            }

            ApplyManualRuleSelection();

            // Apply pattern markings
            ApplyPatternMarkings();

            _initialized = true;
        }

        private void LoadExclusionRules()
        {
            Dictionary<int, HideRuleSet> hideRuleSets = Metadata.GetHideRuleSets();
            if (hideRuleSets.TryGetValue(_info.AssetId, out HideRuleSet ruleSet))
            {
                _ruleMode = ruleSet.Mode;
                SetStoredRules(ruleSet.Rules);
            }
            else
            {
                _ruleMode = HideRuleMode.Hide;
                _rules = new List<string>();
                _manualRules = new List<string>();
            }
            SyncRulesString();
        }

        private void SetStoredRules(IEnumerable<string> rules)
        {
            _rules = new List<string>();
            _manualRules = new List<string>();

            foreach (string rule in HideRuleSet.NormalizeRules(rules))
            {
                if (IsManualTreeRule(rule))
                {
                    _manualRules.Add(rule);
                }
                else
                {
                    _rules.Add(rule);
                }
            }
        }

        private static bool IsManualTreeRule(string rule)
        {
            if (string.IsNullOrWhiteSpace(rule)) return false;

            string trimmedRule = rule.Trim();
            if (trimmedRule.StartsWith("*", StringComparison.Ordinal)) return false;
            return trimmedRule.EndsWith("/", StringComparison.Ordinal) || trimmedRule.Contains("/");
        }

        private void SyncRulesString()
        {
            _exclusionRules = string.Join("\n", _rules);
        }

        private void ApplyPatternMarkings()
        {
            if (_pathToElementMap == null) return;

            // Reset auto-exclusion state
            foreach (KeyValuePair<string, FileTreeElement> kvp in _pathToElementMap)
            {
                kvp.Value.IsAutoExcluded = false;
                kvp.Value.IsAutoIncluded = false;
            }

            foreach (string pattern in _rules)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;

                foreach (KeyValuePair<string, FileTreeElement> kvp in _pathToElementMap)
                {
                    bool matchedByRule = MatchesPattern(kvp.Key, pattern);
                    bool ancestorOfIncludeFolderRule = !matchedByRule
                        && _ruleMode == HideRuleMode.Include
                        && kvp.Value.IsFolder
                        && IsAncestorOfIncludeFolderRule(kvp.Key, pattern);

                    if (kvp.Value.IsFolder && _ruleMode != HideRuleMode.Include) continue;
                    if (matchedByRule || ancestorOfIncludeFolderRule)
                    {
                        if (_ruleMode == HideRuleMode.Include)
                        {
                            kvp.Value.IsAutoIncluded = matchedByRule;
                            kvp.Value.IsSelected = true;
                        }
                        else
                        {
                            kvp.Value.IsAutoExcluded = true;
                            kvp.Value.IsSelected = false;
                        }
                    }
                }
            }
        }

        private bool IsMatchedByAnyRule(string path)
        {
            foreach (string pattern in _rules)
            {
                if (!string.IsNullOrWhiteSpace(pattern) && MatchesPattern(path, pattern)) return true;
            }
            return false;
        }

        private void ApplyManualRuleSelection()
        {
            if (_pathToElementMap == null) return;

            bool selected = _ruleMode == HideRuleMode.Include;
            foreach (string rule in _manualRules)
            {
                if (string.IsNullOrWhiteSpace(rule)) continue;

                foreach (KeyValuePair<string, FileTreeElement> kvp in _pathToElementMap)
                {
                    bool matchedByRule = MatchesPattern(kvp.Key, rule);
                    bool ancestorOfIncludeFolderRule = !matchedByRule
                        && selected
                        && kvp.Value.IsFolder
                        && IsAncestorOfIncludeFolderRule(kvp.Key, rule);

                    if (matchedByRule || ancestorOfIncludeFolderRule)
                    {
                        kvp.Value.IsSelected = selected;
                    }
                }
            }
        }

        private List<string> GetManuallyHiddenPaths()
        {
            return GetPathsMatchingSelection(false, true);
        }

        private List<string> GetManuallyIncludedPaths()
        {
            return GetPathsMatchingSelection(true, true);
        }

        private List<string> GetRulesFromCurrentSelection(HideRuleMode mode)
        {
            return GetPathsMatchingSelection(mode == HideRuleMode.Include, false);
        }

        private List<string> GetManualRulesFromCurrentSelection()
        {
            return _ruleMode == HideRuleMode.Include ? GetManuallyIncludedPaths() : GetManuallyHiddenPaths();
        }

        private List<string> GetPathsMatchingSelection(bool selected, bool skipRuleControlled)
        {
            if (_pathToElementMap == null) return new List<string>();

            List<string> candidateFiles = new List<string>();
            HashSet<string> targetFiles = new HashSet<string>();
            foreach (KeyValuePair<string, FileTreeElement> kvp in _pathToElementMap)
            {
                if (kvp.Value.IsFolder) continue;
                if (skipRuleControlled && IsRuleControlled(kvp.Value)) continue;

                candidateFiles.Add(kvp.Key);
                if (kvp.Value.IsSelected == selected)
                {
                    targetFiles.Add(kvp.Key);
                }
            }

            if (targetFiles.Count == 0) return new List<string>();

            HashSet<string> collapsedFolders = GetCollapsedFolders(candidateFiles, targetFiles);

            List<string> result = new List<string>();
            foreach (string folder in collapsedFolders.OrderBy(f => f))
            {
                result.Add(folder + "/");
            }
            foreach (string file in targetFiles.OrderBy(f => f))
            {
                bool coveredByFolder = collapsedFolders.Any(f => IsPathInsideFolder(file, f));
                if (!coveredByFolder)
                {
                    result.Add(file);
                }
            }
            return HideRuleSet.NormalizeRules(result);
        }

        private static HashSet<string> GetCollapsedFolders(List<string> candidateFiles, HashSet<string> targetFiles)
        {
            Dictionary<string, int> folderToFileCount = new Dictionary<string, int>();
            Dictionary<string, int> folderToTargetFileCount = new Dictionary<string, int>();

            foreach (string file in candidateFiles)
            {
                bool isTargetFile = targetFiles.Contains(file);
                string folder = GetParentFolder(file);
                while (folder != null)
                {
                    folderToFileCount.TryGetValue(folder, out int fileCount);
                    folderToFileCount[folder] = fileCount + 1;

                    if (isTargetFile)
                    {
                        folderToTargetFileCount.TryGetValue(folder, out int targetFileCount);
                        folderToTargetFileCount[folder] = targetFileCount + 1;
                    }

                    folder = GetParentFolder(folder);
                }
            }

            HashSet<string> foldersContainingOnlyTargetFiles = new HashSet<string>();
            foreach (KeyValuePair<string, int> kvp in folderToFileCount)
            {
                if (folderToTargetFileCount.TryGetValue(kvp.Key, out int targetFileCount)
                    && targetFileCount == kvp.Value)
                {
                    foldersContainingOnlyTargetFiles.Add(kvp.Key);
                }
            }

            HashSet<string> collapsedFolders = new HashSet<string>();
            foreach (string folder in foldersContainingOnlyTargetFiles)
            {
                if (!HasAncestorFolder(folder, foldersContainingOnlyTargetFiles))
                {
                    collapsedFolders.Add(folder);
                }
            }
            return collapsedFolders;
        }

        private static bool HasAncestorFolder(string folder, HashSet<string> folders)
        {
            string parent = GetParentFolder(folder);
            while (parent != null)
            {
                if (folders.Contains(parent)) return true;
                parent = GetParentFolder(parent);
            }
            return false;
        }

        private bool IsRuleControlled(FileTreeElement element)
        {
            return _ruleMode == HideRuleMode.Include ? element.IsAutoIncluded : element.IsAutoExcluded;
        }

        private static string GetParentFolder(string path)
        {
            int lastSlash = path.LastIndexOf('/');
            return lastSlash > 0 ? path.Substring(0, lastSlash) : null;
        }

        private static bool IsPathInsideFolder(string path, string folder)
        {
            return path.Length > folder.Length
                && path.StartsWith(folder + "/", StringComparison.Ordinal);
        }

        private void SetSelectionToModeDefault()
        {
            if (_pathToElementMap == null) return;

            bool selected = _ruleMode == HideRuleMode.Hide;
            foreach (KeyValuePair<string, FileTreeElement> kvp in _pathToElementMap)
            {
                kvp.Value.IsSelected = selected;
            }
        }

        private static bool IsAncestorOfIncludeFolderRule(string folderPath, string pattern)
        {
            if (string.IsNullOrEmpty(folderPath) || string.IsNullOrWhiteSpace(pattern)) return false;

            string trimmedPattern = pattern.Trim();
            if (!trimmedPattern.EndsWith("/", StringComparison.Ordinal)) return false;

            string normalizedFolder = folderPath.TrimEnd('/');
            string ruleFolder = trimmedPattern.TrimEnd('/');
            return ruleFolder.StartsWith(normalizedFolder + "/", StringComparison.Ordinal);
        }

        private void SwitchRuleMode(HideRuleMode newMode)
        {
            if (_ruleMode == newMode) return;

            _rules = new List<string>();
            _manualRules = GetRulesFromCurrentSelection(newMode);
            _ruleMode = newMode;
            _newRule = "";
            SyncRulesString();
            ApplyPatternMarkings();
        }

        private void RefreshRulesFromTreeSelection()
        {
            _manualRules = GetManualRulesFromCurrentSelection();
            _newRule = "";
            SyncRulesString();
            ApplyPatternMarkings();
            Repaint();
        }

        private List<string> BuildRulesForApply()
        {
            List<string> result = HideRuleSet.NormalizeRules(_rules);
            _manualRules = _pathToElementMap == null ? HideRuleSet.NormalizeRules(_manualRules) : GetManualRulesFromCurrentSelection();

            foreach (string rule in _manualRules)
            {
                if (!result.Contains(rule)) result.Add(rule);
            }
            return HideRuleSet.NormalizeRules(result);
        }

        private void ReactivateAfterRuleChange()
        {
            if (_pathToElementMap == null) return;

            // Update files that were previously controlled by rules and no longer match.
            foreach (KeyValuePair<string, FileTreeElement> kvp in _pathToElementMap)
            {
                if (kvp.Value.IsFolder) continue;
                if (_ruleMode == HideRuleMode.Include)
                {
                    if (kvp.Value.IsAutoIncluded && !IsMatchedByAnyRule(kvp.Key))
                    {
                        kvp.Value.IsSelected = false;
                    }
                }
                else if (kvp.Value.IsAutoExcluded && !IsMatchedByAnyRule(kvp.Key))
                {
                    kvp.Value.IsSelected = true;
                }
            }
            ApplyPatternMarkings();
            _manualRules = GetManualRulesFromCurrentSelection();
        }

        private bool MatchesPattern(string path, string pattern)
        {
            return HideRuleSet.MatchesPattern(path, pattern);
        }

        private void CreateGUI()
        {
            _uitkActive = true;
            BuildContent();
        }

        private void BuildIfReady()
        {
            if (_uitkActive && rootVisualElement != null && rootVisualElement.childCount > 0)
            {
                BuildContent();
            }
        }

        private void BuildContent()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);
            root.AddToClassList(HideRootClass);

            if (_info == null)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("No package selected.", MessageType.Info));
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            Label subtitle = AssetInventoryUITK.CreateCopyLabel($"Package: {_displayName}");
            subtitle.AddToClassList(HideSubtitleClass);
            root.Add(subtitle);

            if (!_initialized)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("Loading...", MessageType.Info));
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            if (_treeModel == null || _treeModel.NumberOfDataElements <= 1)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("No indexed files found for this package.", MessageType.Info));
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            root.Add(CreateModeBar());
            VisualElement ruleHelp = AssetInventoryUITK.CreateHelpBox(GetRuleModeHelpText(), MessageType.Info);
            ruleHelp.AddToClassList(HideRuleHelpClass);
            root.Add(ruleHelp);

            root.Add(CreateNativeRulesEditor());

            root.Add(CreateFooter());
        }

        private VisualElement CreateModeBar()
        {
            VisualElement bar = new VisualElement();
            bar.AddToClassList(HideModeBarClass);

            for (int i = 0; i < RuleModeOptions.Length; i++)
            {
                HideRuleMode mode = (HideRuleMode)i;
                Button button = AssetInventoryUITK.CreateSecondaryButton(RuleModeOptions[i], () =>
                {
                    SwitchRuleMode(mode);
                    BuildContent();
                });
                button.AddToClassList(HideModeButtonClass);
                button.EnableInClassList(HideModeButtonActiveClass, _ruleMode == mode);
                bar.Add(button);
            }

            return bar;
        }

        private VisualElement CreateFooter()
        {
            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            footer.Add(AssetInventoryUITK.CreatePrimaryButton("Apply", ApplyChanges));
            footer.Add(AssetInventoryUITK.CreateSecondaryButton("Cancel", Close));
            return footer;
        }

        private string GetRuleModeHelpText()
        {
            return _ruleMode == HideRuleMode.Include
                ? "Checked files remain visible in search. Include rules on the right will automatically keep matching files visible and hide all other files."
                : "Checked files remain visible in search. Hide rules on the right will automatically hide matching files.";
        }

        private VisualElement CreateNativeRulesEditor()
        {
            VisualElement editor = new VisualElement();
            editor.AddToClassList(HideEditorContainerClass);

            VisualElement filesSection = AssetInventoryUITK.CreateSection("Files");
            filesSection.AddToClassList(HideFilesSectionClass);
            filesSection.Add(CreateNativeTreeView());
            editor.Add(filesSection);

            string ruleTitle = _ruleMode == HideRuleMode.Include ? "Include Rules" : "Hide Rules";
            VisualElement rulesSection = AssetInventoryUITK.CreateSection(ruleTitle);
            rulesSection.AddToClassList(HideRulesSectionClass);

            Label hint = AssetInventoryUITK.CreateCopyLabel("Patterns: *.ext, folder, path/segment");
            hint.AddToClassList(HideRuleHintClass);
            rulesSection.Add(hint);

            ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.AddToClassList(HideRuleScrollClass);

            _nativeRulesList = new VisualElement();
            scrollView.Add(_nativeRulesList);

            _nativeManualRulesList = new VisualElement();
            scrollView.Add(_nativeManualRulesList);

            rulesSection.Add(scrollView);
            RebuildNativeRulesList();
            RebuildNativeManualRulesList();
            editor.Add(rulesSection);

            return editor;
        }

        private UIElementsTreeView CreateNativeTreeView()
        {
            _nativeTreeView = new UIElementsTreeView
            {
                fixedItemHeight = 22f,
                selectionType = SelectionType.None,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                showBorder = true,
                horizontalScrollingEnabled = true,
                makeItem = CreateNativeTreeRow,
                bindItem = BindNativeTreeRow
            };
            _nativeTreeView.AddToClassList(HideTreeClass);
            _nativeTreeView.SetRootItems(CreateNativeTreeItems(_treeModel?.Root));
            _nativeTreeView.Rebuild();
            _nativeTreeView.ExpandAll();
            return _nativeTreeView;
        }

        private List<TreeViewItemData<FileTreeElement>> CreateNativeTreeItems(FileTreeElement parent)
        {
            List<TreeViewItemData<FileTreeElement>> result = new List<TreeViewItemData<FileTreeElement>>();
            if (parent?.Children == null) return result;

            foreach (TreeElement child in parent.Children)
            {
                if (child is FileTreeElement element)
                {
                    result.Add(CreateNativeTreeItem(element));
                }
            }

            return result;
        }

        private TreeViewItemData<FileTreeElement> CreateNativeTreeItem(FileTreeElement element)
        {
            return new TreeViewItemData<FileTreeElement>(element.TreeId, element, CreateNativeTreeItems(element));
        }

        private VisualElement CreateNativeTreeRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(HideTreeRowClass);

            Toggle toggle = new Toggle();
            toggle.name = "selectionToggle";
            toggle.AddToClassList(HideTreeToggleClass);
            toggle.RegisterValueChangedCallback(evt =>
            {
                FileTreeElement element = toggle.userData as FileTreeElement;
                if (element == null || IsRuleControlled(element)) return;

                FileTreeSelection.SetSelected(element, evt.newValue);
                RefreshRulesFromTreeSelection();
            });
            row.Add(toggle);

            Image icon = new Image
            {
                name = "itemIcon",
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList(HideTreeIconClass);
            row.Add(icon);

            Label label = AssetInventoryUITK.CreateCopyLabel(string.Empty);
            label.name = "itemLabel";
            label.displayTooltipWhenElided = true;
            label.AddToClassList(HideTreeLabelClass);
            row.Add(label);

            return row;
        }

        private void BindNativeTreeRow(VisualElement item, int index)
        {
            FileTreeElement element = _nativeTreeView.GetItemDataForIndex<FileTreeElement>(index);
            Toggle toggle = item.Q<Toggle>("selectionToggle");
            Image icon = item.Q<Image>("itemIcon");
            Label label = item.Q<Label>("itemLabel");

            bool ruleControlled = IsRuleControlled(element);
            bool displaySelected = element.IsAutoIncluded || (!element.IsAutoExcluded && element.IsSelected);
            toggle.userData = element;
            toggle.SetEnabled(!ruleControlled);
            toggle.SetValueWithoutNotify(displaySelected);

            Texture iconTexture = element.IsFolder
                ? EditorGUIUtility.IconContent("Folder Icon").image
                : AssetDatabase.GetCachedIcon(element.Path);
            icon.image = iconTexture;

            label.text = element.TreeName;
            label.tooltip = GetNativeTreeTooltip(element);
        }

        private string GetNativeTreeTooltip(FileTreeElement element)
        {
            if (element == null) return string.Empty;

            if (_ruleMode == HideRuleMode.Include && element.IsAutoIncluded) return $"{element.Path}\nIncluded by rule";
            if (_ruleMode == HideRuleMode.Hide && element.IsAutoExcluded) return $"{element.Path}\nHidden by rule";
            return element.Path;
        }

        private void RebuildNativeRulesList()
        {
            if (_nativeRulesList == null) return;

            _nativeRulesList.Clear();
            for (int i = 0; i < _rules.Count; i++)
            {
                _nativeRulesList.Add(CreateNativeRuleRow(i));
            }

            _nativeRulesList.Add(CreateNativeAddRuleRow());
            RefreshNativeAddRuleButton();
        }

        private VisualElement CreateNativeRuleRow(int index)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(HideRuleRowClass);

            TextField field = new TextField();
            field.AddToClassList(HideRuleFieldClass);
            field.SetValueWithoutNotify(_rules[index]);
            field.RegisterValueChangedCallback(evt =>
            {
                _rules[index] = evt.newValue;
                SyncRulesString();
                ReactivateAfterRuleChange();
                RefreshNativeTreeAndManualRules();
            });
            row.Add(field);

            Button remove = AssetInventoryUITK.CreateIconButton("Remove rule", "TreeEditor.Trash", () =>
            {
                _rules.RemoveAt(index);
                SyncRulesString();
                ReactivateAfterRuleChange();
                RebuildNativeRulesList();
                RefreshNativeTreeAndManualRules();
            });
            row.Add(remove);

            return row;
        }

        private VisualElement CreateNativeAddRuleRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(HideRuleAddRowClass);

            TextField field = new TextField();
            field.AddToClassList(HideRuleFieldClass);
            field.SetValueWithoutNotify(_newRule);
            field.RegisterValueChangedCallback(evt =>
            {
                _newRule = evt.newValue;
                RefreshNativeAddRuleButton();
            });
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                if (!TryAddNativeRule()) return;

                evt.StopPropagation();
            });
            row.Add(field);

            _nativeAddRuleButton = AssetInventoryUITK.CreateSecondaryButton("Add", () =>
            {
                if (TryAddNativeRule())
                {
                    field.SetValueWithoutNotify(_newRule);
                }
            });
            row.Add(_nativeAddRuleButton);

            return row;
        }

        private bool TryAddNativeRule()
        {
            if (string.IsNullOrWhiteSpace(_newRule)) return false;

            _rules.Add(_newRule.Trim());
            _newRule = "";
            SyncRulesString();
            ReactivateAfterRuleChange();
            RebuildNativeRulesList();
            RefreshNativeTreeAndManualRules();
            return true;
        }

        private void RefreshNativeAddRuleButton()
        {
            if (_nativeAddRuleButton != null)
            {
                _nativeAddRuleButton.SetEnabled(!string.IsNullOrWhiteSpace(_newRule));
            }
        }

        private void RebuildNativeManualRulesList()
        {
            if (_nativeManualRulesList == null) return;

            _nativeManualRulesList.Clear();
            if (_manualRules.Count == 0) return;

            string manualTitle = _ruleMode == HideRuleMode.Include ? "Manually Included" : "Manually Hidden";
            Label title = AssetInventoryUITK.CreateCopyLabel($"{manualTitle} ({_manualRules.Count})");
            title.AddToClassList("ai-section-label");
            _nativeManualRulesList.Add(title);

            foreach (string path in _manualRules)
            {
                Label label = AssetInventoryUITK.CreateCopyLabel(path);
                label.AddToClassList(HideManualRuleClass);
                _nativeManualRulesList.Add(label);
            }
        }

        private void RefreshNativeTreeAndManualRules()
        {
            if (_nativeTreeView != null)
            {
                _nativeTreeView.RefreshItems();
            }

            RebuildNativeManualRulesList();
        }

        private void ApplyChanges()
        {
            List<string> rulesForApply = BuildRulesForApply();
            if (_ruleMode == HideRuleMode.Include && rulesForApply.Count == 0)
            {
                bool applyEmptyInclude = EditorUtility.DisplayDialog(
                    "Include Rules Hide All Files",
                    "Include mode has no rules. Applying will hide every indexed file in this package.",
                    "Apply",
                    "Cancel");
                if (!applyEmptyInclude) return;
            }

            SyncRulesString();

            SaveExclusionRules(rulesForApply);
            Assets.ApplyHidePatternsFromScratch(_info.AssetId);

            Close();
        }

        private void SaveExclusionRules()
        {
            SaveExclusionRules(BuildRulesForApply());
        }

        private void SaveExclusionRules(List<string> rules)
        {
            string serializedRules = HideRuleSet.Serialize(_ruleMode, rules);

            // Find or create the Hide metadata assignment
            List<MetadataInfo> metadata = Metadata.GetPackageMetadata(_info.AssetId);
            MetadataInfo hideMetadata = metadata?.FirstOrDefault(m => m.Name == MetadataDefinition.FIELD_HIDE);

            if (_ruleMode == HideRuleMode.Hide && string.IsNullOrEmpty(serializedRules))
            {
                if (hideMetadata != null)
                {
                    Metadata.RemoveAssignment(_info, hideMetadata);
                }
            }
            else
            {
                if (hideMetadata != null)
                {
                    hideMetadata.StringValue = serializedRules;
                    DBAdapter.DB.Update(hideMetadata.ToAssignment());
                }
                else
                {
                    // Find the Hide definition
                    List<MetadataDefinition> defs = Metadata.LoadDefinitions();
                    MetadataDefinition hideDef = defs.FirstOrDefault(d => d.Name == MetadataDefinition.FIELD_HIDE);
                    if (hideDef != null)
                    {
                        Metadata.AddAssignment(_info, hideDef.Id, MetadataAssignment.Target.Package);
                        // Reload and set value
                        metadata = Metadata.GetPackageMetadata(_info.AssetId);
                        hideMetadata = metadata?.FirstOrDefault(m => m.Name == MetadataDefinition.FIELD_HIDE);
                        if (hideMetadata != null)
                        {
                            hideMetadata.StringValue = serializedRules;
                            DBAdapter.DB.Update(hideMetadata.ToAssignment());
                        }
                    }
                }
            }
        }
    }
}
