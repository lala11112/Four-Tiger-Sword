using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Rebuilds only the owned sword robot assets. Original model/animation imports stay intact.</summary>
public static class SwordRobotBossSetup
{
    public const string Folder = "Assets/_Project/Generated/Bosses/SwordRobotBoss";
    public const string PrefabPath = Folder + "/SwordRobotBoss.prefab";
    private const string ModelPath = "Assets/BossMonster_Rigged/Boss_Monster.prefab";
    private const string WeaponPath = "Assets/BossWeapon_3m/Boss_Weapon_For_3m_Boss.prefab";
    private const string AnimRoot = "Assets/Nephilite Studios/Straight Sword Animation Set/Animations/Straight Sword Animations/Nephilim Animations (Remastered)/";

    [MenuItem("Tools/SainGum/Sword Robot Boss/Build and Apply to Current Run")]
    public static void BuildMenu() => Debug.Log(Build());

    [MenuItem("Tools/SainGum/Sword Robot Boss/Apply Phase Two Upgrade")]
    private static void ApplyUpgradeMenu() => Debug.Log(ApplyCurrentBossUpgrade());

    /// <summary>Updates existing combat assets without rebuilding imported animation, materials or the run route.</summary>
    public static string ApplyCurrentBossUpgrade()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Apply the boss upgrade in Edit Mode.");
        var data = Required<BossData>("Stats");
        data.baseAtk = 315f;
        Save(data);
        var phase1 = Required<BossPhaseData>("Phase1");
        var phase2 = Required<BossPhaseData>("Phase2");
        phase1.combatProfile.repositionDuration = .7f;
        phase2.combatProfile.repositionDuration = .4f;
        Save(phase1.combatProfile); Save(phase2.combatProfile);
        foreach (string name in new[] { "Combo_Phase2_Pattern", "Dash_Pattern" })
        {
            var pattern = Required<BossTacticalPatternSO>(name);
            foreach (var step in pattern.steps)
                if (step != null && step.kind == BossTacticalStepKind.Attack && step.attackPolicy != null)
                    step.attackPolicy.lockFraction = .7f;
            Save(pattern);
        }
        ConfigurePhaseEntry(phase1, phase2);
        foreach (string path in new[] { PrefabPath, BossHitReactionVariantsSetup.FinalIKPath, BossHitReactionVariantsSetup.AdditivePath })
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                throw new InvalidOperationException("The existing boss prefab is missing: " + path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var boss = root.GetComponent<Boss>();
                var poise = new SerializedObject(root.GetComponent<PoiseHandler>());
                poise.FindProperty("_maxPoise").floatValue = 500f;
                poise.ApplyModifiedPropertiesWithoutUndo();
                if (path != PrefabPath)
                {
                    boss.PhaseHealthMultiplier = .7f;
                    boss.AdditionalPatternRecovery = .2f;
                    boss.Mobility.farDistance = 7f;
                    boss.Mobility.approachCooldown = 4.5f;
                }
                BossPhaseTwoVisualSetup.Configure(root, path == PrefabPath ? Folder : BossHitReactionVariantsSetup.Folder);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved) throw new InvalidOperationException("Could not save the upgraded boss: " + path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        return "Upgraded sword robot balance, phase-two model/weapon and one-time phase-entry burst; current run route preserved.";
    }

    public static string Build()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Build in Edit Mode.");
        EnsureFolder(Folder); EnsureFolder(Folder + "/Animations");
        var controller = BuildAnimator();
        PlayerKnockdownAnimationSetup.Run();
        var basic = Asset<MonsterDefaultAttackSO>("Basic");
        Configure(basic, "Basic", 3.7f, .467f, 0f);
        basic.damage = 1.1f; basic.knockbackForce = 20f;
        basic.hitStartTime = .467f; basic.hitDuration = .2f; basic.attackDuration = 1.667f;
        basic.hitBoxShape = HitBoxShape.Sphere; basic.hitBoxOffset = new Vector3(0f, 1f, 1.8f); basic.hitBoxRadius = 2f;

