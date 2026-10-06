using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class StringListWindow : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(300f, 350f);

        private string _title;
        private string _separator;
        private Action<string> _callback;
        private List<string> _items;
        private CommonReorderableListView<string> _listView;

        public static StringListWindow ShowAsDropDown(Rect anchor, string value, string separator, Action<string> callback, string title = null)
        {
            StringListWindow window = CreateInstance<StringListWindow>();
            window.Init(value, separator, callback, title);
            CommonUITK.ApplyDropDownWindowStyle(window);
            window.ShowAsDropDown(anchor, WindowSize);
            return window;
        }

        public void Init(string value, string separator, Action<string> callback, string title = null)
        {
            _separator = string.IsNullOrEmpty(separator) ? "," : separator;
            _callback = callback;
            _title = string.IsNullOrWhiteSpace(title) ? "Items" : title;
            _items = string.IsNullOrEmpty(value)
                ? new List<string>()
                : value.Split(new[] {_separator}, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

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

            Label title = new Label(_title ?? "Items");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 6f;
            root.Add(title);

            _listView = new CommonReorderableListView<string>(
                _items ?? new List<string>(),
                MakeItem,
                BindItem,
                24f);
            _listView.SetAddHandler((list, _) => list.AddItem(string.Empty));
            _listView.style.flexGrow = 1f;
            _listView.ListView.style.flexGrow = 1f;
            _listView.AddToClassList(CommonUITK.WindowBodyClass);
            CommonUITK.ApplyWindowLayout(_listView);
            root.Add(_listView);

            VisualElement footer = CommonUITK.CreateWindowFooter(0f, 0f);
            Button ok = new Button(Accept) {text = "OK"};
            ok.style.minWidth = 110f;
            ok.style.height = 24f;
            ok.style.minHeight = 24f;
            Button cancel = new Button(Close) {text = "Cancel"};
            cancel.style.minWidth = 80f;
            cancel.style.height = 24f;
            cancel.style.minHeight = 24f;
            footer.Add(ok);
            footer.Add(cancel);
            root.Add(footer);

            _listView.Refresh();
        }

        private VisualElement MakeItem()
        {
            return new StringListRow(UpdateItem);
        }

        private void BindItem(VisualElement element, string item, int index)
        {
            if (element is StringListRow row)
            {
                row.Bind(index, item);
            }
        }

        private void UpdateItem(int index, string value)
        {
            if (index < 0 || index >= _items.Count) return;

            _items[index] = value;
        }

        private void Accept()
        {
            string result = string.Join(_separator, _items.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            _callback?.Invoke(result);
            Close();
        }

        private sealed class StringListRow : VisualElement
        {
            private readonly TextField _field;
            private readonly Action<int, string> _onChange;
            private int _index;

            public StringListRow(Action<int, string> onChange)
            {
                _onChange = onChange;
                style.flexDirection = FlexDirection.Row;
                style.alignItems = Align.Center;

                _field = new TextField();
                _field.style.flexGrow = 1f;
                _field.RegisterValueChangedCallback(evt => _onChange?.Invoke(_index, evt.newValue));
                Add(_field);
            }

            public void Bind(int index, string value)
            {
                _index = index;
                _field.SetValueWithoutNotify(value ?? string.Empty);
            }
        }
    }
}
