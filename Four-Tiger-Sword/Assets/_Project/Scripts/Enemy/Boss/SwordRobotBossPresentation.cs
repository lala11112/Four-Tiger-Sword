using System.Collections.Generic;
using Shapes;
using StudioBaekho;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Presentation only; the action remains the authority for timing and damage.</summary>
[RequireComponent(typeof(Boss))]
public sealed class SwordRobotBossPresentation : MonoBehaviour
{
    public Material burstMaterial;
    public TrailRenderer bladeTrail;
    public Transform visualPivot;
    public MonsterDefaultAttackSO sweepSkill;
    public Color chargeColor = new Color(1f, .65f, .08f);
    public Color attackColor = new Color(1f, .15f, .06f);
    private Boss _boss;
    private const int OutlineSegments = 128;
    private enum WarningKind { None, Circle, Corridor, Projected }
    private WarningKind _warningKind;
    private Disc _circleBackground, _circleFill, _circleOutline;
    private Rectangle _corridorBackground, _corridorFill, _corridorOutline;
    private Polyline _burstOutline;
    private readonly RaycastHit[] _floorHits = new RaycastHit[32];
    private readonly HashSet<Transform> _ground = new HashSet<Transform>();
    private BossPhaseBurstAction _burst;
    private Mesh _burstMesh;
    private MeshRenderer _burstFill;
    private Polyline _burstWave;
    private ParticleSystem _burstSparks;
    private Transform _burstSurface;
    private Vector3[] _surfacePoints;
    private float[] _surfaceRadii;
    private Color[] _surfaceColors;
    private float _blastRemaining;
    private bool _burstExploded, _hasRoomGround;
    private const int BurstSegments = 96, BurstRings = 16;
    private const float BlastSeconds = .5f;
    public Disc WarningCircle => _circleOutline;
    public Rectangle WarningCorridor => _corridorOutline;
    public bool IsCorridorWarning => _warningKind == WarningKind.Corridor;
    public float WarningFillProgress { get; private set; }
    public bool WarningVisible => isActiveAndEnabled && (_warningKind == WarningKind.Circle
        ? _circleOutline != null && _circleOutline.isActiveAndEnabled
        : _warningKind == WarningKind.Corridor ? _corridorOutline != null && _corridorOutline.isActiveAndEnabled
        : _warningKind == WarningKind.Projected && _burstOutline != null && _burstOutline.isActiveAndEnabled);
    public float BurstFillProgress { get; private set; }
    public bool BurstWarningVisible => _burst != null && WarningVisible && _warningKind == WarningKind.Projected
        && _boss != null && _boss.CurrentAction == _burst && _burst.IsActive
        && _burst.WarningStarted && _burst.BossStage == BossAttackStage.Startup;
    public int BurstEffectCount { get; private set; }

