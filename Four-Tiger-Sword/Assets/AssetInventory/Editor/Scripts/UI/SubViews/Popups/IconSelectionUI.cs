using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed partial class IconSelectionUI : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(320f, 400f);
        private static List<IconEntry> _cachedIcons;

        private string _search;
        private Action<string> _onIconSelected;
        private List<IconEntry> _icons;
        private ToolbarSearchField _searchField;
        private VisualElement _grid;
        private readonly List<IconView> _iconViews = new List<IconView>();
        private VisualElement _emptyState;

        public static IconSelectionUI ShowDropdown(EditorWindow owner, VisualElement anchor, Action<string> onIconSelected = null)
        {
            IconSelectionUI window = CreateInstance<IconSelectionUI>();
            window.titleContent = new GUIContent("Select Icon");
            window.minSize = WindowSize;
            window.Init(onIconSelected);
            AssetInventoryUITK.ShowAsDropDown(window, owner, anchor, WindowSize);
            return window;
        }

        public static IconSelectionUI ShowWindow(Action<string> onIconSelected = null)
        {
            IconSelectionUI window = GetWindow<IconSelectionUI>("Select Icon");
            window.minSize = WindowSize;
            window.Init(onIconSelected);
            return window;
        }

        internal static Texture GetIconTexture(string iconName)
        {
            if (string.IsNullOrWhiteSpace(iconName)) return null;

            List<IconEntry> icons = LoadIcons();
            IconEntry match = icons.FirstOrDefault(icon => icon.Name == iconName);
            return match?.Texture;
        }

        public void Init(Action<string> onIconSelected = null)
        {
            _onIconSelected = onIconSelected;
            _icons = LoadIcons();
            RebuildIfReady();
        }

        private static List<IconEntry> LoadIcons()
        {
            if (_cachedIcons != null) return _cachedIcons;

            _cachedIcons = new List<IconEntry>();
            Type asc = typeof (EditorGUIUtility);
            MethodInfo importPackageMethod = asc.GetMethod("GetEditorAssetBundle", BindingFlags.NonPublic | BindingFlags.Static);
            AssetBundle editorAssetBundle = (AssetBundle)importPackageMethod?.Invoke(null, null);
            if (editorAssetBundle == null) return _cachedIcons;

            _cachedIcons = editorAssetBundle
                .GetAllAssetNames()
                .Where(path => path.StartsWith("icons/", StringComparison.Ordinal))
                .Select(path => new {path, icon = editorAssetBundle.LoadAsset<Texture2D>(path)})
                .Where(x => x.icon != null)
                .Select(x => new
                {
                    x.icon.name,
                    x.icon,
                    lower = x.icon.name.ToLowerInvariant()
                })
                .Where(x =>
                    !x.lower.StartsWith("d_") &&
                    !x.lower.EndsWith(".small") &&
                    !x.lower.EndsWith("_sml") &&
                    !x.name.Contains("@"))
                .OrderBy(x => x.name)
                .GroupBy(x => x.name)
                .Select(group => group.First())
                .Select(x => new IconEntry(x.name, x.icon))
                .ToList();
            return _cachedIcons;
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

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            _searchField = AssetInventoryUITK.CreateWindowSearchField(
                _search,
                "Filter editor icons by name.",
                value =>
                {
                    _search = value;
                    RefreshGrid();
                },
                "ai-icon-picker-search");
            _searchField.name = "icon-search-field";
            root.Add(_searchField);

            ScrollView scroll = new ScrollView();
            scroll.AddToClassList("ai-icon-picker-scroll");

            _grid = new VisualElement();
            _grid.AddToClassList("ai-icon-picker-grid");
            scroll.Add(_grid);
            root.Add(scroll);

            BuildGrid();
            root.schedule.Execute(() => _searchField?.Focus()).ExecuteLater(100);
        }

        private void BuildGrid()
        {
            if (_grid == null) return;

            _grid.Clear();
            _iconViews.Clear();
            if (_icons == null || _icons.Count == 0)
            {
                _emptyState = CreateEmptyState("No editor icons were found.");
                _grid.Add(_emptyState);
                return;
            }

            for (int i = 0; i < _icons.Count; i++)
            {
                IconEntry entry = _icons[i];
                Button button = new Button(() => SelectIcon(entry.Name));
                button.tooltip = entry.Name;
                button.AddToClassList("ai-icon-picker-cell");
                if (entry.Texture != null)
                {
                    Image image = new Image
                    {
                        image = entry.Texture,
                        scaleMode = ScaleMode.ScaleToFit
                    };
                    image.AddToClassList("ai-icon-picker-image");
                    button.Add(image);
                }
                _iconViews.Add(new IconView(entry, button));
                _grid.Add(button);
            }

            _emptyState = CreateEmptyState("No icons match the current search.");
            _grid.Add(_emptyState);
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            int visibleCount = 0;
            for (int i = 0; i < _iconViews.Count; i++)
            {
                IconView view = _iconViews[i];
                bool visible = string.IsNullOrWhiteSpace(_search) ||
                               view.Entry.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
                view.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                    visibleCount++;
            }

            if (_emptyState != null)
                _emptyState.style.display = visibleCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static VisualElement CreateEmptyState(string text)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Icons");
            section.Add(AssetInventoryUITK.CreateCopyLabel(text));
            return section;
        }

        private void SelectIcon(string iconName)
        {
            _onIconSelected?.Invoke(iconName);
            Close();
        }

        private sealed class IconEntry
        {
            internal IconEntry(string name, Texture2D texture)
            {
                Name = name;
                Texture = texture;
            }

            internal string Name { get; }
            internal Texture2D Texture { get; }
        }

        private readonly struct IconView
        {
            internal readonly IconEntry Entry;
            internal readonly VisualElement Root;

            internal IconView(IconEntry entry, VisualElement root)
            {
                Entry = entry;
                Root = root;
            }
        }
    }
}
