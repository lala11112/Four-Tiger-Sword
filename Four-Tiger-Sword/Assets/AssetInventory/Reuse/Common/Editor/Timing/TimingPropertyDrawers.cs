using ImpossibleRobert.Common.Timing;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common.Editor.Timing
{
    [CustomPropertyDrawer(typeof(FrameTimingSettings))]
    public sealed class FrameTimingSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            Foldout root = CommonInspectorElements.CreateFoldoutSection(
                property.displayName,
                "common-frame-timing-" + property.propertyPath,
                property.isExpanded);
            root.RegisterValueChangedCallback(evt => property.isExpanded = evt.newValue);
            root.Add(CreateField(property, "_clockMode", "Clock Source"));
            root.Add(CreateField(property, "_updatePhase", "Update Phase"));
            root.Add(CreateField(property, "_maxDeltaTime", "Max Delta Time"));
            root.Add(CreateField(property, "_tickInEditMode", "Tick In Edit Mode"));
            root.Add(CreateField(property, "_timeScale", "Time Scale"));
            return root;
        }

        static PropertyField CreateField(SerializedProperty root, string relativePath, string label)
        {
            SerializedProperty property = root.FindPropertyRelative(relativePath);
            return property != null ? new PropertyField(property.Copy(), label) : null;
        }

    }

    [CustomPropertyDrawer(typeof(UpdateScheduleSettings))]
    public sealed class UpdateScheduleSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            Foldout root = CommonInspectorElements.CreateFoldoutSection(
                property.displayName,
                "common-update-schedule-" + property.propertyPath,
                property.isExpanded);
            root.RegisterValueChangedCallback(evt => property.isExpanded = evt.newValue);
            SerializedProperty cadence = property.FindPropertyRelative("_cadence");
            SerializedProperty fixedRateHz = property.FindPropertyRelative("_fixedRateHz");
            PropertyField cadenceField = cadence != null ? new PropertyField(cadence.Copy(), "Cadence") : null;
            PropertyField rateField = fixedRateHz != null ? new PropertyField(fixedRateHz.Copy(), "Fixed Rate Hz") : null;
            if (cadenceField != null)
                root.Add(cadenceField);
            if (rateField != null)
            {
                root.Add(rateField);
                System.Action refresh = () => rateField.style.display = cadence != null &&
                    (UpdateCadence)cadence.enumValueIndex == UpdateCadence.FixedRate
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                root.TrackPropertyValue(cadence, _ => refresh());
                refresh();
            }
            return root;
        }

    }

    public static class CommonTimingInspectorElements
    {
        public static PropertyField CreateUpdateSchedule(SerializedProperty updateSchedule)
        {
            return updateSchedule != null
                ? new PropertyField(updateSchedule.Copy(), "Timing")
                : null;
        }
    }

    public static class CommonTimingInspectorGUI
    {
        [System.Obsolete("Use CommonTimingInspectorElements.CreateUpdateSchedule for retained UI Toolkit inspectors.")]
        public static void DrawUpdateSchedule(SerializedProperty updateSchedule)
        {
            if (updateSchedule == null)
                return;

            EditorGUILayout.PropertyField(updateSchedule, new GUIContent("Timing"), true);
        }
    }
}
