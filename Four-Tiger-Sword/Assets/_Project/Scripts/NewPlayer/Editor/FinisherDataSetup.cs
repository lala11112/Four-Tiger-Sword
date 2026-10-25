using System;
using UnityEditor;
using UnityEngine;

/// <summary>Adds the missing temporary Earth third stage; optional finisher slots retain their normal-stage fallback.</summary>
public static class FinisherDataSetup
{
    private const string FormFolder = "Assets/_Project/Scripts/FormActions";
    private const string FirePath = FormFolder + "/FireFormAction.asset";

    [MenuItem("Tools/Studio Baekho/Combat/Prepare Elemental Finisher Data")]
    private static void PrepareFromMenu() => Debug.Log(Apply());

    public static string Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Finisher data setup requires Edit Mode.");
        var fire = AssetDatabase.LoadAssetAtPath<FireFormActionDataSO>(FirePath);
        if (fire?.ComboSteps == null || fire.ComboSteps.Count < 2 || fire.ComboSteps[1] == null)
            throw new InvalidOperationException("Existing Fire stage two is required for the temporary Earth slash timing.");

        int earthThirdStages = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:WeaponActionDataSO", new[] { FormFolder }))
        {
            var data = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(AssetDatabase.GUIDToAssetPath(guid));
            bool addEarthStage = data is EarthFormActionDataSO && data.ComboSteps?.Count == 2;
            if (!addEarthStage) continue;
            Undo.RecordObject(data, "Prepare elemental finisher data");
            if (addEarthStage && EnsureEarthThirdStage((EarthFormActionDataSO)data, fire.ComboSteps[1]))
                earthThirdStages++;
            EditorUtility.SetDirty(data);
        }
        AssetDatabase.SaveAssets();
        return $"Added {earthThirdStages} temporary Earth third stages. Existing first/second stages and optional finisher slots were preserved; empty slots reuse normal stage three.";
    }

    public static WeaponActionData CloneStep(WeaponActionData source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        return JsonUtility.FromJson<WeaponActionData>(JsonUtility.ToJson(source));
    }

    /// <summary>Appends only to the original two-stage Earth combo; never replaces an authored third stage.</summary>
    public static bool EnsureEarthThirdStage(EarthFormActionDataSO earth, WeaponActionData existingSlash)
    {
        if (earth == null || earth.ComboSteps?.Count != 2) return false;
        if (earth.ComboSteps[1] == null || existingSlash == null)
            throw new InvalidOperationException("Earth stage two and the existing slash timing must be present.");
        WeaponActionData third = CloneStep(earth.ComboSteps[1]);
        third.AnimationName = "OutSlash";
        third.Duration = existingSlash.Duration;
        third.ComboTransitionTime = 0f;
        third.HitStartTime = existingSlash.HitStartTime;
        third.HitDuration = existingSlash.HitDuration;
        third.HitEvents.Clear();
        earth.ComboSteps.Add(third);
        return true;
    }
}
