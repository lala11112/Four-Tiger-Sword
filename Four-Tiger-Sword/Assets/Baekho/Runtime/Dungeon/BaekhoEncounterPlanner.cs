using System;
using System.Collections.Generic;

namespace StudioBaekho
{
    /// <summary>The counts have the same order as the costs supplied to Plan.</summary>
    public sealed class BaekhoEncounterPlan
    {
        public int[] Counts { get; }
        public int Spent { get; }
        public int Remaining { get; }
        public bool BalanceRelaxed { get; }

        internal BaekhoEncounterPlan(int[] counts, int spent, int budget, bool balanceRelaxed)
        {
            Counts = counts;
            Spent = spent;
            Remaining = budget - spent;
            BalanceRelaxed = balanceRelaxed;
        }
    }

    /// <summary>
    /// Maximizes spending with at least two species and at most half of the monsters
    /// in the cheapest price tier. Equal prices omit the price-tier restriction.
    /// If no nonempty balanced encounter fits, both restrictions are relaxed.
    /// Uses no Unity objects or global random state.
    /// </summary>
    public static class BaekhoEncounterPlanner
    {
        private const int Unreachable = int.MinValue;

        public static BaekhoEncounterPlan Plan(int budget, int[] costs, int seed)
        {
            if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget), "Encounter budget must be nonnegative.");
            if (costs == null) throw new ArgumentNullException(nameof(costs));

            int cheapest = int.MaxValue;
            int divisor = 0;
            for (int i = 0; i < costs.Length; i++)
            {
                if (costs[i] <= 0)
                    throw new ArgumentException("Every monster cost must be a positive integer. Invalid index: " + i, nameof(costs));
                cheapest = Math.Min(cheapest, costs[i]);
                divisor = GreatestCommonDivisor(divisor, costs[i]);
            }

            var counts = new int[costs.Length];
            if (costs.Length == 0 || budget < cheapest)
                return new BaekhoEncounterPlan(counts, 0, budget, false);

            // Divide out common prices to keep large configured price units inexpensive.
            int capacity = budget / divisor;
            if (capacity == int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(budget), "Encounter budget is too large for the allocation table.");
            var prices = new int[costs.Length];
            var weights = new int[costs.Length];
            bool equalPrices = true;
            for (int i = 0; i < costs.Length; i++)
                if (costs[i] != cheapest) equalPrices = false;
            for (int i = 0; i < costs.Length; i++)
            {
                prices[i] = costs[i] / divisor;
                weights[i] = equalPrices ? 0 : costs[i] == cheapest ? -1 : 1;
            }

            // A balance is (non-cheapest count - cheapest count). For each spend,
            // retain its maximum over mixed-species paths and over any path.
            // Lower balances are dominated for feasibility, so the tables are O(budget).
            // Single-species paths are computed directly from their price.
            var mixed = new int[capacity + 1];
            var any = new int[capacity + 1];
            for (int spent = 0; spent <= capacity; spent++)
            {
                mixed[spent] = Unreachable;
                any[spent] = spent == 0 ? 0 : Unreachable;
            }
            for (int spent = 1; spent <= capacity; spent++)
            {
                for (int species = 0; species < prices.Length; species++)
                {
                    int rest = spent - prices[species];
                    if (rest < 0) continue;
                    if (mixed[rest] != Unreachable)
                        mixed[spent] = Math.Max(mixed[spent], mixed[rest] + weights[species]);
                    for (int other = 0; other < prices.Length; other++)
                    {
                        if (other == species || rest == 0 || rest % prices[other] != 0) continue;
                        int balance = rest / prices[other] * weights[other] + weights[species];
                        mixed[spent] = Math.Max(mixed[spent], balance);
                    }
                    if (spent % prices[species] == 0)
                        any[spent] = Math.Max(any[spent], spent / prices[species] * weights[species]);
                }
                any[spent] = Math.Max(any[spent], mixed[spent]);
            }

            int best = capacity;
            while (best > 0 && mixed[best] < 0) best--;
            bool relaxed = best == 0;
            if (relaxed)
            {
                best = capacity;
                while (best > 0 && any[best] == Unreachable) best--;
            }

            // Reconstruct by choosing among every next species that has a feasible
            // completion. This allows random ties beyond just maximum-balance paths.
            var random = new Random(seed);
            var candidates = new List<int>(prices.Length);
            int remaining = best;
            int prefixBalance = 0;
            int firstSpecies = -1;
            bool prefixMixed = false;
            while (remaining > 0)
            {
                candidates.Clear();
                for (int species = 0; species < prices.Length; species++)
                {
                    int rest = remaining - prices[species];
                    if (rest < 0 || any[rest] == Unreachable) continue;
                    if (!relaxed)
                    {
                        bool becomesMixed = prefixMixed || firstSpecies >= 0 && firstSpecies != species;
                        int completion = becomesMixed ? any[rest] : mixed[rest];
                        if (!becomesMixed && rest > 0)
                        {
                            for (int other = 0; other < prices.Length; other++)
                                if (other != species && rest % prices[other] == 0)
                                    completion = Math.Max(completion, rest / prices[other] * weights[other]);
                        }
                        if (completion == Unreachable || prefixBalance + weights[species] + completion < 0) continue;
                    }
                    candidates.Add(species);
                }
                if (candidates.Count == 0)
                    throw new InvalidOperationException("Encounter allocation could not reconstruct a reachable budget.");
                int chosen = candidates[random.Next(candidates.Count)];
                counts[chosen]++;
                remaining -= prices[chosen];
                prefixBalance += weights[chosen];
                prefixMixed |= firstSpecies >= 0 && firstSpecies != chosen;
                if (firstSpecies < 0) firstSpecies = chosen;
            }

            return new BaekhoEncounterPlan(counts, best * divisor, budget, relaxed);
        }

        private static int GreatestCommonDivisor(int a, int b)
        {
            while (b != 0)
            {
                int remainder = a % b;
                a = b;
                b = remainder;
            }
            return a;
        }
    }
}
