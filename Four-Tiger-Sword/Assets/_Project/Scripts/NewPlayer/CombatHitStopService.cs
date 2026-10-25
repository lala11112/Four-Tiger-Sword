using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Composes owned world slow motion and hit stops without resuming an external pause.</summary>
[DefaultExecutionOrder(-300)]
public sealed class CombatHitStopService : MonoBehaviour
{
    public sealed class WorldScaleLease : System.IDisposable
    {
        private CombatHitStopService _service;
        internal readonly Object Owner;
        internal readonly float Multiplier;

        internal WorldScaleLease(CombatHitStopService service, Object owner, float multiplier)
        { _service = service; Owner = owner; Multiplier = multiplier; }

        public bool IsValid => _service != null && _service.Validate(this);

        public void Dispose()
        {
            var service = _service;
            _service = null;
            if (service != null) service.Release(this);
        }

        internal void Invalidate() => _service = null;
    }

    private sealed class Stop
    {
        public Object Owner;
        public double Expires;
        public float Scale;
        public double Started;
    }
    private static CombatHitStopService _instance;
    private readonly List<Stop> _stops = new();
    private readonly List<WorldScaleLease> _worldScales = new();
    private float _previousScale = 1f, _appliedScale = 1f;
    private bool _ownsWorldTime;
    /// <summary>Changes for every explicit external write, including another owner writing zero again.</summary>
    public static ulong ExternalWorldTimeRevision { get; private set; }

    /// <summary>Owned modal slowdown only; realtime combat delays ignore ordinary hit stop.</summary>
    public static float ModalTimeMultiplier
    {
        get
        {
            if (_instance == null) return Time.timeScale <= 0f ? 0f : 1f;
            _instance.Refresh();
            float baseScale = _instance._ownsWorldTime ? _instance._previousScale : Time.timeScale;
            return baseScale <= 0f ? 0f : _instance.WorldMultiplier;
        }
    }

    public static WorldScaleLease AcquireWorldScale(Object owner, float multiplier)
    {
        if (!OwnerIsAvailable(owner) || !KnockbackMotion.IsFinite(multiplier) || multiplier < 0f || multiplier > 1f)
            return new WorldScaleLease(null, owner, multiplier);
        var service = Instance;
        service.Refresh();
        if (!service.CaptureWorldTime()) return new WorldScaleLease(null, owner, multiplier);
        var lease = new WorldScaleLease(service, owner, multiplier);
        service._worldScales.Add(lease);
        service.ApplyScale();
        return lease;
    }

    private static CombatHitStopService Instance
    {
        get
        {
            if (_instance != null) return _instance;
            var host = new GameObject("Combat Hit Stop Service") { hideFlags = HideFlags.HideAndDontSave };
            if (Application.isPlaying) DontDestroyOnLoad(host);
            return _instance = host.AddComponent<CombatHitStopService>();
        }
    }

    public static void Apply(Object owner, Component attacker, Component victim, HitStopSettings settings,
        bool includeAttacker = true)
    {
        if (settings == null || owner == null) return;
        if (settings.Scope == HitStopScope.Global)
        {
            if (includeAttacker) Instance.Request(owner, settings.HitStopDuration, settings.HitStopTimeScale, settings.Stacking);
            return;
        }
        if (includeAttacker && (settings.Scope == HitStopScope.Attacker || settings.Scope == HitStopScope.Both))
            CombatActorClock.GetOrCreate(attacker)?.Request(owner, settings.HitStopDuration, settings.HitStopTimeScale, settings.Stacking, settings.Scope);
        if (settings.Scope == HitStopScope.Victim || settings.Scope == HitStopScope.Both)
            CombatActorClock.GetOrCreate(victim)?.Request(owner, settings.HitStopDuration, settings.HitStopTimeScale, settings.Stacking, settings.Scope);
    }

    public static void ReleaseOwner(Object owner)
    {
        CombatActorClock.ReleaseOwner(owner);
        if (_instance == null) return;
        _instance.ObserveExternalScale();
        _instance.RemoveWorldScales(lease => lease.Owner == null || lease.Owner == owner);
        _instance.RemoveStops(stop => stop.Owner == null || stop.Owner == owner, "owner-release");
    }

