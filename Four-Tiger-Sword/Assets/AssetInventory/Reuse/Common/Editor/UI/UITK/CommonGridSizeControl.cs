using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class CommonGridSizeControl : VisualElement
    {
        public sealed class GridSizeClasses
        {
            public string RootClass;
            public string ModePopupClass;
            public string SliderClass;
        }

        private static readonly string[] DefaultModeNames = {"Tiny", "Compact", "Standard", "Detailed"};
        private static readonly int[] DefaultPresetSizes = {56, 88, 150, 230};
        private static readonly int[] DefaultModeThresholds = {70, 110, 190};

        private readonly List<string> _modeNames = new List<string>(DefaultModeNames);
        private readonly int[] _presetSizes = new int[4];
        private readonly int[] _modeThresholds = new int[3];
        private readonly Action<int> _onSizeChanged;
        private readonly int _minimum;
        private readonly int _maximum;

        public PopupField<string> ModePopup { get; }
        public SliderInt SizeSlider { get; }
        public int Value => SizeSlider.value;
        public CommonGridViewDisplayMode DisplayMode => GetDisplayMode(Value);

        public CommonGridSizeControl(
            int value,
            int minimum,
            int maximum,
            Action<int> onSizeChanged,
            GridSizeClasses classes = null,
            IReadOnlyList<int> presetSizes = null,
            IReadOnlyList<int> modeThresholds = null)
        {
            _minimum = Math.Min(minimum, maximum);
            _maximum = Math.Max(minimum, maximum);
            _onSizeChanged = onSizeChanged;
            CopyValues(presetSizes, DefaultPresetSizes, _presetSizes);
            CopyValues(modeThresholds, DefaultModeThresholds, _modeThresholds);

            GridSizeClasses safeClasses = classes ?? new GridSizeClasses();
            CommonUITK.AddClasses(this, safeClasses.RootClass);

            int clampedValue = Mathf.Clamp(value, _minimum, _maximum);
            ModePopup = new PopupField<string>(_modeNames, (int)GetDisplayMode(clampedValue))
            {
                tooltip = "Choose a grid presentation preset."
            };
            CommonUITK.AddClasses(ModePopup, safeClasses.ModePopupClass);
            ModePopup.RegisterValueChangedCallback(evt => SelectMode(evt.newValue));
            Add(ModePopup);

            SizeSlider = new SliderInt(_minimum, _maximum)
            {
                value = clampedValue,
                showInputField = true,
                tooltip = "Adjust preview tile size."
            };
            CommonUITK.AddClasses(SizeSlider, safeClasses.SliderClass);
            SizeSlider.RegisterValueChangedCallback(evt => OnSliderChanged(evt.newValue));
            Add(SizeSlider);
        }

        public void SetValueWithoutNotify(int value)
        {
            int clampedValue = Mathf.Clamp(value, _minimum, _maximum);
            SizeSlider.SetValueWithoutNotify(clampedValue);
            ModePopup.SetValueWithoutNotify(_modeNames[(int)GetDisplayMode(clampedValue)]);
        }

        public CommonGridViewDisplayMode GetDisplayMode(int value)
        {
            if (value < _modeThresholds[0]) return CommonGridViewDisplayMode.Tiny;
            if (value < _modeThresholds[1]) return CommonGridViewDisplayMode.Compact;
            if (value < _modeThresholds[2]) return CommonGridViewDisplayMode.Standard;
            return CommonGridViewDisplayMode.Detailed;
        }

        public static CommonGridViewDisplayMode GetDefaultDisplayMode(int value)
        {
            if (value < DefaultModeThresholds[0]) return CommonGridViewDisplayMode.Tiny;
            if (value < DefaultModeThresholds[1]) return CommonGridViewDisplayMode.Compact;
            if (value < DefaultModeThresholds[2]) return CommonGridViewDisplayMode.Standard;
            return CommonGridViewDisplayMode.Detailed;
        }

        private void SelectMode(string mode)
        {
            int modeIndex = _modeNames.IndexOf(mode);
            if (modeIndex < 0 || modeIndex >= _presetSizes.Length) return;

            int size = Mathf.Clamp(_presetSizes[modeIndex], _minimum, _maximum);
            SizeSlider.SetValueWithoutNotify(size);
            ModePopup.SetValueWithoutNotify(_modeNames[(int)GetDisplayMode(size)]);
            _onSizeChanged?.Invoke(size);
        }

        private void OnSliderChanged(int value)
        {
            ModePopup.SetValueWithoutNotify(_modeNames[(int)GetDisplayMode(value)]);
            _onSizeChanged?.Invoke(value);
        }

        private static void CopyValues(IReadOnlyList<int> source, int[] fallback, int[] target)
        {
            IReadOnlyList<int> values = source != null && source.Count == target.Length ? source : fallback;
            for (int i = 0; i < target.Length; i++) target[i] = values[i];
        }
    }
}