    private void Awake()
    {
        _boss = GetComponent<Boss>();
        Clear();
    }
    private void LateUpdate()
    {
        var action = _boss.CurrentAction;
        var mobility = action as BossMobilityAction;
        bool active = action != null && action.IsActive && !action.IsFinished && !action.WasParried
            && _boss.isActiveAndEnabled && !_boss.IsDead && !_boss.IsTransitioning && !_boss.IsGroggy
            && !_boss.PendingGroggy && !_boss.IsHurt && !_boss.IsRoot && !_boss.IsKnockbacking && !_boss.IsSilenced;
        bool attacking = active && (mobility == null || mobility.DealsDamage && !mobility.Failed);
        UpdateBurst(action as BossPhaseBurstAction, active);
        if (visualPivot != null)
        {
            float spin = active && action.SkillData == sweepSkill && sweepSkill.hitDuration > 0f
                ? Mathf.Clamp01((action.AttackTimeline - sweepSkill.hitStartTime) / sweepSkill.hitDuration) : 0f;
            visualPivot.localRotation = Quaternion.Euler(0f, 360f * spin, 0f);
        }
        if (bladeTrail != null)
        {
            bladeTrail.emitting = attacking && action.BossStage == BossAttackStage.Active
                && (mobility == null || mobility.Kind == BossMobilityKind.DashSlash);
            if (active && mobility != null && !mobility.DealsDamage) bladeTrail.Clear();
        }
        bool showWarning = attacking && (action.BossStage == BossAttackStage.Startup
                || mobility != null && mobility.Kind == BossMobilityKind.JumpSlam
                    && action.BossStage == BossAttackStage.Active)
                && (!(action is BossPhaseBurstAction phaseBurst) || phaseBurst.WarningStarted);
        if (showWarning) DrawWarning(action);
        else HideWarning();
        if (!active && !_boss.IsDead && !_boss.IsTransitioning && !_boss.IsHurt && !_boss.IsGroggy
            && !_boss.PendingGroggy && !_boss.IsRoot && !_boss.IsKnockbacking && _boss.Animator != null)
        {
            var animator = _boss.Animator;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            // Move is a directional blend tree with an idle at the origin.
            if (!state.IsName("Move") && !animator.IsInTransition(0)) animator.CrossFadeInFixedTime("Move", .15f);
        }
    }
    private void DrawWarning(EnemyAction action)
    {
        if (action is BossPhaseBurstAction burst)
        {
            EnsureBurstResources();
            SetWarningKind(WarningKind.Projected);
            WarningFillProgress = burst.ChargeProgress;
            _burstOutline.Color = Color.Lerp(chargeColor, new Color(1f, .22f, .04f), burst.ChargeProgress);
            DrawGroundCircle(_burstOutline, burst.Center, burst.Radius);
            return;
        }
        Vector3 origin = transform.position + Vector3.up * .045f;
        Color color = action is EnemyBossChargeAttack ? chargeColor : attackColor;
        if (action is BossMobilityAction mobility && mobility.SkillData is BossMobilitySkillSO movement)
        {
            if (mobility.Kind == BossMobilityKind.DashSlash)
            {
                DrawCorridor(mobility.StartPosition + Vector3.up * .045f,
                    mobility.Destination + Vector3.up * .045f, movement.hitBoxRadius, mobility.WarningProgress, color);
            }
            else if (mobility.Kind == BossMobilityKind.JumpSlam)
                DrawCircle(mobility.Destination + Vector3.up * .045f, movement.slamRadius, mobility.WarningProgress, color);
            else HideWarning();
            return;
        }
        if (action.SkillData is MonsterDashAttackSO dash)
        {
            float length = dash.dashSpeed * dash.dashDuration + dash.hitBoxRadius;
            DrawCorridor(origin, origin + transform.forward * length, dash.hitBoxRadius,
                Progress(action.AttackTimeline, dash.startupTime), color);
            return;
        }
        float radius = 2f;
        float progress = 0f;
        Vector3 offset = Vector3.zero;
        if (action.SkillData is MonsterDefaultAttackSO basic)
        { radius = basic.hitBoxRadius; offset = basic.hitBoxOffset; progress = Progress(action.AttackTimeline, basic.hitStartTime); }
        else if (action is EnemyBossChargeAttack chargeAction && action.SkillData is MonsterBossChargeAttackSO charge)
        { radius = charge.hitBoxRadius; offset = charge.hitBoxOffset; progress = chargeAction.WarningProgress; }
        else if (action is EnemyComboAttack comboAction && action.SkillData is MonsterComboAttackSO combo)
        {
            int index = comboAction.CurrentWarningHitIndex;
            if (index < 0 || index >= combo.hits.Count) { HideWarning(); return; }
            radius = combo.hitBoxRadius; offset = combo.hits[index].hitBoxOffset; progress = comboAction.WarningProgress;
        }
        else { HideWarning(); return; }
        offset.y = 0f;
        origin += transform.rotation * offset;
        DrawCircle(origin, radius, progress, color);
    }

