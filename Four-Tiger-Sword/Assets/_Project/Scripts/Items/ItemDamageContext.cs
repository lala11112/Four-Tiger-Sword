using System;
using UnityEngine;

public enum EnemyRank { Normal, Elite, Boss }

/// <summary>Captured before damage callbacks can end an encounter, heal, or dispatch another hit.</summary>
public readonly struct ItemDeathContext
{
    public readonly PlayerController Player;
    public readonly BoundaryAttackFamily Family;
    public readonly bool CanTrigger, WasInCombat;
    public readonly float PlayerHp, PlayerMaxHp;
    public readonly int CombatEpoch;
    public readonly bool Critical, IsDirectPlayerHit;
    public readonly DamageOrigin Origin;
    public ItemDeathContext(PlayerController player, BoundaryAttackFamily family, bool canTrigger,
        bool critical = false, bool isDirectPlayerHit = false, DamageOrigin origin = DamageOrigin.AuthoredAction)
    {
        Player = player; Family = family; CanTrigger = canTrigger;
        Critical = critical; IsDirectPlayerHit = isDirectPlayerHit; Origin = origin;
        PlayerHp = player != null && player.StatManager != null ? player.StatManager.CurrentHp : 0f;
        PlayerMaxHp = player != null && player.StatManager != null ? player.StatManager.MaxHp : 0f;
        var items = player != null ? player.ItemRuntime : null;
        CombatEpoch = items != null ? items.CombatEpoch : 0;
        WasInCombat = items != null && items.IsInCombat;
    }
}

/// <summary>Scoped only around TakeDamage. Nested hits restore the caller's context.</summary>
public static class ItemDamageContext
{
    private static Scope _current;
    public static ItemDeathContext Current => _current != null ? _current.Context : default;
    public static IDisposable Push(ItemDeathContext context, PlayerItemRuntime runtime = null, ItemHitContext hit = default,
        Action onCommitted = null)
        => new Scope(context, runtime, hit, onCommitted);
    public static void CommitOutgoing(float healthDamage, float shieldDamage)
        => _current?.Commit(healthDamage, shieldDamage);
    public static BoundaryAttackFamily Family(PlayerAttackCategory category) => category switch
    {
        PlayerAttackCategory.Normal or PlayerAttackCategory.Heavy or PlayerAttackCategory.Counter => BoundaryAttackFamily.Normal,
        PlayerAttackCategory.Skill or PlayerAttackCategory.Ultimate or PlayerAttackCategory.SwitchAttack => BoundaryAttackFamily.Skill,
        _ => BoundaryAttackFamily.Any
    };
    private sealed class Scope : IDisposable
    {
        public readonly ItemDeathContext Context;
        private readonly Scope _previous;
        private readonly PlayerItemRuntime _runtime;
        private readonly ItemHitContext _hit;
        private readonly Action _onCommitted;
        private bool _committed;
        public Scope(ItemDeathContext context, PlayerItemRuntime runtime, ItemHitContext hit, Action onCommitted)
        { Context = context; _runtime = runtime; _hit = hit; _onCommitted = onCommitted; _previous = _current; _current = this; }
        public void Commit(float healthDamage, float shieldDamage)
        {
            if (_committed || healthDamage + shieldDamage <= 0f) return;
            _committed = true;
            _onCommitted?.Invoke();
            _runtime?.CommitOutgoing(_hit, healthDamage, shieldDamage);
        }
        public void Dispose() => _current = _previous;
    }
}
