using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class CommonFormBuilder
    {
        public sealed class FormClasses
        {
            public string RowClass;
            public string LabelClass;
            public string ControlClass;
            public string InlineClass;
            public string FieldClass;
            public string ToggleClass;
            public string SuffixClass;
            public bool WrapControls;
            public bool ToggleFirst;
            public bool LabelTogglesControl;
        }

        private readonly FormClasses _classes;

        public CommonFormBuilder(FormClasses classes)
        {
            _classes = classes ?? new FormClasses();
        }

        public VisualElement CreateRow(string label, string tooltip, params VisualElement[] controls)
        {
            string safeTooltip = tooltip ?? string.Empty;
            VisualElement row = CommonUITK.CreateContainer(_classes.RowClass);
            row.tooltip = safeTooltip;

            Label labelElement = CommonUITK.CreateLabel(label, _classes.LabelClass);
            labelElement.tooltip = safeTooltip;
            row.Add(labelElement);

            if (_classes.WrapControls)
            {
                VisualElement container = CommonUITK.CreateContainer(_classes.ControlClass);
                AddControls(container, controls, safeTooltip);
                row.Add(container);
            }
            else if (controls != null && controls.Length == 1)
            {
                VisualElement control = controls[0];
                if (control != null)
                {
                    AlignBaseFieldToRow(control);
                    CommonUITK.AddClasses(control, _classes.ControlClass);
                    ApplyTooltip(control, safeTooltip);
                    row.Add(control);
                }
            }
            else if (controls != null && controls.Length > 1)
            {
                VisualElement container = CommonUITK.CreateContainer(_classes.InlineClass, _classes.ControlClass);
                AddControls(container, controls, safeTooltip);
                row.Add(container);
            }

            return row;
        }

        public Toggle CreateToggle(bool value, Action<bool> onChange, string tooltip = null, params string[] classNames)
        {
            Toggle toggle = new Toggle
            {
                tooltip = tooltip ?? string.Empty
            };
            toggle.SetValueWithoutNotify(value);
            AlignBaseFieldToRow(toggle);
            CommonUITK.AddClasses(toggle, _classes.ToggleClass);
            CommonUITK.AddClasses(toggle, classNames);
            if (onChange != null) toggle.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            return toggle;
        }

        public VisualElement CreateToggleRow(string label, bool value, Action<bool> onChange, string tooltip = null, params string[] classNames)
        {
            Toggle toggle = CreateToggle(value, onChange, tooltip, classNames);
            VisualElement row;
            Label labelElement;
            if (_classes.ToggleFirst)
            {
                row = CommonUITK.CreateContainer(_classes.RowClass);
                row.tooltip = tooltip ?? string.Empty;
                row.Add(toggle);

                labelElement = CommonUITK.CreateLabel(label, _classes.LabelClass);
                labelElement.tooltip = tooltip ?? string.Empty;
                row.Add(labelElement);
            }
            else
            {
                row = CreateRow(label, tooltip, toggle);
                labelElement = row.Q<Label>();
            }

            if (_classes.LabelTogglesControl)
            {
                labelElement?.RegisterCallback<ClickEvent>(_ => toggle.value = !toggle.value);
            }
            return row;
        }

        public TextField CreateTextField(
            string value,
            Action<string> onChange,
            string tooltip = null,
            bool isDelayed = false,
            bool isReadOnly = false,
            params string[] classNames)
        {
            TextField field = new TextField
            {
                value = value ?? string.Empty,
                isDelayed = isDelayed,
                isReadOnly = isReadOnly,
                tooltip = tooltip ?? string.Empty
            };
            ApplyFieldClasses(field, classNames);
            if (onChange != null) field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            return field;
        }

        public VisualElement CreateTextRow(
            string label,
            string value,
            Action<string> onChange,
            string tooltip = null,
            bool isDelayed = false,
            bool isReadOnly = false,
            params string[] classNames)
        {
            return CreateRow(label, tooltip, CreateTextField(value, onChange, tooltip, isDelayed, isReadOnly, classNames));
        }

        public IntegerField CreateIntegerField(int value, Action<int> onChange, string tooltip = null, bool isDelayed = true, params string[] classNames)
        {
            IntegerField field = new IntegerField
            {
                value = value,
                isDelayed = isDelayed,
                tooltip = tooltip ?? string.Empty
            };
            ApplyFieldClasses(field, classNames);
            if (onChange != null) field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            return field;
        }

        public VisualElement CreateIntegerRow(
            string label,
            int value,
            Action<int> onChange,
            string suffix = null,
            string tooltip = null,
            bool isDelayed = true,
            params string[] classNames)
        {
            IntegerField field = CreateIntegerField(value, onChange, tooltip, isDelayed, classNames);
            return CreateUnitRow(label, field, suffix, tooltip);
        }

        public FloatField CreateFloatField(float value, Action<float> onChange, string tooltip = null, bool isDelayed = true, params string[] classNames)
        {
            FloatField field = new FloatField
            {
                value = value,
                isDelayed = isDelayed,
                tooltip = tooltip ?? string.Empty
            };
            ApplyFieldClasses(field, classNames);
            if (onChange != null) field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            return field;
        }

        public VisualElement CreateFloatRow(
            string label,
            float value,
            Action<float> onChange,
            string suffix = null,
            string tooltip = null,
            bool isDelayed = true,
            params string[] classNames)
        {
            FloatField field = CreateFloatField(value, onChange, tooltip, isDelayed, classNames);
            return CreateUnitRow(label, field, suffix, tooltip);
        }

        public EnumField CreateEnumField<TEnum>(TEnum value, Action<TEnum> onChange, string tooltip = null, params string[] classNames)
            where TEnum : Enum
        {
            EnumField field = new EnumField(value)
            {
                tooltip = tooltip ?? string.Empty
            };
            ApplyFieldClasses(field, classNames);
            if (onChange != null)
            {
                field.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is TEnum newValue) onChange(newValue);
                });
            }
            return field;
        }

        public VisualElement CreateEnumRow<TEnum>(string label, TEnum value, Action<TEnum> onChange, string tooltip = null, params string[] classNames)
            where TEnum : Enum
        {
            return CreateRow(label, tooltip, CreateEnumField(value, onChange, tooltip, classNames));
        }

        public ColorField CreateColorField(Color value, Action<Color> onChange, string tooltip = null, params string[] classNames)
        {
            ColorField field = new ColorField
            {
                value = value,
                tooltip = tooltip ?? string.Empty
            };
            ApplyFieldClasses(field, classNames);
            if (onChange != null) field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            return field;
        }

        public VisualElement CreateColorRow(string label, Color value, Action<Color> onChange, string tooltip = null, params string[] classNames)
        {
            return CreateRow(label, tooltip, CreateColorField(value, onChange, tooltip, classNames));
        }

        public Slider CreateSlider(float value, float min, float max, Action<float> onChange, string tooltip = null, params string[] classNames)
        {
            Slider slider = new Slider(min, max)
            {
                value = value,
                showInputField = true,
                tooltip = tooltip ?? string.Empty
            };
            ApplyFieldClasses(slider, classNames);
            if (onChange != null) slider.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            return slider;
        }

        public VisualElement CreateSliderRow(string label, float value, float min, float max, Action<float> onChange, string tooltip = null, params string[] classNames)
        {
            return CreateRow(label, tooltip, CreateSlider(value, min, max, onChange, tooltip, classNames));
        }

        public SliderInt CreateSliderInt(int value, int min, int max, Action<int> onChange, string tooltip = null, params string[] classNames)
        {
            SliderInt slider = new SliderInt(min, max)
            {
                value = value,
                showInputField = true,
                tooltip = tooltip ?? string.Empty
            };
            ApplyFieldClasses(slider, classNames);
            if (onChange != null) slider.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            return slider;
        }

        public VisualElement CreateSliderIntRow(string label, int value, int min, int max, Action<int> onChange, string tooltip = null, params string[] classNames)
        {
            return CreateRow(label, tooltip, CreateSliderInt(value, min, max, onChange, tooltip, classNames));
        }

        public VisualElement CreateUnitRow(string label, VisualElement field, string suffix, string tooltip = null)
        {
            if (string.IsNullOrWhiteSpace(suffix)) return CreateRow(label, tooltip, field);

            VisualElement inline = CommonUITK.CreateContainer(_classes.InlineClass);
            if (field != null)
            {
                AlignBaseFieldToRow(field);
                inline.Add(field);
            }
            inline.Add(CommonUITK.CreateLabel(suffix, _classes.SuffixClass));
            return CreateRow(label, tooltip, inline);
        }

        private void ApplyFieldClasses(VisualElement field, string[] classNames)
        {
            AlignBaseFieldToRow(field);
            CommonUITK.AddClasses(field, _classes.FieldClass);
            CommonUITK.AddClasses(field, classNames);
        }

        private static void AddControls(VisualElement parent, VisualElement[] controls, string tooltip)
        {
            if (parent == null || controls == null) return;

            for (int i = 0; i < controls.Length; i++)
            {
                VisualElement control = controls[i];
                if (control == null) continue;
                AlignBaseFieldToRow(control);
                ApplyTooltip(control, tooltip);
                parent.Add(control);
            }
        }

        private static void AlignBaseFieldToRow(VisualElement control)
        {
            if (control == null || !control.ClassListContains("unity-base-field")) return;
            control.style.marginLeft = 0f;
        }

        private static void ApplyTooltip(VisualElement element, string tooltip)
        {
            if (element == null || string.IsNullOrEmpty(tooltip) || !string.IsNullOrEmpty(element.tooltip)) return;
            element.tooltip = tooltip;
        }
    }
}