        var combo1 = Combo("Combo_Phase1", 1f);
        var combo2 = Combo("Combo_Phase2", 1.12f);
        var charge1 = Charge("Charge_Phase1", 1.15f, 1f);
        var charge2 = Charge("Charge_Phase2", .9f, 1.08f);
        var dash = Asset<MonsterDashAttackSO>("Dash");
        Configure(dash, "Dash", 11f, .85f, 6f);
        dash.startupAnimName = "DashReady";
        dash.minimumRange = 4f; dash.damage = 1.5f; dash.knockbackForce = 34f;
        dash.startupTime = .85f; dash.dashDuration = .5f; dash.dashSpeed = 14f; dash.recoveryTime = .85f;
        dash.hitBoxOffset = new Vector3(0f, 1f, 1.5f); dash.hitBoxRadius = 1.65f;
        var sweep = Asset<MonsterDefaultAttackSO>("Sweep");
        Configure(sweep, "Sweep", 4.5f, 1.15f, 7f);
        sweep.startupAnimName = "SweepReady"; sweep.animationStartDelay = .85f;
        sweep.damage = 1.6f; sweep.knockbackForce = 30f;
        sweep.hitStartTime = 1.15f; sweep.hitDuration = .5f; sweep.attackDuration = 2.45f;
        sweep.hitBoxShape = HitBoxShape.Sphere; sweep.hitBoxOffset = new Vector3(0f, 1f, 0f); sweep.hitBoxRadius = 4.5f;

        var phase1 = Asset<BossPhaseData>("Phase1");
        var phase2 = Asset<BossPhaseData>("Phase2");
        ConfigurePhase(phase1, 1, 7800f, 1f, new MonsterSkillData[] { basic, combo1, charge1 });
        ConfigurePhase(phase2, 2, 9360f, 1.25f, new MonsterSkillData[] { combo2, charge2, dash, sweep });
        phase1.combatProfile = Profile("Phase1_Combat", false,
            Option("Basic_Pattern", basic, false, 4f, .5f, .25f, 0f, 3.7f, 95f),
            Option("Combo_Phase1_Pattern", combo1, false, 2.4f, 3f, .4f, 0f, 3.8f, 90f),
            Option("Charge_Phase1_Pattern", charge1, true, 1.6f, 6f, .5f, 0f, 4.1f, 120f));
        phase2.combatProfile = Profile("Phase2_Combat", true,
            Option("Combo_Phase2_Pattern", combo2, true, 3f, 2f, .35f, 0f, 3.8f, 100f),
            Option("Charge_Phase2_Pattern", charge2, true, 2f, 5f, .4f, 0f, 4.1f, 110f),
            Option("Dash_Pattern", dash, true, 3.5f, 6f, .4f, 4f, 11f, 65f),
            Option("Sweep_Pattern", sweep, true, 2.5f, 7f, .5f, 0f, 4.5f, 180f));
        // A nearby opponent behind the boss encourages its wide sweep.
        var rearSweep = Option("RearSweep_Pattern", sweep, true, 6f, 7f, .5f, 0f, 4.5f, 180f);
        rearSweep.condition.minimumAngle = 100f;
        phase2.combatProfile.patterns.Add(rearSweep);
        ConfigurePhaseEntry(phase1, phase2);

        var rewards = Asset<BossRewardGroupSO>("Rewards"); rewards.gold = 150;
        var data = Asset<BossData>("Stats");
        data.bossId = "sword_robot_guardian"; data.bossName = "철검 수문장";
        data.maxHp = 17160; data.baseAtk = 315f; data.baseDef = 450;
        data.criticalChance = 0f; data.criticalDamage = 1.5f;
        data.elementType = ElementType.ELEMENT_NONE; data.combatRange = 30f; data.combatExitRange = 40f;
        data.knockbackMoveTime = .16f; data.knockbackMaxDistance = .65f;
        data.attackCooldown = 0f; data.staggerResistLevel = StaggerResistLevel.LOW;
        data.monsterSkillData = phase1.skillSet; data.rewardGroup = rewards;
        data.phaseDownSeconds = 2.667f; data.phaseHoldSeconds = .65f; data.phaseReviveSeconds = 2.167f;
        data.groggyDamageMult = 1.5f; data.statusImmunityList = new List<string> { "Airborne" };

