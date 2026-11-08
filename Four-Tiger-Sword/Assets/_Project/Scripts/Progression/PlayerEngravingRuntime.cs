using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A frozen account snapshot and the effects consumed during one run.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(PlayerStatManager))]
public sealed class PlayerEngravingRuntime : MonoBehaviour
{
    public EngravingSnapshot Snapshot { get; private set; } = EngravingSnapshot.Empty;
    public bool ReviveSpent => _reviveSpent;
    public bool IsInvulnerable => _initialized && isActiveAndEnabled && _invulnerability > 0f;
    public double GoldFractionRemainder => _goldFraction;
    public float CriticalPity => _pity;
    public float MoveMultiplier => 1f + Value("footwork")
        + Mathf.Max(_pursuit > 0f ? Value("pursuit") : 0f, _escape > 0f ? Value("escape") : 0f);
    public float NormalReachMultiplier => 1f + Value("reach");

    private PlayerController _player;
    private PlayerStatManager _stats;
    private bool _initialized, _reviveSpent, _applyingStats;
    private float _pity, _counter, _pursuit, _escape, _haste, _invulnerability, _comboAge;
    private int _comboHits;
    private Enemy _comboTarget;
    private double _goldFraction;
    private readonly object _overhealSource = new();
    private readonly HashSet<Enemy> _opened = new(), _bossGuardUsed = new(), _kills = new(), _goldKills = new();

    public static PlayerEngravingRuntime Ensure(PlayerController player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        return player.GetComponent<PlayerEngravingRuntime>() ?? player.gameObject.AddComponent<PlayerEngravingRuntime>();
    }

    private bool Available => _initialized && isActiveAndEnabled && _stats != null && _stats.CurrentHp > 0f;
    private float Value(string id) => _initialized && isActiveAndEnabled ? Snapshot.Amount(id) : 0f;

    private void References()
    {
        if (_stats != null) return;
        _player = GetComponent<PlayerController>();
        _stats = GetComponent<PlayerStatManager>();
        _stats.OnStatsChanged += ClampOverheal;
    }

    public void InitializeRun(EngravingSnapshot snapshot)
    {
        References();
        ClearStageTransient();
        Snapshot = snapshot ?? EngravingSnapshot.Empty;
        _initialized = true;
        _reviveSpent = false;
        _goldFraction = 0d;
        _kills.Clear(); _goldKills.Clear();
        ApplyStats(true);
    }

    public void ClearStageTransient()
    {
        _pity = _counter = _pursuit = _escape = _haste = _invulnerability = _comboAge = 0f;
        _comboTarget = null; _comboHits = 0;
        _opened.Clear(); _bossGuardUsed.Clear();
        _stats?.RemoveShield(_overhealSource);
        if (_initialized) ApplyStats();
    }

    private void Update()
    {
        if (_player == null || !_player.isActiveAndEnabled || _player.IsStageTransitioning
            || _player.IsGameplayModalPaused) return;
        Tick(Time.deltaTime);
    }

    public void Tick(float deltaTime)
    {
        if (_player == null || !_player.isActiveAndEnabled || _player.IsStageTransitioning || _player.IsGameplayModalPaused) return;
        if (!Available || !Finite(deltaTime) || deltaTime <= 0f) return;
        bool hadHaste = _haste > 0f;
        _counter = Mathf.Max(0f, _counter - deltaTime);
        _pursuit = Mathf.Max(0f, _pursuit - deltaTime);
        _escape = Mathf.Max(0f, _escape - deltaTime);
        _haste = Mathf.Max(0f, _haste - deltaTime);
        _invulnerability = Mathf.Max(0f, _invulnerability - deltaTime);
        _comboAge += deltaTime;
        if (hadHaste != (_haste > 0f)) ApplyStats();
    }

