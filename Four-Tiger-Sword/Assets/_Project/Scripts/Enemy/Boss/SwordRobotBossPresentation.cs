using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Presentation only; the action remains the authority for timing and damage.</summary>
[RequireComponent(typeof(Boss))]
public sealed class SwordRobotBossPresentation : MonoBehaviour
{
    public LineRenderer warning;
    public TrailRenderer bladeTrail;
    public Transform visualPivot;
    public MonsterDefaultAttackSO sweepSkill;
    public Color chargeColor = new Color(1f, .65f, .08f);
    public Color attackColor = new Color(1f, .15f, .06f);
    private Boss _boss;
    private readonly Vector3[] _points = new Vector3[129];
    private readonly RaycastHit[] _floorHits = new RaycastHit[32];
    private readonly HashSet<Transform> _ground = new HashSet<Transform>();
    private BossPhaseBurstAction _burst;
    private Mesh _burstMesh;
    private MeshRenderer _burstFill;
    private LineRenderer _burstWave;
    private ParticleSystem _burstSparks;
    private Transform _burstSurface;
    private Vector3[] _surfacePoints;
    private float[] _surfaceRadii;
    private Color[] _surfaceColors;
    private float _blastRemaining, _warningWidth;
    private bool _burstExploded, _hasRoomGround;
    private const int BurstSegments = 96, BurstRings = 16;
    private const float BlastSeconds = .5f;
    public float BurstFillProgress { get; private set; }
    public bool BurstWarningVisible => _burst != null && warning != null && warning.enabled
        && _boss != null && _boss.CurrentAction == _burst && _burst.IsActive
        && _burst.WarningStarted && _burst.BossStage == BossAttackStage.Startup;
    public int BurstEffectCount { get; private set; }

