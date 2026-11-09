using System;
using UnityEditor;
using UnityEngine;

/// <summary>Creates two independent presentation variants of the authored sword robot boss.</summary>
public static class BossHitReactionVariantsSetup
{
    public const string SourcePath = "Assets/_Project/Generated/Bosses/SwordRobotBoss/SwordRobotBoss.prefab";
    public const string Folder = "Assets/_Project/Generated/Bosses/HitReactionVariants";
    public const string FinalIKPath = Folder + "/SwordRobotBoss_FinalIK.prefab";
    public const string AdditivePath = Folder + "/SwordRobotBoss_Additive.prefab";

    [MenuItem("Tools/Studio Baekho/Enemy/Create Boss Hit Reaction Variants")]
    private static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Create boss hit reaction variants in Edit Mode.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath) == null)
            throw new InvalidOperationException("The authored SwordRobotBoss prefab is missing.");

        EnsureFolder(Folder);
        ConfigureCopy(FinalIKPath, root => BossFinalIKHitReactionSetup.Configure(root));
        ConfigureCopy(AdditivePath, root => BossAdditiveHitReactionSetup.Configure(root, Folder));
        BossMobilitySetup.RemoveObsoleteAssets(Folder);
        Selection.objects = new UnityEngine.Object[] {
            AssetDatabase.LoadAssetAtPath<GameObject>(FinalIKPath),
            AssetDatabase.LoadAssetAtPath<GameObject>(AdditivePath)
        };
        return "Created boss hit reaction variants:\n" + FinalIKPath + "\n" + AdditivePath;
    }

    private static void ConfigureCopy(string path, Action<GameObject> configure)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null && !AssetDatabase.CopyAsset(SourcePath, path))
            throw new InvalidOperationException("Could not copy boss prefab: " + path);

        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            root.name = System.IO.Path.GetFileNameWithoutExtension(path);
            var boss = root.GetComponent<Boss>();
            boss.PhaseHealthMultiplier = .7f;
            boss.UseOverlayForOrdinaryHits = true;
            boss.ResistDamageKnockback = true;
            boss.AdditionalPatternRecovery = .2f;
            var poise = new SerializedObject(root.GetComponent<PoiseHandler>());
            poise.FindProperty("_maxPoise").floatValue = 500f;
            poise.ApplyModifiedPropertiesWithoutUndo();
            configure(root);
            BossMobilitySetup.Configure(root, Folder);
            BossPhaseTwoVisualSetup.Configure(root, Folder);
            ConfigurePhaseEntryFlight(root, path);
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved) throw new InvalidOperationException("Could not save boss variant: " + path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static void ConfigurePhaseEntryFlight(GameObject root, string path)
    {
        if (path != AdditivePath) return;
        var skill = AssetDatabase.LoadAssetAtPath<BossPhaseBurstSkillSO>(SwordRobotBossSetup.Folder + "/Phase2_OpeningBurst.asset");
        if (skill == null) throw new InvalidOperationException("The phase-two opening burst is missing.");
        var flight = root.GetComponent<BossPhaseEntryFlight>() ?? root.AddComponent<BossPhaseEntryFlight>();
        var serialized = new SerializedObject(flight);
        serialized.FindProperty("_burstSkill").objectReferenceValue = skill;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }
}
