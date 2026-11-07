using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Installs mobility only into the two owned boss comparison prefabs and their controllers.</summary>
public static class BossMobilitySetup
{
    public const string JumpStartState = "MobilityJumpStart";
    public const string JumpLiftState = "MobilityJumpLift";
    public const string JumpAirState = "MobilityJumpAir";
    public const string JumpLandState = "MobilityJumpLand";
    public const string DashReadyState = "MobilityDashReady";
    public const string DashSlashState = "MobilityDashSlash";
    public const string SlamChargeState = "MobilitySlamCharge";
    public const string SlamAirState = "MobilitySlamAir";
    public const string SlamLandState = "MobilitySlamLand";
    private const string CoreRoot = "Assets/Nephilite Studios/Straight Sword Animation Set/Animations/Core/";
    private const string SwordRoot = "Assets/Nephilite Studios/Straight Sword Animation Set/Animations/Straight Sword Animations/Nephilim Animations (Remastered)/Straight Sword Two Hand/";
    private const string SourceFolder = "Assets/_Project/Generated/Bosses/SwordRobotBoss";
    private const float SlamStartupTime = .45f, SlamRecoveryTime = .5f;

    public static void Configure(GameObject root, string outputFolder)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        var owner = root.GetComponent<Boss>();
        var animator = root.GetComponentInChildren<Animator>(true);
        if (owner == null || animator == null || animator.avatar == null || !animator.avatar.isHuman)
            throw new InvalidOperationException("Boss mobility requires the copied humanoid sword robot.");
        if (!outputFolder.StartsWith("Assets/", StringComparison.Ordinal))
            throw new ArgumentException("Mobility output must be under Assets.", nameof(outputFolder));
        EnsureFolder(outputFolder);
        string folder = outputFolder + "/Mobility";
        string clipsFolder = folder + "/Animations";
        EnsureFolder(folder);
        EnsureFolder(clipsFolder);

