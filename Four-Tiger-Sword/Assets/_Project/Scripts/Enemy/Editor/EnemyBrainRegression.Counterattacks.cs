using System.Threading.Tasks;
using Baekho.Generated.a3355a1c8240244a995e4f671b0dc48bb;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class EnemyBrainRegression
{
    private static async Task ExerciseCounterattacks(RuntimeFixture f)
    {
        if (CombatSettings.Active == null)
        {
            var host = new GameObject("Counterattack element table");
            host.SetActive(false);
            SceneManager.MoveGameObjectToScene(host, f.Scene);
            var settings = host.AddComponent<CombatSettings>();
            Set(settings, "_elementTable", AssetDatabase.LoadAssetAtPath<ElementEffectiveManager>(
                "Assets/_Project/CombatData/ElementEffectiveManager.asset"));
            host.SetActive(true);
        }

        var body = new GameObject("Counterattack player");
        body.SetActive(false);
        SceneManager.MoveGameObjectToScene(body, f.Scene);
        body.layer = LayerMask.NameToLayer("Player");
        body.transform.position = f.Origin + Vector3.forward;
        body.AddComponent<Animator>().runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            "Assets/_Project/Animations/AnimController/BDJ.controller");
        var player = body.AddComponent<PlayerController>();
        Set(player, "_WaterFormActionData", AssetDatabase.LoadAssetAtPath<WaterFormActionDataSO>(
            "Assets/_Project/Scripts/FormActions/WaterFromAction.asset"));
        Set(player, "_EarthFormActionData", AssetDatabase.LoadAssetAtPath<EarthFormActionDataSO>(
            "Assets/_Project/Scripts/FormActions/EarthFormAction.asset"));
        player.Gravity = 0f;
        player.HitStaggerResistance = StaggerResistLevel.SUPER_ARMOR;
        body.GetComponent<PlayerStatManager>().Initialize(f.New<PlayerStatData>());
        body.SetActive(true);
        // Keep the receiver stationary and independent of live input during the frame-based checks.
        typeof(PlayerController).GetProperty(nameof(PlayerController.IsMapPaused)).SetValue(player, true);
        var attack = f.New<MonsterDefaultAttackSO>();
        attack.animName = "Attack";
        attack.hitStartTime = .32f;
        attack.hitDuration = .18f;
        attack.attackDuration = 1.3f;
        attack.telegraphDuration = .32f;
        attack.knockbackForce = 0f;

        var enemy = f.Actor<Enemy>("Three-hit counter", f.SkillSet(attack), out var sensor);
        await Until(() => enemy.Brain != null, "Counter actor never initialized.");
        sensor.Target = player.transform;
        enemy.HurtDuration = 1f;
        DamageResult Hit(bool direct, float poise = 0f, Vector3 push = default)
            => DamageManager.Apply(new HitInfo(1f, ElementType.ELEMENT_NONE, 0f, 1f, trueDamage: true,
                source: player, poiseDamage: poise, power: push, isDirectPlayerHit: direct), enemy, enemy.gameObject);
        enemy.SetEncounterPatrolOnly(true);
        Check(!Hit(true).Applied, "Pre-entry patrol accepted a direct hit.");
        enemy.SetEncounterPatrolOnly(false);
        Check(Hit(true).CausedOrdinaryStagger && Hit(true).CausedOrdinaryStagger, "Opening two hits did not stagger.");
        // Secondary damage still has the same player source and still causes ordinary hitstun.
        Check(Hit(false).CausedOrdinaryStagger, "Secondary damage unexpectedly lost ordinary stagger.");
        await Frames(3);
        Check(enemy.IsHurt && enemy.CurrentAction == null, "Two direct hits and a secondary hit triggered a counter.");
        enemy.StartAttackCooldown();
        Hit(true, push: Vector3.right);
        Check(enemy.CurrentAction == null, "Counter entered from inside the damage callback.");
        await Frames(1);
        if (Time.deltaTime < enemy.EnemyStat.GetStat(EnemyStatType.AttackCooldown))
            Check(enemy.IsHurt && enemy.CurrentAction == null, "Counter bypassed the common attack cooldown.");
        await Until(() => enemy.CurrentAction?.HasNormalAttackArmor == true, "Third direct stagger never started a counter.");
        var counter = enemy.CurrentAction;
        Check(ParryEventBus.IsTelegraphActive(counter) && !enemy.IsKnockbacking,
            "Counter skipped its telegraph or retained the interrupted knockback.");
        float hp = enemy.EnemyStat.CurrentHp;
        float playerHp = player.StatManager.CurrentHp;
        Check(!Hit(true, 3f, Vector3.right * 5f).CausedOrdinaryStagger && enemy.EnemyStat.CurrentHp < hp
            && !enemy.IsKnockbacking && !enemy.IsHurt && enemy.GetComponent<PoiseHandler>().PoiseRatio < 1f,
            "Armored hit failed to preserve HP/poise damage or started hurt/knockback.");
        await Until(() => player.StatManager.CurrentHp < playerHp, "The interrupted combo never resulted in a real player hit.");
        await Until(() => !counter.HasNormalAttackArmor, "Single-hit armor continued into recovery.");
        Check(counter.IsActive && Hit(true).CausedOrdinaryStagger, "Recovery was not punishable.");
        await Frames(2);
        Check(!counter.IsActive && !ParryEventBus.IsTelegraphActive(counter), "Recovery hit retained the old attack.");
        enemy.gameObject.SetActive(false);

        var combo = f.New<MonsterComboAttackSO>();
        combo.animName = "Attack";
        combo.knockbackForce = 0f;
        await CheckArmorWindow(f, player, combo, .45f);
        var dash = f.New<MonsterDashAttackSO>();
        dash.animName = "Attack";
        dash.dashSpeed = 0f;
        await CheckArmorWindow(f, player, dash, dash.startupTime + .1f);
        var burst = f.New<MonsterTripleSphereBurstSO>();
        burst.startupTime = .2f;
        burst.shotInterval = .25f;
        burst.projectileCount = 3;
        burst.projectileLifetime = 2f;
        burst.projectileSpeed = .2f;
        await CheckArmorWindow(f, player, burst, .45f);

        // A queued counter can wait for range/cooldown, but cannot survive a finished hurt chain.
        enemy = f.Actor<Enemy>("Counter gates", f.SkillSet(attack), out sensor);
        await Until(() => enemy.Brain != null, "Counter gate actor never initialized.");
        enemy.HurtDuration = .3f;
        sensor.Target = null;
        Hit(true); Hit(true); Hit(true);
        await Frames(3);
        Check(enemy.CurrentAction == null && enemy.IsHurt, "Memory-only counter ignored line of sight.");
        player.transform.position = f.Origin + Vector3.forward * 3.5f;
        sensor.Target = player.transform;
        await Frames(2);
        Check(enemy.CurrentAction == null, "Counter selected a move outside its execute range.");
        attack.minimumRange = 1.5f;
        player.transform.position = f.Origin + Vector3.forward * .8f;
        await Frames(2);
        Check(enemy.CurrentAction == null, "Counter ignored a ranged attack's minimum range.");
        attack.minimumRange = 0f;
        attack.cooldown = 10f;
        enemy.RecordAttackStarted(attack);
        player.transform.position = f.Origin + Vector3.forward;
        await Frames(2);
        Check(enemy.CurrentAction == null, "Counter ignored its individual skill cooldown.");
        await Until(() => !enemy.IsHurt, "Hurt chain never ended.");
        var replacement = f.New<MonsterDefaultAttackSO>();
        replacement.animName = "Attack";
        replacement.hitStartTime = .7f;
        enemy.EnemyStat.MonsterSkillData.skillData[0] = replacement;
        Hit(true);
        await Frames(3);
        Check(enemy.IsHurt && enemy.CurrentAction == null, "Counter credit leaked between separate hurt chains.");
        Hit(true); Hit(true, poise: 200f);
        await Until(() => enemy.IsGroggy, "A third hit with a poise break did not take priority over counter.");
        Check(enemy.CurrentAction == null, "A counter started during groggy.");
        enemy.gameObject.SetActive(false);

        enemy = f.Actor<Enemy>("Counter cancellation", f.SkillSet(replacement), out sensor);
        sensor.Target = player.transform;
        await Until(() => enemy.CurrentAction?.HasNormalAttackArmor == true, "Autonomous attack never gained armor.");
        var parried = enemy.CurrentAction;
        playerHp = player.StatManager.CurrentHp;
        var earth = (EarthForm)player.FormManager.GetForm(ElementType.ELEMENT_EARTH);
        player.FormManager.ChangeForm(earth);
        player.StateMachine.ChangeState(new PlayerAttackState(player));
        Set(earth, "_timer", .2f);
        player.transform.forward = Vector3.back;
        await Until(() => parried.WasParried && earth.GuardCounterReady,
            "The player's frontal Earth just-guard did not parry the incoming attack.");
        Check(!parried.HasNormalAttackArmor && !parried.IsActive && !ParryEventBus.IsTelegraphActive(parried),
            "Parry left an active normal attack or telegraph.");
        replacement.isParryable = false;
        replacement.hitDuration = .4f;
        replacement.attackDuration = 1.5f;
        await Until(() => enemy.CurrentAction?.HasNormalAttackArmor == true, "Parried attack stranded the normal FSM.");
        Check(player.StatManager.CurrentHp == playerHp, "A parried attack delivered a later hit.");
        var broken = enemy.CurrentAction;
        await Until(() => broken.AttackTimeline >= replacement.hitStartTime, "Unparryable follow-up never reached its hit window.");
        Check(!broken.WasParried && broken.HasNormalAttackArmor && player.StatManager.CurrentHp == playerHp,
            "Earth guard must block damage without cancelling an unparryable normal attack.");
        player.StateMachine.ChangeState(new PlayerIdleState(player));
        Hit(true, 200f);
        Check(!broken.IsActive && !broken.HasNormalAttackArmor, "Poise break left an attack active during damage callbacks.");
        await Until(() => enemy.IsGroggy, "Armored poise break did not enter groggy.");
        enemy.gameObject.SetActive(false);
        enemy = f.Actor<Enemy>("Lethal counter cancellation", f.SkillSet(replacement), out sensor);
        await Until(() => enemy.Brain != null, "Final counter fixture never initialized.");
        enemy.HurtDuration = 0f;
        sensor.Target = player.transform;
        Hit(true); Hit(true); Hit(true);
        int detectedBefore = sensor.Detections;
        await Until(() => sensor.Detections > detectedBefore, "No actionable tick after simultaneous contacts.");
        Check(enemy.CurrentAction?.HasNormalAttackArmor == true && !enemy.IsHurt,
            "Expiring hurt discarded the eligible third hit on the first actionable tick.");
        var lethal = enemy.CurrentAction;
        enemy.TakeDamage(enemy.EnemyStat.CurrentHp + 1f, poiseDamage: 0f);
        Check(enemy.IsDead && !lethal.IsActive && !lethal.HasNormalAttackArmor && !ParryEventBus.IsTelegraphActive(lethal),
            "A lethal hit left attack armor or a telegraph active.");
        body.SetActive(false);
    }

    private static async Task CheckArmorWindow(RuntimeFixture f, PlayerController player, MonsterSkillData skill, float activeTime)
    {
        var enemy = f.Actor<Enemy>("Armor window " + skill.GetType().Name, f.SkillSet(skill), out var sensor);
        sensor.Target = player.transform;
        await Until(() => enemy.CurrentAction?.HasNormalAttackArmor == true, "No startup armor on " + skill.GetType().Name);
        var action = enemy.CurrentAction;
        // Dash uses a phase-local timer, so observe the advancing actor clock here.
        float started = enemy.ActionTime;
        await Until(() => enemy.ActionTime >= started + activeTime, "Attack timeline did not advance.");
        Check(action.HasNormalAttackArmor && enemy.GetComponent<Collider>().enabled, "Premature armor end or disabled hurtbox on " + skill.GetType().Name);
        await Until(() => !action.HasNormalAttackArmor, "No recovery boundary on " + skill.GetType().Name);
        Check(action.IsActive && !action.IsFinished && enemy.TakeDamage(1f, poiseDamage: 0f).CausedOrdinaryStagger,
            "Attack did not expose a punishable recovery/flight interval on " + skill.GetType().Name);
        enemy.gameObject.SetActive(false);
    }
}
