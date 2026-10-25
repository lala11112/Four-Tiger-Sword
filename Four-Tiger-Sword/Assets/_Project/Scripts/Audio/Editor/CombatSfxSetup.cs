using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit, repeatable SFX assignment; never saves or changes open scenes.</summary>
public static class CombatSfxSetup
{
    private const string Root = "Assets/Action RPG SFX V2/";
    private static readonly HashSet<string> AudioFields = new HashSet<string>
    {
        "SwingSound", "HitSound", "SwingAudio", "HitAudio", "SecondaryHitAudio", "LandingAudio"
    };
    private static readonly HashSet<AudioClip> UsedClips = new HashSet<AudioClip>();

    [MenuItem("Tools/Four Tiger Sword/Audio/Assign Combat SFX")]
    public static void AssignFromMenu() => Debug.Log(Assign());

    public static string Assign()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Assign combat SFX in Edit Mode.");
        UsedClips.Clear();
        var sharp = Clip("Combat/Melee_Weapon_Swing_Sharp_Metal_Friction_Small_Short_01_01.wav");
        var simple = Clip("Combat/Melee_Weapon_Swing_Simple_Small_Short_01_01.wav");
        var heavy = Clip("Combat/Melee_Weapon_Swing_Heavy_Deep_Slow_01_01.wav");
        var hard = Clip("Combat/Melee_Weapon_Swing_Heavy_Thud_Metal_Friction_Hard_01_01.wav");
        var fireSwing = Clip("Combat/Melee_Weapon_Swing_Heavy_Deep_Slow_Metal_Friction_01_02.wav");
        var fireFinish = Clip("Combat/Melee_Weapon_Swing_Heavy_Deep_Slow_Metal_Friction_01_03.wav");
        var fire = Clip("Combat/Ranged_Magic_Fire_Projectile_Bright_01_01.wav");
        var water = Clip("Combat/Ranged_Magic_Swing_Shimmer_Ring_01_01.wav");
        var wood = Clip("Combat/Ranged_Magic_Swing_Bright_Vibe_Chirping_Shimmer_01_01.wav");
        var explosion = Clip("Designed Skill/Designed_Skill_Summon_Meteorite_Impact_Fire_Flame_Whoosh_Stone_Debris_01_01.wav");
        var crack = Clip("Impact/Impact_Combat_Hit_Crispy_Crack_Short_01_01.wav");
        var blood = Clip("Impact/Impact_Combat_Hit_Blood_Splash_Crack_Short_01_01.wav");
        var thud = Clip("Impact/Impact_Combat_Thud_Hit_Deep_01_01.wav");
        var metal = Clip("Impact/Impact_Combat_Hit_Cut_Metal_Scrap_Gore_Blood_Spill_01_01.wav");
        var debris = Clip("Foley/Foley_Wooden_Pile_Impact_Debris_01_01.wav");
        var bare = Clip("Combat/Melee_Barehand_Combat_Swing_01_01.wav");
        var bareHeavy = Clip("Combat/Melee_Barehand_Combat_Swing_Heavy_01_01.wav");
        int forms = 0, attacks = 0, enemies = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:WeaponActionDataSO", new[] { "Assets/_Project/Scripts/FormActions" }))
        {
            var data = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null) continue;
            string before = NonAudioSnapshot(data);
            Undo.RecordObject(data, "Assign combat SFX");
            bool isFire = data is FireFormActionDataSO;
            bool isWater = data is WaterFormActionDataSO;
            bool isWood = data is WoodFormActionDataSO;
            bool isEarth = data is EarthFormActionDataSO;
            AudioClip swing = isFire ? fireSwing : isWood ? simple : isEarth ? heavy : sharp;
            AudioClip hit = isFire || data is IronFormActionDataSO ? metal : isEarth ? thud : crack;
            AudioClip magic = isFire ? fire : isWater ? water : isWood ? wood : isEarth ? explosion : hard;
            data.CommonFeedback.HitSound = data.CommonFeedback.HitSound != null ? data.CommonFeedback.HitSound : hit;
            data.CommonFeedback.HitAudio = Sound(data.CommonFeedback.HitSound, .62f);
            data.CommonFeedback.SecondaryHitAudio = Sound(isFire ? explosion : hit, isFire ? .68f : .48f);
            if (data.ComboSteps != null)
                for (int i = 0; i < data.ComboSteps.Count; i++)
                {
                    var step = data.ComboSteps[i];
                    if (step == null) continue;
                    AudioClip selected = isFire && step.SwingSound != null ? step.SwingSound
                        : isFire && i == data.ComboSteps.Count - 1 ? fireFinish : swing;
                    ConfigureStep(step, selected, hit, crack, .48f, .62f, null, sharp);
                    attacks++;
                }
            if (data.AirAttackStep != null)
            {
                ConfigureStep(data.AirAttackStep, hard, isEarth ? thud : hit, crack, .6f, .7f,
                    isEarth || isFire ? explosion : magic, sharp);
                attacks++;
            }
            if (data.CounterStep != null)
            {
                ConfigureStep(data.CounterStep, hard, isWood ? debris : hit, crack, .62f, .72f, null, sharp);
                attacks++;
            }
            if (data.SkillSteps != null)
                foreach (var step in data.SkillSteps)
                {
                    if (step == null) continue;
                    ConfigureStep(step, isWater ? sharp : hard, isWood ? debris : hit, crack, .55f, .68f, magic, sharp);
                    attacks++;
                }
            if (data.UltimateSteps != null)
                foreach (var step in data.UltimateSteps)
                {
                    if (step == null) continue;
                    // Water's fixed ten strikes must stay short; stance/shield ultimates are one activation.
                    ConfigureStep(step, isWater ? sharp : magic, isWater ? crack : hit, crack,
                        isWater ? .5f : .72f, isWater ? .54f : .74f, null, sharp);
                    attacks++;
                }
            if (data is WaterFormActionDataSO waterData && waterData.HeavyAttackStep != null)
            {
                ConfigureStep(waterData.HeavyAttackStep, hard, crack, crack, .62f, .68f, null, sharp);
                attacks++;
            }
            SaveAudioOnly(data, before);
            forms++;
        }
        foreach (string guid in AssetDatabase.FindAssets("t:MonsterSkillData", new[] { "Assets/_Project" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<MonsterSkillData>(path);
            if (data == null) continue;
            string before = NonAudioSnapshot(data);
            Undo.RecordObject(data, "Assign enemy combat SFX");
            bool blade = path.Contains("BlackSlayer") || path.Contains("Chang");
            bool jump = data is MonsterJumpSlamSO;
            bool blast = data is MonsterSelfDestructSO;
            bool dash = data is MonsterDashAttackSO;
            data.SwingAudio = Sound(blast ? explosion : jump ? bareHeavy : dash ? hard : blade ? sharp : bare,
                blast ? .68f : jump || dash ? .52f : .42f);
            data.HitAudio = Sound(jump || blast ? thud : blood, .68f);
            data.LandingAudio = jump ? Sound(explosion, .62f) : new CombatSoundSettings();
            if (data is MonsterComboAttackSO combo && combo.hits != null)
                for (int i = 0; i < combo.hits.Count; i++)
                {
                    var evt = combo.hits[i];
                    if (evt == null) continue;
                    evt.SwingAudio = Sound(blade ? sharp : bare, .42f + (i == combo.hits.Count - 1 ? .06f : 0f));
                    evt.HitAudio = Sound(blood, .64f);
                }
            SaveAudioOnly(data, before);
            enemies++;
        }
        // Only the selected short combat clips are preloaded. No broad pack reimport or compression changes.
        foreach (var clip in UsedClips)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
            if (importer == null) continue;
            var settings = importer.defaultSampleSettings;
            if (settings.preloadAudioData && settings.loadType == AudioClipLoadType.DecompressOnLoad && !importer.loadInBackground) continue;
            settings.preloadAudioData = true;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
        }
        return $"Combat SFX assigned: {forms} form assets, {attacks} player actions, {enemies} enemy skills, {UsedClips.Count} preloaded clips. Non-audio data preserved; no scenes saved.";
    }

    private static void ConfigureStep(WeaponActionData step, AudioClip swing, AudioClip hit, AudioClip shortHit,
        float swingVolume, float hitVolume, AudioClip accent, AudioClip quickSwing)
    {
        step.SwingSound = swing;
        step.Feedback.SwingAudio = Sound(swing, swingVolume, accent);
        step.Feedback.HitAudio = Sound(hit, hitVolume);
        if (step.HitEvents == null) return;
        for (int i = 0; i < step.HitEvents.Count; i++)
        {
            var evt = step.HitEvents[i];
            if (evt == null) continue;
            bool last = i == step.HitEvents.Count - 1;
            // Short repeat strikes remain legible; only the first strike carries the long elemental layer.
            bool rapid = step.HitEvents.Count > 1 &&
                ((i > 0 && step.HitEvents[i - 1] != null && evt.StartTime - step.HitEvents[i - 1].StartTime < .4f)
                || (i + 1 < step.HitEvents.Count && step.HitEvents[i + 1] != null && step.HitEvents[i + 1].StartTime - evt.StartTime < .4f));
            evt.Feedback.SwingAudio = Sound(rapid ? quickSwing : swing, swingVolume, i == 0 ? accent : null);
            evt.Feedback.HitAudio = Sound(last ? hit : shortHit, last ? hitVolume : hitVolume * .85f);
        }
    }

    private static CombatSoundSettings Sound(AudioClip clip, float volume, AudioClip accent = null)
    {
        if (clip != null) UsedClips.Add(clip);
        if (accent != null) UsedClips.Add(accent);
        float accentVolume = accent != null && accent.name.Contains("Shimmer") ? .5f
            : accent != null && accent.name.Contains("Fire_Projectile") ? .38f : .28f;
        return new CombatSoundSettings { Clip = clip, Volume = volume, Accent = accent,
            AccentVolume = accentVolume, PitchRange = new Vector2(.97f, 1.03f) };
    }
    private static AudioClip Clip(string relative)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + relative);
        if (clip == null) throw new InvalidOperationException("Missing existing SFX: " + Root + relative);
        return clip;
    }
    private static string NonAudioSnapshot(UnityEngine.Object data)
    {
        var token = JToken.Parse(EditorJsonUtility.ToJson(data));
        StripAudio(token);
        return token.ToString(Newtonsoft.Json.Formatting.None);
    }
    private static void StripAudio(JToken token)
    {
        if (token is JObject obj)
        {
            var remove = new List<JProperty>();
            foreach (var property in obj.Properties())
                if (AudioFields.Contains(property.Name)) remove.Add(property); else StripAudio(property.Value);
            foreach (var property in remove) property.Remove();
        }
        else if (token is JArray array) foreach (var child in array) StripAudio(child);
    }
    private static void SaveAudioOnly(UnityEngine.Object data, string before)
    {
        if (before != NonAudioSnapshot(data))
            throw new InvalidOperationException("Combat setup changed non-audio data: " + data.name);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
    }
}
