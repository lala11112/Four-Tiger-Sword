using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class CommonTabbedPane : VisualElement
    {
        public sealed class TabbedPaneClasses
        {
            public string RootClass;
            public string HeaderClass;
            public string LeadingClass;
            public string TabStripClass;
            public string TabClass;
            public string SelectedTabClass;
            public string TrailingClass;
            public string BodyClass;
        }

        private readonly List<Button> _tabs = new List<Button>();
        private readonly List<Image> _tabIcons = new List<Image>();
        private readonly TabbedPaneClasses _classes;
        private Action<int> _onTabChanged;

        public VisualElement Header { get; }
        public VisualElement Leading { get; }
        public VisualElement TabStrip { get; }
        public VisualElement Trailing { get; }
        public VisualElement Body { get; }
        public int SelectedIndex { get; private set; } = -1;

        public CommonTabbedPane(TabbedPaneClasses classes = null)
        {
            _classes = classes ?? new TabbedPaneClasses();
            CommonUITK.AddClasses(this, _classes.RootClass);

            Header = CommonUITK.CreateContainer(_classes.HeaderClass);
            Add(Header);

            Leading = CommonUITK.CreateContainer(_classes.LeadingClass);
            Header.Add(Leading);

            TabStrip = CommonUITK.CreateContainer(_classes.TabStripClass);
            Header.Add(TabStrip);

            Trailing = CommonUITK.CreateContainer(_classes.TrailingClass);
            Header.Add(Trailing);

            Body = CommonUITK.CreateContainer(_classes.BodyClass);
            Add(Body);
        }

        public void SetTabs(IReadOnlyList<string> labels, int selectedIndex, Action<int> onTabChanged)
        {
            int count = labels?.Count ?? 0;
            List<GUIContent> content = new List<GUIContent>(count);
            for (int i = 0; i < count; i++)
            {
                content.Add(new GUIContent(labels[i] ?? string.Empty));
            }

            SetTabs(content, selectedIndex, onTabChanged);
        }

        public void SetTabs(IReadOnlyList<GUIContent> content, int selectedIndex, Action<int> onTabChanged)
        {
            _onTabChanged = onTabChanged;
            int count = content?.Count ?? 0;
            EnsureTabCount(count);
            for (int i = 0; i < count; i++)
            {
                GUIContent item = content[i] ?? GUIContent.none;
                _tabs[i].text = item.text ?? string.Empty;
                _tabs[i].tooltip = item.tooltip ?? string.Empty;
                _tabIcons[i].image = item.image;
                _tabIcons[i].style.display = item.image != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            SetSelectedIndexWithoutNotify(selectedIndex);
        }

        public void SetTabEnabled(int index, bool enabled)
        {
            if (index < 0 || index >= _tabs.Count) return;
            _tabs[index].SetEnabled(enabled);
        }

        public void SetSelectedIndexWithoutNotify(int selectedIndex)
        {
            SelectedIndex = selectedIndex >= 0 && selectedIndex < _tabs.Count ? selectedIndex : -1;
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].EnableInClassList(_classes.SelectedTabClass, i == SelectedIndex);
            }
        }

        private void EnsureTabCount(int count)
        {
            while (_tabs.Count < count)
            {
                int index = _tabs.Count;
                Button tab = CommonUITK.CreateButton(string.Empty, () => SelectTab(index), _classes.TabClass);
                Image icon = new Image
                {
                    name = "common-tabbed-pane-icon",
                    pickingMode = PickingMode.Ignore,
                    scaleMode = ScaleMode.ScaleToFit
                };
                icon.AddToClassList("common-tabbed-pane__icon");
                icon.style.display = DisplayStyle.None;
                tab.Insert(0, icon);
                _tabs.Add(tab);
                _tabIcons.Add(icon);
                TabStrip.Add(tab);
            }

            while (_tabs.Count > count)
            {
                int index = _tabs.Count - 1;
                _tabs[index].RemoveFromHierarchy();
                _tabs.RemoveAt(index);
                _tabIcons.RemoveAt(index);
            }
        }

        private void SelectTab(int index)
        {
            if (index < 0 || index >= _tabs.Count || index == SelectedIndex) return;

            SetSelectedIndexWithoutNotify(index);
            _onTabChanged?.Invoke(index);
        }
    }
}
