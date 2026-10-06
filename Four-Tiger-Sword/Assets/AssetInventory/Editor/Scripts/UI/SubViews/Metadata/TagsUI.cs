using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class TagsUI : BasicEditorUI
    {
        private List<Tag> _tags;
        private string _searchTerm;

        private ToolbarSearchField _searchField;
        private Label _addHint;
        private Button _createButton;
        private Button _deleteAllButton;
        private TreeView _treeView;
        private TreeModel<TagTreeElement> _treeModel;
        private bool _runtimeHooksRegistered;
        private bool _suppressTagChangedRebuild;
        private bool _suppressHierarchyPersistence;
        private bool _hierarchyPersistenceScheduled;

        public static TagsUI ShowWindow()
        {
            TagsUI window = GetWindow<TagsUI>("Tag Management");
            window.minSize = new Vector2(430, 260);

            return window;
        }

        public void Init()
        {
            _tags = Tagging.LoadTags();
            InitTreeModel();
            Build();
        }

        private void InitTreeModel()
        {
            List<TagTreeElement> treeData = Tagging.BuildTagTree();
            _treeModel = new TreeModel<TagTreeElement>(treeData);
        }

        public void OnEnable()
        {
            RegisterRuntimeHooks();
        }

        public void OnDisable()
        {
            UnregisterRuntimeHooks();
        }

        private void RegisterRuntimeHooks()
        {
            if (_runtimeHooksRegistered) return;

            _runtimeHooksRegistered = true;
            Tagging.OnTagsChanged -= OnTagsChanged;
            Tagging.OnTagsChanged += OnTagsChanged;
        }

        private void UnregisterRuntimeHooks()
        {
            if (!_runtimeHooksRegistered) return;

            _runtimeHooksRegistered = false;
            Tagging.OnTagsChanged -= OnTagsChanged;
        }

        private void OnTagsChanged()
        {
            if (_suppressTagChangedRebuild) return;

            _tags = Tagging.LoadTags();
            InitTreeModel();
            Build();
        }

        private void OnHierarchyChanged()
        {
            InitTreeModel();
            RefreshTree();
        }

        private Tag GetTagWithHotkey(string hotkey)
        {
            if (string.IsNullOrEmpty(hotkey)) return null;
            return _tags?.Find(t => t.Hotkey == hotkey);
        }

        private void SetHotkey(Tag tag, string newHotkey)
        {
            if (!TagTreeViewControl.CanAssignHotkey(tag)) return;

            if (string.IsNullOrEmpty(newHotkey))
            {
                tag.Hotkey = null;
                Tagging.SaveTag(tag);
                return;
            }

            // Only allow single letter or number
            if (newHotkey.Length > 1)
            {
                newHotkey = newHotkey.Substring(0, 1);
            }
            if (!char.IsLetterOrDigit(newHotkey[0])) return;

            // If hotkey is already in use by another tag, remove it from that tag
            newHotkey = newHotkey.ToLowerInvariant();
            Tag existingTag = GetTagWithHotkey(newHotkey);
            if (existingTag != null && existingTag.Id != tag.Id)
            {
                existingTag.Hotkey = null;
                Tagging.SaveTag(existingTag);
            }

            tag.Hotkey = newHotkey;
            Tagging.SaveTag(tag);
        }

        private void OnRenameTag(Tag tag, VisualElement anchor)
        {
            if (!TagTreeViewControl.CanRenameTag(tag)) return;

            NameWindow.ShowAsDropDown(CommonUITK.ToScreenDropdownAnchor(this, anchor), tag.Name, newName => RenameTag(tag, newName));
        }

        private void OnSetHotkey(Tag tag, VisualElement anchor)
        {
            if (!TagTreeViewControl.CanAssignHotkey(tag)) return;

            NameWindow.ShowAsDropDown(CommonUITK.ToScreenDropdownAnchor(this, anchor), tag.Hotkey, newHotkey => SetHotkey(tag, newHotkey), true);
        }

        private void OnDeleteTag(Tag tag)
        {
            if (!TagTreeViewControl.CanDeleteTag(tag)) return;

            List<Tag> descendants = Tagging.GetDescendantTags(tag.Id);

            string message;
            if (descendants.Count > 0)
            {
                string childList = string.Join("\n", descendants.Select(t => $"• '{t.Name}'"));
                message = $"Are you sure you want to delete the tag '{tag.Name}'?\n\nThis will also delete the following child tags:\n{childList}\n\nThis action cannot be undone.";
            }
            else
            {
                message = $"Are you sure you want to delete the tag '{tag.Name}'? This action cannot be undone.";
            }

            if (EditorUtility.DisplayDialog("Delete Tag", message, "Delete", "Cancel"))
            {
                Tagging.DeleteTagWithDescendants(tag);
            }
        }

        private void CreateGUI()
        {
            if (_tags == null)
            {
                Init();
                return;
            }

            Build();
        }

        private void Build()
        {
            VisualElement root = rootVisualElement;
            if (root == null || _tags == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            root.Add(BuildSearchSection());

            if (_tags.Count == 0)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("No tags created yet. Use the text field above to create the first tag.", MessageType.Info));
                return;
            }

            root.Add(BuildTreeSection());
            root.Add(AssetInventoryUITK.CreateHelpBox("Drag and drop tags to create parent/child relationships.", MessageType.Info));
            root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
            root.Add(BuildFooter());
            RefreshActions();
        }

        private VisualElement BuildSearchSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Create or Find Tags");
            section.AddToClassList("ai-tags-search-section");

            VisualElement row = new VisualElement();
            row.AddToClassList("ai-tags-search-row");

            _searchField = AssetInventoryUITK.CreateWindowSearchField(
                _searchTerm,
                "Find an existing tag, or type a new name and press Return to create it.",
                value =>
                {
                    _searchTerm = value;
                    RefreshTree();
                    RefreshSearchState();
                },
                "ai-tags-search-field");
            _searchField.name = "tagSearchField";
            _searchField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;

                CreateSearchTag();
                evt.StopPropagation();
            });
            row.Add(_searchField);

            _createButton = AssetInventoryUITK.CreatePrimaryButton("Create", CreateSearchTag);
            _createButton.AddToClassList("ai-tags-create-button");
            row.Add(_createButton);
            section.Add(row);

            _addHint = AssetInventoryUITK.CreateCopyLabel(string.Empty);
            _addHint.AddToClassList("ai-tags-add-hint");
            section.Add(_addHint);
            RefreshSearchState();

            return section;
        }

        private VisualElement BuildTreeSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Tags");
            section.AddToClassList("ai-tags-tree-section");

            _treeView = new TreeView
            {
                fixedItemHeight = 28f,
                selectionType = SelectionType.Multiple,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                showBorder = true,
                horizontalScrollingEnabled = true,
                reorderable = string.IsNullOrWhiteSpace(_searchTerm),
                makeItem = CreateTagRow,
                bindItem = BindTagRow
            };
            _treeView.AddToClassList("ai-tags-tree");
            _treeView.itemIndexChanged += OnTagItemIndexChanged;
            _treeView.SetRootItems(CreateTreeItems(_treeModel?.Root));
            _treeView.Rebuild();
            _treeView.ExpandAll();
            section.Add(_treeView);

            return section;
        }

        private VisualElement BuildFooter()
        {
            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            footer.AddToClassList("ai-tags-footer");

            _deleteAllButton = AssetInventoryUITK.CreateDestructiveButton("Delete All", DeleteAllTags);
            _deleteAllButton.tooltip = "Delete all tags that are not protected system tags.";
            footer.Add(_deleteAllButton);

            return footer;
        }

        private List<TreeViewItemData<TagTreeElement>> CreateTreeItems(TagTreeElement parent)
        {
            List<TreeViewItemData<TagTreeElement>> result = new List<TreeViewItemData<TagTreeElement>>();
            if (parent?.Children == null) return result;

            foreach (TreeElement child in parent.Children)
            {
                if (child is TagTreeElement element && ShouldShowTagTreeElement(element))
                {
                    result.Add(CreateTreeItem(element));
                }
            }

            return result;
        }

        private TreeViewItemData<TagTreeElement> CreateTreeItem(TagTreeElement element)
        {
            return new TreeViewItemData<TagTreeElement>(element.TreeId, element, CreateTreeItems(element));
        }

        private bool ShouldShowTagTreeElement(TagTreeElement element)
        {
            if (string.IsNullOrWhiteSpace(_searchTerm)) return true;
            if (TagMatchesSearch(element)) return true;

            if (element.Children == null) return false;
            foreach (TreeElement child in element.Children)
            {
                if (child is TagTreeElement childElement && ShouldShowTagTreeElement(childElement))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TagMatchesSearch(TagTreeElement element)
        {
            if (element?.Tag == null || string.IsNullOrWhiteSpace(_searchTerm)) return false;

            return element.Tag.Name?.IndexOf(_searchTerm, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private VisualElement CreateTagRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("ai-tags-tree-row");

            ColorField colorField = new ColorField
            {
                name = "tagColor"
            };
            colorField.AddToClassList("ai-tags-color");
            colorField.showEyeDropper = false;
            colorField.showAlpha = false;
            colorField.RegisterValueChangedCallback(evt =>
            {
                Tag tag = colorField.userData as Tag;
                SetTagColor(tag, evt.newValue);
            });
            row.Add(colorField);

            Label nameLabel = AssetInventoryUITK.CreateCopyLabel(string.Empty);
            nameLabel.name = "tagName";
            nameLabel.displayTooltipWhenElided = true;
            nameLabel.AddToClassList("ai-tags-name");
            row.Add(nameLabel);

            Button rename = AssetInventoryUITK.CreateIconButton("Rename tag", "editicon.sml", null);
            rename.name = "renameButton";
            rename.AddToClassList("ai-tags-row-icon");
            rename.clicked += () => OnRenameTag(rename.userData as Tag, rename);
            row.Add(rename);

            Button hotkey = AssetInventoryUITK.CreateSecondaryButton(string.Empty, null);
            hotkey.name = "hotkeyButton";
            hotkey.AddToClassList("ai-tags-hotkey-button");
            hotkey.clicked += () => OnSetHotkey(hotkey.userData as Tag, hotkey);
            row.Add(hotkey);

            Button delete = AssetInventoryUITK.CreateIconButton("Remove tag completely", "TreeEditor.Trash", null);
            delete.name = "deleteButton";
            delete.AddToClassList("ai-tags-row-icon");
            delete.clicked += () => OnDeleteTag(delete.userData as Tag);
            row.Add(delete);

            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                TagTreeElement element = row.userData as TagTreeElement;
                PopulateContextMenu(evt.menu, element);
            }));

            return row;
        }

        internal void SetTagColor(Tag tag, Color color)
        {
            if (!TagTreeViewControl.CanAssignColor(tag)) return;

            tag.Color = "#" + ColorUtility.ToHtmlStringRGB(color);
            bool previousSuppressTagChangedRebuild = _suppressTagChangedRebuild;
            _suppressTagChangedRebuild = true;
            try
            {
                Tagging.SaveTag(tag);
            }
            finally
            {
                _suppressTagChangedRebuild = previousSuppressTagChangedRebuild;
            }
        }

        private void BindTagRow(VisualElement row, int index)
        {
            TagTreeElement element = _treeView.GetItemDataForIndex<TagTreeElement>(index);
            Tag tag = element?.Tag;
            if (tag == null) return;

            row.userData = element;

            ColorField colorField = row.Q<ColorField>("tagColor");
            Label nameLabel = row.Q<Label>("tagName");
            Button rename = row.Q<Button>("renameButton");
            Button hotkey = row.Q<Button>("hotkeyButton");
            Button delete = row.Q<Button>("deleteButton");

            bool canAssignColor = TagTreeViewControl.CanAssignColor(tag);
            bool canAssignHotkey = TagTreeViewControl.CanAssignHotkey(tag);
            bool canRename = TagTreeViewControl.CanRenameTag(tag);
            bool canDelete = TagTreeViewControl.CanDeleteTag(tag);

            colorField.userData = tag;
            colorField.SetValueWithoutNotify(tag.GetColor());
            colorField.SetEnabled(canAssignColor);
            colorField.tooltip = canAssignColor ? "Choose the color used for this tag." : "The color of this special tag cannot be changed.";

            string tooltip = canRename
                ? tag.FromAssetStore ? "From Asset Store" : "Local Tag"
                : "Special Tag";
            nameLabel.text = tag.Name;
            nameLabel.tooltip = tooltip;

            rename.userData = tag;
            rename.SetEnabled(canRename);
            rename.tooltip = canRename ? "Rename this tag." : "This special tag cannot be renamed.";

            hotkey.userData = tag;
            hotkey.text = string.IsNullOrEmpty(tag.Hotkey) ? "Shortcut" : $"Alt+{tag.Hotkey}";
            hotkey.SetEnabled(canAssignHotkey);
            hotkey.tooltip = canAssignHotkey
                ? string.IsNullOrEmpty(tag.Hotkey) ? "Assign an Alt+key shortcut to this tag." : $"Change or remove the Alt+{tag.Hotkey} shortcut."
                : "A shortcut cannot be assigned to this special tag.";

            delete.userData = tag;
            delete.SetEnabled(canDelete);
            delete.tooltip = canDelete ? "Delete this tag." : "This special tag cannot be deleted.";
        }

        private void RefreshTree()
        {
            if (_treeView == null || _treeModel == null) return;

            _suppressHierarchyPersistence = true;
            _treeView.reorderable = string.IsNullOrWhiteSpace(_searchTerm);
            _treeView.SetRootItems(CreateTreeItems(_treeModel.Root));
            _treeView.Rebuild();
            _treeView.ExpandAll();
            _suppressHierarchyPersistence = false;
        }

        private void RefreshSearchState()
        {
            bool canCreate = CanCreateSearchTag();
            if (_createButton != null)
            {
                _createButton.SetEnabled(canCreate);
                _createButton.tooltip = canCreate
                    ? "Create this tag. You can also press Return."
                    : string.IsNullOrWhiteSpace(_searchTerm)
                        ? "Enter a tag name to create it."
                        : "A tag with this name already exists.";
            }

            if (_addHint == null) return;
            bool hasSearch = !string.IsNullOrWhiteSpace(_searchTerm);
            bool tagExists = hasSearch && SearchTagExists();
            _addHint.text = tagExists ? "Tag already exists." : canCreate ? "Press Return to create a new tag." : string.Empty;
            _addHint.style.display = hasSearch ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshActions()
        {
            if (_deleteAllButton == null) return;

            int deletableTagCount = GetDeletableTags().Count;
            _deleteAllButton.SetEnabled(deletableTagCount > 0);
            _deleteAllButton.tooltip = deletableTagCount > 0
                ? $"Delete all {deletableTagCount:N0} non-system tags."
                : "There are no non-system tags to delete.";
        }

        private void CreateSearchTag()
        {
            if (!CanCreateSearchTag()) return;

            string tagName = _searchTerm;
            _searchTerm = string.Empty;
            _searchField?.SetValueWithoutNotify(string.Empty);
            RefreshSearchState();
            Tagging.AddTagWithSlashHandling(tagName);
        }

        private bool CanCreateSearchTag()
        {
            return !string.IsNullOrWhiteSpace(_searchTerm) && !SearchTagExists();
        }

        private bool SearchTagExists()
        {
            string normalized = _searchTerm?.Trim();
            if (string.IsNullOrWhiteSpace(normalized) || _tags == null) return false;

            return _tags.Any(tag => string.Equals(tag.Name, normalized, System.StringComparison.OrdinalIgnoreCase));
        }

        private void DeleteAllTags()
        {
            List<Tag> deletableTags = GetDeletableTags();
            if (deletableTags.Count == 0) return;

            if (EditorUtility.DisplayDialog("Delete All Tags", "Are you sure you want to delete all editable tags? This action cannot be undone.", "Delete", "Cancel"))
            {
                deletableTags.ForEach(Tagging.DeleteTag);
            }
        }

        private List<Tag> GetDeletableTags()
        {
            return _tags?.Where(TagTreeViewControl.CanDeleteTag).ToList() ?? new List<Tag>();
        }

        private void OnTagItemIndexChanged(int oldIndex, int newIndex)
        {
            if (_suppressHierarchyPersistence || _treeView == null) return;
            if (!string.IsNullOrWhiteSpace(_searchTerm))
            {
                RefreshTree();
                return;
            }
            if (_hierarchyPersistenceScheduled) return;

            _hierarchyPersistenceScheduled = true;
            _treeView.schedule.Execute(PersistTreeHierarchyFromView).ExecuteLater(0);
        }

        private void PersistTreeHierarchyFromView()
        {
            _hierarchyPersistenceScheduled = false;
            if (_treeView == null || _tags == null) return;

            List<(Tag tag, int? parentId)> changes = new List<(Tag tag, int? parentId)>();
            foreach (Tag tag in _tags)
            {
                if (!TagTreeViewControl.CanRenameTag(tag)) continue;

                int index = _treeView.viewController.GetIndexForId(tag.Id);
                if (index < 0) continue;

                int parentId = _treeView.viewController.GetParentId(tag.Id);
                int? newParentId = parentId > 0 ? parentId : (int?)null;
                if (tag.ParentId == newParentId) continue;

                changes.Add((tag, newParentId));
            }
            if (changes.Count == 0) return;

            _suppressTagChangedRebuild = true;
            try
            {
                foreach ((Tag tag, int? parentId) change in changes)
                {
                    change.tag.ParentId = change.parentId;
                    Tagging.SaveTag(change.tag);
                }
            }
            finally
            {
                _suppressTagChangedRebuild = false;
            }

            OnHierarchyChanged();
        }

        private void PopulateContextMenu(DropdownMenu menu, TagTreeElement clickedElement)
        {
            if (menu == null) return;

            Tag clickedTag = clickedElement?.Tag;
            bool hasSingleConversion = clickedTag != null && clickedTag.Name.Contains("/");
            if (hasSingleConversion)
            {
                menu.AppendAction("Split into hierarchy", _ => ConvertSingleTagToHierarchy(clickedTag));
            }

            List<Tag> slashTags = Tagging.GetTagsWithSlash();
            if (slashTags.Count > 0)
            {
                if (hasSingleConversion)
                {
                    menu.AppendSeparator();
                }
                menu.AppendAction($"Convert all {slashTags.Count} slash tags to hierarchy", _ => ConvertAllTagsToHierarchy());
            }
        }

        private void ConvertSingleTagToHierarchy(Tag tag)
        {
            // Check for conflicts first
            List<(string segment, Tag existingTag, int? currentParentId, int? newParentId)> conflicts = Tagging.CheckConversionConflicts(tag);

            if (conflicts.Count > 0)
            {
                // Build warning message
                string conflictList = string.Join("\n", conflicts.Select(c =>
                {
                    string currentParent = c.currentParentId.HasValue
                        ? _tags?.Find(t => t.Id == c.currentParentId.Value)?.Name ?? $"ID:{c.currentParentId}"
                        : "none";
                    string newParent = c.newParentId.HasValue
                        ? _tags?.Find(t => t.Id == c.newParentId.Value)?.Name ?? $"ID:{c.newParentId}"
                        : "root";
                    return $"• '{c.segment}' (current parent: {currentParent} → new parent: {newParent})";
                }));

                bool proceed = EditorUtility.DisplayDialog(
                    "Reparenting Warning",
                    $"Converting '{tag.Name}' to hierarchy would reparent the following existing tags:\n\n{conflictList}\n\nDo you want to proceed?",
                    "Proceed",
                    "Cancel");

                if (!proceed) return;

                // Force reparent
                if (Tagging.ConvertSlashTagToHierarchy(tag, true))
                {
                    EditorUtility.DisplayDialog("Conversion Complete", $"Tag '{tag.Name}' has been converted to a hierarchy.", "OK");
                }
            }
            else
            {
                if (Tagging.ConvertSlashTagToHierarchy(tag, false))
                {
                    EditorUtility.DisplayDialog("Conversion Complete", $"Tag '{tag.Name}' has been converted to a hierarchy.", "OK");
                }
            }
        }

        private void ConvertAllTagsToHierarchy()
        {
            (int convertedCount, List<(Tag tag, List<(string segment, Tag existingTag, int? currentParentId, int? newParentId)> conflicts)> skipped) result = Tagging.ConvertAllSlashTagsToHierarchy();

            if (result.skipped.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Conversion Complete",
                    $"All {result.convertedCount} slash tags have been converted to hierarchies.",
                    "OK");
            }
            else
            {
                string skippedList = string.Join("\n", result.skipped.Select(s => $"• '{s.tag.Name}'"));
                EditorUtility.DisplayDialog(
                    "Conversion Complete",
                    $"Converted {result.convertedCount} tags to hierarchies.\n\n" +
                    $"Skipped {result.skipped.Count} tags due to reparenting conflicts:\n{skippedList}\n\n" +
                    "Use right-click → 'Split into hierarchy' on individual tags to review and force conversion.",
                    "OK");
            }
        }

        private void RenameTag(Tag tag, string newName)
        {
            if (!TagTreeViewControl.CanRenameTag(tag)) return;
            if (string.IsNullOrEmpty(newName) || tag.Name == newName) return;

            Tag existingTag = DBAdapter.DB.Find<Tag>(t => t.Id != tag.Id && t.Name.ToLower() == newName.ToLower());
            if (existingTag != null)
            {
                EditorUtility.DisplayDialog("Error", "A tag with that name already exists (and merging tags is not yet supported).", "OK");
                return;
            }

            Tagging.RenameTag(tag, newName);
        }
    }
}