    private void ApplyStats(bool refill = false)
    {
        if (_stats == null || _applyingStats) return;
        _applyingStats = true;
        try
        {
            // The account snapshot remains this run's permanent basis while a component or its
            // GameObject is temporarily disabled. Removing max HP here would irreversibly clamp HP.
            float Permanent(string id) => _initialized ? Snapshot.Amount(id) : 0f;
            _stats.SetEngravingBonuses(Permanent("hp"), Permanent("atk"), Permanent("crit") * 100f,
                Permanent("severe") * 100f, Permanent("speed") + (_haste > 0f ? Value("haste") : 0f), refill);
        }
        finally { _applyingStats = false; }
    }

    public static bool IsDirect(HitInfo hit) => hit.IsDirectPlayerHit
        && hit.Origin == DamageOrigin.AuthoredAction && hit.Category != PlayerAttackCategory.Secondary;

    /// <summary>Pure evaluation; the scoped contact commit consumes one-use state only after real damage.</summary>
    public float OutgoingDamageBonus(HitInfo hit, Enemy target)
    {
        if (!Available || target == null || !IsDirect(hit)) return 0f;
        float bonus = target.Rank == EnemyRank.Boss ? Value("bossdmg") : 0f;
        var stats = target.EnemyStat;
        float hp = stats != null ? stats.CurrentHp : 0f, max = stats != null ? stats.MaxHp : 0f;
        if (max > 0f && hp >= max - .0001f && !_opened.Contains(target)) bonus += Value("opener");
        if (max > 0f && hp / max <= Snapshot.Threshold("finisher") + .000001f) bonus += Value("finisher");
        if (target == _comboTarget && _comboAge < Snapshot.Duration("combo"))
            bonus += Mathf.Min(Snapshot.Cap("combo"), _comboHits * Value("combo"));
        if (_counter > 0f) bonus += Value("counter");
        if (Value("backstab") > 0f && _player != null)
        {
            Vector3 towardPlayer = _player.transform.position - target.transform.position;
            towardPlayer.y = 0f;
            Vector3 forward = target.transform.forward; forward.y = 0f;
            if (towardPlayer.sqrMagnitude > .0001f && forward.sqrMagnitude > .0001f
                && Vector3.Angle(forward, towardPlayer) >= Snapshot.Threshold("backstab")) bonus += Value("backstab");
        }
        return Mathf.Max(0f, bonus);
    }

    public float OutgoingCriticalBonus(HitInfo hit)
        => Available && IsDirect(hit) && !hit.TrueDamage ? _pity : 0f;

    public void CommitOutgoing(HitInfo hit, Enemy target, bool critical)
    {
        if (!Available || target == null || !IsDirect(hit)) return;
        _opened.Add(target);
        if (!hit.TrueDamage)
            _pity = critical ? 0f : Mathf.Min(Snapshot.Cap("pity"), _pity + Value("pity"));
        _counter = 0f;
        _comboHits = target == _comboTarget && _comboAge < Snapshot.Duration("combo") ? _comboHits + 1 : 1;
        _comboTarget = target; _comboAge = 0f;
    }

    public void NotifySuccessfulDodge()
    {
        if (Available && Snapshot.Has("counter")) _counter = Snapshot.Duration("counter");
    }

    public float IncomingMultiplier(Enemy attacker, float hpBefore, float maxHp)
    {
        if (!Available) return 1f;
        float multiplier = 1f - Mathf.Clamp01(Value("armor"));
        if (maxHp > 0f && hpBefore / maxHp <= Snapshot.Threshold("guard") + .000001f)
            multiplier *= 1f - Mathf.Clamp01(Value("guard"));
        if (attacker != null && attacker.Rank == EnemyRank.Boss && !_bossGuardUsed.Contains(attacker))
            multiplier *= 1f - Mathf.Clamp01(Value("bossguard"));
        if (Snapshot.Has("crowdguard") && NearbyEnemies(Snapshot.Range("crowdguard")).Count >= Snapshot.Limit("crowdguard"))
            multiplier *= 1f - Mathf.Clamp01(Value("crowdguard"));
        return Mathf.Clamp01(multiplier);
    }

