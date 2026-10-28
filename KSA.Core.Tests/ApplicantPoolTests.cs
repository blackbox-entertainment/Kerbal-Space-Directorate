using System;
using System.Collections.Generic;
using KSA.Core.Personnel;
using KSA.Core.Recruiting;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    [TestFixture]
    public class ApplicantPoolTests
    {
        [Test]
        public void ShippedRecruitingConfig_ParsesCleanly()
        {
            var warnings = new List<string>();
            RecruitCatalog c = ShippedConfigs.Recruiting(warnings);
            Assert.That(warnings, Is.Empty);
            Assert.That(c.Count, Is.EqualTo(5)); // + ColonyBorn (KSA-64)
            Assert.That(c.Get("AirCorps").Roles, Is.EqualTo(new[] { CrewRole.Pilot }));
            Assert.That(c.Get("Industry").Achievements, Is.EqualTo(new[] { "Flight:Kerbin", "Suborbit:Kerbin", "Orbit:Kerbin" }));
            Assert.That(c.Get("WalkIns").WalkInWeight, Is.EqualTo(3));
        }

        [Test]
        public void Generate_MakesUniqueNamedApplicantsWithinSourceRanges()
        {
            RecruitCatalog c = ShippedConfigs.Recruiting();
            var pool = new ApplicantPool();
            var taken = new HashSet<string> { "Applicant 1" };
            List<Applicant> made = pool.Generate(c.Get("University"), 4, 50, 10, new Random(1), new FakeNames(), new[] { "Cautious", "Reckless" }, taken);

            Assert.That(made.Count, Is.EqualTo(4));
            Assert.That(pool.Applicants.Count, Is.EqualTo(4));
            var names = new HashSet<string>();
            foreach (Applicant a in made)
            {
                Assert.That(names.Add(a.Name), Is.True, "names unique");
                Assert.That(a.Name, Is.Not.EqualTo("Applicant 1"), "taken names skipped");
                Assert.That(a.Role, Is.AnyOf(CrewRole.Engineer, CrewRole.Scientist));
                Assert.That(a.Stars, Is.EqualTo(0));
                Assert.That(a.Courage, Is.InRange(0.2f, 0.6f));
                Assert.That(a.Tags.Count, Is.InRange(1, 2));
                Assert.That(a.Revealed, Is.Empty);
                Assert.That(a.HireFee, Is.EqualTo(4000));
            }
            Assert.That(taken.Count, Is.EqualTo(5), "generated names are added to the taken set");
        }

        [Test]
        public void Interview_RevealsInOrder_SeniorPanelRevealsTwo()
        {
            RecruitCatalog c = ShippedConfigs.Recruiting();
            var pool = new ApplicantPool();
            Applicant a = pool.Generate(c.Get("AirCorps"), 1, 50, 0, new Random(2), new FakeNames(), null, null)[0];

            pool.Interview(a.Id, 0);
            Assert.That(a.IsRevealed(Applicant.RevealStars), Is.True);
            Assert.That(a.IsRevealed(Applicant.RevealCourage), Is.False);
            pool.Interview(a.Id, 1);
            Assert.That(a.IsRevealed(Applicant.RevealCourage), Is.True);
            Assert.That(a.IsRevealed(Applicant.RevealStupidity), Is.True);
            Assert.That(a.FullyRevealed, Is.False);
            pool.Interview(a.Id, 0);
            Assert.That(a.FullyRevealed, Is.True);
            Assert.That(a.Interviews, Is.EqualTo(3));
        }

        [Test]
        public void Reject_ThenRefresh_BringsThemBackAYearLater_OneStarWiser()
        {
            RecruitCatalog c = ShippedConfigs.Recruiting();
            var pool = new ApplicantPool();
            Applicant a = pool.Generate(c.Get("University"), 1, 50, 0, new Random(3), new FakeNames(), null, null)[0];
            int stars = a.Stars;

            pool.Reject(a.Id, 10, 426);
            Assert.That(pool.Applicants, Is.Empty);
            Assert.That(pool.Returning.Count, Is.EqualTo(1));
            Assert.That(pool.Refresh(400), Is.EqualTo(0));
            Assert.That(pool.Refresh(436), Is.EqualTo(1));
            Assert.That(pool.Applicants.Count, Is.EqualTo(1));
            Assert.That(pool.Applicants[0].Stars, Is.EqualTo(stars + 1));
            Assert.That(pool.Applicants[0].ReturnDay, Is.EqualTo(-1));
        }

        [Test]
        public void Prestige_BiasesStarsTowardTheTopOfTheRange()
        {
            RecruitCatalog c = ShippedConfigs.Recruiting();
            RecruitSource walkIns = c.Get("WalkIns"); // 0..2 stars
            int highTotal = 0, lowTotal = 0;
            var high = new ApplicantPool();
            var low = new ApplicantPool();
            foreach (Applicant a in high.Generate(walkIns, 200, 100, 0, new Random(5), new FakeNames(), null, null)) highTotal += a.Stars;
            foreach (Applicant a in low.Generate(walkIns, 200, 0, 0, new Random(5), new FakeNames(), null, null)) lowTotal += a.Stars;
            Assert.That(highTotal, Is.GreaterThan(lowTotal));
        }
    }
}
