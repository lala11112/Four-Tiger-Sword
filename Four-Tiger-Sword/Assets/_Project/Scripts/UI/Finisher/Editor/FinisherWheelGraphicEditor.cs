using UnityEditor;
using UnityEngine;

/// <summary>GraphicEditor draws only built-in fields, so explicitly include the wheel's design controls.</summary>
[CustomEditor(typeof(FinisherWheelGraphic)), CanEditMultipleObjects]
public sealed class FinisherWheelGraphicEditor : UnityEditor.UI.GraphicEditor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        serializedObject.Update();
        Group("Element Palette", "_fireColor", "_earthColor", "_goldColor", "_waterColor", "_woodColor", "_fallbackColor");
        Group("Surfaces", "_backgroundColor", "_sectorBaseColor", "_centerColor", "_inactiveCenterRimColor",
            "_selectedFillBlend", "_unselectedFillBlend");
        Group("Rims", "_selectedRimAlpha", "_currentFormRimAlpha", "_unselectedRimAlpha",
            "_selectedRimWidth", "_unselectedRimWidth", "_centerRimWidth", "_innerHighlightWidth");
        Group("Visual Spacing", "_sectorGapDegrees", "_backgroundPadding", "_sectorInnerInset",
            "_sectorOuterInset", "_innerHighlightInset", "_centerInset");
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Selection stays fixed at 90 / 250 logical units with Fire at the top. These controls change appearance only.", MessageType.Info);
        if (serializedObject.ApplyModifiedProperties())
            foreach (Object item in targets)
                ((FinisherWheelGraphic)item).SetVerticesDirty();
    }

    private void Group(string title, params string[] properties)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        foreach (string property in properties)
            EditorGUILayout.PropertyField(serializedObject.FindProperty(property));
    }
}
