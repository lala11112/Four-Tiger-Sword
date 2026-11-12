using System.Linq;
using System.Threading.Tasks;
using StudioBaekho;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static partial class EnemyBrainRegression
{
    private static async Task ExerciseGroupAwareness(RuntimeFixture fixture)
    {
        Vector3 origin = fixture.Origin;
        Vector3 firstSight = origin + Vector3.forward * 2f;
        fixture.Target.position = firstSight;
        var group = CreateGroup(fixture, "Alerting room");
        var otherGroup = CreateGroup(fixture, "Separate room");
        var skill = fixture.New<EnemyBrainProbeSkill>();
        skill.executeRange = 4f;
        var skills = fixture.SkillSet(skill);
        var observer = GroupActor(fixture, group, "Direct observer", origin, fixture.SkillSet(), out _);
        var listener = GroupActor(fixture, group, "Behind the wall", origin + Vector3.right * 3f, skills, out _);
        var distant = GroupActor(fixture, group, "Outside the alert radius", origin + Vector3.right * 8.5f, skills, out _);
        var other = GroupActor(fixture, otherGroup, "Nearby but in another room", origin + Vector3.left, skills, out _);
        // The listener is within attack range and must rely on its own raycast, not the shared snapshot.
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(wall, fixture.Scene);
        wall.transform.position = origin + new Vector3(1.5f, 1f, 1.5f);
        wall.transform.localScale = new Vector3(.35f, 3f, 6f);
        await Until(() => observer.Brain != null && listener.Brain != null && distant.Brain != null && other.Brain != null,
            "Alert fixtures did not initialize.");
        Check(!listener.HasTargetMemory && !distant.HasTargetMemory && !other.HasTargetMemory,
            "Registering active room members must not grant player knowledge.");
        Set(listener, "_targetMemoryDuration", 1f);
        BindRealVision(listener, fixture.Target);
        var distantVision = BindRealVision(distant, fixture.Target);
        var observerVision = BindRealVision(observer, fixture.Target);
        Physics.SyncTransforms();

        await Until(() => observer.DetectedTarget == fixture.Target && listener.HasTargetMemory,
            "A clear observation did not alert an occluded ally within eight metres.");
        float observedAt = Time.time;
        Check(listener.LastKnownTargetPosition == firstSight && !distant.HasTargetMemory && !other.HasTargetMemory,
            "An alert must transmit a snapshot within its room and radius.");
        await Until(() => listener.stateName == nameof(EnemyTraceState),
            "The occluded ally did not investigate the heard position.");
        Check(listener.DetectedTarget == null && listener.CurrentAction == null,
            "A heard position cannot authorize an attack through a wall.");

        Vector3 secondSight = firstSight + Vector3.forward * .5f;
        fixture.Target.position = secondSight;
        Physics.SyncTransforms();
        await Until(() => observer.LastKnownTargetPosition == secondSight,
            "The observer did not update its own memory after the player moved.");
        if (Time.time - observedAt < .4f)
            Check(listener.LastKnownTargetPosition == firstSight,
                "The observer repeated its broadcast before the half-second interval.");
        await Until(() => listener.LastKnownTargetPosition == secondSight,
            "Continued observation did not renew the nearby ally's snapshot after the interval.");
        Check(!distant.HasTargetMemory && !other.HasTargetMemory,
            "A recipient with no sight relayed the alert or leaked it to another room.");

        observerVision.enabled = false;
        fixture.Target.position = origin + Vector3.forward * 28f;
        Physics.SyncTransforms();
        await Frames(3);
        Check(listener.DetectedTarget == null && listener.LastKnownTargetPosition == secondSight,
            "A memory-only ally followed the live player instead of the last observation.");
        await Until(() => !listener.HasTargetMemory && listener.stateName == nameof(EnemyIdleState),
            "Once fresh observations stop, the listener must forget and return to patrol.");

        var player = GroupDamageSource(fixture, firstSight);
        Check(ApplyGroupHit(player, observer, false).Applied && !listener.HasTargetMemory,
            "Secondary player damage must not broadcast an alert.");
        Check(ApplyGroupHit(player, observer).Applied && listener.HasTargetMemory
            && listener.LastKnownTargetPosition == firstSight && listener.DetectedTarget == null,
            "A direct player hit without sight must broadcast its captured position to nearby allies.");
        Check(!distant.HasTargetMemory && !other.HasTargetMemory,
            "A damage alert must use the same room and radius as an observation.");

        distantVision.enabled = false;
        wall.SetActive(false);
        fixture.Target.position = firstSight;
        Physics.SyncTransforms();
        await Until(() => listener.DetectedTarget == fixture.Target && distant.HasTargetMemory,
            "A listener that later sees the player directly must be able to alert its own nearby allies.");
        Check(distant.LastKnownTargetPosition == firstSight && !other.HasTargetMemory,
            "A fresh local observation must still respect the room boundary.");
        foreach (var enemy in new[] { observer, listener, distant, other })
        {
            enemy.CombatLabPassive = true;
            enemy.gameObject.SetActive(false);
        }
    }

    private static async Task ExerciseGroupAttackTiming(RuntimeFixture fixture)
    {
        var group = CreateGroup(fixture, "Three attackers");
        var skill = fixture.New<EnemyBrainProbeSkill>();
        skill.executeRange = 3f;
        var skills = fixture.SkillSet(skill);
        var enemies = new Enemy[3];
        fixture.Target.position = fixture.Origin;
        for (int i = 0; i < enemies.Length; i++)
        {
            enemies[i] = GroupActor(fixture, group, "Simultaneous attacker " + i,
                fixture.Origin + new Vector3((i - 1) * 1.4f, 0f, -1.8f), skills, out _);
            enemies[i].ApproachTimeout = enemies[i].ApproachStuckTimeout = .1f;
        }
        await Until(() => enemies.All(enemy => enemy.Brain != null), "Group attack fixtures did not initialize.");
        foreach (var enemy in enemies) BindRealVision(enemy, fixture.Target);
        Physics.SyncTransforms();

        await Until(() => enemies.All(enemy => enemy.CurrentAction?.IsActive == true),
            "Three ready enemies did not overlap their attacks. Waiting in range must not count as a stalled approach.");
        var starts = skill.Actions.Where(action => action.Entered != 0).OrderBy(action => action.EnteredAt).ToArray();
        Check(starts.Length == 3 && starts.Zip(starts.Skip(1),
                (previous, next) => next.EnteredAt - previous.EnteredAt >= .199f).All(separated => separated),
            "The room must stagger ordinary attack starts by at least 0.2 seconds.");
        Check(enemies.All(enemy => enemy.stateName == nameof(EnemyAttackState)
                && ParryEventBus.IsTelegraphActive(enemy.CurrentAction)),
            "Pacing must retain every concurrently active attack's own telegraph.");
        foreach (var enemy in enemies) enemy.CombatLabPassive = true;
    }

    private static async Task ExerciseGroupAttackQueue(RuntimeFixture fixture)
    {
        var group = CreateGroup(fixture, "Attack queue", new NormalEnemyAISettings { attackStartInterval = 1f });
        var skill = fixture.New<EnemyBrainProbeSkill>();
        skill.executeRange = 4f;
        var skills = fixture.SkillSet(skill);
        fixture.Target.position = fixture.Origin + Vector3.right * 10f;
        var names = new[] { "Opening attacker", "Oldest with lost sight", "Oldest valid",
            "Hurt waiter", "Dead waiter", "Disabled waiter", "Armored waiter", "Newest valid" };
        var fighters = new Enemy[names.Length];
        var sensors = new EnemyBrainSensorProbe[names.Length];
        for (int i = 0; i < fighters.Length; i++)
        {
            Vector3 offset = Quaternion.Euler(0f, i * 360f / fighters.Length, 0f) * Vector3.back * 2.8f;
            fighters[i] = GroupActor(fixture, group, names[i], fixture.Target.position + offset, skills, out sensors[i]);
        }
        await Until(() => fighters.All(enemy => enemy.Brain != null), "Attack queue fixtures did not initialize.");
        sensors[0].Target = fixture.Target;
        await Until(() => fighters[0].CurrentAction?.IsActive == true, "The opening attack did not enter.");
        for (int i = 1; i <= 2; i++)
        {
            int queued = i;
            sensors[i].Target = fixture.Target;
            await Until(() => group.PendingAttackCount == queued, "The next eligible enemy did not join the queue.");
        }
        for (int i = 3; i <= 6; i++) sensors[i].Target = fixture.Target;
        await Until(() => group.PendingAttackCount == 6, "Attack requests did not remain pending during the interval.");
        sensors[7].Target = fixture.Target;
        await Until(() => group.PendingAttackCount == 7, "The newest eligible enemy did not join the queue.");

        sensors[1].Target = null;
        fighters[3].HurtDuration = 4f;
        Check(fighters[3].TakeDamage(1f, poiseDamage: 0f).CausedOrdinaryStagger,
            "The queued hit-reaction fixture did not stagger.");
        Check(fighters[4].TakeDamage(1000f, poiseDamage: 0f).Killed, "The queued lethal fixture did not die.");
        fighters[5].enabled = false;
        Check(group.PendingAttackCount == 3,
            "Losing sight, taking a stagger, dying, and disabling must each cancel the pending request.");
        fighters[6].StaggerResistLevel = StaggerResistLevel.SUPER_ARMOR;
        var armoredHit = fighters[6].TakeDamage(1f, poiseDamage: 0f);
        Check(armoredHit.HealthDamage > 0f && !armoredHit.CausedOrdinaryStagger && group.PendingAttackCount == 2,
            "Taking damage without a stagger must also cancel the pending request.");
        await Until(() => group.PendingAttackCount == 3, "An armored enemy could not request a new attack after damage.");

        await Until(() => fighters[2].CurrentAction?.IsActive == true && fighters[7].CurrentAction?.IsActive == true
                && fighters[6].CurrentAction?.IsActive == true,
            "Cancelled requests prevented eligible members from starting.");
        var oldest = (EnemyBrainProbeAction)fighters[2].CurrentAction;
        var newest = (EnemyBrainProbeAction)fighters[7].CurrentAction;
        var requeued = (EnemyBrainProbeAction)fighters[6].CurrentAction;
        Check(newest.EnteredAt - oldest.EnteredAt >= .999f && requeued.EnteredAt - newest.EnteredAt >= .999f
                && fighters[0].CurrentAction?.IsActive == true,
            "The oldest eligible request must start first without waiting for active attacks to finish.");
        Check(skill.Actions.Count(action => action.Entered != 0) == 4,
            "An invalidated attack request entered its action.");
        foreach (var fighter in fighters) fighter.CombatLabPassive = true;
    }

    private static async Task ExerciseGroupCounterTiming(RuntimeFixture fixture)
    {
        var group = CreateGroup(fixture, "Immediate counter",
            new NormalEnemyAISettings { attackStartInterval = .65f });
        var skill = fixture.New<EnemyBrainProbeSkill>();
        skill.executeRange = 4f;
        var skills = fixture.SkillSet(skill);
        fixture.Target.position = fixture.Origin + Vector3.right * 20f;
        var opener = GroupActor(fixture, group, "Opening attack", fixture.Target.position + Vector3.back * 2f, skills, out var openingSight);
        var counter = GroupActor(fixture, group, "Three-hit counter", fixture.Target.position + Vector3.right * 2f, skills, out var counterSight);
        var follower = GroupActor(fixture, group, "Following attack", fixture.Target.position + Vector3.left * 2f, skills, out var followingSight);
        var player = GroupDamageSource(fixture, fixture.Target.position);
        await Until(() => opener.Brain != null && counter.Brain != null && follower.Brain != null,
            "Group counter fixtures did not initialize.");
        openingSight.Target = fixture.Target;
        await Until(() => opener.CurrentAction?.IsActive == true, "The counter fixture's opening attack did not enter.");
        counterSight.Target = fixture.Target;
        followingSight.Target = fixture.Target;
        Check(ApplyGroupHit(player, counter).CausedOrdinaryStagger
            && ApplyGroupHit(player, counter).CausedOrdinaryStagger
            && ApplyGroupHit(player, counter).CausedOrdinaryStagger,
            "The counter fixture did not receive three direct staggers.");
        await Until(() => counter.CurrentAction?.IsActive == true,
            "The third direct stagger did not start the counter.");
        var opened = (EnemyBrainProbeAction)opener.CurrentAction;
        var countered = (EnemyBrainProbeAction)counter.CurrentAction;
        Check(countered.EnteredAt - opened.EnteredAt < group.Settings.attackStartInterval,
            "The immediate counter had to wait for the ordinary attack interval.");
        await Until(() => follower.CurrentAction?.IsActive == true, "The follower did not attack after the counter.");
        Check(((EnemyBrainProbeAction)follower.CurrentAction).EnteredAt - countered.EnteredAt >= .649f,
            "A counter did not restart the room interval for the next ordinary attack.");
        opener.CombatLabPassive = counter.CombatLabPassive = follower.CombatLabPassive = true;
    }

    private static async Task ExerciseGroupPositioning(RuntimeFixture fixture)
    {
        var skill = fixture.New<EnemyBrainProbeSkill>();
        skill.preferredRange = 2f;
        skill.executeRange = 2.5f;
        skill.engageRange = 9f;
        var data = fixture.New<EnemyData>();
        data.maxHp = 100;
        data.combatRange = 12f;
        data.combatExitRange = 18f;
        data.attackCooldown = 30f;
        data.monsterSkillData = fixture.SkillSet(skill);
        var group = CreateGroup(fixture, "Open positioning room");
        Vector3 target = fixture.Origin + Vector3.left * 15f;
        fixture.Target.position = target;
        var fighters = new Enemy[3];
        for (int i = 0; i < fighters.Length; i++)
        {
            fighters[i] = GroupActor(fixture, group, "Spreading fighter " + i,
                target + new Vector3((i - 1) * .12f, 0f, -4.5f - 1.5f * i),
                data.monsterSkillData, out var sight, data, 5f);
            fighters[i].GetComponent<NavMeshAgent>().radius = .4f;
            fighters[i].StartAttackCooldown();
            sight.Target = fixture.Target;
        }
        await Until(() => fighters.All(enemy => enemy.stateName == nameof(EnemyCombatIdleState)
                && !enemy.GetComponent<NavMeshAgent>().hasPath
                && Mathf.Abs(GroundDistance(enemy.transform.position, target) - 2f) <= .5f),
            "Melee enemies converging from one side did not reach distinct positions in range.");
        for (int i = 0; i < fighters.Length; i++)
        {
            Check(fighters[i].transform.position.z < target.z, "A same-side approach crossed behind the player.");
            for (int j = i + 1; j < fighters.Length; j++)
                Check(GroundDistance(fighters[i].transform.position, fighters[j].transform.position) >= 1.04f,
                    "Melee group positions did not leave enough space between agents.");
        }
        Vector3[] settled = fighters.Select(enemy => enemy.transform.position).ToArray();
        float holdUntil = Time.time + 1.1f;
        await Until(() => Time.time >= holdUntil, "The positioning stability window did not elapse.");
        Check(fighters.Select((enemy, i) => GroundDistance(enemy.transform.position, settled[i]) < .15f)
                .All(stable => stable),
            "In-range enemies repeatedly swapped positions during cooldown.");
        foreach (var fighter in fighters) fighter.CombatLabPassive = true;

        target = fixture.Origin + Vector3.left * 25f;
        fixture.Target.position = target;
        var lane = CreateGroup(fixture, "Narrow positioning room");
        lane.transform.position = target;
        var room = lane.gameObject.AddComponent<BaekhoMapModule>();
        room.localBounds = new Bounds(Vector3.up * 2f, new Vector3(1.3f, 6f, 16f));
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(floor, fixture.Scene);
        floor.transform.SetParent(room.transform, false);
        floor.transform.localPosition = Vector3.down * .25f;
        floor.transform.localScale = new Vector3(1.3f, .5f, 16f);
        room.groundObjects = new[] { floor };
        Physics.SyncTransforms();
        var lead = GroupActor(fixture, lane, "Lane lead", target + Vector3.back * 2f, data.monsterSkillData,
            out var leadSight, data);
        var rear = GroupActor(fixture, lane, "Lane rear", target + Vector3.back * 5f, data.monsterSkillData,
            out var rearSight, data, 4f);
        foreach (var enemy in new[] { lead, rear })
        {
            enemy.StartAttackCooldown();
            enemy.GetComponent<NavMeshAgent>().obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        }
        leadSight.Target = rearSight.Target = fixture.Target;
        await Until(() => rear.stateName == nameof(EnemyCombatIdleState), "The narrow room did not engage.");
        float laneUntil = Time.time + 1.1f;
        await Until(() =>
        {
            if (Mathf.Abs(rear.transform.position.x - target.x) > .66f || rear.transform.position.z > target.z)
                throw new System.InvalidOperationException("A blocked approach left its own narrow room floor.");
            return Time.time >= laneUntil;
        }, "The narrow-room positioning window did not elapse.");
        lead.CombatLabPassive = rear.CombatLabPassive = true;

        await ExerciseSixEnemyPositioning(fixture, data);
    }

    private static async Task ExerciseSixEnemyPositioning(RuntimeFixture fixture, EnemyData data)
    {
        Vector3 target = fixture.Origin + Vector3.forward * 18f;
        fixture.Target.position = target;
        var group = CreateGroup(fixture, "Six-enemy positioning room");
        group.transform.position = target;
        var room = group.gameObject.AddComponent<BaekhoMapModule>();
        room.localBounds = new Bounds(Vector3.up * 2f, new Vector3(16f, 6f, 16f));
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(floor, fixture.Scene);
        floor.transform.SetParent(room.transform, false);
        floor.transform.localPosition = Vector3.down * .25f;
        floor.transform.localScale = new Vector3(16f, .5f, 16f);
        room.groundObjects = new[] { floor };
        Physics.SyncTransforms();

        var fighters = new Enemy[6];
        var directions = new Vector3[fighters.Length];
        for (int i = 0; i < fighters.Length; i++)
        {
            int pair = i / 2;
            int partner = i % 2;
            directions[i] = Quaternion.Euler(0f, pair * 120f + partner * 12f, 0f) * Vector3.back;
            fighters[i] = GroupActor(fixture, group, "Six-enemy fighter " + i,
                target + directions[i] * (4.5f + 1.5f * partner), data.monsterSkillData, out var sight, data, 5f);
            fighters[i].GetComponent<NavMeshAgent>().radius = .4f;
            fighters[i].StartAttackCooldown();
            sight.Target = fixture.Target;
        }

        await Until(() =>
        {
            CheckOwnSideAndFloor();
            bool settled = fighters.All(enemy => enemy.stateName == nameof(EnemyCombatIdleState)
                && !enemy.GetComponent<NavMeshAgent>().hasPath);
            // Each pair must put pressure on its own side. The second fighter may hold an open fallback.
            return settled && Enumerable.Range(0, 3).All(pair => InRange(fighters[pair * 2]) || InRange(fighters[pair * 2 + 1]));
        }, "Six enemies did not settle into valid pressure positions on all three approach sides.");
        for (int i = 0; i < fighters.Length; i++)
            for (int j = i + 1; j < fighters.Length; j++)
                Check(GroundDistance(fighters[i].transform.position, fighters[j].transform.position) >= 1.04f,
                    "Six-enemy positioning settled two agents too close together.");

        Vector3[] positions = fighters.Select(enemy => enemy.transform.position).ToArray();
        float holdUntil = Time.time + 1.1f;
        await Until(() =>
        {
            CheckOwnSideAndFloor();
            for (int i = 0; i < fighters.Length; i++)
                if (GroundDistance(fighters[i].transform.position, positions[i]) >= .15f)
                    throw new System.InvalidOperationException("Six-enemy positioning did not hold a stable destination.");
            return Time.time >= holdUntil;
        }, "The six-enemy stability window did not elapse.");
        foreach (var fighter in fighters) fighter.CombatLabPassive = true;

        bool InRange(Enemy enemy) => Mathf.Abs(GroundDistance(enemy.transform.position, target) - 2f) <= .5f;
        void CheckOwnSideAndFloor()
        {
            for (int i = 0; i < fighters.Length; i++)
            {
                Vector3 position = fighters[i].transform.position;
                if (!room.localBounds.Contains(room.transform.InverseTransformPoint(position))
                    || Vector3.Dot(position - target, directions[i]) <= 0f)
                    throw new System.InvalidOperationException("A six-enemy approach left its room floor or crossed the player.");
            }
        }
    }

    private static async Task ExerciseAuthoredMixedGroup(RuntimeFixture fixture)
    {
        var group = CreateGroup(fixture, "Authored melee and ranged enemies");
        Vector3 target = fixture.Origin + new Vector3(20f, 0f, -20f);
        fixture.Target.position = target;
        Enemy Clone(string path, Vector3 offset)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Check(prefab != null, "Mixed combat prefab is missing: " + path);
            var actor = Object.Instantiate(prefab, target + offset, Quaternion.LookRotation(-offset));
            SceneManager.MoveGameObjectToScene(actor, fixture.Scene);
            actor.transform.SetParent(group.transform, true);
            var enemy = actor.GetComponent<Enemy>();
            enemy.CombatLabPassive = true;
            group.Register(enemy);
            return enemy;
        }
        var melee = Clone("Assets/_Project/Prefabs/Drone.prefab", Vector3.back * 4f);
        var ranged = Clone("Assets/_Project/Generated/Enemies/Hover_new_V2/Hover_new_V2.prefab", Vector3.right * 10f);
        await Until(() => melee.Brain != null && ranged.Brain != null && !melee.IsSpawning && !ranged.IsSpawning,
            "The mixed authored group did not finish spawning.");
        foreach (var enemy in new[] { melee, ranged })
        {
            var sight = enemy.GetComponent<FanVisionSensor>();
            Check(sight != null, "The authored mixed group requires the original vision sensors.");
            Set(sight, "_player", fixture.Target);
            Set(enemy, "_attackCooldownTimer", 30f);
            enemy.CombatLabPassive = false;
        }
        Physics.SyncTransforms();
        await Until(() => Mathf.Abs(GroundDistance(melee.transform.position, target) - 2f) <= .5f
                && Mathf.Abs(GroundDistance(ranged.transform.position, target) - 8f) <= .5f
                && !melee.GetComponent<NavMeshAgent>().hasPath && !ranged.GetComponent<NavMeshAgent>().hasPath,
            "Authored melee and ranged enemies did not hold their respective preferred distances in one group.");

        Set(melee, "_attackCooldownTimer", 0f);
        Set(ranged, "_attackCooldownTimer", 0f);
        float meleeStarted = float.NegativeInfinity, rangedStarted = float.NegativeInfinity;
        await Until(() =>
        {
            if (float.IsNegativeInfinity(meleeStarted) && melee.CurrentAction?.IsActive == true) meleeStarted = Time.time;
            if (float.IsNegativeInfinity(rangedStarted) && ranged.CurrentAction?.IsActive == true) rangedStarted = Time.time;
            return melee.CurrentAction?.IsActive == true && ranged.CurrentAction?.IsActive == true;
        }, "Authored melee and ranged attacks did not overlap.");
        Check(Mathf.Abs(meleeStarted - rangedStarted) >= .199f,
            "The authored mixed attacks started without the room interval.");
        Check(ParryEventBus.IsTelegraphActive(melee.CurrentAction) || ParryEventBus.IsTelegraphActive(ranged.CurrentAction),
            "The mixed group did not retain either authored attack's warning.");
        melee.CombatLabPassive = ranged.CombatLabPassive = true;
    }

    private static EnemyCombatGroup CreateGroup(RuntimeFixture fixture, string name, NormalEnemyAISettings settings = null)
    {
        var root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, fixture.Scene);
        var group = root.AddComponent<EnemyCombatGroup>();
        group.Initialize(settings ?? new NormalEnemyAISettings());
        return group;
    }

    private static Enemy GroupActor(RuntimeFixture fixture, EnemyCombatGroup group, string name,
        Vector3 position, MonsterSkillDataSO skills, out EnemyBrainSensorProbe sensor, EnemyData data = null, float speed = 0f)
    {
        var enemy = fixture.Actor<Enemy>(name, skills, out sensor, data, speed);
        enemy.transform.SetParent(group.transform, true);
        Check(enemy.GetComponent<NavMeshAgent>().Warp(position), "Could not place group fixture: " + name);
        Vector3 toTarget = Vector3.ProjectOnPlane(fixture.Target.position - position, Vector3.up);
        if (toTarget.sqrMagnitude > .001f) enemy.transform.rotation = Quaternion.LookRotation(toTarget);
        group.Register(enemy);
        return enemy;
    }

    private static PlayerController GroupDamageSource(RuntimeFixture fixture, Vector3 position)
    {
        // Supply the actual player attack identity to DamageManager without running a second player's controller.
        var source = new GameObject("Group regression damage source");
        source.SetActive(false);
        SceneManager.MoveGameObjectToScene(source, fixture.Scene);
        source.transform.position = position;
        return source.AddComponent<PlayerController>();
    }

    private static DamageResult ApplyGroupHit(PlayerController player, Enemy enemy, bool direct = true)
        => DamageManager.Apply(new HitInfo(1f, ElementType.ELEMENT_NONE, 0f, 1f, trueDamage: true,
            source: player, isDirectPlayerHit: direct,
            category: direct ? PlayerAttackCategory.Normal : PlayerAttackCategory.Secondary), enemy, enemy.gameObject);

    private static float GroundDistance(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;
}
