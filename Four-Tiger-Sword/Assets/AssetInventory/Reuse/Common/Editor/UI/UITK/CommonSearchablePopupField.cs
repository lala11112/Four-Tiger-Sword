using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class CommonSearchablePopupField : VisualElement
    {
        public sealed class SearchablePopupClasses
        {
            public string RootClass;
            public string ButtonClass;
            public string LabelClass;
            public string ArrowClass;
        }

        private readonly EditorWindow _owner;
        private readonly bool _tintSelectedField;
        private readonly bool _showBracketedValues;
        private readonly bool _treatSlashLiterally;
        private readonly float _maximumHeight;
        private readonly Action<int> _onValueChanged;
        private SearchablePopup.PopupItem[] _items;
        private readonly VisualElement _input;
        private readonly Label _label;

        public int Value { get; private set; }

        public CommonSearchablePopupField(
            EditorWindow owner,
            string[] items,
            int value,
            Action<int> onValueChanged,
            bool showBracketedValues = false,
            bool treatSlashLiterally = false,
            float maximumHeight = 400f,
            SearchablePopupClasses classes = null)
            : this(
                owner,
                CreatePopupItems(items),
                value,
                onValueChanged,
                false,
                showBracketedValues,
                treatSlashLiterally,
                maximumHeight,
                classes)
        {
        }

        public CommonSearchablePopupField(
            EditorWindow owner,
            SearchablePopup.PopupItem[] items,
            int value,
            Action<int> onValueChanged,
            bool tintSelectedField = false,
            bool showBracketedValues = false,
            bool treatSlashLiterally = false,
            float maximumHeight = 400f,
            SearchablePopupClasses classes = null)
        {
            _owner = owner;
            _items = items ?? Array.Empty<SearchablePopup.PopupItem>();
            _onValueChanged = onValueChanged;
            _tintSelectedField = tintSelectedField;
            _showBracketedValues = showBracketedValues;
            _treatSlashLiterally = treatSlashLiterally;
            _maximumHeight = Mathf.Max(120f, maximumHeight);

            SearchablePopupClasses safeClasses = classes ?? new SearchablePopupClasses();
            CommonUITK.AddClasses(
                this,
                BaseField<string>.ussClassName,
                BaseField<string>.noLabelVariantUssClassName,
                BasePopupField<string, string>.ussClassName,
                PopupField<string>.ussClassName,
                safeClasses.RootClass);

            _input = new VisualElement
            {
                focusable = true,
                tabIndex = 0
            };
            CommonUITK.AddClasses(
                _input,
                BaseField<string>.inputUssClassName,
                BasePopupField<string, string>.inputUssClassName,
                PopupField<string>.inputUssClassName,
                safeClasses.ButtonClass);
            _input.AddManipulator(new Clickable(ShowPopup));
            _input.RegisterCallback<PointerDownEvent>(_ => _input.Focus());
            _input.RegisterCallback<KeyDownEvent>(OnInputKeyDown);

            _label = CommonUITK.CreateLabel(string.Empty, safeClasses.LabelClass);
            _label.AddToClassList(BasePopupField<string, string>.textUssClassName);
            _label.pickingMode = PickingMode.Ignore;
            _input.Add(_label);

            VisualElement arrow = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };
            CommonUITK.AddClasses(arrow, BasePopupField<string, string>.arrowUssClassName, safeClasses.ArrowClass);
            _input.Add(arrow);
            Add(_input);

            SetValueWithoutNotify(value);
        }

        public void SetItems(SearchablePopup.PopupItem[] items, int value)
        {
            _items = items ?? Array.Empty<SearchablePopup.PopupItem>();
            SetValueWithoutNotify(value);
        }

        public void SetItems(string[] items, int value)
        {
            SetItems(CreatePopupItems(items), value);
        }

        public void SetValueWithoutNotify(int value)
        {
            Value = _items.Length == 0 ? -1 : Mathf.Clamp(value, 0, _items.Length - 1);
            RefreshDisplay();
        }

        private void ShowPopup()
        {
            if (_owner == null || _items.Length == 0) return;

            Rect anchor = CommonUITK.ToScreenDropdownAnchor(_owner, this);
            SearchablePopupWindow.ShowAsDropDown(
                anchor,
                _items,
                Value,
                SelectValue,
                Mathf.Max(200f, anchor.width),
                _maximumHeight,
                _showBracketedValues,
                _treatSlashLiterally);
        }

        private void OnInputKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter && evt.keyCode != KeyCode.Space) return;

            ShowPopup();
            evt.StopPropagation();
        }

        private void SelectValue(int value)
        {
            if (value < 0 || value >= _items.Length || value == Value) return;

            SetValueWithoutNotify(value);
            _onValueChanged?.Invoke(value);
        }

        private void RefreshDisplay()
        {
            SearchablePopup.PopupItem item = Value >= 0 && Value < _items.Length
                ? _items[Value]
                : default;
            string text = SearchablePopup.GetSelectedDisplayText(Value, _items, _treatSlashLiterally);
            _label.text = text;
            _label.tooltip = string.Equals(item.Text, text, StringComparison.Ordinal) ? string.Empty : item.Text ?? string.Empty;

            if (_tintSelectedField && item.TintBackground)
            {
                _input.style.backgroundColor = item.BackgroundColor;
                _input.style.color = CommonUIStyles.GetHSPColor(item.BackgroundColor);
            }
            else
            {
                _input.style.backgroundColor = StyleKeyword.Null;
                _input.style.color = StyleKeyword.Null;
            }
        }

        private static SearchablePopup.PopupItem[] CreatePopupItems(string[] items)
        {
            string[] source = items ?? Array.Empty<string>();
            SearchablePopup.PopupItem[] result = new SearchablePopup.PopupItem[source.Length];
            for (int i = 0; i < source.Length; i++) result[i] = new SearchablePopup.PopupItem(source[i]);
            return result;
        }
    }
}