    private static float Progress(float elapsed, float duration) => duration > 0f ? Mathf.Clamp01(elapsed / duration) : 0f;

    private void DrawCircle(Vector3 origin, float radius, float progress, Color color)
    {
        if (_circleOutline == null)
        {
            _circleBackground = CreateShape<Disc>("AttackWarningCircleBackground", 0);
            _circleFill = CreateShape<Disc>("AttackWarningCircleFill", 1);
            _circleOutline = CreateShape<Disc>("AttackWarningCircleOutline", 2);
            _circleOutline.Type = DiscType.Ring;
            _circleOutline.Thickness = .055f;
        }
        SetWarningKind(WarningKind.Circle);
        WarningFillProgress = Mathf.Clamp01(progress);
        Quaternion rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
        _circleBackground.transform.SetPositionAndRotation(origin, rotation);
        _circleFill.transform.SetPositionAndRotation(origin, rotation);
        _circleOutline.transform.SetPositionAndRotation(origin, rotation);
        _circleBackground.Radius = _circleOutline.Radius = radius;
        _circleFill.Radius = radius * WarningFillProgress;
        _circleBackground.Color = WithAlpha(color, .055f);
        _circleFill.Color = WithAlpha(color, .25f);
        _circleOutline.Color = color;
        _circleFill.enabled = WarningFillProgress > 0f;
    }
    private void DrawCorridor(Vector3 start, Vector3 end, float halfWidth, float progress, Color color)
    {
        Vector3 direction = end - start;
        if (direction.sqrMagnitude < .0001f) { HideWarning(); return; }
        if (_corridorOutline == null)
        {
            _corridorBackground = CreateShape<Rectangle>("AttackWarningCorridorBackground", 0);
            _corridorFill = CreateShape<Rectangle>("AttackWarningCorridorFill", 1);
            _corridorOutline = CreateShape<Rectangle>("AttackWarningCorridorOutline", 2);
            _corridorOutline.Type = Rectangle.RectangleType.HardBorder;
            _corridorOutline.Thickness = .055f;
        }
        SetWarningKind(WarningKind.Corridor);
        WarningFillProgress = Mathf.Clamp01(progress);
        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
        Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(direction, side).normalized, direction);
        _corridorBackground.transform.SetPositionAndRotation((start + end) * .5f, rotation);
        _corridorOutline.transform.SetPositionAndRotation((start + end) * .5f, rotation);
        _corridorFill.transform.SetPositionAndRotation(start + direction * (WarningFillProgress * .5f), rotation);
        _corridorBackground.Width = _corridorFill.Width = _corridorOutline.Width = halfWidth * 2f;
        _corridorBackground.Height = _corridorOutline.Height = direction.magnitude;
        _corridorFill.Height = direction.magnitude * WarningFillProgress;
        _corridorBackground.Color = WithAlpha(color, .055f);
        _corridorFill.Color = WithAlpha(color, .25f);
        _corridorOutline.Color = color;
        _corridorFill.enabled = WarningFillProgress > 0f;
    }

    private T CreateShape<T>(string name, int order) where T : ShapeRenderer
    {
        var host = new GameObject(name);
        host.transform.SetParent(transform, false);
        var shape = host.AddComponent<T>();
        shape.BlendMode = ShapesBlendMode.Transparent;
        shape.SortingOrder = order;
        shape.enabled = false;
        var renderer = host.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return shape;
    }

    private Polyline CreateGroundOutline(string name, float thickness)
    {
        var line = CreateShape<Polyline>(name, 2);
        line.Geometry = PolylineGeometry.Billboard;
        line.Closed = true;
        line.Thickness = thickness;
        line.points.Clear();
        for (int i = 0; i < OutlineSegments; i++) line.AddPoint(Vector3.zero);
        return line;
    }

    private static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }

    private void SetWarningKind(WarningKind kind)
    {
        _warningKind = kind;
        SetVisible(_circleBackground, kind == WarningKind.Circle);
        SetVisible(_circleFill, kind == WarningKind.Circle);
        SetVisible(_circleOutline, kind == WarningKind.Circle);
        SetVisible(_corridorBackground, kind == WarningKind.Corridor);
        SetVisible(_corridorFill, kind == WarningKind.Corridor);
        SetVisible(_corridorOutline, kind == WarningKind.Corridor);
        SetVisible(_burstOutline, kind == WarningKind.Projected);
    }

    private static void SetVisible(ShapeRenderer shape, bool visible)
    { if (shape != null && shape.enabled != visible) shape.enabled = visible; }

    private void HideWarning()
    {
        SetWarningKind(WarningKind.None);
        WarningFillProgress = 0f;
    }

    /// <summary>World footprint sampled from the actual Shapes geometry, for framing and diagnostics.</summary>
    public int GetWarningPointCount() => !WarningVisible ? 0 : IsCorridorWarning ? 4 : OutlineSegments;

    public Vector3 GetWarningPoint(int index)
    {
        if (index < 0 || index >= GetWarningPointCount()) throw new System.ArgumentOutOfRangeException(nameof(index));
        if (_warningKind == WarningKind.Projected)
            return _burstOutline.transform.TransformPoint(_burstOutline[index].point);
        if (IsCorridorWarning)
        {
            float x = index < 2 ? -.5f : .5f;
            float y = index == 0 || index == 3 ? -.5f : .5f;
            return _corridorOutline.transform.TransformPoint(new Vector3(x * _corridorOutline.Width, y * _corridorOutline.Height, 0f));
        }
        float angle = index * Mathf.PI * 2f / OutlineSegments;
        return _circleOutline.transform.TransformPoint(new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f) * _circleOutline.Radius);
    }
    private void Clear()
    {
        HideWarning();
        if (bladeTrail != null) { bladeTrail.emitting = false; bladeTrail.Clear(); }
        if (visualPivot != null) visualPivot.localRotation = Quaternion.identity;
        if (_burstFill != null) _burstFill.enabled = false;
        if (_burstWave != null) _burstWave.enabled = false;
        if (_burstSparks != null) _burstSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _burst = null;
        _blastRemaining = 0f;
        BurstFillProgress = 0f;
    }

    private void UpdateBurst(BossPhaseBurstAction burst, bool active)
    {
        if (_boss.IsDead || _boss.IsTransitioning || !_boss.isActiveAndEnabled
            || _boss.IsGroggy || _boss.PendingGroggy || _boss.IsHurt || _boss.IsRoot
            || _boss.IsKnockbacking || _boss.IsSilenced)
        {
            if (_burstFill != null) _burstFill.enabled = false;
            if (_burstWave != null) _burstWave.enabled = false;
            if (_burstSparks != null) _burstSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _blastRemaining = 0f;
            BurstFillProgress = 0f;
            _burst = null;
            return;
        }
        if (burst != null && active && burst.WarningStarted && burst != _burst)
        {
            _burst = burst;
            _burstExploded = false;
            _blastRemaining = 0f;
            BuildBurstSurface(burst.Center, burst.Radius);
        }
        if (_burst == null || _burstFill == null) return;
        // The warning remains on the floor while its owner descends from the air.
        _burstSurface.SetPositionAndRotation(_burst.Center, Quaternion.identity);
        if (_blastRemaining > 0f) _blastRemaining = Mathf.Max(0f, _blastRemaining - _boss.ActionDeltaTime);
        bool charging = burst == _burst && active && burst.WarningStarted && burst.BossStage == BossAttackStage.Startup;
        BurstFillProgress = charging ? burst.ChargeProgress : 0f;
        if (burst == _burst && active && burst.BurstPlayed && !_burstExploded)
        {
            _burstExploded = true;
            _blastRemaining = BlastSeconds;
            BurstEffectCount++;
            EmitBurstSparks();
        }
        bool blasting = _blastRemaining > 0f;
        _burstFill.enabled = charging || blasting;
        _burstWave.enabled = blasting;
        if (charging) ColorBurstSurface(BurstFillProgress, false, 1f);
        else if (blasting)
        {
            float strength = _blastRemaining / BlastSeconds;
            ColorBurstSurface(1f, true, strength);
            _burstWave.Color = new Color(1f, .8f, .3f, strength);
            _burstWave.Thickness = Mathf.Lerp(.08f, .45f, strength);
            DrawGroundCircle(_burstWave, _burst.Center, _burst.Radius * Mathf.Lerp(1f, .8f, strength));
        }
        if (!charging && !blasting && (burst != _burst || !active)) _burst = null;
    }

    private void EnsureBurstResources()
    {
        if (_burstFill != null) return;
        _burstOutline = CreateGroundOutline("PhaseBurstOutline", .13f);
        var surface = new GameObject("PhaseBurstGroundFill");
        _burstSurface = surface.transform;
        _burstSurface.SetParent(transform, false);
        _burstMesh = new Mesh { name = "Phase burst projected floor" };
        _burstMesh.MarkDynamic();
        surface.AddComponent<MeshFilter>().sharedMesh = _burstMesh;
        _burstFill = surface.AddComponent<MeshRenderer>();
        _burstFill.sharedMaterial = burstMaterial;
        _burstFill.shadowCastingMode = ShadowCastingMode.Off;
        _burstFill.receiveShadows = false;
        _burstFill.enabled = false;

        _burstWave = CreateGroundOutline("PhaseBurstShockwave", .13f);

        var sparks = new GameObject("PhaseBurstSparks");
        sparks.transform.SetParent(transform, false);
        _burstSparks = sparks.AddComponent<ParticleSystem>();
        var main = _burstSparks.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = .55f;
        main.startSpeed = 0f;
        main.startSize = .2f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1f;
        main.maxParticles = 96;
        var emission = _burstSparks.emission;
        emission.enabled = false;
        var shape = _burstSparks.shape;
        shape.enabled = false;
        _burstSparks.GetComponent<ParticleSystemRenderer>().sharedMaterial = burstMaterial;
        _burstSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void BuildBurstSurface(Vector3 center, float radius)
    {
        EnsureBurstResources();
        _burstSurface.SetPositionAndRotation(center, Quaternion.identity);
        _ground.Clear();
        var room = GetComponentInParent<BaekhoMapModule>();
        if (room != null && room.groundObjects != null)
            foreach (var ground in room.groundObjects) if (ground != null) _ground.Add(ground.transform);
        _hasRoomGround = _ground.Count > 0;
        int count = 1 + BurstSegments * BurstRings;
        _surfacePoints = new Vector3[count];
        _surfaceRadii = new float[count];
        _surfaceColors = new Color[count];
        var local = new Vector3[count];
        _surfacePoints[0] = ProjectToFloor(center, center.y);
        local[0] = _burstSurface.InverseTransformPoint(_surfacePoints[0]);
        for (int ring = 1; ring <= BurstRings; ring++)
            for (int segment = 0; segment < BurstSegments; segment++)
            {
                int index = 1 + (ring - 1) * BurstSegments + segment;
                float fraction = (float)ring / BurstRings;
                float angle = segment * Mathf.PI * 2f / BurstSegments;
                Vector3 point = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius * fraction;
                _surfacePoints[index] = ProjectToFloor(point, center.y);
                _surfaceRadii[index] = fraction;
                local[index] = _burstSurface.InverseTransformPoint(_surfacePoints[index]);
            }
        var triangles = new List<int>(BurstSegments * BurstRings * 6);
        for (int segment = 0; segment < BurstSegments; segment++)
            SurfaceTriangle(triangles, 0, 1 + segment, 1 + (segment + 1) % BurstSegments);
        for (int ring = 1; ring < BurstRings; ring++)
            for (int segment = 0; segment < BurstSegments; segment++)
            {
                int next = (segment + 1) % BurstSegments;
                int a = 1 + (ring - 1) * BurstSegments + segment;
                int b = 1 + (ring - 1) * BurstSegments + next;
                int c = 1 + ring * BurstSegments + segment;
                int d = 1 + ring * BurstSegments + next;
                SurfaceTriangle(triangles, a, c, d);
                SurfaceTriangle(triangles, a, d, b);
            }
        _burstMesh.Clear();
        _burstMesh.vertices = local;
        _burstMesh.triangles = triangles.ToArray();
        _burstMesh.colors = _surfaceColors;
        _burstMesh.RecalculateBounds();
    }

    private void SurfaceTriangle(List<int> triangles, int a, int b, int c)
    {
        float low = Mathf.Min(_surfacePoints[a].y, _surfacePoints[b].y, _surfacePoints[c].y);
        float high = Mathf.Max(_surfacePoints[a].y, _surfacePoints[b].y, _surfacePoints[c].y);
        // A 2 m platform must have its own flat warning rather than a triangle over empty space.
        if (high - low > .6f) return;
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
    }

    private void ColorBurstSurface(float progress, bool explosion, float strength)
    {
        for (int i = 0; i < _surfaceColors.Length; i++)
        {
            bool filled = _surfaceRadii[i] <= progress;
            _surfaceColors[i] = explosion ? new Color(1f, .62f, .12f, .45f * strength)
                : filled ? new Color(1f, .2f, .025f, .25f) : new Color(1f, .5f, .035f, .055f);
        }
        _burstMesh.colors = _surfaceColors;
    }

    private void EmitBurstSparks()
    {
        _burstSparks.Play();
        for (int i = 0; i < 64; i++)
        {
            float angle = i * Mathf.PI * 2f / 64;
            int ring = 5 + i % (BurstRings - 5);
            int segment = Mathf.RoundToInt(i * (float)BurstSegments / 64) % BurstSegments;
            Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            _burstSparks.Emit(new ParticleSystem.EmitParams {
                position = _surfacePoints[1 + ring * BurstSegments + segment] + Vector3.up * .12f,
                velocity = direction * 2f + Vector3.up * (2f + i % 4 * .4f),
                startColor = new Color(1f, .6f + i % 3 * .1f, .1f, .9f),
                startSize = .12f + i % 3 * .05f,
                startLifetime = .4f + i % 3 * .08f
            }, 1);
        }
    }

    private void DrawGroundCircle(Polyline line, Vector3 center, float radius)
    {
        for (int i = 0; i < OutlineSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / OutlineSegments;
            Vector3 point = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            line.SetPointPosition(i, line.transform.InverseTransformPoint(ProjectToFloor(point, center.y)));
        }
    }

    private Vector3 ProjectToFloor(Vector3 point, float fallbackY)
    {
        int count = Physics.RaycastNonAlloc(new Vector3(point.x, fallbackY + 8f, point.z), Vector3.down,
            _floorHits, 16f, ~0, QueryTriggerInteraction.Ignore);
        float height = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = _floorHits[i];
            if (hit.collider == null || hit.normal.y < .5f || hit.collider.transform.IsChildOf(transform)
                || hit.collider.GetComponentInParent<Enemy>() != null
                || hit.collider.GetComponentInParent<PlayerStatManager>() != null) continue;
            if (_hasRoomGround)
            {
                bool ground = false;
                for (Transform candidate = hit.collider.transform; candidate != null; candidate = candidate.parent)
                    if (_ground.Contains(candidate)) { ground = true; break; }
                if (!ground) continue;
            }
            height = Mathf.Max(height, hit.point.y);
        }
        point.y = (float.IsNegativeInfinity(height) ? fallbackY : height) + .06f;
        return point;
    }

    private void OnDisable() => Clear();
    private void OnDestroy() { if (_burstMesh != null) Destroy(_burstMesh); }
}
