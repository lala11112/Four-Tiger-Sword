using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Observes the actual Animator and actions of the dungeon enemy prefabs.</summary>
public static class EnemyHurtRecoveryRegression
{
    private static readonly int Hit = Animator.StringToHash("Hit");
    private static readonly int Move = Animator.StringToHash("Move");
    private static readonly int Idle = Animator.StringToHash("Idle");
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string CatalogPath = "Assets/_Game/ProceduralDungeon/DungeonEnemyCatalog.asset";
    private static Report _report = new Report { status = "idle" };

    [Serializable]
    private sealed class Report
    {
        public string status, currentCase, error;
        public int checks;
        public List<string> results = new();
    }

    public static string StartPlayMode()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling || _report.status == "running")
            throw new InvalidOperationException("Start enemy hurt recovery verification in idle Play Mode.");
        _report = new Report { status = "running" };
        _ = Run();
        return Status();
    }

    public static string Status() => JsonUtility.ToJson(_report, true);

    private static async Task Run()
    {
        bool background = Application.runInBackground;
        float captureDelta = Time.captureDeltaTime;
        var random = UnityEngine.Random.state;
        Fixture fixture = null;
        string outcome = "passed";
        try
        {
            Application.runInBackground = true;
            // Keep the short outgoing transition observable even on a slow Editor frame.
            Time.captureDeltaTime = 1f / 60f;
            fixture = new Fixture();
            var catalog = AssetDatabase.LoadAssetAtPath<DungeonEnemyCatalog>(CatalogPath);
            Check(catalog != null && catalog.enemies.Length >= 6, "The dungeon enemy catalog is incomplete.");
            foreach (var entry in catalog.enemies)
            {
                _report.currentCase = entry.prefab.name;
                var enemy = await fixture.Spawn(entry.prefab, longKnockback: entry.prefab.name == "Test");
                await CheckAuthoredEnemy(enemy, fixture.Target);
                enemy.gameObject.SetActive(false);
                Object.Destroy(enemy.gameObject);
                await Awaitable.NextFrameAsync();
            }

            var drone = catalog.enemies.First(entry => entry.prefab.name == "Drone").prefab;
            await CheckInterruptions(fixture, drone);

            _report.currentCase = "Missing Hit controller";
            var fallback = await fixture.Spawn(catalog.enemies[0].prefab, withoutController: true);
            fallback.HurtDuration = .15f;
            await CheckFallback(fallback);
        }
        catch (Exception exception)
        {
            outcome = "failed";
            _report.error = exception.GetBaseException().Message;
            Debug.LogException(exception);
        }
        finally
        {
            try { if (fixture != null) await fixture.DisposeAsync(); }
            catch (Exception exception)
            {
                outcome = "failed";
                if (string.IsNullOrEmpty(_report.error)) _report.error = exception.GetBaseException().Message;
                Debug.LogException(exception);
            }
            finally
            {
                UnityEngine.Random.state = random;
                Time.captureDeltaTime = captureDelta;
                Application.runInBackground = background;
                _report.status = outcome;
            }
        }
    }

    private static async Task CheckInterruptions(Fixture fixture, GameObject drone)
    {
        _report.currentCase = "Drone Groggy interruption";
        var enemy = await fixture.Spawn(drone);
        var animator = enemy.Animator;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        enemy.GroggyDuration = .15f;
        HitEnemy(enemy, 1f);
        await Until(() => IsHit(animator.GetCurrentAnimatorStateInfo(0)), "No Hit to interrupt.");
        fixture.Target.transform.position = enemy.transform.position + Vector3.forward;
        fixture.Target.Target = fixture.Target.transform;
        enemy.GetComponent<PoiseHandler>().ForceBreak();
        await Until(() => enemy.Brain.StateName == nameof(EnemyGroggyState), "Poise did not interrupt Hurt.");
        Check(!animator.HasState(0, Animator.StringToHash("Groggy")) && HasHit(animator),
            "The Drone fixture no longer exercises Groggy with a frozen Hit pose.");
        await Until(() => enemy.CurrentAction?.IsActive == true, "Drone could not attack after Groggy.",
            () => Check(!enemy.IsSelfDestructing, "Drone self-destructed during the Groggy fixture."));
        Check(!HasHit(animator) && !enemy.IsGroggy, "The next attack started over the frozen Hit pose.");
        enemy.CancelCurrentAction();

        _report.currentCase = "Drone Root after Hurt";
        HitEnemy(enemy, 1f);
        enemy.IsRoot = true;
        enemy.RootDuration = .15f;
        await Until(() => enemy.Brain.StateName == nameof(EnemyRootState), "Root did not follow Hurt.");
        Check(!HasHit(animator) && enemy.CurrentAction?.IsActive != true,
            "Root returned control before Hit and its blend ended.");
        await Until(() => enemy.CurrentAction?.IsActive == true, "Drone could not attack after Root.");
        Check(!HasHit(animator) && !enemy.IsRoot, "An attack entered during Root or the prior Hit pose.");
        enemy.CancelCurrentAction();

        _report.currentCase = "Drone death during Hurt";
        HitEnemy(enemy, 1f);
        await Until(() => IsHit(animator.GetCurrentAnimatorStateInfo(0)), "No Hit before death.");
        Check(enemy.TakeDamage(enemy.EnemyStat.CurrentHp + 1f, poiseDamage: 0f,
            staggerResistLevel: StaggerResistLevel.HIGH).Killed, "Fatal damage did not kill the fixture.");
        await Until(() => enemy.Brain.StateName == nameof(EnemyDieState), "Death did not interrupt Hurt.");
        Check(enemy.CurrentAction?.IsActive != true && !enemy.IsHurt && !enemy.IsSelfDestructing,
            "Death leaked a reaction or started a new attack.");
        fixture.Target.Target = null;
        enemy.gameObject.SetActive(false);
        Object.Destroy(enemy.gameObject);
        await Awaitable.NextFrameAsync();
    }

    private static async Task CheckAuthoredEnemy(Enemy enemy, EnemyBrainSensorProbe target)
    {
        var animator = enemy.Animator;
        await Until(() => animator.isInitialized, "The authored Animator did not initialize.");
        bool hasHit = animator.HasState(0, Hit);
        Check(hasHit || _report.currentCase == "Chang",
            "No Hit state exists on the authored controller " + animator.runtimeAnimatorController.name + ".");
        animator.cullingMode = AnimatorCullingMode.CullCompletely;
        var bomb = enemy.ActiveSkillSet.selfDestruct;
        bool bombing = bomb != null;
        var skill = enemy.ActiveSkillSet.skillData[0];
        var presenter = enemy.GetComponent<EnemyAttackRangePresentation>();
        Check(presenter != null, "The authored prefab lacks the attack warning presenter.");
        if (!bombing)
        {
            target.transform.position = enemy.transform.position
                + Vector3.forward * ((skill.minimumRange + Mathf.Min(skill.executeRange, enemy.CombatRange)) * .5f);
            target.Target = target.transform;
            typeof(Enemy).GetField("_attackCooldownTimer", Hidden).SetValue(enemy, 0f);
        }

        if (!hasHit)
        {
            // The current Chang prefab uses the melee controller without a Hit state.
            float fallbackTime = await CheckFallback(enemy);
            await Until(() => enemy.CurrentAction?.IsActive == true, "The queued attack did not start after the fallback.");
            Check(enemy.CurrentAction.SkillData == skill, "Chang lost its queued skill during the fallback.");
            _report.results.Add(FormattableString.Invariant($"{_report.currentCase}: {fallbackTime:F2}s using the timed fallback, {skill.name} started"));
            target.Target = null;
            return;
        }

        float initialHit = enemy.ActionTime;
        var power = _report.currentCase == "Test" ? Vector3.back * .25f : Vector3.zero;
        HitEnemy(enemy, bombing ? enemy.EnemyStat.CurrentHp * .8f : 1f, power);
        await Until(() => IsHit(animator.GetCurrentAnimatorStateInfo(0)), "The actual Hit animation did not start.",
            () => CheckWaiting(enemy, presenter));
        Check(animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
            "Hurt must advance even if the enemy is outside the camera.");
        var hit = animator.GetCurrentAnimatorStateInfo(0);
        await Until(() => enemy.ActionTime - initialHit > .51f, "Actor time did not advance.",
            () => CheckWaiting(enemy, presenter));
        Check(enemy.IsHurt && HasHit(animator), "Hurt ended at the old timer while the Hit animation was playing.");

        if (_report.currentCase == "Test2")
        {
            Check(hit.loop, "The Test2 fixture no longer exercises a repeating Hit clip.");
            float stopped = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            enemy.ActorClock.Request(enemy, 1f, 0f, HitStopStacking.Replace);
            for (int i = 0; i < 4; i++) await Awaitable.NextFrameAsync();
            Check(enemy.IsHurt && Mathf.Abs(animator.GetCurrentAnimatorStateInfo(0).normalizedTime - stopped) < .001f,
                "Hit animation or recovery advanced during local hit stop.");
            enemy.ActorClock.ClearStops();

            await Until(() => IsExitingHit(animator), "No transition away from the looping Hit clip was observed.",
                () => CheckWaiting(enemy, presenter));
            Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= .99f,
                "The reaction exited before finishing the first loop of Hit.");
            HitEnemy(enemy, 1f);
            initialHit = enemy.ActionTime;
            await Until(() => IsHit(animator.GetCurrentAnimatorStateInfo(0))
                && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < .3f,
                "A hit during the outgoing blend did not restart Hit.", () => CheckWaiting(enemy, presenter));
            enemy.gameObject.SetActive(false);
            await Awaitable.NextFrameAsync();
            enemy.gameObject.SetActive(true);
            await Awaitable.NextFrameAsync();
            CheckWaiting(enemy, presenter);
            Check(enemy.IsHurt, "Re-enabling during a restarted Hit dropped the reaction.");
        }

        if (power != Vector3.zero)
        {
            await Until(() => animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f,
                "The knockback fixture never completed its Hit clip.", () => CheckWaiting(enemy, presenter));
            Check(enemy.IsKnockbacking && enemy.IsHurt, "Hurt did not outlast the completed clip during long knockback.");
        }

        float lastHitTime = 0f;
        await Until(() => !enemy.IsHurt, "Hurt did not finish after its clip and movement.",
            () =>
            {
                CheckWaiting(enemy, presenter);
                var current = animator.GetCurrentAnimatorStateInfo(0);
                if (IsHit(current)) lastHitTime = Mathf.Max(lastHitTime, current.normalizedTime);
            });
        float elapsed = enemy.ActionTime - initialHit;
        Check(lastHitTime >= .99f && !HasHit(animator) && !enemy.IsKnockbacking,
            "The Hurt flag cleared before a full Hit cycle and the outgoing animation finished.");
        if (bombing)
            Check(!animator.HasState(0, Idle) && animator.GetCurrentAnimatorStateInfo(0).shortNameHash == Move,
                "Drone did not use Move as its recovery state.");
        await Until(() => enemy.CurrentAction?.IsActive == true, "The queued attack did not start after recovery.");
        Check(!enemy.IsHurt && !HasHit(animator) && animator.cullingMode == AnimatorCullingMode.CullCompletely
            && enemy.CurrentAction.SkillData == (bombing ? bomb : skill),
            "The first attack overlapped Hit, skipped the selected skill, or leaked culling settings.");
        if (bombing)
            Check(enemy.CurrentAction is EnemySelfDestruct && enemy.IsSelfDestructing,
                "Drone did not start its deferred self-destruct.");
        _report.results.Add(FormattableString.Invariant($"{_report.currentCase}: {elapsed:F2}s to recover, {enemy.CurrentAction.SkillData.name} started"));
        target.Target = null;
    }

    private static async Task<float> CheckFallback(Enemy enemy)
    {
        enemy.SetAnimationSpeed(.8f);
        float started = enemy.ActionTime;
        HitEnemy(enemy, 1f);
        await Until(() => enemy.Brain.StateName == nameof(EnemyHurtState), "Fallback Hurt did not start.");
        Check(enemy.IsHurt && Mathf.Approximately(enemy.ActorClock.AnimationSpeed, 0f),
            "Missing Hit did not hold the pose during Hurt.");
        var presenter = enemy.GetComponent<EnemyAttackRangePresentation>();
        await Until(() => !enemy.IsHurt, "Missing Hit left the enemy in Hurt.",
            () => CheckWaiting(enemy, presenter));
        float elapsed = enemy.ActionTime - started;
        await Until(() => enemy.Brain.StateName != nameof(EnemyHurtState), "The fallback state did not exit.");
        Check(elapsed + .001f >= enemy.HurtDuration && Mathf.Approximately(enemy.ActorClock.AnimationSpeed, .8f),
            "Fallback did not respect the minimum reaction or restore animation speed.");
        return elapsed;
    }

    private static void HitEnemy(Enemy enemy, float damage, Vector3 power = default)
        => Check(enemy.TakeDamage(damage, power: power, poiseDamage: 0f,
            staggerResistLevel: StaggerResistLevel.HIGH).CausedOrdinaryStagger, "The fixture hit did not cause Hurt.");

    private static bool IsHit(AnimatorStateInfo state) => state.shortNameHash == Hit;
    private static bool HasHit(Animator animator) => IsHit(animator.GetCurrentAnimatorStateInfo(0))
        || animator.IsInTransition(0) && IsHit(animator.GetNextAnimatorStateInfo(0));
    private static bool IsExitingHit(Animator animator) => IsHit(animator.GetCurrentAnimatorStateInfo(0))
        && animator.IsInTransition(0) && !IsHit(animator.GetNextAnimatorStateInfo(0));

    private static void CheckWaiting(Enemy enemy, EnemyAttackRangePresentation presenter)
        => Check(enemy.IsHurt && enemy.CurrentAction?.IsActive != true && !enemy.IsSelfDestructing
            && !presenter.WarningVisible, "An attack or warning began during an unfinished Hit reaction.");

    private static async Task Until(Func<bool> condition, string message, Action observe = null)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 12d;
        while (!condition())
        {
            if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > deadline)
                throw new InvalidOperationException(message);
            observe?.Invoke();
            await Awaitable.NextFrameAsync();
        }
        _report.checks++;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(_report.currentCase + ": " + message);
        _report.checks++;
    }

    private sealed class Fixture
    {
        private static readonly Vector3 Origin = new Vector3(1840f, 0f, 1840f);
        private readonly Scene _scene = SceneManager.CreateScene("EnemyHurtRecovery-" + Guid.NewGuid().ToString("N"));
        private readonly List<Object> _assets = new();
        private NavMeshDataInstance _navigation;
        public readonly EnemyBrainSensorProbe Target;

        public Fixture()
        {
            var source = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box, size = new Vector3(40f, .2f, 40f), area = 0,
                transform = Matrix4x4.Translate(Origin + Vector3.down * .1f)
            };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0),
                new List<NavMeshBuildSource> { source }, new Bounds(Origin, new Vector3(45f, 8f, 45f)),
                Vector3.zero, Quaternion.identity);
            Check(data != null, "Could not build isolated navigation.");
            _assets.Add(data);
            _navigation = NavMesh.AddNavMeshData(data);
            Check(_navigation.valid, "Could not attach isolated navigation.");
            var target = new GameObject("Hurt recovery target");
            SceneManager.MoveGameObjectToScene(target, _scene);
            Target = target.AddComponent<EnemyBrainSensorProbe>();
        }

        public async Task<Enemy> Spawn(GameObject prefab, bool longKnockback = false, bool withoutController = false)
        {
            Target.Target = null;
            var stage = new GameObject("Inactive enemy staging");
            SceneManager.MoveGameObjectToScene(stage, _scene);
            stage.SetActive(false);
            var clone = Object.Instantiate(prefab, stage.transform);
            clone.SetActive(false);
            clone.transform.SetParent(null);
            clone.transform.SetPositionAndRotation(Origin, Quaternion.identity);
            Object.Destroy(stage);
            var stat = clone.GetComponent<EnemyStat>();
            var data = Object.Instantiate(stat.Data);
            var skills = Object.Instantiate(data.monsterSkillData);
            var selected = skills.skillData.FirstOrDefault(skill => skill.superArmor) ?? skills.skillData[0];
            skills.skillData = new List<MonsterSkillData> { selected };
            // Exercise the real attack without depending on a random selection or cooldown roll.
            data.monsterSkillData = skills;
            data.combatRange = Mathf.Max(data.combatRange, selected.executeRange + .5f);
            data.combatExitRange = Mathf.Max(data.combatExitRange, data.combatRange + 1f);
            _assets.Add(data);
            _assets.Add(skills);
            if (longKnockback) data.knockbackMoveTime = 1.35f;
            typeof(EnemyStat).GetField("baseData", Hidden).SetValue(stat, data);
            foreach (var animator in clone.GetComponentsInChildren<Animator>(true))
            {
                animator.fireEvents = false;
                if (withoutController) animator.runtimeAnimatorController = null;
            }
            clone.SetActive(true);
            var enemy = clone.GetComponent<Enemy>();
            await Until(() => enemy.Brain != null && !enemy.IsSpawning, "The authored enemy did not finish spawning.");
            typeof(Enemy).GetField("_sensor", Hidden).SetValue(enemy, Target);
            return enemy;
        }

        public async Task DisposeAsync()
        {
            if (_scene.IsValid() && _scene.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(_scene);
                while (unload != null && !unload.isDone) await Awaitable.NextFrameAsync();
            }
            if (_navigation.valid) _navigation.Remove();
            foreach (var asset in _assets) Object.Destroy(asset);
        }
    }
}
