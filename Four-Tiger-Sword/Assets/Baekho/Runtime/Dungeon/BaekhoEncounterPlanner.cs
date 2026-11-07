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

    /// <summary>A portion of an encounter, in the original catalog order.</summary>
    public sealed class BaekhoEncounterWave
    {
        public int[] Counts { get; }
        public int Cost { get; }
        public int EnemyCount { get; }

        internal BaekhoEncounterWave(int[] counts, int cost, int enemyCount)
        {
            Counts = counts;
            Cost = cost;
            EnemyCount = enemyCount;
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

        /// <summary>
        /// Preserves the planned composition while balancing its cost across waves.
        /// The enemy limit is hard; target cost is soft when prices cannot divide evenly.
        /// Lighter waves are returned first. Uses only its own seeded random source.
        /// </summary>
        public static IReadOnlyList<BaekhoEncounterWave> SplitIntoWaves(int[] counts, int[] costs,
            int targetCostPerWave, int maxEnemiesPerWave, int seed)
        {
            if (counts == null) throw new ArgumentNullException(nameof(counts));
            if (costs == null) throw new ArgumentNullException(nameof(costs));
            if (counts.Length != costs.Length)
                throw new ArgumentException("Counts and costs must have the same catalog order and length.");
            if (targetCostPerWave <= 0) throw new ArgumentOutOfRangeException(nameof(targetCostPerWave));
            if (maxEnemiesPerWave <= 0) throw new ArgumentOutOfRangeException(nameof(maxEnemiesPerWave));

            long totalCost = 0;
            int totalEnemies = 0;
            var species = new List<int>();
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] < 0) throw new ArgumentException("Enemy counts must be nonnegative.", nameof(counts));
                if (costs[i] <= 0) throw new ArgumentException("Enemy costs must be positive.", nameof(costs));
                totalCost += (long)counts[i] * costs[i];
                if (totalCost > int.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(counts), "Total encounter cost exceeds the supported integer budget.");
                totalEnemies += counts[i];
                if (counts[i] > 0) species.Add(i);
            }
            if (totalEnemies == 0) return Array.Empty<BaekhoEncounterWave>();

            int waveCount = (int)Math.Min(totalEnemies, Math.Max(
                (totalCost + targetCostPerWave - 1) / targetCostPerWave,
                ((long)totalEnemies + maxEnemiesPerWave - 1) / maxEnemiesPerWave));
            var waveCounts = new int[waveCount][];
            var waveCosts = new int[waveCount];
            var waveEnemyCounts = new int[waveCount];
            for (int i = 0; i < waveCount; i++) waveCounts[i] = new int[counts.Length];

            var random = new Random(seed);
            for (int i = species.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                int swap = species[i];
                species[i] = species[other];
                species[other] = swap;
            }
            // Stable ordering retains the seeded order among equally costly species.
            for (int i = 1; i < species.Count; i++)
            {
                int value = species[i];
                int j = i - 1;
                while (j >= 0 && costs[species[j]] < costs[value])
                {
                    species[j + 1] = species[j];
                    j--;
                }
                species[j + 1] = value;
            }

            var candidates = new List<int>(waveCount);
            foreach (int index in species)
            {
                for (int enemy = 0; enemy < counts[index]; enemy++)
                {
                    candidates.Clear();
                    int lowestCost = int.MaxValue;
                    int lowestCount = int.MaxValue;
                    for (int wave = 0; wave < waveCount; wave++)
                    {
                        if (waveEnemyCounts[wave] >= maxEnemiesPerWave) continue;
                        if (waveCosts[wave] < lowestCost ||
                            (waveCosts[wave] == lowestCost && waveEnemyCounts[wave] < lowestCount))
                        {
                            candidates.Clear();
                            lowestCost = waveCosts[wave];
                            lowestCount = waveEnemyCounts[wave];
                        }
                        if (waveCosts[wave] == lowestCost && waveEnemyCounts[wave] == lowestCount)
                            candidates.Add(wave);
                    }
                    int selected = candidates[random.Next(candidates.Count)];
                    waveCounts[selected][index]++;
                    waveCosts[selected] += costs[index];
                    waveEnemyCounts[selected]++;
                }
            }

            var waves = new List<BaekhoEncounterWave>(waveCount);
            for (int i = 0; i < waveCount; i++)
            {
                var wave = new BaekhoEncounterWave(waveCounts[i], waveCosts[i], waveEnemyCounts[i]);
                int position = waves.Count;
                while (position > 0 && waves[position - 1].Cost > wave.Cost) position--;
                waves.Insert(position, wave);
            }
            return waves;
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
