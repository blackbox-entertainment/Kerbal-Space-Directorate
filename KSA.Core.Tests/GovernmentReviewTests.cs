using KSA.Core.Review;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    [TestFixture]
    public class GovernmentReviewTests
    {
        private static ReviewResult Run(int issued, int met, int deaths = 0, bool probation = false, double prestige = 50)
        {
            return GovernmentReview.Evaluate(1, issued, met, deaths, probation, prestige, 100000, 8, 0.25);
        }

        [TestCase(3, 3, ReviewBand.Excellent, 25)]
        [TestCase(3, 2, ReviewBand.Good, 8)]
        [TestCase(2, 1, ReviewBand.Flat, 0)]
        [TestCase(3, 1, ReviewBand.Lean, -20)]
        [TestCase(3, 0, ReviewBand.Crisis, -40)]
        [TestCase(0, 0, ReviewBand.Excellent, 25)]
        public void Bands_FollowGoalRatio(int issued, int met, ReviewBand band, double pct)
        {
            ReviewResult r = Run(issued, met);
            Assert.That(r.Band, Is.EqualTo(band));
            Assert.That(r.BudgetChangePercent, Is.EqualTo(pct));
        }

        [Test]
        public void Grant_ScalesWithBudgetChangeAndPrestige()
        {
            ReviewResult r = Run(3, 3, prestige: 50);
            // +25%, prestige 60 after → factor 1.05
            Assert.That(r.PrestigeAfter, Is.EqualTo(60));
            Assert.That(r.NextGrant, Is.EqualTo(131250));
            ReviewResult lean = Run(3, 1, prestige: 50);
            Assert.That(lean.PrestigeAfter, Is.EqualTo(42));
            Assert.That(lean.NextGrant, Is.EqualTo(76800));
            Assert.That(lean.HiringFreeze, Is.True);
        }

        [Test]
        public void Deaths_DropOneBand_AndCostPrestige()
        {
            ReviewResult r = Run(3, 3, deaths: 1);
            Assert.That(r.Band, Is.EqualTo(ReviewBand.Good));
            Assert.That(r.PrestigeAfter, Is.EqualTo(55), "+10 for excellent, -5 for the death");
            Assert.That(r.Events, Has.Some.Contains("lost this year"));
            Assert.That(Run(3, 0, deaths: 2).Band, Is.EqualTo(ReviewBand.Crisis), "can't drop below Crisis");
        }

        [Test]
        public void Crisis_StartsProbation_SecondCrisisReplacesDirector()
        {
            ReviewResult first = Run(3, 0);
            Assert.That(first.ProbationAfter, Is.True);
            Assert.That(first.DirectorReplaced, Is.False);
            Assert.That(first.Layoffs, Is.EqualTo(2), "25% of 8 crew");
            Assert.That(first.HiringFreeze, Is.True);

            ReviewResult second = Run(3, 0, probation: true);
            Assert.That(second.DirectorReplaced, Is.True);
            Assert.That(second.ProbationAfter, Is.False);
        }

        [Test]
        public void Probation_LiftedByAnAdequateYear_EndedByALeanOne()
        {
            Assert.That(Run(2, 1, probation: true).ProbationAfter, Is.False);
            Assert.That(Run(2, 1, probation: true).DirectorReplaced, Is.False);
            ReviewResult lean = Run(3, 1, probation: true);
            Assert.That(lean.DirectorReplaced, Is.True);
        }
    }
}
