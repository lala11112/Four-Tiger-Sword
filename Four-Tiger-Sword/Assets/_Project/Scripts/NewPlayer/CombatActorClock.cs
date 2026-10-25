using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Actor action time. Cooldowns, buffs and other world systems keep using Time.deltaTime.</summary>
[DefaultExecutionOrder(-250)]
[DisallowMultipleComponent]
public sealed class CombatActorClock : MonoBehaviour
{
    private sealed class Stop
    {
        public Object Owner;
        public double Expires;
        public float Scale;
        public double Started;
        public HitStopScope Scope;
    }

    private static readonly HashSet<CombatActorClock> Clocks = new();
    private readonly List<Stop> _stops = new();
    private readonly Dictionary<object, float> _movementModifiers = new();
    private Animator _animator;
    private NavMeshAgent _navigation;
    private float _animationSpeed = 1f;
    private float _navigationSpeed, _navigationAcceleration, _navigationAngularSpeed;
    private float _lastNavigationScale = 1f;
    private float _time;

    public float TimeScale
    {
        get
        {
            Prune();
            float scale = 1f;
            foreach (var stop in _stops) scale = Mathf.Min(scale, stop.Scale);
            return scale;
        }
    }
    public float DeltaTime => UnityEngine.Time.deltaTime * TimeScale;
    public float Time => _time;
    public float AnimationSpeed => _animationSpeed;
    public bool IsStopped => TimeScale <= 0f;

    public static CombatActorClock GetOrCreate(Component actor)
    {
        if (actor == null) return null;
        var clock = actor.GetComponent<CombatActorClock>();
        return clock != null ? clock : actor.gameObject.AddComponent<CombatActorClock>();
    }

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        _navigation = GetComponent<NavMeshAgent>();
        if (_animator != null) _animationSpeed = _animator.speed;
        if (_navigation != null)
        {
            _navigationSpeed = _navigation.speed;
            _navigationAcceleration = _navigation.acceleration;
            _navigationAngularSpeed = _navigation.angularSpeed;
        }
    }

    private void OnEnable() => Clocks.Add(this);
    private void Update()
    {
        _time += DeltaTime;
        Apply();
    }

    public void SetAnimationSpeed(float speed)
    {
        if (!KnockbackMotion.IsFinite(speed)) return;
        _animationSpeed = Mathf.Max(0f, speed);
        Apply();
    }

    public void SetMovementModifier(object owner, float scale)
    {
        if (owner == null || !KnockbackMotion.IsFinite(scale)) return;
        _movementModifiers[owner] = Mathf.Clamp01(scale);
        Apply();
    }

    public void RemoveMovementModifier(object owner)
    {
        if (owner != null) _movementModifiers.Remove(owner);
        Apply();
    }

    public void Request(Object owner, float duration, float scale, HitStopStacking stacking,
        HitStopScope scope = HitStopScope.Attacker)
    {
        if (!isActiveAndEnabled || owner == null || !KnockbackMotion.IsFinite(duration) || duration <= 0f
            || !KnockbackMotion.IsFinite(scale))
        {
            CombatHitStopService.EmitStop("hitstop-request", owner, gameObject, scope, duration, scale,
                EffectiveScale(), stacking.ToString(), "rejected-invalid-or-disabled");
            return;
        }
        Prune();
        double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
        var existing = _stops.Find(stop => stop.Owner == owner);
        bool created = existing == null;
        CombatHitStopService.EmitStop("hitstop-request", owner, gameObject, scope, duration, scale,
            EffectiveScale(), stacking.ToString(), "accepted");
        if (existing == null)
        {
            existing = new Stop { Owner = owner, Expires = now + duration, Scale = Mathf.Clamp01(scale), Started = now, Scope = scope };
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
        else
        {
            existing.Expires = now + duration;
            existing.Scale = Mathf.Clamp01(scale);
        }
        existing.Scope = scope;
        Apply();
        CombatHitStopService.EmitStop(created ? "hitstop-begin" : "hitstop-change", owner, gameObject,
            scope, duration, scale, EffectiveScale(), stacking.ToString(), created ? "new-lease" : "merged-lease",
            existing.Started, existing.Expires);
    }

    public void ClearStops(string reason = "actor-clear")
    {
        RemoveStops(_ => true, reason);
        Apply();
    }

    internal static void ReleaseOwner(Object owner)
    {
        foreach (var clock in new List<CombatActorClock>(Clocks))
        {
            if (clock == null) continue;
            clock.RemoveStops(stop => stop.Owner == null || stop.Owner == owner, "owner-release");
            clock.Apply();
        }
    }

    internal static void ClearAllStops(string reason = "clear-all")
    {
        foreach (var clock in new List<CombatActorClock>(Clocks))
            if (clock != null) clock.ClearStops(reason);
    }

    private void Prune()
    {
        double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
        for (int i = _stops.Count - 1; i >= 0; i--)
        {
            var stop = _stops[i];
            string reason = stop.Owner == null ? "owner-destroyed" : now >= stop.Expires ? "expired"
                : stop.Owner is Behaviour behaviour && !behaviour.isActiveAndEnabled ? "owner-disabled" : null;
            if (reason == null) continue;
            _stops.RemoveAt(i);
            EmitEnd(stop, reason);
        }
    }

    private float EffectiveScale()
    {
        float scale = 1f;
        foreach (var stop in _stops) scale = Mathf.Min(scale, stop.Scale);
        return scale;
    }

    private void RemoveStops(System.Predicate<Stop> predicate, string reason)
    {
        for (int i = _stops.Count - 1; i >= 0; i--)
        {
            var stop = _stops[i];
            if (!predicate(stop)) continue;
            _stops.RemoveAt(i);
            EmitEnd(stop, reason);
        }
    }

    private void EmitEnd(Stop stop, string reason) => CombatHitStopService.EmitStop("hitstop-end",
        stop.Owner, gameObject, stop.Scope, 0f, stop.Scale, EffectiveScale(), "", reason, stop.Started, stop.Expires);

    private void Apply()
    {
        float scale = TimeScale;
        if (_animator != null) _animator.speed = _animationSpeed * scale;
        if (_navigation == null) return;
        // Capture legitimate navigation edits only while our previous output was unscaled.
        if (Mathf.Approximately(_lastNavigationScale, 1f))
        {
            _navigationSpeed = _navigation.speed;
            _navigationAcceleration = _navigation.acceleration;
            _navigationAngularSpeed = _navigation.angularSpeed;
        }
        foreach (float modifier in _movementModifiers.Values) scale *= modifier;
        _navigation.speed = _navigationSpeed * scale;
        _navigation.acceleration = _navigationAcceleration * scale;
        _navigation.angularSpeed = _navigationAngularSpeed * scale;
        if (_navigation.enabled && _navigation.isOnNavMesh && scale < _lastNavigationScale)
            _navigation.velocity = _lastNavigationScale > 0f
                ? _navigation.velocity * (scale / _lastNavigationScale) : Vector3.zero;
        _lastNavigationScale = scale;
    }

    private void OnDisable()
    {
        Clocks.Remove(this);
        ClearStops("actor-disabled");
        _movementModifiers.Clear();
        Apply();
    }
}