        bool additive = root.GetComponent<BossAdditiveHitReaction>() != null;
        string controllerPath = outputFolder + (additive ? "/Additive_Boss.controller" : "/FinalIK_Boss.controller");
        var controller = OwnedController(animator, controllerPath);
        var jumpStart = CopyClip(CoreRoot + "core_th_jump_01_start.fbx", clipsFolder + "/Mobility_JumpStart.anim", false);
        var jumpLift = CopyClip(CoreRoot + "core_th_jump_01_lift.fbx", clipsFolder + "/Mobility_JumpLift.anim", false);
        var jumpAir = CopyClip(CoreRoot + "core_th_jump_01_idle.fbx", clipsFolder + "/Mobility_JumpAir.anim", true);
        var jumpLand = CopyClip(CoreRoot + "core_th_jump_01_end.fbx", clipsFolder + "/Mobility_JumpLand.anim", false);
        var slamCharge = CopyClip(SwordRoot + "straight_sword_th_jump_attack_02_charge.fbx", clipsFolder + "/Mobility_SlamCharge.anim", false);
        var slamAir = CopyClip(SwordRoot + "straight_sword_th_jump_attack_02_idle.fbx", clipsFolder + "/Mobility_SlamAir.anim", true);
        var slamLand = CopyClip(SwordRoot + "straight_sword_th_jump_attack_02_end.fbx", clipsFolder + "/Mobility_SlamLand.anim", false);
        var dash = AssetDatabase.LoadAssetAtPath<AnimationClip>(SourceFolder + "/Animations/Dash.anim");
        var ready = AssetDatabase.LoadAssetAtPath<AnimationClip>(SourceFolder + "/Animations/Idle.anim");
        if (dash == null || ready == null) throw new InvalidOperationException("The existing sword robot dash/guard clips are missing.");
        var machine = controller.layers[0].stateMachine;
        State(machine, JumpStartState, jumpStart, new Vector3(700f, 40f));
        State(machine, JumpLiftState, jumpLift, new Vector3(700f, 100f));
        State(machine, JumpAirState, jumpAir, new Vector3(700f, 160f));
        State(machine, JumpLandState, jumpLand, new Vector3(700f, 220f));
        State(machine, DashReadyState, ready, new Vector3(950f, 40f));
        State(machine, DashSlashState, dash, new Vector3(950f, 100f));
        var obsoleteSprint = machine.states.FirstOrDefault(child => child.state.name == "MobilitySprint").state;
        if (obsoleteSprint != null) machine.RemoveState(obsoleteSprint);
        State(machine, SlamChargeState, slamCharge, new Vector3(1200f, 40f), slamCharge.length / SlamStartupTime);
        State(machine, SlamAirState, slamAir, new Vector3(1200f, 100f));
        State(machine, SlamLandState, slamLand, new Vector3(1200f, 160f), slamLand.length / SlamRecoveryTime);
        EditorUtility.SetDirty(machine);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);
        animator.runtimeAnimatorController = controller;

        var metadata = AssetDatabase.LoadAssetAtPath<MonsterDashAttackSO>(SourceFolder + "/Dash.asset");
        if (metadata == null) throw new InvalidOperationException("The existing sword robot dash skill metadata is missing.");
        var escape = Skill(folder, "EscapeJump", BossMobilityKind.EscapeJump, metadata);
        escape.startupTime = .2f;
        escape.moveSpeed = 10f;
        escape.maxDistance = 6f;
        escape.stopDistance = 0f;
        escape.jumpHeight = 1.2f;
        escape.jumpDuration = .65f;
        escape.recoveryTime = .25f;
        JumpAnimations(escape);

        var slash = Skill(folder, "DashSlash", BossMobilityKind.DashSlash, metadata);
        slash.startupTime = .35f;
        slash.moveSpeed = 14f;
        slash.maxDistance = 12f;
        slash.stopDistance = 2f;
        slash.recoveryTime = .4f;
        slash.damage = 1.5f;
        slash.startupAnimName = DashReadyState;
        slash.animName = DashSlashState;

        var slam = Skill(folder, "JumpSlam", BossMobilityKind.JumpSlam, metadata);
        slam.startupTime = SlamStartupTime;
        slam.moveSpeed = 12f;
        slam.maxDistance = 12f;
        slam.stopDistance = 1.5f;
        slam.jumpHeight = 1.8f;
        slam.jumpDuration = .65f;
        slam.recoveryTime = SlamRecoveryTime;
        slam.damage = 1.5f;
        slam.slamRadius = 2.5f;
        slam.hitBoxOffset = new Vector3(0f, 1f, 0f);
        slam.startupAnimName = SlamChargeState;
        slam.liftAnimName = SlamAirState;
        slam.animName = SlamAirState;
        slam.landingAnimName = SlamLandState;
        foreach (var skill in new[] { escape, slash, slam })
        {
            skill.executeRange = skill.maxDistance + skill.stopDistance;
            skill.preferredRange = skill.stopDistance;
            skill.telegraphDuration = skill.damage > 0f ? skill.startupTime : 0f;
            EditorUtility.SetDirty(skill);
            AssetDatabase.SaveAssetIfDirty(skill);
        }

        var settings = owner.Mobility ?? new BossMobilitySettings();
        settings.enabled = true;
        settings.hitCount = 5;
        settings.hitWindow = 2f;
        settings.cornerDuration = .6f;
        settings.cornerPlayerDistance = 4f;
        settings.cornerEdgeDistance = 1.2f;
        settings.blockedRetreatDistance = 2f;
        settings.farDistance = 7f;
        settings.farDuration = .5f;
        settings.escapeCooldown = 6f;
        settings.approachCooldown = 4.5f;
        settings.sharedCooldown = 2f;
        settings.retryInterval = .5f;
        settings.escapeJump = escape;
        settings.dashSlash = slash;
        settings.jumpSlam = slam;
        owner.Mobility = settings;
        EditorUtility.SetDirty(owner);
        EditorUtility.SetDirty(animator);
    }

    public static void RemoveObsoleteAssets(string outputFolder)
    {
        // Both owned controllers and prefabs are updated before removing their former assets.
        AssetDatabase.DeleteAsset(outputFolder + "/Mobility/Sprint.asset");
        AssetDatabase.DeleteAsset(outputFolder + "/Mobility/Animations/Mobility_Sprint.anim");
    }

    private static AnimatorController OwnedController(Animator animator, string path)
    {
        var source = animator.runtimeAnimatorController as AnimatorController;
        if (source == null || source.layers.Length == 0)
            throw new InvalidOperationException("Expected a configured sword robot AnimatorController.");
        if (AssetDatabase.GetAssetPath(source) == path) return source;
        var owned = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (owned != null) return owned;
        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (string.IsNullOrEmpty(sourcePath) || !AssetDatabase.CopyAsset(sourcePath, path))
            throw new InvalidOperationException("Could not copy the variant's mobility AnimatorController.");
        owned = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (owned == null) throw new InvalidOperationException("The copied mobility AnimatorController failed to load.");
        return owned;
    }

    private static BossMobilitySkillSO Skill(string folder, string name, BossMobilityKind kind, MonsterDashAttackSO metadata)
    {
        string path = folder + "/" + name + ".asset";
        var skill = AssetDatabase.LoadAssetAtPath<BossMobilitySkillSO>(path);
        if (skill == null)
        {
            skill = ScriptableObject.CreateInstance<BossMobilitySkillSO>();
            AssetDatabase.CreateAsset(skill, path);
        }
        bool damaging = kind == BossMobilityKind.DashSlash || kind == BossMobilityKind.JumpSlam;
        skill.name = name;
        skill.kind = kind;
        skill.attackSpeed = 1f;
        skill.engageRange = 20f;
        skill.minimumRange = 0f;
        skill.weight = 1f;
        skill.repeatWeightMultiplier = 1f;
        // The mobility scheduler owns shared cooldowns, rather than the normal attack selector.
        skill.cooldown = 0f;
        skill.isParryable = damaging && metadata.isParryable;
        skill.staggerLevel = metadata.staggerLevel;
        skill.playerHitReaction = metadata.playerHitReaction;
        skill.playerDownSeconds = metadata.playerDownSeconds;
        skill.playerGetUpSeconds = metadata.playerGetUpSeconds;
        skill.SwingAudio = Sound(metadata.SwingAudio, damaging);
        skill.HitAudio = Sound(metadata.HitAudio, damaging);
        skill.LandingAudio = Sound(metadata.LandingAudio, damaging);
        skill.damage = 0f;
        skill.knockbackForce = damaging ? metadata.knockbackForce : 0f;
        skill.hitBoxRadius = metadata.hitBoxRadius;
        skill.hitBoxOffset = metadata.hitBoxOffset;
        skill.slamRadius = 2.5f;
        skill.jumpHeight = 0f;
        skill.jumpDuration = .65f;
        skill.startupAnimName = string.Empty;
        skill.liftAnimName = string.Empty;
        skill.animName = string.Empty;
        skill.landingAnimName = string.Empty;
        return skill;
    }

    private static CombatSoundSettings Sound(CombatSoundSettings source, bool enabled)
    {
        if (!enabled || source == null) return new CombatSoundSettings();
        return new CombatSoundSettings { Clip = source.Clip, Accent = source.Accent,
            Volume = source.Volume, AccentVolume = source.AccentVolume, PitchRange = source.PitchRange };
    }

    private static void JumpAnimations(BossMobilitySkillSO skill)
    {
        skill.startupAnimName = JumpStartState;
        skill.liftAnimName = JumpLiftState;
        skill.animName = JumpAirState;
        skill.landingAnimName = JumpLandState;
    }

    private static AnimationClip CopyClip(string sourcePath, string destination, bool loop)
    {
        var source = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>()
            .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
        if (source == null || !source.humanMotion)
            throw new InvalidOperationException("A humanoid mobility animation is missing: " + sourcePath);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(destination);
        if (clip == null)
        {
            clip = Object.Instantiate(source);
            AssetDatabase.CreateAsset(clip, destination);
        }
        else EditorUtility.CopySerialized(source, clip);
        clip.name = System.IO.Path.GetFileNameWithoutExtension(destination);
        AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        settings.loopBlend = loop;
        // World movement and the jump arc belong to BossMobilityAction. Keep authored root
        // movement extracted rather than baking a second displacement into the visible rig.
        settings.keepOriginalOrientation = true;
        settings.keepOriginalPositionY = true;
        settings.keepOriginalPositionXZ = true;
        settings.loopBlendOrientation = false;
        settings.loopBlendPositionY = false;
        settings.loopBlendPositionXZ = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssetIfDirty(clip);
        return clip;
    }

    private static void State(AnimatorStateMachine machine, string name, AnimationClip clip, Vector3 position, float speed = 1f)
    {
        var state = machine.states.FirstOrDefault(child => child.state.name == name).state;
        if (state == null) state = machine.AddState(name, position);
        state.motion = clip;
        state.speed = Mathf.Max(.01f, speed);
        state.speedParameterActive = false;
        state.timeParameterActive = false;
        state.writeDefaultValues = true;
        EditorUtility.SetDirty(state);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int slash = folder.LastIndexOf('/');
        if (slash < 0) throw new ArgumentException("Expected an Assets folder path.", nameof(folder));
        string parent = folder.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
    }
}
