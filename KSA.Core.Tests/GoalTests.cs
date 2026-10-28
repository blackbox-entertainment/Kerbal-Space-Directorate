using System;
using System.Collections.Generic;
using KSA.Core.Goals;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    [TestFixture]
    public class GoalTests
    {
        [Test]
        public void ShippedGoalsConfig_ParsesCleanly()
        {
            var warnings = new List<string>();
            GoalCatalog c = ShippedConfigs.Goals(warnings);
            Assert.That(warnings, Is.Empty, string.Join(" | ", warnings));
            Assert.That(c.Count, Is.EqualTo(32)); // + FoundColony, ColonizeSystem (KSA-64)
            Assert.That(c.Get("AchieveOrbit").Pattern, Is.EqualTo("Orbit:Kerbin"));
            Assert.That(c.Get("AchieveOrbit").Repeatable, Is.False, "firsts are not repeatable by default");
            Assert.That(c.Get("NoCrewLosses").Repeatable, Is.True, "conditional goals repeat by default");
            Assert.That(c.Get("MunLanding").MultiYear, Is.True);
            Assert.That(c.Get("ThreeMissions").Threshold, Is.EqualTo(3));
        }

        [Test]
        public void Evaluator_CoversEveryCondition()
        {
            var ctx = new GoalContext { HasAchievement = p => p == "Orbit:Kerbin", MissionsThisYear = 2, DeathsThisYear = 0, UnderBudget = false };
            Assert.That(GoalEvaluator.IsMet(new GoalDefinition { Condition = GoalConditionType.Achievement, Pattern = "Orbit:Kerbin" }, ctx), Is.True);
            Assert.That(GoalEvaluator.IsMet(new GoalDefinition { Condition = GoalConditionType.Achievement, Pattern = "Land:Mun" }, ctx), Is.False);
            Assert.That(GoalEvaluator.IsMet(new GoalDefinition { Condition = GoalConditionType.MissionsThisYear, Threshold = 2 }, ctx), Is.True);
            Assert.That(GoalEvaluator.IsMet(new GoalDefinition { Condition = GoalConditionType.MissionsThisYear, Threshold = 3 }, ctx), Is.False);
            Assert.That(GoalEvaluator.IsMet(new GoalDefinition { Condition = GoalConditionType.NoCrewLoss }, ctx), Is.True);
            Assert.That(GoalEvaluator.IsMet(new GoalDefinition { Condition = GoalConditionType.UnderBudget }, ctx), Is.False);
        }

        [Test]
        public void Selector_PrefersEarlyTier_SkipsAchieved_OneConditionalMax()
        {
            GoalCatalog c = ShippedConfigs.Goals();
            var rng = new Random(7);
            List<GoalDefinition> picked = GoalSelector.Pick(c, 3, p => p == "Suborbit:Kerbin", new HashSet<string>(), new HashSet<string>(), rng);

            Assert.That(picked.Count, Is.EqualTo(3));
            int conditional = 0;
            foreach (GoalDefinition g in picked)
            {
                Assert.That(g.Id, Is.Not.EqualTo("ReachSpace"), "already achieved");
                if (g.Tier == GoalTier.Conditional) conditional++;
                else Assert.That(g.Tier, Is.EqualTo(GoalTier.Early), "lowest open tier first");
            }
            Assert.That(conditional, Is.EqualTo(1));
        }

        [Test]
        public void Selector_MovesUpTiers_WhenEarlyIsExhausted()
        {
            GoalCatalog c = ShippedConfigs.Goals();
            var met = new HashSet<string> { "ReachSpace", "AchieveOrbit", "FirstEVA", "ReturnSafely", "ScienceReturnKerbin" };
            List<GoalDefinition> picked = GoalSelector.Pick(c, 3, p => false, met, new HashSet<string>(), new Random(1));
            Assert.That(picked.Count, Is.EqualTo(3));
            foreach (GoalDefinition g in picked)
                if (g.Tier != GoalTier.Conditional) Assert.That(g.Tier, Is.EqualTo(GoalTier.Mid));
        }

        [Test]
        public void Selector_NeverRepeatsAnActiveGoal()
        {
            GoalCatalog c = ShippedConfigs.Goals();
            var active = new HashSet<string> { "AchieveOrbit", "FirstEVA", "ReturnSafely", "ReachSpace" };
            List<GoalDefinition> picked = GoalSelector.Pick(c, 2, p => false, new HashSet<string>(), active, new Random(3));
            foreach (GoalDefinition g in picked) Assert.That(active.Contains(g.Id), Is.False);
        }
    }
}
