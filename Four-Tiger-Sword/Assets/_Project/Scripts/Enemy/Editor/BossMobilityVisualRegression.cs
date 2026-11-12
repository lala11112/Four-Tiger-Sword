using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Renders the actual comparison prefabs while their three real mobility actions run.</summary>
public static class BossMobilityVisualRegression
{
    private const string Folder = "Assets/_Project/Generated/Bosses/HitReactionVariants";
    private const int CaptureSize = 512;
    private static bool _running;
    private static Report _report = new Report { status = "idle" };

    [Serializable] private sealed class Report
    {
        public string status, error, directory, reportPath, contactSheet;
        public int checks;
        public List<Sample> samples = new List<Sample>();
        public List<string> screenshots = new List<string>();
    }

    [Serializable] private sealed class Sample
    {
        public string variant, kind, startupImage, activeImage, landingImage;
        public int frames;
        public float startingHp, endingHp, movement, peakJumpHeight, landingError, navigationError, warningDestinationError, peakWarningFill;
        public bool completed, navigationRestored, collidersPreserved, midairHitApplied, midairReactionObserved, offensiveWarningVerified;
        public Vector3 start, destination, end;
    }

    public static string StartPlayMode()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling || _running)
            throw new InvalidOperationException("Start mobility visual verification in idle Play Mode.");
        _running = true;
        _report = new Report { status = "running" };
        _report.directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
            "BossHitReactionVerification", "MobilityVisual", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")));
        Directory.CreateDirectory(_report.directory);
        _report.reportPath = Path.Combine(_report.directory, "report.json");
        _ = Run();
        return Status();
    }

    public static string Status() => JsonUtility.ToJson(_report, true);

    private static async Task Run()
    {
        Fixture fixture = null;
        try
        {
            fixture = new Fixture();
            fixture.BuildNavigation();
            foreach (string variant in new[] { "SwordRobotBoss_FinalIK", "SwordRobotBoss_Additive" })
            {
                var owner = fixture.Actor(Folder + "/" + variant + ".prefab");
                await Until(() => owner != null && owner.Brain != null, "The actual mobility prefab did not initialize.");
                await Frame();
                Check(Mathf.Approximately(owner.EnemyStat.CurrentHp, 5460f)
                    && Mathf.Approximately(owner.EnemyStat.MaxHp, 5460f),
                    variant + " did not enter phase one with 5460 HP.");
                var settings = owner.Mobility;
                Check(settings != null && settings.enabled, variant + " has no enabled mobility configuration.");
                foreach (var skill in new[] { settings.escapeJump, settings.dashSlash, settings.jumpSlam })
                {
                    Check(skill != null, variant + " is missing a mobility skill.");
                    await Verify(fixture, owner, variant, skill);
                }
                owner.gameObject.SetActive(false);
                Object.Destroy(owner.gameObject);
                await Frame();
            }
            BuildContactSheet();
            _report.status = "passed";
        }
        catch (Exception exception)
        {
            _report.status = "failed";
            _report.error = exception.GetBaseException().Message;
            Debug.LogException(exception);
        }
        finally
        {
            try { if (fixture != null) await fixture.Dispose(); }
            catch (Exception exception)
            { _report.status = "failed"; _report.error = "Cleanup: " + exception.GetBaseException().Message; }
            _running = false;
            File.WriteAllText(_report.reportPath, Status());
        }
    }

    private static async Task Verify(Fixture fixture, Boss owner, string variant, BossMobilitySkillSO skill)
    {
        owner.CancelCurrentAction();
        ClearReaction(owner);
        var nav = owner.GetComponent<NavMeshAgent>();
        Check(nav != null && nav.enabled && nav.isOnNavMesh, variant + " has no live NavMeshAgent.");
        fixture.Reset(owner, skill.kind);
        await Frame();
        await Frame();
        bool updatePosition = nav.updatePosition, updateRotation = nav.updateRotation, stopped = nav.isStopped;
        var colliders = owner.GetComponentsInChildren<Collider>(true).Where(c => c.enabled).ToArray();
        var sample = new Sample { variant = variant, kind = skill.kind.ToString(), startingHp = owner.EnemyStat.CurrentHp };
        var presentation = owner.GetComponent<SwordRobotBossPresentation>();
        _report.samples.Add(sample);
        Check(skill.TryCreateAction(owner, out var action), variant + " could not plan " + skill.kind + " on clear ground.");
        sample.start = action.StartPosition;
        sample.destination = action.Destination;
        owner.CurrentAction = action;
        owner.HasSelectedAttack = true;
        action.Enter();
        Check(action.IsActive && !action.Failed, variant + " could not enter " + skill.kind + ": " + action.FailureReason);
        bool startupCaptured = false, activeCaptured = false, landingCaptured = false, hitSent = false;
        float landedAt = -1f;
        bool shouldHit = skill.kind == BossMobilityKind.EscapeJump;
        double deadline = Time.realtimeSinceStartupAsDouble + 9d;
        while (action.IsActive && !action.IsFinished)
        {
            if (Time.realtimeSinceStartupAsDouble > deadline || sample.frames > 3000)
                throw new InvalidOperationException(variant + " " + skill.kind + " did not finish within its visual-smoke budget.");
            await NextFrame();
            action.Update();
            await Awaitable.EndOfFrameAsync();
            sample.frames++;
            Check(!action.Failed, variant + " " + skill.kind + " failed: " + action.FailureReason);
            Check(owner.CurrentAction == action && !owner.IsDead && !owner.IsHurt && !owner.IsGroggy
                && !owner.PendingGroggy && !owner.IsKnockbacking,
                variant + " " + skill.kind + " unexpectedly lost its active action.");
            Check(nav.enabled && nav.isOnNavMesh, "Mobility disabled the actual prefab's navigation.");
            Check(colliders.All(c => c != null && c.enabled), "Mobility disabled an actual prefab body collider.");
            float height = owner.transform.position.y - nav.nextPosition.y;
            sample.peakJumpHeight = Mathf.Max(sample.peakJumpHeight, height);
            sample.movement = Mathf.Max(sample.movement,
                BossMobilityNavigation.Flat(owner.transform.position - sample.start).magnitude);
            Check(BossMobilityNavigation.Finite(owner.transform.position) && height >= -.08f
                && height <= Mathf.Max(.2f, skill.jumpHeight + .2f), "Mobility produced an invalid world-space jump arc.");

            if (!skill.DealsDamage)
            {
                Check(presentation == null || !presentation.WarningVisible
                    && (presentation.bladeTrail == null || !presentation.bladeTrail.emitting),
                    "A movement-only mobility action displayed an attack warning or blade trail.");
            }
            else if (action.BossStage == BossAttackStage.Startup
                || skill.kind == BossMobilityKind.JumpSlam && action.BossStage == BossAttackStage.Active)
            {
                Check(presentation != null && presentation.WarningVisible
                    && presentation.WarningFillProgress >= sample.peakWarningFill - .01f
                    && presentation.WarningFillProgress <= 1f,
                    "An offensive mobility warning disappeared or its fill moved backward before impact.");
                sample.peakWarningFill = Mathf.Max(sample.peakWarningFill, presentation.WarningFillProgress);
            }
            else
                Check(presentation != null && !presentation.WarningVisible,
                    "An offensive mobility warning remained visible after impact.");
            if (skill.DealsDamage && !startupCaptured && action.BossStage == BossAttackStage.Startup)
            {
                sample.warningDestinationError = VerifyWarning(owner, action, skill);
                sample.offensiveWarningVerified = true;
                sample.startupImage = Capture(fixture, owner, variant + "_" + skill.kind + "_startup", action, skill);
                startupCaptured = true;
            }
            if (shouldHit && !hitSent && action.IsAirborne && action.MovementProgress >= .28f)
            {
                hitSent = true;
                float hp = owner.EnemyStat.CurrentHp;
                var hit = owner.TakeDamage(1f, power: Vector3.forward * 5f, poiseDamage: 0f,
                    staggerResistLevel: StaggerResistLevel.HIGH);
                sample.midairHitApplied = hit.Applied && hit.HealthDamage > 0f
                    && owner.EnemyStat.CurrentHp < hp && owner.CurrentAction == action && action.IsActive
                    && !owner.IsHurt && !owner.IsKnockbacking;
                Check(sample.midairHitApplied, "A normal powered hit failed to damage the airborne boss while preserving mobility.");
            }
            if (hitSent && Reacting(owner)) sample.midairReactionObserved = true;
            bool moving = action.BossStage == BossAttackStage.Active && action.MovementProgress >= .42f;
            if (!activeCaptured && moving)
            {
                if (skill.kind == BossMobilityKind.JumpSlam)
                    sample.warningDestinationError = Mathf.Max(sample.warningDestinationError, VerifyWarning(owner, action, skill));
                sample.activeImage = Capture(fixture, owner, variant + "_" + skill.kind + "_active");
                activeCaptured = true;
            }
            if (!landingCaptured && action.BossStage == BossAttackStage.Recovery)
            {
                if (landedAt < 0f) landedAt = owner.ActionTime;
                // Let the dedicated slam landing clip reach its downward strike, rather than
                // capturing the first blended frame that still resembles the airborne pose.
                if (skill.kind != BossMobilityKind.JumpSlam || owner.ActionTime - landedAt >= .18f)
                {
                    sample.landingImage = Capture(fixture, owner, variant + "_" + skill.kind + "_landing");
                    landingCaptured = true;
                }
            }
        }
        sample.completed = action.IsFinished && !action.Failed;
        sample.end = owner.transform.position;
        sample.endingHp = owner.EnemyStat.CurrentHp;
        sample.landingError = Vector3.Distance(sample.end, sample.destination);
        sample.navigationError = Vector3.Distance(sample.end, nav.nextPosition);
        Check(sample.completed && activeCaptured && landingCaptured,
            variant + " " + skill.kind + " did not complete both active and landing captures.");
        if (skill.DealsDamage)
            Check(startupCaptured && sample.offensiveWarningVerified && sample.peakWarningFill > .65f,
                "An offensive mobility action did not show and fill its verified destination warning before impact.");
        Check(sample.movement > 3f && sample.landingError < .15f && sample.navigationError < .1f,
            variant + " " + skill.kind + " did not move and land at its validated destination.");
        if (skill.IsJump)
            Check(sample.peakJumpHeight > skill.jumpHeight * .6f, "The actual jump action never visibly left the ground.");
        else Check(sample.peakJumpHeight < .15f, "A ground mobility action unexpectedly became airborne.");
        if (shouldHit)
            Check(hitSent && sample.midairHitApplied && sample.midairReactionObserved,
                "A powered hit did not produce the copied boss's cosmetic reaction during an actual jump.");

        owner.CancelCurrentAction();
        await Frame();
        sample.navigationRestored = nav.enabled && nav.isOnNavMesh && nav.updatePosition == updatePosition
            && nav.updateRotation == updateRotation && nav.isStopped == stopped && !nav.hasPath;
        sample.collidersPreserved = colliders.All(c => c != null && c.enabled);
        Check(sample.navigationRestored && sample.collidersPreserved && !action.IsActive && !owner.IsDead,
            "Finishing actual-prefab mobility did not restore navigation, colliders, and action ownership.");
        ClearReaction(owner);
    }

    private static bool Reacting(Boss owner)
    {
        var finalIK = owner.GetComponent<BossFinalIKHitReaction>();
        var additive = owner.GetComponent<BossAdditiveHitReaction>();
        return finalIK != null && finalIK.IsReacting || additive != null && additive.IsReacting;
    }

    private static float VerifyWarning(Boss owner, BossMobilityAction action, BossMobilitySkillSO skill)
    {
        var presentation = owner.GetComponent<SwordRobotBossPresentation>();
        Check(presentation != null && presentation.WarningVisible,
            "Offensive mobility must display a world-space destination warning.");
        if (skill.kind == BossMobilityKind.JumpSlam)
        {
            var circle = presentation.WarningCircle;
            Check(!presentation.IsCorridorWarning && circle != null && circle.isActiveAndEnabled
                && circle.Type == Shapes.DiscType.Ring, "Jump slam did not draw a Shapes landing circle.");
            float error = Vector3.Distance(circle.transform.position, action.Destination + Vector3.up * .045f);
            Check(error < .07f && Mathf.Abs(circle.Radius - skill.slamRadius) < .05f,
                "The jump slam warning did not match its locked landing position and damage radius.");
            return error;
        }
        var corridor = presentation.WarningCorridor;
        Check(presentation.IsCorridorWarning && corridor != null && corridor.isActiveAndEnabled
            && corridor.IsBorder && Mathf.Abs(corridor.Width - skill.hitBoxRadius * 2f) < .05f,
            "Dash slash did not draw its locked travel corridor with the authored width.");
        Vector3 forward = corridor.transform.up * (corridor.Height * .5f);
        Vector3 begin = corridor.transform.position - forward;
        Vector3 end = corridor.transform.position + forward;
        float destinationError = Vector3.Distance(end, action.Destination + Vector3.up * .045f);
        Check(Vector3.Distance(begin, action.StartPosition + Vector3.up * .045f) < .07f
            && destinationError < .07f, "The dash warning moved away from its locked start or destination.");
        return destinationError;
    }

    private static void ClearReaction(Boss owner)
    {
        if (owner == null) return;
        owner.GetComponent<BossFinalIKHitReaction>()?.ClearReaction();
        owner.GetComponent<BossAdditiveHitReaction>()?.ClearReaction();
    }

    private static string Capture(Fixture fixture, Boss owner, string label,
        BossMobilityAction framedAction = null, BossMobilitySkillSO framedSkill = null)
    {
        var camera = fixture.Camera;
        if (framedAction == null)
        {
            camera.transform.position = owner.transform.position + new Vector3(5f, 4f, 7f);
            camera.transform.LookAt(owner.transform.position + Vector3.up * 1.3f);
        }
        else
        {
            // Frame the entire locked destination telegraph, rather than following the boss
            // closely and leaving its landing circle underneath/outside the camera frustum.
            var bounds = new Bounds(owner.transform.position + Vector3.up * 1.5f, new Vector3(3f, 4f, 3f));
            float radius = framedSkill.kind == BossMobilityKind.JumpSlam ? framedSkill.slamRadius : framedSkill.hitBoxRadius;
            bounds.Encapsulate(new Bounds(framedAction.Destination + Vector3.up * .045f,
                new Vector3(radius * 2f, .1f, radius * 2f)));
            float distance = bounds.extents.magnitude / Mathf.Sin(camera.fieldOfView * .5f * Mathf.Deg2Rad) * 1.08f;
            camera.transform.position = bounds.center + new Vector3(.55f, .85f, 1f).normalized * distance;
            camera.transform.LookAt(bounds.center);
            var warning = owner.GetComponent<SwordRobotBossPresentation>();
            for (int i = 0; i < warning.GetWarningPointCount(); i++)
            {
                Vector3 viewport = camera.WorldToViewportPoint(warning.GetWarningPoint(i));
                Check(viewport.z > 0f && viewport.x > .02f && viewport.x < .98f
                    && viewport.y > .02f && viewport.y < .98f,
                    "The offensive startup capture did not include its complete destination warning.");
            }
        }
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(CaptureSize, CaptureSize, 24, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(CaptureSize, CaptureSize, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, CaptureSize, CaptureSize), 0, 0);
            texture.Apply();
            string path = Path.Combine(_report.directory, label + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            _report.screenshots.Add(path);
            return path;
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.Destroy(texture);
        }
    }

    private static void BuildContactSheet()
    {
        if (_report.screenshots.Count == 0) return;
        int rows = Mathf.CeilToInt(_report.screenshots.Count / 4f);
        var sheet = new Texture2D(CaptureSize * 4, CaptureSize * rows, TextureFormat.RGB24, false);
        try
        {
            for (int i = 0; i < _report.screenshots.Count; i++)
            {
                var tile = new Texture2D(2, 2, TextureFormat.RGB24, false);
                try
                {
                    tile.LoadImage(File.ReadAllBytes(_report.screenshots[i]));
                    sheet.SetPixels(i % 4 * CaptureSize, (rows - 1 - i / 4) * CaptureSize,
                        CaptureSize, CaptureSize, tile.GetPixels());
                }
                finally { Object.Destroy(tile); }
            }
            sheet.Apply();
            _report.contactSheet = Path.Combine(_report.directory, "mobility-contact.png");
            File.WriteAllBytes(_report.contactSheet, sheet.EncodeToPNG());
        }
        finally { Object.Destroy(sheet); }
    }

    private static async Task NextFrame()
    {
        int before = Time.frameCount;
        double deadline = Time.realtimeSinceStartupAsDouble + 12d;
        do
        {
            if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > deadline)
                throw new InvalidOperationException("Play Mode frames stopped during mobility visual verification.");
            await Task.Yield();
        } while (Time.frameCount == before);
    }

    private static async Task Frame()
    {
        await NextFrame();
        await Awaitable.EndOfFrameAsync();
    }

    private static async Task Until(Func<bool> condition, string message)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 12d;
        while (!condition())
        {
            if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > deadline)
                throw new InvalidOperationException(message);
            await Task.Yield();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _report.checks++;
    }

    private static void Set(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) { field.SetValue(target, value); return; }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    internal sealed class Fixture
    {
        private readonly Scene _scene;
        private readonly List<Object> _owned = new List<Object>();
        private readonly float _timeScale = Time.timeScale;
        private readonly bool _background = Application.runInBackground;
        private readonly UnityEngine.Random.State _random = UnityEngine.Random.state;
        private readonly Vector3 _origin = new Vector3(16000f, 0f, 16000f);
        private readonly Transform _target;
        private readonly Action<bool, string> _check;
        private NavMeshDataInstance _navigation;
        public readonly Camera Camera;

        public Fixture(Action<bool, string> check = null)
        {
            _check = check ?? Check;
            Time.timeScale = 1f;
            Application.runInBackground = true;
            _scene = SceneManager.CreateScene("BossMobilityVisual-" + Guid.NewGuid().ToString("N"));
            var floor = Primitive(PrimitiveType.Cube, "Mobility visual ground");
            floor.transform.position = _origin - Vector3.up * .1f;
            floor.transform.localScale = new Vector3(80f, .2f, 80f);
            Material(floor, new Color(.22f, .25f, .28f));
            var marker = Primitive(PrimitiveType.Cylinder, "Mobility visual target marker");
            marker.transform.localScale = new Vector3(.6f, .03f, .6f);
            var markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null) Object.Destroy(markerCollider);
            Material(marker, new Color(.05f, .8f, .85f));
            _target = marker.transform;
            _target.position = _origin + Vector3.forward * 10f;
            var cameraHost = Host("Mobility visual camera");
            Camera = cameraHost.AddComponent<Camera>();
            Camera.enabled = false;
            Camera.fieldOfView = 38f;
            Camera.nearClipPlane = .05f;
            Camera.farClipPlane = 50f;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(.10f, .13f, .17f);
            var light = Host("Mobility visual light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
        }

        public void BuildNavigation()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/SwordRobotBoss_FinalIK.prefab");
            if (prefab == null) throw new InvalidOperationException("The actual FinalIK prefab is missing.");
            var sourceAgent = prefab.GetComponent<NavMeshAgent>();
            var settings = NavMesh.GetSettingsByID(sourceAgent.agentTypeID);
            var sources = new List<NavMeshBuildSource> { new NavMeshBuildSource {
                shape = NavMeshBuildSourceShape.Box, size = new Vector3(80f, .2f, 80f), area = 0,
                transform = Matrix4x4.TRS(_origin - Vector3.up * .1f, Quaternion.identity, Vector3.one)
            } };
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(_origin, new Vector3(90f, 12f, 90f)), Vector3.zero, Quaternion.identity);
            if (data == null) throw new InvalidOperationException("Could not create isolated mobility visual navigation.");
            _owned.Add(data);
            _navigation = NavMesh.AddNavMeshData(data);
            _check(_navigation.valid, "The isolated mobility visual NavMesh did not register.");
            Physics.SyncTransforms();
        }

        public Boss Actor(string path)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null) throw new InvalidOperationException("Missing mobility comparison prefab: " + path);
            var staging = Host("Inactive mobility visual actor staging");
            staging.SetActive(false);
            var host = Object.Instantiate(source, staging.transform);
            host.SetActive(false);
            host.transform.SetParent(null);
            SceneManager.MoveGameObjectToScene(host, _scene);
            host.transform.SetPositionAndRotation(_origin, Quaternion.identity);
            Object.Destroy(staging);
            var owner = host.GetComponent<ConfiguredBoss>();
            var data = Object.Instantiate(owner.Data);
            data.rewardGroup = null;
            data.initialShield = 0f;
            _owned.Add(data);
            Set(owner, "BossData", data);
            Set(host.GetComponent<EnemyStat>(), "baseData", data);
            var phases = new List<BossPhaseData>();
            foreach (var sourcePhase in owner.PhaseConfiguration)
            {
                var phase = Object.Instantiate(sourcePhase);
                phase.musicMode = BossMusicMode.Keep;
                phase.overrideShield = true;
                phase.shieldAmount = 0f;
                phases.Add(phase);
                _owned.Add(phase);
            }
            Set(owner, "PhaseDatas", phases);
            owner.CombatLabPassive = true;
            owner.SetCombatTarget(_target);
            foreach (var animator in host.GetComponentsInChildren<Animator>(true)) animator.fireEvents = false;
            host.SetActive(true);
            var nav = host.GetComponent<NavMeshAgent>();
            if (nav.enabled && nav.isOnNavMesh) nav.Warp(_origin);
            return owner;
        }

        public void Reset(Boss owner, BossMobilityKind kind)
        {
            _target.position = _origin + Vector3.forward * (kind == BossMobilityKind.EscapeJump ? 2.5f : 10f);
            var nav = owner.GetComponent<NavMeshAgent>();
            _check(nav.Warp(_origin), "Could not reset the visual actor on its isolated NavMesh.");
            owner.transform.position = nav.nextPosition;
            owner.transform.rotation = Quaternion.identity;
            nav.ResetPath();
            nav.velocity = Vector3.zero;
            owner.SetAnimationSpeed(1f);
            owner.Animator.SetFloat("DirX", 0f);
            owner.Animator.SetFloat("DirY", 0f);
            owner.Animator.Play("Move", 0, 0f);
            owner.Animator.Update(0f);
            owner.SyncMovementLock();
            Physics.SyncTransforms();
        }

        private GameObject Host(string name)
        {
            var host = new GameObject(name);
            SceneManager.MoveGameObjectToScene(host, _scene);
            return host;
        }

        private GameObject Primitive(PrimitiveType type, string name)
        {
            var host = GameObject.CreatePrimitive(type);
            host.name = name;
            SceneManager.MoveGameObjectToScene(host, _scene);
            return host;
        }

        private void Material(GameObject host, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return;
            var material = new Material(shader) { color = color };
            _owned.Add(material);
            host.GetComponent<Renderer>().sharedMaterial = material;
        }

        public async Task Dispose()
        {
            try
            {
                if (_scene.IsValid() && _scene.isLoaded)
                {
                    foreach (var root in _scene.GetRootGameObjects())
                        foreach (var owner in root.GetComponentsInChildren<Boss>(true))
                        { ClearReaction(owner); owner.CancelCurrentAction(); }
                    var operation = SceneManager.UnloadSceneAsync(_scene);
                    double deadline = Time.realtimeSinceStartupAsDouble + 12d;
                    while (operation != null && !operation.isDone)
                    {
                        if (Time.realtimeSinceStartupAsDouble > deadline)
                            throw new InvalidOperationException("Mobility visual scene cleanup timed out.");
                        await Task.Yield();
                    }
                }
            }
            finally
            {
                if (_navigation.valid) _navigation.Remove();
                foreach (var owned in _owned) if (owned != null) Object.Destroy(owned);
                Time.timeScale = _timeScale;
                Application.runInBackground = _background;
                UnityEngine.Random.state = _random;
            }
        }
    }
}
