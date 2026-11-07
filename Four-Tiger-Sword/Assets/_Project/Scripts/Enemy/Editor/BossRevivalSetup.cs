using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>One-time lossless migration of owned legacy phase assets; does not touch open scenes.</summary>
public static class BossRevivalSetup
{
    [MenuItem("Tools/SainGum/Migrate Boss Revival Phases")]
    public static void MigrateMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run boss migration in Edit Mode.");
        var converted = new Dictionary<BossPhaseData, float>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project", "Assets/_Game" }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var boss in prefab.GetComponentsInChildren<Boss>(true))
            {
                var phases = boss.PhaseConfiguration;
                if (phases.Count == 0) continue;
                var thresholds = new List<float>();
                foreach (var phase in phases)
                {
                    if (phase == null) throw new InvalidOperationException(prefab.name + " has a missing phase.");
                    string text = File.ReadAllText(AssetDatabase.GetAssetPath(phase));
                    var match = Regex.Match(text, @"(?m)^  triggerHpPercent: ([^\r\n]+)");
                    thresholds.Add(match.Success ? float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : float.NaN);
                }
                if (thresholds.All(float.IsNaN)) continue;
                if (thresholds.Any(float.IsNaN) || boss.Data == null || thresholds[0] != 100f)
                    throw new InvalidOperationException(prefab.name + " has incomplete legacy phase data.");
                for (int i = 0; i < phases.Count; i++)
                {
                    float next = i + 1 < phases.Count ? thresholds[i + 1] : 0f;
                    float hp = boss.Data.maxHp * (thresholds[i] - next) / 100f;
                    if (float.IsNaN(hp) || float.IsInfinity(hp) || hp <= 0f)
                        throw new InvalidOperationException(prefab.name + " has invalid legacy phase thresholds.");
                    if (converted.TryGetValue(phases[i], out float previous) && !Mathf.Approximately(previous, hp))
                        throw new InvalidOperationException("Shared phase has conflicting health budgets: " + phases[i].name);
                    converted[phases[i]] = hp;
                }
            }
        }
        foreach (var pair in converted)
        {
            pair.Key.phaseMaxHp = pair.Value;
            EditorUtility.SetDirty(pair.Key);
            AssetDatabase.SaveAssetIfDirty(pair.Key);
        }
        ConfigureTestBoss();
        return $"PASS: migrated {converted.Count} legacy boss phases and configured Test_Boss revival animations.";
    }

    private static void ConfigureTestBoss()
    {
        const string folder = "Assets/_Project/Generated/Bosses/Test_Boss/";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(folder + "Test_Boss.controller");
        if (controller == null) throw new InvalidOperationException("Test_Boss controller is missing.");
        AddState(controller, "PhaseDown", "92217937de2e52e4689c735db01cc979");
        AddState(controller, "PhaseRevive", "11c3cd791a98eb6438e777640a912778");
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);
        float[] hp = { 1680f, 1680f, 1440f };
        string[] notes = {
            "체력 1680: 기본 1타와 3연타. 소진 시 3초 동안 빈사 후 부활.",
            "체력 1680: 돌진 추가, 공격력 1.1배. 소진 시 3초 동안 빈사 후 부활.",
            "체력 1440: 광범위 휩쓸기 추가, 공격력 1.2배. 소진 시 최종 처치."
        };
        for (int i = 0; i < hp.Length; i++)
        {
            var phase = AssetDatabase.LoadAssetAtPath<BossPhaseData>(folder + $"Test_Boss_Phase{i + 1}.asset");
            phase.phaseMaxHp = hp[i];
            phase.note = notes[i];
            EditorUtility.SetDirty(phase);
            AssetDatabase.SaveAssetIfDirty(phase);
        }
        var data = AssetDatabase.LoadAssetAtPath<BossData>(folder + "Test_Boss_Stats.asset");
        data.maxHp = Mathf.RoundToInt(hp.Sum());
        data.phaseDownSeconds = 1f;
        data.phaseHoldSeconds = .5f;
        data.phaseReviveSeconds = 1.5f;
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        // These imported clips contain weapon-socket events; this boss has no socket-switching rig.
        string prefabPath = folder + "Test_Boss.prefab";
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.GetComponent<Dummy_Event>() == null)
            {
                animator.gameObject.AddComponent<Dummy_Event>();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void AddState(AnimatorController controller, string name, string clipGuid)
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(clipGuid))
            .OfType<AnimationClip>().FirstOrDefault(value => !value.name.StartsWith("__preview__"));
        if (clip == null || !clip.isHumanMotion) throw new InvalidOperationException(name + " requires a humanoid clip.");
        var machine = controller.layers[0].stateMachine;
        var state = machine.states.Select(child => child.state).FirstOrDefault(value => value.name == name)
            ?? machine.AddState(name);
        state.motion = clip;
        state.writeDefaultValues = true;
        EditorUtility.SetDirty(state);
    }
}
