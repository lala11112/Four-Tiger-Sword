using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Shapes;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// <summary>Exercises the range renderer against real action phases and authored enemies.</summary>
public static class EnemyAttackRangeRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo Timer = typeof(EnemyAction).GetField("_timer", Hidden);
    private static readonly Vector3 Origin = new Vector3(1700f, 0f, 1700f);
    private static Report _report = new Report { status = "idle" };

    [Serializable]
    private sealed class Report
    {
        public string status, error, directory;
        public int checks;
        public List<string> captures = new();
    }

    public static string RunEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run the edit checks outside Play Mode.");
        using var fixture = new Fixture();
        fixture.CheckMeleeAndLifetime();
        return $"PASS: attack ranges in Edit Mode ({fixture.Checks} checks).";
    }

    public static string StartPlayMode()
    {
        if (!Application.isPlaying || _report.status == "running")
            throw new InvalidOperationException("Start attack range verification in idle Play Mode.");
        _report = new Report
        {
            status = "running",
            directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp",
                "EnemyAttackRange", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")))
        };
        Directory.CreateDirectory(_report.directory);
        _ = RunPlayMode();
        return Status();
    }

    public static string Status() => JsonUtility.ToJson(_report, true);

    private static async Task RunPlayMode()
    {
        bool background = Application.runInBackground;
        float previousCaptureDelta = Time.captureDeltaTime;
        try
        {
            Application.runInBackground = true;
            Time.captureDeltaTime = 1f / 30f;
            await Awaitable.NextFrameAsync();
            using var fixture = new Fixture();
            fixture.BuildNavigation();
            fixture.CheckMeleeAndLifetime();
            fixture.CheckActionPhases();
            await fixture.CheckCatalogAndCapture(_report);
            _report.checks = fixture.Checks;
            _report.status = "passed";
        }
        catch (Exception exception)
        {
            _report.status = "failed";
            _report.error = exception.GetBaseException().ToString();
            Debug.LogException(exception);
        }
        finally
        {
            Time.captureDeltaTime = previousCaptureDelta;
            Application.runInBackground = background;
            File.WriteAllText(Path.Combine(_report.directory, "report.json"), Status());
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject _root = new GameObject("Enemy attack range regression")
            { hideFlags = HideFlags.HideAndDontSave };
        private readonly List<Object> _owned = new();
        private readonly UnityEngine.Random.State _random = UnityEngine.Random.state;
        private NavMeshDataInstance _navigation;
        public int Checks { get; private set; }

        public void CheckMeleeAndLifetime()
        {
            var actor = Actor(Origin);
            actor.transform.localScale = new Vector3(2f, .6f, 3f);
            actor.transform.rotation = Quaternion.Euler(0f, 38f, 0f);
            var second = Actor(Origin + Vector3.right * 6f);
            var melee = Data<MonsterDefaultAttackSO>();
            melee.hitBoxRadius = 1.6f;
            melee.hitBoxOffset = new Vector3(.45f, 1.2f, 1.75f);
            melee.hitStartTime = .6f;
            melee.hitBoxShape = HitBoxShape.Box;
            melee.hitBoxSize = new Vector3(1.7f, 3f, 2.3f);

            var action = Begin(actor, melee);
            SetTimer(action, .3f);
            var presenter = Show(action, AttackRangeWarningKind.Rectangle, out var warning);
            Vector3 center = actor.transform.position + actor.transform.rotation * melee.hitBoxOffset;
            var outline = presenter.WarningRectangle;
            Check(FlatDistance(center, outline.transform.position) < .01f
                && Near(outline.Width, 1.7f) && Near(outline.Height, 2.3f)
                && Near(outline.transform.lossyScale.x, 1f) && Near(outline.transform.lossyScale.y, 1f)
                && Vector3.Dot(outline.transform.up, actor.transform.forward) > .999f
                && Near(warning.Progress, .5f), "Box warning did not match the rotated, unscaled hit box.");
            Check(action.IsAttackWarningActive && !ParryEventBus.IsTelegraphActive(action),
                "The ground warning changed the existing non-parryable attack cue.");
            actor.CancelCurrentAction();
            HiddenRange(actor, "Cancellation");

            melee.hitBoxShape = HitBoxShape.Sphere;
            action = Begin(actor, melee);
            var firstRing = Show(action, AttackRangeWarningKind.Circle, out _).WarningCircle;
            Check(Near(firstRing.Radius, 1.6f) && FlatDistance(center, firstRing.transform.position) < .01f
                && Near(firstRing.transform.lossyScale.x, 1f), "Melee circle did not use the unscaled damage radius and center.");
            melee.hitBoxShape = HitBoxShape.Capsule;
            var other = Begin(second, melee);
            var otherRing = Show(other, AttackRangeWarningKind.Circle, out _).WarningCircle;
            Check(otherRing != firstRing && Near(otherRing.Radius, 1.6f)
                && FlatDistance(otherRing.transform.position, firstRing.transform.position) > 3f,
                "Two enemies shared a ring, or a capsule lost its radius.");

            actor.gameObject.SetActive(false);
            if (!Application.isPlaying)
            {
                InvokeEnemy(actor, "OnDisable");
                typeof(EnemyAttackRangePresentation).GetMethod("OnDisable", Hidden).Invoke(presenter, null);
            }
            Check(!firstRing.enabled && !presenter.WarningVisible && !action.IsActive,
                "Disabling the enemy left an active warning or attack.");
            Check(otherRing.enabled, "Disabling one enemy hid a second enemy's warning.");
            actor.gameObject.SetActive(true);
            HiddenRange(actor, "Reactivation before an attack");
            melee.isParryable = true;
            action = Begin(actor, melee);
            Check(Show(action, AttackRangeWarningKind.Circle, out _).WarningCircle == firstRing
                && actor.GetComponents<EnemyAttackRangePresentation>().Length == 1,
                "Reactivation duplicated the presenter or its stored geometry.");
            action.OnParried();
            HiddenRange(actor, "Parry");
            InvokeEnemy(second, "Die");
            HiddenRange(second, "Death");
            Check(second.IsDead, "The death check did not use the real death path.");
        }

        public void CheckActionPhases()
        {
            var elevated = Actor(Origin + Vector3.back * 8f, true);
            elevated.GetComponent<NavMeshAgent>().baseOffset = 1f;
            elevated.transform.position = Origin + Vector3.back * 8f + Vector3.up;
            var elevatedStrike = Begin(elevated, Data<MonsterDefaultAttackSO>());
            Show(elevatedStrike, AttackRangeWarningKind.Circle, out var elevatedWarning);
            Check(Near(elevatedWarning.Center.y, Origin.y),
                "An elevated NavMesh pivot lifted its melee warning above the floor.");

            var actor = Actor(Origin + Vector3.left * 4f);
            var melee = Data<MonsterDefaultAttackSO>();
            melee.attackSpeed = 2.5f;
            melee.hitStartTime = .6f;
            melee.hitDuration = .04f;
            var basic = Begin(actor, melee);
            float dt = actor.ActionDeltaTime;
            Check(dt > 0f, "Play Mode time is not advancing.");
            basic.Update();
            Show(basic, AttackRangeWarningKind.Circle, out var warning);
            Check(Near(warning.Progress, dt * 2.5f / .6f),
                "Windup fill did not follow action speed.");

            var clock = CombatActorClock.GetOrCreate(actor);
            typeof(Enemy).GetProperty(nameof(Enemy.ActorClock)).SetValue(actor, clock);
            clock.Request(actor, 1f, 0f, HitStopStacking.Replace);
            float beforePause = warning.Progress;
            basic.Update();
            Show(basic, AttackRangeWarningKind.Circle, out warning);
            Check(Near(warning.Progress, beforePause), "Actor hit stop advanced the warning fill.");
            clock.ClearStops();
            TickAcross(basic, melee.hitStartTime);
            Check(basic.AttackTimeline > melee.hitStartTime + melee.hitDuration,
                "The test frame did not skip over the short melee hit window.");
            HiddenRange(actor, "A frame crossing the entire melee hit window");

            var combo = Data<MonsterComboAttackSO>();
            combo.hits[0].hitBoxOffset = Vector3.forward;
            combo.hits[1].hitBoxOffset = Vector3.right * 2f;
            var chain = Begin(actor, combo);
            var first = combo.hits[0];
            var second = combo.hits[1];
            TickAcross(chain, first.hitStartTime);
            HiddenRange(actor, "First combo hit");
            SetTimer(chain, .5f * (first.hitStartTime + first.hitDuration + second.hitStartTime));
            Show(chain, AttackRangeWarningKind.Circle, out warning);
            Check(Near(warning.Progress, .5f) && !chain.IsAttackWarningActive
                && FlatDistance(warning.Center, actor.transform.position + Vector3.right * 2f) < .01f,
                "The second windup did not restart independently at its own hit center.");
            TickAcross(chain, second.hitStartTime);
            HiddenRange(actor, "Second combo hit");

            var dashSkill = Data<MonsterDashAttackSO>();
            dashSkill.hitBoxOffset = new Vector3(.5f, 1f, 1f);
            dashSkill.dashSpeed = 12f;
            dashSkill.dashDuration = .5f;
            dashSkill.hitBoxRadius = .7f;
            var dash = Begin(actor, dashSkill);
            var road = Show(dash, AttackRangeWarningKind.Corridor, out _).WarningRectangle;
            Check(Near(road.Width, 1.4f) && Near(road.Height, 7.4f)
                && FlatDistance(road.transform.position, actor.transform.position + new Vector3(.5f, 0f, 4f)) < .01f,
                "Dash corridor did not account for the swept hitbox offset and extent.");
            TickAcross(dash, dashSkill.startupTime);
            HiddenRange(actor, "Dash movement");

            var ranged = Begin(actor, Data<Baekho.Generated.a3355a1c8240244a995e4f671b0dc48bb.MonsterTripleSphereBurstSO>());
            HiddenRange(actor, "Ranged windup");
            Check(ranged.IsAttackWarningActive, "The ranged attack lost its existing cue.");

            var jumper = Actor(Origin + Vector3.right * 8f, true);
            var target = new GameObject("Jump target");
            target.transform.SetParent(_root.transform);
            target.transform.position = Origin + Vector3.right * 20f;
            var sense = jumper.gameObject.AddComponent<EnemyBrainSensorProbe>();
            sense.Target = target.transform;
            typeof(Enemy).GetField("_sensor", Hidden).SetValue(jumper, sense);
            var jumpSkill = Data<MonsterJumpSlamSO>();
            jumpSkill.hitBoxOffset = new Vector3(.2f, 1f, .65f);
            var jump = Begin(jumper, jumpSkill);
            Show(jump, AttackRangeWarningKind.Circle, out var planned);
            TickAcross(jump, jumpSkill.startupTime);
            target.transform.position += Vector3.forward * 5f;
            SetTimer(jump, jumpSkill.jumpDuration * .5f);
            jump.Update();
            Show(jump, AttackRangeWarningKind.Circle, out var airborne);
            Check(FlatDistance(airborne.Center, planned.Center) < .01f && airborne.Progress > planned.Progress
                && jumper.transform.position.y > airborne.Center.y + 1f,
                "The landing marker followed the target or the enemy into the air.");
            TickAcross(jump, jumpSkill.jumpDuration);
            HiddenRange(jumper, "Landing impact");
            var landed = jumper.transform.position + jumper.transform.rotation * jumpSkill.hitBoxOffset;
            Check(jumper.GetComponent<NavMeshAgent>().isOnNavMesh && FlatDistance(landed, airborne.Center) < .03f,
                "The warning did not match the corrected NavMesh landing position.");

            var bomber = Actor(Origin + Vector3.back * 5f);
            var explosive = Data<MonsterSelfDestructSO>();
            explosive.attackSpeed = 4f;
            explosive.explosionDelay = 2f * dt;
            explosive.explosionOffset = new Vector3(.5f, 0f, 1f);
            InvokeEnemy(bomber, "BeginSelfDestruct");
            var bomb = Begin(bomber, explosive);
            bomber.IsHurt = true;
            bomb.Update();
            Show(bomb, AttackRangeWarningKind.Circle, out warning);
            Check(Near(warning.Progress, .5f) && Near(warning.Radius, explosive.explosionRadius),
                "A still-active self-destruct warning used attack speed or disappeared when the actor was hurt.");
            bomb.Update();
            HiddenRange(bomber, "The explosion");
            Check(bomber.IsDead, "The action did not reach the normal self-destruct death path.");

            // Close the still-live synchronous actors before the next Unity frame.
            foreach (var enemy in _root.GetComponentsInChildren<Enemy>(true))
                Object.DestroyImmediate(enemy.gameObject);
        }

        public void BuildNavigation()
        {
            var source = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box, size = new Vector3(40f, .2f, 40f), area = 0,
                transform = Matrix4x4.Translate(Origin - Vector3.up * .1f)
            };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0),
                new List<NavMeshBuildSource> { source }, new Bounds(Origin, new Vector3(45f, 8f, 45f)),
                Vector3.zero, Quaternion.identity);
            Check(data != null, "Could not build the isolated attack-range navigation surface.");
            _owned.Add(data);
            _navigation = NavMesh.AddNavMeshData(data);
            Check(_navigation.valid, "Could not register attack-range navigation.");
        }

        public async Task CheckCatalogAndCapture(Report report)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<DungeonEnemyCatalog>(
                "Assets/_Game/ProceduralDungeon/DungeonEnemyCatalog.asset");
            Check(catalog != null, "The dungeon enemy catalog could not be loaded.");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(_root.transform);
            floor.transform.position = Origin - Vector3.up * .1f;
            floor.transform.localScale = new Vector3(40f, .2f, 40f);
            var light = Child("Range verification light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(48f, -30f, 0f);
            var camera = Child("Range verification camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .14f, .18f);
            camera.farClipPlane = 50f;
            foreach (var entry in catalog.enemies)
            {
                var stage = Child("Enemy staging");
                stage.SetActive(false);
                var instance = Object.Instantiate(entry.prefab, stage.transform);
                instance.SetActive(false);
                instance.transform.SetParent(_root.transform, false);
                instance.transform.SetPositionAndRotation(Origin, Quaternion.identity);
                foreach (var animator in instance.GetComponentsInChildren<Animator>(true)) animator.fireEvents = false;
                instance.SetActive(true);
                var actor = instance.GetComponent<Enemy>();
                double timeout = Time.realtimeSinceStartupAsDouble + 5d;
                while (actor.Brain == null && Time.realtimeSinceStartupAsDouble < timeout)
                    await Awaitable.NextFrameAsync();
                Check(actor.Brain != null, entry.prefab.name + " did not initialize.");
                var reveal = actor.GetComponent<EnemyDeathFade>();
                if (reveal != null) typeof(EnemyDeathFade).GetMethod("EndSpawnReveal", Hidden).Invoke(reveal, null);
                await Awaitable.NextFrameAsync();
                var skills = actor.ActiveSkillSet;
                Check(skills != null, entry.prefab.name + " has no skill set.");
                MonsterSkillData skill = skills.selfDestruct;
                if (skill == null)
                    skill = skills.skillData.FirstOrDefault(s => s is MonsterDefaultAttackSO or MonsterComboAttackSO
                        or MonsterDashAttackSO or MonsterJumpSlamSO);
                var action = Begin(actor, skill ?? skills.skillData.First());
                if (skill == null)
                {
                    HiddenRange(actor, entry.prefab.name + " ranged windup");
                }
                else
                {
                    if (skill is MonsterSelfDestructSO)
                        Check(typeof(EnemySelfDestruct).GetField("_warning", Hidden).GetValue(action) == null,
                            entry.prefab.name + " duplicated its legacy ground warning.");
                    foreach (float progress in new[] { .15f, .5f, .85f })
                    {
                        SetStartupProgress(action, progress);
                        var presenter = actor.GetComponent<EnemyAttackRangePresentation>();
                        presenter.Refresh();
                        Check(presenter.WarningVisible && Near(presenter.WarningFillProgress, progress),
                            entry.prefab.name + " did not display the expected windup fill.");
                        Check(action.TryGetAttackRangeWarning(out var warning), "The visible range had no action footprint.");
                        Capture(camera, instance, warning, report, entry.prefab.name, progress);
                    }
                }
                var renderer = actor.GetComponent<EnemyAttackRangePresentation>();
                var ring = renderer.WarningCircle;
                var rectangle = renderer.WarningRectangle;
                actor.CancelCurrentAction();
                instance.SetActive(false);
                Object.Destroy(instance);
                Object.Destroy(stage);
                await Awaitable.NextFrameAsync();
                await Awaitable.NextFrameAsync();
                Check(ring == null && rectangle == null, entry.prefab.name + " left orphaned warning geometry.");
            }
        }

        private static void SetStartupProgress(EnemyAction action, float progress)
        {
            switch (action.SkillData)
            {
                case MonsterDefaultAttackSO melee:
                    SetTimer(action, melee.hitStartTime * progress);
                    break;
                case MonsterComboAttackSO chain:
                    SetTimer(action, chain.hits[0].hitStartTime * progress);
                    break;
                case MonsterDashAttackSO dash:
                    SetTimer(action, dash.startupTime * progress);
                    break;
                case MonsterJumpSlamSO jump:
                    SetTimer(action, (jump.startupTime + jump.jumpDuration) * progress);
                    break;
                case MonsterSelfDestructSO bomb:
                    typeof(EnemySelfDestruct).GetField("_elapsed", Hidden).SetValue(action, bomb.explosionDelay * progress);
                    break;
                default:
                    throw new InvalidOperationException("This catalog attack needs a windup fixture: " + action.SkillData.name);
            }
        }

        private static void Capture(Camera camera, GameObject actor, AttackRangeWarning range, Report report,
            string name, float progress)
        {
            float width = Mathf.Max(range.Radius * 2f, range.Size.x, range.Size.y, 2.5f);
            var center = Vector3.Lerp(actor.transform.position + Vector3.up * .7f, range.Center, .55f);
            camera.transform.position = center + new Vector3(.7f, 1f, -1f).normalized * (width + 2f);
            camera.transform.LookAt(center);
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(512, 512, 24, RenderTextureFormat.ARGB32);
            var png = new Texture2D(512, 512, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                png.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                png.Apply();
                string path = Path.Combine(report.directory, $"{name}_{Mathf.RoundToInt(progress * 100f)}.png");
                File.WriteAllBytes(path, png.EncodeToPNG());
                report.captures.Add(path);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(png);
            }
        }

        private Enemy Actor(Vector3 position, bool onNavMesh = false)
        {
            var body = Child("Range test actor");
            body.SetActive(false);
            body.transform.position = position;
            var nav = body.AddComponent<NavMeshAgent>();
            nav.enabled = onNavMesh;
            var actor = body.AddComponent<Enemy>();
            actor.EnemyStat = body.GetComponent<EnemyStat>();
            body.SetActive(true);
            actor.EnemyStat.CurrentHp = 100f;
            if (!Application.isPlaying) EnemyAttackRangePresentation.Ensure(actor);
            Check(actor.GetComponent<EnemyAttackRangePresentation>() != null,
                "A normal enemy did not get its range presenter.");
            if (onNavMesh) Check(nav.Warp(position), "Could not place the jump fixture on its NavMesh.");
            return actor;
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(_root.transform, false);
            return child;
        }

        private T Data<T>() where T : MonsterSkillData
        {
            var skill = ScriptableObject.CreateInstance<T>();
            skill.isParryable = false;
            _owned.Add(skill);
            return skill;
        }

        private EnemyAction Begin(Enemy actor, MonsterSkillData skill)
        {
            actor.CancelCurrentAction();
            var action = skill.CreateAction(actor);
            actor.CurrentAction = action;
            action.Enter();
            Check(action.IsActive, "The action did not start: " + skill.GetType().Name);
            return action;
        }

        private EnemyAttackRangePresentation Show(EnemyAction action, AttackRangeWarningKind kind,
            out AttackRangeWarning warning)
        {
            Check(action.TryGetAttackRangeWarning(out warning) && warning.Kind == kind,
                action.GetType().Name + " did not report its expected " + kind + " warning.");
            var presenter = action.Owner.GetComponent<EnemyAttackRangePresentation>();
            presenter.Refresh();
            Check(presenter.WarningVisible && Near(presenter.WarningFillProgress, warning.Progress),
                action.GetType().Name + " did not render the action's warning.");
            return presenter;
        }

        private void HiddenRange(Enemy actor, string context)
        {
            var presenter = actor.GetComponent<EnemyAttackRangePresentation>();
            presenter.Refresh();
            Check(!presenter.WarningVisible && Near(presenter.WarningFillProgress, 0f),
                context + " left a ground warning visible.");
        }

        private void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            Checks++;
        }

        public void Dispose()
        {
            if (_root != null)
            {
                foreach (var actor in _root.GetComponentsInChildren<Enemy>(true))
                {
                    actor.CancelCurrentAction();
                    if (!Application.isPlaying && actor.TryGetComponent<EnemyAttackRangePresentation>(out var presenter))
                        typeof(EnemyAttackRangePresentation).GetMethod("OnDestroy", Hidden).Invoke(presenter, null);
                }
                Object.DestroyImmediate(_root);
            }
            if (_navigation.valid) _navigation.Remove();
            foreach (var obj in _owned) Object.DestroyImmediate(obj);
            UnityEngine.Random.state = _random;
        }
    }

    private static void TickAcross(EnemyAction action, float boundary)
    {
        SetTimer(action, boundary - .25f * action.Owner.ActionDeltaTime * action.SkillData.attackSpeed);
        action.Update();
    }

    private static void SetTimer(EnemyAction action, float timer) => Timer.SetValue(action, timer);
    private static void InvokeEnemy(Enemy actor, string method) => typeof(Enemy).GetMethod(method, Hidden).Invoke(actor, null);
    private static bool Near(float first, float second) => Mathf.Abs(first - second) < .005f;
    private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
}
