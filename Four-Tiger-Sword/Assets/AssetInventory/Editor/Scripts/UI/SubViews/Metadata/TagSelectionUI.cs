using System;
using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class TagSelectionUI : EditorWindow
    {
        private const float WindowWidth = 320f;
        private const float MinimumHeight = 220f;
        private const float BaseIndent = 8f;
        private const float IndentPerDepth = 14f;
        private const float MaxIndent = 96f;
        private static readonly string KeyboardSelectedRowClass = BaseVerticalCollectionView.itemSelectedVariantUssClassName;
        private const EventModifiers UserInputModifiers =
            EventModifiers.Alt | EventModifiers.Control | EventModifiers.Command | EventModifiers.Shift;

        private List<AssetInfo> _assetInfo;
        private List<Tag> _tags;
        private string _newTag;
        private bool _firstRunDone;
        private ToolbarSearchField _tagField;
        private ScrollView _rowsContainer;
        private readonly List<TagSelectionRow> _visibleRows = new List<TagSelectionRow>();
        private readonly TagSelectionKeyboardState _keyboardSelection = new TagSelectionKeyboardState();
        private readonly Dictionary<int, TagRowView> _rowViews = new Dictionary<int, TagRowView>();
        private VisualElement _emptyState;
        private Label _emptyStateLabel;
        private TagAssignment.Target _target;
        private Action _onChange;

        public static TagSelectionUI ShowDropdown(Rect anchor, TagAssignment.Target target, List<AssetInfo> infos, Action onChange = null)
        {
            TagSelectionUI window = CreateInstance<TagSelectionUI>();
            window.titleContent = new GUIContent("Add Tag");
            window.minSize = GetWindowSize();
            window.Init(target, onChange);
            window.SetAssets(infos);
            AssetInventoryUITK.ShowAsDropDown(window, anchor, GetWindowSize());
            return window;
        }

        public static TagSelectionUI ShowDropdown(EditorWindow owner, VisualElement anchor, TagAssignment.Target target, List<AssetInfo> infos, Action onChange = null)
        {
            TagSelectionUI window = CreateInstance<TagSelectionUI>();
            window.titleContent = new GUIContent("Add Tag");
            window.minSize = GetWindowSize();
            window.Init(target, onChange);
            window.SetAssets(infos);
            AssetInventoryUITK.ShowAsDropDown(window, owner, anchor, GetWindowSize());
            return window;
        }

        public static TagSelectionUI ShowWindow(TagAssignment.Target target = TagAssignment.Target.Package, Action onChange = null)
        {
            TagSelectionUI window = GetWindow<TagSelectionUI>("Add Tag");
            window.minSize = GetWindowSize();
            window.Init(target, onChange);
            return window;
        }

        public void Init(TagAssignment.Target target, Action onChange = null)
        {
            _target = target;
            _onChange = onChange;
            RefreshTags();
            RebuildIfReady();
        }

        public void SetAssets(List<AssetInfo> infos)
        {
            _assetInfo = infos;
            RebuildIfReady();
        }

        private static Vector2 GetWindowSize()
        {
            return new Vector2(WindowWidth, Mathf.Max(MinimumHeight, AI.Config.tagListHeight));
        }

        private void CreateGUI()
        {
            BuildContent();
        }

        private void RebuildIfReady()
        {
            if (rootVisualElement != null && rootVisualElement.childCount > 0)
            {
                BuildContent();
            }
        }

        private void BuildContent()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            _keyboardSelection.Clear();
            _visibleRows.Clear();
            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            if (_assetInfo == null)
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Selection");
                section.Add(AssetInventoryUITK.CreateCopyLabel("No assets selected."));
                root.Add(section);
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            root.Add(CreateToolbar());

            ScrollView scroll = new ScrollView();
            scroll.AddToClassList("ai-tag-selector-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _rowsContainer = scroll;
            BuildRows();
            root.Add(scroll);

            if (!_firstRunDone)
            {
                _firstRunDone = true;
                root.schedule.Execute(() => _tagField?.Focus());
            }
        }

        private void BuildRows()
        {
            if (_rowsContainer == null) return;

            _rowsContainer.Clear();
            _rowViews.Clear();
            _visibleRows.Clear();
            if (_tags != null)
            {
                for (int i = 0; i < _tags.Count; i++)
                {
                    TagRowView view = CreateTagRow(_tags[i]);
                    _rowViews[_tags[i].Id] = view;
                    _rowsContainer.Add(view.Root);
                }
            }

            _emptyState = AssetInventoryUITK.CreateSection("Tags");
            _emptyStateLabel = AssetInventoryUITK.CreateCopyLabel(string.Empty);
            _emptyState.Add(_emptyStateLabel);
            _rowsContainer.Add(_emptyState);
            PopulateRows();
        }

        private void PopulateRows()
        {
            if (_rowsContainer == null) return;

            int shownItems = 0;
            HashSet<int> visibleIds = new HashSet<int>();
            _visibleRows.Clear();
            if (_tags != null)
            {
                HashSet<int> assignedTagIds = GetAssignedTagIds();
                List<TagSelectionRow> rows = TagSelectionRows.Build(_tags, _newTag, assignedTagIds).ToList();
                _visibleRows.AddRange(rows);
                for (int i = 0; i < rows.Count; i++)
                {
                    TagSelectionRow row = rows[i];
                    if (!_rowViews.TryGetValue(row.Tag.Id, out TagRowView view))
                        continue;
                    UpdateTagRow(view, row, i);
                    _rowsContainer.Insert(i, view.Root);
                    visibleIds.Add(row.Tag.Id);
                    shownItems++;
                }
            }

            foreach (KeyValuePair<int, TagRowView> pair in _rowViews)
                pair.Value.Root.style.display = visibleIds.Contains(pair.Key) ? DisplayStyle.Flex : DisplayStyle.None;

            _keyboardSelection.RetainIfSelectable(_visibleRows);
            RefreshKeyboardSelection(false);

            if (_emptyState != null)
                _emptyState.style.display = shownItems == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (shownItems == 0 && _emptyStateLabel != null)
            {
                if (_tags == null || _tags.Count == 0)
                {
                    _emptyStateLabel.text = "No tags created yet. Use the text field above to create the first tag.";
                }
                else if (string.IsNullOrWhiteSpace(_newTag))
                {
                    _emptyStateLabel.text = "All existing tags were assigned already. Use the text field above to create additional tags.";
                }
                else
                {
                    _emptyStateLabel.text = "Press Return to create a new tag.";
                }
            }
        }

        private VisualElement CreateToolbar()
        {
            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("ai-tag-selector-toolbar");

            _tagField = AssetInventoryUITK.CreateWindowSearchField(
                _newTag,
                "Find an available tag, or type a new name and press Return to create it.",
                value =>
                {
                    _newTag = value;
                    _keyboardSelection.Clear();
                    PopulateRows();
                },
                "ai-tag-selector-field");
            _tagField.RegisterCallback<KeyDownEvent>(evt =>
            {
                bool hasUserModifier = (evt.modifiers & UserInputModifiers) != EventModifiers.None;
                if (!hasUserModifier && evt.keyCode == KeyCode.DownArrow)
                {
                    MoveKeyboardSelection(1);
                    CommonUITK.ConsumeEvent(evt);
                    return;
                }

                if (!hasUserModifier && evt.keyCode == KeyCode.UpArrow)
                {
                    MoveKeyboardSelection(-1);
                    CommonUITK.ConsumeEvent(evt);
                    return;
                }

                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;

                if (_keyboardSelection.TryGetActiveRow(_visibleRows, out TagSelectionRow row))
                {
                    SelectTag(row);
                }
                else
                {
                    CreateNewTagFromInput();
                }
                CommonUITK.ConsumeEvent(evt);
            }, TrickleDown.TrickleDown);
            toolbar.Add(_tagField);

            toolbar.Add(AssetInventoryUITK.CreateIconButton("Manage Tags", "Settings", OpenTagsManager));
            return toolbar;
        }

        private void MoveKeyboardSelection(int direction)
        {
            if (!_keyboardSelection.Move(_visibleRows, direction)) return;

            RefreshKeyboardSelection(true);
        }

        private void RefreshKeyboardSelection(bool scrollToSelection)
        {
            int? activeTagId = _keyboardSelection.ActiveTagId;
            foreach (KeyValuePair<int, TagRowView> pair in _rowViews)
            {
                pair.Value.Root.EnableInClassList(
                    KeyboardSelectedRowClass,
                    activeTagId.HasValue && pair.Key == activeTagId.Value);
            }

            if (!scrollToSelection || !activeTagId.HasValue || _rowsContainer == null) return;

            int tagId = activeTagId.Value;
            _rowsContainer.schedule.Execute(() =>
            {
                if (_rowsContainer == null || _keyboardSelection.ActiveTagId != tagId) return;
                if (_rowViews.TryGetValue(tagId, out TagRowView view))
                {
                    _rowsContainer.ScrollTo(view.Root);
                }
            });
        }

        private HashSet<int> GetAssignedTagIds()
        {
            if (_assetInfo == null || _assetInfo.Count != 1) return new HashSet<int>();

            switch (_target)
            {
                case TagAssignment.Target.Package:
                    return _assetInfo[0].PackageTags?.Select(tag => tag.TagId).ToHashSet() ?? new HashSet<int>();

                case TagAssignment.Target.Asset:
                    return _assetInfo[0].AssetTags?.Select(tag => tag.TagId).ToHashSet() ?? new HashSet<int>();

                default:
                    return new HashSet<int>();
            }
        }

        private TagRowView CreateTagRow(Tag tag)
        {
            VisualElement container = new VisualElement();
            container.AddToClassList("ai-list-row");
            CommonUITK.ApplyListRowStyle(container);
            container.AddToClassList("ai-tag-selector-row");

            VisualElement indent = new VisualElement();
            indent.AddToClassList("ai-tag-selector-indent");
            VisualElement branch = new VisualElement();
            branch.AddToClassList("ai-tag-selector-branch");
            VisualElement vertical = new VisualElement();
            vertical.AddToClassList("ai-tag-selector-branch-vertical");
            branch.Add(vertical);
            VisualElement horizontal = new VisualElement();
            horizontal.AddToClassList("ai-tag-selector-branch-horizontal");
            branch.Add(horizontal);
            indent.Add(branch);
            container.Add(indent);

            Label chip = CreateTagChip(tag.Name, tag.GetColor(), tag.Name, true);
            container.Add(chip);

            VisualElement spacer = new VisualElement();
            spacer.AddToClassList("ai-tag-selector-spacer");
            container.Add(spacer);

            Label hotkey = AssetInventoryUITK.CreateStatusPill(string.Empty);
            hotkey.AddToClassList("ai-tag-hotkey-pill");
            container.Add(hotkey);

            TagRowView view = new TagRowView(container, indent, branch, chip, hotkey);
            container.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (evt.button != 0 || view.Current == null || !view.Current.IsSelectable) return;

                SelectTag(view.Current);
                evt.StopPropagation();
            });

            return view;
        }

        private static void UpdateTagRow(TagRowView view, TagSelectionRow row, int index)
        {
            view.Current = row;
            view.Root.EnableInClassList(BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName, index % 2 == 1);
            float indentWidth = Mathf.Min(MaxIndent, BaseIndent + row.Depth * IndentPerDepth);
            view.Indent.style.width = indentWidth;
            view.Branch.style.marginLeft = Mathf.Max(0f, indentWidth - 11f);
            view.Branch.style.display = row.Depth > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            view.Chip.text = row.IsSelectable ? "+ " + row.Tag.Name : row.Tag.Name;
            view.Chip.tooltip = row.FullPath;
            Color tagColor = row.IsContextOnly ? GetContextColor(row.Tag.GetColor()) : row.Tag.GetColor();
            view.Chip.style.backgroundColor = tagColor;
            view.Chip.style.color = CommonUITK.GetReadableTextColor(tagColor);
            view.Chip.EnableInClassList("ai-tag-chip-selectable", row.IsSelectable);
            view.Chip.EnableInClassList("ai-tag-chip-context", row.IsContextOnly);

            bool hasHotkey = !string.IsNullOrWhiteSpace(row.Tag.Hotkey);
            view.Hotkey.text = hasHotkey ? $"Alt+{row.Tag.Hotkey}" : string.Empty;
            view.Hotkey.tooltip = row.FullPath;
            view.Hotkey.style.display = hasHotkey ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static Label CreateTagChip(string text, Color color, string tooltip, bool selectable)
        {
            Label chip = new Label(text);
            chip.tooltip = tooltip;
            chip.AddToClassList("ai-tag-chip");
            if (selectable) chip.AddToClassList("ai-tag-chip-selectable");
            chip.style.backgroundColor = color;
            chip.style.color = CommonUITK.GetReadableTextColor(color);
            return chip;
        }

        private void SelectTag(TagSelectionRow row)
        {
            if (row == null || !row.IsSelectable || _assetInfo == null) return;

            _keyboardSelection.Clear();
            Tagging.AddAssignments(_assetInfo, row.Tag, _target, true);
            _onChange?.Invoke();
            RefreshTags();
            BuildRows();
        }

        private void CreateNewTagFromInput()
        {
            if (_assetInfo == null || string.IsNullOrWhiteSpace(_newTag)) return;

            _keyboardSelection.Clear();
            Tagging.AddAssignments(_assetInfo, _newTag.Trim(), _target, true);
            _newTag = string.Empty;
            _onChange?.Invoke();
            RefreshTags();
            BuildRows();
        }

        private void RefreshTags()
        {
            _tags = Tagging.LoadTags();
        }

        private static void OpenTagsManager()
        {
            TagsUI tagsUI = TagsUI.ShowWindow();
            tagsUI.Init();
        }

        private static Color GetContextColor(Color color)
        {
            Color target = EditorGUIUtility.isProSkin
                ? new Color(0.34f, 0.34f, 0.34f, color.a)
                : new Color(0.82f, 0.82f, 0.82f, color.a);
            return Color.Lerp(color, target, 0.62f);
        }

        private sealed class TagRowView
        {
            internal readonly VisualElement Root;
            internal readonly VisualElement Indent;
            internal readonly VisualElement Branch;
            internal readonly Label Chip;
            internal readonly Label Hotkey;
            internal TagSelectionRow Current;

            internal TagRowView(VisualElement root, VisualElement indent, VisualElement branch, Label chip, Label hotkey)
            {
                Root = root;
                Indent = indent;
                Branch = branch;
                Chip = chip;
                Hotkey = hotkey;
            }
        }

    }
}
