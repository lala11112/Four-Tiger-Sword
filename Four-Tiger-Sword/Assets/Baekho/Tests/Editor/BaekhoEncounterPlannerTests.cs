using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace StudioBaekho.Tests
{
    public sealed class BaekhoEncounterPlannerTests
    {
        [Test]
        public void MatchesExhaustiveSmallBudgetOracle()
        {
            for (int length = 1; length <= 3; length++)
                CheckEveryCostVector(new int[length], 0, 5, 20);
            CheckEveryCostVector(new int[4], 0, 3, 12);
        }

        [Test]
        public void ExactAllocationAvoidsGreedyRemainder()
        {
            var plan = BaekhoEncounterPlanner.Plan(16, new[] { 4, 6, 9 }, 12);
            Assert.That(plan.Spent, Is.EqualTo(16));
            Assert.That(plan.BalanceRelaxed, Is.False);
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, plan.Counts);
        }

        [Test]
        public void IdenticalSeedReproducesAndOtherSeedsBreakTies()
        {
            int[] costs = { 2, 5, 4 };
            var combinations = new HashSet<string>();
            for (int seed = 0; seed < 40; seed++)
            {
                var first = BaekhoEncounterPlanner.Plan(20, costs, seed);
                var repeat = BaekhoEncounterPlanner.Plan(20, costs, seed);
                CollectionAssert.AreEqual(first.Counts, repeat.Counts);
                Assert.That(first.Spent, Is.EqualTo(20));
                AssertValid(first, 20, costs);
                combinations.Add(string.Join(",", first.Counts));
            }
            Assert.That(combinations.Count, Is.GreaterThan(1), "Equivalent optimal encounters should vary with the seed.");
            CollectionAssert.AreEqual(new[] { 2, 5, 4 }, costs, "Planning must not alter catalog order or prices.");
        }

        [Test]
        public void AllocationIsNotLimitedByMarkerCount()
        {
            var plan = BaekhoEncounterPlanner.Plan(20, new[] { 2, 5, 4 }, 3);
            Assert.That(Total(plan.Counts), Is.GreaterThan(1));
            Assert.That(plan.Spent, Is.EqualTo(20));
        }

        [Test]
        public void EqualPricesKeepVarietyWithoutCheapestTierCap()
        {
            var plan = BaekhoEncounterPlanner.Plan(9, new[] { 3, 3 }, 1);
            Assert.That(plan.Spent, Is.EqualTo(9));
            Assert.That(plan.Counts[0], Is.Positive);
            Assert.That(plan.Counts[1], Is.Positive);
            Assert.That(plan.BalanceRelaxed, Is.False);
        }

        [Test]
        public void CheapestTierCountsAllSpeciesAtTheMinimumPrice()
        {
            var plan = BaekhoEncounterPlanner.Plan(9, new[] { 2, 2, 5 }, 1);
            Assert.That(plan.Spent, Is.EqualTo(7));
            Assert.That(plan.Counts[0] + plan.Counts[1], Is.EqualTo(1));
            Assert.That(plan.Counts[2], Is.EqualTo(1));
            Assert.That(plan.BalanceRelaxed, Is.False);
        }

        [Test]
        public void ImpossibleBalanceRelaxesAndStillMaximizesSpending()
        {
            var oneSpecies = BaekhoEncounterPlanner.Plan(8, new[] { 3 }, 4);
            Assert.That(oneSpecies.Spent, Is.EqualTo(6));
            Assert.That(oneSpecies.Counts[0], Is.EqualTo(2));
            Assert.That(oneSpecies.BalanceRelaxed, Is.True);
            var cannotMix = BaekhoEncounterPlanner.Plan(5, new[] { 2, 5 }, 4);
            Assert.That(cannotMix.Spent, Is.EqualTo(5));
            Assert.That(cannotMix.BalanceRelaxed, Is.True);
            var onlyCheapTierFits = BaekhoEncounterPlanner.Plan(4, new[] { 2, 2, 9 }, 4);
            Assert.That(onlyCheapTierFits.Spent, Is.EqualTo(4));
            Assert.That(onlyCheapTierFits.BalanceRelaxed, Is.True);
        }

        [Test]
        public void EmptyAndUnaffordableEncountersAreNotReportedAsRelaxations()
        {
            foreach (int budget in new[] { 0, 1 })
            {
                var plan = BaekhoEncounterPlanner.Plan(budget, new[] { 2, 4, 5 }, 0);
                Assert.That(plan.Spent, Is.Zero);
                Assert.That(Total(plan.Counts), Is.Zero);
                Assert.That(plan.Remaining, Is.EqualTo(budget));
                Assert.That(plan.BalanceRelaxed, Is.False);
            }
            var empty = BaekhoEncounterPlanner.Plan(20, Array.Empty<int>(), 0);
            Assert.That(empty.Counts, Is.Empty);
            Assert.That(empty.Spent, Is.Zero);
            Assert.That(empty.Remaining, Is.EqualTo(20));
            Assert.That(empty.BalanceRelaxed, Is.False);
        }

        [Test]
        public void ModerateBudgetAndLargePriceUnitsUseExactArithmetic()
        {
            var moderate = BaekhoEncounterPlanner.Plan(10000, new[] { 2, 5, 4 }, 15);
            Assert.That(moderate.Spent, Is.EqualTo(10000));
            AssertValid(moderate, 10000, new[] { 2, 5, 4 });
            int[] largePrices = { 600000000, 900000000 };
            var large = BaekhoEncounterPlanner.Plan(2000000000, largePrices, 1);
            Assert.That(large.Spent, Is.EqualTo(1500000000));
            Assert.That(large.Remaining, Is.EqualTo(500000000));
            Assert.That(large.BalanceRelaxed, Is.False);
            AssertValid(large, 2000000000, largePrices);
        }

        [Test]
        public void InvalidInputsFailClearlyEvenForZeroBudget()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BaekhoEncounterPlanner.Plan(-1, new[] { 2 }, 0));
            Assert.Throws<ArgumentNullException>(() => BaekhoEncounterPlanner.Plan(0, null, 0));
            Assert.Throws<ArgumentException>(() => BaekhoEncounterPlanner.Plan(0, new[] { 2, 0 }, 0));
            Assert.Throws<ArgumentException>(() => BaekhoEncounterPlanner.Plan(20, new[] { -1, 5 }, 0));
        }

        private static void CheckEveryCostVector(int[] costs, int index, int maxCost, int maxBudget)
        {
            if (index < costs.Length)
            {
                for (int cost = 1; cost <= maxCost; cost++)
                {
                    costs[index] = cost;
                    CheckEveryCostVector(costs, index + 1, maxCost, maxBudget);
                }
                return;
            }
            for (int budget = 0; budget <= maxBudget; budget++)
            {
                int bestAny = 0, bestBalanced = -1;
                BruteForce(costs, new int[costs.Length], 0, budget, 0, ref bestAny, ref bestBalanced);
                var plan = BaekhoEncounterPlanner.Plan(budget, costs, budget * 97 + costs.Length);
                string context = "budget=" + budget + ", costs=" + string.Join(",", costs);
                Assert.That(plan.Spent, Is.EqualTo(bestBalanced >= 0 ? bestBalanced : bestAny), context);
                Assert.That(plan.BalanceRelaxed, Is.EqualTo(bestAny > 0 && bestBalanced < 0), context);
                AssertValid(plan, budget, costs);
            }
        }

        private static void BruteForce(int[] costs, int[] counts, int index, int budget, int spent,
            ref int bestAny, ref int bestBalanced)
        {
            if (index == costs.Length)
            {
                bestAny = Math.Max(bestAny, spent);
                if (IsBalanced(counts, costs)) bestBalanced = Math.Max(bestBalanced, spent);
                return;
            }
            for (int count = 0; count <= (budget - spent) / costs[index]; count++)
            {
                counts[index] = count;
                BruteForce(costs, counts, index + 1, budget, spent + count * costs[index], ref bestAny, ref bestBalanced);
            }
            counts[index] = 0;
        }

        private static void AssertValid(BaekhoEncounterPlan plan, int budget, int[] costs)
        {
            Assert.That(plan.Counts.Length, Is.EqualTo(costs.Length));
            long spent = 0;
            for (int i = 0; i < costs.Length; i++)
            {
                Assert.That(plan.Counts[i], Is.GreaterThanOrEqualTo(0));
                spent += (long)plan.Counts[i] * costs[i];
            }
            Assert.That(spent, Is.EqualTo(plan.Spent));
            Assert.That(plan.Remaining, Is.EqualTo(budget - plan.Spent));
            Assert.That(plan.Spent, Is.LessThanOrEqualTo(budget));
            if (!plan.BalanceRelaxed && plan.Spent > 0) Assert.That(IsBalanced(plan.Counts, costs), Is.True);
        }

        private static bool IsBalanced(int[] counts, int[] costs)
        {
            int cheapest = int.MaxValue;
            for (int i = 0; i < costs.Length; i++) cheapest = Math.Min(cheapest, costs[i]);
            bool equal = true;
            int species = 0, cheapCount = 0, total = 0;
            for (int i = 0; i < costs.Length; i++)
            {
                if (costs[i] != cheapest) equal = false;
                if (counts[i] > 0) species++;
                if (costs[i] == cheapest) cheapCount += counts[i];
                total += counts[i];
            }
            return species >= 2 && (equal || cheapCount <= total - cheapCount);
        }

        private static int Total(int[] counts)
        {
            int total = 0;
            for (int i = 0; i < counts.Length; i++) total += counts[i];
            return total;
        }
    }
}
