using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Reusable UI Toolkit window for displaying searchable line-based content.
    /// </summary>
    public sealed class LineListWindow : EditorWindow
    {
        private string[] _lines = Array.Empty<string>();
        private Action<int, string> _onLineClick;
        private CommonSearchableLineList _lineList;
        private bool _focusSearchField;

        public static LineListWindow Show(string title, string[] lines, Action<int, string> onLineClick = null, float width = 500f, float height = 400f)
        {
            LineListWindow window = CreateInstance<LineListWindow>();
            window.titleContent = new GUIContent(title);
            window._lines = lines ?? Array.Empty<string>();
            window._onLineClick = onLineClick;
            window._focusSearchField = true;

            Vector2 screenCenter = new Vector2(Screen.currentResolution.width / 2f, Screen.currentResolution.height / 2f);
            window.position = new Rect(screenCenter.x - width / 2f, screenCenter.y - height / 2f, width, height);
            window.minSize = new Vector2(300, 200);

            window.ShowUtility();
            return window;
        }

        public static LineListWindow Show(string title, IEnumerable<string> lines, Action<int, string> onLineClick = null, float width = 500f, float height = 400f)
        {
            return Show(title, lines != null ? new List<string>(lines).ToArray() : Array.Empty<string>(), onLineClick, width, height);
        }

        private void CreateGUI()
        {
            Build();
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

            _lineList = new CommonSearchableLineList(_lines, _onLineClick);
            _lineList.AddToClassList(CommonUITK.WindowBodyClass);
            CommonUITK.ApplyWindowLayout(_lineList);
            root.Add(_lineList);

            if (_focusSearchField)
            {
                _focusSearchField = false;
                root.schedule.Execute(() => _lineList?.FocusSearchField()).ExecuteLater(0);
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape) return;

            Close();
            evt.StopPropagation();
        }
    }
}
