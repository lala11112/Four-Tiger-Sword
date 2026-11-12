using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shapes;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks the authored boss warning geometry and captures its fill at three points before impact.</summary>
public static class BossAttackShapesRegression
{
    private const string BossFolder = "Assets/_Project/Generated/Bosses/SwordRobotBoss";
    private const string VariantPrefab = BossHitReactionVariantsSetup.AdditivePath;
    private const float FloorLift = .045f;
    private static readonly float[] CaptureThresholds = { .12f, .44f, .76f };
    private static bool _running;
    private static Report _report = new Report { status = "idle" };

    [Serializable] private sealed class Report
    {
        public string status, currentCase, error, directory;
        public int checks;
        public List<Sample> samples = new List<Sample>();
    }

    [Serializable] private sealed class Sample
    {
        public string attack, image;
        public int warning;
        public float progress;
    }

    public static string StartPlayMode()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling || _running)
            throw new InvalidOperationException("Start boss Shapes verification in idle Play Mode.");
        _report = new Report
        {
            status = "running",
            directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp",
                "BossAttackShapes", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")))
        };
        Directory.CreateDirectory(_report.directory);
        _running = true;
        _ = Run();
        return Status();
    }

    public static string Status() => JsonUtility.ToJson(_report, true);

    private static async Task Run()
    {
        BossMobilityVisualRegression.Fixture fixture = null;
        float previousCaptureDeltaTime = Time.captureDeltaTime;
        try
        {
            // Keep the short startup windows observable even if saving a screenshot stalls a frame.
            Time.captureDeltaTime = 1f / 30f;
            fixture = new BossMobilityVisualRegression.Fixture(Check);
            fixture.BuildNavigation();
            VerifyPrefabs();
            var boss = fixture.Actor(VariantPrefab);
            await Frame();
            Check(boss.Brain != null, "The authored boss did not initialize.");
            var presentation = boss.GetComponent<SwordRobotBossPresentation>();
            Check(presentation != null, "The authored boss did not have its warning presentation.");
            var basic = Skill("Basic");
            await Verify(fixture, boss, presentation, basic);
            var ring = presentation.WarningCircle;
            await Verify(fixture, boss, presentation, Skill("Sweep"));
            await Verify(fixture, boss, presentation, Skill("Charge_Phase2"));
            await Verify(fixture, boss, presentation, Skill("Combo_Phase2"));
            await Verify(fixture, boss, presentation, Skill("Dash"));
            await Verify(fixture, boss, presentation, boss.Mobility.dashSlash);
            await Verify(fixture, boss, presentation, boss.Mobility.jumpSlam);
            await Verify(fixture, boss, presentation, Skill("Phase2_OpeningBurst"));
            await VerifyCancellationAndReuse(fixture, boss, presentation, basic, ring);
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
            {
                _report.status = "failed";
                _report.error = "Cleanup: " + exception.GetBaseException().Message;
                Debug.LogException(exception);
            }
            Time.captureDeltaTime = previousCaptureDeltaTime;
            _running = false;
            File.WriteAllText(Path.Combine(_report.directory, "report.json"), Status());
        }
    }

    private static void VerifyPrefabs()
    {
        foreach (string path in new[]
        {
            SwordRobotBossSetup.PrefabPath, BossHitReactionVariantsSetup.FinalIKPath, VariantPrefab
        })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var presentation = prefab != null ? prefab.GetComponent<SwordRobotBossPresentation>() : null;
            Check(presentation != null && presentation.burstMaterial != null,
                "The boss prefab is missing its projected floor and sparks material: " + path);
            Check(prefab.transform.Find("AttackWarning") == null,
                "The prefab still contains the old attack warning: " + path);
        }
    }

    private static MonsterSkillData Skill(string assetName)
    {
        var skill = AssetDatabase.LoadAssetAtPath<MonsterSkillData>(BossFolder + "/" + assetName + ".asset");
        Check(skill != null, "Missing authored attack: " + assetName);
        return skill;
    }

    private static EnemyAction Begin(BossMobilityVisualRegression.Fixture fixture, Boss boss, MonsterSkillData skill)
    {
        boss.CancelCurrentAction();
        fixture.Reset(boss, BossMobilityKind.DashSlash);
        var action = skill.CreateAction(boss);
        Check(action != null, "Could not plan " + skill.name);
        if (!(action is BossMobilityAction))
            action.ConfigureBossPolicy(new BossAttackPolicy { enabled = true, turnSpeed = 180f, lockFraction = .6f });
        boss.CurrentAction = action;
        boss.HasSelectedAttack = true;
        action.Enter();
        Check(action.IsActive, "Could not start " + skill.name);
        return action;
    }

    private static async Task Verify(BossMobilityVisualRegression.Fixture fixture, Boss boss,
        SwordRobotBossPresentation presentation, MonsterSkillData skill)
    {
        _report.currentCase = skill.name;
        var action = Begin(fixture, boss, skill);
        if (action is BossPhaseBurstAction opener) AddRaisedFloor(boss, opener);
        int expectedWarnings = skill is MonsterComboAttackSO combo ? combo.hits.Count : 1;
        int warnings = 0, captures = 0;
        float lastProgress = 0f;
        bool visibleBefore = false;
        double deadline = Time.realtimeSinceStartupAsDouble + 20d;
        while (action.IsActive && !action.IsFinished)
        {
            if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > deadline)
                throw new InvalidOperationException("Timed out waiting for warning and impact: " + skill.name);
            await Awaitable.NextFrameAsync();
            action.Update();
            await Awaitable.EndOfFrameAsync();
            bool visible = presentation.WarningVisible;
            if (!visible)
            {
                if (visibleBefore)
                {
                    Check(captures == CaptureThresholds.Length,
                        skill.name + " reached impact before all three fill samples were captured.");
                    Check(presentation.WarningFillProgress == 0f, skill.name + " retained its fill after impact.");
                    if (warnings == expectedWarnings) break;
                }
                visibleBefore = false;
                continue;
            }

            if (!visibleBefore)
            {
                visibleBefore = true;
                warnings++;
                captures = 0;
                lastProgress = 0f;
            }
            float progress = action is BossPhaseBurstAction ? presentation.BurstFillProgress : presentation.WarningFillProgress;
            Check(progress >= lastProgress - .001f && progress <= 1f, skill.name + " fill regressed or exceeded its range.");
            lastProgress = progress;
            if (captures >= CaptureThresholds.Length || progress < CaptureThresholds[captures]) continue;

            VerifyTiming(action, captures);
            VerifyGeometry(boss, presentation, action, warnings - 1, progress);
            string image = Capture(fixture.Camera, boss, presentation, skill.name, warnings, captures);
            _report.samples.Add(new Sample { attack = skill.name, warning = warnings, progress = progress, image = image });
            captures++;
        }

        Check(warnings == expectedWarnings && captures == CaptureThresholds.Length && !presentation.WarningVisible,
            skill.name + " did not complete each warning and hide it at impact.");
        if (action is BossPhaseBurstAction burst)
            Check(burst.BurstPlayed && presentation.BurstEffectCount == 1
                && Shape<Polyline>(boss, "PhaseBurstShockwave").isActiveAndEnabled,
                "The opener lost its impact or Shapes shockwave when the warning ended.");
        boss.CancelCurrentAction();
        await Frame();
    }

    private static void AddRaisedFloor(Boss boss, BossPhaseBurstAction burst)
    {
        var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = "Raised phase opener verification floor";
        SceneManager.MoveGameObjectToScene(platform, boss.gameObject.scene);
        platform.transform.position = burst.Center + new Vector3(burst.Radius - 1.5f, 1f, 0f);
        platform.transform.localScale = new Vector3(7f, 2f, 8f);
        Physics.SyncTransforms();
    }

    private static void VerifyTiming(EnemyAction action, int sample)
    {
        if (action is EnemyBossChargeAttack charge && action.SkillData is MonsterBossChargeAttackSO data)
        {
            if (sample == 1)
                Check(charge.IsCharging && Mathf.Approximately(charge.AttackTimeline, data.chargePoseTime),
                    "The middle charge sample did not occur during the held pose.");
            else if (sample == 2)
                Check(!charge.IsCharging && charge.AttackTimeline < data.releaseHitDelay,
                    "The last charge sample did not occur during release before impact.");
        }
        if (sample == 2 && action is BossMobilityAction { Kind: BossMobilityKind.JumpSlam } jump)
            Check(jump.IsAirborne && jump.MovementProgress > .4f,
                "The jump slam fill completed its late warning sample before the boss was descending.");
    }

    private static void VerifyGeometry(Boss boss, SwordRobotBossPresentation presentation, EnemyAction action,
        int warning, float progress)
    {
        if (action is BossPhaseBurstAction burst)
        {
            var outline = Shape<Polyline>(boss, "PhaseBurstOutline");
            Check(presentation.BurstWarningVisible && outline.isActiveAndEnabled && outline.Closed
                && presentation.GetWarningPointCount() >= 64, "The opener did not show a projected Shapes outline.");
            Vector3 perimeter = presentation.GetWarningPoint(0) - burst.Center;
            perimeter.y = 0f;
            Check(Mathf.Abs(perimeter.magnitude - burst.Radius) < .05f, "The opener outline changed its authored radius.");
            var mesh = boss.GetComponentsInChildren<MeshFilter>(true)
                .SingleOrDefault(filter => filter.name == "PhaseBurstGroundFill")?.sharedMesh;
            Check(mesh != null, "The opener lost its floor-projected fill mesh.");
            var heights = mesh.vertices.Select(vertex => vertex.y).ToArray();
            Check(heights.Max() - heights.Min() > 1.9f,
                "The opener fill did not project onto the raised floor.");
            var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                float a = heights[triangles[i]], b = heights[triangles[i + 1]], c = heights[triangles[i + 2]];
                if (Mathf.Max(a, Mathf.Max(b, c)) - Mathf.Min(a, Mathf.Min(b, c)) > .61f)
                    throw new InvalidOperationException("The opener filled the air between separate floor levels.");
            }
            Color[] colors = mesh.colors;
            int filled = colors.Count(color => color.a > .1f);
            float filledFraction = (float)filled / colors.Length;
            Check(filled > 0 && Mathf.Abs(filledFraction - progress) < .09f,
                "The opener's brighter projected rings did not advance with its charge.");
            return;
        }

        if (action.SkillData is MonsterDashAttackSO || action is BossMobilityAction { Kind: BossMobilityKind.DashSlash })
        {
            var outline = presentation.WarningCorridor;
            var fill = Shape<Shapes.Rectangle>(boss, "AttackWarningCorridorFill");
            Check(presentation.IsCorridorWarning && outline != null && outline.isActiveAndEnabled && outline.IsBorder
                && presentation.GetWarningPointCount() == 4 && fill.isActiveAndEnabled && !fill.IsBorder,
                "A dash did not show a filled Shapes corridor.");
            Check(Mathf.Abs(fill.Width - outline.Width) < .05f && Mathf.Abs(fill.Height - outline.Height * progress) < .05f,
                "A dash fill did not grow along the outlined corridor.");
            Vector3 start = outline.transform.position - outline.transform.up * (outline.Height * .5f);
            Vector3 end = outline.transform.position + outline.transform.up * (outline.Height * .5f);
            Vector3 fillStart = fill.transform.position - fill.transform.up * (fill.Height * .5f);
            Check(FlatDistance(start, fillStart) < .06f, "A dash fill did not stay anchored to its start.");
            if (action.SkillData is MonsterDashAttackSO dash)
            {
                float length = dash.dashSpeed * dash.dashDuration + dash.hitBoxRadius;
                Check(FlatDistance(start, boss.transform.position) < .05f
                    && FlatDistance(end, boss.transform.position + boss.transform.forward * length) < .05f
                    && Mathf.Abs(outline.Width - dash.hitBoxRadius * 2f) < .05f,
                    "The dash corridor lost its authored width or length.");
            }
            return;
        }

        var ring = presentation.WarningCircle;
        var disc = Shape<Disc>(boss, "AttackWarningCircleFill");
        Check(!presentation.IsCorridorWarning && ring != null && ring.isActiveAndEnabled && ring.Type == DiscType.Ring
            && presentation.GetWarningPointCount() >= 64 && disc.isActiveAndEnabled && disc.Type == DiscType.Disc,
            "A circular attack did not show a filled Shapes circle.");
        Vector3 center = boss.transform.position;
        Vector3 offset = Vector3.zero;
        float radius;
        switch (action.SkillData)
        {
            case MonsterDefaultAttackSO basic:
                radius = basic.hitBoxRadius; offset = basic.hitBoxOffset; break;
            case MonsterBossChargeAttackSO charge:
                radius = charge.hitBoxRadius; offset = charge.hitBoxOffset; break;
            case MonsterComboAttackSO combo:
                radius = combo.hitBoxRadius; offset = combo.hits[warning].hitBoxOffset; break;
            case BossMobilitySkillSO jump when action is BossMobilityAction mobility:
                radius = jump.slamRadius; center = mobility.Destination; break;
            default:
                throw new InvalidOperationException("No circular warning fixture for " + action.SkillData.name);
        }
        offset.y = 0f;
        center += boss.transform.rotation * offset + Vector3.up * FloorLift;
        Check(Vector3.Distance(center, ring.transform.position) < .08f && Mathf.Abs(ring.Radius - radius) < .05f,
            "A circular attack lost its authored center or radius.");
        Check(Mathf.Abs(disc.Radius - radius * progress) < .05f && FlatDistance(disc.transform.position, center) < .05f,
            "A circular fill did not grow from its outlined center.");
    }

    private static async Task VerifyCancellationAndReuse(BossMobilityVisualRegression.Fixture fixture, Boss boss,
        SwordRobotBossPresentation presentation, MonsterSkillData skill, Disc initialOutline)
    {
        _report.currentCase = "cancellation and reuse";
        var action = Begin(fixture, boss, skill);
        await Awaitable.NextFrameAsync();
        action.Update();
        await Awaitable.EndOfFrameAsync();
        Check(presentation.WarningVisible && presentation.WarningCircle == initialOutline
            && presentation.WarningFillProgress > 0f && presentation.WarningFillProgress < .3f,
            "Reentering a circular attack did not reuse its outline and restart the fill.");
        boss.CancelCurrentAction();
        await Frame();
        Check(!presentation.WarningVisible && presentation.WarningFillProgress == 0f
            && !initialOutline.isActiveAndEnabled && !Shape<Disc>(boss, "AttackWarningCircleFill").isActiveAndEnabled,
            "Cancelling a circular attack left a visible warning.");
    }

    private static T Shape<T>(Boss boss, string name) where T : ShapeRenderer
    {
        var shape = boss.GetComponentsInChildren<T>(true).SingleOrDefault(component => component.name == name);
        Check(shape != null, "Missing Shapes component: " + name);
        return shape;
    }

    private static string Capture(Camera camera, Boss boss, SwordRobotBossPresentation presentation,
        string name, int warning, int frame)
    {
        var bounds = new Bounds(presentation.GetWarningPoint(0), Vector3.zero);
        for (int i = 1; i < presentation.GetWarningPointCount(); i++)
            bounds.Encapsulate(presentation.GetWarningPoint(i));
        bounds.Encapsulate(new Bounds(boss.transform.position + Vector3.up * 1.5f, new Vector3(3f, 3f, 3f)));
        float distance = bounds.extents.magnitude / Mathf.Sin(camera.fieldOfView * .5f * Mathf.Deg2Rad) * 1.12f;
        camera.transform.position = bounds.center + new Vector3(.5f, .86f, .7f).normalized * distance;
        camera.transform.LookAt(bounds.center);
        float previousFarClip = camera.farClipPlane;
        camera.farClipPlane = Mathf.Max(previousFarClip, distance + bounds.extents.magnitude + 1f);
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(512, 512, 24, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(512, 512, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            texture.Apply();
            string path = Path.Combine(_report.directory, $"{name}_{warning}_{frame + 1}.png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            return path;
        }
        finally
        {
            camera.targetTexture = null;
            camera.farClipPlane = previousFarClip;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.Destroy(texture);
        }
    }

    private static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

    private static async Task Frame()
    {
        await Awaitable.NextFrameAsync();
        await Awaitable.EndOfFrameAsync();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _report.checks++;
    }
}