    public void CommitIncoming(Enemy attacker, float healthDamage, float shieldDamage)
    {
        if (!Available || healthDamage + shieldDamage <= 0f) return;
        if (attacker != null && attacker.Rank == EnemyRank.Boss) _bossGuardUsed.Add(attacker);
        if (Snapshot.Has("escape")) _escape = Snapshot.Duration("escape");
    }

    public bool TryRevive(out float restoredHp)
    {
        restoredHp = 0f;
        if (!_initialized || !isActiveAndEnabled || _stats == null || _reviveSpent || !Snapshot.Has("revive")) return false;
        _reviveSpent = true;
        _invulnerability = Snapshot.Duration("revive");
        restoredHp = _stats.MaxHp * Snapshot.Amount("revive");
        return restoredHp > 0f;
    }

    public void PushAfterRevive()
    {
        foreach (var enemy in NearbyEnemies(Snapshot.Range("revive")))
        {
            Vector3 direction = enemy.transform.position - transform.position; direction.y = 0f;
            if (direction.sqrMagnitude < .0001f) direction = transform.forward;
            enemy.TryApplyExternalKnockback(direction.normalized * Snapshot.Secondary("revive"),
                StaggerResistLevel.HIGH, _player);
        }
    }

    private HashSet<Enemy> NearbyEnemies(float radius)
    {
        var found = new HashSet<Enemy>();
        if (radius <= 0f) return found;
        foreach (var collider in Physics.OverlapSphere(transform.position, radius, LayerMask.GetMask("Enemy"), QueryTriggerInteraction.Collide))
        {
            var enemy = collider.GetComponentInParent<Enemy>();
            if (enemy != null && enemy.isActiveAndEnabled && !enemy.IsDead && enemy.EnemyStat?.CurrentHp > 0f
                && !enemy.EncounterPatrolOnly) found.Add(enemy);
        }
        return found;
    }

    public void NotifyKill(Enemy target, ItemDeathContext death)
    {
        if (!Available || target == null || death.Player != _player || !death.IsDirectPlayerHit
            || death.Origin != DamageOrigin.AuthoredAction || !_kills.Add(target)) return;
        if (Snapshot.Has("pursuit")) _pursuit = Snapshot.Duration("pursuit");
        if (Snapshot.Has("haste")) { _haste = Snapshot.Duration("haste"); ApplyStats(); }
        if (death.Critical && Snapshot.Has("critheal")) _stats.Heal(_stats.MaxHp * Value("critheal"));
    }

    public int BonusKillGold(Enemy target, ItemDeathContext death, int originalBaseGold)
    {
        if (!Available || target == null || death.Player != _player || target.Rank != EnemyRank.Normal
            || originalBaseGold <= 0 || !Snapshot.Has("firstblood") || !_goldKills.Add(target)) return 0;
        _goldFraction = Math.Round(_goldFraction + originalBaseGold * Math.Round(Value("firstblood"), 6), 9);
        double whole = Math.Floor(_goldFraction + .000001d);
        _goldFraction = Math.Max(0d, _goldFraction - whole);
        return (int)Math.Min(int.MaxValue, whole);
    }

    public void NotifyRoomCleared()
    {
        if (Available && Snapshot.Has("roomheal")) _stats.Heal(_stats.MaxHp * Value("roomheal"));
    }

    public float HealingMultiplier => 1f + Value("heal");
    public void NotifyOverheal(float amount)
    {
        if (Available && amount > 0f && Snapshot.Has("shield"))
            _stats.AddCappedShield(_overhealSource, amount * Value("shield"),
                _stats.MaxHp * Snapshot.Cap("shield"), Snapshot.Duration("shield"));
    }

    private void ClampOverheal()
    {
        if (_stats != null) _stats.ClampShield(_overhealSource, _stats.MaxHp * Snapshot.Cap("shield"));
    }
    private void OnEnable() { References(); if (_initialized) ApplyStats(); }
    private void OnDisable() { ClearStageTransient(); ApplyStats(); }
    private void OnDestroy() { if (_stats != null) _stats.OnStatsChanged -= ClampOverheal; }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