        GameObject prefab = BuildPrefab(data, new[] { phase1, phase2 }, controller, sweep);
        var run = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/_Game/RunProgression/DefaultRun.asset");
        if (run == null) throw new InvalidOperationException("Current run is missing.");
        var runSO = new SerializedObject(run);
        var route = runSO.FindProperty("chapters").GetArrayElementAtIndex(0).FindPropertyRelative("stages").GetArrayElementAtIndex(0).FindPropertyRelative("bossPrefab");
        if (route.objectReferenceValue == null)
        { route.objectReferenceValue = prefab; runSO.ApplyModifiedPropertiesWithoutUndo(); }
        foreach (string guid in AssetDatabase.FindAssets("", new[] { Folder }))
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
        }
        AssetDatabase.SaveAssetIfDirty(run);
        return "Built SwordRobotBoss (2 phases, one revival, five attacks) and preserved the current run boss.";
    }

    private static MonsterComboAttackSO Combo(string name, float speed)
    {
        var combo = Asset<MonsterComboAttackSO>(name);
        Configure(combo, "Combo1", 3.8f, .467f, 2f); combo.attackSpeed = speed;
        combo.baseDamage = .7f; combo.knockbackForce = 12f; combo.hitBoxRadius = 2.1f;
        combo.attackDuration = 3.533f; combo.hitBoxShape = HitBoxShape.Sphere;
        combo.stepStopDistance = 1.4f;
        combo.hits = new List<EnemyHitEvent>();
        float[] starts = { 0f, .95f, 1.8f };
        float[] hits = { .467f, 1.417f, 2.2f };
        float[] stepStarts = { .3f, 1.15f, 1.98f };
        float[] stepDistances = { .65f, .8f, 1.05f };
        // Early pushes stay shorter than the matching forward steps so the combo can connect.
        // The final swing creates distance and a clear knockdown/punish boundary.
        float[] knockbackForces = { 12f, 14f, 32f };
        for (int i = 0; i < 3; i++) combo.hits.Add(new EnemyHitEvent {
            animationState = "Combo" + (i + 1), animationStartTime = starts[i],
            hitStartTime = hits[i], hitDuration = .18f, damage = i == 2 ? 1.1f : .7f,
            hitBoxOffset = new Vector3(0f, 1f, 1.8f), overrideReaction = true,
            stepStartTime = stepStarts[i], stepDistance = stepDistances[i], stepDuration = i == 2 ? .36f : .3f,
            knockbackForce = knockbackForces[i], staggerLevel = StaggerResistLevel.HIGH,
            knockdown = i == 2, downSeconds = .3f, getUpSeconds = .5f
        });
        return combo;
    }
    private static void ConfigurePhaseEntry(BossPhaseData phase1, BossPhaseData phase2)
    {
        var charge = Required<MonsterBossChargeAttackSO>("Charge_Phase2");
        var burst = Asset<BossPhaseBurstSkillSO>("Phase2_OpeningBurst");
        Configure(burst, charge.animName, 1000f, 2.5f, 0f);
        burst.animName = charge.animName;
        burst.radius = 12f; burst.chargeDuration = 2.5f; burst.activeDuration = .15f;
        burst.recoveryTime = 1.2f; burst.damage = 2f; burst.knockbackForce = 38f;
        burst.heightTolerance = 4f; burst.preparationDuration = charge.chargePoseTime;
        burst.chargePoseNormalized = charge.chargePoseNormalized; burst.releaseHitDelay = charge.releaseHitDelay;
        burst.isParryable = false; burst.playerHitReaction = EnemyPlayerHitReaction.Knockdown;
        burst.SwingAudio = charge.SwingAudio; burst.HitAudio = charge.HitAudio;
        var entry = Asset<BossTacticalPatternSO>("Phase2_EntryPattern");
        entry.loop = false;
        entry.steps = new List<BossTacticalStep> { new BossTacticalStep {
            kind = BossTacticalStepKind.Attack, skill = burst, timeout = 8f,
            attackPolicy = new BossAttackPolicy { enabled = true, turnSpeed = 0f, lockFraction = 0f,
                armorDuringStartup = true, armorDuringActive = true }
        } };
        phase1.entryPattern = null;
        phase2.entryPattern = entry;
        Save(burst); Save(entry); Save(phase1); Save(phase2);
    }
    private static MonsterBossChargeAttackSO Charge(string name, float hold, float speed)
    {
        var charge = Asset<MonsterBossChargeAttackSO>(name);
        Configure(charge, "Charge", 4.1f, 1.467f, 5f); charge.attackSpeed = speed;
        charge.playerHitReaction = EnemyPlayerHitReaction.Knockdown;
        charge.damage = 3f; charge.knockbackForce = 38f;
        charge.chargePoseTime = 16f / 30f; charge.chargePoseNormalized = 16f / 82f;
        charge.chargeSeconds = hold; charge.releaseHitDelay = 28f / 30f;
        charge.hitDuration = .2f; charge.recoveryTime = 1.0667f;
        charge.hitBoxShape = HitBoxShape.Sphere; charge.hitBoxOffset = new Vector3(0f, 1f, 2f); charge.hitBoxRadius = 2.2f;
        charge.interruptOnKnockback = true;
        return charge;
    }
    private static void Configure(MonsterSkillData skill, string state, float range, float telegraph, float cooldown)
    {
        skill.animName = state; skill.executeRange = range; skill.engageRange = 20f;
        skill.preferredRange = Mathf.Min(range, 3f); skill.minimumRange = 0f;
        skill.attackSpeed = 1f; skill.cooldown = cooldown; skill.weight = 1f;
        skill.telegraphDuration = telegraph; skill.isParryable = true; skill.staggerLevel = StaggerResistLevel.HIGH;
        skill.playerHitReaction = EnemyPlayerHitReaction.Normal;
        skill.playerDownSeconds = .3f; skill.playerGetUpSeconds = .5f;
        var source = AssetDatabase.LoadAssetAtPath<MonsterSkillData>("Assets/_Project/Generated/Bosses/Test_Boss/Test_Boss_Attack1.asset");
        if (source != null) { skill.SwingAudio = source.SwingAudio; skill.HitAudio = source.HitAudio; }
    }
    private static void ConfigurePhase(BossPhaseData phase, int index, float hp, float attack, MonsterSkillData[] skills)
    {
        phase.phaseId = "sword_robot_phase_" + index; phase.phaseMaxHp = hp;
        phase.atkMultiplier = attack; phase.defMultiplier = 1f; phase.pattern = null;
        phase.skillSet = Asset<MonsterSkillDataSO>("Phase" + index + "_Skills");
        phase.skillSet.monsterName = "철검 수문장"; phase.skillSet.skillData = skills.ToList();
        phase.note = index == 1 ? "기본 베기 / 3연격 / 차징. 차징 중 넉백으로 그로기. 체력 소진 시 한 번 부활."
            : "공격력 1.25배, 연격·차징 가속. 돌진과 주변 휩쓸기 추가. 소진 시 최종 처치.";
    }
    private static BossPatternOption Option(string name, MonsterSkillData skill, bool armor, float weight, float cooldown, float recovery, float min, float max, float angle)
    {
        var pattern = Asset<BossTacticalPatternSO>(name);
        pattern.steps = new List<BossTacticalStep> { new BossTacticalStep {
            kind = BossTacticalStepKind.Attack, skill = skill, timeout = 12f,
            attackPolicy = new BossAttackPolicy { enabled = true, turnSpeed = 150f,
                lockFraction = name == "Combo_Phase2_Pattern" || name == "Dash_Pattern" ? .7f : .6f,
                armorDuringStartup = armor, armorDuringActive = armor }
        } };
        return new BossPatternOption { pattern = pattern, weight = weight, cooldown = cooldown,
            recovery = recovery, repeatMultiplier = .2f, preferredDistance = skill.preferredRange,
            condition = new BossCombatCondition { minimumDistance = min, maximumDistance = max, maximumAngle = angle,
                visibility = BossVisibilityRequirement.Visible } };
    }
    private static BossCombatProfileSO Profile(string name, bool second, params BossPatternOption[] options)
    {
        var profile = Asset<BossCombatProfileSO>(name);
        profile.patterns = options.ToList(); profile.decisionInterval = .2f;
        profile.preferredMinDistance = 2.4f; profile.preferredMaxDistance = 3.3f;
        profile.turnSpeed = second ? 220f : 180f; profile.parryRecovery = 1f;
        profile.strafeDuration = .8f; profile.movementTimeout = 4f; profile.stuckTimeout = 1f;
        profile.repositionDuration = second ? .4f : .7f;
        return profile;
    }
    private static AnimatorController BuildAnimator()
    {
        string path = Folder + "/SwordRobotBoss.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path) ?? AnimatorController.CreateAnimatorControllerAtPath(path);
        foreach (string param in new[] { "DirX", "DirY" })
            if (!controller.parameters.Any(p => p.name == param)) controller.AddParameter(param, AnimatorControllerParameterType.Float);
        var machine = controller.layers[0].stateMachine;
        var idle = Clip("Idle", "straight_sword_th_guard_idle_01", true);
        State(controller, "Idle", idle); State(controller, "DashReady", idle); State(controller, "SweepReady", idle);
        State(controller, "Basic", Clip("Basic", "straight_sword_th_light_attack_01"));
        for (int i = 1; i <= 3; i++) State(controller, "Combo" + i, Clip("Combo" + i, "straight_sword_th_light_attack_0" + i));
        State(controller, "Charge", Clip("Charge", "straight_sword_th_charged_attack_01"));
        State(controller, "Sweep", Clip("Sweep", "straight_sword_th_back_step_attack_02_release"));
        State(controller, "Dash", Clip("Dash", "straight_sword_th_run_attack_01"));
        State(controller, "Hit", Clip("Hit", "straight_sword_th_guard_break_01"));
        State(controller, "Groggy", Clip("Groggy", "core_main_stance_broken_f_01"));
        State(controller, "Die", Clip("Die", "straight_sword_th_death_01"));
        State(controller, "PhaseDown", Clip("PhaseDown", "core_riposte_death_01"));
        State(controller, "PhaseRevive", Clip("PhaseRevive", "core_main_riposte_getup_01"));
        var move = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Move") ?? machine.AddState("Move");
        var tree = move.motion as BlendTree;
        if (tree == null) { tree = new BlendTree { name = "Directional Guard", blendType = BlendTreeType.FreeformCartesian2D }; AssetDatabase.AddObjectToAsset(tree, controller); move.motion = tree; }
        tree.blendParameter = "DirX"; tree.blendParameterY = "DirY";
        tree.children = new ChildMotion[0]; tree.AddChild(idle, Vector2.zero);
        tree.AddChild(Clip("WalkF", "straight_sword_th_walk_guard_F_01", true), new Vector2(0f, 3.5f));
        tree.AddChild(Clip("WalkB", "straight_sword_th_walk_guard_B_01", true), new Vector2(0f, -3.5f));
        tree.AddChild(Clip("WalkL", "straight_sword_th_walk_guard_L_01", true), new Vector2(-3.5f, 0f));
        tree.AddChild(Clip("WalkR", "straight_sword_th_walk_guard_R_01", true), new Vector2(3.5f, 0f));
        machine.defaultState = move;
        EditorUtility.SetDirty(tree); EditorUtility.SetDirty(controller);
        return controller;
    }
    private static void State(AnimatorController controller, string name, AnimationClip clip)
    {
        var machine = controller.layers[0].stateMachine;
        var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? machine.AddState(name);
        state.motion = clip; state.writeDefaultValues = true;
        EditorUtility.SetDirty(state);
    }
    private static AnimationClip Clip(string name, string sourceName, bool loop = false)
    {
        string path = AssetDatabase.FindAssets(sourceName, new[] { AnimRoot.TrimEnd('/') })
            .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == sourceName && p.EndsWith(".fbx"));
        if (path == null) throw new InvalidOperationException("Missing animation " + sourceName);
        var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
        string destination = Folder + "/Animations/" + name + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(destination);
        if (clip == null) { clip = UnityEngine.Object.Instantiate(source); AssetDatabase.CreateAsset(clip, destination); }
        else EditorUtility.CopySerialized(source, clip);
        clip.name = name; AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop; settings.loopBlend = loop;
        // Preserve source root extraction; baking its forward displacement into the
        // skeleton would separate the visible sword from the action's world hitbox.
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }
    private static GameObject BuildPrefab(BossData data, BossPhaseData[] phases, AnimatorController controller, MonsterDefaultAttackSO sweep)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var root = new GameObject("SwordRobotBoss");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        root.layer = LayerMask.NameToLayer("Enemy");
        try
        {
            var pivot = new GameObject("VisualPivot"); pivot.transform.SetParent(root.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), scene);
            model.name = "SwordRobotModel"; model.transform.SetParent(pivot.transform, false);
            var animator = model.GetComponentInChildren<Animator>(true);
            animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.fireEvents = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand == null) throw new InvalidOperationException("Boss model has no right hand.");
            var weapon = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPath), scene);
            weapon.name = "BossGreatsword"; weapon.transform.SetParent(hand, true);
            weapon.transform.localPosition = Vector3.zero;
            weapon.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            SwordRobotBossRigSetup.Configure(animator, weapon, Folder, controller);
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;
            var nav = root.AddComponent<NavMeshAgent>(); nav.radius = .65f; nav.height = 3f; nav.baseOffset = 0f;
            nav.speed = 3.5f; nav.acceleration = 12f; nav.angularSpeed = 180f; nav.stoppingDistance = 2.6f;
            var capsule = root.AddComponent<CapsuleCollider>(); capsule.radius = .65f; capsule.height = 3f; capsule.center = Vector3.up * 1.5f;
            var boss = root.AddComponent<ConfiguredBoss>();
            boss.HurtDuration = .35f; boss.GroggyDuration = 3f; boss.StaggerResistLevel = StaggerResistLevel.LOW;
            boss.EnemyStat = root.GetComponent<EnemyStat>();
            var bossSO = new SerializedObject(boss); bossSO.FindProperty("BossData").objectReferenceValue = data;
            var list = bossSO.FindProperty("PhaseDatas"); list.arraySize = phases.Length;
            for (int i = 0; i < phases.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = phases[i];
            bossSO.ApplyModifiedPropertiesWithoutUndo();
            var stats = new SerializedObject(boss.EnemyStat); stats.FindProperty("baseData").objectReferenceValue = data; stats.ApplyModifiedPropertiesWithoutUndo();
            var poise = new SerializedObject(root.GetComponent<PoiseHandler>());
            poise.FindProperty("_maxPoise").floatValue = 500f; poise.FindProperty("_poiseRegen").floatValue = 20f; poise.FindProperty("_breakDuration").floatValue = 3f; poise.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<StatusEffectHandler>(); root.AddComponent<EnemyHitFlash>(); root.AddComponent<EnemyDeathFade>();
            var presentation = root.AddComponent<SwordRobotBossPresentation>();
            presentation.visualPivot = pivot.transform; presentation.sweepSkill = sweep;
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Telegraph.mat");
            if (material == null) { material = new Material(Shader.Find("Sprites/Default")); AssetDatabase.CreateAsset(material, Folder + "/Telegraph.mat"); }
            var ring = new GameObject("AttackWarning"); ring.transform.SetParent(root.transform, false);
            var line = ring.AddComponent<LineRenderer>(); line.sharedMaterial = material; line.useWorldSpace = true;
            line.widthMultiplier = .055f; line.numCornerVertices = 3; line.enabled = false;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            presentation.warning = line;
            var trailGO = new GameObject("BladeTrail"); trailGO.transform.SetParent(weapon.transform, false); trailGO.transform.localPosition = Vector3.up * 1.5f;
            var trail = trailGO.AddComponent<TrailRenderer>(); trail.sharedMaterial = material; trail.time = .12f; trail.widthMultiplier = .3f;
            trail.startColor = new Color(1f, .65f, .2f, .6f); trail.endColor = new Color(1f, .2f, .05f, 0f); trail.emitting = false;
            presentation.bladeTrail = trail;
            BossPhaseTwoVisualSetup.Configure(root, Folder);
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static T Asset<T>(string name) where T : ScriptableObject
    {
        string path = Folder + "/" + name + ".asset";
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) { asset = ScriptableObject.CreateInstance<T>(); asset.name = name; AssetDatabase.CreateAsset(asset, path); }
        return asset;
    }
    private static T Required<T>(string name) where T : ScriptableObject
        => AssetDatabase.LoadAssetAtPath<T>(Folder + "/" + name + ".asset")
            ?? throw new InvalidOperationException("Missing authored sword robot asset: " + name);
    private static void Save(UnityEngine.Object asset)
    { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, split));
        AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
}
