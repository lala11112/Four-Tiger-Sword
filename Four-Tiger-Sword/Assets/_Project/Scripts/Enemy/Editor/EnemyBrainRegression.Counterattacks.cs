using System.Reflection;
using System.Threading.Tasks;
using Baekho.Generated.a3355a1c8240244a995e4f671b0dc48bb;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class EnemyBrainRegression
{
    private static float AttackCooldownRemaining(Enemy enemy)
        => (float)typeof(Enemy).GetField("_attackCooldownTimer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(enemy);

    private static MonsterSkillData PendingAttack(Enemy enemy)
        => (MonsterSkillData)typeof(NormalEnemyBrain).GetProperty("PendingAttackSkill", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(enemy.Brain);

    private static bool IsHitAnimationPresent(Enemy enemy)
    {
        var animator = enemy.Animator;
        int hit = Animator.StringToHash("Hit");
        return animator.GetCurrentAnimatorStateInfo(0).shortNameHash == hit
            || animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == hit;
    }

    private static async Task ExerciseScheduledAttacks(RuntimeFixture f)
    {
        if (CombatSettings.Active == null)
        {
            var host = new GameObject("Scheduled attack element table");
            host.SetActive(false);
            SceneManager.MoveGameObjectToScene(host, f.Scene);
            var settings = host.AddComponent<CombatSettings>();
            Set(settings, "_elementTable", AssetDatabase.LoadAssetAtPath<ElementEffectiveManager>(
                "Assets/_Project/CombatData/ElementEffectiveManager.asset"));
            host.SetActive(true);
        }

        var body = new GameObject("Scheduled attack player");
        body.SetActive(false);
        SceneManager.MoveGameObjectToScene(body, f.Scene);
        body.layer = LayerMask.NameToLayer("Player");
        body.transform.position = f.Origin + Vector3.forward;
        body.AddComponent<Animator>().runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            "Assets/_Project/Animations/AnimController/BDJ.controller");
        // This stationary damage fixture has no weapon socket receiver; animation events are not under test.
        body.GetComponent<Animator>().fireEvents = false;
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
        attack.superArmor = true;
        attack.animName = "Attack";
        attack.hitStartTime = .32f;
        attack.hitDuration = .18f;
        attack.attackDuration = 1.3f;
        attack.telegraphDuration = .32f;
        attack.knockbackForce = 0f;

        var enemy = f.Actor<Enemy>("Scheduled armored attack", f.SkillSet(attack), out var sensor);
        await Until(() => enemy.Brain != null, "Scheduled actor never initialized.");
        sensor.Target = player.transform;
        enemy.HurtDuration = .05f;
        DamageResult Hit(bool direct, float poise = 0f, Vector3 push = default)
            => DamageManager.Apply(new HitInfo(1f, ElementType.ELEMENT_NONE, 0f, 1f, trueDamage: true,
                source: player, poiseDamage: poise, power: push, isDirectPlayerHit: direct), enemy, enemy.gameObject);
        enemy.SetEncounterPatrolOnly(true);
        Check(!Hit(true).Applied, "Pre-entry patrol accepted a direct hit.");
        enemy.SetEncounterPatrolOnly(false);
        Set(enemy, "_attackCooldownTimer", 10f);
        Check(Hit(true).CausedOrdinaryStagger && Hit(true).CausedOrdinaryStagger, "Opening two hits did not stagger.");
        await Until(() => enemy.stateName == nameof(EnemyHurtState), "Direct hits never entered Hurt.");
        Check(Hit(true).CausedOrdinaryStagger && Hit(false).CausedOrdinaryStagger,
            "Repeated hits granted stagger armor before an attack was active.");
        Check(enemy.CurrentAction == null && PendingAttack(enemy) == null && AttackCooldownRemaining(enemy) > 5f,
            "Repeated hits must neither start an attack nor restart the selection clock.");
        Set(enemy, "_attackCooldownTimer", .12f);
        Check(Hit(true, push: Vector3.right * .2f).CausedOrdinaryStagger && enemy.CurrentAction == null,
            "Taking a hit must not execute the scheduled attack.");
        await Until(() => PendingAttack(enemy) == attack, "The selection interval stopped during Hurt.");
        Check(enemy.IsHurt && IsHitAnimationPresent(enemy) && enemy.CurrentAction == null
            && AttackCooldownRemaining(enemy) > 0f && enemy.IsSkillReady(attack),
            "A due armored attack did not stay pending while the Hit animation was playing.");
        await Until(() =>
        {
            if (enemy.IsHurt && enemy.CurrentAction != null)
                throw new System.InvalidOperationException("An armored action was prepared or started during Hurt.");
            return !enemy.IsHurt;
        }, "The enemy did not complete its Hit animation.");
        await Until(() => enemy.CurrentAction?.HasNormalAttackArmor == true,
            "The pending armored attack did not start after Hurt.");
        var armored = enemy.CurrentAction;
        Check(ParryEventBus.IsTelegraphActive(armored) && !enemy.IsKnockbacking && !enemy.IsHurt
            && !IsHitAnimationPresent(enemy), "The armored attack skipped its telegraph or overlapped the Hit animation.");
        float hp = enemy.EnemyStat.CurrentHp;
        float playerHp = player.StatManager.CurrentHp;
        Check(!Hit(true, 3f, Vector3.right * 5f).CausedOrdinaryStagger && enemy.EnemyStat.CurrentHp < hp
            && !enemy.IsKnockbacking && !enemy.IsHurt && enemy.GetComponent<PoiseHandler>().PoiseRatio < 1f,
            "Armored hit failed to preserve HP/poise damage or started hurt/knockback.");
        await Until(() => player.StatManager.CurrentHp < playerHp, "The armored attack never resulted in a real player hit.");
        Hit(false);
        var recoil = enemy.GetComponent<EnemyArmoredHitReaction>();
        Check(recoil != null && recoil.IsReacting, "An armored active-window hit did not start the visual recoil.");
        await Until(() => !armored.HasNormalAttackArmor, "Single-hit armor continued into recovery.");
        Check(!recoil.IsReacting, "Natural recovery retained a super-armor-only hit motion.");
        Check(armored.IsActive && Hit(true).CausedOrdinaryStagger && !recoil.IsReacting,
            "Recovery was not punishable or restarted an armored hit motion.");
        await Frames(2);
        Check(!armored.IsActive && !ParryEventBus.IsTelegraphActive(armored), "Recovery hit retained the old attack.");
        enemy.gameObject.SetActive(false);

        await ExerciseNonArmoredPendingAttack(f);

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

        enemy = f.Actor<Enemy>("Scheduled attack gates", f.SkillSet(attack), out sensor);
        await Until(() => enemy.Brain != null, "Selection gate actor never initialized.");
        enemy.HurtDuration = 2f;
        sensor.Target = null;
        Hit(true);
        Set(enemy, "_attackCooldownTimer", 0f);
        await Until(() => enemy.stateName == nameof(EnemyHurtState), "Selection gate fixture did not enter Hurt.");
        Check(enemy.CurrentAction == null && PendingAttack(enemy) == null, "Memory alone authorized an attack from Hurt.");
        player.transform.position = f.Origin + Vector3.forward * 3.5f;
        sensor.Target = player.transform;
        await Until(() => AttackCooldownRemaining(enemy) > 0f, "An out-of-range selection did not consume its interval.");
        Check(enemy.CurrentAction == null && PendingAttack(enemy) == attack,
            "An armored skill in selection range and outside execute range did not stay pending.");
        attack.minimumRange = 1.5f;
        player.transform.position = f.Origin + Vector3.forward * .8f;
        Set(enemy, "_attackCooldownTimer", 0f);
        await Until(() => AttackCooldownRemaining(enemy) > 0f, "A too-close selection did not consume its interval.");
        Check(enemy.CurrentAction == null && PendingAttack(enemy) == null,
            "The skill selection ignored its minimum range.");
        attack.minimumRange = 0f;
        attack.cooldown = 10f;
        enemy.RecordAttackStarted(attack);
        player.transform.position = f.Origin + Vector3.forward;
        Set(enemy, "_attackCooldownTimer", 0f);
        await Until(() => AttackCooldownRemaining(enemy) > 0f, "A selection with no ready skill did not consume its interval.");
        Check(enemy.CurrentAction == null && PendingAttack(enemy) == null,
            "The skill selection ignored its individual cooldown.");
        var replacement = f.New<MonsterDefaultAttackSO>();
        replacement.animName = "Attack";
        replacement.hitStartTime = .7f;
        replacement.superArmor = true;
        enemy.EnemyStat.MonsterSkillData.skillData[0] = replacement;
        Set(enemy, "_attackCooldownTimer", 0f);
        Hit(true, poise: 200f);
        await Until(() => enemy.IsGroggy, "A poise break did not take priority over the due selection.");
        Check(enemy.CurrentAction == null, "A scheduled attack started during groggy.");
        enemy.gameObject.SetActive(false);

        enemy = f.Actor<Enemy>("Armored attack cancellation", f.SkillSet(replacement), out sensor);
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
        enemy = f.Actor<Enemy>("Lethal armored attack cancellation", f.SkillSet(replacement), out sensor);
        await Until(() => enemy.Brain != null, "Final armored attack fixture never initialized.");
        enemy.HurtDuration = 2f;
        sensor.Target = player.transform;
        Hit(true);
        Set(enemy, "_attackCooldownTimer", 0f);
        await Until(() => enemy.CurrentAction?.HasNormalAttackArmor == true && !enemy.IsHurt,
            "The scheduled armored attack did not start after the first hit recovered.");
        var lethal = enemy.CurrentAction;
        enemy.TakeDamage(enemy.EnemyStat.CurrentHp + 1f, poiseDamage: 0f);
        Check(enemy.IsDead && !lethal.IsActive && !lethal.HasNormalAttackArmor && !ParryEventBus.IsTelegraphActive(lethal),
            "A lethal hit left attack armor or a telegraph active.");
        body.SetActive(false);
    }

    private static async Task ExerciseNonArmoredPendingAttack(RuntimeFixture f)
    {
        var original = f.New<EnemyBrainProbeSkill>();
        original.cooldown = 60f;
        var replacement = f.New<EnemyBrainProbeSkill>();
        var skills = f.SkillSet(original);
        var enemy = f.Actor<Enemy>("Pending unarmored attack", skills, out var sensor, TacticsData(f, skills, 10f));
        await Until(() => enemy.Brain != null, "Pending attack fixture did not initialize.");
        f.Target.position = enemy.transform.position + Vector3.forward * 3f;
        sensor.Target = f.Target;
        enemy.GetComponent<UnityEngine.AI.NavMeshAgent>().speed = 0f;
        Set(enemy, "_attackCooldownTimer", 0f);
        await Until(() => enemy.stateName == nameof(EnemyApproachState) && original.Actions.Count == 1,
            "The fixture never prepared an out-of-range ordinary attack.");
        enemy.HurtDuration = 1f;
        enemy.TakeDamage(1f, poiseDamage: 0f);
        await Until(() => PendingAttack(enemy) == original && enemy.stateName == nameof(EnemyHurtState),
            "Taking a hit while approaching did not return the prepared skill to the pending slot.");
        Check(enemy.IsHurt && enemy.CurrentAction == null && original.Actions[0].Entered == 0 && enemy.IsSkillReady(original),
            "Returning an interrupted approach to the pending slot executed the action or spent its cooldown.");
        skills.skillData[0] = replacement;
        await Frames(3);
        Check(PendingAttack(enemy) == original && AttackCooldownRemaining(enemy) > 5f,
            "The pending skill was rerolled before the next selection interval.");
        Check(enemy.TakeDamage(1f, poiseDamage: 0f).CausedOrdinaryStagger,
            "Waiting with an ordinary attack selected granted stagger armor.");
        f.Target.position = enemy.transform.position + Vector3.forward;
        Set(enemy, "_attackCooldownTimer", 0f);
        await Until(() => PendingAttack(enemy) == replacement, "The next interval did not replace the pending ordinary attack.");
        Check(enemy.IsHurt && enemy.CurrentAction == null && replacement.Actions.Count == 0,
            "The replacement started before Hurt ended.");
        await Until(() => enemy.CurrentAction?.IsActive == true, "The selected ordinary attack did not start after Hurt.");
        Check(enemy.CurrentAction.SkillData == replacement && !enemy.CurrentAction.HasNormalAttackArmor
            && !IsHitAnimationPresent(enemy)
            && enemy.TakeDamage(1f, poiseDamage: 0f).CausedOrdinaryStagger
            && enemy.GetComponent<EnemyArmoredHitReaction>() == null,
            "An ordinary attack started with super armor or could not be interrupted by a hit.");
        enemy.gameObject.SetActive(false);
    }

    private static async Task CheckArmorWindow(RuntimeFixture f, PlayerController player, MonsterSkillData skill, float activeTime)
    {
        skill.superArmor = true;
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