    private void Awake()
    {
        _boss = GetComponent<Boss>();
        _warningWidth = warning != null ? warning.widthMultiplier : .055f;
        Clear();
    }
    private void LateUpdate()
    {
        var action = _boss.CurrentAction;
        var mobility = action as BossMobilityAction;
        bool active = action != null && action.IsActive && !action.IsFinished && !_boss.IsDead
            && !_boss.IsTransitioning && !_boss.IsGroggy && !_boss.PendingGroggy;
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
        if (warning != null)
        {
            warning.widthMultiplier = action is BossPhaseBurstAction ? .13f : _warningWidth;
            warning.enabled = attacking && (action.BossStage == BossAttackStage.Startup
                || mobility != null && mobility.Kind == BossMobilityKind.JumpSlam
                    && action.BossStage == BossAttackStage.Active)
                && (!(action is BossPhaseBurstAction phaseBurst) || phaseBurst.WarningStarted);
            if (warning.enabled) DrawWarning(action);
        }
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
            warning.startColor = warning.endColor = Color.Lerp(chargeColor, new Color(1f, .22f, .04f), burst.ChargeProgress);
            DrawGroundCircle(warning, burst.Center, burst.Radius);
            return;
        }
        Vector3 origin = transform.position + Vector3.up * .045f;
        Color color = action is EnemyBossChargeAttack ? chargeColor : attackColor;
        warning.startColor = warning.endColor = color;
        if (action is BossMobilityAction mobility && mobility.SkillData is BossMobilitySkillSO movement)
        {
            if (mobility.Kind == BossMobilityKind.DashSlash)
            {
                DrawCorridor(mobility.StartPosition + Vector3.up * .045f,
                    mobility.Destination + Vector3.up * .045f, movement.hitBoxRadius);
            }
            else if (mobility.Kind == BossMobilityKind.JumpSlam)
                DrawCircle(mobility.Destination + Vector3.up * .045f, movement.slamRadius);
            return;
        }
        if (action.SkillData is MonsterDashAttackSO dash)
        {
            float length = dash.dashSpeed * dash.dashDuration + dash.hitBoxRadius;
            DrawCorridor(origin, origin + transform.forward * length, dash.hitBoxRadius);
            return;
        }
        float radius = 2f;
        Vector3 offset = Vector3.zero;
        if (action.SkillData is MonsterDefaultAttackSO basic) { radius = basic.hitBoxRadius; offset = basic.hitBoxOffset; }
        else if (action.SkillData is MonsterBossChargeAttackSO charge) { radius = charge.hitBoxRadius; offset = charge.hitBoxOffset; }
        else if (action.SkillData is MonsterComboAttackSO combo) { radius = combo.hitBoxRadius; if (combo.hits.Count > 0) offset = combo.hits[0].hitBoxOffset; }
        offset.y = 0f;
        origin += transform.rotation * offset;
        DrawCircle(origin, radius);
    }
    private void DrawCircle(Vector3 origin, float radius)
    {
        for (int i = 0; i < _points.Length; i++)
        {
            float angle = i * Mathf.PI * 2f / (_points.Length - 1);
            _points[i] = origin + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
        }
        warning.positionCount = _points.Length;
        warning.SetPositions(_points);
    }
    private void DrawCorridor(Vector3 start, Vector3 end, float width)
    {
        Vector3 direction = end - start;
        direction.y = 0f;
        if (direction.sqrMagnitude < .0001f) direction = transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, direction.normalized) * width;
        warning.positionCount = 5;
        warning.SetPosition(0, start - side);
        warning.SetPosition(1, end - side);
        warning.SetPosition(2, end + side);
        warning.SetPosition(3, start + side);
        warning.SetPosition(4, start - side);
    }
    private void Clear()
    {
        if (warning != null) warning.enabled = false;
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
        if (_boss.IsDead || _boss.IsTransitioning)
        {
            if (_burstFill != null) _burstFill.enabled = false;
            if (_burstWave != null) _burstWave.enabled = false;
            if (_burstSparks != null) _burstSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _blastRemaining = 0f;
            BurstFillProgress = 0f;
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
            _burstWave.startColor = _burstWave.endColor = new Color(1f, .8f, .3f, strength);
            _burstWave.widthMultiplier = Mathf.Lerp(.08f, .45f, strength);
            DrawGroundCircle(_burstWave, _burst.Center, _burst.Radius * Mathf.Lerp(1f, .8f, strength));
        }
    }

    private void EnsureBurstResources()
    {
        if (_burstFill != null) return;
        var surface = new GameObject("PhaseBurstGroundFill");
        _burstSurface = surface.transform;
        _burstSurface.SetParent(transform, false);
        _burstMesh = new Mesh { name = "Phase burst projected floor" };
        _burstMesh.MarkDynamic();
        surface.AddComponent<MeshFilter>().sharedMesh = _burstMesh;
        _burstFill = surface.AddComponent<MeshRenderer>();
        _burstFill.sharedMaterial = warning.sharedMaterial;
        _burstFill.shadowCastingMode = ShadowCastingMode.Off;
        _burstFill.receiveShadows = false;
        _burstFill.enabled = false;

        var wave = new GameObject("PhaseBurstShockwave");
        wave.transform.SetParent(transform, false);
        _burstWave = wave.AddComponent<LineRenderer>();
        _burstWave.sharedMaterial = warning.sharedMaterial;
        _burstWave.useWorldSpace = true;
        _burstWave.numCornerVertices = 3;
        _burstWave.shadowCastingMode = ShadowCastingMode.Off;
        _burstWave.receiveShadows = false;
        _burstWave.enabled = false;

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
        _burstSparks.GetComponent<ParticleSystemRenderer>().sharedMaterial = warning.sharedMaterial;
        _burstSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void BuildBurstSurface(Vector3 center, float radius)
    {
        if (warning == null) return;
        EnsureBurstResources();
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

    private void DrawGroundCircle(LineRenderer line, Vector3 center, float radius)
    {
        line.positionCount = _points.Length;
        for (int i = 0; i < _points.Length; i++)
        {
            float angle = i * Mathf.PI * 2f / (_points.Length - 1);
            Vector3 point = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            _points[i] = ProjectToFloor(point, center.y);
        }
        line.SetPositions(_points);
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
