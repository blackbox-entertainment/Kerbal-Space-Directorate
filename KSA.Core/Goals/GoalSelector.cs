using System;
using System.Collections.Generic;

namespace KSA.Core.Goals
{
    /// <summary>What the goal rules can see about the agency at evaluation time.</summary>
    public sealed class GoalContext
    {
        public Func<string, bool> HasAchievement { get; set; }
        public int MissionsThisYear { get; set; }
        public int DeathsThisYear { get; set; }
        public bool UnderBudget { get; set; }
    }

    public static class GoalEvaluator
    {
        public static bool IsMet(GoalDefinition goal, GoalContext ctx)
        {
            if (goal == null || ctx == null) return false;
            switch (goal.Condition)
            {
                case GoalConditionType.Achievement: return ctx.HasAchievement != null && ctx.HasAchievement(goal.Pattern);
                case GoalConditionType.MissionsThisYear: return ctx.MissionsThisYear >= goal.Threshold;
                case GoalConditionType.NoCrewLoss: return ctx.DeathsThisYear == 0;
                case GoalConditionType.UnderBudget: return ctx.UnderBudget;
                default: return false;
            }
        }
    }

    /// <summary>Picks the year's goals: lowest open tier first, at most one conditional, never a first the agency already has.</summary>
    public static class GoalSelector
    {
        public static List<GoalDefinition> Pick(GoalCatalog catalog, int count, Func<string, bool> hasAchievement,
                                                ICollection<string> previouslyMetIds, ICollection<string> currentlyActiveIds, Random rng)
        {
            return Pick(catalog, count, hasAchievement, previouslyMetIds, currentlyActiveIds, rng, null);
        }

        /// <summary>As above, skipping goals the <paramref name="feasible"/> filter rejects (e.g. missions the tech tree cannot fly yet).</summary>
        public static List<GoalDefinition> Pick(GoalCatalog catalog, int count, Func<string, bool> hasAchievement,
                                                ICollection<string> previouslyMetIds, ICollection<string> currentlyActiveIds, Random rng, Func<GoalDefinition, bool> feasible)
        {
            var picked = new List<GoalDefinition>();
            if (catalog == null || count <= 0) return picked;
            rng = rng ?? new Random();

            var candidates = new List<GoalDefinition>();
            foreach (GoalDefinition g in catalog.Goals)
            {
                if (currentlyActiveIds != null && currentlyActiveIds.Contains(g.Id)) continue;
                bool metBefore = previouslyMetIds != null && previouslyMetIds.Contains(g.Id);
                if (metBefore && !g.Repeatable) continue;
                if (g.Condition == GoalConditionType.Achievement && hasAchievement != null && hasAchievement(g.Pattern)) continue;
                if (feasible != null && !feasible(g)) continue;
                candidates.Add(g);
            }

            // Lowest tier that still has open goals gets priority; conditional goals fill one slot at most.
            candidates.Sort((a, b) =>
            {
                int t = a.Tier.CompareTo(b.Tier);
                return t != 0 ? t : string.CompareOrdinal(a.Id, b.Id);
            });

            bool conditionalTaken = false;
            var byTier = new Dictionary<GoalTier, List<GoalDefinition>>();
            foreach (GoalDefinition g in candidates)
            {
                List<GoalDefinition> list;
                if (!byTier.TryGetValue(g.Tier, out list)) { list = new List<GoalDefinition>(); byTier[g.Tier] = list; }
                list.Add(g);
            }

            foreach (GoalTier tier in new[] { GoalTier.Early, GoalTier.Mid, GoalTier.Late, GoalTier.Program })
            {
                List<GoalDefinition> list;
                if (!byTier.TryGetValue(tier, out list)) continue;
                while (list.Count > 0 && picked.Count < count - (conditionalTaken ? 0 : 1))
                {
                    GoalDefinition g = TakeWeighted(list, rng);
                    picked.Add(g);
                }
                if (picked.Count >= count - 1) break;
            }

            List<GoalDefinition> conditionals;
            if (picked.Count < count && byTier.TryGetValue(GoalTier.Conditional, out conditionals) && conditionals.Count > 0)
            {
                picked.Add(TakeWeighted(conditionals, rng));
                conditionalTaken = true;
            }

            // Still short (e.g. late game)? Fill from any remaining non-conditional tier.
            if (picked.Count < count)
            {
                foreach (GoalTier tier in new[] { GoalTier.Early, GoalTier.Mid, GoalTier.Late, GoalTier.Program })
                {
                    List<GoalDefinition> list;
                    if (!byTier.TryGetValue(tier, out list)) continue;
                    while (list.Count > 0 && picked.Count < count) picked.Add(TakeWeighted(list, rng));
                }
            }
            return picked;
        }

        private static GoalDefinition TakeWeighted(List<GoalDefinition> list, Random rng)
        {
            int total = 0;
            for (int i = 0; i < list.Count; i++) total += list[i].Weight;
            int roll = rng.Next(total);
            for (int i = 0; i < list.Count; i++)
            {
                roll -= list[i].Weight;
                if (roll < 0)
                {
                    GoalDefinition g = list[i];
                    list.RemoveAt(i);
                    return g;
                }
            }
            GoalDefinition last = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return last;
        }
    }
}
