using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class NameWindow : EditorWindow
    {
        private const float Width = 260f;
        private const float HeightWithoutTitle = 88f;
        private const float HeightWithTitle = 112f;

        private string _text;
        private string _title;
        private Action<string> _callback;
        private bool _allowEmpty;
        private TextField _textField;
        private Button _okButton;

        public static NameWindow ShowAsDropDown(Rect anchor, string text, Action<string> callback, bool allowEmpty = false, string title = null)
        {
            NameWindow window = CreateInstance<NameWindow>();
            window.Init(text, callback, allowEmpty, title);
            CommonUITK.ApplyDropDownWindowStyle(window);
            window.ShowAsDropDown(anchor, new Vector2(Width, string.IsNullOrEmpty(title) ? HeightWithoutTitle : HeightWithTitle));
            return window;
        }

        public void Init(string text, Action<string> callback, bool allowEmpty = false, string title = null)
        {
            _text = text ?? string.Empty;
            _callback = callback;
            _allowEmpty = allowEmpty;
            _title = title;
            titleContent = new GUIContent(string.IsNullOrWhiteSpace(title) ? "Name" : title);
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
            root.style.paddingLeft = 8f;
            root.style.paddingRight = 8f;
            root.style.paddingTop = 8f;
            root.style.paddingBottom = 8f;

            if (!string.IsNullOrEmpty(_title))
            {
                Label title = new Label(_title);
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.marginBottom = 6f;
                root.Add(title);
            }

            _textField = new TextField
            {
                value = _text ?? string.Empty
            };
            _textField.style.flexGrow = 1f;
            _textField.RegisterValueChangedCallback(evt =>
            {
                _text = evt.newValue;
                RefreshActions();
            });
            _textField.RegisterCallback<KeyDownEvent>(OnKeyDown);
            root.Add(_textField);

            VisualElement footer = CommonUITK.CreateWindowFooter(8f, 8f);

            _okButton = new Button(Accept) {text = "OK"};
            _okButton.style.minWidth = 110f;
            _okButton.style.height = 24f;
            _okButton.style.minHeight = 24f;
            footer.Add(_okButton);

            Button cancel = new Button(Close) {text = "Cancel"};
            cancel.style.minWidth = 80f;
            cancel.style.height = 24f;
            cancel.style.minHeight = 24f;
            footer.Add(cancel);

            root.Add(footer);
            RefreshActions();

            root.schedule.Execute(() => _textField?.Focus()).ExecuteLater(0);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                if (CanAccept())
                {
                    Accept();
                    evt.StopPropagation();
                }
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                Close();
                evt.StopPropagation();
            }
        }

        private void RefreshActions()
        {
            if (_okButton != null)
            {
                _okButton.SetEnabled(CanAccept());
            }
        }

        private bool CanAccept()
        {
            return _allowEmpty || !string.IsNullOrWhiteSpace(_text);
        }

        private void Accept()
        {
            if (!CanAccept()) return;

            _callback?.Invoke(_text);
            Close();
        }
    }
}
