using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class DropDownWindow : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(180f, 300f);
        private const float ItemHeight = 24f;

        private readonly List<DropDownItem> _items = new List<DropDownItem>();
        private Action<int> _callback;
        private int _current;
        private ListView _listView;

        public static DropDownWindow ShowAsDropDown(Rect anchor, int min, int max, int current, string prefix, string suffix, Action<int> callback)
        {
            DropDownWindow window = CreateInstance<DropDownWindow>();
            window.Init(min, max, current, prefix, suffix, callback);
            CommonUITK.ApplyDropDownWindowStyle(window);
            window.ShowAsDropDown(anchor, WindowSize);
            return window;
        }

        public static DropDownWindow ShowAsDropDown(Rect anchor, IEnumerable<Tuple<int, string>> data, int current, Action<int> callback)
        {
            DropDownWindow window = CreateInstance<DropDownWindow>();
            window.Init(data, current, callback);
            CommonUITK.ApplyDropDownWindowStyle(window);
            window.ShowAsDropDown(anchor, WindowSize);
            return window;
        }

        public void Init(int min, int max, int current, string prefix, string suffix, Action<int> callback)
        {
            List<Tuple<int, string>> data = new List<Tuple<int, string>>();
            string safePrefix = prefix ?? string.Empty;
            string safeSuffix = suffix ?? string.Empty;
            for (int i = min; i <= max; i++)
            {
                data.Add(new Tuple<int, string>(i, safePrefix + i + safeSuffix));
            }

            Init(data, current, callback);
        }

        public void Init(IEnumerable<Tuple<int, string>> data, int current, Action<int> callback)
        {
            _items.Clear();
            if (data != null)
            {
                foreach (Tuple<int, string> item in data)
                {
                    if (item == null) continue;

                    _items.Add(new DropDownItem(item.Item1, item.Item2 ?? string.Empty));
                }
            }

            _current = current;
            _callback = callback;
            titleContent = new GUIContent("Select");
            BuildIfReady();
        }

        private void CreateGUI()
        {
            Build();
        }

        private void BuildIfReady()
        {
            if (rootVisualElement != null && rootVisualElement.panel != null)
            {
                Build();
            }
        }

        private void Build()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            root.style.flexGrow = 1f;
            CommonUITK.ApplyWindowLayout(root);
            root.UnregisterCallback<KeyDownEvent>(OnKeyDown);
            root.RegisterCallback<KeyDownEvent>(OnKeyDown);

            if (_items.Count == 0)
            {
                root.Add(new HelpBox("No items available", HelpBoxMessageType.Info));
                return;
            }

            _listView = new ListView(_items, ItemHeight, CreateRow, BindRow)
            {
                fixedItemHeight = ItemHeight,
                horizontalScrollingEnabled = false,
                selectionType = SelectionType.None,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                showBorder = false,
                showBoundCollectionSize = false,
                showFoldoutHeader = false,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight
            };
            _listView.style.flexGrow = 1f;
            root.Add(_listView);
            _listView.RefreshItems();
        }

        private Button CreateRow()
        {
            Button button = null;
            button = new Button(() => Select(button.userData as DropDownItem));
            button.style.height = ItemHeight;
            button.style.alignSelf = Align.Stretch;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.marginLeft = 0f;
            button.style.marginRight = 0f;
            button.style.marginTop = 1f;
            button.style.marginBottom = 1f;
            return button;
        }

        private void BindRow(VisualElement element, int index)
        {
            if (!(element is Button button)) return;

            DropDownItem item = index >= 0 && index < _items.Count ? _items[index] : null;
            button.text = item?.Label ?? string.Empty;
            button.tooltip = item?.Label ?? string.Empty;
            button.userData = item;
            button.SetEnabled(item != null && item.Value != _current);
        }

        private void Select(DropDownItem item)
        {
            if (item == null || item.Value == _current) return;

            _callback?.Invoke(item.Value);
            Close();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape) return;

            Close();
            evt.StopPropagation();
        }

        private sealed class DropDownItem
        {
            public DropDownItem(int value, string label)
            {
                Value = value;
                Label = label;
            }

            public int Value { get; }
            public string Label { get; }
        }
    }
}