    public static void ClearAll(string reason = "clear-all")
    {
        CombatActorClock.ClearAllStops(reason);
        if (_instance == null) return;
        _instance.ObserveExternalScale();
        _instance.RemoveStops(_ => true, reason);
    }

    /// <summary>Pause/scene controllers use this ownership barrier, including writes equal to our current scale.</summary>
    public static void SetExternalWorldTimeScale(float scale)
    {
        if (!KnockbackMotion.IsFinite(scale) || scale < 0f) return;
        if (_instance != null)
        {
            _instance.ObserveExternalScale();
            _instance.RemoveWorldScales(_ => true);
        }
        ClearAll("external-world-time");
        ExternalWorldTimeRevision++;
        Time.timeScale = scale;
        if (_instance != null)
        {
            _instance._previousScale = _instance._appliedScale = scale;
            _instance._ownsWorldTime = false;
        }
    }

    private void OnEnable() => SceneManager.activeSceneChanged += OnSceneChanged;
    private void OnSceneChanged(Scene previous, Scene current)
    {
        ObserveExternalScale();
        RemoveWorldScales(_ => true);
        ClearAll("scene-change");
    }
    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
        ObserveExternalScale();
        RemoveWorldScales(_ => true);
        ClearAll("service-disabled");
        if (_instance == this) _instance = null;
    }
    private void Update() => Refresh();

    private void Refresh()
    {
        ObserveExternalScale();
        Prune();
    }

    private void ObserveExternalScale()
    {
        if (!_ownsWorldTime || Mathf.Approximately(Time.timeScale, _appliedScale)) return;
        // An unrelated pause/scene controller now owns world time. Never restore over it.
        _ownsWorldTime = false;
        _previousScale = _appliedScale = Time.timeScale;
        RemoveWorldScales(_ => true);
        RemoveStops(_ => true, "external-world-time-observed");
    }

    private bool CaptureWorldTime()
    {
        if (_ownsWorldTime) return _previousScale > 0f;
        if (!KnockbackMotion.IsFinite(Time.timeScale) || Time.timeScale <= 0f) return false;
        _previousScale = Time.timeScale;
        _ownsWorldTime = true;
        return true;
    }

    private float WorldMultiplier
    {
        get
        {
            float multiplier = 1f;
            foreach (var lease in _worldScales) multiplier = Mathf.Min(multiplier, lease.Multiplier);
            return multiplier;
        }
    }

    private static bool OwnerIsAvailable(Object owner)
        => owner != null && (!(owner is Behaviour behaviour) || behaviour.isActiveAndEnabled)
            && (!(owner is GameObject host) || host.activeInHierarchy);

    private bool Validate(WorldScaleLease lease)
    {
        Refresh();
        return _worldScales.Contains(lease);
    }

    private void Release(WorldScaleLease lease)
    {
        ObserveExternalScale();
        _worldScales.Remove(lease);
        ApplyScale();
    }

    private void RemoveWorldScales(System.Predicate<WorldScaleLease> predicate)
    {
        for (int i = _worldScales.Count - 1; i >= 0; i--)
        {
            var lease = _worldScales[i];
            if (!predicate(lease)) continue;
            _worldScales.RemoveAt(i);
            lease.Invalidate();
        }
        // An observed external write relinquishes ownership before cleanup.
        if (_ownsWorldTime) ApplyScale();
    }

    private void Request(Object owner, float duration, float scale, HitStopStacking stacking)
    {
        if (!KnockbackMotion.IsFinite(duration) || duration <= 0f || !KnockbackMotion.IsFinite(scale))
        {
            EmitStop("hitstop-request", owner, null, HitStopScope.Global, duration, scale, Time.timeScale,
                stacking.ToString(), "rejected-invalid");
            return;
        }
        ObserveExternalScale();
        double now = Time.realtimeSinceStartupAsDouble;
        Prune();
        if (!CaptureWorldTime())
        {
            EmitStop("hitstop-request", owner, null, HitStopScope.Global, duration, scale, Time.timeScale,
                stacking.ToString(), "rejected-external-pause");
            return;
        }
        var existing = _stops.Find(stop => stop.Owner == owner);
        bool created = existing == null;
        EmitStop("hitstop-request", owner, null, HitStopScope.Global, duration, scale, Time.timeScale,
            stacking.ToString(), "accepted");
        if (existing == null)
        {
            existing = new Stop { Owner = owner, Expires = now + duration, Scale = Mathf.Clamp01(scale), Started = now };
            _stops.Add(existing);
        }
        else if (stacking == HitStopStacking.Extend)
        {
            existing.Expires = System.Math.Max(now, existing.Expires) + duration;
            existing.Scale = Mathf.Min(existing.Scale, Mathf.Clamp01(scale));
        }
        else if (stacking == HitStopStacking.Maximum)
        {
            existing.Expires = System.Math.Max(existing.Expires, now + duration);
            existing.Scale = Mathf.Min(existing.Scale, Mathf.Clamp01(scale));
        }
        else { existing.Expires = now + duration; existing.Scale = Mathf.Clamp01(scale); }
        ApplyScale();
        EmitStop(created ? "hitstop-begin" : "hitstop-change", owner, null, HitStopScope.Global, duration,
            scale, Time.timeScale, stacking.ToString(), created ? "new-lease" : "merged-lease", existing.Started, existing.Expires);
    }

    private void Prune()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        RemoveWorldScales(lease => !OwnerIsAvailable(lease.Owner));
        RemoveStops(stop => stop.Owner == null || now >= stop.Expires
            || stop.Owner is Behaviour behaviour && !behaviour.isActiveAndEnabled, "prune");
    }

    private void RemoveStops(System.Predicate<Stop> predicate, string reason)
    {
        var removed = new List<Stop>();
        for (int i = _stops.Count - 1; i >= 0; i--)
        {
            var stop = _stops[i];
            if (!predicate(stop)) continue;
            _stops.RemoveAt(i); removed.Add(stop);
        }
        ApplyScale();
        foreach (var stop in removed)
        {
            string actualReason = reason == "prune" ? stop.Owner == null ? "owner-destroyed"
                : stop.Owner is Behaviour behaviour && !behaviour.isActiveAndEnabled ? "owner-disabled" : "expired" : reason;
            EmitStop("hitstop-end", stop.Owner, null, HitStopScope.Global, 0, stop.Scale, Time.timeScale,
                "", actualReason, stop.Started, stop.Expires);
        }
    }

    internal static void EmitStop(string kind, Object owner, GameObject target, HitStopScope scope,
        float duration, float requestedScale, float appliedScale, string stacking, string reason,
        double started = 0, double expires = 0)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        var ownerObject = owner == null ? null : owner is Component component ? component.gameObject : owner as GameObject;
        string detail = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "channel=hitstop;scope={0};requested={1:R};applied={2:R};duration={3:R};stacking={4};reason={5};realTime={6:R};elapsedReal={7:R};remainingReal={8:R}",
            scope, requestedScale, appliedScale, duration, stacking, reason, now,
            started > 0 ? System.Math.Max(0, now - started) : 0, System.Math.Max(0, expires - now));
        CombatFeedbackTelemetry.Emit(kind, ownerObject, target, detail: detail, value: appliedScale);
    }

    private void ApplyScale()
    {
        if (_stops.Count == 0 && _worldScales.Count == 0)
        {
            if (_ownsWorldTime && Mathf.Approximately(Time.timeScale, _appliedScale)) Time.timeScale = _previousScale;
            _ownsWorldTime = false;
            _appliedScale = Time.timeScale;
            return;
        }
        if (!_ownsWorldTime) return;
        float scale = _previousScale * WorldMultiplier;
        foreach (var stop in _stops) scale = Mathf.Min(scale, stop.Scale);
        Time.timeScale = _appliedScale = scale;
    }
}
