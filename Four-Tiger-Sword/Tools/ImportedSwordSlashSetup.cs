using System;
using UnityEditor;
using UnityEngine;

/// <summary>Reapply the imported single-swing presets without modifying the vendor prefabs.</summary>
public static class ImportedSwordSlashSetup
{
    const string Folder = "Assets/_Project/Scripts/FormActions/";
    const string Effects = "Assets/Hovl Studio/Sword slash VFX/Prefabs/";

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit Mode required.");
        string[] assets = { "WaterFromAction", "WaterFormLab/WaterFormActionSO", "WaterFormLab/WaterFormActionSO_V2",
            "FireFormAction", "WoodFormAction", "IronFormAction", "EarthFormAction" };
        int count = 0;
        foreach (string name in assets)
        {
            var data = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(Folder + name + ".asset");
            if (data == null) throw new InvalidOperationException("Missing " + name);
            Undo.RecordObject(data, "Synchronize imported sword effects");
            bool water = data is WaterFormActionDataSO;
            string effect = water ? "Sword Slash 1" : name.StartsWith("Fire") ? "Sword Slash 10"
                : name.StartsWith("Wood") ? "Sword Slash 3" : name.StartsWith("Iron") ? "Sword Slash 13" : "Sword Slash 12";
            for (int i = 0; i < data.ComboSteps.Count; i++)
            {
                var step = data.ComboSteps[i];
                step.Feedback.Slash = Settings(step, effect, new Vector3(0, 0, i % 2 == 0 ? 25 : -15));
                count++;
                if (water) ConfigureWater(step, i);
                else if (step.AnimationName == "Fire1") ConfigureFireThirdEffects(step);
                else if (step.AnimationName == "InSlash") step.Feedback.Slash.SlashVFXRotation = new Vector3(0, 0, 205);
                else if (step.AnimationName == "OutSlash") step.Feedback.Slash.SlashVFXRotation = new Vector3(0, 0, 15);
            }
            if (water)
            {
                var w = (WaterFormActionDataSO)data;
                ConfigureDash(w.HeavyAttackStep); count++;
                ConfigureDash(data.CounterStep); count++;
                foreach (var step in data.SkillSteps) { ConfigureDash(step); count++; }
                if (data.AirAttackStep != null)
                {
                    data.AirAttackStep.Feedback.Slash = Settings(data.AirAttackStep, "Sword Slash 9", Vector3.zero);
                    count++;
                }
            }
            else if (name.StartsWith("Fire"))
            {
                data.CounterStep.Feedback.Slash = Settings(data.CounterStep, effect, new Vector3(0, 0, 15));
                count++;
                foreach (var step in data.SkillSteps) { step.Feedback.Slash = Settings(step, effect, new Vector3(0, 0, 25)); count++; }
                data.AirAttackStep.Feedback.Slash = Settings(data.AirAttackStep, effect, new Vector3(0, 0, 90)); count++;
            }
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
        }
        return "Configured " + count + " attack steps in seven form assets, including the active Water V2 data.";
    }

    static AttackSlashSettings Settings(WeaponActionData step, string name, Vector3 rotation)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Effects + name + ".prefab");
        if (prefab == null) throw new InvalidOperationException("Missing " + name);
        return new AttackSlashSettings {
            SlashVFX = prefab, SlashVFXStartTime = -1f,
            // PlayerController is centered on the capsule; the visible rig is offset down by one metre.
            SlashVFXOffset = new Vector3(0, .05f, .1f) - step.HitBoxOffset,
            SlashVFXRotation = rotation, SlashVFXScale = .65f, SlashVFXLifetime = .42f,
            PlaybackSpeed = 2.6f, TimeOffset = -.02f, FollowPlayer = true, AlternateDirection = true
        };
    }

    static void ConfigureWater(WeaponActionData step, int combo)
    {
        if (combo == 2)
        {
            ConfigureWaterThirdEffects(step);
            return;
        }
        // Sampled on the active player rig at 120 Hz. Visual timing is independent of damage tuning.
        float[][] starts = { new[] { .33f, .445f }, new[] { .235f, .365f }, new[] { .35f, .825f } };
        for (int i = 0; i < step.HitEvents.Count; i++)
        {
            var hit = step.HitEvents[i];
            string effect = "Sword Slash 1";
            var rotation = new Vector3(0, 0, combo == 0 ? (i == 0 ? 190 : 205) : (i == 0 ? 10 : -15));
            if (combo == 2) rotation = new Vector3(0, 0, i == 0 ? 90 : 55);
            hit.Feedback.OverrideSlashVFX = true;
            hit.Feedback.Slash = Settings(step, effect, rotation);
            hit.Feedback.Slash.SlashVFXStartTime = starts[Mathf.Min(combo, 2)][Mathf.Min(i, 1)];
            hit.Feedback.Slash.AlternateDirection = false;
        }
    }

    // Only the visual feedback changes; Water3's motion, damage and hit windows remain authored.
    public static void ConfigureWaterThirdEffects(WeaponActionData step)
    {
        step.Feedback.Slash = WaterThrust(step);
        for (int i = 0; i < step.HitEvents.Count; i++)
        {
            var hit = step.HitEvents[i];
            hit.Feedback.OverrideSlashVFX = true;
            hit.Feedback.Slash = WaterThrust(step);
            // Prick 3's tip flash occurs at native time .1, or .05 attack seconds at speed 2.
            hit.Feedback.Slash.SlashVFXStartTime = i == 0 ? .33f : .805f;
        }
    }

    static AttackSlashSettings WaterThrust(WeaponActionData step)
    {
        var settings = Settings(step, "Prick 3", Vector3.zero);
        settings.SlashVFXScale = .5f;
        settings.SlashVFXLifetime = .48f;
        settings.PlaybackSpeed = 2f;
        settings.TimeOffset = -.05f;
        settings.AlternateDirection = false;
        return settings;
    }

    public static void ConfigureFireThirdEffects(WeaponActionData step)
    {
        step.Feedback.Slash = Settings(step, "Sword Slash 10", new Vector3(0, 0, 90));
        step.Feedback.Slash.SlashVFXStartTime = .105f;
        step.Feedback.Slash.SlashVFXScale = .6f;
        step.Feedback.Slash.AlternateDirection = false;
        step.UseCustomTrailTiming = true;
        step.TrailStartTime = 2f / 30f;
        step.TrailEndTime = .3f;
    }

    static void ConfigureDash(WeaponActionData step)
    {
        if (step == null) return;
        step.Feedback.Slash = Settings(step, "Sword Slash 2", new Vector3(0, 0, 8));
        step.Feedback.Slash.SlashVFXStartTime = .245f;
        step.Feedback.Slash.SlashVFXScale = .7f;
        step.Feedback.Slash.AlternateDirection = false;
    }
}
