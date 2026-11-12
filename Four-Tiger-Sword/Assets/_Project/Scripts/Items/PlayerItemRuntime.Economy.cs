using System;

public sealed partial class PlayerItemRuntime
{
    internal double EarnedGoldRatio
    {
        get
        {
            if (!_initialized) return 0d;
            double ratio = 0d;
            foreach (var source in _boundarySources.Values)
                if (source.Active && source.Effect.stat == BoundaryItemStat.GoldGainRatio
                    && source.Effect.trigger == BoundaryItemTrigger.WhileOwned && IsUnconditional(source.Effect))
                    // Remove float noise from authored percentage values before fractional accumulation.
                    ratio += Math.Round(source.Effect.amount, 6);
            return ratio;
        }
    }

    internal int CombatRoomClearGold
    {
        get
        {
            if (!_initialized) return 0;
            double amount = 0d;
            foreach (var source in _boundarySources.Values)
                if (source.Active && source.Effect.stat == BoundaryItemStat.GoldFlat
                    && source.Effect.trigger == BoundaryItemTrigger.RoomCleared && IsUnconditional(source.Effect))
                    amount += source.Effect.amount;
            return (int)Math.Min(int.MaxValue, Math.Floor(amount));
        }
    }
}
